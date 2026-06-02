using AssetPack.Common;
using Model.Ops;
using Model.Ops.Definition;
using System.Collections.Generic;
using UnityEngine;

namespace Toolshed.ServiceFacilities
{
	/// <summary>
	/// Samples an animation by facility storage percentage.
	/// This is for visual inventory states such as a wood pile: empty storage samples
	/// the beginning of the clip, half storage samples the midpoint, and full storage
	/// samples the end.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class ServiceFacilityStorageAnimationDriver : MonoBehaviour
	{
		public AnimationMap animationMap;
		public string animationMapKey;
		public GameObject sampleRoot;
		public Industry sourceIndustry;
		public Load load;
		public float capacity;
		public bool invert;
		public bool debugLogging;
		public bool useTransformFallback;
		public string fallbackTransformName;
		public float emptyLocalY;
		public float fullLocalY;
		public float emptyLocalScaleZ = 1f;
		public float fullLocalScaleZ = 1f;

		private const float RefreshIntervalSeconds = 0.25f;

		private AnimationClip _clip;
		private float _nextRefreshTime;
		private float _nextBindingRefreshTime;
		private GameObject _resolvedSampleRoot;
		private Transform _fallbackTransform;
		private bool _hasLastNormalized;
		private float _lastNormalized;

		private const float BindingRefreshIntervalSeconds = 0.5f;

		private void OnEnable()
		{
			NormalizeNames();
			ResolveClip();
			SampleStorage();
		}

		public void RefreshBinding(AnimationMap map, GameObject root, Industry industry, Load storageLoad)
		{
			if (animationMap == map && sampleRoot == root && sourceIndustry == industry && load == storageLoad)
			{
				if (_clip == null)
				{
					ResolveClip();
					SampleStorage();
				}
				return;
			}

			animationMap = map;
			sampleRoot = root;
			sourceIndustry = industry;
			load = storageLoad;
			NormalizeNames();
			_clip = null;
			_resolvedSampleRoot = null;
			_fallbackTransform = null;
			_hasLastNormalized = false;
			_nextBindingRefreshTime = 0f;
			ResolveClip();
			SampleStorage();
		}

		private void Update()
		{
			if (Time.unscaledTime < _nextRefreshTime)
			{
				return;
			}
			_nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
			RefreshStaleBindingIfNeeded();
			ResolveClip();
			SampleStorage();
		}

		private void LateUpdate()
		{
			if (!useTransformFallback)
			{
				return;
			}

			// Storage props can be reset when scenery children are streamed back in.
			// Re-applying the storage pose late keeps visual fill level tied to inventory.
			RefreshStaleBindingIfNeeded();
			SampleStorage();
		}

		private void RefreshStaleBindingIfNeeded()
		{
			if (Time.unscaledTime < _nextBindingRefreshTime)
			{
				return;
			}
			_nextBindingRefreshTime = Time.unscaledTime + BindingRefreshIntervalSeconds;

			if (sampleRoot == null)
			{
				return;
			}

			bool mapStale = animationMap == null || !IsChildOf(animationMap.transform, sampleRoot.transform);
			bool sampleStale = _resolvedSampleRoot != null && !IsChildOf(_resolvedSampleRoot.transform, sampleRoot.transform);
			if (mapStale || sampleStale)
			{
				AnimationMap replacement = FindAnimationMapUnder(sampleRoot);
				if (replacement != animationMap)
				{
					if (debugLogging)
					{
						Main.Log("[ServiceFacility][Loader] storage animation binding refreshed mapKey=" + animationMapKey +
							", oldMap=" + (animationMap != null ? animationMap.name : "<null>") +
							", newMap=" + (replacement != null ? replacement.name : "<null>") +
							", root=" + sampleRoot.name);
					}
					RefreshBinding(replacement, sampleRoot, sourceIndustry, load);
					return;
				}

				_resolvedSampleRoot = null;
				_clip = null;
			}

			if (_fallbackTransform != null && !IsChildOf(_fallbackTransform, sampleRoot.transform))
			{
				if (debugLogging)
				{
					Main.Log("[ServiceFacility][Loader] storage transform fallback stale mapKey=" + animationMapKey +
						", transform=" + _fallbackTransform.name +
						", root=" + sampleRoot.name);
				}
				_fallbackTransform = null;
			}
		}

		private void ResolveClip()
		{
			NormalizeNames();
			if (_clip != null || animationMap == null || string.IsNullOrWhiteSpace(animationMapKey))
			{
				return;
			}

			try
			{
				_clip = animationMap.ClipForName(animationMapKey);
				_resolvedSampleRoot = ResolveSampleRoot();
				if (debugLogging)
				{
					Main.Log("[ServiceFacility][Loader] storage animation bound mapKey=" + animationMapKey + ", clip=" + _clip.name + ", mapRoot=" + animationMap.gameObject.name + ", sampleRoot=" + SampleRoot().name);
				}
			}
			catch (System.Exception ex)
			{
				if (debugLogging)
				{
					Main.Warn("[ServiceFacility][Loader] storage animation '" + animationMapKey + "' not available on " + animationMap.name + ": " + ex.Message);
				}
			}
		}

		private void SampleStorage()
		{
			if (sourceIndustry == null || load == null)
			{
				return;
			}

			float storageCapacity = capacity;
			if (storageCapacity <= 0f)
			{
				sourceIndustry.TryGetStorageCapacity(load, out storageCapacity);
			}
			if (storageCapacity <= 0f)
			{
				return;
			}

			float quantity = sourceIndustry.Storage.QuantityInStorage(load, null);
			float normalized = Mathf.Clamp01(quantity / storageCapacity);
			if (invert)
			{
				normalized = 1f - normalized;
			}
			LogStorageState(quantity, storageCapacity, normalized);
			if (_clip != null)
			{
				_clip.SampleAnimation(SampleRoot(), normalized * _clip.length);
			}
			ApplyTransformFallback(normalized);
		}

		private void LogStorageState(float quantity, float storageCapacity, float normalized)
		{
			if (!debugLogging)
			{
				return;
			}
			if (_hasLastNormalized && Mathf.Abs(_lastNormalized - normalized) < 0.01f)
			{
				return;
			}
			_hasLastNormalized = true;
			_lastNormalized = normalized;
			Main.Log("[ServiceFacility][Loader] storage animation state mapKey=" + animationMapKey +
				", load=" + load.id +
				", quantity=" + quantity.ToString("0.###") +
				", capacity=" + storageCapacity.ToString("0.###") +
				", normalized=" + normalized.ToString("0.###") +
				", sampleRoot=" + SampleRoot().name);
		}

		private GameObject SampleRoot()
		{
			if (_resolvedSampleRoot != null)
			{
				return _resolvedSampleRoot;
			}
			if (sampleRoot != null)
			{
				return sampleRoot;
			}
			if (animationMap != null)
			{
				return animationMap.gameObject;
			}
			return gameObject;
		}

		private GameObject ResolveSampleRoot()
		{
			GameObject best = sampleRoot != null ? sampleRoot : (animationMap != null ? animationMap.gameObject : gameObject);
			int bestChangedCount = -1;
			List<GameObject> candidates = BuildSampleRootCandidates();
			for (int i = 0; i < candidates.Count; i++)
			{
				GameObject candidate = candidates[i];
				int changedCount = CountChangedTransformsWhenSampled(candidate);
				if (changedCount > bestChangedCount)
				{
					bestChangedCount = changedCount;
					best = candidate;
				}
			}

			if (debugLogging)
			{
				Main.Log("[ServiceFacility][Loader] storage animation sample root selected mapKey=" + animationMapKey + ", root=" + best.name + ", changedTransforms=" + bestChangedCount + ", candidates=" + candidates.Count);
			}
			return best;
		}

		private void ApplyTransformFallback(float normalized)
		{
			if (!useTransformFallback)
			{
				return;
			}
			Transform target = ResolveFallbackTransform();
			if (target == null)
			{
				return;
			}

			Vector3 position = target.localPosition;
			position.y = Mathf.Lerp(emptyLocalY, fullLocalY, normalized);
			target.localPosition = position;

			Vector3 scale = target.localScale;
			scale.z = Mathf.Lerp(emptyLocalScaleZ, fullLocalScaleZ, normalized);
			target.localScale = scale;
		}

		private Transform ResolveFallbackTransform()
		{
			NormalizeNames();
			if (_fallbackTransform != null)
			{
				GameObject root = sampleRoot != null ? sampleRoot : SampleRoot();
				if (root == null || IsChildOf(_fallbackTransform, root.transform))
				{
					return _fallbackTransform;
				}
				_fallbackTransform = null;
			}
			if (string.IsNullOrWhiteSpace(fallbackTransformName))
			{
				return null;
			}

			Transform[] transforms = SampleRoot().GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < transforms.Length; i++)
			{
				Transform transform = transforms[i];
				if (NamesEqual(transform.name, fallbackTransformName))
				{
					_fallbackTransform = transform;
					if (debugLogging)
					{
						Main.Log("[ServiceFacility][Loader] storage transform fallback bound mapKey=" + animationMapKey + ", transform=" + transform.name);
					}
					return _fallbackTransform;
				}
			}

			if (debugLogging)
			{
				Main.Warn("[ServiceFacility][Loader] storage transform fallback '" + fallbackTransformName + "' not found under " + SampleRoot().name);
			}
			return null;
		}

		private List<GameObject> BuildSampleRootCandidates()
		{
			List<GameObject> candidates = new List<GameObject>();
			AddCandidate(candidates, animationMap != null ? animationMap.gameObject : null);
			AddCandidate(candidates, sampleRoot);

			if (animationMap != null)
			{
				Transform current = animationMap.transform.parent;
				while (current != null)
				{
					AddCandidate(candidates, current.gameObject);
					if (sampleRoot != null && current.gameObject == sampleRoot)
					{
						break;
					}
					current = current.parent;
				}
			}

			GameObject root = sampleRoot != null ? sampleRoot : (animationMap != null ? animationMap.gameObject : null);
			if (root != null)
			{
				Transform[] children = root.GetComponentsInChildren<Transform>(true);
				for (int i = 0; i < children.Length; i++)
				{
					AddCandidate(candidates, children[i].gameObject);
				}
			}

			return candidates;
		}

		private static void AddCandidate(List<GameObject> candidates, GameObject candidate)
		{
			if (candidate == null || candidates.Contains(candidate))
			{
				return;
			}
			candidates.Add(candidate);
		}

		private int CountChangedTransformsWhenSampled(GameObject root)
		{
			if (_clip == null || root == null)
			{
				return 0;
			}

			Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
			Vector3[] positions = new Vector3[transforms.Length];
			Quaternion[] rotations = new Quaternion[transforms.Length];
			Vector3[] scales = new Vector3[transforms.Length];
			for (int i = 0; i < transforms.Length; i++)
			{
				Transform transform = transforms[i];
				positions[i] = transform.localPosition;
				rotations[i] = transform.localRotation;
				scales[i] = transform.localScale;
			}

			_clip.SampleAnimation(root, _clip.length);
			int changed = 0;
			for (int i = 0; i < transforms.Length; i++)
			{
				Transform transform = transforms[i];
				if ((transform.localPosition - positions[i]).sqrMagnitude > 0.000001f ||
					Quaternion.Angle(transform.localRotation, rotations[i]) > 0.01f ||
					(transform.localScale - scales[i]).sqrMagnitude > 0.000001f)
				{
					changed++;
				}
			}

			for (int i = 0; i < transforms.Length; i++)
			{
				Transform transform = transforms[i];
				transform.localPosition = positions[i];
				transform.localRotation = rotations[i];
				transform.localScale = scales[i];
			}
			return changed;
		}

		private AnimationMap FindAnimationMapUnder(GameObject root)
		{
			if (root == null)
			{
				return null;
			}

			AnimationMap[] maps = root.GetComponentsInChildren<AnimationMap>(true);
			if (maps == null || maps.Length == 0)
			{
				return null;
			}

			for (int i = 0; i < maps.Length; i++)
			{
				AnimationMap map = maps[i];
				if (MapHasRequestedClip(map))
				{
					return map;
				}
			}
			return maps[0];
		}

		private bool MapHasRequestedClip(AnimationMap map)
		{
			if (map == null || map.animationClips == null)
			{
				return false;
			}

			for (int i = 0; i < map.animationClips.Count; i++)
			{
				AnimationMap.MapEntry entry = map.animationClips[i];
				if (NamesEqual(entry.name, animationMapKey) ||
					(entry.clip != null && NamesEqual(entry.clip.name, animationMapKey)))
				{
					return true;
				}
			}
			return map.animationClips.Count == 1;
		}

		private void NormalizeNames()
		{
			animationMapKey = Clean(animationMapKey);
			fallbackTransformName = Clean(fallbackTransformName);
		}

		private static string Clean(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
		}

		private static bool NamesEqual(string left, string right)
		{
			return string.Equals(Clean(left), Clean(right), System.StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsChildOf(Transform child, Transform ancestor)
		{
			if (child == null || ancestor == null)
			{
				return false;
			}

			Transform current = child;
			while (current != null)
			{
				if (current == ancestor)
				{
					return true;
				}
				current = current.parent;
			}
			return false;
		}
	}
}
