using UnityEngine;
using UnityEngine.UI;
using Fluxa.Input;
using Fluxa.Drawing;
using Fluxa.WebView;

namespace Fluxa.UI
{
    /// <summary>
    /// Creates a toolbar on the FLUXA panel with timeframe buttons and drawing tool buttons.
    ///
    /// Button clicks are handled natively by Unity's UI system via OVRInputModule +
    /// OVRRaycaster (configured by VRUISetup). This script only builds the UI and
    /// manages visual state.
    ///
    /// Double-tap shortcuts (OnTimeframeCycleRequested / OnToolCycleRequested)
    /// provide bonus quick-access via MX Ink physical buttons.
    /// </summary>
    public class TimeframeController : MonoBehaviour
    {
        // ── Configuration ────────────────────────────────────────────────
        [System.Serializable]
        public struct TimeframeOption
        {
            public string label;
            public string resolution;
        }

        [Header("Timeframes")]
        [SerializeField] private TimeframeOption[] timeframes = new TimeframeOption[]
        {
            new TimeframeOption { label = "1m",  resolution = "1" },
            new TimeframeOption { label = "5m",  resolution = "5" },
            new TimeframeOption { label = "15m", resolution = "15" },
            new TimeframeOption { label = "1H",  resolution = "60" },
            new TimeframeOption { label = "4H",  resolution = "240" },
            new TimeframeOption { label = "1D",  resolution = "D" },
            new TimeframeOption { label = "1W",  resolution = "W" },
        };

        [Header("Appearance")]
        [SerializeField] private int fontSize = 11;
        [SerializeField] private float buttonWidth = 42f;
        [SerializeField] private float buttonHeight = 24f;
        [SerializeField] private float buttonSpacing = 4f;
        [SerializeField] private float rowHeight = 30f;
        [SerializeField] private float rowPadding = 3f;

        [Header("Colors")]
        [SerializeField] private Color normalBg    = new Color(0.13f, 0.15f, 0.18f, 1f);
        [SerializeField] private Color normalText   = new Color(0.55f, 0.58f, 0.62f, 1f);
        [SerializeField] private Color activeBg     = new Color(0f, 0.74f, 0.83f, 1f);
        [SerializeField] private Color activeText   = new Color(0.05f, 0.07f, 0.09f, 1f);
        [SerializeField] private Color rowBgColor   = new Color(0.086f, 0.106f, 0.133f, 1f);
        [SerializeField] private Color toolActiveBg   = new Color(0.12f, 0.23f, 0.24f, 1f);
        [SerializeField] private Color toolActiveText = new Color(0f, 0.74f, 0.83f, 1f);

        // ── Runtime ──────────────────────────────────────────────────────
        private FluxaWebViewManager _webViewManager;
        private ChartDrawingManager _drawingManager;
        private StylusInputManager  _stylus;

        private Button[] _tfButtons;
        private Button[] _toolButtons;
        private int _activeTimeframeIndex = 0;

        private RectTransform _toolbarRect;

        // ─────────────────────────────────────────────────────────────────
        private void Start()
        {
            ResolveManagers();
            BuildUI();

            // Subscribe to double-tap shortcuts (bonus quick-access)
            if (_stylus != null)
                _stylus.OnTimeframeCycleRequested += CycleTimeframe;
        }

        private void ResolveManagers()
        {
            _webViewManager = FluxaWebViewManager.Instance;
            if (_webViewManager == null)
                _webViewManager = FindAnyObjectByType<FluxaWebViewManager>();

            _drawingManager = FindAnyObjectByType<ChartDrawingManager>();
            _stylus = StylusInputManager.Instance;

            if (_drawingManager != null)
            {
                _drawingManager.OnToolModeChanged += OnToolModeChangedExternally;
                Debug.Log("[TimeframeController] Subscribed to ChartDrawingManager.OnToolModeChanged");
            }
            else
            {
                Debug.LogWarning("[TimeframeController] ChartDrawingManager NOT found at Start — " +
                                 "will attempt late-binding in UpdateToolVisuals");
            }

            Debug.Log($"[TimeframeController] ResolveManagers: drawMgr={(_drawingManager != null)}, " +
                      $"stylus={(_stylus != null)}, webView={(_webViewManager != null)}");
        }

        private void OnDestroy()
        {
            if (_drawingManager != null)
                _drawingManager.OnToolModeChanged -= OnToolModeChangedExternally;

            if (_stylus != null)
                _stylus.OnTimeframeCycleRequested -= CycleTimeframe;
        }

        // ═════════════════════════════════════════════════════════════════
        //  TIMEFRAME CYCLING (back-button double-tap bonus shortcut)
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Advance to the next timeframe and apply it.
        /// Wraps around to the first timeframe after the last.
        /// </summary>
        public void CycleTimeframe()
        {
            if (timeframes.Length == 0) return;

            int next = (_activeTimeframeIndex + 1) % timeframes.Length;
            OnTimeframeClicked(next);

            Debug.Log($"[TimeframeController] Cycled timeframe -> {timeframes[_activeTimeframeIndex].label}");
        }

        // ═════════════════════════════════════════════════════════════════
        //  UI CONSTRUCTION
        // ═════════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            Transform canvasTransform = FindPanelCanvas();
            if (canvasTransform == null)
            {
                Debug.LogWarning("[TimeframeController] Could not find FLUXAPanel Canvas.");
                return;
            }

            Transform titleBar = canvasTransform.Find("TitleBar");
            Transform chartSection = canvasTransform.Find("ChartSection");

            // ── Row container ────────────────────────────────────────────
            var rowGO = new GameObject("ToolbarRow");
            rowGO.transform.SetParent(canvasTransform, false);

            _toolbarRect = rowGO.AddComponent<RectTransform>();
            _toolbarRect.anchorMin = new Vector2(0f, 1f);
            _toolbarRect.anchorMax = new Vector2(1f, 1f);

            float titleBarHeight = 40f;
            if (titleBar != null)
            {
                var tbRect = titleBar as RectTransform;
                if (tbRect != null && tbRect.sizeDelta.y > 0)
                    titleBarHeight = tbRect.sizeDelta.y;
            }

            _toolbarRect.pivot = new Vector2(0.5f, 1f);
            _toolbarRect.anchoredPosition = new Vector2(0f, -titleBarHeight);
            _toolbarRect.sizeDelta = new Vector2(0f, rowHeight);

            var rowBg = rowGO.AddComponent<Image>();
            rowBg.color = rowBgColor;
            rowBg.raycastTarget = false;

            var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = buttonSpacing;
            hlg.padding = new RectOffset(
                Mathf.RoundToInt(rowPadding * 3f),
                Mathf.RoundToInt(rowPadding * 3f),
                Mathf.RoundToInt(rowPadding),
                Mathf.RoundToInt(rowPadding));
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;

            // Adjust ChartSection top
            if (chartSection != null)
            {
                var csRect = chartSection as RectTransform;
                if (csRect != null)
                {
                    csRect.offsetMax = new Vector2(csRect.offsetMax.x, csRect.offsetMax.y - rowHeight);
                    Debug.Log($"[TimeframeController] Adjusted ChartSection top by -{rowHeight}px");
                }
            }

            if (titleBar != null)
                rowGO.transform.SetSiblingIndex(titleBar.GetSiblingIndex() + 1);

            // ── Timeframe buttons ────────────────────────────────────────
            _tfButtons = new Button[timeframes.Length];
            for (int i = 0; i < timeframes.Length; i++)
            {
                int idx = i;
                _tfButtons[i] = CreateButton(
                    rowGO.transform, timeframes[i].label, buttonWidth,
                    () => OnTimeframeClicked(idx));
            }

            // ── Divider ──────────────────────────────────────────────────
            CreateDivider(rowGO.transform);

            // ── Tool buttons ─────────────────────────────────────────────
            var tools = new (string label, DrawToolMode mode)[]
            {
                ("\u270E", DrawToolMode.Freehand),       // pencil
                ("\u2571", DrawToolMode.TrendLine),       // diagonal
                ("\u2500", DrawToolMode.HorizontalLine),  // horizontal
            };

            _toolButtons = new Button[tools.Length];
            for (int i = 0; i < tools.Length; i++)
            {
                DrawToolMode mode = tools[i].mode;
                _toolButtons[i] = CreateButton(
                    rowGO.transform, tools[i].label, 30f,
                    () => OnToolClicked(mode));
            }

            // ── Force layout ────────────────────────────────────────────
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_toolbarRect);

            UpdateTimeframeVisuals();
            UpdateToolVisuals();

            Debug.Log($"[TimeframeController] Built toolbar: {timeframes.Length} TF + {tools.Length} tools. " +
                      $"Clicks handled by OVRInputModule via OVRRaycaster.");
        }

        private Button CreateButton(Transform parent, string label, float width,
            System.Action onClick)
        {
            var btnGO = new GameObject($"Btn_{label}");
            btnGO.transform.SetParent(parent, false);

            var btnRect = btnGO.AddComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(width, buttonHeight);

            // Background image — also serves as the raycast target for OVRRaycaster
            var btnImage = btnGO.AddComponent<Image>();
            btnImage.color = normalBg;
            btnImage.raycastTarget = true;

            // Standard Unity UI Button — OVRInputModule fires onClick natively
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = btnImage;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.1f;
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick());

            // Text child
            var textGO = new GameObject("Label");
            textGO.transform.SetParent(btnGO.transform, false);

            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.anchoredPosition = Vector2.zero;

            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = normalText;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;

            return btn;
        }

        private void CreateDivider(Transform parent)
        {
            var divGO = new GameObject("Divider");
            divGO.transform.SetParent(parent, false);

            var rect = divGO.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1f, buttonHeight * 0.7f);

            var img = divGO.AddComponent<Image>();
            img.color = new Color(0.19f, 0.21f, 0.24f, 1f);
            img.raycastTarget = false;
        }

        private Transform FindPanelCanvas()
        {
            var panel = GameObject.Find("FLUXAPanel");
            if (panel == null) return null;
            return panel.transform.Find("Canvas");
        }

        // ═════════════════════════════════════════════════════════════════
        //  CLICK HANDLERS
        // ═════════════════════════════════════════════════════════════════

        private void OnTimeframeClicked(int index)
        {
            if (index == _activeTimeframeIndex) return;

            _activeTimeframeIndex = index;
            string tf = ResolutionToTf(timeframes[index].resolution);

            Debug.Log($"[TimeframeController] Timeframe -> {timeframes[index].label} (tf={tf})");

            if (_webViewManager == null)
            {
                _webViewManager = FluxaWebViewManager.Instance;
                if (_webViewManager == null)
                    _webViewManager = FindAnyObjectByType<FluxaWebViewManager>();
            }

            if (_webViewManager != null)
                _webViewManager.SetTimeframe(tf);
            else
                Debug.LogWarning("[TimeframeController] FluxaWebViewManager not found.");

            UpdateTimeframeVisuals();
        }

        private void OnToolClicked(DrawToolMode mode)
        {
            if (_drawingManager == null)
                _drawingManager = FindAnyObjectByType<ChartDrawingManager>();

            if (_drawingManager != null)
                _drawingManager.SetToolMode(mode);

            UpdateToolVisuals();
        }

        private void OnToolModeChangedExternally(DrawToolMode mode)
        {
            Debug.Log($"[TimeframeController] OnToolModeChanged event received: mode={mode}");
            UpdateToolVisuals();
        }

        // ═════════════════════════════════════════════════════════════════
        //  VISUAL UPDATES
        // ═════════════════════════════════════════════════════════════════

        private void UpdateTimeframeVisuals()
        {
            if (_tfButtons == null) return;

            for (int i = 0; i < _tfButtons.Length; i++)
            {
                if (_tfButtons[i] == null) continue;
                bool active = (i == _activeTimeframeIndex);
                SetButtonColors(_tfButtons[i], active ? activeBg : normalBg,
                                                active ? activeText : normalText);
            }
        }

        private void UpdateToolVisuals()
        {
            if (_toolButtons == null)
            {
                Debug.LogWarning("[TimeframeController] UpdateToolVisuals: _toolButtons is null");
                return;
            }

            // Lazy-resolve if _drawingManager wasn't available at Start
            if (_drawingManager == null)
            {
                _drawingManager = FindAnyObjectByType<ChartDrawingManager>();
                if (_drawingManager != null)
                {
                    _drawingManager.OnToolModeChanged += OnToolModeChangedExternally;
                    Debug.Log("[TimeframeController] Late-bound to ChartDrawingManager.OnToolModeChanged");
                }
            }

            DrawToolMode current = _drawingManager != null
                ? _drawingManager.CurrentToolMode
                : DrawToolMode.Freehand;

            Debug.Log($"[TimeframeController] UpdateToolVisuals: current={current}, " +
                      $"buttonCount={_toolButtons.Length}, drawMgr={((_drawingManager != null) ? "found" : "NULL")}");

            var modes = new[] { DrawToolMode.Freehand, DrawToolMode.TrendLine, DrawToolMode.HorizontalLine };
            for (int i = 0; i < _toolButtons.Length && i < modes.Length; i++)
            {
                if (_toolButtons[i] == null)
                {
                    Debug.LogWarning($"[TimeframeController] _toolButtons[{i}] is null!");
                    continue;
                }
                bool active = (modes[i] == current);
                SetButtonColors(_toolButtons[i], active ? toolActiveBg : normalBg,
                                                  active ? toolActiveText : normalText);
            }
        }

        private static void SetButtonColors(Button btn, Color bg, Color text)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = bg;
            var txt = btn.GetComponentInChildren<Text>();
            if (txt != null) txt.color = text;
        }

        private static string ResolutionToTf(string resolution)
        {
            switch (resolution)
            {
                case "1":   return "1m";
                case "5":   return "5m";
                case "15":  return "15m";
                case "60":  return "1h";
                case "240": return "4h";
                case "D":   return "1d";
                case "W":   return "1w";
                default:    return resolution;
            }
        }

        // ── Public API ───────────────────────────────────────────────────

        public void SetActiveTimeframe(string resolution)
        {
            for (int i = 0; i < timeframes.Length; i++)
            {
                if (timeframes[i].resolution == resolution)
                {
                    _activeTimeframeIndex = i;
                    UpdateTimeframeVisuals();
                    return;
                }
            }
        }
    }
}
