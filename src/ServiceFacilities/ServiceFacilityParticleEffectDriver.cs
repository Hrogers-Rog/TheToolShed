using System;
using System.Collections.Generic;
using KeyValue.Runtime;
using UnityEngine;

namespace Toolshed.ServiceFacilities
{
	/// <summary>
	/// Small key-driven particle bridge for service assets.
	/// Vanilla water uses a custom WaterCylinderController; this gives custom loaders a reusable,
	/// data-driven way to turn an existing or runtime-created ParticleSystem on while loading.
	/// </summary>
	internal sealed class ServiceFacilityParticleEffectDriver : MonoBehaviour
	{
		public KeyValueObject keyValueObject;
		public string boolKey = "animateLoad";
		public string requiredBoolKey;
		public bool requiredBoolExpectedValue = true;
		public GameObject sampleRoot;
		public string effectObjectName;
		public string[] effectObjectNames;
		public bool invert;
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
		public bool debugLogging;

		private readonly List<ParticleSystem> _particleSystems = new List<ParticleSystem>();
		private readonly List<GameObject> _streamChunkObjects = new List<GameObject>();
		private readonly List<IDisposable> _observers = new List<IDisposable>();
		private Material _createdMaterial;
		private Material _createdSolidMaterial;
		private Material _createdChunkMaterial;
		private GameObject _root;
		private GameObject _createdParticleObject;
		private GameObject _streamObject;
		private GameObject _streamMeshObject;
		private GameObject _runtimeFlowOriginObject;
		private GameObject _debugOriginObject;
		private LineRenderer _streamRenderer;
		private MeshRenderer _streamMeshRenderer;
		private Transform _streamAnchor;
		private ServiceFacilityFlowOrigin _selectedFlowOrigin;
		private Transform _selectedFlowRoot;
		private KeyValueObject _observedKeyValueObject;
		private string _observedBoolKey;
		private string _observedRequiredBoolKey;
		private bool _usingFallbackParent;
		private bool _loggedMissingEffect;
		private bool _loggedMissingFlowOriginFollow;
		private bool _loggedBlockedByFallbackParent;
		private bool _lastShouldPlay;
		private bool _hasLastShouldPlay;
		private bool _disabledAfterError;
		private bool _streamChunksActive;
		private float _streamChunkPhase;
		private float _nextParentRetryTime;
		private float _nextFlowOriginFollowRetryTime;
		private float _nextResolveRetryTime;

		private void OnEnable()
		{
			ResolveParticleSystems();
			EnsureObservers();
			SafeApplyPlaybackState();
		}

		private void OnDisable()
		{
			DisposeObservers();
			StopAll(clearOnStop);
		}

		private void OnDestroy()
		{
			if (_createdMaterial != null)
			{
				Destroy(_createdMaterial);
				_createdMaterial = null;
			}
			if (_createdSolidMaterial != null)
			{
				Destroy(_createdSolidMaterial);
				_createdSolidMaterial = null;
			}
			if (_createdChunkMaterial != null)
			{
				Destroy(_createdChunkMaterial);
				_createdChunkMaterial = null;
			}
			DestroyStreamChunks();
			if (_runtimeFlowOriginObject != null)
			{
				Destroy(_runtimeFlowOriginObject);
				_runtimeFlowOriginObject = null;
			}
		}

		public void RefreshBinding(GameObject root)
		{
			if (root != null)
			{
				sampleRoot = root;
			}

			EnsureObservers();
			if (NeedsResolve())
			{
				ResolveParticleSystems();
			}
			else
			{
				RetryFlowOriginFollowBinding();
				RetryParentBinding();
			}
			SafeApplyPlaybackState();
		}

		private void LateUpdate()
		{
			if (NeedsResolve() && Time.unscaledTime >= _nextResolveRetryTime)
			{
				_nextResolveRetryTime = Time.unscaledTime + 1f;
				ResolveParticleSystems();
				SafeApplyPlaybackState();
			}
			RetryFlowOriginFollowBinding();
			RetryParentBinding();
			if (_streamRenderer != null && _streamRenderer.enabled)
			{
				UpdateStreamPositions();
			}
			if (_streamChunksActive)
			{
				float visualSpeed = streamAnimationSpeed > 0f ? streamAnimationSpeed : startSpeed;
				_streamChunkPhase = Mathf.Repeat(_streamChunkPhase + Time.deltaTime * Mathf.Max(0.05f, visualSpeed), 1f);
				UpdateStreamChunkPositions();
			}
		}

		private void PropertyChanged(Value value)
		{
			SafeApplyPlaybackState();
		}

		private void SafeApplyPlaybackState()
		{
			if (_disabledAfterError)
			{
				return;
			}
			try
			{
				ApplyPlaybackState();
			}
			catch (Exception ex)
			{
				_disabledAfterError = true;
				StopAll(true);
				Main.Warn("[ServiceFacility][Loader] particle effect disabled after exception on " +
					name + ": " + ex.GetType().Name + " - " + ex.Message);
			}
		}

		private bool ShouldPlay()
		{
			if (keyValueObject == null || string.IsNullOrEmpty(boolKey))
			{
				return false;
			}
			bool value = keyValueObject[boolKey].BoolValue;
			if (invert)
			{
				value = !value;
			}
			if (!value)
			{
				return false;
			}
			if (string.IsNullOrEmpty(requiredBoolKey))
			{
				return true;
			}
			return keyValueObject[requiredBoolKey].BoolValue == requiredBoolExpectedValue;
		}

		private void ResolveParticleSystems()
		{
			_particleSystems.Clear();
			GameObject root = sampleRoot != null ? sampleRoot : gameObject;
			_root = root;
			_usingFallbackParent = false;
			foreach (string candidateName in CandidateNames())
			{
				Transform match = FindChildByName(root.transform, candidateName);
				if (match == null)
				{
					continue;
				}
				AddParticleSystems(match.gameObject);
			}
			if (_createdParticleObject != null)
			{
				bool usedFallback;
				Transform parent = ResolveParent(root, out usedFallback);
				_usingFallbackParent |= usedFallback;
				_createdParticleObject.transform.SetParent(parent, false);
				ApplyLayerFromParent(_createdParticleObject, parent);
				_createdParticleObject.transform.localPosition = LocalPositionForParent(parent);
				_createdParticleObject.transform.localRotation = Quaternion.Euler(localEuler);
				_createdParticleObject.transform.localScale = localScale == Vector3.zero ? Vector3.one : localScale;
				AddParticleSystems(_createdParticleObject);
			}

			if (createIfMissing && ShouldUseChunkStream())
			{
				EnsureChunkStream(root);
			}
			if (_particleSystems.Count == 0 && createIfMissing && !ShouldUseChunkStream())
			{
				ParticleSystem created = CreateParticleSystem(root);
				if (created != null)
				{
					_particleSystems.Add(created);
				}
			}
			if (ShouldDrawSolidStream())
			{
				EnsureVisibleStream(root);
			}

			for (int i = 0; i < _particleSystems.Count; i++)
			{
				ConfigureParticleSystem(_particleSystems[i]);
			}

			if (_particleSystems.Count == 0 && debugLogging && !_loggedMissingEffect)
			{
				_loggedMissingEffect = true;
				Main.Warn("[ServiceFacility][Loader] particle effect '" + EffectDescription + "' was not found under " + root.name);
			}
		}

		private bool NeedsResolve()
		{
			GameObject root = sampleRoot != null ? sampleRoot : gameObject;
			if (_root != root)
			{
				return true;
			}
			if (ShouldDrawSolidStream() && (_streamObject == null || _streamRenderer == null))
			{
				return true;
			}
			if (ShouldDrawSolidStream() && (_streamMeshObject == null || _streamMeshRenderer == null))
			{
				return true;
			}
			if (ShouldUseChunkStream() && _streamChunkObjects.Count == 0)
			{
				return true;
			}
			if (createIfMissing && !ShouldUseChunkStream() && _createdParticleObject == null && _particleSystems.Count == 0)
			{
				return true;
			}
			for (int i = 0; i < _particleSystems.Count; i++)
			{
				if (_particleSystems[i] == null)
				{
					return true;
				}
			}
			return false;
		}

		private void ApplyPlaybackState()
		{
			bool shouldPlay = ShouldPlay();
			LogPlaybackStateIfChanged(shouldPlay);
			if (shouldPlay)
			{
				PlayAll();
			}
			else
			{
				StopAll(clearOnStop);
			}
		}

		private void EnsureObservers()
		{
			if (!isActiveAndEnabled)
			{
				return;
			}

			string primaryKey = boolKey ?? "";
			string secondaryKey = "";
			if (!string.IsNullOrEmpty(requiredBoolKey) && !string.Equals(requiredBoolKey, primaryKey, StringComparison.Ordinal))
			{
				secondaryKey = requiredBoolKey;
			}

			if (_observedKeyValueObject == keyValueObject &&
				string.Equals(_observedBoolKey, primaryKey, StringComparison.Ordinal) &&
				string.Equals(_observedRequiredBoolKey, secondaryKey, StringComparison.Ordinal))
			{
				return;
			}

			DisposeObservers();
			if (keyValueObject == null || string.IsNullOrEmpty(primaryKey))
			{
				return;
			}

			_observedKeyValueObject = keyValueObject;
			_observedBoolKey = primaryKey;
			_observedRequiredBoolKey = secondaryKey;
			_observers.Add(keyValueObject.Observe(primaryKey, PropertyChanged, true));
			if (!string.IsNullOrEmpty(secondaryKey))
			{
				_observers.Add(keyValueObject.Observe(secondaryKey, PropertyChanged, true));
			}
		}

		private void DisposeObservers()
		{
			for (int i = 0; i < _observers.Count; i++)
			{
				if (_observers[i] != null)
				{
					_observers[i].Dispose();
				}
			}
			_observers.Clear();
			_observedKeyValueObject = null;
			_observedBoolKey = null;
			_observedRequiredBoolKey = null;
		}

		private IEnumerable<string> CandidateNames()
		{
			if (!string.IsNullOrWhiteSpace(effectObjectName))
			{
				yield return effectObjectName;
			}
			if (effectObjectNames == null)
			{
				yield break;
			}
			for (int i = 0; i < effectObjectNames.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(effectObjectNames[i]))
				{
					yield return effectObjectNames[i];
				}
			}
		}

		private void AddParticleSystems(GameObject root)
		{
			ParticleSystem direct = root.GetComponent<ParticleSystem>();
			if (direct != null)
			{
				_particleSystems.Add(direct);
			}
			ParticleSystem[] children = root.GetComponentsInChildren<ParticleSystem>(true);
			for (int i = 0; i < children.Length; i++)
			{
				ParticleSystem particleSystem = children[i];
				if (particleSystem != null && !_particleSystems.Contains(particleSystem))
				{
					_particleSystems.Add(particleSystem);
				}
			}
		}

		private ParticleSystem CreateParticleSystem(GameObject root)
		{
			bool usedFallback;
			Transform parent = ResolveParent(root, out usedFallback);
			_usingFallbackParent |= usedFallback;

			if (_createdParticleObject == null)
			{
				_createdParticleObject = new GameObject("Toolshed Oil Flow");
			}
			_createdParticleObject.transform.SetParent(parent, false);
			ApplyLayerFromParent(_createdParticleObject, parent);
			_createdParticleObject.transform.localPosition = LocalPositionForParent(parent);
			_createdParticleObject.transform.localRotation = Quaternion.Euler(localEuler);
			_createdParticleObject.transform.localScale = localScale == Vector3.zero ? Vector3.one : localScale;
			EnsureDebugOriginMarker(_createdParticleObject.transform);
			return _createdParticleObject.GetComponent<ParticleSystem>() ?? _createdParticleObject.AddComponent<ParticleSystem>();
		}

		private void EnsureVisibleStream(GameObject root)
		{
			bool usedFallback;
			Transform parent = ResolveParent(root, out usedFallback);
			_usingFallbackParent |= usedFallback;

			bool created = false;
			if (_streamObject == null)
			{
				_streamObject = new GameObject("Toolshed Oil Stream");
				created = true;
			}
			_streamObject.transform.SetParent(parent, false);
			ApplyLayerFromParent(_streamObject, parent);
			_streamObject.transform.localPosition = LocalPositionForParent(parent);
			_streamObject.transform.localRotation = Quaternion.Euler(localEuler);
			_streamObject.transform.localScale = Vector3.one;
			_streamAnchor = _streamObject.transform;
			EnsureDebugOriginMarker(_streamObject.transform);

			_streamRenderer = _streamObject.GetComponent<LineRenderer>() ?? _streamObject.AddComponent<LineRenderer>();
			_streamRenderer.positionCount = 2;
			_streamRenderer.useWorldSpace = streamUsesWorldDown;
			_streamRenderer.startWidth = Mathf.Max(0.001f, streamWidth);
			_streamRenderer.endWidth = Mathf.Max(0.001f, streamWidth * 0.8f);
			_streamRenderer.numCapVertices = 6;
			_streamRenderer.numCornerVertices = 2;
			_streamRenderer.textureMode = LineTextureMode.Stretch;
			_streamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			_streamRenderer.receiveShadows = false;
			_streamRenderer.material = CreateParticleMaterial();
			_streamRenderer.startColor = streamColor;
			_streamRenderer.endColor = streamColor;
			UpdateStreamPositions();
			_streamRenderer.enabled = false;
			EnsureVisibleStreamMesh(_streamAnchor != null ? _streamAnchor : parent);

			if (debugLogging && created)
			{
				Main.Log("[ServiceFacility][Loader] visible stream created anchor=" + parent.name +
					", worldDown=" + streamUsesWorldDown +
					", localPosition=" + localPosition +
					", width=" + streamWidth.ToString("0.###") +
					", length=" + streamLength.ToString("0.###"));
			}
		}

		private void EnsureVisibleStreamMesh(Transform parent)
		{
			if (parent == null)
			{
				return;
			}
			if (_streamMeshObject == null)
			{
				_streamMeshObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
				_streamMeshObject.name = "Toolshed Oil Stream Mesh";
				Collider collider = _streamMeshObject.GetComponent<Collider>();
				if (collider != null)
				{
					Destroy(collider);
				}
			}
			_streamMeshObject.transform.SetParent(parent, false);
			ApplyLayerFromParent(_streamMeshObject, parent);
			_streamMeshRenderer = _streamMeshObject.GetComponent<MeshRenderer>();
			if (_streamMeshRenderer != null)
			{
				_streamMeshRenderer.material = CreateSolidStreamMaterial();
				_streamMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				_streamMeshRenderer.receiveShadows = false;
			}
			_streamMeshObject.SetActive(false);
			UpdateStreamPositions();
		}

		private void EnsureChunkStream(GameObject root)
		{
			bool usedFallback;
			Transform parent = ResolveParent(root, out usedFallback);
			_usingFallbackParent |= usedFallback;
			if (parent == null)
			{
				return;
			}

			_streamAnchor = parent;
			EnsureDebugOriginMarker(parent);
			if (_streamChunkObjects.Count == 0)
			{
				for (int i = 0; i < 24; i++)
				{
					GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
					chunk.name = "Toolshed Coal Chunk";
					chunk.transform.SetParent(parent, false);
					ApplyLayerFromParent(chunk, parent);
					Collider collider = chunk.GetComponent<Collider>();
					if (collider != null)
					{
						Destroy(collider);
					}
					Renderer renderer = chunk.GetComponent<Renderer>();
					if (renderer != null)
					{
						renderer.material = CreateChunkMaterial();
						renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
						renderer.receiveShadows = true;
						renderer.allowOcclusionWhenDynamic = false;
					}
					chunk.SetActive(false);
					_streamChunkObjects.Add(chunk);
				}
				if (debugLogging)
				{
					Main.Log("[ServiceFacility][Loader] coal chunk stream created anchor=" + parent.name +
						", parentLayer=" + parent.gameObject.layer +
						", chunkLayer=" + _streamChunkObjects[0].layer +
						", start=" + streamLocalStart +
						", end=" + EffectiveLocalStreamEnd() +
						", drop=" + streamLength.ToString("0.###") +
						", width=" + streamWidth.ToString("0.###"));
				}
			}
			UpdateStreamChunkPositions();
		}

		private void UpdateStreamChunkPositions()
		{
			if (_streamChunkObjects.Count == 0)
			{
				return;
			}

			Vector3 start = streamLocalStart;
			Vector3 chuteEnd = EffectiveLocalStreamEnd();
			Vector3 dropEnd = chuteEnd + EffectiveLocalWorldDown() * Mathf.Max(0f, streamLength);
			float chuteLength = Vector3.Distance(start, chuteEnd);
			float dropLength = Vector3.Distance(chuteEnd, dropEnd);
			float totalLength = chuteLength + dropLength;
			if (totalLength <= 0.01f)
			{
				chuteEnd = start + Vector3.down * Mathf.Max(0.01f, streamLength);
				chuteLength = Vector3.Distance(start, chuteEnd);
				totalLength = chuteLength;
				dropLength = 0f;
			}
			float baseSize = Mathf.Max(0.015f, streamWidth * 0.45f);
			for (int i = 0; i < _streamChunkObjects.Count; i++)
			{
				GameObject chunk = _streamChunkObjects[i];
				if (chunk == null)
				{
					continue;
				}
				float t = Mathf.Repeat(_streamChunkPhase + (float)i / _streamChunkObjects.Count, 1f);
				float distance = t * totalLength;
				Vector3 segmentStart = start;
				Vector3 segmentEnd = chuteEnd;
				float segmentLength = chuteLength;
				if (dropLength > 0.01f && distance > chuteLength)
				{
					segmentStart = chuteEnd;
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
				Vector3 position = Vector3.Lerp(segmentStart, segmentEnd, segmentT) +
					side * jitterA * streamWidth * 0.35f +
					up * jitterB * streamWidth * 0.25f;
				float scale = baseSize * (0.65f + ((i * 17) % 7) * 0.08f);
				chunk.transform.localPosition = position;
				chunk.transform.localRotation = Quaternion.LookRotation(direction, up) *
					Quaternion.Euler(i * 29f, i * 47f, i * 71f);
				chunk.transform.localScale = new Vector3(scale * 1.25f, scale * 0.75f, scale);
			}
		}

		private void SetChunkStreamActive(bool active)
		{
			bool wasActive = _streamChunksActive;
			_streamChunksActive = active;
			for (int i = 0; i < _streamChunkObjects.Count; i++)
			{
				if (_streamChunkObjects[i] != null)
				{
					_streamChunkObjects[i].SetActive(active);
				}
			}
			if (active)
			{
				UpdateStreamChunkPositions();
			}
			if (debugLogging && wasActive != active)
			{
				Main.Log("[ServiceFacility][Loader] coal chunk stream " + (active ? "shown" : "hidden") +
					" " + ChunkDebugText());
			}
		}

		private void DestroyStreamChunks()
		{
			for (int i = 0; i < _streamChunkObjects.Count; i++)
			{
				if (_streamChunkObjects[i] != null)
				{
					Destroy(_streamChunkObjects[i]);
				}
			}
			_streamChunkObjects.Clear();
			_streamChunksActive = false;
		}

		private void ConfigureParticleSystem(ParticleSystem particleSystem)
		{
			if (particleSystem == null)
			{
				return;
			}

			ParticleSystem.MainModule main = particleSystem.main;
			main.loop = true;
			main.playOnAwake = false;
			main.startLifetime = Mathf.Max(0.05f, startLifetime);
			main.startSpeed = Mathf.Max(0f, startSpeed);
			main.startSize = Mathf.Max(0.001f, startSize);
			main.gravityModifier = gravityModifier;
			if (overrideStartColor)
			{
				main.startColor = new ParticleSystem.MinMaxGradient(startColor);
			}

			ParticleSystem.EmissionModule emission = particleSystem.emission;
			emission.enabled = true;
			emission.rateOverTime = new ParticleSystem.MinMaxCurve(Mathf.Max(0f, emissionRate));

			ParticleSystem.ShapeModule shape = particleSystem.shape;
			shape.enabled = true;
			shape.shapeType = ParticleSystemShapeType.Cone;
			shape.angle = 3f;
			shape.radius = 0.025f;

			ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
			if (renderer != null)
			{
				renderer.renderMode = ParticleSystemRenderMode.Billboard;
				renderer.material = CreateParticleMaterial();
			}

			particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
		}

		private void EnsureDebugOriginMarker(Transform parent)
		{
			if (!debugOriginMarker || parent == null || _debugOriginObject != null)
			{
				return;
			}

			_debugOriginObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			_debugOriginObject.name = "Toolshed Oil Flow Debug Origin";
			_debugOriginObject.transform.SetParent(parent, false);
			ApplyLayerFromParent(_debugOriginObject, parent);
			_debugOriginObject.transform.localPosition = Vector3.zero;
			_debugOriginObject.transform.localRotation = Quaternion.identity;
			_debugOriginObject.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
			Collider collider = _debugOriginObject.GetComponent<Collider>();
			if (collider != null)
			{
				Destroy(collider);
			}
			Renderer renderer = _debugOriginObject.GetComponent<Renderer>();
			if (renderer != null)
			{
				renderer.material.color = Color.yellow;
			}
		}

		private Material CreateParticleMaterial()
		{
			if (_createdMaterial != null)
			{
				return _createdMaterial;
			}

			Shader shader = Shader.Find("Sprites/Default");
			if (shader == null)
			{
				shader = Shader.Find("Unlit/Color");
			}
			if (shader == null)
			{
				shader = Shader.Find("Particles/Standard Unlit");
			}
			if (shader == null)
			{
				shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
			}
			if (shader != null)
			{
				_createdMaterial = new Material(shader);
			}
			else
			{
				return null;
			}
			_createdMaterial.color = overrideStartColor ? startColor : streamColor;
			SetMaterialColorIfPresent(_createdMaterial, "_BaseColor", overrideStartColor ? startColor : streamColor);
			SetMaterialColorIfPresent(_createdMaterial, "_Color", overrideStartColor ? startColor : streamColor);
			SetMaterialColorIfPresent(_createdMaterial, "_TintColor", overrideStartColor ? startColor : streamColor);
			return _createdMaterial;
		}

		private Material CreateSolidStreamMaterial()
		{
			if (_createdSolidMaterial != null)
			{
				return _createdSolidMaterial;
			}

			Shader shader = Shader.Find("Unlit/Color");
			if (shader == null)
			{
				shader = Shader.Find("Standard");
			}
			if (shader == null)
			{
				shader = Shader.Find("Sprites/Default");
			}
			if (shader == null)
			{
				return CreateParticleMaterial();
			}

			_createdSolidMaterial = new Material(shader);
			_createdSolidMaterial.color = streamColor;
			SetMaterialColorIfPresent(_createdSolidMaterial, "_BaseColor", streamColor);
			SetMaterialColorIfPresent(_createdSolidMaterial, "_Color", streamColor);
			SetMaterialColorIfPresent(_createdSolidMaterial, "_TintColor", streamColor);
			return _createdSolidMaterial;
		}

		private Material CreateChunkMaterial()
		{
			if (_createdChunkMaterial != null)
			{
				return _createdChunkMaterial;
			}

			Shader shader = Shader.Find("Unlit/Color");
			if (shader == null)
			{
				shader = Shader.Find("Sprites/Default");
			}
			if (shader == null)
			{
				shader = Shader.Find("Standard");
			}
			if (shader == null)
			{
				return CreateParticleMaterial();
			}

			Color color = VisibleChunkColor(overrideStartColor ? startColor : streamColor);
			_createdChunkMaterial = new Material(shader);
			_createdChunkMaterial.color = color;
			SetMaterialColorIfPresent(_createdChunkMaterial, "_BaseColor", color);
			SetMaterialColorIfPresent(_createdChunkMaterial, "_Color", color);
			SetMaterialColorIfPresent(_createdChunkMaterial, "_TintColor", color);
			return _createdChunkMaterial;
		}

		private static Color VisibleChunkColor(Color color)
		{
			return new Color(
				Mathf.Max(color.r, 0.09f),
				Mathf.Max(color.g, 0.08f),
				Mathf.Max(color.b, 0.065f),
				Mathf.Max(color.a, 1f));
		}

		private bool ShouldUseChunkStream()
		{
			return createIfMissing &&
				!streamUsesWorldDown &&
				(streamLocalStart != Vector3.zero || streamLocalEnd != Vector3.zero);
		}

		private bool ShouldDrawSolidStream()
		{
			return createVisibleStream && !ShouldUseChunkStream();
		}

		private Transform ResolveParent(GameObject root, out bool usedFallback)
		{
			Transform fallback = root != null ? root.transform : transform;
			ServiceFacilityFlowOrigin flowOrigin = FindFlowOrigin(fallback);
			if (flowOrigin != null)
			{
				AttachFlowOriginToFollowTransform(flowOrigin, fallback);
				_selectedFlowOrigin = flowOrigin;
				_selectedFlowRoot = fallback;
				usedFallback = false;
				if (debugLogging)
				{
					Main.Log("[ServiceFacility][Loader] particle flow origin component selected " + flowOrigin.name +
						" id=" + (flowOrigin.originId ?? "") +
						" under " + fallback.name);
				}
				return flowOrigin.transform;
			}

			foreach (string candidateName in ParentCandidateNames())
			{
				Transform match = FindChildByName(fallback, candidateName);
				if (match != null)
				{
					usedFallback = false;
					if (debugLogging)
					{
						Main.Log("[ServiceFacility][Loader] particle parent using configured flow origin " +
							match.name + " under " + fallback.name + ".");
					}
					return match;
				}
			}

			Transform followParent = FindFlowOriginFollowTransform(fallback);
			if (followParent != null)
			{
				Transform runtimeOrigin = EnsureRuntimeFlowOrigin(followParent);
				usedFallback = false;
				if (debugLogging)
				{
					Main.Log("[ServiceFacility][Loader] particle parent using runtime flow origin under animated transform " +
						followParent.name + " because no configured flow origin marker was found.");
				}
				return runtimeOrigin;
			}

			if (debugLogging && (!string.IsNullOrWhiteSpace(parentTransformName) || parentTransformNames != null && parentTransformNames.Length > 0))
			{
				Main.Warn("[ServiceFacility][Loader] particle parent '" + ParentDescription + "' was not found under " + fallback.name + "; using root.");
			}
			usedFallback = true;
			return fallback;
		}

		private void RetryParentBinding()
		{
			if (!_usingFallbackParent || _root == null || Time.unscaledTime < _nextParentRetryTime)
			{
				return;
			}
			_nextParentRetryTime = Time.unscaledTime + 1f;
			ServiceFacilityFlowOrigin flowOrigin = FindFlowOrigin(_root.transform);
			if (flowOrigin != null)
			{
				AttachFlowOriginToFollowTransform(flowOrigin, _root.transform);
				_selectedFlowOrigin = flowOrigin;
				_selectedFlowRoot = _root.transform;
				RebindCreatedEffects(flowOrigin.transform);
				_usingFallbackParent = false;
				if (debugLogging)
				{
					Main.Log("[ServiceFacility][Loader] particle parent rebound to flow origin component " +
						flowOrigin.name + " id=" + (flowOrigin.originId ?? "") + " under " + _root.name);
				}
				if (ShouldPlay())
				{
					PlayAll();
				}
				return;
			}
			foreach (string candidateName in ParentCandidateNames())
			{
				Transform match = FindChildByName(_root.transform, candidateName);
				if (match == null)
				{
					continue;
				}
				RebindCreatedEffects(match);
				_usingFallbackParent = false;
				if (debugLogging)
				{
					Main.Log("[ServiceFacility][Loader] particle parent rebound to " + match.name + " under " + _root.name);
				}
				if (ShouldPlay())
				{
					PlayAll();
				}
				return;
			}
			Transform followParent = FindFlowOriginFollowTransform(_root.transform);
			if (followParent != null)
			{
				Transform runtimeOrigin = EnsureRuntimeFlowOrigin(followParent);
				RebindCreatedEffects(runtimeOrigin);
				_usingFallbackParent = false;
				if (debugLogging)
				{
					Main.Log("[ServiceFacility][Loader] particle parent rebound to runtime flow origin under animated transform " + followParent.name + " under " + _root.name);
				}
				if (ShouldPlay())
				{
					PlayAll();
				}
				return;
			}
		}

		private void RetryFlowOriginFollowBinding()
		{
			if (_selectedFlowOrigin == null || _selectedFlowRoot == null || !HasFlowOriginFollowCandidates() ||
				Time.unscaledTime < _nextFlowOriginFollowRetryTime)
			{
				return;
			}
			_nextFlowOriginFollowRetryTime = Time.unscaledTime + 1f;
			AttachFlowOriginToFollowTransform(_selectedFlowOrigin, _selectedFlowRoot);
		}

		private ServiceFacilityFlowOrigin FindFlowOrigin(Transform root)
		{
			if (root == null)
			{
				return null;
			}
			ServiceFacilityFlowOrigin[] origins = root.GetComponentsInChildren<ServiceFacilityFlowOrigin>(true);
			if (origins == null || origins.Length == 0)
			{
				return null;
			}
			for (int i = 0; i < origins.Length; i++)
			{
				ServiceFacilityFlowOrigin origin = origins[i];
				if (origin != null &&
					MatchesFlowOriginId(origin.originId) &&
					!string.IsNullOrWhiteSpace(flowOriginId) &&
					string.Equals(origin.transform.name, flowOriginId, StringComparison.OrdinalIgnoreCase))
				{
					return origin;
				}
			}
			for (int i = 0; i < origins.Length; i++)
			{
				ServiceFacilityFlowOrigin origin = origins[i];
				if (origin != null && MatchesFlowOriginId(origin.originId))
				{
					return origin;
				}
			}
			return string.IsNullOrWhiteSpace(flowOriginId) ? origins[0] : null;
		}

		private void AttachFlowOriginToFollowTransform(ServiceFacilityFlowOrigin flowOrigin, Transform root)
		{
			if (flowOrigin == null || root == null || !HasFlowOriginFollowCandidates())
			{
				return;
			}

			Transform followTransform = FindFlowOriginFollowTransform(root);
			if (followTransform == null)
			{
				if (debugLogging && !_loggedMissingFlowOriginFollow)
				{
					_loggedMissingFlowOriginFollow = true;
					Main.Warn("[ServiceFacility][Loader] flow origin follow transform '" +
						FlowOriginFollowDescription + "' was not found under " + root.name + ".");
				}
				return;
			}
			if (flowOrigin.transform.parent == followTransform)
			{
				return;
			}

			flowOrigin.transform.SetParent(followTransform, flowOriginFollowPreserveWorldPosition);
			if (debugLogging)
			{
				Main.Log("[ServiceFacility][Loader] flow origin " + flowOrigin.name +
					" attached to animated transform " + followTransform.name +
					", preserveWorld=" + flowOriginFollowPreserveWorldPosition +
					", localPosition=" + flowOrigin.transform.localPosition +
					", localEuler=" + flowOrigin.transform.localEulerAngles);
			}
			UpdateStreamPositions();
		}

		private Transform FindFlowOriginFollowTransform(Transform root)
		{
			if (root == null || !HasFlowOriginFollowCandidates())
			{
				return null;
			}
			foreach (string candidateName in FlowOriginFollowCandidateNames())
			{
				Transform followTransform = FindChildByName(root, candidateName);
				if (followTransform != null)
				{
					return followTransform;
				}
			}
			return null;
		}

		private bool MatchesFlowOriginId(string candidate)
		{
			return string.IsNullOrWhiteSpace(flowOriginId) ||
				string.Equals(candidate ?? "", flowOriginId, StringComparison.OrdinalIgnoreCase);
		}

		private void RebindCreatedEffects(Transform parent)
		{
			if (parent == null)
			{
				return;
			}
			if (_createdParticleObject != null)
			{
				_createdParticleObject.transform.SetParent(parent, false);
				ApplyLayerFromParent(_createdParticleObject, parent);
				_createdParticleObject.transform.localPosition = LocalPositionForParent(parent);
				_createdParticleObject.transform.localRotation = Quaternion.Euler(localEuler);
				_createdParticleObject.transform.localScale = localScale == Vector3.zero ? Vector3.one : localScale;
			}
			if (_streamObject != null)
			{
				_streamObject.transform.SetParent(parent, false);
				ApplyLayerFromParent(_streamObject, parent);
				_streamObject.transform.localPosition = LocalPositionForParent(parent);
				_streamObject.transform.localRotation = Quaternion.Euler(localEuler);
				_streamObject.transform.localScale = Vector3.one;
				_streamAnchor = _streamObject.transform;
			}
			else
			{
				_streamAnchor = parent;
			}
			if (_streamMeshObject != null)
			{
				Transform streamParent = _streamAnchor != null ? _streamAnchor : parent;
				_streamMeshObject.transform.SetParent(streamParent, false);
				ApplyLayerFromParent(_streamMeshObject, streamParent);
			}
			for (int i = 0; i < _streamChunkObjects.Count; i++)
			{
				GameObject chunk = _streamChunkObjects[i];
				if (chunk != null)
				{
					chunk.transform.SetParent(parent, false);
					ApplyLayerFromParent(chunk, parent);
				}
			}
			UpdateStreamPositions();
			UpdateStreamChunkPositions();
		}

		private Vector3 LocalPositionForParent(Transform parent)
		{
			return parent != null && ShouldUseZeroLocalOffset(parent)
				? Vector3.zero
				: localPosition;
		}

		private bool ShouldUseZeroLocalOffset(Transform parent)
		{
			if (parent == null)
			{
				return false;
			}
			if (parent.GetComponent<ServiceFacilityFlowOrigin>() != null)
			{
				return true;
			}
			if (_runtimeFlowOriginObject != null && parent == _runtimeFlowOriginObject.transform)
			{
				return true;
			}
			foreach (string candidateName in ParentCandidateNames())
			{
				if (string.Equals(parent.name, candidateName, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}

		private Transform EnsureRuntimeFlowOrigin(Transform followParent)
		{
			if (followParent == null)
			{
				return null;
			}
			if (_runtimeFlowOriginObject == null)
			{
				_runtimeFlowOriginObject = new GameObject("Toolshed Runtime Flow Origin");
			}
			_runtimeFlowOriginObject.transform.SetParent(followParent, false);
			ApplyLayerFromParent(_runtimeFlowOriginObject, followParent);
			_runtimeFlowOriginObject.transform.localPosition = localPosition;
			_runtimeFlowOriginObject.transform.localRotation = Quaternion.identity;
			_runtimeFlowOriginObject.transform.localScale = Vector3.one;
			return _runtimeFlowOriginObject.transform;
		}

		private static void ApplyLayerFromParent(GameObject child, Transform parent)
		{
			if (child == null || parent == null)
			{
				return;
			}
			int parentLayer = parent.gameObject.layer;
			child.layer = parentLayer == global::ObjectPicker.LayerClickable ? 0 : parentLayer;
		}

		private IEnumerable<string> ParentCandidateNames()
		{
			if (!string.IsNullOrWhiteSpace(parentTransformName))
			{
				yield return parentTransformName;
			}
			if (parentTransformNames == null)
			{
				yield break;
			}
			for (int i = 0; i < parentTransformNames.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(parentTransformNames[i]) && !string.Equals(parentTransformNames[i], parentTransformName, StringComparison.OrdinalIgnoreCase))
				{
					yield return parentTransformNames[i];
				}
			}
		}

		private bool HasFlowOriginFollowCandidates()
		{
			if (!string.IsNullOrWhiteSpace(flowOriginFollowTransformName))
			{
				return true;
			}
			if (flowOriginFollowTransformNames == null)
			{
				return false;
			}
			for (int i = 0; i < flowOriginFollowTransformNames.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(flowOriginFollowTransformNames[i]))
				{
					return true;
				}
			}
			return false;
		}

		private IEnumerable<string> FlowOriginFollowCandidateNames()
		{
			if (!string.IsNullOrWhiteSpace(flowOriginFollowTransformName))
			{
				yield return flowOriginFollowTransformName;
			}
			if (flowOriginFollowTransformNames == null)
			{
				yield break;
			}
			for (int i = 0; i < flowOriginFollowTransformNames.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(flowOriginFollowTransformNames[i]) &&
					!string.Equals(flowOriginFollowTransformNames[i], flowOriginFollowTransformName, StringComparison.OrdinalIgnoreCase))
				{
					yield return flowOriginFollowTransformNames[i];
				}
			}
		}

		private static void SetMaterialColorIfPresent(Material material, string propertyName, Color color)
		{
			if (material != null && material.HasProperty(propertyName))
			{
				material.SetColor(propertyName, color);
			}
		}

		private void UpdateStreamPositions()
		{
			if (_streamRenderer == null && _streamMeshObject == null)
			{
				return;
			}
			Vector3 start;
			Vector3 end;
			if (streamUsesWorldDown)
			{
				Transform anchor = _streamAnchor != null ? _streamAnchor : transform;
				start = anchor.TransformPoint(streamLocalStart);
				end = start + Vector3.down * Mathf.Max(0.01f, streamLength);
			}
			else
			{
				start = streamLocalStart;
				end = EffectiveLocalStreamEnd();
			}

			if (_streamRenderer != null)
			{
				_streamRenderer.SetPosition(0, start);
				_streamRenderer.SetPosition(1, end);
			}

			UpdateStreamMesh(start, end);
		}

		private void UpdateStreamMesh(Vector3 start, Vector3 end)
		{
			if (_streamMeshObject == null)
			{
				return;
			}
			Vector3 direction = end - start;
			float length = Mathf.Max(0.01f, direction.magnitude);
			if (length <= 0.01f)
			{
				direction = Vector3.down;
			}
			else
			{
				direction /= length;
			}

			if (streamUsesWorldDown)
			{
				_streamMeshObject.transform.position = start + direction * (length * 0.5f);
				_streamMeshObject.transform.rotation = Quaternion.FromToRotation(Vector3.up, direction);
			}
			else
			{
				_streamMeshObject.transform.localPosition = start + direction * (length * 0.5f);
				_streamMeshObject.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
			}
			_streamMeshObject.transform.localScale = new Vector3(
				Mathf.Max(0.001f, streamWidth),
				length * 0.5f,
				Mathf.Max(0.001f, streamWidth));
		}

		private Vector3 EffectiveLocalStreamEnd()
		{
			return streamLocalStart == Vector3.zero && streamLocalEnd == Vector3.zero
				? new Vector3(0f, -Mathf.Max(0.01f, streamLength), 0f)
				: streamLocalEnd;
		}

		private Vector3 EffectiveLocalWorldDown()
		{
			Transform anchor = _streamAnchor != null ? _streamAnchor : transform;
			if (anchor == null)
			{
				return Vector3.down;
			}
			Vector3 localDown = anchor.InverseTransformDirection(Vector3.down);
			return localDown.sqrMagnitude <= 0.0001f ? Vector3.down : localDown.normalized;
		}

		private void LogPlaybackStateIfChanged(bool shouldPlay)
		{
			if (!debugLogging)
			{
				return;
			}
			if (_hasLastShouldPlay && _lastShouldPlay == shouldPlay)
			{
				return;
			}

			_hasLastShouldPlay = true;
			_lastShouldPlay = shouldPlay;
			Main.Log("[ServiceFacility][Loader] particle state key=" + boolKey + "=" + BoolValueText(boolKey) +
				", required=" + (string.IsNullOrEmpty(requiredBoolKey) ? "<none>" : requiredBoolKey + "=" + BoolValueText(requiredBoolKey)) +
				", play=" + shouldPlay +
				", fallbackParent=" + _usingFallbackParent +
				", anchor=" + (_streamAnchor != null ? _streamAnchor.name : "<none>") +
				", stream=" + (_streamRenderer != null ? "ready" : "missing") +
				", mesh=" + (_streamMeshObject != null ? "ready" : "missing") +
				", chunks=" + _streamChunkObjects.Count +
				", chunksActive=" + _streamChunksActive +
				", streamPosition=" + StreamPositionText());
		}

		private string StreamPositionText()
		{
			Transform anchor = _streamAnchor != null ? _streamAnchor : (_streamMeshObject != null ? _streamMeshObject.transform : null);
			return anchor != null ? anchor.position.ToString() : "<none>";
		}

		private string ChunkDebugText()
		{
			if (_streamChunkObjects.Count == 0 || _streamChunkObjects[0] == null)
			{
				return "chunks=0";
			}
			GameObject chunk = _streamChunkObjects[0];
			Renderer renderer = chunk.GetComponent<Renderer>();
			string shaderName = renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
				? renderer.sharedMaterial.shader.name
				: "<none>";
			return "chunks=" + _streamChunkObjects.Count +
				", firstActive=" + chunk.activeSelf +
				", firstLayer=" + chunk.layer +
				", firstWorld=" + chunk.transform.position +
				", firstLocal=" + chunk.transform.localPosition +
				", parentScale=" + (_streamAnchor != null ? _streamAnchor.lossyScale.ToString() : "<none>") +
				", renderer=" + (renderer != null && renderer.enabled ? "on" : "off") +
				", shader=" + shaderName;
		}

		private string BoolValueText(string key)
		{
			if (keyValueObject == null || string.IsNullOrEmpty(key))
			{
				return "<missing>";
			}
			return keyValueObject[key].BoolValue ? "true" : "false";
		}

		private void PlayAll()
		{
			if (requireParentTransform && _usingFallbackParent)
			{
				if (debugLogging && !_loggedBlockedByFallbackParent)
				{
					_loggedBlockedByFallbackParent = true;
					Main.Warn("[ServiceFacility][Loader] particle play blocked until a configured parent transform is available.");
				}
				return;
			}
			_loggedBlockedByFallbackParent = false;
			if (_streamRenderer != null)
			{
				UpdateStreamPositions();
				_streamRenderer.enabled = true;
			}
			if (_streamMeshObject != null)
			{
				UpdateStreamPositions();
				_streamMeshObject.SetActive(true);
			}
			if (_streamChunkObjects.Count > 0)
			{
				SetChunkStreamActive(true);
			}
			for (int i = 0; i < _particleSystems.Count; i++)
			{
				ParticleSystem particleSystem = _particleSystems[i];
				if (particleSystem != null && !particleSystem.isPlaying)
				{
					particleSystem.Play(true);
				}
			}
		}

		private void StopAll(bool clear)
		{
			if (_streamRenderer != null)
			{
				_streamRenderer.enabled = false;
			}
			if (_streamMeshObject != null)
			{
				_streamMeshObject.SetActive(false);
			}
			if (_streamChunkObjects.Count > 0)
			{
				SetChunkStreamActive(false);
			}
			ParticleSystemStopBehavior stopBehavior = clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting;
			for (int i = 0; i < _particleSystems.Count; i++)
			{
				ParticleSystem particleSystem = _particleSystems[i];
				if (particleSystem != null)
				{
					particleSystem.Stop(true, stopBehavior);
				}
			}
		}

		private static Transform FindChildByName(Transform root, string targetName)
		{
			if (root == null || string.IsNullOrWhiteSpace(targetName))
			{
				return null;
			}
			if (string.Equals(root.name, targetName, StringComparison.OrdinalIgnoreCase))
			{
				return root;
			}
			for (int i = 0; i < root.childCount; i++)
			{
				Transform match = FindChildByName(root.GetChild(i), targetName);
				if (match != null)
				{
					return match;
				}
			}
			return null;
		}

		private string EffectDescription
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(effectObjectName))
				{
					return effectObjectName;
				}
				return createIfMissing ? "runtime oil flow" : "<unnamed>";
			}
		}

		private string ParentDescription
		{
			get
			{
				List<string> names = new List<string>();
				foreach (string candidateName in ParentCandidateNames())
				{
					names.Add(candidateName);
				}
				return names.Count > 0 ? string.Join(", ", names.ToArray()) : "<none>";
			}
		}

		private string FlowOriginFollowDescription
		{
			get
			{
				List<string> names = new List<string>();
				foreach (string candidateName in FlowOriginFollowCandidateNames())
				{
					names.Add(candidateName);
				}
				return names.Count > 0 ? string.Join(", ", names.ToArray()) : "<none>";
			}
		}
	}
}
