using System;
using System.Collections.Generic;
using System.Reflection;
using FUSE.Authoring.Data;
using Game.AccessControl;
using Game.State;
using KeyValue.Runtime;
using KinematicCharacterController;
using Track;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toolshed.Turntables
{
    public sealed class HandTurntableController : MonoBehaviour, global::IPickable, IMoverController
    {
        private const float DefaultDegreesPerSecond = 18f;
        private const float MinDegreesPerSecond = 2f;
        private const float MaxDegreesPerSecond = 60f;
        private const float NodeMarkerRadius = 0.85f;
        private const float NodeMarkerHeight = 0.85f;
        private const float NodeMarkerYOffset = 1.15f;
        private const string ClickTargetName = "Toolshed Turntable Click Target";
        private const float ClickTargetWidth = 3.4f;
        private const float ClickTargetDepth = 1.6f;
        private const float ClickTargetTopY = -0.05f;
        private static readonly FieldInfo NodesField = typeof(Turntable).GetField("nodes", BindingFlags.Instance | BindingFlags.NonPublic);

        // Replicated intent (any player writes, host consumes).
        private const string SpeedKey = "speed";
        private const string TargetKey = "target";
        private const string TargetSpeedKey = "targetSpeed";
        private const string TargetSeqKey = "targetSeq";
        // Replicated state (host writes, clients render). Persisted in the save snapshot.
        private const string AngleKey = "angle";
        private const string StopKey = "stop";
        private const string NetObjectIdPrefix = "toolshed.turntable/";
        private const float NetTransmitInterval = 0.2f;
        private const float NetAngleSnapDegrees = 25f;

        private Turntable _turntable;
        private Transform _bridgeRoot;
        private object _definition;
        private Rect _windowRect = new Rect(120f, 120f, 280f, 190f);
        private bool _windowOpen;
        private float _speedMultiplier = 1f;
        private float _controllerDegreesPerSecond = DefaultDegreesPerSecond;
        private BoxCollider _interactionCollider;
        private PhysicsMover _bridgeMover;
        private Behaviour _suppressedVisualBinding;
        private Vector3 _bridgeLocalPosition;
        private bool _dragging;
        private int _dragEndOffset;
        private int? _dragTargetNodeIndex;
        private int? _dragStopIndex;
        private int? _targetStopIndex;
        private float _targetAngle;
        private float _targetSpeedDegreesPerSecond = DefaultDegreesPerSecond;
        private bool _rotatingToTarget;
        private Transform _nodeHighlightRoot;
        private readonly List<GameObject> _nodeHighlights = new List<GameObject>();
        private readonly List<Renderer> _nodeHighlightRenderers = new List<Renderer>();
        private readonly List<Behaviour> _suppressedCameraBehaviours = new List<Behaviour>();
        private Material _nodeMaterial;
        private Material _nodeActiveMaterial;
        private bool _cameraSuppressed;

        private KeyValueObject _keyValueObject;
        private string _registeredObjectId;
        private bool _registered;
        private readonly List<IDisposable> _netObservers = new List<IDisposable>();
        private bool _suppressStateObservers;
        private int _lastTargetSeq;
        private float _localSpeedCommand;
        private bool _hostMoving;
        private bool _publishedRestThisFrame;
        private float _lastPublishedAngle;
        private float _nextAnglePublishTime;
        private float _netAngle;
        private float _netAngleRate;
        private float _lastNetAngleTime;
        private bool _hasNetAngle;

        public float MaxPickDistance => 250f;

        public int Priority => 20;

        public global::TooltipInfo TooltipInfo => new global::TooltipInfo("Hand Turntable", "Drag to line the bridge");

        public global::PickableActivationFilter ActivationFilter => global::PickableActivationFilter.PrimaryOnly;

        private bool NetworkReady => _registered && _keyValueObject != null;

        // Host in multiplayer, or single player, or fallback when the state manager is unavailable.
        private bool IsAuthority => !NetworkReady || StateManager.IsHost;

        private float CurrentSpeedCommand => NetworkReady
            ? _keyValueObject[SpeedKey].FloatValueOrDefault(0f)
            : _localSpeedCommand;

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
            EnsureBridgeMover();
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

        private void Update()
        {
            if (!Main.Enabled || _turntable == null)
            {
                return;
            }

            EnsureNetworkRegistration();

            if (_dragging)
            {
                EnsureNodeHighlights();
                UpdateDragTarget();
                SyncBridgeVisual();
                return;
            }

            if (IsAuthority)
            {
                UpdateAuthoritySimulation();
            }
            else
            {
                UpdateClientView();
            }

            SyncBridgeVisual();
        }

        private void OnDisable()
        {
            _localSpeedCommand = 0f;
            _rotatingToTarget = false;
            _targetStopIndex = null;
            _hostMoving = false;
            EndDragInteraction(false);
            SuppressCameraInput(false);
        }

        private void OnDestroy()
        {
            UnregisterNetwork();
            SetVisualBindingSuppressed(false);
            if (_bridgeMover != null)
            {
                // PhysicsMover.VelocityUpdate dereferences MoverController without a null
                // check, so the mover must not outlive this controller.
                Destroy(_bridgeMover);
                _bridgeMover = null;
            }
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
            _speedMultiplier = Mathf.Max(speedMultiplier, 0f);
            IssueCancelTarget();
            IssueManualSpeed(Mathf.Sign(direction) * CurrentManualSpeed());
        }

        private float CurrentManualSpeed()
        {
            return Mathf.Min(_controllerDegreesPerSecond * Mathf.Max(_speedMultiplier, 0f), MaxDegreesPerSecond);
        }

        private void RefreshManualSpeed()
        {
            var current = CurrentSpeedCommand;
            if (Mathf.Abs(current) > 0.01f)
            {
                IssueManualSpeed(Mathf.Sign(current) * CurrentManualSpeed());
            }
        }

        private void StopUnlined()
        {
            _speedMultiplier = 1f;
            IssueCancelTarget();
            IssueManualSpeed(0f);
        }

        private void LineNearestStop()
        {
            _speedMultiplier = 1f;
            float remainder;
            var index = _turntable.IndexAndRemainderForAngle(out remainder);
            IssueLineToStop(index);
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

            IssueCancelTarget();
            IssueManualSpeed(0f);
            _speedMultiplier = 1f;
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
                IssueLineToStop(selected.Value);
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

        private void EnsureNetworkRegistration()
        {
            if (_turntable == null || string.IsNullOrWhiteSpace(_turntable.id))
            {
                return;
            }

            var desiredId = NetObjectIdPrefix + _turntable.id;
            if (_registered && _registeredObjectId == desiredId)
            {
                return;
            }

            if (_registered)
            {
                UnregisterNetwork();
            }

            var stateManager = StateManager.Shared;
            if (stateManager == null)
            {
                return;
            }

            _keyValueObject = GetComponent<KeyValueObject>();
            if (_keyValueObject == null)
            {
                _keyValueObject = gameObject.AddComponent<KeyValueObject>();
            }

            stateManager.RegisterPropertyObject(desiredId, _keyValueObject, AuthorizationRequirement.MinimumLevelCrew);
            _registeredObjectId = desiredId;
            _registered = true;

            // A target command restored from a save or join snapshot is stale; don't re-fire it.
            _lastTargetSeq = _keyValueObject[TargetSeqKey].IntValueOrDefault(0);

            var isHost = StateManager.IsHost;
            if (isHost)
            {
                ApplyRestoredAuthorityState();
            }

            // Subscribe angle before stop: the stop handler relies on _hasNetAngle.
            _netObservers.Add(_keyValueObject.Observe(AngleKey, OnAngleValueChanged, !isHost));
            _netObservers.Add(_keyValueObject.Observe(StopKey, OnStopValueChanged, !isHost));
        }

        private void UnregisterNetwork()
        {
            for (var i = 0; i < _netObservers.Count; i++)
            {
                _netObservers[i]?.Dispose();
            }

            _netObservers.Clear();
            if (_registered && !string.IsNullOrEmpty(_registeredObjectId))
            {
                try
                {
                    StateManager.Shared?.UnregisterPropertyObject(_registeredObjectId);
                }
                catch (Exception ex)
                {
                    Main.Warn($"Hand turntable could not unregister property object '{_registeredObjectId}': {ex.Message}");
                }
            }

            _registered = false;
            _registeredObjectId = null;
        }

        private void ApplyRestoredAuthorityState()
        {
            var stop = _keyValueObject[StopKey];
            if (stop.Type == KeyValue.Runtime.ValueType.Int)
            {
                _turntable.SetStopIndex(NormalizeStopIndex(stop.IntValue));
                SyncBridgeVisual();
                return;
            }

            var angle = _keyValueObject[AngleKey];
            if (angle.Type == KeyValue.Runtime.ValueType.Float)
            {
                _turntable.UpdateSegmentIndex(true);
                _turntable.SetAngle(Mathf.Repeat(angle.FloatValue, 360f));
                _turntable.UpdateSegmentIndex(false);
                SyncBridgeVisual();
            }
        }

        private void OnAngleValueChanged(Value value)
        {
            if (_suppressStateObservers || _turntable == null || value.Type != KeyValue.Runtime.ValueType.Float)
            {
                return;
            }

            if (IsAuthority)
            {
                // Only fires from a snapshot restore that arrived after registration.
                ApplyRestoredAuthorityState();
                return;
            }

            var angle = Mathf.Repeat(value.FloatValue, 360f);
            var now = Time.unscaledTime;
            if (_hasNetAngle && now - _lastNetAngleTime > 0.01f)
            {
                var travelled = Mathf.Abs(Mathf.DeltaAngle(_netAngle, angle));
                _netAngleRate = Mathf.Clamp(travelled / (now - _lastNetAngleTime), MinDegreesPerSecond, MaxDegreesPerSecond * 2f);
            }

            _netAngle = angle;
            _hasNetAngle = true;
            _lastNetAngleTime = now;
        }

        private void OnStopValueChanged(Value value)
        {
            if (_suppressStateObservers || _turntable == null)
            {
                return;
            }

            if (IsAuthority)
            {
                ApplyRestoredAuthorityState();
                return;
            }

            if (value.Type == KeyValue.Runtime.ValueType.Int)
            {
                _turntable.SetStopIndex(NormalizeStopIndex(value.IntValue));
                _netAngle = _turntable.Angle;
                _hasNetAngle = true;
                SyncBridgeVisual();
            }
            else if (_hasNetAngle)
            {
                _turntable.UpdateSegmentIndex(true);
            }
        }

        private void UpdateAuthoritySimulation()
        {
            _publishedRestThisFrame = false;
            ProcessPendingTargetCommand();

            var wasMoving = _hostMoving;
            var moving = false;
            var speedCommand = CurrentSpeedCommand;

            if (Mathf.Abs(speedCommand) > 0.01f)
            {
                _rotatingToTarget = false;
                _targetStopIndex = null;
                moving = StepManualRotation(speedCommand, wasMoving);
            }
            else if (_rotatingToTarget)
            {
                moving = StepTargetRotation(wasMoving);
            }

            if (moving)
            {
                PublishAngleIfDue();
            }
            else if (wasMoving && !_publishedRestThisFrame)
            {
                FinalizeMovementState();
            }

            _hostMoving = moving;
        }

        private bool StepManualRotation(float speedCommand, bool wasMoving)
        {
            if (_turntable.TryGetCarBlockingMovement(out var car))
            {
                IssueManualSpeed(0f);
                Main.Warn($"Hand turntable '{_turntable.id}' refused to move because car '{car?.id ?? "unknown"}' is blocking the bridge.");
                return false;
            }

            if (!wasMoving)
            {
                PublishMovementStarted();
            }

            var angle = Mathf.Repeat(_turntable.Angle + speedCommand * Time.deltaTime, 360f);
            _turntable.SetAngle(angle);
            _turntable.UpdateSegmentIndex(true);
            return true;
        }

        private bool StepTargetRotation(bool wasMoving)
        {
            if (!_targetStopIndex.HasValue)
            {
                _rotatingToTarget = false;
                return false;
            }

            if (_turntable.TryGetCarBlockingMovement(out var car))
            {
                _rotatingToTarget = false;
                _targetStopIndex = null;
                Main.Warn($"Hand turntable '{_turntable.id}' stopped because car '{car?.id ?? "unknown"}' is blocking the bridge.");
                FinalizeMovementState();
                return false;
            }

            var delta = Mathf.DeltaAngle(_turntable.Angle, _targetAngle);
            var step = Mathf.Max(_targetSpeedDegreesPerSecond, MinDegreesPerSecond) * Time.deltaTime;
            if (Mathf.Abs(delta) <= step)
            {
                var index = _targetStopIndex.Value;
                _rotatingToTarget = false;
                _targetStopIndex = null;
                _turntable.SetStopIndex(index);
                FinalizeMovementState();
                return false;
            }

            if (!wasMoving)
            {
                PublishMovementStarted();
            }

            _turntable.SetAngle(Mathf.Repeat(_turntable.Angle + Mathf.Sign(delta) * step, 360f));
            _turntable.UpdateSegmentIndex(true);
            return true;
        }

        private void ProcessPendingTargetCommand()
        {
            if (!NetworkReady)
            {
                return;
            }

            var seq = _keyValueObject[TargetSeqKey].IntValueOrDefault(0);
            if (seq == _lastTargetSeq)
            {
                return;
            }

            _lastTargetSeq = seq;
            var target = _keyValueObject[TargetKey];
            if (target.Type == KeyValue.Runtime.ValueType.Int)
            {
                ApplyLineToStop(target.IntValue, _keyValueObject[TargetSpeedKey].FloatValueOrDefault(DefaultDegreesPerSecond));
            }
            else
            {
                _rotatingToTarget = false;
                _targetStopIndex = null;
            }
        }

        private void ApplyLineToStop(int index, float degreesPerSecond)
        {
            if (_turntable.TryGetCarBlockingMovement(out var car))
            {
                Main.Warn($"Hand turntable '{_turntable.id}' refused to line because car '{car?.id ?? "unknown"}' is blocking the bridge.");
                return;
            }

            _targetStopIndex = NormalizeStopIndex(index);
            _targetAngle = Mathf.Repeat(_turntable.AngleForIndex(_targetStopIndex.Value), 360f);
            _targetSpeedDegreesPerSecond = Mathf.Clamp(degreesPerSecond, MinDegreesPerSecond, MaxDegreesPerSecond);
            _rotatingToTarget = true;
        }

        private void FinalizeMovementState()
        {
            if (!_turntable.StopIndex.HasValue)
            {
                _turntable.UpdateSegmentIndex(false);
            }

            PublishRestState();
            _publishedRestThisFrame = true;
        }

        private void PublishMovementStarted()
        {
            if (!NetworkReady)
            {
                return;
            }

            _suppressStateObservers = true;
            try
            {
                _keyValueObject[StopKey] = Value.Null();
                _keyValueObject[AngleKey] = Value.Float(_turntable.Angle);
            }
            finally
            {
                _suppressStateObservers = false;
            }

            _lastPublishedAngle = _turntable.Angle;
            _nextAnglePublishTime = Time.unscaledTime + NetTransmitInterval;
        }

        private void PublishAngleIfDue()
        {
            if (!NetworkReady || Time.unscaledTime < _nextAnglePublishTime)
            {
                return;
            }

            if (Mathf.Abs(Mathf.DeltaAngle(_lastPublishedAngle, _turntable.Angle)) < 0.001f)
            {
                return;
            }

            _suppressStateObservers = true;
            try
            {
                _keyValueObject[AngleKey] = Value.Float(_turntable.Angle);
            }
            finally
            {
                _suppressStateObservers = false;
            }

            _lastPublishedAngle = _turntable.Angle;
            _nextAnglePublishTime = Time.unscaledTime + NetTransmitInterval;
        }

        private void PublishRestState()
        {
            if (!NetworkReady)
            {
                return;
            }

            _suppressStateObservers = true;
            try
            {
                _keyValueObject[AngleKey] = Value.Float(_turntable.Angle);
                _keyValueObject[StopKey] = _turntable.StopIndex.HasValue
                    ? Value.Int(_turntable.StopIndex.Value)
                    : Value.Null();
            }
            finally
            {
                _suppressStateObservers = false;
            }

            _lastPublishedAngle = _turntable.Angle;
        }

        private void UpdateClientView()
        {
            if (!_hasNetAngle || _turntable.StopIndex.HasValue)
            {
                return;
            }

            var delta = Mathf.DeltaAngle(_turntable.Angle, _netAngle);
            if (Mathf.Abs(delta) < 0.001f)
            {
                return;
            }

            float next;
            if (Mathf.Abs(delta) > NetAngleSnapDegrees)
            {
                next = _netAngle;
            }
            else
            {
                var rate = _netAngleRate > 0.01f ? _netAngleRate : DefaultDegreesPerSecond;
                next = Mathf.MoveTowardsAngle(_turntable.Angle, _netAngle, rate * Time.deltaTime);
            }

            _turntable.SetAngle(Mathf.Repeat(next, 360f));
        }

        private void IssueManualSpeed(float degreesPerSecond)
        {
            _localSpeedCommand = degreesPerSecond;
            if (NetworkReady)
            {
                _keyValueObject[SpeedKey] = Value.Float(degreesPerSecond);
            }
        }

        private void IssueCancelTarget()
        {
            if (NetworkReady)
            {
                _keyValueObject[TargetKey] = Value.Null();
                _keyValueObject[TargetSeqKey] = Value.Int(_keyValueObject[TargetSeqKey].IntValueOrDefault(0) + 1);
            }
            else
            {
                _rotatingToTarget = false;
                _targetStopIndex = null;
            }
        }

        private void IssueLineToStop(int index)
        {
            if (_turntable.TryGetCarBlockingMovement(out var car))
            {
                Main.Warn($"Hand turntable '{_turntable.id}' refused to line because car '{car?.id ?? "unknown"}' is blocking the bridge.");
                return;
            }

            var normalized = NormalizeStopIndex(index);
            IssueManualSpeed(0f);
            if (NetworkReady)
            {
                _keyValueObject[TargetKey] = Value.Int(normalized);
                _keyValueObject[TargetSpeedKey] = Value.Float(_controllerDegreesPerSecond);
                _keyValueObject[TargetSeqKey] = Value.Int(_keyValueObject[TargetSeqKey].IntValueOrDefault(0) + 1);
            }
            else
            {
                ApplyLineToStop(normalized, _controllerDegreesPerSecond);
            }
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
            // While a PhysicsMover drives the bridge, writing the transform here would
            // bypass the character system and leave a walking player behind.
            if (_turntable == null || _bridgeRoot == null || _bridgeMover != null)
            {
                return;
            }

            _bridgeRoot.localRotation = Quaternion.Euler(0f, _turntable.Angle, 0f);
        }

        private void EnsureBridgeMover()
        {
            if (_bridgeRoot == null)
            {
                return;
            }

            // Rotate the bridge through a KCC PhysicsMover like the vanilla
            // TurntableController does, so a player standing on the deck is carried
            // along. Child colliders (the bridge asset's walk surfaces) attach to the
            // mover's kinematic rigidbody automatically.
            _bridgeLocalPosition = _bridgeRoot.localPosition;
            _bridgeMover = _bridgeRoot.GetComponent<PhysicsMover>();
            if (_bridgeMover == null)
            {
                _bridgeMover = _bridgeRoot.gameObject.AddComponent<PhysicsMover>();
            }

            _bridgeMover.MoverController = this;
            SetVisualBindingSuppressed(true);
        }

        public void UpdateMovement(out Vector3 goalPosition, out Quaternion goalRotation, float deltaTime)
        {
            if (_bridgeRoot == null)
            {
                goalPosition = Vector3.zero;
                goalRotation = Quaternion.identity;
                return;
            }

            var parent = _bridgeRoot.parent;
            if (parent == null || _turntable == null)
            {
                goalPosition = _bridgeRoot.position;
                goalRotation = _bridgeRoot.rotation;
                return;
            }

            goalPosition = parent.TransformPoint(_bridgeLocalPosition);
            goalRotation = parent.rotation * Quaternion.Euler(0f, _turntable.Angle, 0f);
        }

        private void SetVisualBindingSuppressed(bool suppressed)
        {
            // FUSE's FuseTurntableVisualBinding rewrites the bridge rotation in
            // LateUpdate, which fights the PhysicsMover's interpolation. The type is
            // internal to FUSE, so match it by name.
            if (suppressed)
            {
                if (_suppressedVisualBinding != null)
                {
                    return;
                }

                foreach (var behaviour in GetComponents<MonoBehaviour>())
                {
                    if (behaviour != null && behaviour.enabled &&
                        behaviour.GetType().FullName == "FUSE.Runtime.API.FuseTurntableVisualBinding")
                    {
                        behaviour.enabled = false;
                        _suppressedVisualBinding = behaviour;
                        break;
                    }
                }

                return;
            }

            if (_suppressedVisualBinding != null)
            {
                _suppressedVisualBinding.enabled = true;
                _suppressedVisualBinding = null;
            }
        }

        private void EnsureInteractionCollider()
        {
            // A whole-turntable sphere here used to sit between the camera and anything
            // parked on the bridge, so it stole clicks aimed at the locomotive. The click
            // target instead hugs the bridge deck below railhead: ObjectPicker takes the
            // first clickable hit along the ray, so rolling stock above the deck wins.
            var legacySphere = gameObject.GetComponent<SphereCollider>();
            if (legacySphere != null)
            {
                Destroy(legacySphere);
            }

            if (_bridgeRoot == null)
            {
                return;
            }

            var halfLength = _turntable != null && _turntable.radius > 0f
                ? _turntable.radius
                : InteractionRadiusFromDefinition(_definition);
            if (halfLength <= 0f)
            {
                halfLength = 8f;
            }

            var existing = _bridgeRoot.Find(ClickTargetName);
            var clickTarget = existing != null ? existing.gameObject : new GameObject(ClickTargetName);
            clickTarget.transform.SetParent(_bridgeRoot, false);
            clickTarget.transform.localPosition = Vector3.zero;
            clickTarget.transform.localRotation = Quaternion.identity;
            clickTarget.transform.localScale = Vector3.one;
            // ObjectPicker resolves IPickable via GetComponentInParent, so a child of the
            // bridge still resolves to this controller on the turntable root.
            clickTarget.layer = global::ObjectPicker.LayerClickable;

            _interactionCollider = clickTarget.GetComponent<BoxCollider>();
            if (_interactionCollider == null)
            {
                _interactionCollider = clickTarget.AddComponent<BoxCollider>();
            }

            _interactionCollider.center = new Vector3(0f, ClickTargetTopY - ClickTargetDepth * 0.5f, 0f);
            _interactionCollider.size = new Vector3(ClickTargetWidth, ClickTargetDepth, halfLength * 2f);
            _interactionCollider.isTrigger = true;
            _interactionCollider.enabled = true;
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
