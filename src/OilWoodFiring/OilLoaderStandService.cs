using System;
using System.Collections.Generic;
using System.Reflection;
using Game.AccessControl;
using Game.State;
using HarmonyLib;
using KeyValue.Runtime;
using Model.Ops;
using RollingStock;
using RollingStock.Controls;
using RollingStock.ContinuousControls;
using UI.Map;
using UnityEngine;

namespace Toolshed.OilWoodFiring
{
	internal static class OilLoaderStandService
	{
		private const float RetryIntervalSeconds = 2f;
		private const float MaxRetryIntervalSeconds = 30f;

		private const float CloneOffsetRight = 6.5f;

		private const float CloneOffsetForward = 0.5f;

		private const string BunkerCStandpipeTitle = "Water / Bunker-C Standpipe";

		private const string RotateTooltipText = "Rotate";

		private static readonly Color OilVisualColor = new Color(0.08f, 0.06f, 0.04f, 0.95f);

		private static readonly FieldInfo AuthorizationRequirementField = AccessTools.Field(typeof(GlobalKeyValueObject), "authorizationRequirement");

		private static float _nextAttemptTime;
		private static float _retryIntervalSeconds = RetryIntervalSeconds;

		private static GameObject _spawnedRoot;

		private static CarLoadTargetLoader _spawnedLoader;

		private static string _selectedLoaderGlobalId;

		private static string _lastCandidateSnapshot;

		private static bool _loggedNoCandidates;

		internal static void Update()
		{
			if (!Main.Enabled || !StateManager.IsHost)
			{
				return;
			}
			if (HasLiveConfiguredLoader())
			{
				return;
			}
			if (Time.unscaledTime < _nextAttemptTime)
			{
				return;
			}
			CarLoadTargetLoader[] loaders = UnityEngine.Object.FindObjectsByType<CarLoadTargetLoader>(
				FindObjectsSortMode.None);
			DestroyLegacyClones(loaders);
			TrySpawnStandpipeClone(loaders);
			_retryIntervalSeconds = HasLiveConfiguredLoader()
				? RetryIntervalSeconds
				: Mathf.Min(MaxRetryIntervalSeconds, _retryIntervalSeconds * 2f);
			_nextAttemptTime = Time.unscaledTime + _retryIntervalSeconds;
		}

		internal static void Restore()
		{
			DestroyLegacyClones();
			_spawnedRoot = null;
			_spawnedLoader = null;
			_selectedLoaderGlobalId = null;
			_nextAttemptTime = 0f;
			_retryIntervalSeconds = RetryIntervalSeconds;
		}

		internal static void OnSceneChanged()
		{
			_spawnedRoot = null;
			_spawnedLoader = null;
			_selectedLoaderGlobalId = null;
			_nextAttemptTime = 0f;
			_retryIntervalSeconds = RetryIntervalSeconds;
		}

		private static bool HasLiveConfiguredLoader()
		{
			if (_spawnedLoader == null)
			{
				return false;
			}
			if (!IsDualServiceLoader(_spawnedLoader))
			{
				_spawnedLoader = null;
				_spawnedRoot = null;
				_selectedLoaderGlobalId = null;
				return false;
			}
			return true;
		}

		private static void TrySpawnStandpipeClone(CarLoadTargetLoader[] loaders)
		{
			if (loaders == null || loaders.Length == 0)
			{
				LogNoCandidatesOnce("no car load target loaders are active in the scene yet.");
				return;
			}

			List<CarLoadTargetLoader> waterCandidates = new List<CarLoadTargetLoader>();
			for (int i = 0; i < loaders.Length; i++)
			{
				CarLoadTargetLoader loader = loaders[i];
				if (loader == null || loader.load == null)
				{
					continue;
				}
				if (string.Equals(loader.load.id, OilFuelConstants.WaterLoadId, StringComparison.OrdinalIgnoreCase))
				{
					waterCandidates.Add(loader);
				}
			}

			LogCandidateSnapshot(waterCandidates);
			if (waterCandidates.Count == 0)
			{
				LogNoCandidatesOnce("no water standpipe candidates were found for bunker-c service.");
				return;
			}

			CarLoadTargetLoader templateLoader = SelectCandidate(waterCandidates);
			if (templateLoader == null)
			{
				LogNoCandidatesOnce("water standpipe candidates were found but none matched selection rules for bunker-c service.");
				return;
			}

			GameObject templateRoot = ResolveTemplateRoot(templateLoader);
			if (templateRoot == null)
			{
				Main.Warn("oil stand configuration skipped: could not resolve a water standpipe root for " + DescribeLoader(templateLoader));
				return;
			}

			ConfigureSelectedStand(templateRoot, templateLoader);
		}

		internal static bool IsDualServiceLoader(CarLoadTargetLoader loader)
		{
			if (loader == null || string.IsNullOrEmpty(_selectedLoaderGlobalId))
			{
				return false;
			}

			return string.Equals(GetGlobalObjectId(loader), _selectedLoaderGlobalId, StringComparison.OrdinalIgnoreCase);
		}

		private static void ConfigureSelectedStand(GameObject root, CarLoadTargetLoader loader)
		{
			if (root == null || loader == null)
			{
				return;
			}

			string globalId = GetGlobalObjectId(loader);
			if (string.IsNullOrEmpty(globalId))
			{
				Main.Warn("oil stand configuration skipped: selected water standpipe has no global object id: " + DescribeLoader(loader));
				return;
			}

			_spawnedRoot = root;
			_spawnedLoader = loader;
			_selectedLoaderGlobalId = globalId;
			RetitleCloneControls(root);
			Main.Log("oil stand configured on existing water standpipe: " + DescribeLoader(loader));
		}

		private static void DestroyLegacyClones()
		{
			CarLoadTargetLoader[] loaders = UnityEngine.Object.FindObjectsByType<CarLoadTargetLoader>(
				FindObjectsSortMode.None);
			DestroyLegacyClones(loaders);
		}

		private static void DestroyLegacyClones(CarLoadTargetLoader[] loaders)
		{
			if (loaders == null || loaders.Length == 0)
			{
				return;
			}

			int removed = 0;
			for (int i = 0; i < loaders.Length; i++)
			{
				CarLoadTargetLoader loader = loaders[i];
				if (!IsLegacyClone(loader))
				{
					continue;
				}

				GameObject root = ResolveTemplateRoot(loader);
				if (root == null)
				{
					root = loader.gameObject;
				}

				if (root != null)
				{
					UnityEngine.Object.Destroy(root);
					removed++;
				}
			}

			if (removed > 0)
			{
				Main.Log("removed legacy bunker-c standpipe clones: " + removed);
			}
		}

		private static bool IsLegacyClone(CarLoadTargetLoader loader)
		{
			if (loader == null)
			{
				return false;
			}

			string globalId = GetGlobalObjectId(loader);
			if (!string.IsNullOrEmpty(globalId) && globalId.EndsWith("-bunker-c", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			string description = DescribeLoader(loader);
			return description.IndexOf("[Bunker-C]", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static void SpawnClone(GameObject templateRoot, CarLoadTargetLoader templateLoader)
		{
			if (templateRoot == null || templateLoader == null)
			{
				return;
			}

			Model.Ops.Definition.Load bunkerCLoad = OilFuelRegistry.GetOrCreateBunkerCLoad();
			string loaderPath = GetRelativePath(templateRoot.transform, templateLoader.transform);
			KeyValueObject templateKeyValueObject = templateLoader.keyValueObject ?? templateRoot.GetComponentInChildren<KeyValueObject>(true);
			GlobalKeyValueObject templateGlobal = templateRoot.GetComponentInChildren<GlobalKeyValueObject>(true);
			AuthorizationRequirement requirement = GetAuthorizationRequirement(templateGlobal);
			string originalId = templateGlobal != null ? templateGlobal.globalObjectId : null;

			GameObject cloneRoot = UnityEngine.Object.Instantiate(templateRoot, templateRoot.transform.parent);
			cloneRoot.name = templateRoot.name + " [Bunker-C]";
			cloneRoot.transform.position = templateRoot.transform.position + templateRoot.transform.right * CloneOffsetRight + templateRoot.transform.forward * CloneOffsetForward;
			cloneRoot.transform.rotation = templateRoot.transform.rotation;

			CarLoadTargetLoader cloneLoader = FindRelativeComponent<CarLoadTargetLoader>(cloneRoot.transform, loaderPath) ?? cloneRoot.GetComponentInChildren<CarLoadTargetLoader>(true);
			if (cloneLoader == null)
			{
				UnityEngine.Object.Destroy(cloneRoot);
				Main.Warn("oil stand cloned/spawned skipped: cloned water standpipe did not contain a loader.");
				return;
			}

			KeyValueObject cloneKeyValueObject = cloneLoader.keyValueObject ?? cloneRoot.GetComponentInChildren<KeyValueObject>(true);
			if (cloneKeyValueObject == null)
			{
				cloneKeyValueObject = cloneRoot.AddComponent<KeyValueObject>();
			}

			GlobalKeyValueObject cloneGlobal = cloneRoot.GetComponentInChildren<GlobalKeyValueObject>(true);
			if (cloneGlobal == null)
			{
				cloneGlobal = cloneRoot.AddComponent<GlobalKeyValueObject>();
			}

			string cloneId = BuildCloneId(originalId, cloneRoot.name);
			cloneGlobal.globalObjectId = cloneId;
			ResetCloneKeyValues(cloneKeyValueObject);
			if (!string.IsNullOrEmpty(originalId) && templateKeyValueObject != null)
			{
				StateManager.Shared.RegisterPropertyObject(originalId, templateKeyValueObject, requirement, null);
			}
			StateManager.Shared.RegisterPropertyObject(cloneId, cloneKeyValueObject, requirement, null);

			RetargetCloneComponents(cloneRoot, cloneKeyValueObject, bunkerCLoad);
			RetitleCloneControls(cloneRoot);
			DarkenLiquidVisuals(cloneRoot);
			StripCloneMetadata(cloneRoot);

			_spawnedRoot = cloneRoot;
			_spawnedLoader = cloneLoader;

			Main.Log("oil stand cloned/spawned: cloned water standpipe " + BuildHierarchyPath(templateRoot.transform) + " -> " + DescribeLoader(cloneLoader) + ", rate=" + cloneLoader.outputRate.ToString("0.##") + " gal/s");
		}

		private static void RetargetCloneComponents(GameObject cloneRoot, KeyValueObject cloneKeyValueObject, Model.Ops.Definition.Load bunkerCLoad)
		{
			CarLoadTargetLoader[] loaders = cloneRoot.GetComponentsInChildren<CarLoadTargetLoader>(true);
			for (int i = 0; i < loaders.Length; i++)
			{
				CarLoadTargetLoader loader = loaders[i];
				loader.load = bunkerCLoad;
				loader.sourceIndustry = null;
				loader.outputRate = OilFuelConstants.BunkerCLoadingRateGallonsPerSecond;
				loader.keyValueObject = cloneKeyValueObject;
			}

			CarLoaderSequencer[] sequencers = cloneRoot.GetComponentsInChildren<CarLoaderSequencer>(true);
			for (int i = 0; i < sequencers.Length; i++)
			{
				sequencers[i].keyValueObject = cloneKeyValueObject;
			}

		}

		private static void RetitleCloneControls(GameObject cloneRoot)
		{
			KeyValuePickableToggle[] toggles = cloneRoot.GetComponentsInChildren<KeyValuePickableToggle>(true);
			for (int i = 0; i < toggles.Length; i++)
			{
				KeyValuePickableToggle toggle = toggles[i];
				toggle.displayTitle = BunkerCStandpipeTitle;
				toggle.displayMessageFalse = "Click to Open";
				toggle.displayMessageTrue = "Click to Close";
			}

			ContinuousControl[] continuousControls = cloneRoot.GetComponentsInChildren<ContinuousControl>(true);
			for (int i = 0; i < continuousControls.Length; i++)
			{
				ContinuousControl control = continuousControls[i];
				control.displayName = BunkerCStandpipeTitle;
				control.tooltipText = () => RotateTooltipText;
			}
		}

		private static void DarkenLiquidVisuals(GameObject cloneRoot)
		{
			ParticleSystem[] particleSystems = cloneRoot.GetComponentsInChildren<ParticleSystem>(true);
			for (int i = 0; i < particleSystems.Length; i++)
			{
				ParticleSystem particleSystem = particleSystems[i];
				ParticleSystem.MainModule main = particleSystem.main;
				main.startColor = new ParticleSystem.MinMaxGradient(OilVisualColor);
			}

			Renderer[] renderers = cloneRoot.GetComponentsInChildren<Renderer>(true);
			for (int i = 0; i < renderers.Length; i++)
			{
				Renderer renderer = renderers[i];
				if (!LooksLikeLiquidRenderer(renderer))
				{
					continue;
				}

				Material[] materials = renderer.materials;
				for (int j = 0; j < materials.Length; j++)
				{
					Material material = materials[j];
					if (material == null)
					{
						continue;
					}
					SetMaterialColorIfPresent(material, "_BaseColor", OilVisualColor);
					SetMaterialColorIfPresent(material, "_Color", OilVisualColor);
					SetMaterialColorIfPresent(material, "_TintColor", OilVisualColor);
					SetMaterialColorIfPresent(material, "_EmissionColor", OilVisualColor * 0.15f);
				}
				renderer.materials = materials;
			}
		}

		private static void StripCloneMetadata(GameObject cloneRoot)
		{
			if (cloneRoot == null)
			{
				return;
			}

			int removedMapIcons = DestroyComponentsInChildren<MapIcon>(cloneRoot, disableFirst: true);
			int removedMapLabels = DestroyComponentsInChildren<MapLabel>(cloneRoot, disableFirst: false);
			int removedHoverables = DestroyComponentsInChildren<IndustryContentHoverable>(cloneRoot, disableFirst: true);
			int removedIndustryComponents = DestroyComponentsInChildren<IndustryComponent>(cloneRoot, disableFirst: true);
			int removedIndustries = DestroyComponentsInChildren<Industry>(cloneRoot, disableFirst: true);
			int removedTotal = removedMapIcons + removedMapLabels + removedHoverables + removedIndustryComponents + removedIndustries;
			if (removedTotal > 0)
			{
				Main.Log("oil stand metadata stripped from clone: mapIcons=" + removedMapIcons + ", mapLabels=" + removedMapLabels + ", hoverables=" + removedHoverables + ", industryComponents=" + removedIndustryComponents + ", industries=" + removedIndustries);
			}
		}

		private static int DestroyComponentsInChildren<T>(GameObject root, bool disableFirst) where T : Component
		{
			if (root == null)
			{
				return 0;
			}

			T[] components = root.GetComponentsInChildren<T>(true);
			int removed = 0;
			for (int i = 0; i < components.Length; i++)
			{
				T component = components[i];
				if (component == null)
				{
					continue;
				}

				if (disableFirst && component is Behaviour behaviour)
				{
					behaviour.enabled = false;
				}

				UnityEngine.Object.Destroy(component);
				removed++;
			}

			return removed;
		}

		private static bool LooksLikeLiquidRenderer(Renderer renderer)
		{
			if (renderer == null)
			{
				return false;
			}

			string objectName = renderer.name ?? string.Empty;
			if (ContainsLiquidHint(objectName))
			{
				return true;
			}

			Material[] sharedMaterials = renderer.sharedMaterials;
			for (int i = 0; i < sharedMaterials.Length; i++)
			{
				Material material = sharedMaterials[i];
				if (material != null && ContainsLiquidHint(material.name))
				{
					return true;
				}
			}

			return renderer is ParticleSystemRenderer;
		}

		private static bool ContainsLiquidHint(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return false;
			}

			return value.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 ||
				value.IndexOf("stream", StringComparison.OrdinalIgnoreCase) >= 0 ||
				value.IndexOf("liquid", StringComparison.OrdinalIgnoreCase) >= 0 ||
				value.IndexOf("flow", StringComparison.OrdinalIgnoreCase) >= 0 ||
				value.IndexOf("spout", StringComparison.OrdinalIgnoreCase) >= 0 ||
				value.IndexOf("hose", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static void SetMaterialColorIfPresent(Material material, string propertyName, Color color)
		{
			if (material.HasProperty(propertyName))
			{
				material.SetColor(propertyName, color);
			}
		}

		private static void ResetCloneKeyValues(KeyValueObject keyValueObject)
		{
			if (keyValueObject == null)
			{
				return;
			}

			keyValueObject["request"] = Value.Bool(false);
			keyValueObject["canLoad"] = Value.Bool(false);
			keyValueObject["isLoading"] = Value.Bool(false);
			keyValueObject["prepareLoad"] = Value.Bool(false);
			keyValueObject["animateLoad"] = Value.Bool(false);
		}

		private static AuthorizationRequirement GetAuthorizationRequirement(GlobalKeyValueObject globalKeyValueObject)
		{
			if (globalKeyValueObject == null || AuthorizationRequirementField == null)
			{
				return AuthorizationRequirement.MinimumLevelCrew;
			}

			object value = AuthorizationRequirementField.GetValue(globalKeyValueObject);
			if (value is AuthorizationRequirement requirement)
			{
				return requirement;
			}

			return AuthorizationRequirement.MinimumLevelCrew;
		}

		private static GameObject ResolveTemplateRoot(CarLoadTargetLoader loader)
		{
			if (loader == null)
			{
				return null;
			}

			for (Transform current = loader.transform; current != null; current = current.parent)
			{
				string name = current.name ?? string.Empty;
				if (name.IndexOf("water column", StringComparison.OrdinalIgnoreCase) >= 0 ||
					name.IndexOf("standpipe", StringComparison.OrdinalIgnoreCase) >= 0 ||
					name.IndexOf("water stand", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return current.gameObject;
				}
			}

			GlobalKeyValueObject global = loader.GetComponentInParent<GlobalKeyValueObject>();
			if (global != null)
			{
				return global.gameObject;
			}

			CarLoaderSequencer sequencer = loader.GetComponentInParent<CarLoaderSequencer>();
			if (sequencer != null)
			{
				return sequencer.gameObject;
			}

			return loader.transform.parent != null ? loader.transform.parent.gameObject : loader.gameObject;
		}

		private static CarLoadTargetLoader SelectCandidate(List<CarLoadTargetLoader> candidates)
		{
			CarLoadTargetLoader preferred = null;
			string preferredKey = null;
			int preferredScore = int.MinValue;
			for (int i = 0; i < candidates.Count; i++)
			{
				CarLoadTargetLoader candidate = candidates[i];
				string key = DescribeLoader(candidate);
				int score = CandidateScore(key);
				if (score <= 0)
				{
					continue;
				}
				if (preferred == null || score > preferredScore || (score == preferredScore && string.Compare(key, preferredKey, StringComparison.OrdinalIgnoreCase) < 0))
				{
					preferred = candidate;
					preferredKey = key;
					preferredScore = score;
				}
			}

			return preferred;
		}

		private static int CandidateScore(string description)
		{
			if (string.IsNullOrEmpty(description))
			{
				return 0;
			}

			int score = 0;
			if (description.IndexOf("water column", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				score += 100;
			}
			if (description.IndexOf("standpipe", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				score += 90;
			}
			if (description.IndexOf("column", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				score += 70;
			}
			if (description.IndexOf("spout", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				score += 50;
			}
			if (description.IndexOf("stand", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				score += 20;
			}
			if (description.IndexOf("tower", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				score -= 100;
			}

			return score;
		}

		private static void LogCandidateSnapshot(List<CarLoadTargetLoader> candidates)
		{
			if (candidates == null || candidates.Count == 0)
			{
				return;
			}

			List<string> descriptions = new List<string>(candidates.Count);
			for (int i = 0; i < candidates.Count; i++)
			{
				descriptions.Add(DescribeLoader(candidates[i]));
			}

			descriptions.Sort(StringComparer.OrdinalIgnoreCase);
			string snapshot = string.Join(" | ", descriptions.ToArray());
			if (snapshot == _lastCandidateSnapshot)
			{
				return;
			}

			_lastCandidateSnapshot = snapshot;
			_loggedNoCandidates = false;
			Main.Log("bunker-c standpipe water candidates: " + snapshot);
		}

		private static void LogNoCandidatesOnce(string reason)
		{
			if (_loggedNoCandidates)
			{
				return;
			}

			_loggedNoCandidates = true;
			Main.Warn("oil stand cloned/spawned skipped: " + reason);
		}

		private static string DescribeLoader(CarLoadTargetLoader loader)
		{
			if (loader == null)
			{
				return "<null loader>";
			}

			string path = BuildHierarchyPath(loader.transform);
			string globalId = GetGlobalObjectId(loader);
			string loadId = loader.load != null ? loader.load.id : "<null>";
			if (!string.IsNullOrEmpty(globalId))
			{
				return path + " [globalId=" + globalId + ", load=" + loadId + "]";
			}

			return path + " [load=" + loadId + "]";
		}

		private static string GetGlobalObjectId(CarLoadTargetLoader loader)
		{
			if (loader == null)
			{
				return null;
			}

			GlobalKeyValueObject global = loader.GetComponentInParent<GlobalKeyValueObject>();
			return global != null ? global.globalObjectId : null;
		}

		private static string BuildCloneId(string originalId, string cloneName)
		{
			if (!string.IsNullOrEmpty(originalId))
			{
				return originalId + "-bunker-c";
			}

			string safeName = string.IsNullOrEmpty(cloneName) ? "standpipe" : cloneName.Replace(' ', '-');
			return "oilfiring-" + safeName.ToLowerInvariant();
		}

		private static string GetRelativePath(Transform root, Transform target)
		{
			if (root == null || target == null)
			{
				return null;
			}

			List<string> parts = new List<string>();
			Transform current = target;
			while (current != null && current != root)
			{
				parts.Add(current.name);
				current = current.parent;
			}

			if (current != root)
			{
				return null;
			}

			parts.Reverse();
			return string.Join("/", parts.ToArray());
		}

		private static T FindRelativeComponent<T>(Transform root, string relativePath) where T : Component
		{
			if (root == null || string.IsNullOrEmpty(relativePath))
			{
				return null;
			}

			Transform child = root.Find(relativePath);
			return child != null ? child.GetComponent<T>() : null;
		}

		private static string BuildHierarchyPath(Transform transform)
		{
			if (transform == null)
			{
				return "<null>";
			}

			List<string> names = new List<string>();
			while (transform != null)
			{
				names.Add(transform.name);
				transform = transform.parent;
			}

			names.Reverse();
			return string.Join("/", names.ToArray());
		}
	}
}
