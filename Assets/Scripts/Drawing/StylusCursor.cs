using UnityEngine;
using Fluxa.Input;

namespace Fluxa.Drawing
{
    /// <summary>
    /// Renders a small dot / reticle on the chart surface where the stylus raycast hits.
    /// Scales with pressure and changes color when drawing vs hovering.
    /// Attach to the FLUXAPanel or any scene object.
    /// </summary>
    public class StylusCursor : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────
        [Header("Appearance")]
        [SerializeField] private float baseScale   = 0.003f;
        [SerializeField] private float maxScale    = 0.008f;
        [SerializeField] private Color hoverColor  = new Color(0f, 0.74f, 0.83f, 0.6f);   // #00bcd4
        [SerializeField] private Color drawColor   = new Color(1f, 0.85f, 0.2f, 0.9f);     // yellow

        [Header("Offset")]
        [Tooltip("Small offset along the surface normal to prevent z-fighting.")]
        [SerializeField] private float normalOffset = 0.0005f;

        // ── Runtime ───────────────────────────────────────────────────────
        private GameObject _cursorObj;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private ChartDrawingManager _drawManager;
        private StylusInputManager _stylus;
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        // ─────────────────────────────────────────────────────────────────
        private void Start()
        {
            _stylus      = StylusInputManager.Instance;
            _drawManager = FindAnyObjectByType<ChartDrawingManager>();

            CreateCursorVisual();
            _mpb = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            if (_drawManager == null || _stylus == null || _cursorObj == null)
            {
                if (_cursorObj != null) _cursorObj.SetActive(false);
                return;
            }

            if (_drawManager.HasHit)
            {
                _cursorObj.SetActive(true);

                // Position at hit point, slightly off the surface
                _cursorObj.transform.position = _drawManager.HitPoint
                    + _drawManager.HitNormal * normalOffset;

                // Orient flat against the surface
                _cursorObj.transform.rotation =
                    Quaternion.LookRotation(-_drawManager.HitNormal);

                // Scale with pressure
                float pressure = _stylus.TipPressure;
                float s = Mathf.Lerp(baseScale, maxScale, pressure);
                _cursorObj.transform.localScale = new Vector3(s, s, s * 0.1f);

                // Color: drawing vs hovering
                Color c = _stylus.IsDrawing ? drawColor : hoverColor;
                _mpb.SetColor(ColorProp, c);
                _renderer.SetPropertyBlock(_mpb);
            }
            else
            {
                _cursorObj.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_cursorObj != null)
                Destroy(_cursorObj);
        }

        // ── Visual setup ──────────────────────────────────────────────────

        private void CreateCursorVisual()
        {
            _cursorObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _cursorObj.name = "StylusCursor";
            _cursorObj.transform.localScale = Vector3.one * baseScale;

            // Remove the default SphereCollider IMMEDIATELY so it can't block raycasts
            var col = _cursorObj.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            _renderer = _cursorObj.GetComponent<MeshRenderer>();

            // Use an unlit transparent material
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = hoverColor;
            _renderer.material = mat;

            _cursorObj.SetActive(false);
        }
    }
}
