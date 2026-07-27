using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using Newtonsoft.Json;
using Track;
using UnityEngine;
using UnityModManagerNet;

namespace Toolshed.SelectiveInterchanges
{
	/// <summary>
	/// Data-driven bridge for route packages that want selective interchange behavior
	/// without hand-building runtime GameObjects.
	/// </summary>
	internal static class SelectiveInterchangeRuntime
	{
		private const string ConfigFileName = "ToolshedSelectiveInterchanges.json";
		private const float RetryIntervalSeconds = 2f;

		private static readonly List<SelectiveInterchangeDefinition> Definitions = new List<SelectiveInterchangeDefinition>();
		private static readonly Dictionary<string, SelectiveInterchangeComponent> Applied = new Dictionary<string, SelectiveInterchangeComponent>(StringComparer.OrdinalIgnoreCase);
		private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private static readonly MethodInfo OpsRebuildCollectionsMethod = AccessTools.Method(typeof(OpsController), "RebuildCollections");
		private static readonly MethodInfo CarUpdateWaybillMethod = AccessTools.Method(typeof(Car), "UpdateWaybill");

		private static bool _loaded;
		private static bool _loggedScanRoots;
		private static float _nextRetryTime;

		public static void Initialize()
		{
			Definitions.Clear();
			Applied.Clear();
			Warned.Clear();
			SelectiveInterchangeCarModelRegistry.Clear();
			_loggedScanRoots = false;
			LoadDefinitions();
			_loaded = true;
		}

		public static void Unload()
		{
			Definitions.Clear();
			Applied.Clear();
			Warned.Clear();
			SelectiveInterchangeCarModelRegistry.Clear();
			_loggedScanRoots = false;
			_loaded = false;
		}

		public static void Update()
		{
			if (!Main.Enabled)
			{
				return;
			}
			if (!_loaded)
			{
				Initialize();
			}
			if (Time.unscaledTime < _nextRetryTime)
			{
				return;
			}

			_nextRetryTime = Time.unscaledTime + RetryIntervalSeconds;
			if (Definitions.Count == 0)
			{
				// Mod configuration files are static for the lifetime of a
				// Railroader process. Initialize already scanned every mod root;
				// repeating that disk walk every two seconds when no files exist
				// creates permanent frame-time spikes for no useful work.
				return;
			}

			for (int i = 0; i < Definitions.Count; i++)
			{
				ApplyOrRefresh(Definitions[i]);
			}
			RepairCarWaybillCaches();
		}

		/// <summary>
		/// Car.Waybill is a cache that goes null (and stays null) when a waybill referencing
		/// one of our components parses before the component is registered with OpsController —
		/// which happens every save load, because cars initialize before this runtime applies
		/// definitions. Re-parse those caches once our identifiers resolve so arrival payments
		/// and waybill tags survive a reload.
		/// </summary>
		private static void RepairCarWaybillCaches()
		{
			if (CarUpdateWaybillMethod == null || Applied.Count == 0)
			{
				return;
			}
			TrainController trainController = TrainController.Shared;
			OpsController opsController = OpsController.Shared;
			if (trainController == null || opsController == null)
			{
				return;
			}

			foreach (Car car in trainController.Cars)
			{
				if (car == null || car.Waybill != null || car.KeyValueObject == null)
				{
					continue;
				}
				KeyValue.Runtime.Value raw = car.KeyValueObject[Car.KeyOpsWaybill];
				if (raw.Type != KeyValue.Runtime.ValueType.Dictionary)
				{
					continue;
				}
				try
				{
					if (Waybill.FromPropertyValue(raw, opsController) == null)
					{
						continue;
					}
				}
				catch (Exception)
				{
					// Destination or origin still unresolvable; retry on a later pass.
					continue;
				}
				try
				{
					CarUpdateWaybillMethod.Invoke(car, new object[] { raw });
					Main.Log("[SelectiveInterchange] restored cached waybill for " + car.DisplayName + ".");
				}
				catch (Exception ex)
				{
					Main.Warn("[SelectiveInterchange] could not restore waybill cache for " + car.DisplayName + ": " + ex.Message);
				}
			}
		}

		/// <summary>
		/// OpsCarPosition.Equals compares span arrays by reference, so reassigning
		/// trackSpans invalidates every waybill destination already cached on a car.
		/// Reuse the existing array whenever the resolved spans are unchanged.
		/// </summary>
		internal static bool SpanArraysMatch(TrackSpan[] current, TrackSpan[] resolved)
		{
			if (ReferenceEquals(current, resolved))
			{
				return true;
			}
			if (current == null || resolved == null || current.Length != resolved.Length)
			{
				return false;
			}
			for (int i = 0; i < current.Length; i++)
			{
				if (!ReferenceEquals(current[i], resolved[i]))
				{
					return false;
				}
			}
			return true;
		}

		internal static IEnumerable<string> CandidateStrings(string primary, string[] aliases)
		{
			if (!string.IsNullOrWhiteSpace(primary))
			{
				yield return primary;
			}
			if (aliases == null)
			{
				yield break;
			}
			for (int i = 0; i < aliases.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(aliases[i]))
				{
					yield return aliases[i];
				}
			}
		}

		internal static Load ResolveLoad(string loadId)
		{
			if (string.IsNullOrWhiteSpace(loadId) || CarPrototypeLibrary.instance == null)
			{
				return null;
			}
			return CarPrototypeLibrary.instance.LoadForId(loadId);
		}

		internal static Industry ResolveIndustry(string industryId)
		{
			if (string.IsNullOrWhiteSpace(industryId))
			{
				return null;
			}
			return UnityEngine.Object.FindObjectsOfType<Industry>(true)
				.FirstOrDefault(item => item != null && string.Equals(item.identifier, industryId, StringComparison.OrdinalIgnoreCase));
		}

		internal static Interchange ResolveInterchange(string componentIdentifier)
		{
			if (string.IsNullOrWhiteSpace(componentIdentifier))
			{
				return null;
			}
			return UnityEngine.Object.FindObjectsOfType<Interchange>(true)
				.FirstOrDefault(item => item != null && string.Equals(item.Identifier, componentIdentifier, StringComparison.OrdinalIgnoreCase));
		}

		internal static bool ShouldExcludeFromGenericInterchangeSelection(Interchange interchange)
		{
			if (interchange == null || interchange.Industry == null)
			{
				return false;
			}
			SelectiveInterchangeComponent[] components = interchange.Industry.GetComponentsInChildren<SelectiveInterchangeComponent>(true);
			for (int i = 0; i < components.Length; i++)
			{
				SelectiveInterchangeComponent component = components[i];
				if (component != null && component.excludeFromGenericInterchangeSelection && !component.ProgressionDisabled)
				{
					return true;
				}
			}
			return false;
		}

		internal static IndustryComponent ResolveIndustryComponent(string componentIdentifier)
		{
			if (string.IsNullOrWhiteSpace(componentIdentifier))
			{
				return null;
			}
			return UnityEngine.Object.FindObjectsOfType<IndustryComponent>(true)
				.FirstOrDefault(item => item != null && string.Equals(item.Identifier, componentIdentifier, StringComparison.OrdinalIgnoreCase));
		}

		internal static TrackSpan[] ResolveTrackSpans(string[] spanIds)
		{
			if (spanIds == null || spanIds.Length == 0)
			{
				return Array.Empty<TrackSpan>();
			}

			List<TrackSpan> spans = new List<TrackSpan>();
			TrackSpan[] allSpans = UnityEngine.Object.FindObjectsOfType<TrackSpan>(true);
			for (int i = 0; i < spanIds.Length; i++)
			{
				string spanId = spanIds[i];
				if (string.IsNullOrWhiteSpace(spanId))
				{
					continue;
				}
				TrackSpan span = allSpans.FirstOrDefault(item => item != null && string.Equals(item.id, spanId, StringComparison.OrdinalIgnoreCase));
				if (span != null)
				{
					spans.Add(span);
				}
			}
			return spans.ToArray();
		}

		private static void LoadDefinitions()
		{
			Definitions.Clear();
			List<string> configFiles = FindConfigFiles().ToList();
			if (configFiles.Count == 0)
			{
				WarnOnce("scan:no-configs", "no " + ConfigFileName + " files found; scan complete for this session.");
				return;
			}

			foreach (string path in configFiles)
			{
				try
				{
					string json = File.ReadAllText(path);
					SelectiveInterchangeConfigFile file = JsonConvert.DeserializeObject<SelectiveInterchangeConfigFile>(json);
					if (file == null || file.interchanges == null)
					{
						Main.Warn("[SelectiveInterchange] ignored " + path + ": no interchanges array was parsed.");
						continue;
					}

					for (int i = 0; i < file.interchanges.Length; i++)
					{
						SelectiveInterchangeDefinition definition = file.interchanges[i];
						if (definition == null)
						{
							continue;
						}
						definition.sourceFile = path;
						Definitions.Add(definition);
					}
					Main.Log("[SelectiveInterchange] loaded " + file.interchanges.Length + " selective interchange definition(s) from " + path);
				}
				catch (Exception ex)
				{
					Main.Warn("[SelectiveInterchange] failed to read " + path + ": " + ex.Message);
				}
			}
		}

		private static IEnumerable<string> FindConfigFiles()
		{
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			List<string> rootDirectories = FindScanRoots().ToList();
			if (!_loggedScanRoots)
			{
				_loggedScanRoots = true;
				Main.Log("[SelectiveInterchange] config scan roots: " + (rootDirectories.Count > 0 ? string.Join(" | ", rootDirectories.ToArray()) : "(none)"));
			}

			for (int i = 0; i < rootDirectories.Count; i++)
			{
				foreach (string path in EnumerateModConfigFiles(rootDirectories[i]))
				{
					if (seen.Add(path))
					{
						yield return path;
					}
				}
			}
		}

		private static IEnumerable<string> FindScanRoots()
		{
			HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string directory in FindModsDirectories())
			{
				if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
				{
					continue;
				}

				foreach (string modDirectory in Directory.GetDirectories(directory))
				{
					if (roots.Add(modDirectory))
					{
						yield return modDirectory;
					}
				}
			}

			string ownModDirectory = ResolveOwnModDirectory();
			if (!string.IsNullOrWhiteSpace(ownModDirectory) && roots.Add(ownModDirectory))
			{
				yield return ownModDirectory;
			}
		}

		private static IEnumerable<string> FindModsDirectories()
		{
			HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (!string.IsNullOrWhiteSpace(UnityModManager.modsPath))
			{
				directories.Add(UnityModManager.modsPath);
			}

			string ownModDirectory = ResolveOwnModDirectory();
			if (!string.IsNullOrWhiteSpace(ownModDirectory))
			{
				DirectoryInfo parent = Directory.GetParent(ownModDirectory);
				if (parent != null)
				{
					directories.Add(parent.FullName);
				}
			}

			string currentDirectory = Environment.CurrentDirectory;
			if (!string.IsNullOrWhiteSpace(currentDirectory))
			{
				directories.Add(Path.Combine(currentDirectory, "Mods"));
			}

			foreach (string directory in directories)
			{
				yield return directory;
			}
		}

		private static string ResolveOwnModDirectory()
		{
			if (Main.ModEntry == null || string.IsNullOrWhiteSpace(Main.ModEntry.Path))
			{
				return ResolveAssemblyDirectory();
			}
			if (Directory.Exists(Main.ModEntry.Path))
			{
				return Main.ModEntry.Path;
			}
			string directory = Path.GetDirectoryName(Main.ModEntry.Path);
			if (Directory.Exists(directory))
			{
				return directory;
			}
			return ResolveAssemblyDirectory();
		}

		private static string ResolveAssemblyDirectory()
		{
			string location = Assembly.GetExecutingAssembly().Location;
			if (string.IsNullOrWhiteSpace(location))
			{
				return null;
			}
			string directory = Path.GetDirectoryName(location);
			return Directory.Exists(directory) ? directory : null;
		}

		private static IEnumerable<string> EnumerateModConfigFiles(string modPath)
		{
			if (string.IsNullOrWhiteSpace(modPath) || !Directory.Exists(modPath))
			{
				yield break;
			}

			string rootConfig = Path.Combine(modPath, ConfigFileName);
			if (File.Exists(rootConfig))
			{
				yield return rootConfig;
			}

			string folder = Path.Combine(modPath, "SelectiveInterchanges");
			if (Directory.Exists(folder))
			{
				string[] configs = SafeGetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
				for (int i = 0; i < configs.Length; i++)
				{
					yield return configs[i];
				}
			}
		}

		private static string[] SafeGetFiles(string folder, string pattern, SearchOption searchOption)
		{
			try
			{
				return Directory.GetFiles(folder, pattern, searchOption);
			}
			catch (Exception ex)
			{
				Main.Warn("[SelectiveInterchange] could not scan " + folder + " for " + pattern + ": " + ex.Message);
				return Array.Empty<string>();
			}
		}

		private static void ApplyOrRefresh(SelectiveInterchangeDefinition definition)
		{
			string id = definition.EffectiveId;
			if (string.IsNullOrWhiteSpace(id))
			{
				WarnOnce(definition.sourceFile + ":missing-id", "definition in " + definition.sourceFile + " has no id or interchangeIndustryId.");
				return;
			}

			Industry industry = ResolveDefinitionIndustry(definition);
			if (industry == null)
			{
				WarnOnce(id + ":industry", "waiting for interchange industry '" + definition.InterchangeDescription + "' from " + definition.sourceFile);
				return;
			}

			Interchange interchange = industry.GetComponentInChildren<Interchange>(true);
			if (interchange == null)
			{
				WarnOnce(id + ":interchange", "waiting for Interchange component under industry '" + industry.identifier + "'.");
				return;
			}

			SelectiveInterchangeComponent component;
			if (Applied.TryGetValue(id, out component) && component == null)
			{
				Applied.Remove(id);
			}

			bool dirty = false;
			string subId = string.IsNullOrWhiteSpace(definition.componentId) ? "toolshed-selective-" + id : definition.componentId;
			if (component == null)
			{
				Transform child = industry.transform.Find("Toolshed Selective Interchange - " + id);
				GameObject childObject = child != null ? child.gameObject : new GameObject("Toolshed Selective Interchange - " + id);
				if (child == null)
				{
					childObject.SetActive(false);
					childObject.transform.SetParent(industry.transform, false);
				}
				component = childObject.GetComponent<SelectiveInterchangeComponent>() ?? childObject.AddComponent<SelectiveInterchangeComponent>();
				// Identifier caches "industry.subIdentifier" on first access; make sure the
				// component never becomes visible to the game with a null subIdentifier.
				component.subIdentifier = subId;
				childObject.SetActive(true);
				Applied[id] = component;
				dirty = true;
			}

			TrackSpan[] spans = ResolveTrackSpans(definition.trackSpanIds);
			if (!string.Equals(component.subIdentifier, subId, StringComparison.Ordinal))
			{
				component.subIdentifier = subId;
				dirty = true;
			}
			component.name = string.IsNullOrWhiteSpace(definition.displayName) ? "Selective Interchange" : definition.displayName;
			component.displayTitle = component.name;
			component.configurePurchases = definition.configurePurchases;
			component.disableUnlistedPurchaseLoaders = definition.disableUnlistedPurchaseLoaders;
			component.enableOutboundTraffic = definition.enableOutboundTraffic;
			component.enableProductionAccounting = definition.enableProductionAccounting;
			component.initialCounters = definition.initialCounters ?? Array.Empty<SelectiveInterchangeInitialCounter>();
			component.requireEnabledInterchange = definition.requireEnabledInterchange;
			component.excludeFromGenericInterchangeSelection = definition.excludeFromGenericInterchangeSelection;
			component.purchaseLoads = definition.purchaseLoads ?? Array.Empty<SelectiveInterchangeLoadRule>();
			component.inboundLoads = definition.inboundLoads ?? Array.Empty<SelectiveInterchangeInboundLoadRule>();
			component.emptyCarRequests = definition.emptyCarRequests ?? Array.Empty<SelectiveInterchangeEmptyCarRequest>();
			component.outboundRoutes = definition.outboundRoutes ?? Array.Empty<SelectiveInterchangeOutboundRoute>();
			TrackSpan[] resolvedSpans = spans.Length > 0 ? spans : interchange.trackSpans;
			if (!SpanArraysMatch(component.trackSpans, resolvedSpans))
			{
				// Keep the existing array instance whenever possible: waybill destinations cached
				// on cars hold this array by reference, and payment matching breaks if it churns.
				component.trackSpans = resolvedSpans;
				dirty = true;
			}
			component.carTypeFilter = new CarTypeFilter(string.IsNullOrWhiteSpace(definition.carTypeFilterQuery) ? "*" : definition.carTypeFilterQuery);
			component.sharedStorage = true;
			component.debugLogging = definition.debugLogging;
			component.Configure();
			if (dirty)
			{
				RebuildOpsCollections();
			}
		}

		private static Industry ResolveDefinitionIndustry(SelectiveInterchangeDefinition definition)
		{
			foreach (string industryId in CandidateStrings(definition.interchangeIndustryId, definition.interchangeIndustryIds))
			{
				Industry industry = ResolveIndustry(industryId);
				if (industry != null)
				{
					return industry;
				}
			}
			return null;
		}

		private static void RebuildOpsCollections()
		{
			if (OpsController.Shared == null || OpsRebuildCollectionsMethod == null)
			{
				return;
			}
			try
			{
				OpsRebuildCollectionsMethod.Invoke(OpsController.Shared, null);
			}
			catch (Exception ex)
			{
				Main.Warn("[SelectiveInterchange] could not rebuild ops collections: " + ex.Message);
			}
		}

		private static void WarnOnce(string key, string message)
		{
			if (Warned.Add(key))
			{
				Main.Warn("[SelectiveInterchange] " + message);
			}
		}
	}

#pragma warning disable 0649
	[Serializable]
	internal sealed class SelectiveInterchangeConfigFile
	{
		public SelectiveInterchangeDefinition[] interchanges;
	}

	[Serializable]
	internal sealed class SelectiveInterchangeDefinition
	{
		public string id;
		public string componentId;
		public string displayName;
		public string interchangeIndustryId;
		public string[] interchangeIndustryIds;
		public string[] trackSpanIds;
		public string carTypeFilterQuery = "*";
		public bool configurePurchases = true;
		public bool disableUnlistedPurchaseLoaders = true;
		public bool enableOutboundTraffic;
		public bool enableProductionAccounting;
		public SelectiveInterchangeInitialCounter[] initialCounters;
		public bool requireEnabledInterchange = true;
		public bool excludeFromGenericInterchangeSelection = true;
		public SelectiveInterchangeLoadRule[] purchaseLoads;
		public SelectiveInterchangeInboundLoadRule[] inboundLoads;
		public SelectiveInterchangeEmptyCarRequest[] emptyCarRequests;
		public SelectiveInterchangeOutboundRoute[] outboundRoutes;
		public bool debugLogging;
		[NonSerialized]
		public string sourceFile;

		public string EffectiveId
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(id))
				{
					return id;
				}
				if (!string.IsNullOrWhiteSpace(interchangeIndustryId))
				{
					return interchangeIndustryId;
				}
				if (interchangeIndustryIds != null)
				{
					for (int i = 0; i < interchangeIndustryIds.Length; i++)
					{
						if (!string.IsNullOrWhiteSpace(interchangeIndustryIds[i]))
						{
							return interchangeIndustryIds[i];
						}
					}
				}
				return null;
			}
		}

		public string InterchangeDescription
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(interchangeIndustryId))
				{
					return interchangeIndustryId;
				}
				return interchangeIndustryIds != null && interchangeIndustryIds.Length > 0
					? string.Join(", ", interchangeIndustryIds)
					: string.Empty;
			}
		}
	}
#pragma warning restore 0649
}
