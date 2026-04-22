using UnityEngine;
using UnityEngine.UI;
using Fluxa.WebView;

namespace Fluxa.Input
{
    /// <summary>
    /// Connects Logitech MX Ink stylus events to the FLUXA chart HTML via JavaScript injection.
    ///
    /// Clicking is handled by direct JS injection (raycast → UV → pixel coords → dispatchEvent),
    /// bypassing the OVR Input Module / EventSystem / PointerEventSource chain entirely.
    /// This works regardless of whether the OVR Raycaster registers hits on the WebView texture.
    ///
    /// Per-frame injections:
    ///   - mousemove (throttled to ~30 Hz) for hover/crosshair
    ///   - mousedown + mouseup + click on tip pressure crossing threshold
    ///   - setMXInkPressure() for freehand line width
    ///   - mxInkBarrelPanDelta() when front button is held and stylus moves
    ///
    /// Button gestures (from StylusInputManager deferred-tap system):
    ///   Front single tap  → mxInkBarrelPress()  (cancel / undo)
    ///   Front double tap  → mxInkDoubleTap()    (cycle draw tool)
    ///   Back  single tap  → mxInkBarrelPress()  (undo last drawing)
    ///   Back  double tap  → setTimeframeByResolution() cycle
    /// </summary>
    public class MxInkWebViewBridge : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("RawImage displaying the chart WebView texture.")]
        [SerializeField] private RawImage chartRawImage;

        [Tooltip("Root of OVR tracking space (usually OVRCameraRig/TrackingSpace). " +
                 "Leave null if OVRCameraRig is at world origin.")]
        [SerializeField] private Transform trackingSpaceRoot;

        [Header("WebView Resolution")]
        [Tooltip("Must match the pixel width the Android WebView was initialised at " +
                 "(see FluxaWebViewManager — default 1024).")]
        [SerializeField] private int webViewWidth  = 1024;
        [Tooltip("Must match the pixel height the Android WebView was initialised at " +
                 "(see FluxaWebViewManager — default 600).")]
        [SerializeField] private int webViewHeight = 600;

        [Header("Pointer")]
        [Tooltip("Tip pressure level that triggers a click (fallback when proximity not available).")]
        [Range(0.05f, 0.5f)]
        [SerializeField] private float clickPressureThreshold = 0.1f;

        [Tooltip("Seconds between mousemove injections. Lower = smoother but more JS calls.")]
        [Range(0.008f, 0.1f)]
        [SerializeField] private float hoverThrottleSeconds = 0.016f;

        [Tooltip("Log pose/pressure/hit-status once per second while debugging.")]
        [SerializeField] private bool debugLog = true;

        [Header("Tip Proximity")]
        [Tooltip("Distance (metres) from the chart panel at which the tip counts as a click. " +
                 "Triggers on the press edge when crossing inwards.")]
        [Range(0.005f, 0.1f)]
        [SerializeField] private float tipProximityMeters = 0.025f;

        [Tooltip("Extra distance added to release the click, so tiny jitter doesn't re-fire.")]
        [Range(0.002f, 0.05f)]
        [SerializeField] private float tipProximityHysteresis = 0.01f;

        [Header("Ray Visual")]
        [Tooltip("Optional LineRenderer drawn from the stylus tip along the aim direction.")]
        [SerializeField] private LineRenderer rayLine;
        [Tooltip("Ray length when the stylus is not aimed at the chart.")]
        [SerializeField] private float rayDefaultLength = 1.5f;
        [Tooltip("Color when pointing at the chart.")]
        [SerializeField] private Color rayHitColor  = new Color(0.2f, 1f, 0.4f, 1f);
        [Tooltip("Color when aimed elsewhere.")]
        [SerializeField] private Color rayMissColor = new Color(1f, 1f, 1f, 0.6f);

        // ── Runtime state ─────────────────────────────────────────────────
        private StylusInputManager  _stylus;
        private FluxaWebViewManager _webView;

        // Pointer injection state
        private bool    _wasClickPressed;
        private Vector2 _lastHoverPx   = new Vector2(-9999f, -9999f);
        private float   _lastHoverTime;

        // Drag (front-button-hold) state
        private bool    _dragActive;
        private Vector2 _dragStartPx;
        private bool    _wasFrontDown;
        private float   _frontPressTime = -1f;

        // Front button must be held at least this long before WebView drag events fire.
        // Quick taps shorter than this are pure gesture input and stay silent to the WebView.
        private const float DragStartHoldSeconds = 0.20f;

        // Tip proximity state — absolute distance from tip to the chart panel plane.
        private float _tipDistToPanel = float.MaxValue;

        // Indicator cycle state (front single tap)
        private int _indicatorIndex = -1;

        // Symbol cycle state (front triple tap)
        private int _symbolIndex = 0;

        // Debug
        private float _lastDebugTime;

        // ─────────────────────────────────────────────────────────────────
        private void Awake()
        {
            Debug.Log($"[MxInkWebViewBridge] Awake on GameObject '{gameObject.name}' " +
                      $"(active={gameObject.activeInHierarchy})");
        }

        private void Start()
        {
            Debug.Log("[MxInkWebViewBridge] Start running — resolving dependencies.");
            _stylus  = StylusInputManager.Instance;
            _webView = FluxaWebViewManager.Instance;

            if (_stylus == null)
            {
                Debug.LogWarning("[MxInkWebViewBridge] StylusInputManager not found in scene.");
                enabled = false;
                return;
            }
            if (_webView == null)
            {
                Debug.LogWarning("[MxInkWebViewBridge] FluxaWebViewManager not found in scene.");
                enabled = false;
                return;
            }

            _stylus.OnFrontButtonPressed      += OnFrontSingleTap;
            _stylus.OnToolCycleRequested      += OnFrontDoubleTap;
            _stylus.OnSymbolCycleRequested    += OnFrontTripleTap;
            _stylus.OnUndoPressed             += OnBackSingleTap;
            _stylus.OnTimeframeCycleRequested += OnBackDoubleTap;

            Debug.Log("[MxInkWebViewBridge] Initialised — direct JS pointer injection active.");
        }

        // Per-frame cached UV — computed once, shared by drag / pointer / ray visual.
        private Vector2 _frameNorm;

        private void Update()
        {
            if (_stylus == null || _webView == null) return;

            _frameNorm = GetNormalisedStylusOnCanvas();
            _tipDistToPanel = ComputeTipDistToPanel();

            StreamPressure();
            UpdateDrag();
            UpdatePointer();
            UpdateRayVisual();
        }

        /// <summary>
        /// Absolute perpendicular distance (metres) from the stylus tip to the chart panel plane.
        /// Returns float.MaxValue when the tip or panel isn't valid.
        /// </summary>
        private float ComputeTipDistToPanel()
        {
            if (chartRawImage == null || _stylus == null) return float.MaxValue;
            if (_stylus.TipPosition == Vector3.zero)       return float.MaxValue;

            Vector3 tipWorld = trackingSpaceRoot != null
                ? trackingSpaceRoot.TransformPoint(_stylus.TipPosition)
                : _stylus.TipPosition;

            Vector3 panelNormal = chartRawImage.rectTransform.forward;
            Vector3 panelOrigin = chartRawImage.rectTransform.position;
            return Mathf.Abs(Vector3.Dot(tipWorld - panelOrigin, panelNormal));
        }

        // Front-button hold (≥DragStartHoldSeconds) = drag/draw on the WebView.
        // Quick taps shorter than the threshold are pure gesture input (single/double/triple
        // tap) and are intentionally silent to the WebView so they don't accidentally click
        // chart elements while the gesture is being recognised.
        private float _lastDragLogTime;

        private void UpdateDrag()
        {
            bool down = _stylus.IsFrontButtonDown;
            Vector2 norm = _frameNorm;
            bool hits = norm != Vector2.zero;

            if (Time.unscaledTime - _lastDragLogTime > 1f)
            {
                _lastDragLogTime = Time.unscaledTime;
                Debug.Log($"[MxInkWebViewBridge] front={down} hits={hits} uv={norm} " +
                          $"tipPos={_stylus.TipPosition:F2} mxInk={_stylus.IsMXInkActive}");
            }

            // Record the moment the front button goes down.
            if (down && !_wasFrontDown)
                _frontPressTime = Time.unscaledTime;

            // Start a drag only after the button has been held long enough.
            // This prevents gesture taps from firing stray WebView pointer events.
            if (down && !_dragActive && hits &&
                _frontPressTime >= 0f &&
                Time.unscaledTime - _frontPressTime >= DragStartHoldSeconds)
            {
                int px = Mathf.RoundToInt(norm.x * webViewWidth);
                int py = Mathf.RoundToInt((1f - norm.y) * webViewHeight);
                _dragActive  = true;
                _dragStartPx = new Vector2(px, py);
                JS($"(function(){{" +
                   $"var t={JsTarget(px, py)};" +
                   $"window.__mxInkDragEl=t;" +
                   $"var opt={{bubbles:true,cancelable:true,clientX:{px},clientY:{py},screenX:{px},screenY:{py},button:0,buttons:1}};" +
                   $"t.dispatchEvent(new PointerEvent('pointerdown',Object.assign({{pointerId:1,pointerType:'pen',pressure:0.5}},opt)));" +
                   $"t.dispatchEvent(new MouseEvent('mousedown',opt));" +
                   $"}})()");
                Debug.Log($"[MxInkWebViewBridge] Drag start ({px},{py})");
            }
            else if (down && _dragActive && hits)
            {
                int px = Mathf.RoundToInt(norm.x * webViewWidth);
                int py = Mathf.RoundToInt((1f - norm.y) * webViewHeight);
                float nowT = Time.unscaledTime;
                bool moved = Mathf.Abs(px - _lastHoverPx.x) > 1 ||
                             Mathf.Abs(py - _lastHoverPx.y) > 1;
                if (moved && nowT - _lastHoverTime >= hoverThrottleSeconds)
                {
                    _lastHoverTime = nowT;
                    _lastHoverPx   = new Vector2(px, py);
                    JS($"(function(){{" +
                       $"var t=window.__mxInkDragEl||{JsTarget(px, py)};" +
                       $"var opt={{bubbles:true,cancelable:true,clientX:{px},clientY:{py},screenX:{px},screenY:{py},buttons:1}};" +
                       $"t.dispatchEvent(new PointerEvent('pointermove',Object.assign({{pointerId:1,pointerType:'pen',pressure:0.5}},opt)));" +
                       $"t.dispatchEvent(new MouseEvent('mousemove',opt));" +
                       $"}})()");
                }
            }
            else if (!down && _wasFrontDown)
            {
                if (_dragActive)
                {
                    Vector2 end = hits
                        ? new Vector2(Mathf.RoundToInt(norm.x * webViewWidth),
                                      Mathf.RoundToInt((1f - norm.y) * webViewHeight))
                        : _dragStartPx;
                    int px = (int)end.x, py = (int)end.y;
                    JS($"(function(){{" +
                       $"var t=window.__mxInkDragEl||{JsTarget(px, py)};" +
                       $"var opt={{bubbles:true,cancelable:true,clientX:{px},clientY:{py},screenX:{px},screenY:{py},button:0,buttons:0}};" +
                       $"t.dispatchEvent(new PointerEvent('pointerup',Object.assign({{pointerId:1,pointerType:'pen',pressure:0}},opt)));" +
                       $"t.dispatchEvent(new MouseEvent('mouseup',opt));" +
                       $"t.dispatchEvent(new MouseEvent('click',opt));" +
                       $"window.__mxInkDragEl=null;" +
                       $"}})()");
                    Debug.Log($"[MxInkWebViewBridge] Drag end ({px},{py})");
                    _dragActive = false;
                }
                // Quick release without drag = gesture tap; no WebView events fired.
                _frontPressTime = -1f;
            }

            _wasFrontDown = down;
        }

        private void UpdateRayVisual()
        {
            if (rayLine == null) return;
            if (_stylus.TipPosition == Vector3.zero || chartRawImage == null)
            {
                rayLine.enabled = false;
                return;
            }

            // Draw a short "drop line" from the stylus tip perpendicular to the panel,
            // landing at the projected cursor point. This visualises the position-only
            // cursor model (rotation is ignored).
            Vector3 tipWorld = trackingSpaceRoot != null
                ? trackingSpaceRoot.TransformPoint(_stylus.TipPosition)
                : _stylus.TipPosition;

            Vector3 panelNormal = chartRawImage.rectTransform.forward;
            Vector3 panelOrigin = chartRawImage.rectTransform.position;
            float signedDist = Vector3.Dot(tipWorld - panelOrigin, panelNormal);
            Vector3 projected = tipWorld - signedDist * panelNormal;

            bool hitsChart = _frameNorm != Vector2.zero;

            rayLine.enabled = true;
            rayLine.useWorldSpace = true;
            rayLine.positionCount = 2;
            rayLine.SetPosition(0, tipWorld);
            rayLine.SetPosition(1, projected);
            Color c = hitsChart ? rayHitColor : rayMissColor;
            rayLine.startColor = c;
            rayLine.endColor   = c;
        }

        // ── Pressure ──────────────────────────────────────────────────────

        private void StreamPressure()
        {
            float p = _stylus.TipPressure;
            if (p > 0.01f)
                JS($"if(window.setMXInkPressure)setMXInkPressure({p:F2})");
        }

        // ── Direct JS pointer injection ────────────────────────────────────
        //
        // Raycasts from the stylus aim pose onto the ChartRawImage plane,
        // converts the hit to WebView pixel coords, then dispatches DOM events
        // inside the WebView via EvaluateJavascript. This sidesteps the entire
        // OVR Input Module → EventSystem → PointerEventSource chain.

        private void UpdatePointer()
        {
            // Skip when the stylus isn't actually tracked — tip=(0,0,0) produces
            // spurious hover events that point at the panel centre.
            if (_stylus.TipPosition == Vector3.zero) return;

            Vector2 norm = _frameNorm;
            bool hits = norm != Vector2.zero;

            if (debugLog && Time.unscaledTime - _lastDebugTime > 1f)
            {
                _lastDebugTime = Time.unscaledTime;

                Vector3 tipWorld = trackingSpaceRoot != null
                    ? trackingSpaceRoot.TransformPoint(_stylus.TipPosition)
                    : _stylus.TipPosition;
                Quaternion rotWorld = trackingSpaceRoot != null
                    ? trackingSpaceRoot.rotation * _stylus.TipRotation
                    : _stylus.TipRotation;
                Vector3 dir = rotWorld * Vector3.forward;

                string chartInfo = chartRawImage != null
                    ? $"chartPos={chartRawImage.rectTransform.position:F2} " +
                      $"chartFwd={chartRawImage.rectTransform.forward:F2}"
                    : "chartRawImage=NULL";

                Debug.Log($"[MxInkWebViewBridge] tipLocal={_stylus.TipPosition:F2} " +
                          $"tipWorld={tipWorld:F2} dirWorld={dir:F2} " +
                          $"mxInk={_stylus.IsMXInkActive} press={_stylus.TipPressure:F2} " +
                          $"dist={_tipDistToPanel:F3}m uv={norm} hits={hits} | {chartInfo}");
            }

            if (hits)
            {
                // UV → pixel coords. UV Y=0 is bottom; WebView Y=0 is top → flip Y.
                int px = Mathf.RoundToInt(norm.x * webViewWidth);
                int py = Mathf.RoundToInt((1f - norm.y) * webViewHeight);

                // Throttled mousemove so we don't flood the WebView thread.
                float now = Time.unscaledTime;
                bool moved = Mathf.Abs(px - _lastHoverPx.x) > 1 ||
                             Mathf.Abs(py - _lastHoverPx.y) > 1;

                if (moved && now - _lastHoverTime >= hoverThrottleSeconds)
                {
                    _lastHoverTime = now;
                    _lastHoverPx   = new Vector2(px, py);
                    // Inject both pointermove (draw canvas) and mousemove (tooltip system).
                    JS($"(function(){{" +
                       $"var t={JsTarget(px, py)};" +
                       $"var opt={{bubbles:true,cancelable:true,clientX:{px},clientY:{py},screenX:{px},screenY:{py}}};" +
                       $"t.dispatchEvent(new PointerEvent('pointermove',Object.assign({{pointerId:1,pointerType:'pen',pressure:0.5}},opt)));" +
                       $"document.dispatchEvent(new MouseEvent('mousemove',opt));" +
                       $"}})()");
                }

                // Tip counts as pressed when EITHER it is close to the panel plane
                // OR hardware pressure crosses the threshold (fingers pressing the tip).
                // Hysteresis on the release side prevents micro-jitter re-firing.
                float pressDist = tipProximityMeters;
                float releaseDist = tipProximityMeters + tipProximityHysteresis;
                bool proximityNow = _wasClickPressed
                    ? _tipDistToPanel <= releaseDist
                    : _tipDistToPanel <= pressDist;
                bool pressureNow = _stylus.TipPressure >= clickPressureThreshold;
                bool clickNow = proximityNow || pressureNow;

                // Suppress click injection while the front button handles drag/draw,
                // so the drag's own pointerup + click at release doesn't fire twice.
                bool frontBusy = _wasFrontDown || _dragActive;

                if (clickNow && !_wasClickPressed && !frontBusy)
                    InjectClick(px, py);
                _wasClickPressed = clickNow;
            }
            else
            {
                _wasClickPressed = false;
            }
        }

        private void InjectClick(int px, int py)
        {
            // Dispatch pointer + mouse events so both draw-canvas (pointerdown/up)
            // and toolbar buttons (onclick/click) receive the interaction.
            JS($"(function(){{" +
               $"var t={JsTarget(px, py)};" +
               $"var opt={{bubbles:true,cancelable:true,clientX:{px},clientY:{py},screenX:{px},screenY:{py},button:0,buttons:1}};" +
               $"var popt=Object.assign({{pointerId:1,pointerType:'pen',pressure:0.5}},opt);" +
               $"t.dispatchEvent(new PointerEvent('pointerdown',popt));" +
               $"t.dispatchEvent(new MouseEvent('mousedown',opt));" +
               $"t.dispatchEvent(new PointerEvent('pointerup',Object.assign({{}},popt,{{buttons:0,pressure:0}})));" +
               $"t.dispatchEvent(new MouseEvent('mouseup',Object.assign({{}},opt,{{buttons:0}})));" +
               $"t.dispatchEvent(new MouseEvent('click',Object.assign({{}},opt,{{buttons:0}})));" +
               $"}})()");
            Debug.Log($"[MxInkWebViewBridge] Click injected → WebView ({px},{py})");
        }

        // Barrel pan via front-hold was removed — front-hold is now reserved for
        // freehand drawing / drag (see UpdateDrag). Chart panning is still available
        // through the WebView's own pointer handling once a drag begins on the chart.

        // ── Raycasting ────────────────────────────────────────────────────

        /// <summary>
        /// Returns normalised [0,1] UV of the stylus tip orthogonally projected onto the
        /// ChartRawImage plane, or Vector2.zero if the projection falls outside the rect.
        ///
        /// Uses tip POSITION only — rotation is ignored. This makes clicking independent of
        /// the MX Ink aim/grip-pose problem: move the stylus around in space to slide a
        /// cursor across the panel, like a mouse on a desk.
        /// </summary>
        private Vector2 GetNormalisedStylusOnCanvas()
        {
            if (chartRawImage == null) return Vector2.zero;

            Vector3 tipWorld = trackingSpaceRoot != null
                ? trackingSpaceRoot.TransformPoint(_stylus.TipPosition)
                : _stylus.TipPosition;

            // World → RawImage local space. Local Z is distance from the panel plane;
            // X,Y are position on the panel — exactly what we want.
            Vector3 local3 = chartRawImage.rectTransform.InverseTransformPoint(tipWorld);
            Rect r = chartRawImage.rectTransform.rect;
            float u = (local3.x - r.x) / r.width;
            float v = (local3.y - r.y) / r.height;

            if (u < 0f || u > 1f || v < 0f || v > 1f) return Vector2.zero;
            return new Vector2(u, v);
        }

        // ── Button event handlers ─────────────────────────────────────────

        // Indicator cycle order for front single tap:
        // EMA20 → EMA50 → BB → VWAP → RSI → MACD → Stoch → PDH/L → wraps
        private static readonly string[] IndicatorJs =
        {
            "toggleEMA(20)",
            "toggleEMA(50)",
            "toggleBB()",
            "toggleVWAP()",
            "toggleRSI()",
            "toggleMACD()",
            "toggleStoch()",
            "togglePDHL()",
        };

        // Symbol cycle order for front triple tap:
        // BTC → ETH → SOL → BNB → XRP → DOGE → wraps
        private static readonly string[] Symbols =
        {
            "BTCUSDT", "ETHUSDT", "SOLUSDT", "BNBUSDT", "XRPUSDT", "DOGEUSDT",
        };

        private void OnFrontSingleTap()
        {
            // Advance to the next indicator and toggle it.
            _indicatorIndex = (_indicatorIndex + 1) % IndicatorJs.Length;
            string fn = IndicatorJs[_indicatorIndex];
            JS(fn);
            Debug.Log($"[MxInkWebViewBridge] Indicator toggle → {fn}");
        }

        private void OnFrontDoubleTap()
        {
            // Cycle draw tools: none → Trend → H-Line → Fib → Rect → Pen → Position → none.
            JS("if(window.mxInkDoubleTap)mxInkDoubleTap()");
        }

        private void OnFrontTripleTap()
        {
            // Cycle trading pair: BTC → ETH → SOL → BNB → XRP → DOGE → wraps.
            _symbolIndex = (_symbolIndex + 1) % Symbols.Length;
            string pair = Symbols[_symbolIndex];
            JS($"setPair('{pair}')");
            Debug.Log($"[MxInkWebViewBridge] Symbol cycle → {pair}");
        }

        private void OnBackSingleTap()
        {
            // Undo last drawing.
            JS("if(window.mxInkBarrelPress)mxInkBarrelPress()");
        }

        private void OnBackDoubleTap()
        {
            // Cycle timeframe: 1m → 5m → 15m → 1h → 4h → 1D → 1W → repeat.
            JS("if(window.setTimeframeByResolution){" +
               "var tfs=['1','5','15','60','240','D','W'];" +
               "var cur=window._mxTfIdx||0;" +
               "cur=(cur+1)%tfs.length;" +
               "window._mxTfIdx=cur;" +
               "setTimeframeByResolution(tfs[cur]);}");
        }

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns a JS expression that evaluates to the topmost interactive element at
        /// (x,y) — button, anchor, input, role=button, or cursor:pointer — falling back
        /// to the first element in the stack, then document.body.
        ///
        /// Uses document.elementsFromPoint (plural) so chart-canvas overlays don't hide
        /// toolbar buttons that sit underneath them in the stacking order.
        /// </summary>
        private static string JsTarget(int px, int py) =>
            $"(function(x,y){{" +
            $"var all=(document.elementsFromPoint||document.msElementsFromPoint).call(document,x,y)||[];" +
            $"for(var i=0;i<all.length;i++){{" +
            $"var e=all[i],tag=(e.tagName||'').toLowerCase();" +
            $"if(tag==='button'||tag==='a'||tag==='input'||tag==='select'||tag==='textarea'||" +
            $"e.getAttribute('role')==='button'||e.getAttribute('tabindex')!=null&&e.getAttribute('tabindex')>=0||" +
            $"window.getComputedStyle(e).cursor==='pointer')return e;" +
            $"}}return all[0]||document.body;}})({px},{py})";

        private void JS(string js) => _webView.InjectJavaScript(js);

        private void OnDestroy()
        {
            if (_stylus == null) return;
            _stylus.OnFrontButtonPressed      -= OnFrontSingleTap;
            _stylus.OnToolCycleRequested      -= OnFrontDoubleTap;
            _stylus.OnSymbolCycleRequested    -= OnFrontTripleTap;
            _stylus.OnUndoPressed             -= OnBackSingleTap;
            _stylus.OnTimeframeCycleRequested -= OnBackDoubleTap;
        }
    }
}
