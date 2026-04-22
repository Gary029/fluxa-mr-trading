using UnityEngine;

/// <summary>
/// Prevents the Meta XR Simulator from going to sleep due to low framerate
/// or inactivity on slow hardware (e.g. GTX 1050 Ti).
///
/// Attach to any GameObject in SampleScene (e.g. the Camera Rig).
/// Remove or disable before shipping to Quest 3 — not needed on real hardware.
/// </summary>
public class SimulatorKeepAlive : MonoBehaviour
{
    [Tooltip("Cap framerate to avoid overwhelming a slow GPU. 36 = stable on GTX 1050 Ti.")]
    [SerializeField] private int targetFrameRate = 36;

    [Tooltip("Disable VSync so targetFrameRate is respected.")]
    [SerializeField] private bool disableVSync = true;

    void Awake()
    {
        if (disableVSync)
            QualitySettings.vSyncCount = 0;

        Application.targetFrameRate = targetFrameRate;

        // Keep the app running even when the Unity Editor window loses focus
        Application.runInBackground = true;

        // Prevent screen dimming / sleep
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        Debug.Log($"[SimulatorKeepAlive] targetFrameRate={targetFrameRate}, runInBackground=true, sleepTimeout=NeverSleep");
    }

#if UNITY_EDITOR
    void Update()
    {
        // Ping OVRManager every frame to signal the XR session is alive.
        // This prevents the Meta XR Simulator from transitioning to IDLE/Sleep
        // when the GPU is slow and frames take too long.
        if (OVRManager.instance != null)
        {
            // Accessing hasVrFocus keeps the OVR heartbeat active in the simulator.
            var _ = OVRManager.hasVrFocus;
        }
    }
#endif
}
