using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Fluxa.UI
{
    /// <summary>
    /// Configures the FLUXA panel Canvas and EventSystem for XR Interaction Toolkit.
    ///
    /// Replaces OVRRaycaster → TrackedDeviceGraphicRaycaster
    /// Replaces OVRInputModule → XRUIInputModule
    ///
    /// Note: chart interaction goes through direct JS injection (MxInkWebViewBridge),
    /// not Unity's EventSystem. This setup covers any native Unity UI on the Canvas.
    /// </summary>
    public class VRUISetup : MonoBehaviour
    {
        private void Awake()
        {
            SetupCanvas();
            SetupEventSystem();
        }

        private void SetupCanvas()
        {
            var panel = GameObject.Find("FLUXAPanel");
            if (panel == null)
            {
                Debug.LogWarning("[VRUISetup] FLUXAPanel not found in scene.");
                return;
            }

            var canvasTransform = panel.transform.Find("Canvas");
            if (canvasTransform == null)
            {
                Debug.LogWarning("[VRUISetup] Canvas not found under FLUXAPanel.");
                return;
            }

            var canvasGO = canvasTransform.gameObject;

            // Remove any existing raycasters (GraphicRaycaster, OVRRaycaster, etc.)
            foreach (var r in canvasGO.GetComponents<BaseRaycaster>())
            {
                if (r is TrackedDeviceGraphicRaycaster) continue;
                string typeName = r.GetType().Name;
                DestroyImmediate(r);
                Debug.Log($"[VRUISetup] Removed {typeName} from Canvas.");
            }

            if (canvasGO.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            {
                canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
                Debug.Log("[VRUISetup] Added TrackedDeviceGraphicRaycaster to Canvas.");
            }
        }

        private void SetupEventSystem()
        {
            var eventSystem = FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                Debug.LogWarning("[VRUISetup] No EventSystem found in scene.");
                return;
            }

            var esGO = eventSystem.gameObject;

            // Remove all existing input modules except XRUIInputModule
            foreach (var module in esGO.GetComponents<BaseInputModule>())
            {
                if (module is XRUIInputModule) continue;
                string typeName = module.GetType().Name;
                DestroyImmediate(module);
                Debug.Log($"[VRUISetup] Removed {typeName} from EventSystem.");
            }

            if (esGO.GetComponent<XRUIInputModule>() == null)
            {
                esGO.AddComponent<XRUIInputModule>();
                Debug.Log("[VRUISetup] Added XRUIInputModule to EventSystem.");
            }
        }
    }
}
