using UnityEngine;
using UnityEngine.UI;

namespace Fluxa.WebView
{
    /// <summary>
    /// Bridges WebViewManager with FLUXAPanel for SimpleUnity3DWebView.
    ///
    /// SimpleUnity3DWebView renders the chart as a texture onto a RawImage in 3D space,
    /// so there is no screen-space overlay to position. This class simply syncs the
    /// chart's visibility with the FLUXA panel and routes symbol/timeframe changes.
    ///
    /// Setup: Add this component to the FLUXAPanel root (or any scene GameObject).
    /// Assign the RawImage child of the panel in the Inspector, or let Start() find it.
    /// </summary>
    public class WebViewPanel : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────
        [Tooltip("FluxaWebViewManager singleton. Auto-resolved from scene if left empty.")]
        [SerializeField] private FluxaWebViewManager webViewManager;

        [Tooltip("RawImage on the FLUXA panel quad where the chart renders. " +
                 "Auto-discovered on the FLUXAPanel if left empty.")]
        [SerializeField] private RawImage chartRawImage;

        // ── Runtime state ─────────────────────────────────────────────────
        private FLUXAPanel _fluxaPanel;
        private bool       _lastPanelVisible = false;

        // ─────────────────────────────────────────────────────────────────
        private void Start()
        {
            // Resolve WebViewManager
            if (webViewManager == null)
                webViewManager = FluxaWebViewManager.Instance;

            if (webViewManager == null)
                Debug.LogWarning("[WebViewPanel] No FluxaWebViewManager found. " +
                                 "Run FLUXA → Setup WebView or assign manually in Inspector.");

            // Resolve FLUXAPanel
            var panelGO = GameObject.Find("FLUXAPanel");
            if (panelGO != null)
                _fluxaPanel = panelGO.GetComponent<FLUXAPanel>();

            // Auto-discover RawImage if not wired in Inspector
            if (chartRawImage == null && _fluxaPanel != null)
                chartRawImage = _fluxaPanel.GetComponentInChildren<RawImage>(includeInactive: true);

            // Create RawImage if still missing (fallback — prefer FLUXA → Setup WebView)
            if (chartRawImage == null && _fluxaPanel != null)
            {
                var riGO = new GameObject("ChartRawImage");
                riGO.transform.SetParent(_fluxaPanel.transform, false);
                chartRawImage = riGO.AddComponent<RawImage>();
                Debug.Log("[WebViewPanel] Created ChartRawImage on FLUXAPanel. " +
                          "Size it to cover the panel in the Inspector.");
            }

            // Wire RawImage into manager if it doesn't already have one
            if (chartRawImage != null && webViewManager != null)
            {
                var riField = typeof(FluxaWebViewManager)
                    .GetField("webViewRenderTarget",
                              System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (riField != null && riField.GetValue(webViewManager) == null)
                    riField.SetValue(webViewManager, chartRawImage);
            }

            // Sync initial visibility
            if (_fluxaPanel == null)
            {
                ShowChart();
            }
            else
            {
                _lastPanelVisible = _fluxaPanel.isVisible;
                if (_lastPanelVisible)
                    ShowChart();
                else
                    HideChart();
            }
        }

        private void Update()
        {
            if (_fluxaPanel == null || webViewManager == null) return;

            bool panelVisible = _fluxaPanel.gameObject.activeInHierarchy && _fluxaPanel.isVisible;
            if (panelVisible != _lastPanelVisible)
            {
                _lastPanelVisible = panelVisible;
                if (panelVisible) ShowChart();
                else              HideChart();
            }
        }

        // ── Public API ────────────────────────────────────────────────────

        public void ShowChart()
        {
            webViewManager?.ShowChart();
        }

        public void HideChart()
        {
            webViewManager?.HideChart();
        }

        /// <summary>Call to switch the chart's trading pair (e.g. "ETHUSDT").</summary>
        public void OnSymbolChanged(string symbol)
        {
            webViewManager?.SetSymbol(symbol);
        }

        /// <summary>Call to switch the chart's timeframe (e.g. "15m").</summary>
        public void OnTimeframeChanged(string tf)
        {
            webViewManager?.SetTimeframe(tf);
        }
    }
}
