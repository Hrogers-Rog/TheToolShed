using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AssetPack.Common;
using Helpers;
using KeyValue.Runtime;
using Model;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;
using Newtonsoft.Json;
using RollingStock;
using RollingStock.Controls;
using Track;
using UnityEngine;
using UnityModManagerNet;

namespace Toolshed.ServiceFacilities
{
	/// <summary>
	/// Data-driven bridge used by FUSE packages.
	/// A package places normal scenery and declares a small ToolshedServiceFacilities.json file;
	/// this runtime attaches the vanilla Railroader loader components to the placed scenery.
	/// </summary>
	internal static class ServiceFacilityRuntime
	{
		private const string ConfigFileName = "ToolshedServiceFacilities.json";
		private const string LegacyConfigFileName = "toolshed-service-facilities.json";
		private const float RetryIntervalSeconds = 2f;

		private static readonly List<ServiceFacilityDefinition> Definitions = new List<ServiceFacilityDefinition>();
		private static readonly Dictionary<string, UniversalServiceFacilityComponent> Applied = new Dictionary<string, UniversalServiceFacilityComponent>(StringComparer.OrdinalIgnoreCase);
		private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private static readonly HashSet<string> AnimationBound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private static float _nextRetryTime;
		private static bool _loaded;
		private static bool _loggedScanRoots;

		public static void Initialize()
		{
			Definitions.Clear();
			Applied.Clear();
			Warned.Clear();
			AnimationBound.Clear();
			_loggedScanRoots = false;
			LoadDefinitions();
			_loaded = true;
		}

		public static void Unload()
		{
			Definitions.Clear();
			Applied.Clear();
			Warned.Clear();
			AnimationBound.Clear();
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
				LoadDefinitions();
			}

			for (int i = 0; i < Definitions.Count; i++)
			{
				ApplyOrRefresh(Definitions[i]);
			}
		}

		private static void LoadDefinitions()
		{
			Definitions.Clear();
			List<string> configFiles = FindConfigFiles().ToList();
			if (configFiles.Count == 0)
			{
				WarnOnce("scan:no-configs", "no " + ConfigFileName + " files found yet; scanner will retry.");
				return;
			}

			foreach (string path in configFiles)
			{
				try
				{
					string json = File.ReadAllText(path);
					ServiceFacilityConfigFile file = JsonConvert.DeserializeObject<ServiceFacilityConfigFile>(json);
					if (file == null || file.facilities == null)
					{
						Main.Warn("[ServiceFacility] ignored " + path + ": no facilities array was parsed.");
						continue;
					}

					for (int i = 0; i < file.facilities.Length; i++)
					{
						ServiceFacilityDefinition definition = file.facilities[i];
						if (definition == null)
						{
							continue;
						}
						definition.sourceFile = path;
						Definitions.Add(definition);
					}
					Main.Log("[ServiceFacility] loaded " + file.facilities.Length + " service facility definition(s) from " + path);
				}
				catch (Exception ex)
				{
					Main.Warn("[ServiceFacility] failed to read " + path + ": " + ex.Message);
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
				Main.Log("[ServiceFacility] config scan roots: " + (rootDirectories.Count > 0 ? string.Join(" | ", rootDirectories.ToArray()) : "(none)"));
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

			foreach (string fileName in new[] { ConfigFileName, LegacyConfigFileName })
			{
				string rootConfig = Path.Combine(modPath, fileName);
				if (File.Exists(rootConfig))
				{
					yield return rootConfig;
				}
			}

			string serviceFolder = Path.Combine(modPath, "ServiceFacilities");
			if (Directory.Exists(serviceFolder))
			{
				string[] serviceConfigs = SafeGetFiles(serviceFolder, "*.json", SearchOption.TopDirectoryOnly);
				for (int i = 0; i < serviceConfigs.Length; i++)
				{
					yield return serviceConfigs[i];
				}
			}

			string assetPackFolder = Path.Combine(modPath, "SCAssetPacks");
			if (Directory.Exists(assetPackFolder))
			{
				foreach (string fileName in new[] { ConfigFileName, LegacyConfigFileName })
				{
					string[] assetPackConfigs = SafeGetFiles(assetPackFolder, fileName, SearchOption.AllDirectories);
					for (int i = 0; i < assetPackConfigs.Length; i++)
					{
						yield return assetPackConfigs[i];
					}
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
				Main.Warn("[ServiceFacility] could not scan " + folder + " for " + pattern + ": " + ex.Message);
				return Array.Empty<string>();
			}
		}

		private static void ApplyOrRefresh(ServiceFacilityDefinition definition)
		{
			string id = definition.EffectiveId;
			if (string.IsNullOrWhiteSpace(id))
			{
				WarnOnce(definition.sourceFile + ":missing-id", "definition in " + definition.sourceFile + " has no id or targetObjectName.");
				return;
			}

			UniversalServiceFacilityComponent existing;
			if (Applied.TryGetValue(id, out existing) && existing == null)
			{
				Applied.Remove(id);
				RemoveBindingsFor(id);
			}
			else if (existing != null)
			{
				GameObject existingTarget = existing.transform.parent != null ? existing.transform.parent.gameObject : existing.gameObject;
				Load resolvedLoad = ResolveLoad(definition.serviceLoadId);
				bool resolvedUsesInfiniteSupply = definition.UsesInfiniteSupply;
				Industry resolvedIndustry = resolvedUsesInfiniteSupply ? null : ResolveIndustry(definition);
				TrackSpan[] resolvedSpans = ResolveTrackSpans(definition);
				TrackSpan resolvedSpan = FirstTrackSpan(resolvedSpans);
				if (resolvedLoad != null && (resolvedUsesInfiniteSupply || resolvedIndustry != null))
				{
					if (!PlaceServiceRoot(existing.gameObject, definition, resolvedSpan, existingTarget))
					{
						existing.gameObject.SetActive(false);
						return;
					}
					ConfigureRuntimeObject(existing.gameObject, existingTarget, id, definition, resolvedIndustry, resolvedSpan, resolvedSpans, resolvedLoad);
					if (!existing.gameObject.activeSelf)
					{
						existing.gameObject.SetActive(true);
					}
					existing.Configure();
				}
				EnsureAnimations(definition, existing.gameObject, existingTarget);
				EnsureStorageAnimations(definition, existing.gameObject, existingTarget, existing.linkedIndustry, resolvedLoad);
				EnsureParticleEffects(definition, existing.gameObject, existingTarget);
				return;
			}

			GameObject target = FindTarget(definition);
			if (target == null)
			{
				WarnOnce(id + ":target", "waiting for target object '" + definition.TargetDescription + "' from " + definition.sourceFile);
				return;
			}
			if (TryApplyAuthoredLoadPoints(definition, target))
			{
				return;
			}
			Load load = ResolveLoad(definition.serviceLoadId);
			if (load == null)
			{
				WarnOnce(id + ":load", "waiting for load id '" + definition.serviceLoadId + "' for " + id);
				return;
			}

			bool usesInfiniteSupply = definition.UsesInfiniteSupply;
			Industry industry = usesInfiniteSupply ? null : ResolveIndustry(definition);
			if (!usesInfiniteSupply && industry == null)
			{
				WarnOnce(id + ":industry", "waiting for source industry '" + definition.sourceIndustryId + "' for " + id);
				return;
			}

			TrackSpan[] spans = ResolveTrackSpans(definition);
			TrackSpan span = FirstTrackSpan(spans);
			GameObject serviceRoot = target.transform.Find("Toolshed Service Facility - " + id)?.gameObject;
			if (serviceRoot == null)
			{
				serviceRoot = new GameObject("Toolshed Service Facility - " + id);
				serviceRoot.SetActive(false);
				serviceRoot.transform.SetParent(target.transform, false);
			}
			else
			{
				serviceRoot.SetActive(false);
			}

			if (!PlaceServiceRoot(serviceRoot, definition, span, target))
			{
				return;
			}
			ConfigureRuntimeObject(serviceRoot, target, id, definition, industry, span, spans, load);
			serviceRoot.SetActive(true);

			UniversalServiceFacilityComponent facility = serviceRoot.GetComponent<UniversalServiceFacilityComponent>();
			facility.Configure();
			Applied[id] = facility;
			EnsureAnimations(definition, serviceRoot, target);
			EnsureStorageAnimations(definition, serviceRoot, target, industry, load);
			EnsureParticleEffects(definition, serviceRoot, target);
			Main.Log("[ServiceFacility] attached " + id + " to " + target.name + " load=" + definition.serviceLoadId + ", source=" + (industry != null ? industry.identifier : "infinite"));
		}

		private static bool TryApplyAuthoredLoadPoints(ServiceFacilityDefinition definition, GameObject target)
		{
			if (!definition.useAuthoredLoadPoints || target == null)
			{
				return false;
			}

			ServiceFacilityLoadPointAuthoring[] loadPoints = target.GetComponentsInChildren<ServiceFacilityLoadPointAuthoring>(true);
			if (loadPoints == null || loadPoints.Length == 0)
			{
				if (definition.requireAuthoredLoadPoints)
				{
					WarnOnce(definition.EffectiveId + ":authored-load-points",
						"target '" + definition.TargetDescription + "' has no Toolshed service load point components. Legacy JSON binding is disabled for this entry.");
					return true;
				}
				return false;
			}

			List<ServiceFacilityLoadPointAuthoring> selectedLoadPoints = loadPoints
				.Where(loadPoint => loadPoint != null && MatchesLoadPointFilter(definition, loadPoint))
				.ToList();
			if (selectedLoadPoints.Count == 0)
			{
				WarnOnce(definition.EffectiveId + ":authored-load-point",
					"target '" + definition.TargetDescription + "' has Toolshed service load point components, but none match loadPointId '" +
					definition.LoadPointDescription + "'.");
				return true;
			}

			ServiceFacilityStorageAuthoring[] storages = target.GetComponentsInChildren<ServiceFacilityStorageAuthoring>(true);
			for (int i = 0; i < selectedLoadPoints.Count; i++)
			{
				ServiceFacilityLoadPointAuthoring loadPoint = selectedLoadPoints[i];
				ServiceFacilityStorageAuthoring storage = ResolveStorageAuthoring(storages, definition, loadPoint);
				ServiceFacilityDefinition pointDefinition = BuildDefinitionForAuthoredLoadPoint(definition, loadPoint, storage);
				if (AuthoredLoadPointAlreadyApplied(pointDefinition, loadPoint))
				{
					continue;
				}
				ApplyAuthoredLoadPoint(pointDefinition, target, loadPoint, storage);
			}

			return true;
		}

		private static bool AuthoredLoadPointAlreadyApplied(ServiceFacilityDefinition definition, ServiceFacilityLoadPointAuthoring loadPoint)
		{
			if (definition == null || loadPoint == null)
			{
				return false;
			}
			string id = definition.EffectiveId;
			if (string.IsNullOrWhiteSpace(id))
			{
				return false;
			}

			UniversalServiceFacilityComponent facility;
			if (!Applied.TryGetValue(id, out facility) || facility == null)
			{
				if (Applied.ContainsKey(id))
				{
					Applied.Remove(id);
					RemoveBindingsFor(id);
				}
				return false;
			}

			if (facility.transform.parent != loadPoint.transform)
			{
				return false;
			}

			if (!facility.gameObject.activeSelf)
			{
				facility.gameObject.SetActive(true);
			}
			return true;
		}

		private static void ApplyAuthoredLoadPoint(ServiceFacilityDefinition definition, GameObject target, ServiceFacilityLoadPointAuthoring loadPoint, ServiceFacilityStorageAuthoring storage)
		{
			string id = definition.EffectiveId;
			Load load = ResolveLoad(definition.serviceLoadId);
			if (load == null)
			{
				WarnOnce(id + ":load", "waiting for load id '" + definition.serviceLoadId + "' for " + id);
				return;
			}

			bool usesInfiniteSupply = definition.UsesInfiniteSupply;
			Industry industry = usesInfiniteSupply ? null : ResolveIndustry(definition);
			if (!usesInfiniteSupply && industry == null)
			{
				WarnOnce(id + ":industry", "waiting for source industry '" + definition.sourceIndustryId + "' for " + id);
				return;
			}

			TrackSpan[] spans = ResolveTrackSpans(definition);
			TrackSpan span = FirstTrackSpan(spans);
			GameObject serviceRoot = FindServiceRoot(target, id);
			if (serviceRoot == null)
			{
				serviceRoot = new GameObject("Toolshed Service Facility - " + id);
				serviceRoot.SetActive(false);
			}

			PlaceServiceRootAtAuthoredLoadPoint(serviceRoot, loadPoint);
			ConfigureRuntimeObject(serviceRoot, target, id, definition, industry, span, spans, load);
			KeyValueObject keyValue = serviceRoot.GetComponent<KeyValueObject>();
			ConfigureAuthoredPickable(loadPoint, definition, keyValue, definition.requestLoadingBoolKey, industry, load);
			ConfigureAuthoredStoragePickable(storage, definition, industry, load);
			serviceRoot.SetActive(true);

			UniversalServiceFacilityComponent facility = serviceRoot.GetComponent<UniversalServiceFacilityComponent>();
			facility.Configure();
			SeedInitialStorageIfNeeded(definition, keyValue, industry, load, facility);
			Applied[id] = facility;
			EnsureAnimations(definition, serviceRoot, target);
			EnsureStorageAnimations(definition, serviceRoot, target, industry, load);
			EnsureParticleEffects(definition, serviceRoot, target);
			if (definition.debugLogging)
			{
				Main.Log("[ServiceFacility] attached authored load point " + id +
					" to " + TransformPath(loadPoint.transform) +
					" load=" + definition.serviceLoadId +
					", source=" + (industry != null ? industry.identifier : "infinite"));
			}
		}

		private static GameObject FindServiceRoot(GameObject target, string id)
		{
			if (target == null || string.IsNullOrWhiteSpace(id))
			{
				return null;
			}
			string objectName = "Toolshed Service Facility - " + id;
			Transform direct = target.transform.Find(objectName);
			if (direct != null)
			{
				return direct.gameObject;
			}
			Transform nested = FindChildByName(target.transform, objectName);
			return nested != null ? nested.gameObject : null;
		}

		private static void PlaceServiceRootAtAuthoredLoadPoint(GameObject serviceRoot, ServiceFacilityLoadPointAuthoring loadPoint)
		{
			if (serviceRoot == null || loadPoint == null)
			{
				return;
			}
			serviceRoot.transform.SetParent(loadPoint.transform, false);
			serviceRoot.transform.localPosition = loadPoint.loaderLocalPosition;
			serviceRoot.transform.localRotation = Quaternion.Euler(loadPoint.loaderLocalRotation);
			serviceRoot.transform.localScale = Vector3.one;
		}

		private static void ConfigureAuthoredPickable(ServiceFacilityLoadPointAuthoring loadPoint, ServiceFacilityDefinition definition, KeyValueObject keyValue, string requestKey, Industry industry, Load load)
		{
			if (loadPoint == null || keyValue == null)
			{
				return;
			}

			string colliderDescription;
			ConfigureInteractionCollider(loadPoint.gameObject, definition, out colliderDescription);
			loadPoint.gameObject.layer = ObjectPicker.LayerClickable;
			ServiceFacilityPickable pickable = loadPoint.GetComponent<ServiceFacilityPickable>() ?? loadPoint.gameObject.AddComponent<ServiceFacilityPickable>();
			ConfigurePickable(pickable, definition, keyValue, requestKey, industry, load);
			MakeCollidersClickable(loadPoint.gameObject);
			if (definition.debugLogging)
			{
				Main.Log("[ServiceFacility][Loader] authored interaction for " + definition.EffectiveId +
					" bound to " + TransformPath(loadPoint.transform) + ", " + colliderDescription);
			}
		}

		private static void ConfigureAuthoredStoragePickable(ServiceFacilityStorageAuthoring storage, ServiceFacilityDefinition definition, Industry industry, Load load)
		{
			if (storage == null || industry == null || load == null)
			{
				return;
			}

			if (!storage.showStorageTooltip)
			{
				return;
			}

			string colliderDescription;
			ConfigureCollider(storage.gameObject,
				storage.useBoxInteractionCollider || storage.interactionBoxSize != Vector3.zero,
				storage.interactionBoxCenter,
				storage.interactionBoxSize,
				storage.interactionRadius > 0f ? storage.interactionRadius : 1f,
				out colliderDescription);
			storage.gameObject.layer = ObjectPicker.LayerClickable;

			ServiceFacilityStoragePickable pickable = storage.GetComponent<ServiceFacilityStoragePickable>() ??
				storage.gameObject.AddComponent<ServiceFacilityStoragePickable>();
			pickable.displayTitle = FirstNonEmpty(storage.displayTitle, definition.requestTitle, "Service Storage");
			pickable.sourceIndustry = industry;
			pickable.load = load;
			pickable.capacity = Mathf.Max(definition.facilityCapacity, storage.facilityCapacity, 0f);
			pickable.maxPickDistance = FirstPositive(storage.maxPickDistance, definition.maxPickDistance, 50f);
			MakeCollidersClickable(storage.gameObject);

			if (definition.debugLogging || storage.debugLogging)
			{
				Main.Log("[ServiceFacility][Storage] storage hover for " + definition.EffectiveId +
					" bound to " + TransformPath(storage.transform) + ", " + colliderDescription);
			}
		}

		private static void SeedInitialStorageIfNeeded(ServiceFacilityDefinition definition, KeyValueObject keyValue, Industry industry, Load load, UniversalServiceFacilityComponent facility)
		{
			if (definition == null || keyValue == null || industry == null || load == null || definition.initialStorage <= 0f)
			{
				return;
			}

			string seedKey = "toolshedSeeded." + definition.EffectiveId + "." + load.id;
			if (keyValue[seedKey].BoolValue)
			{
				return;
			}

			float current = industry.Storage.QuantityInStorage(load, null);
			if (current <= load.ZeroThreshold)
			{
				float amount = Mathf.Clamp(definition.initialStorage, 0f, Mathf.Max(definition.facilityCapacity, definition.initialStorage));
				industry.Storage.SetStorage(load, amount, null);
				if (facility != null)
				{
					facility.currentStorage = amount;
				}
				if (definition.debugLogging)
				{
					Main.Log("[ServiceFacility][Storage] seeded " + definition.EffectiveId +
						" load=" + load.id +
						", amount=" + amount.ToString("0.###") +
						", capacity=" + definition.facilityCapacity.ToString("0.###"));
				}
			}
			keyValue[seedKey] = Value.Bool(true);
		}

		private static ServiceFacilityStorageAuthoring ResolveStorageAuthoring(ServiceFacilityStorageAuthoring[] storages, ServiceFacilityDefinition definition, ServiceFacilityLoadPointAuthoring loadPoint)
		{
			if (storages == null || storages.Length == 0 || loadPoint == null)
			{
				return null;
			}

			string facilityId = !string.IsNullOrWhiteSpace(loadPoint.facilityId) ? loadPoint.facilityId : definition.facilityId;
			string storageId = !string.IsNullOrWhiteSpace(loadPoint.storageId) ? loadPoint.storageId : definition.storageId;
			string loadId = !string.IsNullOrWhiteSpace(loadPoint.serviceLoadId) ? loadPoint.serviceLoadId : definition.serviceLoadId;
			for (int i = 0; i < storages.Length; i++)
			{
				ServiceFacilityStorageAuthoring storage = storages[i];
				if (storage != null && storage.Matches(facilityId, storageId, loadId))
				{
					return storage;
				}
			}

			for (int i = 0; i < storages.Length; i++)
			{
				ServiceFacilityStorageAuthoring storage = storages[i];
				if (storage != null && storage.Matches(facilityId, null, loadId))
				{
					return storage;
				}
			}

			ServiceFacilityStorageAuthoring storageByIdAndLoad = SingleStorageMatch(storages, storage =>
				storage != null &&
				StorageIdMatches(storage, storageId) &&
				LoadIdMatches(storage.serviceLoadId, loadId));
			if (storageByIdAndLoad != null)
			{
				return storageByIdAndLoad;
			}

			ServiceFacilityStorageAuthoring storageByLoad = SingleStorageMatch(storages, storage =>
				storage != null && LoadIdMatches(storage.serviceLoadId, loadId));
			if (storageByLoad != null)
			{
				return storageByLoad;
			}

			ServiceFacilityStorageAuthoring storageById = SingleStorageMatch(storages, storage =>
				storage != null && StorageIdMatches(storage, storageId));
			if (storageById != null)
			{
				return storageById;
			}

			return null;
		}

		private static ServiceFacilityStorageAuthoring SingleStorageMatch(ServiceFacilityStorageAuthoring[] storages, Func<ServiceFacilityStorageAuthoring, bool> predicate)
		{
			ServiceFacilityStorageAuthoring match = null;
			if (storages == null || predicate == null)
			{
				return null;
			}
			for (int i = 0; i < storages.Length; i++)
			{
				ServiceFacilityStorageAuthoring storage = storages[i];
				if (storage == null || !predicate(storage))
				{
					continue;
				}
				if (match != null)
				{
					return null;
				}
				match = storage;
			}
			return match;
		}

		private static bool StorageIdMatches(ServiceFacilityStorageAuthoring storage, string requestedStorageId)
		{
			if (storage == null || string.IsNullOrWhiteSpace(requestedStorageId))
			{
				return false;
			}
			string effectiveStorageId = storage.EffectiveStorageId;
			return string.Equals(effectiveStorageId ?? "", requestedStorageId, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(storage.storageId ?? "", requestedStorageId, StringComparison.OrdinalIgnoreCase);
		}

		private static bool LoadIdMatches(string storageLoadId, string requestedLoadId)
		{
			if (string.IsNullOrWhiteSpace(requestedLoadId))
			{
				return false;
			}
			return string.IsNullOrWhiteSpace(storageLoadId) ||
				string.Equals(storageLoadId, requestedLoadId, StringComparison.OrdinalIgnoreCase);
		}

		private static ServiceFacilityDefinition BuildDefinitionForAuthoredLoadPoint(ServiceFacilityDefinition baseDefinition, ServiceFacilityLoadPointAuthoring loadPoint, ServiceFacilityStorageAuthoring storage)
		{
			ServiceFacilityDefinition definition = new ServiceFacilityDefinition();
			definition.id = ShouldAppendLoadPointId(baseDefinition)
				? baseDefinition.EffectiveId + "." + ToIdPart(loadPoint.EffectiveLoadPointId)
				: baseDefinition.EffectiveId;
			definition.targetObjectName = baseDefinition.targetObjectName;
			definition.targetObjectNames = baseDefinition.targetObjectNames;
			definition.modelIdentifier = baseDefinition.modelIdentifier;
			definition.modelIdentifiers = baseDefinition.modelIdentifiers;
			definition.facilityId = !string.IsNullOrWhiteSpace(loadPoint.facilityId) ? loadPoint.facilityId : baseDefinition.facilityId;
			definition.storageId = !string.IsNullOrWhiteSpace(loadPoint.storageId) ? loadPoint.storageId : baseDefinition.storageId;
			definition.loadPointId = loadPoint.EffectiveLoadPointId;
			definition.serviceLoadId = FirstNonEmpty(loadPoint.serviceLoadId, storage != null ? storage.serviceLoadId : null, baseDefinition.serviceLoadId);
			definition.sourceIndustryId = baseDefinition.sourceIndustryId;
			definition.sourceIndustryIds = baseDefinition.sourceIndustryIds;
			definition.serviceTrackSpanId = baseDefinition.serviceTrackSpanId;
			definition.serviceTrackSpanIds = baseDefinition.serviceTrackSpanIds;
			definition.infiniteSupply = storage != null ? storage.infiniteSupply : baseDefinition.infiniteSupply;
			definition.facilityCapacity = FirstPositive(storage != null ? storage.facilityCapacity : 0f, baseDefinition.facilityCapacity, 10000f);
			definition.initialStorage = FirstPositive(storage != null ? storage.initialStorage : 0f, baseDefinition.initialStorage);
			definition.loadingRate = FirstPositive(loadPoint.loadingRate, storage != null ? storage.defaultLoadingRate : 0f, baseDefinition.loadingRate);
			definition.serviceRadius = FirstPositive(loadPoint.serviceRadius, baseDefinition.serviceRadius, 0.65f);
			definition.maximumSpeedMph = FirstPositive(loadPoint.maximumSpeedMph, baseDefinition.maximumSpeedMph, 5f);
			definition.requirePlayerOwnedCars = loadPoint.requirePlayerOwnedCars;
			definition.configureReceivingUnloader = baseDefinition.configureReceivingUnloader || !definition.UsesInfiniteSupply;
			definition.configureInterchangeLoader = baseDefinition.configureInterchangeLoader;
			definition.createMissingIndustryComponents = baseDefinition.createMissingIndustryComponents || !definition.UsesInfiniteSupply;
			definition.canPurchaseThroughInterchange = baseDefinition.canPurchaseThroughInterchange;
			definition.purchaseDelayDays = baseDefinition.purchaseDelayDays;
			definition.carTypeFilterQuery = baseDefinition.carTypeFilterQuery;
			definition.debugLogging = baseDefinition.debugLogging || loadPoint.debugLogging || storage != null && storage.debugLogging;
			definition.enableExtendedTenderSearch = loadPoint.enableExtendedTenderSearch || baseDefinition.enableExtendedTenderSearch;
			definition.extendedSearchRadius = FirstPositive(loadPoint.extendedSearchRadius, baseDefinition.extendedSearchRadius, 8f);
			definition.extendedLoadTargetRadius = FirstPositive(loadPoint.extendedLoadTargetRadius, baseDefinition.extendedLoadTargetRadius, 3f);
			definition.useServiceTargetBox = loadPoint.useServiceTargetBox || baseDefinition.useServiceTargetBox;
			definition.serviceTargetBoxCenter = loadPoint.serviceTargetBoxCenter != Vector3.zero ? loadPoint.serviceTargetBoxCenter : baseDefinition.serviceTargetBoxCenter;
			definition.serviceTargetBoxSize = loadPoint.serviceTargetBoxSize != Vector3.zero ? loadPoint.serviceTargetBoxSize : baseDefinition.serviceTargetBoxSize;
			definition.restrictLoadingToServiceTrackSpan = baseDefinition.restrictLoadingToServiceTrackSpan || loadPoint.restrictLoadingToServiceTrackSpan;
			definition.serviceTrackRouteLimit = FirstPositive(loadPoint.serviceTrackRouteLimit, baseDefinition.serviceTrackRouteLimit, 80f);
			definition.attachTargetPickable = false;
			definition.createInteractionTrigger = false;
			definition.interactionRadius = FirstPositive(loadPoint.interactionRadius, baseDefinition.interactionRadius, 0.45f);
			definition.useBoxInteractionCollider = loadPoint.useBoxInteractionCollider || loadPoint.interactionBoxSize != Vector3.zero;
			definition.interactionBoxCenter = loadPoint.interactionBoxCenter;
			definition.interactionBoxSize = loadPoint.interactionBoxSize;
			definition.requestTitle = FirstNonEmpty(loadPoint.displayTitle, baseDefinition.requestTitle, "Service Loader");
			definition.requestMessageTrue = FirstNonEmpty(loadPoint.messageWhenActive, baseDefinition.requestMessageTrue, "Raise");
			definition.requestMessageFalse = FirstNonEmpty(loadPoint.messageWhenInactive, baseDefinition.requestMessageFalse, "Lower");
			definition.maxPickDistance = FirstPositive(loadPoint.maxPickDistance, baseDefinition.maxPickDistance, 50f);
			definition.requestLoadingBoolKey = FirstNonEmpty(baseDefinition.requestLoadingBoolKey, "request");
			definition.prepareLoadBoolKey = FirstNonEmpty(baseDefinition.prepareLoadBoolKey, "prepareLoad");
			definition.canLoadBoolKey = FirstNonEmpty(baseDefinition.canLoadBoolKey, "canLoad");
			definition.isLoadingBoolKey = FirstNonEmpty(baseDefinition.isLoadingBoolKey, "isLoading");
			definition.animateLoadBoolKey = FirstNonEmpty(baseDefinition.animateLoadBoolKey, "animateLoad");
			definition.requireServiceCondition = loadPoint.requireLoweredBeforeLoading || baseDefinition.requireServiceCondition;
			definition.serviceConditionBoolKey = FirstNonEmpty(baseDefinition.serviceConditionBoolKey, definition.requestLoadingBoolKey);
			definition.serviceConditionExpectedValue = true;
			definition.animations = BuildAuthoredAnimationDefinitions(loadPoint, baseDefinition);
			definition.storageAnimations = BuildAuthoredStorageAnimationDefinitions(storage, definition, baseDefinition.storageAnimations);
			definition.particleEffects = BuildAuthoredParticleEffectDefinitions(loadPoint);
			definition.sourceFile = baseDefinition.sourceFile;
			return definition;
		}

		private static ServiceFacilityStorageAnimationDefinition[] BuildAuthoredStorageAnimationDefinitions(
			ServiceFacilityStorageAuthoring storage,
			ServiceFacilityDefinition definition,
			ServiceFacilityStorageAnimationDefinition[] fallback)
		{
			if (storage == null || !storage.HasStorageAnimation)
			{
				return fallback;
			}

			return new[]
			{
				new ServiceFacilityStorageAnimationDefinition
				{
					animationMapKey = Clean(storage.storageAnimationMapKey),
					loadId = FirstNonEmpty(storage.storageAnimationLoadId, storage.serviceLoadId, definition.serviceLoadId),
					capacity = FirstPositive(storage.storageAnimationCapacity, storage.facilityCapacity, definition.facilityCapacity),
					invert = storage.storageAnimationInvert,
					useTransformFallback = storage.storageAnimationUseTransformFallback,
					fallbackTransformName = Clean(storage.storageAnimationFallbackTransformName),
					emptyLocalY = storage.storageAnimationEmptyLocalY,
					fullLocalY = storage.storageAnimationFullLocalY,
					emptyLocalScaleZ = storage.storageAnimationEmptyLocalScaleZ,
					fullLocalScaleZ = storage.storageAnimationFullLocalScaleZ
				}
			};
		}

		private static bool ShouldAppendLoadPointId(ServiceFacilityDefinition definition)
		{
			if (definition == null)
			{
				return true;
			}

			int filterCount = 0;
			if (!string.IsNullOrWhiteSpace(definition.loadPointId))
			{
				filterCount++;
			}
			if (definition.loadPointIds != null)
			{
				for (int i = 0; i < definition.loadPointIds.Length; i++)
				{
					if (!string.IsNullOrWhiteSpace(definition.loadPointIds[i]))
					{
						filterCount++;
					}
				}
			}
			return filterCount != 1;
		}

		private static ServiceFacilityAnimationDefinition[] BuildAuthoredAnimationDefinitions(ServiceFacilityLoadPointAuthoring loadPoint, ServiceFacilityDefinition baseDefinition)
		{
			if (loadPoint != null && !string.IsNullOrWhiteSpace(loadPoint.animationMapKey))
			{
				return new[]
				{
					new ServiceFacilityAnimationDefinition
					{
						animationMapKey = Clean(loadPoint.animationMapKey),
						boolKey = "request",
						speed = loadPoint.animationSpeed > 0f ? loadPoint.animationSpeed : 1f,
						invert = loadPoint.animationInvert
					}
				};
			}
			return baseDefinition.animations;
		}

		private static ServiceFacilityParticleEffectDefinition[] BuildAuthoredParticleEffectDefinitions(ServiceFacilityLoadPointAuthoring loadPoint)
		{
			if (loadPoint == null || !loadPoint.HasParticleEffect)
			{
				return null;
			}

			Color color = new Color(
				Mathf.Clamp01(loadPoint.effectColorRgb.x),
				Mathf.Clamp01(loadPoint.effectColorRgb.y),
				Mathf.Clamp01(loadPoint.effectColorRgb.z),
				Mathf.Clamp01(loadPoint.effectAlpha));
			return new[]
			{
				new ServiceFacilityParticleEffectDefinition
				{
					effectObjectName = Clean(loadPoint.existingEffectObjectName),
					boolKey = FirstNonEmpty(loadPoint.effectBoolKey, "animateLoad"),
					requiredBoolKey = loadPoint.requireLoweredBeforeLoading ? "request" : null,
					requiredBoolExpectedValue = true,
					createIfMissing = loadPoint.createParticleSystem,
					requireParentTransform = true,
					flowOriginId = Clean(loadPoint.flowOriginId),
					parentTransformName = Clean(loadPoint.flowOriginId),
					localEuler = loadPoint.effectLocalEuler,
					emissionRate = FirstPositive(loadPoint.effectEmissionRate, 80f),
					startLifetime = FirstPositive(loadPoint.effectStartLifetime, 0.35f),
					startSpeed = FirstPositive(loadPoint.effectStartSpeed, 1f),
					streamAnimationSpeed = loadPoint.flowAnimationSpeed,
					startSize = FirstPositive(loadPoint.effectStartSize, 0.045f),
					gravityModifier = loadPoint.effectGravityModifier,
					overrideStartColor = true,
					startColor = color,
					createVisibleStream = loadPoint.createVisibleStream,
					streamUsesWorldDown = loadPoint.streamUsesWorldDown,
					streamLocalStart = loadPoint.streamLocalStart,
					streamLocalEnd = loadPoint.streamLocalEnd,
					streamLength = FirstPositive(loadPoint.streamLength, 1f),
					streamWidth = FirstPositive(loadPoint.streamWidth, 0.06f),
					streamColor = color,
					debugOriginMarker = loadPoint.debugOriginMarker,
					clearOnStop = true
				}
			};
		}

		private static bool MatchesLoadPointFilter(ServiceFacilityDefinition definition, ServiceFacilityLoadPointAuthoring loadPoint)
		{
			if (definition == null || loadPoint == null || !definition.HasLoadPointFilter)
			{
				return true;
			}
			foreach (string candidate in CandidateStrings(definition.loadPointId, definition.loadPointIds))
			{
				if (NamesEqual(candidate, loadPoint.EffectiveLoadPointId) ||
					NamesEqual(candidate, loadPoint.name))
				{
					return true;
				}
			}
			return false;
		}

		private static string FirstNonEmpty(params string[] values)
		{
			if (values == null)
			{
				return "";
			}
			for (int i = 0; i < values.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(values[i]))
				{
					return values[i].Trim();
				}
			}
			return "";
		}

		private static string Clean(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
		}

		private static bool NamesEqual(string left, string right)
		{
			return string.Equals(Clean(left), Clean(right), StringComparison.OrdinalIgnoreCase);
		}

		private static float FirstPositive(params float[] values)
		{
			if (values == null)
			{
				return 0f;
			}
			for (int i = 0; i < values.Length; i++)
			{
				if (values[i] > 0f)
				{
					return values[i];
				}
			}
			return 0f;
		}

		private static string ToIdPart(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return "load-point";
			}
			char[] chars = value.Trim().ToLowerInvariant().ToCharArray();
			for (int i = 0; i < chars.Length; i++)
			{
				char c = chars[i];
				if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
				{
					chars[i] = '-';
				}
			}
			return new string(chars);
		}

		private static void ConfigureRuntimeObject(GameObject serviceRoot, GameObject target, string id, ServiceFacilityDefinition definition, Industry industry, TrackSpan span, TrackSpan[] spans, Load load)
		{
			KeyValueObject keyValue = serviceRoot.GetComponent<KeyValueObject>() ?? serviceRoot.AddComponent<KeyValueObject>();
			GlobalKeyValueObject global = serviceRoot.GetComponent<GlobalKeyValueObject>() ?? serviceRoot.AddComponent<GlobalKeyValueObject>();
			global.globalObjectId = id + ".service-loader";

			CarLoadTargetLoader loader = serviceRoot.GetComponent<CarLoadTargetLoader>() ?? serviceRoot.AddComponent<CarLoadTargetLoader>();
			CarLoaderSequencer sequencer = serviceRoot.GetComponent<CarLoaderSequencer>() ?? serviceRoot.AddComponent<CarLoaderSequencer>();
			UniversalServiceFacilityComponent facility = serviceRoot.GetComponent<UniversalServiceFacilityComponent>() ?? serviceRoot.AddComponent<UniversalServiceFacilityComponent>();
			string canLoadKey = string.IsNullOrWhiteSpace(definition.canLoadBoolKey) ? "canLoad" : definition.canLoadBoolKey;
			string isLoadingKey = string.IsNullOrWhiteSpace(definition.isLoadingBoolKey) ? "isLoading" : definition.isLoadingBoolKey;
			string requestKey = string.IsNullOrWhiteSpace(definition.requestLoadingBoolKey) ? "request" : definition.requestLoadingBoolKey;
			string prepareKey = string.IsNullOrWhiteSpace(definition.prepareLoadBoolKey) ? "prepareLoad" : definition.prepareLoadBoolKey;
			string animateKey = string.IsNullOrWhiteSpace(definition.animateLoadBoolKey) ? "animateLoad" : definition.animateLoadBoolKey;

			facility.serviceLoadId = definition.serviceLoadId;
			facility.infiniteSupply = definition.UsesInfiniteSupply;
			facility.facilityCapacity = Mathf.Max(definition.facilityCapacity, 0f);
			facility.loadingRate = definition.loadingRate > 0f ? definition.loadingRate : loader.outputRate;
			facility.serviceRadius = definition.serviceRadius > 0f ? definition.serviceRadius : 0.8f;
			facility.maximumSpeedMph = definition.maximumSpeedMph > 0f ? definition.maximumSpeedMph : 5f;
			facility.serviceTrackSpan = span;
			facility.serviceTrackSpans = spans ?? Array.Empty<TrackSpan>();
			facility.linkedIndustry = industry;
			facility.requirePlayerOwnedCars = definition.requirePlayerOwnedCars;
			facility.configureReceivingUnloader = definition.configureReceivingUnloader;
			facility.configureInterchangeLoader = definition.configureInterchangeLoader;
			facility.createMissingIndustryComponents = definition.createMissingIndustryComponents;
			facility.canPurchaseThroughInterchange = definition.canPurchaseThroughInterchange;
			facility.purchaseDelayDays = definition.purchaseDelayDays > 0f ? definition.purchaseDelayDays : 0.9583333f;
			facility.carTypeFilterQuery = definition.carTypeFilterQuery ?? "";
			facility.canLoadBoolKey = canLoadKey;
			facility.isLoadingBoolKey = isLoadingKey;
			facility.requestLoadingBoolKey = requestKey;
			facility.prepareLoadBoolKey = prepareKey;
			facility.animateLoadBoolKey = animateKey;
			facility.requireServiceCondition = definition.requireServiceCondition;
			facility.serviceConditionBoolKey = string.IsNullOrWhiteSpace(definition.serviceConditionBoolKey) ? requestKey : definition.serviceConditionBoolKey;
			facility.serviceConditionExpectedValue = definition.serviceConditionExpectedValue;
			facility.serviceLoader = loader;
			facility.serviceSequencer = sequencer;
			facility.keyValueObject = keyValue;
			facility.debugLogging = definition.debugLogging;
			facility.enableExtendedTenderSearch = definition.enableExtendedTenderSearch;
			facility.extendedSearchRadius = definition.extendedSearchRadius > 0f ? definition.extendedSearchRadius : facility.extendedSearchRadius;
			facility.extendedLoadTargetRadius = definition.extendedLoadTargetRadius > 0f ? definition.extendedLoadTargetRadius : facility.extendedLoadTargetRadius;
			facility.useServiceTargetBox = definition.useServiceTargetBox;
			facility.serviceTargetBoxCenter = definition.serviceTargetBoxCenter;
			facility.serviceTargetBoxSize = definition.serviceTargetBoxSize;
			facility.restrictLoadingToServiceTrackSpan = definition.restrictLoadingToServiceTrackSpan;
			facility.serviceTrackRouteLimit = definition.serviceTrackRouteLimit > 0f ? definition.serviceTrackRouteLimit : facility.serviceTrackRouteLimit;

			// These must be wired before the inactive service root is enabled; vanilla CarLoaderSequencer registers
			// its key observer in OnEnable, and late assignment leaves manual request toggles unable to drive prepareLoad.
			loader.keyValueObject = keyValue;
			loader.canLoadBoolKey = canLoadKey;
			loader.isLoadingBoolKey = isLoadingKey;
			sequencer.keyValueObject = keyValue;
			sequencer.readWantsLoadingKey = requestKey;
			sequencer.readIsLoadingKey = isLoadingKey;
			sequencer.writeCanLoadKey = canLoadKey;
			sequencer.writePrepareLoadKey = prepareKey;
			sequencer.writeAnimateLoadKey = animateKey;
			sequencer.logStateChanges = definition.debugLogging;

			if (definition.createInteractionTrigger)
			{
				ConfigureToggle(serviceRoot, target, definition, keyValue, requestKey, industry, load);
			}
			if (definition.attachTargetPickable)
			{
				ConfigureTargetPickable(target, definition, keyValue, requestKey, industry, load);
			}
			else
			{
				RemoveTargetPickable(target);
			}
		}

		private static void ConfigureToggle(GameObject serviceRoot, GameObject target, ServiceFacilityDefinition definition, KeyValueObject keyValue, string requestKey, Industry industry, Load load)
		{
			Transform parent = ResolveInteractionParent(serviceRoot, target, definition);
			if (parent == null)
			{
				return;
			}

			if (parent != serviceRoot.transform)
			{
				RemoveServiceRootToggle(serviceRoot);
			}

			string toggleName = "Toolshed Service Toggle - " + definition.EffectiveId;
			RemoveStaleToggles(target, serviceRoot, parent, toggleName);
			Transform toggleTransform = parent.Find(toggleName);
			if (toggleTransform == null)
			{
				toggleTransform = parent.Find("Toolshed Service Toggle");
			}
			GameObject toggleObject;
			bool created = false;
			bool moved = false;
			if (toggleTransform == null)
			{
				toggleObject = new GameObject(toggleName);
				toggleObject.transform.SetParent(parent, false);
				created = true;
			}
			else
			{
				toggleObject = toggleTransform.gameObject;
				toggleObject.name = toggleName;
				if (toggleObject.transform.parent != parent)
				{
					toggleObject.transform.SetParent(parent, false);
					moved = true;
				}
			}

			toggleObject.transform.localPosition = definition.interactionLocalPosition;
			toggleObject.transform.localRotation = Quaternion.Euler(definition.interactionLocalRotation);
			toggleObject.transform.localScale = Vector3.one;
			string colliderDescription;
			ConfigureInteractionCollider(toggleObject, definition, out colliderDescription);
			toggleObject.layer = ObjectPicker.LayerClickable;

			KeyValuePickableToggle oldToggle = toggleObject.GetComponent<KeyValuePickableToggle>();
			if (oldToggle != null)
			{
				UnityEngine.Object.Destroy(oldToggle);
			}

			ServiceFacilityPickable pickable = toggleObject.GetComponent<ServiceFacilityPickable>() ?? toggleObject.AddComponent<ServiceFacilityPickable>();
			ConfigurePickable(pickable, definition, keyValue, requestKey, industry, load);
			ConfigureInteractionSurfacePickable(parent, target, definition, keyValue, requestKey, industry, load);
			if (definition.debugLogging && (created || moved))
			{
				Main.Log("[ServiceFacility][Loader] interaction trigger " + (created ? "created" : "moved") +
					" for " + definition.EffectiveId + " under " + TransformPath(parent) +
					", " + colliderDescription);
			}
		}

		private static void ConfigureInteractionCollider(GameObject toggleObject, ServiceFacilityDefinition definition, out string description)
		{
			if (toggleObject == null)
			{
				description = "collider=<none>";
				return;
			}

			float radius = definition.interactionRadius > 0f ? definition.interactionRadius : 3f;
			if (definition.UseBoxInteractionCollider)
			{
				ConfigureCollider(toggleObject, true, definition.interactionBoxCenter, definition.interactionBoxSize, radius, out description);
				return;
			}

			ConfigureCollider(toggleObject, false, Vector3.zero, Vector3.zero, radius, out description);
		}

		private static void ConfigureCollider(GameObject target, bool useBox, Vector3 boxCenter, Vector3 boxSize, float radius, out string description)
		{
			if (target == null)
			{
				description = "collider=<none>";
				return;
			}

			if (useBox)
			{
				SphereCollider oldSphere = target.GetComponent<SphereCollider>();
				if (oldSphere != null)
				{
					oldSphere.enabled = false;
					UnityEngine.Object.Destroy(oldSphere);
				}

				BoxCollider box = target.GetComponent<BoxCollider>() ?? target.AddComponent<BoxCollider>();
				box.center = boxCenter;
				box.size = boxSize == Vector3.zero ? new Vector3(radius * 2f, radius * 2f, radius * 2f) : boxSize;
				box.isTrigger = true;
				box.enabled = true;
				description = "boxCenter=" + box.center.ToString("0.###") + ", boxSize=" + box.size.ToString("0.###");
				return;
			}

			BoxCollider oldBox = target.GetComponent<BoxCollider>();
			if (oldBox != null)
			{
				oldBox.enabled = false;
				UnityEngine.Object.Destroy(oldBox);
			}

			SphereCollider sphere = target.GetComponent<SphereCollider>() ?? target.AddComponent<SphereCollider>();
			sphere.radius = radius;
			sphere.isTrigger = true;
			sphere.enabled = true;
			description = "radius=" + sphere.radius.ToString("0.###");
		}

		private static void ConfigureInteractionSurfacePickable(Transform interactionParent, GameObject target, ServiceFacilityDefinition definition, KeyValueObject keyValue, string requestKey, Industry industry, Load load)
		{
			if (!definition.attachInteractionSurfacePickable)
			{
				return;
			}

			Transform surface = ResolveInteractionSurfacePickableParent(interactionParent, target, definition);
			if (surface == null)
			{
				return;
			}

			ServiceFacilityPickable pickable = surface.GetComponent<ServiceFacilityPickable>() ?? surface.gameObject.AddComponent<ServiceFacilityPickable>();
			ConfigurePickable(pickable, definition, keyValue, requestKey, industry, load);
			MakeCollidersClickable(surface.gameObject);
			if (definition.debugLogging)
			{
				Main.Log("[ServiceFacility][Loader] interaction surface pickable for " + definition.EffectiveId +
					" bound to " + TransformPath(surface));
			}
		}

		private static Transform ResolveInteractionSurfacePickableParent(Transform interactionParent, GameObject target, ServiceFacilityDefinition definition)
		{
			if (target != null)
			{
				foreach (string transformName in CandidateStrings(definition.interactionPickableTransformName, definition.interactionPickableTransformNames))
				{
					Transform match = FindChildByName(target.transform, transformName);
					if (match != null)
					{
						return match;
					}
				}
			}

			Transform surface = interactionParent;
			int parentLevels = definition.interactionPickableParentLevels > 0 ? definition.interactionPickableParentLevels : 1;
			for (int i = 0; i < parentLevels && surface != null && surface.parent != null; i++)
			{
				surface = surface.parent;
			}
			return surface;
		}

		private static void ConfigureTargetPickable(GameObject target, ServiceFacilityDefinition definition, KeyValueObject keyValue, string requestKey, Industry industry, Load load)
		{
			if (target == null || keyValue == null)
			{
				return;
			}

			ServiceFacilityPickable pickable = target.GetComponent<ServiceFacilityPickable>() ?? target.AddComponent<ServiceFacilityPickable>();
			ConfigurePickable(pickable, definition, keyValue, requestKey, industry, load);
			MakeCollidersClickable(target);
		}

		private static void ConfigurePickable(ServiceFacilityPickable pickable, ServiceFacilityDefinition definition, KeyValueObject keyValue, string requestKey, Industry industry, Load load)
		{
			if (pickable == null)
			{
				return;
			}

			pickable.keyValueObject = keyValue;
			pickable.requestKey = requestKey;
			pickable.displayTitle = string.IsNullOrWhiteSpace(definition.requestTitle) ? "Service Loader" : definition.requestTitle;
			pickable.displayMessageTrue = string.IsNullOrWhiteSpace(definition.requestMessageTrue) ? "Click to Stop Loading" : definition.requestMessageTrue;
			pickable.displayMessageFalse = string.IsNullOrWhiteSpace(definition.requestMessageFalse) ? "Click to Start Loading" : definition.requestMessageFalse;
			pickable.sourceIndustry = industry;
			pickable.load = load;
			pickable.capacity = Mathf.Max(definition.facilityCapacity, 0f);
			pickable.maxPickDistance = definition.maxPickDistance > 0f ? definition.maxPickDistance : 50f;
		}

		private static void RemoveTargetPickable(GameObject target)
		{
			if (target == null)
			{
				return;
			}

			ServiceFacilityPickable pickable = target.GetComponent<ServiceFacilityPickable>();
			if (pickable != null)
			{
				UnityEngine.Object.Destroy(pickable);
			}
		}

		private static void RemoveStaleToggles(GameObject target, GameObject serviceRoot, Transform desiredParent, string toggleName)
		{
			if (desiredParent == null || string.IsNullOrWhiteSpace(toggleName))
			{
				return;
			}

			HashSet<GameObject> removals = new HashSet<GameObject>();
			CollectStaleToggles(target != null ? target.transform : null, desiredParent, toggleName, removals);
			CollectStaleToggles(serviceRoot != null ? serviceRoot.transform : null, desiredParent, toggleName, removals);
			foreach (GameObject removal in removals)
			{
				if (removal != null)
				{
					UnityEngine.Object.Destroy(removal);
				}
			}
		}

		private static void CollectStaleToggles(Transform root, Transform desiredParent, string toggleName, HashSet<GameObject> removals)
		{
			if (root == null || desiredParent == null || removals == null)
			{
				return;
			}

			Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < transforms.Length; i++)
			{
				Transform candidate = transforms[i];
				if (candidate == null || !string.Equals(candidate.name, toggleName, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				if (candidate.parent == desiredParent)
				{
					continue;
				}

				// Old service configs sometimes left a trigger parented to the scenery root or a previous
				// outlet empty. Remove those so only the current chute owns the hover/click target.
				removals.Add(candidate.gameObject);
			}
		}

		private static Transform ResolveInteractionParent(GameObject serviceRoot, GameObject target, ServiceFacilityDefinition definition)
		{
			if (target != null)
			{
				foreach (string transformName in CandidateStrings(definition.interactionTransformName, definition.interactionTransformNames))
				{
					Transform match = FindChildByName(target.transform, transformName);
					if (match != null)
					{
						return match;
					}
				}
			}

			if (definition.requireInteractionTransform && HasInteractionTransformCandidates(definition))
			{
				WarnOnce(definition.EffectiveId + ":interaction-transform", "waiting for interaction transform '" + definition.InteractionTransformDescription + "' on " + definition.TargetDescription);
				return null;
			}

			return serviceRoot != null ? serviceRoot.transform : null;
		}

		private static string TransformPath(Transform transform)
		{
			if (transform == null)
			{
				return "<none>";
			}

			List<string> names = new List<string>();
			Transform current = transform;
			while (current != null)
			{
				names.Add(current.name);
				current = current.parent;
			}
			names.Reverse();
			return string.Join("/", names.ToArray());
		}

		private static bool HasInteractionTransformCandidates(ServiceFacilityDefinition definition)
		{
			if (!string.IsNullOrWhiteSpace(definition.interactionTransformName))
			{
				return true;
			}
			if (definition.interactionTransformNames == null)
			{
				return false;
			}
			for (int i = 0; i < definition.interactionTransformNames.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(definition.interactionTransformNames[i]))
				{
					return true;
				}
			}
			return false;
		}

		private static Transform FindChildByName(Transform root, string name)
		{
			if (root == null || string.IsNullOrWhiteSpace(name))
			{
				return null;
			}

			Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < transforms.Length; i++)
			{
				Transform transform = transforms[i];
				if (transform != null && NamesEqual(transform.name, name))
				{
					return transform;
				}
			}
			return null;
		}

		private static void RemoveServiceRootToggle(GameObject serviceRoot)
		{
			if (serviceRoot == null)
			{
				return;
			}

			List<GameObject> removals = new List<GameObject>();
			for (int i = 0; i < serviceRoot.transform.childCount; i++)
			{
				Transform child = serviceRoot.transform.GetChild(i);
				if (child != null && child.name.StartsWith("Toolshed Service Toggle", StringComparison.OrdinalIgnoreCase))
				{
					removals.Add(child.gameObject);
				}
			}
			for (int i = 0; i < removals.Count; i++)
			{
				UnityEngine.Object.Destroy(removals[i]);
			}
		}

		private static void MakeCollidersClickable(GameObject target)
		{
			if (target == null)
			{
				return;
			}

			Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
			for (int i = 0; i < colliders.Length; i++)
			{
				Collider collider = colliders[i];
				if (collider != null && collider.gameObject.layer != ObjectPicker.LayerClickable)
				{
					collider.gameObject.layer = ObjectPicker.LayerClickable;
				}
			}
		}

		private static bool PlaceServiceRoot(GameObject serviceRoot, ServiceFacilityDefinition definition, TrackSpan span, GameObject target)
		{
			if (TryPlaceServiceRootAtTransform(serviceRoot, definition, target))
			{
				return true;
			}
			if (definition.requireLoaderTransform && HasLoaderTransformCandidates(definition))
			{
				WarnOnce(definition.EffectiveId + ":loader-transform", "waiting for loader transform '" + definition.LoaderTransformDescription + "' on " + definition.TargetDescription);
				return false;
			}

			if (definition.useLoaderWorldPosition)
			{
				serviceRoot.transform.position = definition.loaderWorldPosition;
				serviceRoot.transform.rotation = Quaternion.Euler(definition.loaderWorldRotation);
				return true;
			}

			if (definition.loaderAtTrackSpanCenter && span != null)
			{
				serviceRoot.transform.position = span.GetCenterPoint().GameToWorld();
				serviceRoot.transform.rotation = Quaternion.Euler(definition.loaderWorldRotation);
				return true;
			}

			serviceRoot.transform.localPosition = definition.loaderLocalPosition;
			serviceRoot.transform.localRotation = Quaternion.Euler(definition.loaderLocalRotation);
			return true;
		}

		private static bool TryPlaceServiceRootAtTransform(GameObject serviceRoot, ServiceFacilityDefinition definition, GameObject target)
		{
			if (serviceRoot == null || target == null || !HasLoaderTransformCandidates(definition))
			{
				return false;
			}

			foreach (string transformName in CandidateStrings(definition.loaderTransformName, definition.loaderTransformNames))
			{
				Transform match = FindChildByName(target.transform, transformName);
				if (match == null)
				{
					continue;
				}

				serviceRoot.transform.position = match.TransformPoint(definition.loaderTransformLocalPosition);
				serviceRoot.transform.rotation = match.rotation * Quaternion.Euler(definition.loaderTransformLocalRotation);
				if (definition.debugLogging)
				{
					Main.Log("[ServiceFacility][Loader] service point for " + definition.EffectiveId +
						" placed at " + TransformPath(match) +
						", localOffset=" + definition.loaderTransformLocalPosition.ToString("0.###"));
				}
				return true;
			}
			return false;
		}

		private static bool HasLoaderTransformCandidates(ServiceFacilityDefinition definition)
		{
			if (!string.IsNullOrWhiteSpace(definition.loaderTransformName))
			{
				return true;
			}
			if (definition.loaderTransformNames == null)
			{
				return false;
			}
			for (int i = 0; i < definition.loaderTransformNames.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(definition.loaderTransformNames[i]))
				{
					return true;
				}
			}
			return false;
		}

		private static void EnsureAnimations(ServiceFacilityDefinition definition, GameObject serviceRoot, GameObject target)
		{
			if (definition.animations == null || definition.animations.Length == 0 || target == null || serviceRoot == null)
			{
				return;
			}

			KeyValueObject keyValue = serviceRoot.GetComponent<KeyValueObject>();
			if (keyValue == null)
			{
				return;
			}

			for (int i = 0; i < definition.animations.Length; i++)
			{
				ServiceFacilityAnimationDefinition animation = definition.animations[i];
				if (animation == null || string.IsNullOrWhiteSpace(animation.animationMapKey))
				{
					continue;
				}

				AnimationMap map = target.GetComponentsInChildren<AnimationMap>(true).FirstOrDefault();
				if (map == null)
				{
					continue;
				}

				string boolKey = string.IsNullOrWhiteSpace(animation.boolKey) ? "prepareLoad" : animation.boolKey;
				ServiceFacilityAnimationDriver driver = serviceRoot.GetComponentsInChildren<ServiceFacilityAnimationDriver>(true)
					.FirstOrDefault(item => item != null &&
					NamesEqual(item.animationMapKey, animation.animationMapKey) &&
						string.Equals(item.boolKey, boolKey, StringComparison.OrdinalIgnoreCase));
				if (driver == null)
				{
					GameObject driverObject = new GameObject("Toolshed Animation - " + animation.animationMapKey);
					driverObject.SetActive(false);
					driverObject.transform.SetParent(serviceRoot.transform, false);
					driver = driverObject.AddComponent<ServiceFacilityAnimationDriver>();
				}
				driver.keyValueObject = keyValue;
				driver.boolKey = boolKey;
				driver.animationMapKey = animation.animationMapKey;
				driver.speed = animation.speed > 0f ? animation.speed : 1f;
				driver.invert = animation.invert;
				driver.debugLogging = definition.debugLogging;
				driver.useTransformFallback = animation.useTransformFallback;
				driver.fallbackTransformName = animation.fallbackTransformName;
				driver.fallbackTransformNames = animation.fallbackTransformNames;
				driver.fallbackTransformOverrides = animation.fallbackTransformOverrides;
				driver.fallbackInactiveLocalEuler = animation.fallbackInactiveLocalEuler;
				driver.fallbackActiveLocalEuler = animation.fallbackActiveLocalEuler;
				driver.fallbackDurationSeconds = animation.fallbackDurationSeconds;
				driver.RefreshBinding(map, target);
				driver.gameObject.SetActive(true);
			}
		}

		private static void EnsureStorageAnimations(ServiceFacilityDefinition definition, GameObject serviceRoot, GameObject target, Industry industry, Load serviceLoad)
		{
			if (definition.storageAnimations == null || definition.storageAnimations.Length == 0 || target == null || serviceRoot == null || industry == null)
			{
				return;
			}

			AnimationMap map = target.GetComponentsInChildren<AnimationMap>(true).FirstOrDefault();
			if (map == null)
			{
				return;
			}

			for (int i = 0; i < definition.storageAnimations.Length; i++)
			{
				ServiceFacilityStorageAnimationDefinition animation = definition.storageAnimations[i];
				if (animation == null || string.IsNullOrWhiteSpace(animation.animationMapKey))
				{
					continue;
				}

				Load load = string.IsNullOrWhiteSpace(animation.loadId) ? serviceLoad : ResolveLoad(animation.loadId);
				if (load == null)
				{
					continue;
				}

				ServiceFacilityStorageAnimationDriver driver = serviceRoot.GetComponentsInChildren<ServiceFacilityStorageAnimationDriver>(true)
					.FirstOrDefault(item => item != null &&
						NamesEqual(item.animationMapKey, animation.animationMapKey));
				if (driver == null)
				{
					GameObject driverObject = new GameObject("Toolshed Storage Animation - " + animation.animationMapKey);
					driverObject.SetActive(false);
					driverObject.transform.SetParent(serviceRoot.transform, false);
					driver = driverObject.AddComponent<ServiceFacilityStorageAnimationDriver>();
				}
				driver.animationMapKey = animation.animationMapKey;
				driver.capacity = animation.capacity > 0f ? animation.capacity : definition.facilityCapacity;
				driver.invert = animation.invert;
				driver.debugLogging = definition.debugLogging;
				driver.useTransformFallback = animation.useTransformFallback;
				driver.fallbackTransformName = animation.fallbackTransformName;
				driver.emptyLocalY = animation.emptyLocalY;
				driver.fullLocalY = animation.fullLocalY;
				driver.emptyLocalScaleZ = animation.emptyLocalScaleZ;
				driver.fullLocalScaleZ = animation.fullLocalScaleZ;
				driver.RefreshBinding(map, target, industry, load);
				driver.gameObject.SetActive(true);
			}
		}

		private static void RemoveBindingsFor(string idPrefix)
		{
			if (string.IsNullOrWhiteSpace(idPrefix) || AnimationBound.Count == 0)
			{
				return;
			}

			string prefix = idPrefix + ":";
			List<string> matches = AnimationBound.Where(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
			for (int i = 0; i < matches.Count; i++)
			{
				AnimationBound.Remove(matches[i]);
			}
		}

		private static void EnsureParticleEffects(ServiceFacilityDefinition definition, GameObject serviceRoot, GameObject target)
		{
			if (definition.particleEffects == null || definition.particleEffects.Length == 0 || target == null || serviceRoot == null)
			{
				return;
			}

			KeyValueObject keyValue = serviceRoot.GetComponent<KeyValueObject>();
			if (keyValue == null)
			{
				return;
			}

			for (int i = 0; i < definition.particleEffects.Length; i++)
			{
				ServiceFacilityParticleEffectDefinition effect = definition.particleEffects[i];
				if (effect == null)
				{
					continue;
				}

				string boolKey = string.IsNullOrWhiteSpace(effect.boolKey) ? "isLoading" : effect.boolKey;
				string driverName = "Toolshed Particle Effect - " + effect.EffectiveName;
				ServiceFacilityParticleEffectDriver driver = serviceRoot.GetComponentsInChildren<ServiceFacilityParticleEffectDriver>(true)
					.FirstOrDefault(item => item != null &&
						string.Equals(item.gameObject.name, driverName, StringComparison.OrdinalIgnoreCase) &&
						string.Equals(item.boolKey, boolKey, StringComparison.OrdinalIgnoreCase));
				if (driver == null)
				{
					GameObject driverObject = new GameObject(driverName);
					driverObject.SetActive(false);
					driverObject.transform.SetParent(serviceRoot.transform, false);
					driver = driverObject.AddComponent<ServiceFacilityParticleEffectDriver>();
				}
				driver.keyValueObject = keyValue;
				driver.boolKey = boolKey;
				driver.sampleRoot = target;
				driver.effectObjectName = effect.effectObjectName;
				driver.effectObjectNames = effect.effectObjectNames;
				driver.invert = effect.invert;
				driver.requiredBoolKey = effect.requiredBoolKey;
				driver.requiredBoolExpectedValue = effect.requiredBoolExpectedValue;
				driver.createIfMissing = effect.createIfMissing;
				driver.requireParentTransform = effect.requireParentTransform;
				driver.flowOriginId = effect.flowOriginId;
				driver.flowOriginFollowTransformName = effect.flowOriginFollowTransformName;
				driver.flowOriginFollowTransformNames = effect.flowOriginFollowTransformNames;
				driver.flowOriginFollowPreserveWorldPosition = effect.flowOriginFollowPreserveWorldPosition;
				driver.parentTransformName = effect.parentTransformName;
				driver.parentTransformNames = effect.parentTransformNames;
				driver.localPosition = effect.localPosition;
				driver.localEuler = effect.localEuler;
				driver.localScale = effect.localScale == Vector3.zero ? Vector3.one : effect.localScale;
				driver.emissionRate = effect.emissionRate > 0f ? effect.emissionRate : 40f;
				driver.startLifetime = effect.startLifetime > 0f ? effect.startLifetime : 0.55f;
				driver.startSpeed = effect.startSpeed > 0f ? effect.startSpeed : 1.25f;
				driver.startSize = effect.startSize > 0f ? effect.startSize : 0.08f;
				driver.gravityModifier = effect.gravityModifier;
				driver.overrideStartColor = effect.overrideStartColor;
				driver.startColor = effect.startColor;
				driver.createVisibleStream = effect.createVisibleStream;
				driver.streamUsesWorldDown = effect.streamUsesWorldDown;
				driver.streamLocalStart = effect.streamLocalStart;
				driver.streamLocalEnd = effect.streamLocalEnd;
				driver.streamLength = effect.streamLength > 0f ? effect.streamLength : 2.25f;
				driver.streamWidth = effect.streamWidth > 0f ? effect.streamWidth : 0.12f;
				driver.streamAnimationSpeed = effect.streamAnimationSpeed;
				driver.streamColor = effect.streamColor.a > 0f ? effect.streamColor : new Color(0.16f, 0.09f, 0.035f, 0.95f);
				driver.debugOriginMarker = effect.debugOriginMarker;
				driver.clearOnStop = effect.clearOnStop;
				driver.debugLogging = definition.debugLogging;
				driver.gameObject.SetActive(true);
				driver.RefreshBinding(target);
			}
		}

		private static GameObject FindTarget(ServiceFacilityDefinition definition)
		{
			foreach (string targetName in CandidateStrings(definition.targetObjectName, definition.targetObjectNames))
			{
				GameObject direct = GameObject.Find(targetName);
				if (direct != null)
				{
					return direct;
				}

				Transform transform = UnityEngine.Object.FindObjectsOfType<Transform>(true)
					.FirstOrDefault(item => string.Equals(item.name, targetName, StringComparison.OrdinalIgnoreCase));
				if (transform != null)
				{
					return transform.gameObject;
				}
			}

			foreach (string modelIdentifier in CandidateStrings(definition.modelIdentifier, definition.modelIdentifiers))
			{
				SceneryAssetInstance instance = UnityEngine.Object.FindObjectsOfType<SceneryAssetInstance>(true)
					.FirstOrDefault(item => string.Equals(item.identifier, modelIdentifier, StringComparison.OrdinalIgnoreCase));
				if (instance != null)
				{
					return instance.gameObject;
				}
			}

			return null;
		}

		private static IEnumerable<string> CandidateStrings(string primary, string[] aliases)
		{
			if (!string.IsNullOrWhiteSpace(primary))
			{
				yield return primary.Trim();
			}
			if (aliases == null)
			{
				yield break;
			}
			for (int i = 0; i < aliases.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(aliases[i]))
				{
					yield return aliases[i].Trim();
				}
			}
		}

		private static Load ResolveLoad(string loadId)
		{
			if (string.IsNullOrWhiteSpace(loadId) || CarPrototypeLibrary.instance == null)
			{
				return null;
			}
			return CarPrototypeLibrary.instance.LoadForId(loadId);
		}

		private static Industry ResolveIndustry(ServiceFacilityDefinition definition)
		{
			foreach (string industryId in CandidateStrings(definition.sourceIndustryId, definition.sourceIndustryIds))
			{
				Industry industry = UnityEngine.Object.FindObjectsOfType<Industry>(true)
					.FirstOrDefault(item => string.Equals(item.identifier, industryId, StringComparison.OrdinalIgnoreCase));
				if (industry != null)
				{
					return industry;
				}
			}

			return null;
		}

		private static TrackSpan[] ResolveTrackSpans(ServiceFacilityDefinition definition)
		{
			List<TrackSpan> spans = new List<TrackSpan>();
			TrackSpan[] allSpans = UnityEngine.Object.FindObjectsOfType<TrackSpan>(true);
			foreach (string spanId in CandidateStrings(definition.serviceTrackSpanId, definition.serviceTrackSpanIds))
			{
				TrackSpan span = allSpans.FirstOrDefault(item => string.Equals(item.id, spanId, StringComparison.OrdinalIgnoreCase));
				if (span != null && !spans.Contains(span))
				{
					spans.Add(span);
				}
			}

			return spans.ToArray();
		}

		private static TrackSpan FirstTrackSpan(TrackSpan[] spans)
		{
			return spans != null && spans.Length > 0 ? spans[0] : null;
		}

		private static void WarnOnce(string key, string message)
		{
			if (Warned.Add(key))
			{
				Main.Warn("[ServiceFacility] " + message);
			}
		}
	}

#pragma warning disable 0649
	[Serializable]
	internal sealed class ServiceFacilityConfigFile
	{
		public ServiceFacilityDefinition[] facilities;
	}

	[Serializable]
	internal sealed class ServiceFacilityDefinition
	{
		public string id;
		public string targetObjectName;
		public string[] targetObjectNames;
		public string modelIdentifier;
		public string[] modelIdentifiers;
		public bool useAuthoredLoadPoints = true;
		public bool requireAuthoredLoadPoints;
		public string facilityId;
		public string storageId;
		public string loadPointId;
		public string[] loadPointIds;
		public string serviceLoadId;
		public string sourceIndustryId;
		public string[] sourceIndustryIds;
		public string serviceTrackSpanId;
		public string[] serviceTrackSpanIds;
		public bool infiniteSupply;
		public float facilityCapacity;
		public float initialStorage;
		public float loadingRate;
		public float serviceRadius = 0.8f;
		public float maximumSpeedMph = 5f;
		public bool requirePlayerOwnedCars = true;
		public bool configureReceivingUnloader;
		public bool configureInterchangeLoader;
		public bool createMissingIndustryComponents;
		public bool canPurchaseThroughInterchange;
		public float purchaseDelayDays;
		public string carTypeFilterQuery;
		public bool debugLogging;
		public bool enableExtendedTenderSearch;
		public float extendedSearchRadius = 12f;
		public float extendedLoadTargetRadius = 8f;
		public bool useServiceTargetBox;
		public Vector3 serviceTargetBoxCenter;
		public Vector3 serviceTargetBoxSize;
		public bool restrictLoadingToServiceTrackSpan;
		public float serviceTrackRouteLimit = 80f;
		public bool useLoaderWorldPosition;
		public Vector3 loaderWorldPosition;
		public Vector3 loaderWorldRotation;
		public bool loaderAtTrackSpanCenter;
		public string loaderTransformName;
		public string[] loaderTransformNames;
		public bool requireLoaderTransform;
		public Vector3 loaderTransformLocalPosition;
		public Vector3 loaderTransformLocalRotation;
		public bool attachTargetPickable = true;
		public bool createInteractionTrigger;
		public string interactionTransformName;
		public string[] interactionTransformNames;
		public bool requireInteractionTransform;
		public bool attachInteractionSurfacePickable = true;
		public string interactionPickableTransformName;
		public string[] interactionPickableTransformNames;
		public int interactionPickableParentLevels = 1;
		public Vector3 loaderLocalPosition;
		public Vector3 loaderLocalRotation;
		public Vector3 interactionLocalPosition;
		public Vector3 interactionLocalRotation;
		public float interactionRadius = 3f;
		public bool useBoxInteractionCollider;
		public Vector3 interactionBoxCenter;
		public Vector3 interactionBoxSize;
		public string requestTitle;
		public string requestMessageTrue;
		public string requestMessageFalse;
		public float maxPickDistance = 50f;
		public string requestLoadingBoolKey = "request";
		public string prepareLoadBoolKey = "prepareLoad";
		public string canLoadBoolKey = "canLoad";
		public string isLoadingBoolKey = "isLoading";
		public string animateLoadBoolKey = "animateLoad";
		public bool requireServiceCondition;
		public string serviceConditionBoolKey = "request";
		public bool serviceConditionExpectedValue = true;
		public ServiceFacilityAnimationDefinition[] animations;
		public ServiceFacilityStorageAnimationDefinition[] storageAnimations;
		public ServiceFacilityParticleEffectDefinition[] particleEffects;
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
				if (!string.IsNullOrWhiteSpace(targetObjectName) && !string.IsNullOrWhiteSpace(serviceLoadId))
				{
					return targetObjectName + "." + serviceLoadId;
				}
				if (targetObjectNames != null)
				{
					for (int i = 0; i < targetObjectNames.Length; i++)
					{
						if (!string.IsNullOrWhiteSpace(targetObjectNames[i]) && !string.IsNullOrWhiteSpace(serviceLoadId))
						{
							return targetObjectNames[i] + "." + serviceLoadId;
						}
					}
				}
				return targetObjectName;
			}
		}

		public string TargetDescription
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(targetObjectName))
				{
					return targetObjectName;
				}
				if (targetObjectNames != null && targetObjectNames.Length > 0)
				{
					return string.Join(", ", targetObjectNames);
				}
				if (!string.IsNullOrWhiteSpace(modelIdentifier))
				{
					return modelIdentifier;
				}
				if (modelIdentifiers != null && modelIdentifiers.Length > 0)
				{
					return string.Join(", ", modelIdentifiers);
				}
				return modelIdentifier;
			}
		}

		public string InteractionTransformDescription
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(interactionTransformName))
				{
					return interactionTransformName;
				}
				if (interactionTransformNames != null && interactionTransformNames.Length > 0)
				{
					return string.Join(", ", interactionTransformNames);
				}
				return interactionTransformName;
			}
		}

		public string LoaderTransformDescription
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(loaderTransformName))
				{
					return loaderTransformName;
				}
				if (loaderTransformNames != null && loaderTransformNames.Length > 0)
				{
					return string.Join(", ", loaderTransformNames);
				}
				return loaderTransformName;
			}
		}

		public string LoadPointDescription
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(loadPointId))
				{
					return loadPointId;
				}
				if (loadPointIds != null && loadPointIds.Length > 0)
				{
					return string.Join(", ", loadPointIds);
				}
				return loadPointId;
			}
		}

		public bool HasLoadPointFilter
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(loadPointId))
				{
					return true;
				}
				if (loadPointIds == null)
				{
					return false;
				}
				for (int i = 0; i < loadPointIds.Length; i++)
				{
					if (!string.IsNullOrWhiteSpace(loadPointIds[i]))
					{
						return true;
					}
				}
				return false;
			}
		}

		public bool UseBoxInteractionCollider
		{
			get { return useBoxInteractionCollider || interactionBoxSize != Vector3.zero; }
		}

		public bool UsesInfiniteSupply
		{
			get
			{
				return infiniteSupply || !HasSourceIndustry;
			}
		}

		private bool HasSourceIndustry
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(sourceIndustryId))
				{
					return true;
				}
				if (sourceIndustryIds == null)
				{
					return false;
				}
				for (int i = 0; i < sourceIndustryIds.Length; i++)
				{
					if (!string.IsNullOrWhiteSpace(sourceIndustryIds[i]))
					{
						return true;
					}
				}
				return false;
			}
		}
	}

	[Serializable]
	internal sealed class ServiceFacilityAnimationDefinition
	{
		public string animationMapKey;
		public string boolKey = "prepareLoad";
		public float speed = 1f;
		public bool invert;
		public bool useTransformFallback;
		public string fallbackTransformName;
		public string[] fallbackTransformNames;
		public ServiceFacilityAnimationFallbackTransformDefinition[] fallbackTransformOverrides;
		public Vector3 fallbackInactiveLocalEuler;
		public Vector3 fallbackActiveLocalEuler;
		public float fallbackDurationSeconds;
	}

	[Serializable]
	internal sealed class ServiceFacilityAnimationFallbackTransformDefinition
	{
		public string transformName;
		public string[] transformNames;
		public Vector3 inactiveLocalEuler;
		public Vector3 activeLocalEuler;

		public bool Matches(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return false;
			}
			if (string.Equals(transformName, name, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (transformNames == null)
			{
				return false;
			}
			for (int i = 0; i < transformNames.Length; i++)
			{
				if (string.Equals(transformNames[i], name, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}
	}

	[Serializable]
	internal sealed class ServiceFacilityStorageAnimationDefinition
	{
		public string animationMapKey;
		public string loadId;
		public float capacity;
		public bool invert;
		public bool useTransformFallback;
		public string fallbackTransformName;
		public float emptyLocalY;
		public float fullLocalY;
		public float emptyLocalScaleZ = 1f;
		public float fullLocalScaleZ = 1f;
	}

	[Serializable]
	internal sealed class ServiceFacilityParticleEffectDefinition
	{
		public string effectObjectName;
		public string[] effectObjectNames;
		public string boolKey = "isLoading";
		public bool invert;
		public string requiredBoolKey;
		public bool requiredBoolExpectedValue = true;
		public bool createIfMissing;
		public bool requireParentTransform;
		public string flowOriginId;
		public string flowOriginFollowTransformName;
		public string[] flowOriginFollowTransformNames;
		public bool flowOriginFollowPreserveWorldPosition = true;
		public string parentTransformName;
		public string[] parentTransformNames;
		public Vector3 localPosition;
		public Vector3 localEuler;
		public Vector3 localScale = Vector3.one;
		public float emissionRate = 40f;
		public float startLifetime = 0.55f;
		public float startSpeed = 1.25f;
		public float startSize = 0.08f;
		public float gravityModifier = 1f;
		public bool overrideStartColor;
		public Color startColor = new Color(0.08f, 0.06f, 0.04f, 0.95f);
		public bool createVisibleStream;
		public bool streamUsesWorldDown = true;
		public Vector3 streamLocalStart;
		public Vector3 streamLocalEnd = new Vector3(0f, -2.25f, 0f);
		public float streamLength = 2.25f;
		public float streamWidth = 0.12f;
		public float streamAnimationSpeed;
		public Color streamColor = new Color(0.16f, 0.09f, 0.035f, 0.95f);
		public bool debugOriginMarker;
		public bool clearOnStop = true;

		public string EffectiveName
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(effectObjectName))
				{
					return effectObjectName;
				}
				if (!string.IsNullOrWhiteSpace(parentTransformName))
				{
					return parentTransformName;
				}
				return createIfMissing ? "runtime" : "effect";
			}
		}
	}
#pragma warning restore 0649
}
