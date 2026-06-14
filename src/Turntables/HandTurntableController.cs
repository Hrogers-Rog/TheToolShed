using System;
using System.Collections.Generic;
using System.Reflection;
using FUSE.Authoring.Data;
using Track;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toolshed.Turntables
{
    public sealed class HandTurntableController : MonoBehaviour, global::IPickable
    {
        private const float DefaultDegreesPerSecond = 18f;
        private const float MinDegreesPerSecond = 2f;
        private const float MaxDegreesPerSecond = 60f;
        private const float NodeMarkerRadius = 0.85f;
        private const float NodeMarkerHeight = 0.85f;
        private const float NodeMarkerYOffset = 1.15f;
        private static readonly FieldInfo NodesField = typeof(Turntable).GetField("nodes", BindingFlags.Instance | BindingFlags.NonPublic);

        private Turntable _turntable;
        private Transform _bridgeRoot;
        private object _definition;
        private Rect _windowRect = new Rect(120f, 120f, 280f, 190f);
        private bool _windowOpen;
        private float _speedDirection;
        private float _speedMultiplier = 1f;
        private float _controllerDegreesPerSecond = DefaultDegreesPerSecond;
        private float _speedDegreesPerSecond = DefaultDegreesPerSecond;
        private SphereCollider _interactionCollider;
        private bool _dragging;
        private int _dragEndOffset;
        private int? _dragTargetNodeIndex;
        private int? _dragStopIndex;
        private int? _targetStopIndex;
        private float _targetAngle;
        private bool _rotatingToTarget;
        private Transform _nodeHighlightRoot;
        private readonly List<GameObject> _nodeHighlights = new List<GameObject>();
        private readonly List<Renderer> _nodeHighlightRenderers = new List<Renderer>();
        private readonly List<Behaviour> _suppressedCameraBehaviours = new List<Behaviour>();
        private Material _nodeMaterial;
        private Material _nodeActiveMaterial;
        private bool _cameraSuppressed;

        public float MaxPickDistance => 250f;

        public int Priority => 20;

        public global::TooltipInfo TooltipInfo => new global::TooltipInfo("Hand Turntable", "Drag to line the bridge");

        public global::PickableActivationFilter ActivationFilter => global::PickableActivationFilter.PrimaryOnly;

        public void Configure(Turntable turntable, Transform bridgeRoot, FuseTurntable definition)
        {
            Configure(turntable, bridgeRoot, (object)definition);
        }

        public void Configure(Turntable turntable, Transform bridgeRoot, object definition)
        {
            _turntable = turntable;
            _bridgeRoot = bridgeRoot;
            _definition = definition;
            enabled = true;
            EnsureInteractionCollider();
            SyncBridgeVisual();
        }

        public void Activate(global::PickableActivateEvent evt)
        {
            if (evt.Activation == global::PickableActivation.Primary)
            {
                StartDragInteraction();
            }
        }

        public void Deactivate()
        {
            EndDragInteraction(true);
        }

        private void OnMouseDown()
        {
            if (!Main.Enabled)
            {
                return;
            }

            StartDragInteraction();
        }

        private void OnMouseDrag()
        {
            if (!Main.Enabled || !_dragging)
            {
                return;
            }

            UpdateDragTarget();
        }

        private void OnMouseUp()
        {
            EndDragInteraction(true);
        }

        private void Update()
        {
            if (!Main.Enabled || _turntable == null)
            {
                return;
            }

            if (_dragging)
            {
                EnsureNodeHighlights();
                UpdateDragTarget();
                SyncBridgeVisual();
                return;
            }

            if (_rotatingToTarget)
            {
                RotateTowardTarget();
            }
            else if (Mathf.Abs(_speedDirection) > 0.01f)
            {
                if (_turntable.TryGetCarBlockingMovement(out var car))
                {
                    _speedDirection = 0f;
                    Main.Warn($"Hand turntable '{_turntable.id}' refused to move because car '{car?.id ?? "unknown"}' is blocking the bridge.");
                    return;
                }

                var angle = Mathf.Repeat(
                    _turntable.Angle + _speedDirection * _speedDegreesPerSecond * Time.deltaTime,
                    360f);
                _turntable.SetAngle(angle);
                _turntable.UpdateSegmentIndex(true);
            }

            SyncBridgeVisual();
        }

        private void OnDisable()
        {
            _speedDirection = 0f;
            _rotatingToTarget = false;
            EndDragInteraction(false);
            SuppressCameraInput(false);
        }

        private void OnGUI()
        {
            if (!Main.Enabled || !_windowOpen || _turntable == null)
            {
                return;
            }

            _windowRect = GUILayout.Window(GetInstanceID(), _windowRect, DrawWindow, WindowTitle());
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.Label($"Angle: {_turntable.Angle:0.0} deg");
            GUILayout.Label(_turntable.StopIndex.HasValue
                ? $"Lined to stop {_turntable.StopIndex.Value}"
                : "Between stops");
            GUILayout.Label($"Speed: {_controllerDegreesPerSecond:0} deg/sec");
            var previousSpeed = _controllerDegreesPerSecond;
            _controllerDegreesPerSecond = GUILayout.HorizontalSlider(
                _controllerDegreesPerSecond,
                MinDegreesPerSecond,
                MaxDegreesPerSecond);
            if (!Mathf.Approximately(previousSpeed, _controllerDegreesPerSecond))
            {
                RefreshManualSpeed();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<<", GUILayout.Height(28f)))
            {
                StartTurning(-1f, 2f);
            }
            if (GUILayout.Button("<", GUILayout.Height(28f)))
            {
                StartTurning(-1f, 1f);
            }
            if (GUILayout.Button("Stop", GUILayout.Height(28f)))
            {
                StopUnlined();
            }
            if (GUILayout.Button(">", GUILayout.Height(28f)))
            {
                StartTurning(1f, 1f);
            }
            if (GUILayout.Button(">>", GUILayout.Height(28f)))
            {
                StartTurning(1f, 2f);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Line nearest", GUILayout.Height(28f)))
            {
                LineNearestStop();
            }
            if (GUILayout.Button("Close", GUILayout.Height(28f)))
            {
                _windowOpen = false;
                StopUnlined();
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private string WindowTitle()
        {
            return string.IsNullOrWhiteSpace(_turntable.id)
                ? "Hand Turntable"
                : "Hand Turntable - " + _turntable.id;
        }

        private void StartTurning(float direction, float speedMultiplier)
        {
            EndDragInteraction(false);
            _rotatingToTarget = false;
            _targetStopIndex = null;
            _speedDirection = Mathf.Sign(direction);
            _speedMultiplier = Mathf.Max(speedMultiplier, 0f);
            RefreshManualSpeed();
        }

        private float CurrentManualSpeed()
        {
            return Mathf.Min(_controllerDegreesPerSecond * Mathf.Max(_speedMultiplier, 0f), MaxDegreesPerSecond);
        }

        private void RefreshManualSpeed()
        {
            _speedDegreesPerSecond = CurrentManualSpeed();
        }

        private void StopUnlined()
        {
            _speedDirection = 0f;
            _speedMultiplier = 1f;
            _rotatingToTarget = false;
            _targetStopIndex = null;
            _turntable.UpdateSegmentIndex(false);
            SyncBridgeVisual();
        }

        private void LineNearestStop()
        {
            _speedDirection = 0f;
            _speedMultiplier = 1f;
            float remainder;
            var index = _turntable.IndexAndRemainderForAngle(out remainder);
            RotateToStop(index);
        }

        private void StartDragInteraction()
        {
            _windowOpen = true;
            BeginDrag();
        }

        private void BeginDrag()
        {
            if (_turntable == null)
            {
                return;
            }

            if (_turntable.TryGetCarBlockingMovement(out var car))
            {
                EndDragInteraction(false);
                Main.Warn($"Hand turntable '{_turntable.id}' refused drag because car '{car?.id ?? "unknown"}' is blocking the bridge.");
                return;
            }

            if (_dragging)
            {
                return;
            }

            _speedDirection = 0f;
            _speedMultiplier = 1f;
            _rotatingToTarget = false;
            _targetStopIndex = null;
            _dragEndOffset = DetermineGrabbedEndOffset();
            _dragTargetNodeIndex = null;
            _dragStopIndex = null;
            _dragging = true;
            SuppressCameraInput(true);
            EnsureNodeHighlights();
            UpdateDragTarget();
        }

        private void EndDragInteraction(bool rotateToSelectedNode)
        {
            if (!_dragging)
            {
                return;
            }

            var selected = _dragStopIndex;
            _dragging = false;
            _dragTargetNodeIndex = null;
            _dragStopIndex = null;
            SetNodeHighlightsVisible(false);
            SuppressCameraInput(false);
            if (rotateToSelectedNode && selected.HasValue)
            {
                RotateToStop(selected.Value);
            }
        }

        private void UpdateDragTarget()
        {
            if (_turntable == null)
            {
                return;
            }

            if (TryGetStopIndexUnderMouse(out var index))
            {
                _dragTargetNodeIndex = NormalizeStopIndex(index);
                _dragStopIndex = NormalizeStopIndex(_dragTargetNodeIndex.Value - _dragEndOffset);
            }

            UpdateNodeHighlightMaterials();
        }

        private int DetermineGrabbedEndOffset()
        {
            if (_turntable == null || _turntable.subdivisions <= 1 || !TryMouseAngle(out var mouseAngle))
            {
                return 0;
            }

            var forwardDelta = Mathf.Abs(Mathf.DeltaAngle(_turntable.Angle, mouseAngle));
            var backDelta = Mathf.Abs(Mathf.DeltaAngle(_turntable.Angle + 180f, mouseAngle));
            return backDelta < forwardDelta ? _turntable.subdivisions / 2 : 0;
        }

        private void RotateToStop(int index)
        {
            if (_turntable == null)
            {
                return;
            }

            if (_turntable.TryGetCarBlockingMovement(out var car))
            {
                Main.Warn($"Hand turntable '{_turntable.id}' refused to line because car '{car?.id ?? "unknown"}' is blocking the bridge.");
                return;
            }

            _speedDirection = 0f;
            _speedMultiplier = 1f;
            _targetStopIndex = NormalizeStopIndex(index);
            _targetAngle = Mathf.Repeat(_turntable.AngleForIndex(_targetStopIndex.Value), 360f);
            _rotatingToTarget = true;
            _turntable.UpdateSegmentIndex(true);
            SyncBridgeVisual();
        }

        private void RotateTowardTarget()
        {
            if (_turntable == null || !_targetStopIndex.HasValue)
            {
                _rotatingToTarget = false;
                return;
            }

            if (_turntable.TryGetCarBlockingMovement(out var car))
            {
                _rotatingToTarget = false;
                _targetStopIndex = null;
                Main.Warn($"Hand turntable '{_turntable.id}' stopped because car '{car?.id ?? "unknown"}' is blocking the bridge.");
                return;
            }

            var delta = Mathf.DeltaAngle(_turntable.Angle, _targetAngle);
            var step = _controllerDegreesPerSecond * Time.deltaTime;
            if (Mathf.Abs(delta) <= step)
            {
                _turntable.SetStopIndex(_targetStopIndex.Value);
                _rotatingToTarget = false;
                _targetStopIndex = null;
                SyncBridgeVisual();
                return;
            }

            var angle = Mathf.Repeat(_turntable.Angle + Mathf.Sign(delta) * step, 360f);
            _turntable.SetAngle(angle);
            _turntable.UpdateSegmentIndex(true);
            SyncBridgeVisual();
        }

        private int NormalizeStopIndex(int index)
        {
            var count = Mathf.Max(_turntable?.subdivisions ?? 1, 1);
            return ((index % count) + count) % count;
        }

        private bool TryGetStopIndexUnderMouse(out int index)
        {
            index = 0;
            if (TryGetNearestHighlightedNode(out index))
            {
                return true;
            }

            if (!TryMouseAngle(out var angle) || _turntable == null || _turntable.subdivisions <= 0)
            {
                return false;
            }

            var degreesPerIndex = 360f / _turntable.subdivisions;
            index = Mathf.RoundToInt(angle / degreesPerIndex) % _turntable.subdivisions;
            return true;
        }

        private bool TryGetNearestHighlightedNode(out int index)
        {
            index = 0;
            var camera = Camera.main;
            if (camera == null || _nodeHighlights.Count == 0)
            {
                return false;
            }

            var mouse = (Vector2)Input.mousePosition;
            var bestDistance = float.MaxValue;
            var found = false;
            for (var i = 0; i < _nodeHighlights.Count; i++)
            {
                var marker = _nodeHighlights[i];
                if (marker == null || !marker.activeSelf)
                {
                    continue;
                }

                var screen = camera.WorldToScreenPoint(marker.transform.position);
                if (screen.z < 0f)
                {
                    continue;
                }

                var distance = ((Vector2)screen - mouse).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                index = i;
                found = true;
            }

            return found;
        }

        private bool TryMouseAngle(out float angle)
        {
            angle = 0f;
            if (_turntable == null || Camera.main == null)
            {
                return false;
            }

            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(_turntable.transform.up, _turntable.transform.position);
            if (!plane.Raycast(ray, out var distance))
            {
                return false;
            }

            var hit = ray.GetPoint(distance);
            var local = _turntable.transform.InverseTransformPoint(hit);
            local.y = 0f;
            if (local.sqrMagnitude < 0.01f)
            {
                return false;
            }

            angle = Mathf.Repeat(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, 360f);
            return true;
        }

        private void SyncBridgeVisual()
        {
            if (_turntable == null || _bridgeRoot == null)
            {
                return;
            }

            _bridgeRoot.localRotation = Quaternion.Euler(0f, _turntable.Angle, 0f);
        }

        private void EnsureInteractionCollider()
        {
            var radius = InteractionRadiusFromDefinition(_definition);
            if (radius <= 0f && _turntable != null)
            {
                radius = Mathf.Max(_turntable.radius + 2f, 8f);
            }
            if (radius <= 0f)
            {
                radius = 16f;
            }

            _interactionCollider = gameObject.GetComponent<SphereCollider>();
            if (_interactionCollider == null)
            {
                _interactionCollider = gameObject.AddComponent<SphereCollider>();
            }

            _interactionCollider.center = Vector3.zero;
            _interactionCollider.radius = radius;
            _interactionCollider.isTrigger = true;
            _interactionCollider.enabled = true;
            gameObject.layer = global::ObjectPicker.LayerClickable;
        }

        private static float InteractionRadiusFromDefinition(object definition)
        {
            if (definition == null)
            {
                return 0f;
            }

            try
            {
                var visualsProperty = definition.GetType().GetProperty("Visuals", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var visuals = visualsProperty != null ? visualsProperty.GetValue(definition, null) : null;
                if (visuals == null)
                {
                    return 0f;
                }

                var radiusProperty = visuals.GetType().GetProperty("InteractionRadius", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (radiusProperty == null)
                {
                    return 0f;
                }

                var value = radiusProperty.GetValue(visuals, null);
                return value is float ? (float)value : 0f;
            }
            catch (Exception ex)
            {
                Main.Warn("Hand turntable could not read optional FUSE interaction radius: " + ex.Message);
                return 0f;
            }
        }

        private void EnsureNodeHighlights()
        {
            if (_turntable == null)
            {
                return;
            }

            EnsureHighlightMaterials();
            EnsureHighlightRoot();
            var nodes = GetTurntableNodes();
            var desiredCount = nodes != null && nodes.Count > 0 ? nodes.Count : Mathf.Max(_turntable.subdivisions, 0);
            while (_nodeHighlights.Count < desiredCount)
            {
                CreateNodeHighlight(_nodeHighlights.Count);
            }

            for (var i = 0; i < _nodeHighlights.Count; i++)
            {
                var active = i < desiredCount;
                var marker = _nodeHighlights[i];
                if (marker == null)
                {
                    continue;
                }

                marker.SetActive(active && _dragging);
                if (!active)
                {
                    continue;
                }

                marker.transform.position = NodeWorldPosition(i, nodes) + _turntable.transform.up * NodeMarkerYOffset;
                marker.transform.rotation = Quaternion.LookRotation(_turntable.transform.forward, _turntable.transform.up);
            }

            UpdateNodeHighlightMaterials();
        }

        private void EnsureHighlightRoot()
        {
            if (_nodeHighlightRoot != null)
            {
                return;
            }

            var root = new GameObject("Toolshed Turntable Node Highlights");
            root.transform.SetParent(transform, false);
            _nodeHighlightRoot = root.transform;
        }

        private void EnsureHighlightMaterials()
        {
            if (_nodeMaterial == null)
            {
                _nodeMaterial = CreateHighlightMaterial(new Color(1f, 0.78f, 0f, 1f));
            }
            if (_nodeActiveMaterial == null)
            {
                _nodeActiveMaterial = CreateHighlightMaterial(new Color(0f, 1f, 0.95f, 1f));
            }
        }

        private static Material CreateHighlightMaterial(Color color)
        {
            var shader = Shader.Find("Sprites/Default") ??
                         Shader.Find("GUI/Text Shader") ??
                         Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color") ??
                         Shader.Find("Standard");
            var material = new Material(shader)
            {
                color = color
            };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", color * 3f);
                material.EnableKeyword("_EMISSION");
            }
            return material;
        }

        private void CreateNodeHighlight(int index)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"Toolshed Node Highlight {index}";
            marker.transform.SetParent(_nodeHighlightRoot, false);
            marker.transform.localScale = new Vector3(NodeMarkerRadius, NodeMarkerHeight, NodeMarkerRadius);
            var collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = _nodeMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            marker.SetActive(false);
            _nodeHighlights.Add(marker);
            _nodeHighlightRenderers.Add(renderer);
        }

        private List<TrackNode> GetTurntableNodes()
        {
            return NodesField?.GetValue(_turntable) as List<TrackNode>;
        }

        private Vector3 NodeWorldPosition(int index, List<TrackNode> nodes)
        {
            if (nodes != null && index >= 0 && index < nodes.Count && nodes[index] != null)
            {
                return nodes[index].transform.position;
            }

            var angle = _turntable.AngleForIndex(index);
            return _turntable.transform.position +
                   _turntable.transform.rotation * Quaternion.Euler(0f, angle, 0f) * Vector3.forward * _turntable.radius;
        }

        private void SetNodeHighlightsVisible(bool visible)
        {
            for (var i = 0; i < _nodeHighlights.Count; i++)
            {
                if (_nodeHighlights[i] != null)
                {
                    _nodeHighlights[i].SetActive(visible);
                }
            }
        }

        private void UpdateNodeHighlightMaterials()
        {
            for (var i = 0; i < _nodeHighlightRenderers.Count; i++)
            {
                var renderer = _nodeHighlightRenderers[i];
                if (renderer != null)
                {
                    renderer.sharedMaterial = _dragTargetNodeIndex.HasValue && i == _dragTargetNodeIndex.Value ? _nodeActiveMaterial : _nodeMaterial;
                }
            }
        }

        private void SuppressCameraInput(bool suppress)
        {
            if (suppress)
            {
                if (_cameraSuppressed)
                {
                    return;
                }

                _cameraSuppressed = true;
                DisableCameraBehaviour("Cameras.StrategyCameraController");
                DisableCameraBehaviour("Cameras.StationaryCameraController");
                ClearMouseLook();
                return;
            }

            if (!_cameraSuppressed)
            {
                return;
            }

            for (var i = 0; i < _suppressedCameraBehaviours.Count; i++)
            {
                var behaviour = _suppressedCameraBehaviours[i];
                if (behaviour != null)
                {
                    behaviour.enabled = true;
                }
            }

            _suppressedCameraBehaviours.Clear();
            _cameraSuppressed = false;
            ClearMouseLook();
        }

        private void DisableCameraBehaviour(string typeName)
        {
            var type = Type.GetType(typeName + ", Assembly-CSharp");
            if (type == null)
            {
                return;
            }

            var behaviours = FindObjectsOfType(type);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i] as Behaviour;
                if (behaviour == null || !behaviour.enabled)
                {
                    continue;
                }

                behaviour.enabled = false;
                _suppressedCameraBehaviours.Add(behaviour);
            }
        }

        private void ClearMouseLook()
        {
            var type = Type.GetType("Cameras.MouseLookInput, Assembly-CSharp");
            if (type == null)
            {
                return;
            }

            var method = type.GetMethod("SetMouseMovesCamera", BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
            {
                return;
            }

            var inputs = FindObjectsOfType(type);
            for (var i = 0; i < inputs.Length; i++)
            {
                method.Invoke(inputs[i], new object[] { false });
            }
        }
    }
}
