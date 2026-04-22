using System;
using System.Collections.Generic;
using UnityEngine;
using Fluxa.Input;

namespace Fluxa.Drawing
{
    /// <summary>Drawing tool modes for the chart overlay.</summary>
    public enum DrawToolMode
    {
        Freehand,       // Default — continuous pressure-based freehand drawing
        TrendLine,      // Tap start point, tap end point → straight line
        HorizontalLine, // Tap once → horizontal line across full chart width
    }

    /// <summary>
    /// Manages drawing on the FLUXA chart panel.
    ///
    /// The BoxCollider covers the ENTIRE Canvas (toolbar + chart) so the stylus
    /// raycast hits everywhere on the panel.  After a hit, the world-space point
    /// is tested against the ChartSection rect to decide:
    ///   • Inside ChartSection  → drawing + IsHitOnDrawArea = true
    ///   • Outside (toolbar)    → cursor visible, no drawing, IsHitOnDrawArea = false
    ///
    /// TimeframeController reads IsHitOnDrawArea == false to know the stylus is
    /// over a toolbar button and handles the click itself.
    /// </summary>
    public class ChartDrawingManager : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────
        [Header("References")]
        [Tooltip("The RectTransform that contains the chart (used for local-space conversion). " +
                 "Auto-discovered from ChartSection if left empty.")]
        [SerializeField] private RectTransform drawSurface;

        [Tooltip("BoxCollider for raycast hit detection. Auto-added to the Canvas if missing.")]
        [SerializeField] private BoxCollider surfaceCollider;

        [Header("Line Settings")]
        [SerializeField] private Color lineColor = new Color(1f, 0.85f, 0.2f, 0.8f);
        [SerializeField] private float minWidth = 0.001f;
        [SerializeField] private float maxWidth = 0.005f;

        [Header("Smoothing")]
        [Tooltip("Minimum world-space distance between recorded points.")]
        [SerializeField] private float minPointDistance = 0.0005f;
        [Tooltip("Number of subdivisions for Catmull-Rom smoothing (0 = off).")]
        [Range(0, 4)]
        [SerializeField] private int smoothSubdivisions = 2;

        [Header("Near / Far Mode")]
        [Tooltip("Distance threshold (world meters) for switching to near (contact) mode.")]
        [SerializeField] private float nearModeThreshold = 0.05f;
        [Tooltip("In near mode, stylus tip within this distance of the surface counts as touching.")]
        [SerializeField] private float contactDistance = 0.01f;
        [Tooltip("Haptic amplitude for surface contact in near mode (stronger than far mode).")]
        [Range(0f, 1f)]
        [SerializeField] private float nearHapticAmplitude = 0.5f;

        [Header("Raycast (Far Mode)")]
        [Tooltip("Maximum raycast distance from the stylus tip.")]
        [SerializeField] private float maxRayDistance = 5.0f;
        [Tooltip("Layer mask for the drawing surface collider.")]
        [SerializeField] private LayerMask surfaceLayerMask = ~0;

        [Header("Draw Area")]
        [Tooltip("Height in canvas pixels excluded from the top of the canvas for the title bar. " +
                 "Drawing is allowed everywhere on the canvas below this line.")]
        [SerializeField] private float titleBarExcludeHeight = 45f;

        // ── Runtime state ─────────────────────────────────────────────────
        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private readonly List<Vector3> _currentPoints = new List<Vector3>();
        private LineRenderer _activeLine;
        private Material _lineMaterial;
        private StylusInputManager _stylus;
        private bool _wasDrawing;
        private bool _wasNearMode;

        /// <summary>The Canvas RectTransform that owns the collider.</summary>
        private RectTransform _canvasRect;

        // ── Tool mode state ──────────────────────────────────────────────
        private DrawToolMode _toolMode = DrawToolMode.Freehand;
        private Vector3 _trendLineStartLocal;   // local-space start for trend line
        private bool _hasTrendLineStart;         // true when first point is set

        /// <summary>Current drawing tool mode.</summary>
        public DrawToolMode CurrentToolMode => _toolMode;

        /// <summary>Fired when the tool mode changes.</summary>
        public event Action<DrawToolMode> OnToolModeChanged;

        // Public read-only for cursor / other systems
        /// <summary>True when the raycast hits anywhere on the Canvas panel.</summary>
        public bool    HasHit           { get; private set; }
        /// <summary>World-space hit point on the panel.</summary>
        public Vector3 HitPoint         { get; private set; }
        /// <summary>Hit point in drawSurface (ChartSection) local space.</summary>
        public Vector3 HitLocalPos      { get; private set; }
        /// <summary>Surface normal at the hit point.</summary>
        public Vector3 HitNormal        { get; private set; }
        /// <summary>True when the stylus is close enough for direct contact drawing.</summary>
        public bool    IsNearMode       { get; private set; }
        /// <summary>
        /// True when the hit point falls inside the ChartSection drawing area.
        /// False when the hit is on the toolbar or other non-chart areas.
        /// </summary>
        public bool    IsHitOnDrawArea  { get; private set; }

        // ─────────────────────────────────────────────────────────────────
        private void Start()
        {
            // Find or create the stylus manager
            _stylus = StylusInputManager.Instance;
            if (_stylus == null)
            {
                var go = new GameObject("StylusInputManager");
                _stylus = go.AddComponent<StylusInputManager>();
                Debug.Log("[ChartDrawingManager] Created StylusInputManager.");
            }

            // Auto-discover draw surface and canvas
            if (drawSurface == null)
            {
                var panel = GameObject.Find("FLUXAPanel");
                if (panel != null)
                {
                    Debug.Log($"[ChartDrawingManager] Found FLUXAPanel. Children: " +
                              $"{LogChildNames(panel.transform)}");

                    var canvas = panel.transform.Find("Canvas");
                    if (canvas != null)
                    {
                        _canvasRect = canvas as RectTransform;

                        Debug.Log($"[ChartDrawingManager] Found Canvas. Children: " +
                                  $"{LogChildNames(canvas)}");

                        var chartSection = canvas.Find("ChartSection");
                        if (chartSection != null)
                            drawSurface = chartSection as RectTransform;
                        else
                            Debug.LogWarning("[ChartDrawingManager] ChartSection not found under Canvas!");
                    }
                    else
                    {
                        Debug.LogWarning("[ChartDrawingManager] Canvas not found under FLUXAPanel!");
                    }
                }
                else
                {
                    Debug.LogWarning("[ChartDrawingManager] FLUXAPanel not found in scene!");
                }
            }
            else
            {
                // drawSurface was set in the inspector — derive canvas from its parent
                _canvasRect = drawSurface.parent as RectTransform;
            }

            if (drawSurface == null || _canvasRect == null)
            {
                Debug.LogWarning("[ChartDrawingManager] No drawSurface or canvas found. Disabling.");
                enabled = false;
                return;
            }

            Debug.Log($"[ChartDrawingManager] drawSurface: {drawSurface.name}, " +
                      $"canvas: {_canvasRect.name}, " +
                      $"rect={drawSurface.rect}, sizeDelta={drawSurface.sizeDelta}, " +
                      $"lossyScale={drawSurface.lossyScale}");

            // Ensure a BoxCollider exists on the CANVAS for raycasting
            EnsureCollider();

            // Create shared line material (unlit, transparent)
            _lineMaterial = new Material(Shader.Find("Sprites/Default"));
            _lineMaterial.color = lineColor;

            // Subscribe to undo and tool cycle
            _stylus.OnUndoPressed += UndoLastLine;
            _stylus.OnToolCycleRequested += CycleToolMode;
        }

        private static string LogChildNames(Transform parent)
        {
            var names = new System.Text.StringBuilder();
            for (int i = 0; i < parent.childCount; i++)
            {
                if (i > 0) names.Append(", ");
                names.Append(parent.GetChild(i).name);
            }
            return names.ToString();
        }

        private void Update()
        {
            if (_stylus == null || drawSurface == null) return;

            // Reset surface state each frame — DoRaycast will set it if hit
            _stylus.IsOnSurface = false;
            HasHit = false;
            IsHitOnDrawArea = false;

            DoRaycast();

            // Only handle drawing when the hit is on the chart area
            if (IsHitOnDrawArea)
                HandleDrawing();
            else
                HandleDrawingInterrupted();
        }

        private void OnDestroy()
        {
            if (_stylus != null)
            {
                _stylus.OnUndoPressed -= UndoLastLine;
                _stylus.OnToolCycleRequested -= CycleToolMode;
            }

            if (_lineMaterial != null)
                Destroy(_lineMaterial);
        }

        // ── Interaction (near + far) ──────────────────────────────────────

        private void DoRaycast()
        {
            // Transform local controller pose into world space.
            Transform trackingSpace = GetTrackingSpace();
            Vector3 worldTip;
            Vector3 worldForward;

            if (trackingSpace != null)
            {
                worldTip = trackingSpace.TransformPoint(_stylus.TipPosition);
                worldForward = trackingSpace.rotation * (_stylus.TipRotation * Vector3.forward);
            }
            else
            {
                worldTip = _stylus.TipPosition;
                worldForward = _stylus.TipRotation * Vector3.forward;
            }

            // ── Decide near vs far mode ──────────────────────────────────
            float distToSurface = DistanceToCollider(worldTip);
            IsNearMode = distToSurface <= nearModeThreshold && distToSurface >= 0f;

            if (IsNearMode)
            {
                DoNearMode(worldTip);
            }
            else
            {
                DoFarMode(worldTip, worldForward);
            }

            // Haptic bump on mode transition to near
            if (IsNearMode && !_wasNearMode)
                OVRInput.SetControllerVibration(0.2f, nearHapticAmplitude, OVRInput.Controller.RTouch);
            _wasNearMode = IsNearMode;
        }

        /// <summary>
        /// After setting world-space hit info, classify whether the hit falls
        /// in the drawable area (full canvas except the title bar at the top).
        /// Drawing is allowed edge-to-edge on the canvas below the title bar.
        /// </summary>
        private void ClassifyHitRegion(Vector3 worldHitPoint)
        {
            // HitLocalPos is in drawSurface (ChartSection) local space for line rendering
            HitLocalPos = drawSurface.InverseTransformPoint(worldHitPoint);

            // For draw-area classification, check against the CANVAS rect
            // (not ChartSection) so drawing works edge-to-edge.
            Vector3 localInCanvas = _canvasRect.InverseTransformPoint(worldHitPoint);
            Rect canvasRect = _canvasRect.rect;

            // Allow drawing everywhere on the canvas except the title bar at the top.
            // Title bar occupies from (canvasRect.yMax - titleBarExcludeHeight) to canvasRect.yMax.
            bool withinCanvas = canvasRect.Contains(new Vector2(localInCanvas.x, localInCanvas.y));
            bool inTitleBar = localInCanvas.y > (canvasRect.yMax - titleBarExcludeHeight);

            IsHitOnDrawArea = withinCanvas && !inTitleBar;
        }

        /// <summary>
        /// NEAR MODE: direct contact drawing — closest point on the panel collider
        /// to the stylus tip gives the "writing on a whiteboard" feel.
        /// </summary>
        private void DoNearMode(Vector3 worldTip)
        {
            Vector3 closestPt = surfaceCollider.ClosestPoint(worldTip);
            float dist = Vector3.Distance(worldTip, closestPt);

            // Surface normal: for a thin panel, it's the canvas transform's forward.
            Vector3 surfaceNormal = _canvasRect.forward;

            // Count as "on surface" when within contactDistance of the collider face.
            bool touching = dist <= contactDistance;

            HasHit   = true;  // always show cursor in near mode
            HitPoint = closestPt;
            HitNormal = surfaceNormal;

            ClassifyHitRegion(closestPt);

            // Only mark IsOnSurface (enables drawing) when on the chart area
            _stylus.IsOnSurface = touching && IsHitOnDrawArea;

            // Stronger continuous haptic while in contact on the draw area
            if (touching && _stylus.TipPressure > 0f && IsHitOnDrawArea)
            {
                float amp = Mathf.Lerp(0.1f, nearHapticAmplitude, _stylus.TipPressure);
                OVRInput.SetControllerVibration(0.1f, amp, OVRInput.Controller.RTouch);
            }
        }

        /// <summary>
        /// FAR MODE: raycast from the stylus tip along its forward direction.
        /// </summary>
        private void DoFarMode(Vector3 worldTip, Vector3 worldForward)
        {
            if (Physics.Raycast(worldTip, worldForward, out RaycastHit hit, maxRayDistance, surfaceLayerMask))
            {
                if (hit.collider == surfaceCollider ||
                    hit.collider.transform.IsChildOf(_canvasRect.transform))
                {
                    HasHit   = true;
                    HitPoint = hit.point;
                    HitNormal = hit.normal;

                    ClassifyHitRegion(hit.point);

                    // Only mark IsOnSurface (enables drawing) when on the chart area
                    _stylus.IsOnSurface = IsHitOnDrawArea;
                    return;
                }
            }

            HasHit = false;
            _stylus.IsOnSurface = false;
        }

        /// <summary>
        /// Returns the unsigned world-space distance from a point to the surface collider.
        /// Returns -1 if the collider is null.
        /// </summary>
        private float DistanceToCollider(Vector3 worldPoint)
        {
            if (surfaceCollider == null) return -1f;
            Vector3 closest = surfaceCollider.ClosestPoint(worldPoint);
            return Vector3.Distance(worldPoint, closest);
        }

        private Transform _trackingSpaceCache;

        private Transform GetTrackingSpace()
        {
            if (_trackingSpaceCache != null) return _trackingSpaceCache;

            var cameraRig = FindAnyObjectByType<OVRCameraRig>();
            if (cameraRig != null)
                _trackingSpaceCache = cameraRig.trackingSpace;

            return _trackingSpaceCache;
        }

        // ── Tool mode ─────────────────────────────────────────────────────

        /// <summary>Switch the active drawing tool.</summary>
        public void SetToolMode(DrawToolMode mode)
        {
            if (mode == _toolMode) return;

            // Cancel any in-progress tool action
            CancelPendingTool();

            _toolMode = mode;
            Debug.Log($"[ChartDrawingManager] Tool mode -> {mode}");
            OnToolModeChanged?.Invoke(mode);
        }

        /// <summary>Cycle to the next tool mode (for controller shortcut).</summary>
        public void CycleToolMode()
        {
            var modes = (DrawToolMode[])Enum.GetValues(typeof(DrawToolMode));
            int next = ((int)_toolMode + 1) % modes.Length;
            SetToolMode(modes[next]);
        }

        private void CancelPendingTool()
        {
            _hasTrendLineStart = false;
            // Discard any in-progress freehand line
            if (_activeLine != null)
            {
                Destroy(_activeLine.gameObject);
                _activeLine = null;
                _currentPoints.Clear();
            }
        }

        // ── Drawing ───────────────────────────────────────────────────────

        /// <summary>
        /// Called when the stylus moves off the draw area mid-stroke.
        /// Ends any active freehand line so we don't leave a dangling stroke.
        /// </summary>
        private void HandleDrawingInterrupted()
        {
            if (_wasDrawing)
            {
                EndFreehandLine();
                _wasDrawing = false;
            }
        }

        private void HandleDrawing()
        {
            switch (_toolMode)
            {
                case DrawToolMode.Freehand:
                    HandleFreehandDrawing();
                    break;
                case DrawToolMode.TrendLine:
                    HandleTrendLineDrawing();
                    break;
                case DrawToolMode.HorizontalLine:
                    HandleHorizontalLineDrawing();
                    break;
            }
        }

        // ── Freehand (original behavior) ─────────────────────────────────

        private void HandleFreehandDrawing()
        {
            bool isDrawing = _stylus.IsDrawing && HasHit;

            if (isDrawing && !_wasDrawing)
                BeginFreehandLine();

            if (isDrawing && _activeLine != null)
                ContinueFreehandLine();

            if (!isDrawing && _wasDrawing)
                EndFreehandLine();

            _wasDrawing = isDrawing;
        }

        private void BeginFreehandLine()
        {
            _activeLine = CreateLineRenderer($"Line_{_lines.Count}");
            _currentPoints.Clear();
            AddFreehandPoint(WorldToLocal(HitPoint));
        }

        private void ContinueFreehandLine()
        {
            Vector3 localPt = WorldToLocal(HitPoint);
            if (_currentPoints.Count == 0 ||
                Vector3.Distance(localPt, _currentPoints[_currentPoints.Count - 1]) > minPointDistance)
            {
                AddFreehandPoint(localPt);
            }

            float w = Mathf.Lerp(minWidth, maxWidth, _stylus.TipPressure);
            _activeLine.startWidth = w;
            _activeLine.endWidth   = w;
        }

        private void AddFreehandPoint(Vector3 localPoint)
        {
            _currentPoints.Add(localPoint);

            if (smoothSubdivisions > 0 && _currentPoints.Count >= 4)
            {
                var smoothed = CatmullRomSmooth(_currentPoints, smoothSubdivisions);
                _activeLine.positionCount = smoothed.Count;
                _activeLine.SetPositions(smoothed.ToArray());
            }
            else
            {
                _activeLine.positionCount = _currentPoints.Count;
                _activeLine.SetPositions(_currentPoints.ToArray());
            }
        }

        private void EndFreehandLine()
        {
            if (_activeLine != null && _currentPoints.Count < 2)
                Destroy(_activeLine.gameObject);
            else if (_activeLine != null)
                _lines.Add(_activeLine);

            _activeLine = null;
            _currentPoints.Clear();
        }

        // ── Trend Line (two-tap) ─────────────────────────────────────────

        private void HandleTrendLineDrawing()
        {
            bool isDrawing = _stylus.IsDrawing && HasHit;
            bool tapStart = isDrawing && !_wasDrawing;

            if (tapStart)
            {
                Vector3 localPt = WorldToLocal(HitPoint);

                if (!_hasTrendLineStart)
                {
                    _trendLineStartLocal = localPt;
                    _hasTrendLineStart = true;
                    Debug.Log($"[ChartDrawingManager] Trend line: start set at local {localPt}");
                }
                else
                {
                    var lr = CreateLineRenderer($"TrendLine_{_lines.Count}");
                    lr.positionCount = 2;
                    lr.SetPositions(new[] { _trendLineStartLocal, localPt });
                    float w = (minWidth + maxWidth) * 0.5f;
                    lr.startWidth = w;
                    lr.endWidth = w;
                    _lines.Add(lr);

                    Debug.Log($"[ChartDrawingManager] Trend line drawn: {_trendLineStartLocal} -> {localPt}");
                    _hasTrendLineStart = false;
                }
            }

            _wasDrawing = isDrawing;
        }

        // ── Horizontal Line (single-tap) ─────────────────────────────────

        private void HandleHorizontalLineDrawing()
        {
            bool isDrawing = _stylus.IsDrawing && HasHit;
            bool tapStart = isDrawing && !_wasDrawing;

            if (tapStart)
            {
                Vector3 hitLocal = WorldToLocal(HitPoint);
                Rect surfaceRect = drawSurface.rect;

                Vector3 leftLocal  = new Vector3(surfaceRect.xMin, hitLocal.y, 0f);
                Vector3 rightLocal = new Vector3(surfaceRect.xMax, hitLocal.y, 0f);

                var lr = CreateLineRenderer($"HLine_{_lines.Count}");
                lr.positionCount = 2;
                lr.SetPositions(new[] { leftLocal, rightLocal });
                float w = (minWidth + maxWidth) * 0.5f;
                lr.startWidth = w;
                lr.endWidth = w;
                _lines.Add(lr);

                Debug.Log($"[ChartDrawingManager] H-line at local Y={hitLocal.y:F1}");
            }

            _wasDrawing = isDrawing;
        }

        // ── Shared line creation ─────────────────────────────────────────

        private LineRenderer CreateLineRenderer(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(drawSurface, worldPositionStays: false);

            var lr = go.AddComponent<LineRenderer>();
            lr.material = _lineMaterial;
            lr.startColor = lineColor;
            lr.endColor   = lineColor;
            lr.numCapVertices  = 4;
            lr.numCornerVertices = 4;
            lr.useWorldSpace = false;
            lr.positionCount = 0;
            return lr;
        }

        /// <summary>Convert a world-space point to drawSurface local space.</summary>
        private Vector3 WorldToLocal(Vector3 worldPoint)
        {
            return drawSurface.InverseTransformPoint(worldPoint);
        }

        // ── Undo ──────────────────────────────────────────────────────────

        public void UndoLastLine()
        {
            if (_lines.Count == 0) return;

            int last = _lines.Count - 1;
            if (_lines[last] != null)
                Destroy(_lines[last].gameObject);
            _lines.RemoveAt(last);

            Debug.Log($"[ChartDrawingManager] Undo -- {_lines.Count} lines remaining.");
        }

        public void ClearAllLines()
        {
            foreach (var lr in _lines)
            {
                if (lr != null) Destroy(lr.gameObject);
            }
            _lines.Clear();
        }

        // ── Collider setup ────────────────────────────────────────────────

        /// <summary>
        /// Adds a BoxCollider to the CANVAS (not ChartSection) so the raycast
        /// covers the entire panel — toolbar buttons included.
        /// </summary>
        private void EnsureCollider()
        {
            // Put the collider on the Canvas, not on ChartSection
            surfaceCollider = _canvasRect.GetComponent<BoxCollider>();
            if (surfaceCollider != null) return;

            surfaceCollider = _canvasRect.gameObject.AddComponent<BoxCollider>();

            // Force layout so rect.size is computed from stretched anchors.
            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_canvasRect);

            Vector2 size = _canvasRect.rect.size;

            if (size.x <= 0 || size.y <= 0)
            {
                if (_canvasRect.anchorMin == _canvasRect.anchorMax)
                    size = _canvasRect.sizeDelta;
            }

            // Last resort: compute from the parent
            if (size.x <= 0 || size.y <= 0)
            {
                var parentRect = _canvasRect.parent as RectTransform;
                if (parentRect != null)
                {
                    Canvas.ForceUpdateCanvases();
                    Vector2 parentSize = parentRect.rect.size;
                    size = parentSize + _canvasRect.sizeDelta;
                    Debug.Log($"[ChartDrawingManager] Computed canvas size from parent: " +
                              $"parent={parentSize}, inset={_canvasRect.sizeDelta}, result={size}");
                }
            }

            // Absolute fallback
            if (size.x <= 0 || size.y <= 0)
            {
                size = new Vector2(800, 500);
                Debug.LogWarning("[ChartDrawingManager] Could not determine canvas size, using fallback 800x500");
            }

            // Z depth: thick enough for reliable raycasts
            surfaceCollider.size   = new Vector3(size.x, size.y, 50f);
            surfaceCollider.center = Vector3.zero;

            Debug.Log($"[ChartDrawingManager] Added BoxCollider to Canvas ({_canvasRect.name}): " +
                      $"size=({size.x}, {size.y}, 50) worldScale={_canvasRect.lossyScale}");
        }

        // ── Catmull-Rom smoothing ─────────────────────────────────────────

        private static List<Vector3> CatmullRomSmooth(List<Vector3> points, int subdivisions)
        {
            if (points.Count < 4)
                return new List<Vector3>(points);

            var result = new List<Vector3>(points.Count * (subdivisions + 1));

            result.Add(points[0]);

            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 p0 = points[Mathf.Max(i - 1, 0)];
                Vector3 p1 = points[i];
                Vector3 p2 = points[Mathf.Min(i + 1, points.Count - 1)];
                Vector3 p3 = points[Mathf.Min(i + 2, points.Count - 1)];

                for (int s = 1; s <= subdivisions; s++)
                {
                    float t = s / (float)(subdivisions + 1);
                    result.Add(CatmullRomPoint(p0, p1, p2, p3, t));
                }

                if (i < points.Count - 2)
                    result.Add(p2);
            }

            result.Add(points[points.Count - 1]);

            return result;
        }

        private static Vector3 CatmullRomPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }
    }
}
