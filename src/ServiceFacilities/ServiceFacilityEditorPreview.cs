using System;
using System.Collections.Generic;
using AssetPack.Common;
using Model;
using UI.CarEditor;
using UnityEngine;

namespace Toolshed.ServiceFacilities
{
	/// <summary>
	/// Definition-editor-only visual helper for service loader authoring.
	/// This lets the asset author prove chute animation, click boxes, and flow origins while
	/// editing the scenery definition, instead of reloading a map to guess at alignment.
	/// </summary>
	internal sealed class ServiceFacilityEditorPreview : MonoBehaviour
	{
		public bool previewEnabled;
		public bool previewAnimation = true;
		public float previewAnimationPosition;
		public bool previewFlow = true;
		public bool previewOrigin = true;
		public bool previewClickBox = true;
		public bool previewServiceRadius = true;
		public string animationMapKey;
		public AnimationMap animationMap;
		public GameObject sampleRoot;
		public bool useBoxInteractionCollider;
		public Vector3 interactionBoxCenter;
		public Vector3 interactionBoxSize;
		public float interactionRadius = 0.45f;
		public float serviceRadius = 0.65f;
		public float extendedLoadTargetRadius = 3f;
		public bool useServiceTargetBox;
		public Vector3 serviceTargetBoxCenter;
		public Vector3 serviceTargetBoxSize;
		public bool createParticleSystem;
		public bool createVisibleStream;
		public float streamLength = 1f;
		public float streamWidth = 0.06f;
		public bool streamUsesWorldDown = true;
		public Vector3 streamLocalStart;
		public Vector3 streamLocalEnd;
		public Vector3 effectColorRgb = new Vector3(0.08f, 0.06f, 0.04f);
		public float effectAlpha = 0.9f;

		private GameObject _originMarker;
		private GameObject _clickBoxMarker;
		private GameObject _serviceRadiusMarker;
		private GameObject _extendedRadiusMarker;
		private GameObject _serviceTargetBoxMarker;
		private GameObject _streamMarker;
		private readonly List<GameObject> _streamChunkMarkers = new List<GameObject>();
		private Material _originMaterial;
		private Material _clickMaterial;
		private Material _serviceRadiusMaterial;
		private Material _extendedRadiusMaterial;
		private Material _serviceTargetBoxMaterial;
		private Material _streamMaterial;

		public static void ConfigureStorage(GameObject gameObject, ServiceFacilityStorageAuthoring authoring, ToolshedServiceStorageComponent component)
		{
			if (gameObject == null || authoring == null || component == null)
			{
				return;
			}

			ServiceFacilityEditorPreview preview = gameObject.GetComponent<ServiceFacilityEditorPreview>() ??
				gameObject.AddComponent<ServiceFacilityEditorPreview>();
			preview.previewEnabled = component.EditorPreviewEnabled;
			preview.previewAnimation = false;
			preview.previewFlow = false;
			preview.previewOrigin = false;
			preview.previewClickBox = component.EditorPreviewShowClickBox;
			preview.previewServiceRadius = false;
			preview.useBoxInteractionCollider = component.UseBoxInteractionCollider || component.InteractionBoxSize != Vector3.zero;
			preview.interactionBoxCenter = component.InteractionBoxCenter;
			preview.interactionBoxSize = component.InteractionBoxSize;
			preview.interactionRadius = component.InteractionRadius > 0f ? component.InteractionRadius : 1f;
		}

		public static void ConfigureLoadPoint(ComponentBuilderContext ctx, ServiceFacilityLoadPointAuthoring authoring, ToolshedServiceLoadPointComponent component)
		{
			if (ctx.GameObject == null || authoring == null || component == null || authoring.gameObject == null)
			{
				return;
			}

			ServiceFacilityEditorPreview preview = authoring.gameObject.GetComponent<ServiceFacilityEditorPreview>() ??
				authoring.gameObject.AddComponent<ServiceFacilityEditorPreview>();
			preview.previewEnabled = component.EditorPreviewEnabled;
			preview.previewAnimation = component.EditorPreviewAnimation;
			preview.previewAnimationPosition = Mathf.Clamp01(component.EditorPreviewAnimationPosition);
			preview.previewFlow = component.EditorPreviewFlow;
			preview.previewOrigin = component.EditorPreviewShowOrigin || component.DebugOriginMarker;
			preview.previewClickBox = component.EditorPreviewShowClickBox;
			preview.previewServiceRadius = component.EditorPreviewShowServiceRadius;
			preview.animationMapKey = component.AnimationMapKey ?? "";
			preview.sampleRoot = ResolveSampleRoot(ctx.GameObject, ctx.AnimatorGameObject);
			preview.animationMap = ResolveAnimationMap(ctx.AnimatorGameObject, preview.sampleRoot);
			preview.useBoxInteractionCollider = component.UseBoxInteractionCollider || component.InteractionBoxSize != Vector3.zero;
			preview.interactionBoxCenter = component.InteractionBoxCenter;
			preview.interactionBoxSize = component.InteractionBoxSize;
			preview.interactionRadius = component.InteractionRadius > 0f ? component.InteractionRadius : 0.45f;
			preview.serviceRadius = component.ServiceRadius > 0f ? component.ServiceRadius : 0.65f;
			preview.extendedLoadTargetRadius = component.ExtendedLoadTargetRadius > 0f ? component.ExtendedLoadTargetRadius : 3f;
			preview.useServiceTargetBox = component.UseServiceTargetBox;
			preview.serviceTargetBoxCenter = component.ServiceTargetBoxCenter;
			preview.serviceTargetBoxSize = component.ServiceTargetBoxSize;
			preview.createParticleSystem = component.CreateParticleSystem;
			preview.createVisibleStream = component.CreateVisibleStream;
			preview.streamLength = component.StreamLength > 0f ? component.StreamLength : 1f;
			preview.streamWidth = component.StreamWidth > 0f ? component.StreamWidth : 0.06f;
			preview.streamUsesWorldDown = component.StreamUsesWorldDown;
			preview.streamLocalStart = component.StreamLocalStart;
			preview.streamLocalEnd = component.StreamLocalEnd;
			preview.effectColorRgb = component.EffectColorRgb;
			preview.effectAlpha = Mathf.Clamp01(component.EffectAlpha);
		}

		private static GameObject ResolveSampleRoot(GameObject componentObject, GameObject animatorObject)
		{
			if (animatorObject != null)
			{
				return animatorObject;
			}
			if (componentObject != null && componentObject.transform.root != null)
			{
				return componentObject.transform.root.gameObject;
			}
			return componentObject;
		}

		private static AnimationMap ResolveAnimationMap(GameObject animatorObject, GameObject sampleRoot)
		{
			if (animatorObject != null)
			{
				AnimationMap map = animatorObject.GetComponent<AnimationMap>();
				if (map != null)
				{
					return map;
				}
			}
			return sampleRoot != null ? sampleRoot.GetComponentInChildren<AnimationMap>(true) : null;
		}

		private void Update()
		{
			bool visible = previewEnabled && DefinitionEditorModeController.IsEditing;
			SetPreviewVisible(visible);
			if (!visible)
			{
				return;
			}

			if (previewAnimation)
			{
				SampleAnimation();
			}
			if (previewOrigin)
			{
				EnsureOriginMarker();
			}
			if (previewClickBox)
			{
				EnsureClickBoxMarker();
			}
			if (previewServiceRadius)
			{
				EnsureRadiusMarkers();
				UpdateRadiusMarkers();
			}
			if (useServiceTargetBox && serviceTargetBoxSize != Vector3.zero)
			{
				EnsureServiceTargetBoxMarker();
				UpdateServiceTargetBoxMarker();
			}
			if (previewFlow && ShouldPreviewChunkStream())
			{
				EnsureStreamChunkMarkers();
				UpdateStreamChunkMarkers();
			}
			if (previewFlow && createVisibleStream)
			{
				EnsureStreamMarker();
				UpdateStreamMarker();
			}
		}

		private void OnDisable()
		{
			SetPreviewVisible(false);
		}

		private void OnDestroy()
		{
			DestroyPreviewObject(_originMarker);
			DestroyPreviewObject(_clickBoxMarker);
			DestroyPreviewObject(_serviceRadiusMarker);
			DestroyPreviewObject(_extendedRadiusMarker);
			DestroyPreviewObject(_serviceTargetBoxMarker);
			DestroyPreviewObject(_streamMarker);
			DestroyStreamChunkMarkers();
			DestroyPreviewMaterial(_originMaterial);
			DestroyPreviewMaterial(_clickMaterial);
			DestroyPreviewMaterial(_serviceRadiusMaterial);
			DestroyPreviewMaterial(_extendedRadiusMaterial);
			DestroyPreviewMaterial(_serviceTargetBoxMaterial);
			DestroyPreviewMaterial(_streamMaterial);
		}

		private void SetPreviewVisible(bool visible)
		{
			SetActive(_originMarker, visible && previewOrigin);
			SetActive(_clickBoxMarker, visible && previewClickBox);
			SetActive(_serviceRadiusMarker, visible && previewServiceRadius);
			SetActive(_extendedRadiusMarker, visible && previewServiceRadius);
			SetActive(_serviceTargetBoxMarker, visible && useServiceTargetBox && serviceTargetBoxSize != Vector3.zero);
			SetActive(_streamMarker, visible && previewFlow && createVisibleStream);
			for (int i = 0; i < _streamChunkMarkers.Count; i++)
			{
				SetActive(_streamChunkMarkers[i], visible && previewFlow && ShouldPreviewChunkStream());
			}
		}

		private static void SetActive(GameObject gameObject, bool active)
		{
			if (gameObject != null && gameObject.activeSelf != active)
			{
				gameObject.SetActive(active);
			}
		}

		private void SampleAnimation()
		{
			AnimationClip clip = ResolveClip();
			GameObject root = sampleRoot != null ? sampleRoot : gameObject;
			if (clip == null || root == null)
			{
				return;
			}
			float time = Mathf.Clamp01(previewAnimationPosition) * Mathf.Max(clip.length, 0f);
			clip.SampleAnimation(root, time);
		}

		private AnimationClip ResolveClip()
		{
			AnimationMap map = animationMap != null ? animationMap : ResolveAnimationMap(null, sampleRoot != null ? sampleRoot : gameObject);
			if (map != null)
			{
				AnimationClip fallback = null;
				for (int i = 0; i < map.animationClips.Count; i++)
				{
					AnimationMap.MapEntry entry = map.animationClips[i];
					if (entry.clip == null)
					{
						continue;
					}
					if (fallback == null)
					{
						fallback = entry.clip;
					}
					if (string.Equals(entry.name, animationMapKey, StringComparison.OrdinalIgnoreCase) ||
						string.Equals(entry.clip.name, animationMapKey, StringComparison.OrdinalIgnoreCase))
					{
						return entry.clip;
					}
				}
				if (map.animationClips.Count == 1)
				{
					return fallback;
				}
			}

			GameObject root = sampleRoot != null ? sampleRoot : gameObject;
			Animation[] animations = root != null ? root.GetComponentsInChildren<Animation>(true) : Array.Empty<Animation>();
			for (int i = 0; i < animations.Length; i++)
			{
				foreach (AnimationState state in animations[i])
				{
					if (state != null && state.clip != null &&
						(string.Equals(state.name, animationMapKey, StringComparison.OrdinalIgnoreCase) ||
						string.Equals(state.clip.name, animationMapKey, StringComparison.OrdinalIgnoreCase)))
					{
						return state.clip;
					}
				}
			}
			return null;
		}

		private void EnsureOriginMarker()
		{
			if (_originMarker == null)
			{
				_originMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
				_originMarker.name = "Toolshed Editor Flow Origin Preview";
				_originMarker.hideFlags = HideFlags.DontSave;
				_originMarker.transform.SetParent(transform, false);
				_originMarker.transform.localPosition = Vector3.zero;
				_originMarker.transform.localRotation = Quaternion.identity;
				_originMarker.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
				DestroyCollider(_originMarker);
				_originMaterial = CreateMaterial(new Color(1f, 0.85f, 0.1f, 0.8f));
				SetMaterial(_originMarker, _originMaterial);
			}
		}

		private void EnsureClickBoxMarker()
		{
			if (_clickBoxMarker == null)
			{
				_clickBoxMarker = GameObject.CreatePrimitive(useBoxInteractionCollider ? PrimitiveType.Cube : PrimitiveType.Sphere);
				_clickBoxMarker.name = "Toolshed Editor Click Box Preview";
				_clickBoxMarker.hideFlags = HideFlags.DontSave;
				_clickBoxMarker.transform.SetParent(transform, false);
				DestroyCollider(_clickBoxMarker);
				_clickMaterial = CreateMaterial(new Color(0.1f, 0.75f, 1f, 0.22f));
				SetMaterial(_clickBoxMarker, _clickMaterial);
			}
			_clickBoxMarker.transform.localPosition = interactionBoxCenter;
			_clickBoxMarker.transform.localRotation = Quaternion.identity;
			_clickBoxMarker.transform.localScale = useBoxInteractionCollider && interactionBoxSize != Vector3.zero
				? interactionBoxSize
				: Vector3.one * Mathf.Max(0.01f, interactionRadius * 2f);
		}

		private void EnsureStreamMarker()
		{
			if (_streamMarker == null)
			{
				_streamMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
				_streamMarker.name = "Toolshed Editor Flow Preview";
				_streamMarker.hideFlags = HideFlags.DontSave;
				DestroyCollider(_streamMarker);
				Color color = new Color(
					Mathf.Clamp01(effectColorRgb.x),
					Mathf.Clamp01(effectColorRgb.y),
					Mathf.Clamp01(effectColorRgb.z),
					Mathf.Clamp01(effectAlpha));
				_streamMaterial = CreateMaterial(color);
				SetMaterial(_streamMarker, _streamMaterial);
			}
		}

		private void EnsureStreamChunkMarkers()
		{
			if (_streamChunkMarkers.Count > 0)
			{
				return;
			}

			Color color = new Color(
				Mathf.Clamp01(effectColorRgb.x),
				Mathf.Clamp01(effectColorRgb.y),
				Mathf.Clamp01(effectColorRgb.z),
				Mathf.Clamp01(effectAlpha));
			if (_streamMaterial == null)
			{
				_streamMaterial = CreateMaterial(color);
			}
			for (int i = 0; i < 18; i++)
			{
				GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
				chunk.name = "Toolshed Editor Coal Chunk Preview";
				chunk.hideFlags = HideFlags.DontSave;
				chunk.transform.SetParent(transform, false);
				DestroyCollider(chunk);
				SetMaterial(chunk, _streamMaterial);
				_streamChunkMarkers.Add(chunk);
			}
		}

		private void UpdateStreamChunkMarkers()
		{
			if (_streamChunkMarkers.Count == 0)
			{
				return;
			}
			Vector3 localEnd = streamLocalStart == Vector3.zero && streamLocalEnd == Vector3.zero
				? new Vector3(0f, -Mathf.Max(0.01f, streamLength), 0f)
				: streamLocalEnd;
			Vector3 dropEnd = localEnd + EffectiveLocalWorldDown() * Mathf.Max(0f, streamLength);
			float chuteLength = Vector3.Distance(streamLocalStart, localEnd);
			float dropLength = Vector3.Distance(localEnd, dropEnd);
			float totalLength = chuteLength + dropLength;
			if (totalLength <= 0.01f)
			{
				localEnd = streamLocalStart + Vector3.down * Mathf.Max(0.01f, streamLength);
				chuteLength = Vector3.Distance(streamLocalStart, localEnd);
				totalLength = chuteLength;
				dropLength = 0f;
			}
			float baseSize = Mathf.Max(0.015f, streamWidth * 0.45f);
			for (int i = 0; i < _streamChunkMarkers.Count; i++)
			{
				GameObject chunk = _streamChunkMarkers[i];
				float t = (float)i / Mathf.Max(1, _streamChunkMarkers.Count - 1);
				float distance = t * totalLength;
				Vector3 segmentStart = streamLocalStart;
				Vector3 segmentEnd = localEnd;
				float segmentLength = chuteLength;
				if (dropLength > 0.01f && distance > chuteLength)
				{
					segmentStart = localEnd;
					segmentEnd = dropEnd;
					segmentLength = dropLength;
					distance -= chuteLength;
				}
				Vector3 direction = segmentEnd - segmentStart;
				if (direction.sqrMagnitude <= 0.0001f)
				{
					direction = Vector3.down;
					segmentLength = Mathf.Max(0.01f, segmentLength);
				}
				else
				{
					direction.Normalize();
				}
				Vector3 side = Vector3.Cross(direction, Vector3.up);
				if (side.sqrMagnitude <= 0.001f)
				{
					side = Vector3.Cross(direction, Vector3.right);
				}
				side.Normalize();
				Vector3 up = Vector3.Cross(side, direction).normalized;
				float segmentT = segmentLength <= 0.01f ? 0f : Mathf.Clamp01(distance / segmentLength);
				float jitterA = (((i * 37) % 11) - 5) / 5f;
				float jitterB = (((i * 53) % 13) - 6) / 6f;
				chunk.transform.localPosition = Vector3.Lerp(segmentStart, segmentEnd, segmentT) +
					side * jitterA * streamWidth * 0.35f +
					up * jitterB * streamWidth * 0.25f;
				chunk.transform.localRotation = Quaternion.LookRotation(direction, up) *
					Quaternion.Euler(i * 29f, i * 47f, i * 71f);
				float scale = baseSize * (0.65f + ((i * 17) % 7) * 0.08f);
				chunk.transform.localScale = new Vector3(scale * 1.25f, scale * 0.75f, scale);
			}
		}

		private bool ShouldPreviewChunkStream()
		{
			return createParticleSystem &&
				!streamUsesWorldDown &&
				(streamLocalStart != Vector3.zero || streamLocalEnd != Vector3.zero);
		}

		private Vector3 EffectiveLocalWorldDown()
		{
			Vector3 localDown = transform.InverseTransformDirection(Vector3.down);
			return localDown.sqrMagnitude <= 0.0001f ? Vector3.down : localDown.normalized;
		}

		private void EnsureRadiusMarkers()
		{
			if (_serviceRadiusMarker == null)
			{
				_serviceRadiusMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
				_serviceRadiusMarker.name = "Toolshed Editor Service Radius Preview";
				_serviceRadiusMarker.hideFlags = HideFlags.DontSave;
				DestroyCollider(_serviceRadiusMarker);
				_serviceRadiusMaterial = CreateMaterial(new Color(1f, 0.86f, 0.12f, 0.18f));
				SetMaterial(_serviceRadiusMarker, _serviceRadiusMaterial);
			}
			if (_extendedRadiusMarker == null)
			{
				_extendedRadiusMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
				_extendedRadiusMarker.name = "Toolshed Editor Extended Target Radius Preview";
				_extendedRadiusMarker.hideFlags = HideFlags.DontSave;
				DestroyCollider(_extendedRadiusMarker);
				_extendedRadiusMaterial = CreateMaterial(new Color(1f, 0.28f, 0.08f, 0.12f));
				SetMaterial(_extendedRadiusMarker, _extendedRadiusMaterial);
			}
		}

		private void UpdateRadiusMarkers()
		{
			UpdateRadiusMarker(_serviceRadiusMarker, Mathf.Max(0.01f, serviceRadius));
			UpdateRadiusMarker(_extendedRadiusMarker, Mathf.Max(0.01f, extendedLoadTargetRadius));
		}

		private void EnsureServiceTargetBoxMarker()
		{
			if (_serviceTargetBoxMarker == null)
			{
				_serviceTargetBoxMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
				_serviceTargetBoxMarker.name = "Toolshed Editor Service Target Box Preview";
				_serviceTargetBoxMarker.hideFlags = HideFlags.DontSave;
				_serviceTargetBoxMarker.transform.SetParent(transform, false);
				DestroyCollider(_serviceTargetBoxMarker);
				_serviceTargetBoxMaterial = CreateMaterial(new Color(0.1f, 1f, 0.2f, 0.22f));
				SetMaterial(_serviceTargetBoxMarker, _serviceTargetBoxMaterial);
			}
		}

		private void UpdateServiceTargetBoxMarker()
		{
			if (_serviceTargetBoxMarker == null)
			{
				return;
			}
			_serviceTargetBoxMarker.transform.localPosition = serviceTargetBoxCenter;
			_serviceTargetBoxMarker.transform.localRotation = Quaternion.identity;
			_serviceTargetBoxMarker.transform.localScale = serviceTargetBoxSize;
		}

		private void UpdateRadiusMarker(GameObject marker, float radius)
		{
			if (marker == null)
			{
				return;
			}
			marker.transform.position = transform.position + Vector3.up * 0.02f;
			marker.transform.rotation = Quaternion.identity;
			marker.transform.localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);
		}

		private void UpdateStreamMarker()
		{
			if (_streamMarker == null)
			{
				return;
			}

			Vector3 localEnd = !streamUsesWorldDown && streamLocalStart == Vector3.zero && streamLocalEnd == Vector3.zero
				? new Vector3(0f, -Mathf.Max(0.01f, streamLength), 0f)
				: streamLocalEnd;
			Vector3 start = streamUsesWorldDown ? transform.position : transform.TransformPoint(streamLocalStart);
			Vector3 end = streamUsesWorldDown
				? start + Vector3.down * Mathf.Max(0.01f, streamLength)
				: transform.TransformPoint(localEnd);
			Vector3 direction = end - start;
			float length = Mathf.Max(0.01f, direction.magnitude);
			direction /= length;
			_streamMarker.transform.position = start + direction * (length * 0.5f);
			_streamMarker.transform.rotation = Quaternion.FromToRotation(Vector3.up, direction);
			_streamMarker.transform.localScale = new Vector3(Mathf.Max(0.001f, streamWidth), length * 0.5f, Mathf.Max(0.001f, streamWidth));
		}

		private static Material CreateMaterial(Color color)
		{
			Shader shader = Shader.Find("Unlit/Color");
			if (shader == null)
			{
				shader = Shader.Find("Sprites/Default");
			}
			if (shader == null)
			{
				return null;
			}
			Material material = new Material(shader);
			material.hideFlags = HideFlags.DontSave;
			material.color = color;
			if (material.HasProperty("_Color"))
			{
				material.SetColor("_Color", color);
			}
			if (material.HasProperty("_BaseColor"))
			{
				material.SetColor("_BaseColor", color);
			}
			return material;
		}

		private static void SetMaterial(GameObject target, Material material)
		{
			Renderer renderer = target != null ? target.GetComponent<Renderer>() : null;
			if (renderer != null && material != null)
			{
				renderer.material = material;
			}
		}

		private static void DestroyCollider(GameObject target)
		{
			Collider collider = target != null ? target.GetComponent<Collider>() : null;
			if (collider != null)
			{
				Destroy(collider);
			}
		}

		private static void DestroyPreviewObject(GameObject target)
		{
			if (target != null)
			{
				Destroy(target);
			}
		}

		private void DestroyStreamChunkMarkers()
		{
			for (int i = 0; i < _streamChunkMarkers.Count; i++)
			{
				DestroyPreviewObject(_streamChunkMarkers[i]);
			}
			_streamChunkMarkers.Clear();
		}

		private static void DestroyPreviewMaterial(Material material)
		{
			if (material != null)
			{
				Destroy(material);
			}
		}
	}
}
