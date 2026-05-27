using AssetPack.Common;
using Helpers.Animation;
using KeyValue.Runtime;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Toolshed.ServiceFacilities
{
	/// <summary>
	/// Small runtime animation bridge for service scenery assets.
	/// Railroader's built-in KeyValueBoolAnimator expects its AnimationClip before Awake runs,
	/// which is awkward when Toolshed is attaching components after a FUSE asset has
	/// already spawned. This driver samples the clip directly from the asset's AnimationMap.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class ServiceFacilityAnimationDriver : MonoBehaviour
	{
		public KeyValueObject keyValueObject;
		public string boolKey = "prepareLoad";
		public AnimationMap animationMap;
		public GameObject sampleRoot;
		public string animationMapKey;
		public float speed = 1f;
		public bool invert;
		public bool debugLogging;
		public bool useTransformFallback;
		public string fallbackTransformName;
		public string[] fallbackTransformNames;
		internal ServiceFacilityAnimationFallbackTransformDefinition[] fallbackTransformOverrides;
		public Vector3 fallbackInactiveLocalEuler;
		public Vector3 fallbackActiveLocalEuler;
		public float fallbackDurationSeconds;

		private AnimationClip _clip;
		private float _time;
		private bool _hasLastTargetValue;
		private bool _lastTargetValue;
		private GameObject _resolvedSampleRoot;
		private Helpers.Animation.PlayableHandle _playable;
		private bool _resolveFailed;
		private bool _playableFailed;
		private TransformSnapshot[] _movementProbeSnapshot;
		private float _movementProbeLogTime;
		private bool _movementProbeActive;
		private Transform _fallbackTransform;
		private ServiceFacilityAnimationFallbackTransformDefinition _fallbackTransformOverride;
		private float _fallbackProgress;
		private bool _fallbackLogged;
		private float _nextBindingRefreshTime;
		private float _lastKnownFallbackDuration = DefaultFallbackDurationSeconds;

		private const float BindingRefreshIntervalSeconds = 0.5f;
		private const float DefaultFallbackDurationSeconds = 1f;
		private const float MinimumFallbackDurationSeconds = 0.05f;

		private void OnEnable()
		{
			ResolveClip();
			EnsurePlayable();
			Sample(TargetValue() ? ClipLength() : 0f);
		}

		public void RefreshBinding(AnimationMap map, GameObject root)
		{
			if (animationMap == map && sampleRoot == root)
			{
				if (_clip == null || _resolveFailed)
				{
					_resolveFailed = false;
					ResolveClip();
					EnsurePlayable();
					Sample(TargetValue() ? ClipLength() : 0f);
				}
				return;
			}

			DisposePlayable();
			animationMap = map;
			sampleRoot = root;
			_clip = null;
			_resolvedSampleRoot = null;
			_resolveFailed = false;
			_playableFailed = false;
			_fallbackTransform = null;
			_fallbackTransformOverride = null;
			_fallbackLogged = false;
			_movementProbeSnapshot = null;
			_nextBindingRefreshTime = 0f;
			ResolveClip();
			EnsurePlayable();
			Sample(TargetValue() ? ClipLength() : 0f);
		}

		private void OnDisable()
		{
			DisposePlayable();
		}

		private void OnDestroy()
		{
			DisposePlayable();
		}

		private void Update()
		{
			if (keyValueObject == null)
			{
				return;
			}

			RefreshStaleBindingIfNeeded();
			ResolveClip();
			EnsurePlayable();

			bool active = TargetValue();
			LogTargetChange(active);
			if (DriveTransformFallback(active))
			{
				CheckMovementProbe();
				return;
			}
			if (_clip == null)
			{
				return;
			}
			if (DrivePlayable(active))
			{
				CheckMovementProbe();
				return;
			}
			float delta = Mathf.Max(speed, 0.01f) * Time.deltaTime;
			if (_clip.isLooping && active)
			{
				_time += delta;
				if (_time > _clip.length)
				{
					_time = Mathf.Repeat(_time, _clip.length);
				}
			}
			else
			{
				float target = active ? _clip.length : 0f;
				_time = Mathf.MoveTowards(_time, target, delta);
			}

			Sample(_time);
			CheckMovementProbe();
		}

		private void LateUpdate()
		{
			if (keyValueObject == null || !useTransformFallback)
			{
				return;
			}

			// Some scenery systems restore prefab transforms after Update when an asset is
			// streamed or rebound. Re-applying the fallback pose in LateUpdate keeps the
			// service handle visually in sync with the persisted KeyValueObject state.
			RefreshStaleBindingIfNeeded();
			if (_fallbackTransform == null)
			{
				_fallbackTransform = ResolveFallbackTransform();
			}
			ApplyFallbackRotation();
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
						Main.Log("[ServiceFacility][Loader] animation binding refreshed mapKey=" + animationMapKey +
							", oldMap=" + (animationMap != null ? animationMap.name : "<null>") +
							", newMap=" + (replacement != null ? replacement.name : "<null>") +
							", root=" + sampleRoot.name);
					}
					RefreshBinding(replacement, sampleRoot);
					return;
				}

				_resolvedSampleRoot = null;
				_clip = null;
				_resolveFailed = false;
				_playableFailed = false;
				DisposePlayable();
			}

			if (_fallbackTransform != null && !IsChildOf(_fallbackTransform, sampleRoot.transform))
			{
				if (debugLogging)
				{
					Main.Log("[ServiceFacility][Loader] animation transform fallback stale mapKey=" + animationMapKey +
						", transform=" + _fallbackTransform.name +
						", root=" + sampleRoot.name);
				}
				_fallbackTransform = null;
				_fallbackTransformOverride = null;
				_fallbackLogged = false;
			}
		}

		private bool TargetValue()
		{
			bool value = keyValueObject != null && keyValueObject[boolKey].BoolValue;
			return invert ? !value : value;
		}

		private void LogTargetChange(bool active)
		{
			if (!debugLogging)
			{
				return;
			}
			if (_hasLastTargetValue && _lastTargetValue == active)
			{
				return;
			}
			_hasLastTargetValue = true;
			_lastTargetValue = active;
			Main.Log("[ServiceFacility][Loader] animation state key=" + boolKey + ", active=" + active + ", mapKey=" + animationMapKey + ", sampleRoot=" + SampleRoot().name);
			BeginMovementProbe(active);
		}

		private void ResolveClip()
		{
			if (_clip != null || _resolveFailed || string.IsNullOrWhiteSpace(animationMapKey))
			{
				return;
			}

			string source;
			if (!TryResolveClip(out _clip, out source))
			{
				_resolveFailed = true;
				return;
			}

			if (_resolvedSampleRoot == null)
			{
				_resolvedSampleRoot = ResolveSampleRoot();
			}
			RememberFallbackDuration(_clip.length);
			if (debugLogging)
			{
				Main.Log("[ServiceFacility][Loader] animation bound key=" + boolKey +
					", mapKey=" + animationMapKey +
					", clip=" + _clip.name +
					", source=" + source +
					", sampleRoot=" + SampleRoot().name);
			}
		}

		private bool TryResolveClip(out AnimationClip clip, out string source)
		{
			clip = null;
			source = null;

			if (TryResolveAnimationMapClip(out clip, out source))
			{
				return true;
			}
			if (TryResolveAnimationComponentClip(out clip, out source))
			{
				return true;
			}
			if (TryResolveAnimatorControllerClip(out clip, out source))
			{
				return true;
			}

			if (debugLogging)
			{
				string mapRootName = animationMap != null ? animationMap.name : "<no AnimationMap>";
				Main.Warn("[ServiceFacility][Loader] animation '" + animationMapKey + "' not available on " + mapRootName +
					". AnimationMap entries: " + DescribeAnimationMapEntries() +
					"; Animation component clips: " + DescribeAnimationComponentClips() +
					"; Animator controller clips: " + DescribeAnimatorControllerClips());
			}
			return false;
		}

		private bool DriveTransformFallback(bool active)
		{
			if (!useTransformFallback)
			{
				return false;
			}
			if (_fallbackTransform == null)
			{
				_fallbackTransform = ResolveFallbackTransform();
				if (_fallbackTransform == null)
				{
					return false;
				}
				_fallbackProgress = active ? 1f : 0f;
			}

			float duration = FallbackDuration();
			float target = active ? 1f : 0f;
			_fallbackProgress = Mathf.MoveTowards(_fallbackProgress, target, Mathf.Max(speed, 0.01f) * Time.deltaTime / duration);
			Vector3 inactiveEuler = EffectiveFallbackInactiveLocalEuler();
			Vector3 activeEuler = EffectiveFallbackActiveLocalEuler();
			Quaternion inactive = Quaternion.Euler(inactiveEuler);
			Quaternion activeRotation = Quaternion.Euler(activeEuler);
			ApplyFallbackRotation(inactive, activeRotation);
			_time = _fallbackProgress * duration;

			if (debugLogging && !_fallbackLogged)
			{
				_fallbackLogged = true;
				Main.Log("[ServiceFacility][Loader] animation transform fallback bound mapKey=" + animationMapKey +
					", transform=" + _fallbackTransform.name +
					", inactiveEuler=" + inactiveEuler +
					", activeEuler=" + activeEuler);
			}
			return true;
		}

		private float FallbackDuration()
		{
			if (fallbackDurationSeconds > MinimumFallbackDurationSeconds)
			{
				return fallbackDurationSeconds;
			}

			float clipLength = ClipLength();
			if (clipLength > MinimumFallbackDurationSeconds)
			{
				RememberFallbackDuration(clipLength);
				return clipLength;
			}
			return Mathf.Max(_lastKnownFallbackDuration, DefaultFallbackDurationSeconds);
		}

		private void RememberFallbackDuration(float duration)
		{
			if (duration > MinimumFallbackDurationSeconds)
			{
				_lastKnownFallbackDuration = duration;
			}
		}

		private void ApplyFallbackRotation()
		{
			if (_fallbackTransform == null)
			{
				return;
			}
			ApplyFallbackRotation(Quaternion.Euler(EffectiveFallbackInactiveLocalEuler()), Quaternion.Euler(EffectiveFallbackActiveLocalEuler()));
		}

		private void ApplyFallbackRotation(Quaternion inactive, Quaternion activeRotation)
		{
			if (_fallbackTransform == null)
			{
				return;
			}
			_fallbackTransform.localRotation = Quaternion.Slerp(inactive, activeRotation, _fallbackProgress);
		}

		private Transform ResolveFallbackTransform()
		{
			GameObject root = sampleRoot != null ? sampleRoot : (animationMap != null ? animationMap.gameObject : gameObject);
			string[] names = FallbackTransformNameCandidates();
			if (names.Length == 0)
			{
				return root != null ? root.transform : null;
			}

			Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
			for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
			{
				string candidateName = names[nameIndex];
				for (int i = 0; i < transforms.Length; i++)
				{
					Transform transform = transforms[i];
					if (transform != null && string.Equals(transform.name, candidateName, StringComparison.OrdinalIgnoreCase))
					{
						_fallbackTransformOverride = FindFallbackTransformOverride(candidateName);
						return transform;
					}
				}
			}

			if (debugLogging)
			{
				Main.Warn("[ServiceFacility][Loader] animation transform fallback '" + string.Join(", ", names) + "' not found under " + (root != null ? root.name : "<null>"));
			}
			return null;
		}

		private Vector3 EffectiveFallbackInactiveLocalEuler()
		{
			return _fallbackTransformOverride != null ? _fallbackTransformOverride.inactiveLocalEuler : fallbackInactiveLocalEuler;
		}

		private Vector3 EffectiveFallbackActiveLocalEuler()
		{
			return _fallbackTransformOverride != null ? _fallbackTransformOverride.activeLocalEuler : fallbackActiveLocalEuler;
		}

		private ServiceFacilityAnimationFallbackTransformDefinition FindFallbackTransformOverride(string transformName)
		{
			if (fallbackTransformOverrides == null || string.IsNullOrWhiteSpace(transformName))
			{
				return null;
			}

			for (int i = 0; i < fallbackTransformOverrides.Length; i++)
			{
				ServiceFacilityAnimationFallbackTransformDefinition candidate = fallbackTransformOverrides[i];
				if (candidate != null && candidate.Matches(transformName))
				{
					return candidate;
				}
			}
			return null;
		}

		private string[] FallbackTransformNameCandidates()
		{
			List<string> names = new List<string>();
			AddNameCandidate(names, fallbackTransformName);
			if (fallbackTransformNames != null)
			{
				for (int i = 0; i < fallbackTransformNames.Length; i++)
				{
					AddNameCandidate(names, fallbackTransformNames[i]);
				}
			}
			return names.ToArray();
		}

		private static void AddNameCandidate(List<string> names, string name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return;
			}
			for (int i = 0; i < names.Count; i++)
			{
				if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
				{
					return;
				}
			}
			names.Add(name);
		}

		private bool TryResolveAnimationMapClip(out AnimationClip clip, out string source)
		{
			clip = null;
			source = null;
			if (animationMap == null)
			{
				return false;
			}

			AnimationMap.MapEntry? fallbackEntry = null;
			for (int i = 0; i < animationMap.animationClips.Count; i++)
			{
				AnimationMap.MapEntry entry = animationMap.animationClips[i];
				if (entry.clip == null)
				{
					continue;
				}
				if (fallbackEntry == null)
				{
					fallbackEntry = entry;
				}
				if (string.Equals(entry.name, animationMapKey, StringComparison.OrdinalIgnoreCase))
				{
					clip = entry.clip;
					source = "AnimationMap entry '" + entry.name + "'";
					_resolvedSampleRoot = animationMap.gameObject;
					return true;
				}
				if (string.Equals(entry.clip.name, animationMapKey, StringComparison.OrdinalIgnoreCase))
				{
					clip = entry.clip;
					source = "AnimationMap clip '" + entry.clip.name + "' from entry '" + entry.name + "'";
					_resolvedSampleRoot = animationMap.gameObject;
					return true;
				}
			}

			if (animationMap.animationClips.Count == 1 && fallbackEntry != null)
			{
				AnimationMap.MapEntry entry = fallbackEntry.Value;
				clip = entry.clip;
				source = "single AnimationMap fallback entry '" + entry.name + "'";
				_resolvedSampleRoot = animationMap.gameObject;
				return true;
			}
			return false;
		}

		private bool TryResolveAnimationComponentClip(out AnimationClip clip, out string source)
		{
			clip = null;
			source = null;
			GameObject root = sampleRoot != null ? sampleRoot : (animationMap != null ? animationMap.gameObject : gameObject);
			Animation[] animations = root.GetComponentsInChildren<Animation>(true);
			AnimationClip singleClip = null;
			string singleSource = null;
			int clipCount = 0;
			for (int i = 0; i < animations.Length; i++)
			{
				Animation animation = animations[i];
				if (animation == null)
				{
					continue;
				}
				foreach (AnimationState state in animation)
				{
					if (state == null || state.clip == null)
					{
						continue;
					}
					clipCount++;
					singleClip = state.clip;
					singleSource = "Animation component '" + animation.name + "' state '" + state.name + "'";
					if (string.Equals(state.name, animationMapKey, StringComparison.OrdinalIgnoreCase) ||
						string.Equals(state.clip.name, animationMapKey, StringComparison.OrdinalIgnoreCase))
					{
						clip = state.clip;
						source = singleSource;
						_resolvedSampleRoot = animation.gameObject;
						return true;
					}
				}
			}

			if (clipCount == 1 && singleClip != null)
			{
				clip = singleClip;
				source = "single " + singleSource;
				return true;
			}
			return false;
		}

		private bool TryResolveAnimatorControllerClip(out AnimationClip clip, out string source)
		{
			clip = null;
			source = null;
			GameObject root = sampleRoot != null ? sampleRoot : (animationMap != null ? animationMap.gameObject : gameObject);
			Animator[] animators = root.GetComponentsInChildren<Animator>(true);
			AnimationClip singleClip = null;
			string singleSource = null;
			int clipCount = 0;
			for (int i = 0; i < animators.Length; i++)
			{
				Animator animator = animators[i];
				if (animator == null || animator.runtimeAnimatorController == null)
				{
					continue;
				}
				AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
				for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
				{
					AnimationClip candidate = clips[clipIndex];
					if (candidate == null)
					{
						continue;
					}
					clipCount++;
					singleClip = candidate;
					singleSource = "Animator '" + animator.name + "' controller clip '" + candidate.name + "'";
					if (string.Equals(candidate.name, animationMapKey, StringComparison.OrdinalIgnoreCase))
					{
						clip = candidate;
						source = singleSource;
						_resolvedSampleRoot = animator.gameObject;
						return true;
					}
				}
			}

			if (clipCount == 1 && singleClip != null)
			{
				clip = singleClip;
				source = "single " + singleSource;
				return true;
			}
			return false;
		}

		private float ClipLength()
		{
			return _clip != null ? _clip.length : 0f;
		}

		private void Sample(float time)
		{
			if (_clip == null)
			{
				return;
			}

			_time = Mathf.Clamp(time, 0f, _clip.length);
			if (_playable != null)
			{
				_playable.Time = _time;
				_playable.Speed = 0f;
				_playable.Play();
				return;
			}
			_clip.SampleAnimation(SampleRoot(), _time);
		}

		private void EnsurePlayable()
		{
			if (_playable != null || _playableFailed || _clip == null)
			{
				return;
			}

			try
			{
				GameObject root = SampleRoot();
				Animator animator = root.GetComponent<Animator>();
				if (animator == null)
				{
					animator = root.AddComponent<Animator>();
				}
				animator.applyRootMotion = false;
				_playable = animator.PlayableGraphAdapter().AddPlayable(_clip);
				_playable.Time = TargetValue() ? _clip.length : 0f;
				_playable.Speed = 0f;
				_playable.Play();
				_time = _playable.Time;
				if (debugLogging)
				{
					Main.Log("[ServiceFacility][Loader] animation playable ready mapKey=" + animationMapKey +
						", clip=" + _clip.name +
						", animatorRoot=" + root.name);
				}
			}
			catch (Exception ex)
			{
				_playableFailed = true;
				if (debugLogging)
				{
					Main.Warn("[ServiceFacility][Loader] animation playable failed mapKey=" + animationMapKey + ": " + ex.Message);
				}
			}
		}

		private bool DrivePlayable(bool active)
		{
			if (_playable == null || _clip == null)
			{
				return false;
			}

			_playable.ClampTimeToClipBounds();
			if (_clip.isLooping)
			{
				_playable.Speed = active ? Mathf.Max(speed, 0.01f) : 0f;
				_playable.Play();
				_time = _playable.Time;
				return true;
			}

			float targetSpeed = Mathf.Max(speed, 0.01f) * (active ? 1f : -1f);
			bool atRaisedEnd = active && _playable.Time >= _clip.length - 0.001f;
			bool atLoweredEnd = !active && _playable.Time <= 0.001f;
			if (atRaisedEnd)
			{
				_playable.Time = _clip.length;
				_playable.Speed = 0f;
			}
			else if (atLoweredEnd)
			{
				_playable.Time = 0f;
				_playable.Speed = 0f;
			}
			else
			{
				_playable.Speed = targetSpeed;
			}
			_playable.Play();
			_time = _playable.Time;
			return true;
		}

		private void DisposePlayable()
		{
			if (_playable == null)
			{
				return;
			}
			try
			{
				_playable.Dispose();
			}
			catch (Exception ex)
			{
				if (debugLogging)
				{
					Main.Warn("[ServiceFacility][Loader] ignored stale playable dispose mapKey=" + animationMapKey + ": " + ex.Message);
				}
			}
			finally
			{
				_playable = null;
			}
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
			if (animationMap != null)
			{
				return animationMap.gameObject;
			}
			GameObject best = sampleRoot != null ? sampleRoot : gameObject;
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
				Main.Log("[ServiceFacility][Loader] animation sample root selected mapKey=" + animationMapKey + ", root=" + best.name + ", changedTransforms=" + bestChangedCount + ", candidates=" + candidates.Count);
			}
			return best;
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
				if (string.Equals(entry.name, animationMapKey, StringComparison.OrdinalIgnoreCase) ||
					(entry.clip != null && string.Equals(entry.clip.name, animationMapKey, StringComparison.OrdinalIgnoreCase)))
				{
					return true;
				}
			}
			return map.animationClips.Count == 1;
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

		private void BeginMovementProbe(bool active)
		{
			if (!debugLogging || _clip == null)
			{
				return;
			}

			Transform[] transforms = SampleRoot().GetComponentsInChildren<Transform>(true);
			_movementProbeSnapshot = new TransformSnapshot[transforms.Length];
			for (int i = 0; i < transforms.Length; i++)
			{
				_movementProbeSnapshot[i] = new TransformSnapshot(transforms[i]);
			}
			_movementProbeActive = active;
			_movementProbeLogTime = Time.unscaledTime + 0.75f;
		}

		private void CheckMovementProbe()
		{
			if (!debugLogging || _movementProbeSnapshot == null || Time.unscaledTime < _movementProbeLogTime)
			{
				return;
			}

			int changed = 0;
			for (int i = 0; i < _movementProbeSnapshot.Length; i++)
			{
				if (_movementProbeSnapshot[i].HasChanged())
				{
					changed++;
				}
			}
			Main.Log("[ServiceFacility][Loader] animation movement probe mapKey=" + animationMapKey +
				", active=" + _movementProbeActive +
				", changedTransforms=" + changed +
				", clipTime=" + _time.ToString("0.###") + "/" + ClipLength().ToString("0.###") +
				", playable=" + (_playable != null));
			_movementProbeSnapshot = null;
		}

		private string DescribeAnimationMapEntries()
		{
			if (animationMap == null || animationMap.animationClips == null || animationMap.animationClips.Count == 0)
			{
				return "(none)";
			}
			List<string> names = new List<string>();
			for (int i = 0; i < animationMap.animationClips.Count; i++)
			{
				AnimationMap.MapEntry entry = animationMap.animationClips[i];
				names.Add("'" + entry.name + "' -> '" + (entry.clip != null ? entry.clip.name : "<null>") + "'");
			}
			return string.Join(", ", names.ToArray());
		}

		private string DescribeAnimationComponentClips()
		{
			GameObject root = sampleRoot != null ? sampleRoot : (animationMap != null ? animationMap.gameObject : gameObject);
			Animation[] animations = root.GetComponentsInChildren<Animation>(true);
			List<string> names = new List<string>();
			for (int i = 0; i < animations.Length; i++)
			{
				Animation animation = animations[i];
				if (animation == null)
				{
					continue;
				}
				foreach (AnimationState state in animation)
				{
					if (state != null && state.clip != null)
					{
						names.Add(animation.name + "." + state.name + " -> '" + state.clip.name + "'");
					}
				}
			}
			return names.Count > 0 ? string.Join(", ", names.ToArray()) : "(none)";
		}

		private string DescribeAnimatorControllerClips()
		{
			GameObject root = sampleRoot != null ? sampleRoot : (animationMap != null ? animationMap.gameObject : gameObject);
			Animator[] animators = root.GetComponentsInChildren<Animator>(true);
			List<string> names = new List<string>();
			for (int i = 0; i < animators.Length; i++)
			{
				Animator animator = animators[i];
				if (animator == null || animator.runtimeAnimatorController == null)
				{
					continue;
				}
				AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
				for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
				{
					AnimationClip clip = clips[clipIndex];
					if (clip != null)
					{
						names.Add(animator.name + " -> '" + clip.name + "'");
					}
				}
			}
			return names.Count > 0 ? string.Join(", ", names.ToArray()) : "(none)";
		}

		private struct TransformSnapshot
		{
			private readonly Transform _transform;
			private readonly Vector3 _localPosition;
			private readonly Quaternion _localRotation;
			private readonly Vector3 _localScale;

			public TransformSnapshot(Transform transform)
			{
				_transform = transform;
				_localPosition = transform != null ? transform.localPosition : Vector3.zero;
				_localRotation = transform != null ? transform.localRotation : Quaternion.identity;
				_localScale = transform != null ? transform.localScale : Vector3.one;
			}

			public bool HasChanged()
			{
				if (_transform == null)
				{
					return false;
				}
				return (_transform.localPosition - _localPosition).sqrMagnitude > 0.000001f ||
					Quaternion.Angle(_transform.localRotation, _localRotation) > 0.01f ||
					(_transform.localScale - _localScale).sqrMagnitude > 0.000001f;
			}
		}
	}
}
