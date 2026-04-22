using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controls the FLUXA floating panel in MR space.
/// On Start(), positions the panel relative to the user's head so it spawns
/// comfortably whether seated or standing. Then follows the user if they
/// move beyond followDistance.
/// </summary>
public class FLUXAPanel : MonoBehaviour
{
    [Header("Spawn Settings")]
    [Tooltip("Distance in front of the user to spawn the panel")]
    public float spawnDistance = 1.2f;
    [Tooltip("Vertical offset from eye level (negative = below eyes)")]
    public float spawnHeightOffset = 0.0f;
    [Tooltip("X-axis tilt in degrees (positive = top tilted away from user)")]
    public float spawnTiltDegrees = -12f;

    [Header("Follow Settings")]
    [Tooltip("How far the user can move before the panel follows")]
    public float followDistance = 2.5f;
    [Tooltip("How fast the panel lerps to follow position")]
    public float followSpeed = 2f;

    [Header("Input")]
    [Tooltip("OVR button to toggle panel (default: Start/Menu button)")]
    public OVRInput.Button toggleButton = OVRInput.Button.Start;

    [Header("Panel State")]
    public bool isVisible = true;
    public bool allowManualGrab = true;

    private Transform _cameraTransform;
    private Vector3 _targetPosition;
    private bool _isFollowing = false;
    private bool _isGrabbed = false;
    private Vector3 _grabOffset;
    private bool _hasPositionedAtHead = false;

    void Start()
    {
        if (Camera.main != null)
            _cameraTransform = Camera.main.transform;

        // Position at head on first frame if camera is ready
        if (_cameraTransform != null)
            PositionAtHead();
    }

    void Update()
    {
        // Retry head positioning if camera wasn't ready in Start()
        if (!_hasPositionedAtHead)
        {
            if (_cameraTransform == null && Camera.main != null)
                _cameraTransform = Camera.main.transform;

            if (_cameraTransform != null)
                PositionAtHead();
        }

        if (_cameraTransform == null || !isVisible) return;

        // Follow logic removed — panel spawns once at head and stays put.

        // ── Toggle with controller ────────────────────────────────────
        if (OVRInput.GetDown(toggleButton))
            ToggleVisibility();

        // ── Grab ──────────────────────────────────────────────────────
        if (allowManualGrab)
        {
            if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger) ||
                OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger))
            {
                _isGrabbed = true;
                _isFollowing = false;
                _grabOffset = transform.position - _cameraTransform.position;
            }
            if (OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger) ||
                OVRInput.GetUp(OVRInput.Button.SecondaryHandTrigger))
            {
                _isGrabbed = false;
            }
            if (_isGrabbed)
                transform.position = _cameraTransform.position + _grabOffset;
        }
    }

    // ── Head-relative positioning ─────────────────────────────────────

    /// <summary>
    /// Positions the panel relative to the user's current head pose.
    /// Called once on Start (or first Update if camera wasn't ready).
    /// </summary>
    private void PositionAtHead()
    {
        _hasPositionedAtHead = true;

        Vector3 headPos = _cameraTransform.position;
        Vector3 headForward = _cameraTransform.forward;

        // Flatten forward to horizontal plane
        headForward.y = 0f;
        if (headForward.sqrMagnitude < 0.001f)
            headForward = Vector3.forward; // fallback if looking straight down/up
        headForward.Normalize();

        // Place panel: spawnDistance meters in front, spawnHeightOffset below eye level
        Vector3 panelPos = headPos
            + headForward * spawnDistance
            + Vector3.up * spawnHeightOffset;

        transform.position = panelPos;
        ApplyFacingRotation();

        Debug.Log($"[FLUXAPanel] Positioned at head: pos={panelPos}, " +
                  $"headY={headPos.y:F2}, tilt={spawnTiltDegrees}°");
    }

    /// <summary>
    /// Rotates the panel to face the user with the configured X tilt.
    /// </summary>
    private void ApplyFacingRotation()
    {
        if (_cameraTransform == null) return;

        Vector3 lookDir = transform.position - _cameraTransform.position;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude < 0.001f) return;

        Quaternion facing = Quaternion.LookRotation(lookDir);
        // Apply tilt: positive spawnTiltDegrees tilts the top away from the user
        transform.rotation = facing * Quaternion.Euler(spawnTiltDegrees, 0f, 0f);
    }

    // ── Public API ────────────────────────────────────────────────────

    public void Show()
    {
        isVisible = true;
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        isVisible = false;
        gameObject.SetActive(false);
    }

    public void ToggleVisibility()
    {
        if (isVisible) Hide();
        else Show();
    }

    /// <summary>Re-snap the panel to the user's current head position.</summary>
    public void RecenterToHead()
    {
        if (_cameraTransform == null && Camera.main != null)
            _cameraTransform = Camera.main.transform;
        if (_cameraTransform != null)
            PositionAtHead();
    }
}
