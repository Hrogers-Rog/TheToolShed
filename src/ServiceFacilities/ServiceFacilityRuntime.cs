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
	/// Data-driven bridge used by both RailLoader-era test packs and FUSE packages.
	/// A pack places normal scenery and declares a small ToolshedServiceFacilities.json file;
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
				EnsureAnimations(definition, existing.gameObject, existingTarget);
				EnsureStorageAnimations(definition, existing.gameObject, existingTarget, existing.linkedIndustry, ResolveLoad(definition.serviceLoadId));
				EnsureParticleEffects(definition, existing.gameObject, existingTarget);
				return;
			}

			GameObject target = FindTarget(definition);
			if (target == null)
			{
				WarnOnce(id + ":target", "waiting for target object '" + definition.TargetDescription + "' from " + definition.sourceFile);
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

			TrackSpan span = ResolveTrackSpan(definition);
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

			PlaceServiceRoot(serviceRoot, definition, span);
			ConfigureRuntimeObject(serviceRoot, target, id, definition, industry, span, load);
			serviceRoot.SetActive(true);

			UniversalServiceFacilityComponent facility = serviceRoot.GetComponent<UniversalServiceFacilityComponent>();
			facility.Configure();
			Applied[id] = facility;
			EnsureAnimations(definition, serviceRoot, target);
			EnsureStorageAnimations(definition, serviceRoot, target, industry, load);
			EnsureParticleEffects(definition, serviceRoot, target);
			Main.Log("[ServiceFacility] attached " + id + " to " + target.name + " load=" + definition.serviceLoadId + ", source=" + (industry != null ? industry.identifier : "infinite"));
		}

		private static void ConfigureRuntimeObject(GameObject serviceRoot, GameObject target, string id, ServiceFacilityDefinition definition, Industry industry, TrackSpan span, Load load)
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
			facility.linkedIndustry = industry;
			facility.requirePlayerOwnedCars = definition.requirePlayerOwnedCars;
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
				ConfigureToggle(serviceRoot, definition, requestKey);
			}
			ConfigureTargetPickable(target, definition, keyValue, requestKey, industry, load);
		}

		private static void ConfigureToggle(GameObject serviceRoot, ServiceFacilityDefinition definition, string requestKey)
		{
			Transform toggleTransform = serviceRoot.transform.Find("Toolshed Service Toggle");
			GameObject toggleObject;
			if (toggleTransform == null)
			{
				toggleObject = new GameObject("Toolshed Service Toggle");
				toggleObject.transform.SetParent(serviceRoot.transform, false);
			}
			else
			{
				toggleObject = toggleTransform.gameObject;
			}

			toggleObject.transform.localPosition = Vector3.zero;
			toggleObject.transform.localRotation = Quaternion.identity;
			toggleObject.transform.localScale = Vector3.one;
			SphereCollider collider = toggleObject.GetComponent<SphereCollider>() ?? toggleObject.AddComponent<SphereCollider>();
			collider.radius = definition.interactionRadius > 0f ? definition.interactionRadius : 3f;
			collider.isTrigger = true;
			toggleObject.layer = ObjectPicker.LayerClickable;

			KeyValuePickableToggle toggle = toggleObject.GetComponent<KeyValuePickableToggle>() ?? toggleObject.AddComponent<KeyValuePickableToggle>();
			toggle.key = requestKey;
			toggle.displayTitle = string.IsNullOrWhiteSpace(definition.requestTitle) ? "Service Loader" : definition.requestTitle;
			toggle.displayMessageTrue = string.IsNullOrWhiteSpace(definition.requestMessageTrue) ? "Click to Stop Loading" : definition.requestMessageTrue;
			toggle.displayMessageFalse = string.IsNullOrWhiteSpace(definition.requestMessageFalse) ? "Click to Start Loading" : definition.requestMessageFalse;
		}

		private static void ConfigureTargetPickable(GameObject target, ServiceFacilityDefinition definition, KeyValueObject keyValue, string requestKey, Industry industry, Load load)
		{
			if (target == null || keyValue == null)
			{
				return;
			}

			ServiceFacilityPickable pickable = target.GetComponent<ServiceFacilityPickable>() ?? target.AddComponent<ServiceFacilityPickable>();
			pickable.keyValueObject = keyValue;
			pickable.requestKey = requestKey;
			pickable.displayTitle = string.IsNullOrWhiteSpace(definition.requestTitle) ? "Service Loader" : definition.requestTitle;
			pickable.displayMessageTrue = string.IsNullOrWhiteSpace(definition.requestMessageTrue) ? "Click to Stop Loading" : definition.requestMessageTrue;
			pickable.displayMessageFalse = string.IsNullOrWhiteSpace(definition.requestMessageFalse) ? "Click to Start Loading" : definition.requestMessageFalse;
			pickable.sourceIndustry = industry;
			pickable.load = load;
			pickable.capacity = Mathf.Max(definition.facilityCapacity, 0f);
			pickable.maxPickDistance = definition.maxPickDistance > 0f ? definition.maxPickDistance : 50f;
			MakeCollidersClickable(target);
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

		private static void PlaceServiceRoot(GameObject serviceRoot, ServiceFacilityDefinition definition, TrackSpan span)
		{
			if (definition.useLoaderWorldPosition)
			{
				serviceRoot.transform.position = definition.loaderWorldPosition;
				serviceRoot.transform.rotation = Quaternion.Euler(definition.loaderWorldRotation);
				return;
			}

			if (definition.loaderAtTrackSpanCenter && span != null)
			{
				serviceRoot.transform.position = span.GetCenterPoint().GameToWorld();
				serviceRoot.transform.rotation = Quaternion.Euler(definition.loaderWorldRotation);
				return;
			}

			serviceRoot.transform.localPosition = definition.loaderLocalPosition;
			serviceRoot.transform.localRotation = Quaternion.Euler(definition.loaderLocalRotation);
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
						string.Equals(item.animationMapKey, animation.animationMapKey, StringComparison.OrdinalIgnoreCase) &&
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
						string.Equals(item.animationMapKey, animation.animationMapKey, StringComparison.OrdinalIgnoreCase));
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
				string id = definition.EffectiveId + ":particle:" + effect.EffectiveName + ":" + boolKey;
				if (AnimationBound.Contains(id))
				{
					continue;
				}

				GameObject driverObject = new GameObject("Toolshed Particle Effect - " + effect.EffectiveName);
				driverObject.SetActive(false);
				driverObject.transform.SetParent(serviceRoot.transform, false);
				ServiceFacilityParticleEffectDriver driver = driverObject.AddComponent<ServiceFacilityParticleEffectDriver>();
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
				driver.streamLocalEnd = effect.streamLocalEnd == Vector3.zero ? new Vector3(0f, -2.25f, 0f) : effect.streamLocalEnd;
				driver.streamLength = effect.streamLength > 0f ? effect.streamLength : 2.25f;
				driver.streamWidth = effect.streamWidth > 0f ? effect.streamWidth : 0.12f;
				driver.streamColor = effect.streamColor.a > 0f ? effect.streamColor : new Color(0.16f, 0.09f, 0.035f, 0.95f);
				driver.debugOriginMarker = effect.debugOriginMarker;
				driver.clearOnStop = effect.clearOnStop;
				driver.debugLogging = definition.debugLogging;
				driverObject.SetActive(true);
				AnimationBound.Add(id);
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

		private static TrackSpan ResolveTrackSpan(ServiceFacilityDefinition definition)
		{
			foreach (string spanId in CandidateStrings(definition.serviceTrackSpanId, definition.serviceTrackSpanIds))
			{
				TrackSpan span = UnityEngine.Object.FindObjectsOfType<TrackSpan>(true)
					.FirstOrDefault(item => string.Equals(item.id, spanId, StringComparison.OrdinalIgnoreCase));
				if (span != null)
				{
					return span;
				}
			}

			return null;
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
		public string serviceLoadId;
		public string sourceIndustryId;
		public string[] sourceIndustryIds;
		public string serviceTrackSpanId;
		public string[] serviceTrackSpanIds;
		public bool infiniteSupply;
		public float facilityCapacity;
		public float loadingRate;
		public float serviceRadius = 0.8f;
		public float maximumSpeedMph = 5f;
		public bool requirePlayerOwnedCars = true;
		public bool debugLogging;
		public bool enableExtendedTenderSearch;
		public float extendedSearchRadius = 12f;
		public float extendedLoadTargetRadius = 8f;
		public bool useLoaderWorldPosition;
		public Vector3 loaderWorldPosition;
		public Vector3 loaderWorldRotation;
		public bool loaderAtTrackSpanCenter;
		public bool createInteractionTrigger;
		public Vector3 loaderLocalPosition;
		public Vector3 loaderLocalRotation;
		public float interactionRadius = 3f;
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
		public Vector3 fallbackInactiveLocalEuler;
		public Vector3 fallbackActiveLocalEuler;
		public float fallbackDurationSeconds;
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
