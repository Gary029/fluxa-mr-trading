using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Fluxa.Input
{
    /// <summary>
    /// Reads MX Ink stylus input.
    ///
    /// Pose (both MX Ink and Touch):
    ///   OVRInput.GetLocalControllerPosition/Rotation(RTouch) — grip pose.
    ///   OVRPlugin.GetActionStatePose("aim_right") is NOT used: the action is not
    ///   registered in OVR's OpenXR action sets and the call fails every frame,
    ///   leaving rotation frozen (ray stuck pointing forward).
    ///
    /// When MX Ink is active:
    ///   Pressure→ OVRPlugin.GetActionStateFloat("tip")
    ///   Buttons → OVRPlugin.GetActionStateBoolean("front"/"back")
    ///   Haptics → OVRPlugin.TriggerVibrationAction("haptic_pulse")
    ///
    /// When Touch controller (fallback):
    ///   Pressure→ OVRInput.Axis1D.PrimaryIndexTrigger
    ///   Buttons → OVRInput.Button.One / Two
    ///   Haptics → UnityEngine.XR.InputDevices
    ///
    /// OVRPlugin calls are native P/Invoke and work with Active Input Handling =
    /// "Input System Package (New)" — they do not use Unity's legacy Input Manager.
    ///
    /// Button gestures (deferred-tap system):
    ///   Front single tap  → OnFrontButtonPressed
    ///   Front double tap  → OnToolCycleRequested
    ///   Back  single tap  → OnUndoPressed
    ///   Back  double tap  → OnTimeframeCycleRequested
    /// </summary>
    public class StylusInputManager : MonoBehaviour
    {
        // ── Singleton ──────────────────────────────────────────────────────
        public static StylusInputManager Instance { get; private set; }

        // ── Inspector ──────────────────────────────────────────────────────
        [Header("Pressure")]
        [Range(0f, 0.3f)]
        public float pressureDeadzone = 0.05f;

        [Header("Haptics")]
        [Range(0f, 1f)] public float touchHapticAmplitude = 0.3f;
        [Range(0f, 1f)] public float drawHapticAmplitude  = 0.15f;

        [Header("MX Ink Detection")]
        public bool hideControllerWhenMXInk = true;

        [Header("Smoothing")]
        [Tooltip("How fast the pose tracks raw input. Higher = snappier, lower = smoother. " +
                 "25 gives ~1 frame lag at 90 Hz which kills micro-jitter without feeling sluggish.")]
        [Range(5f, 200f)]
        public float positionSmoothSpeed = 25f;

        [Header("Double-Tap")]
        [Range(0.15f, 0.5f)]
        public float doubleTapWindow = 0.35f;

        // ── Public read-only state ─────────────────────────────────────────
        public Vector3    TipPosition       { get; private set; }
        public Quaternion TipRotation       { get; private set; }
        public float      TipPressure       { get; private set; }
        public bool       IsFrontButtonDown { get; private set; }
        public bool       IsBackButtonDown  { get; private set; }
        public bool       IsDrawing         { get; private set; }
        public bool       IsOnSurface       { get; set; }
        public bool       IsMXInkActive     { get; private set; }

        // ── Events ─────────────────────────────────────────────────────────
        public event Action OnDrawStart;
        public event Action OnDrawEnd;
        public event Action OnUndoPressed;
        public event Action OnFrontButtonPressed;    // front single tap
        public event Action OnToolCycleRequested;   // front double tap
        public event Action OnSymbolCycleRequested; // front triple tap
        public event Action OnTimeframeCycleRequested;

        // ── Private ────────────────────────────────────────────────────────
        private const OVRInput.Controller Stylus = OVRInput.Controller.RTouch;

        // MX Ink action names (from Logitech OpenXR interaction profile)
        private const string TipAction      = "tip";
        private const string FrontAction    = "front";
        private const string BackAction     = "back";
        private const string HapticAction   = "haptic_pulse";

        private bool _wasDrawing;
        private bool _wasOnSurface;
        private bool _controllerHidden;
        private OVRControllerHelper _rightControllerHelper;

        // Rising-edge tracking for OVRPlugin boolean reads (no GetDown equivalent)
        private bool _prevMxFront;
        private bool _prevMxBack;

        // Deferred tap — front (counts consecutive taps within doubleTapWindow)
        private float _frontTapTime  = -1f;
        private bool  _frontTapPending;
        private int   _frontTapCount;

        // Deferred tap — back
        private float _backTapTime = -1f;
        private bool  _backTapPending;

        // Raw (unsmoothed) pose — smoothed values are exposed via TipPosition/TipRotation
        private Vector3    _rawPosition;
        private Quaternion _rawRotation = Quaternion.identity;
        private bool       _poseInitialized;

        // MX Ink hysteresis — prevents single-frame flip causing visible pose shake
        private const int MXInkStableFramesRequired = 6;
        private int  _mxInkCandidateFrames;
        private bool _mxInkCandidateValue;

        // Debug
        private float _lastDebugLogTime;

        // ──────────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Debug.Log("[StylusInputManager] Awake.");
        }

        private void Update()
        {
            DetectMXInk();
            ReadPose();
            ReadButtons();
            ProcessDeferredTaps();
            HandleDrawingState();
            HandleHaptics();
            DebugLog();
        }

        // ── MX Ink detection ───────────────────────────────────────────────

        private void DetectMXInk()
        {
            bool raw = false;
            try
            {
                var profile = OVRPlugin.GetCurrentInteractionProfileName(OVRPlugin.Hand.HandRight);
                raw = !string.IsNullOrEmpty(profile) && profile.Contains("mx_ink");
            }
            catch { }

            if (raw == _mxInkCandidateValue)
                _mxInkCandidateFrames++;
            else
            {
                _mxInkCandidateValue  = raw;
                _mxInkCandidateFrames = 1;
            }

            if (_mxInkCandidateFrames >= MXInkStableFramesRequired &&
                IsMXInkActive != _mxInkCandidateValue)
            {
                IsMXInkActive = _mxInkCandidateValue;
                if (hideControllerWhenMXInk)
                    UpdateControllerVisibility();
                Debug.Log($"[StylusInputManager] MX Ink active = {IsMXInkActive}");
            }
        }

        private void UpdateControllerVisibility()
        {
            if (_rightControllerHelper == null)
                _rightControllerHelper = FindRightControllerHelper();
            if (_rightControllerHelper != null)
            {
                _rightControllerHelper.gameObject.SetActive(!IsMXInkActive);
                _controllerHidden = IsMXInkActive;
            }
        }

        private static OVRControllerHelper FindRightControllerHelper()
        {
            foreach (var h in FindObjectsByType<OVRControllerHelper>(FindObjectsSortMode.None))
                if (h.m_controller == OVRInput.Controller.RTouch)
                    return h;
            return null;
        }

        // ── Pose reading ───────────────────────────────────────────────────

        private readonly List<InputDevice> _poseDevices = new List<InputDevice>();

        private void ReadPose()
        {
            // Read right-hand node pose directly from the XR subsystem.
            // OVRInput.GetLocalControllerPosition/Rotation(RTouch) returns stale data
            // when MX Ink is active because OVRInput expects a Touch controller device —
            // MX Ink uses its own OpenXR interaction profile, so OVRInput never updates.
            // XR InputDevices + CommonUsages reads the node pose regardless of device type.
            InputDevices.GetDevicesAtXRNode(XRNode.RightHand, _poseDevices);
            bool gotPose = false;
            foreach (var d in _poseDevices)
            {
                if (d.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos) &&
                    d.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
                {
                    _rawPosition = pos;
                    _rawRotation = rot;
                    gotPose = true;
                    break;
                }
            }
            if (!gotPose)
            {
                _rawPosition = OVRInput.GetLocalControllerPosition(Stylus);
                _rawRotation = OVRInput.GetLocalControllerRotation(Stylus);
            }

            // Snap on first valid read so we don't lerp from Vector3.zero across the room.
            if (!_poseInitialized)
            {
                TipPosition      = _rawPosition;
                TipRotation      = _rawRotation;
                _poseInitialized = true;
                return;
            }

            // Frame-rate-independent exponential smoothing.
            // alpha ≈ 0.25 at 90 Hz with speed=25 → ~1 frame lag, kills micro-jitter.
            float alpha = Mathf.Clamp01(positionSmoothSpeed * Time.deltaTime);
            TipPosition = Vector3.Lerp(TipPosition, _rawPosition, alpha);
            TipRotation = Quaternion.Slerp(TipRotation, _rawRotation, alpha);
        }

        // ── Button + pressure reading ──────────────────────────────────────

        private void ReadButtons()
        {
            if (IsMXInkActive)
            {
                OVRPlugin.GetActionStateFloat(TipAction, out float tip);
                TipPressure = tip > pressureDeadzone ? tip : 0f;

                OVRPlugin.GetActionStateBoolean(FrontAction, out bool front);
                OVRPlugin.GetActionStateBoolean(BackAction,  out bool back);

                if (front && !_prevMxFront) RegisterFrontTap();
                if (back  && !_prevMxBack)  RegisterBackTap();

                IsFrontButtonDown = front;
                IsBackButtonDown  = back;
                _prevMxFront      = front;
                _prevMxBack       = back;
            }
            else
            {
                float raw = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, Stylus);
                TipPressure = raw > pressureDeadzone ? raw : 0f;

                IsFrontButtonDown = OVRInput.Get(OVRInput.Button.One, Stylus);
                IsBackButtonDown  = OVRInput.Get(OVRInput.Button.Two, Stylus);

                if (OVRInput.GetDown(OVRInput.Button.One, Stylus)) RegisterFrontTap();
                if (OVRInput.GetDown(OVRInput.Button.Two, Stylus)) RegisterBackTap();

                _prevMxFront = false;
                _prevMxBack  = false;
            }
        }

        // ── Deferred tap processing ────────────────────────────────────────

        private void RegisterFrontTap()
        {
            float now = Time.unscaledTime;
            if (_frontTapPending && (now - _frontTapTime) <= doubleTapWindow)
            {
                _frontTapCount++;
                _frontTapTime = now; // slide window forward from last tap
            }
            else
            {
                _frontTapCount = 1;
                _frontTapTime  = now;
            }
            _frontTapPending = true;
        }

        private void RegisterBackTap()
        {
            float now = Time.unscaledTime;
            if (_backTapPending && (now - _backTapTime) <= doubleTapWindow)
            {
                _backTapPending = false;
                _backTapTime    = -1f;
                Debug.Log("[StylusInputManager] Back double-tap → timeframe cycle");
                OnTimeframeCycleRequested?.Invoke();
            }
            else
            {
                _backTapPending = true;
                _backTapTime    = now;
            }
        }

        private void ProcessDeferredTaps()
        {
            float now = Time.unscaledTime;

            if (_frontTapPending && (now - _frontTapTime) > doubleTapWindow)
            {
                _frontTapPending = false;
                switch (_frontTapCount)
                {
                    case 1:
                        Debug.Log("[StylusInputManager] Front single tap → RSI toggle");
                        OnFrontButtonPressed?.Invoke();
                        break;
                    case 2:
                        Debug.Log("[StylusInputManager] Front double tap → tool cycle");
                        OnToolCycleRequested?.Invoke();
                        break;
                    default:
                        Debug.Log("[StylusInputManager] Front triple tap → symbol cycle");
                        OnSymbolCycleRequested?.Invoke();
                        break;
                }
                _frontTapCount = 0;
            }

            if (_backTapPending && (now - _backTapTime) > doubleTapWindow)
            {
                _backTapPending = false;
                Debug.Log("[StylusInputManager] Back single tap → undo");
                OnUndoPressed?.Invoke();
            }
        }

        // ── Drawing state ──────────────────────────────────────────────────

        private void HandleDrawingState()
        {
            IsDrawing = TipPressure > 0f && IsOnSurface;
            if (IsDrawing && !_wasDrawing)      OnDrawStart?.Invoke();
            else if (!IsDrawing && _wasDrawing) OnDrawEnd?.Invoke();
            _wasDrawing = IsDrawing;
        }

        // ── Haptics ────────────────────────────────────────────────────────

        private readonly List<InputDevice> _hapticDevices = new List<InputDevice>();

        private void HandleHaptics()
        {
            if (IsOnSurface && !_wasOnSurface)
                SendHapticImpulse(touchHapticAmplitude, 0.05f);
            if (IsDrawing)
                SendHapticImpulse(TipPressure * drawHapticAmplitude, 0.05f);
            else if (_wasDrawing)
                StopHaptics();
            _wasOnSurface = IsOnSurface;
        }

        private void SendHapticImpulse(float amplitude, float duration)
        {
            if (IsMXInkActive)
            {
                OVRPlugin.TriggerVibrationAction(HapticAction, OVRPlugin.Hand.HandRight,
                                                 duration, amplitude);
            }
            else
            {
                InputDevices.GetDevicesAtXRNode(XRNode.RightHand, _hapticDevices);
                foreach (var d in _hapticDevices)
                    d.SendHapticImpulse(0, amplitude, duration);
            }
        }

        private void StopHaptics()
        {
            if (!IsMXInkActive)
            {
                InputDevices.GetDevicesAtXRNode(XRNode.RightHand, _hapticDevices);
                foreach (var d in _hapticDevices)
                    d.StopHaptics();
            }
        }

        // ── Debug ──────────────────────────────────────────────────────────

        private void DebugLog()
        {
            if (Time.unscaledTime - _lastDebugLogTime < 1f) return;
            _lastDebugLogTime = Time.unscaledTime;
            Debug.Log($"[StylusInputManager] pos={TipPosition:F2} pressure={TipPressure:F3} " +
                      $"front={IsFrontButtonDown} back={IsBackButtonDown} mxInk={IsMXInkActive}");
        }

        private void OnDestroy()
        {
            StopHaptics();
            if (Instance == this) Instance = null;
        }
    }
}
