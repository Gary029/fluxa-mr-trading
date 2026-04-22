using System;
using System.IO;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Fluxa.WebView
{
    /// <summary>
    /// Wraps SimpleUnity3DWebView (WebView.WebViewManager) to display the FLUXA chart
    /// as a 3D texture on a RawImage in MR space.
    ///
    /// Add SIMPLE_WEBVIEW to Player Settings > Scripting Define Symbols after importing
    /// the SimpleUnity3DWebView package from https://github.com/t-34400/SimpleUnity3DWebView
    ///
    /// The library's WebViewManager component must be on the same GameObject.
    /// Configure it in the Inspector: assign the RawImage and PointerEventSource,
    /// and leave defaultUrl empty (we load chart.html ourselves after the file copy).
    /// </summary>
#if SIMPLE_WEBVIEW
    [RequireComponent(typeof(global::WebView.WebViewManager))]
#endif
    public class FluxaWebViewManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────
        public static FluxaWebViewManager Instance { get; private set; }

        // ── Inspector ─────────────────────────────────────────────────────
        [Tooltip("RawImage component on the FLUXA panel where the chart will be rendered.")]
        [SerializeField] private RawImage webViewRenderTarget;

        [Tooltip("Filename of the chart page inside StreamingAssets/WebView/")]
        [SerializeField] private string chartHtmlFileName = "chart.html";

        [SerializeField] private bool showOnAwake = true;

        // ── Runtime state ─────────────────────────────────────────────────
        private bool   _isVisible    = false;
        private bool   _chartLoaded  = false;
        private string _resolvedUrl  = null;

#if SIMPLE_WEBVIEW
        // The library's WebViewManager component on this same GameObject.
        // Handles Android WebView initialisation, texture rendering, and the Java bridge.
        private global::WebView.WebViewManager _libWebView;
#endif

        // ─────────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Auto-discover webViewRenderTarget if not assigned in the Inspector.
            if (webViewRenderTarget == null)
            {
                var panel = GameObject.Find("FLUXAPanel");
                if (panel != null)
                    webViewRenderTarget = panel.GetComponentInChildren<RawImage>(includeInactive: true);

                if (webViewRenderTarget != null)
                    Debug.Log("[FluxaWebViewManager] Auto-discovered RawImage on FLUXAPanel: " +
                              webViewRenderTarget.gameObject.name);
                else
                    Debug.LogWarning("[FluxaWebViewManager] webViewRenderTarget not assigned and no " +
                                     "RawImage found on FLUXAPanel. Chart will not render.");
            }

#if SIMPLE_WEBVIEW && !UNITY_EDITOR
            // Wire the library's required serialized fields BEFORE its Start() runs.
            // The library's Start() will NullRef if webViewImage or pointerEventSource are null.
            WireLibraryComponent();
#endif

#if UNITY_EDITOR
            Debug.Log("[FluxaWebViewManager] Running in Editor — WebView not available.\n" +
                      "Open Assets/StreamingAssets/WebView/chart.html in Chrome to preview the chart.\n" +
                      "To test on device: build to Quest 3 with SIMPLE_WEBVIEW define symbol set.");
#elif UNITY_ANDROID
            StartCoroutine(InitAndroid());
#else
            Debug.Log("[FluxaWebViewManager] WebView only supported on Android. " +
                      "Open Assets/StreamingAssets/WebView/chart.html in a browser to preview.");
#endif
        }

#if SIMPLE_WEBVIEW && !UNITY_EDITOR
        /// <summary>
        /// Wires the library's WebViewManager private serialized fields via reflection.
        /// Must run in Awake() — before the library's Start() — so it can initialize
        /// its Java bridge, texture, and frame update loop without NullReferenceExceptions.
        /// </summary>
        private void WireLibraryComponent()
        {
            var lib = GetComponent<global::WebView.WebViewManager>();
            if (lib == null) return;

            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var libType = typeof(global::WebView.WebViewManager);

            // 1) Wire webViewImage → our webViewRenderTarget
            if (webViewRenderTarget != null)
            {
                var wvImageField = libType.GetField("webViewImage", flags);
                if (wvImageField != null)
                {
                    var current = wvImageField.GetValue(lib) as RawImage;
                    if (current == null)
                    {
                        wvImageField.SetValue(lib, webViewRenderTarget);
                        Debug.Log("[FluxaWebViewManager] Wired library webViewImage → " +
                                  webViewRenderTarget.gameObject.name);
                    }
                }
            }

            // 2) Ensure a PointerEventSource exists on the RawImage and wire it
            if (webViewRenderTarget != null)
            {
                var pes = webViewRenderTarget.GetComponent<global::WebView.PointerEventSource>();
                if (pes == null)
                    pes = webViewRenderTarget.gameObject.AddComponent<global::WebView.PointerEventSource>();

                var pesField = libType.GetField("pointerEventSource", flags);
                if (pesField != null)
                {
                    var current = pesField.GetValue(lib);
                    if (current == null)
                    {
                        pesField.SetValue(lib, pes);
                        Debug.Log("[FluxaWebViewManager] Wired library pointerEventSource → " +
                                  webViewRenderTarget.gameObject.name);
                    }
                }
            }

            // 3) Clear defaultUrl so the library doesn't load its own page in Start().
            //    We load chart.html ourselves in InitAndroid() after copying from StreamingAssets.
            var urlField = libType.GetField("defaultUrl", flags);
            if (urlField != null)
                urlField.SetValue(lib, "");

            // 4) Ensure the RawImage has non-zero rect for the library's texture size calculation.
            //    With stretched anchors (0,0 → 1,1), both sizeDelta and rect.size can be zero
            //    before layout runs, causing textureHeight=0 → Java crash.
            if (webViewRenderTarget != null)
            {
                var rt = webViewRenderTarget.rectTransform;
                if (rt.rect.width <= 0 || rt.rect.height <= 0)
                {
                    // Switch to centered anchors so sizeDelta == pixel dimensions.
                    // With stretch anchors (0,0→1,1) sizeDelta is an inset, not a size,
                    // so setting it to (1024,600) on a zero-size parent yields a negative rect.
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(1024f, 600f);
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                    Debug.Log("[FluxaWebViewManager] ChartRawImage had zero rect — forced to 1024×600 " +
                              "centered anchors for ImageReader texture init.");
                }
            }
        }
#endif

        // ── Android init ──────────────────────────────────────────────────
        private IEnumerator InitAndroid()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // ── Resolve chart URL ────────────────────────────────────────────
            // Primary: file:///android_asset/ reads directly from the APK's assets/ directory
            // (which is where Unity places StreamingAssets content). This special scheme is
            // exempt from Android 14's default setAllowFileAccess(false) restriction.
            _resolvedUrl = "file:///android_asset/WebView/" + chartHtmlFileName;

#if SIMPLE_WEBVIEW
            // Get the library's WebViewManager (guaranteed present by [RequireComponent]).
            _libWebView = GetComponent<global::WebView.WebViewManager>();

            // Wait one frame so the library's Start() can run and create its
            // Java bridge, WebView, texture, and WebViewDataReceiver child.
            yield return null;

            // Enable file:// access on the Android WebView as a safety net.
            // file:///android_asset/ should work without this, but some OEM WebView
            // implementations (including Meta Quest browser) may still need it.
            _libWebView.EnableFileAccess();

            // Subscribe to JS→Unity messages via the library's WebViewDataReceiver child component.
            var receiver = GetComponentInChildren<global::WebView.WebViewDataReceiver>();
            if (receiver != null)
                receiver.DataReceived += OnLibDataReceived;
            else
                Debug.LogWarning("[FluxaWebViewManager] WebViewDataReceiver not found on children. " +
                                 "JS→Unity messaging will be unavailable.");

            LoadChart();

            if (showOnAwake)
                ShowChart();
            else
                HideChart();
#else
            Debug.LogWarning("[FluxaWebViewManager] SIMPLE_WEBVIEW not defined. " +
                             "Import SimpleUnity3DWebView then add SIMPLE_WEBVIEW to " +
                             "Player Settings > Other Settings > Scripting Define Symbols.");
            yield break;
#endif
#else
            yield break;
#endif
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>Load (or reload) chart.html into the WebView.</summary>
        public void LoadChart()
        {
#if SIMPLE_WEBVIEW && UNITY_ANDROID && !UNITY_EDITOR
            if (_libWebView != null && !string.IsNullOrEmpty(_resolvedUrl))
            {
                _libWebView.LoadUrl(_resolvedUrl);
                _chartLoaded = true;
                Debug.Log("[FluxaWebViewManager] Loading chart: " + _resolvedUrl);
            }
#else
            Debug.Log("[FluxaWebViewManager] LoadChart() — stub (platform/plugin not available).");
#endif
        }

        /// <summary>Show the chart RawImage and resume WebView texture updates.</summary>
        public void ShowChart()
        {
            _isVisible = true;
            if (webViewRenderTarget != null)
                webViewRenderTarget.gameObject.SetActive(true);

#if SIMPLE_WEBVIEW && UNITY_ANDROID && !UNITY_EDITOR
            // Enabling the library component triggers its OnEnable → bridge.StartUpdate().
            if (_libWebView != null)
                _libWebView.enabled = true;
#else
            Debug.Log("[FluxaWebViewManager] ShowChart() — stub.");
#endif
        }

        /// <summary>Hide the chart RawImage and suspend WebView texture updates.</summary>
        public void HideChart()
        {
            _isVisible = false;
            if (webViewRenderTarget != null)
                webViewRenderTarget.gameObject.SetActive(false);

#if SIMPLE_WEBVIEW && UNITY_ANDROID && !UNITY_EDITOR
            // Disabling the library component triggers its OnDisable → bridge.StopUpdate().
            if (_libWebView != null)
                _libWebView.enabled = false;
#else
            Debug.Log("[FluxaWebViewManager] HideChart() — stub.");
#endif
        }

        /// <summary>Tell the chart page to switch trading pair (e.g. "ETHUSDT").</summary>
        public void SetSymbol(string symbol)
        {
            EvalJS($"setSymbol('{EscapeJs(symbol)}')");
        }

        /// <summary>Tell the chart page to switch timeframe (e.g. "5m").</summary>
        public void SetTimeframe(string timeframe)
        {
            EvalJS($"setTimeframe('{EscapeJs(timeframe)}')");
        }

        /// <summary>Evaluate arbitrary JavaScript in the chart WebView. Used by MxInkWebViewBridge.</summary>
        public void InjectJavaScript(string js) => EvalJS(js);

        public bool IsVisible   => _isVisible;
        public bool ChartLoaded => _chartLoaded;

        public event Action<string> OnSymbolChangedEvent;
        public event Action<string> OnTimeframeChangedEvent;

        // ── JS→Unity callback ─────────────────────────────────────────────

        /// <summary>
        /// Receives JSON messages sent from JavaScript via Android.sendJsonData(json).
        /// The library routes these through WebViewDataReceiver.OnDataReceived → DataReceived event.
        /// ReceivedData fields: type (string), data (string).
        /// </summary>
        public void OnWebViewMessage(string json)
        {
            Debug.Log("[FluxaWebViewManager] Message from chart JS: " + json);
            try
            {
                var msg = JsonUtility.FromJson<WebViewMessage>(json);
                if (msg == null || string.IsNullOrEmpty(msg.data)) return;
                if (msg.data.StartsWith("pair:"))
                    OnSymbolChangedEvent?.Invoke(msg.data.Substring(5));
                else if (msg.data.StartsWith("tf:"))
                    OnTimeframeChangedEvent?.Invoke(msg.data.Substring(3));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FluxaWebViewManager] Failed to parse WebView message: " + e.Message);
            }
        }

        // ── Internal helpers ──────────────────────────────────────────────

#if SIMPLE_WEBVIEW
        private void OnLibDataReceived(global::WebView.ReceivedData data)
        {
            OnWebViewMessage(JsonUtility.ToJson(data));
        }
#endif

        private void EvalJS(string js)
        {
#if SIMPLE_WEBVIEW && UNITY_ANDROID && !UNITY_EDITOR
            if (_libWebView != null && _chartLoaded)
                _libWebView.EvaluateJavascript(js);
#else
            Debug.Log($"[FluxaWebViewManager] EvalJS (stub): {js}");
#endif
        }

        private static string EscapeJs(string s)
        {
            return s?.Replace("\\", "\\\\").Replace("'", "\\'") ?? string.Empty;
        }

        private void OnDestroy()
        {
#if SIMPLE_WEBVIEW && UNITY_ANDROID && !UNITY_EDITOR
            var receiver = GetComponentInChildren<global::WebView.WebViewDataReceiver>();
            if (receiver != null)
                receiver.DataReceived -= OnLibDataReceived;
#endif
            if (Instance == this)
                Instance = null;
        }
    }

    [System.Serializable]
    internal class WebViewMessage
    {
        public string type;
        public string data;
    }
}
