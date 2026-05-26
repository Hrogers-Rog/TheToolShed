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
		private GameObject _resolvedSampleRoot;
		private Transform _fallbackTransform;
		private bool _hasLastNormalized;
		private float _lastNormalized;

		private void OnEnable()
		{
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
			ResolveClip();
			SampleStorage();
		}

		private void ResolveClip()
		{
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
			if (_clip == null || animationMap == null || sourceIndustry == null || load == null)
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
			return sampleRoot != null ? sampleRoot : animationMap.gameObject;
		}

		private GameObject ResolveSampleRoot()
		{
			GameObject best = sampleRoot != null ? sampleRoot : animationMap.gameObject;
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
			if (_fallbackTransform != null)
			{
				return _fallbackTransform;
			}
			if (string.IsNullOrWhiteSpace(fallbackTransformName))
			{
				return null;
			}

			Transform[] transforms = SampleRoot().GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < transforms.Length; i++)
			{
				Transform transform = transforms[i];
				if (string.Equals(transform.name, fallbackTransformName, System.StringComparison.OrdinalIgnoreCase))
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
	}
}
