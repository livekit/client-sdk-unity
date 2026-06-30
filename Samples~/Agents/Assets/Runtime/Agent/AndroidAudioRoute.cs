using System.Collections;
using UnityEngine;

namespace AgentsRPG
{
    // Pins Android's communication audio route to a hands-free output so WebRTC's ADM and
    // Unity's media engine target the same physical device.
    //
    // LiveKit platform audio hands the device to WebRTC, which switches Android into
    // MODE_IN_COMMUNICATION and, left alone, grabs the earpiece. Unity's audio engine keeps
    // its media stream on the loudspeaker. The two routes disagree, so the audio policy
    // ping-pongs the device — the continuous "AudioStreamLegacy onAudioDeviceUpdate ...
    // request DISCONNECT in data callback, devices 3 => 2" loop with broken playback.
    //
    // Selecting an explicit communication device (preferring a headset, falling back to the
    // built-in speaker — never the earpiece) settles the route: WebRTC's playback and Unity's
    // music share one output and the loop stops. The SDK can't do this for us — on Android it
    // treats SetPlayoutDevice as a no-op and leaves routing entirely to AudioManager (this is
    // the Android counterpart to the AVAudioSession DefaultToSpeaker option the SDK applies on
    // iOS, which has no Android analog baked in).
    //
    // Keeping the route correct for the whole call needs two triggers, because setCommunicationDevice
    // is an explicit manual selection the system then holds:
    //   1. OnCommunicationDeviceChangedListener fires when the *active* device changes — e.g. the
    //      selected device is removed and the policy falls back. It does NOT fire when a new device
    //      merely becomes available, because our pin keeps the active device unchanged.
    //   2. A once-a-second poll catches exactly that missed case (a headset connecting mid-call), so
    //      the route follows up to a better device too. AudioDeviceCallback would be the event-driven
    //      equivalent, but it's an abstract class and Unity's AndroidJavaProxy can only implement
    //      interfaces, so we poll instead.
    // Both funnel into ReconcileRoute, which re-pins only when the best available device differs from
    // the active one — so it's idempotent, adds no route churn, and can't loop on its own re-pins.
    //
    // No-op outside a real Android device.
    public static class AndroidAudioRoute
    {
        // AudioDeviceInfo type constants, in the order we prefer to route communication audio.
        // Earpiece (TYPE_BUILTIN_EARPIECE = 1) is intentionally excluded — this is hands-free.
        static readonly int[] PreferredTypes =
        {
            22, // TYPE_USB_HEADSET
            3,  // TYPE_WIRED_HEADSET
            4,  // TYPE_WIRED_HEADPHONES
            8,  // TYPE_BLUETOOTH_A2DP
            7,  // TYPE_BLUETOOTH_SCO
            2,  // TYPE_BUILTIN_SPEAKER
        };

#if UNITY_ANDROID && !UNITY_EDITOR
        // Held alive for the duration of the call so the Java proxy stays valid; also the handle
        // we need to unregister on Reset(). Non-null only while the API 31+ listener is registered.
        static CommDeviceChangedListener _listener;

        // Per-second poll that catches a higher-ranked device connecting mid-call (the listener
        // doesn't fire on availability changes). Non-null only while the route is pinned.
        static GameObject _pump;

        // Whether ForceHandsFree() currently wants the route pinned. Gates ReconcileRoute() so a
        // late callback or poll tick after Reset() can't fight us.
        static bool _routePinned;
#endif

        public static void ForceHandsFree()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                int sdkInt = version.GetStatic<int>("SDK_INT");

                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null)
                {
                    Debug.LogWarning("[AndroidAudioRoute] No currentActivity; cannot set audio route.");
                    return;
                }

                using var audioManager = activity.Call<AndroidJavaObject>("getSystemService", "audio");

                if (sdkInt >= 31)
                {
                    // API 31+: setSpeakerphoneOn is deprecated; pick the device explicitly.
                    _routePinned = true;
                    ReconcileRoute(audioManager);

                    // Trigger 1: re-pin when the policy changes the active device. getMainExecutor()
                    // delivers callbacks on the Android UI thread, which is JNI-attached and safe to
                    // call AudioManager from.
                    if (_listener == null)
                    {
                        _listener = new CommDeviceChangedListener();
                        using var executor = activity.Call<AndroidJavaObject>("getMainExecutor");
                        audioManager.Call("addOnCommunicationDeviceChangedListener", executor, _listener);
                        Debug.Log("[AndroidAudioRoute] Registered communication-device listener.");
                    }

                    // Trigger 2: poll for a better device connecting mid-call.
                    if (_pump == null)
                    {
                        _pump = new GameObject("AndroidAudioRoutePump") { hideFlags = HideFlags.HideAndDontSave };
                        Object.DontDestroyOnLoad(_pump);
                        _pump.AddComponent<RoutePump>();
                    }
                }
                else
                {
                    // Legacy: WebRTC has already entered communication mode, so routing to the
                    // loudspeaker is enough to keep both engines on one device.
                    audioManager.Call("setSpeakerphoneOn", true);
                    Debug.Log("[AndroidAudioRoute] setSpeakerphoneOn(true) (legacy path).");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AndroidAudioRoute] Failed to set hands-free route: {e.Message}");
            }
#endif
        }

        public static void Reset()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _routePinned = false;

                if (_pump != null)
                {
                    Object.Destroy(_pump);
                    _pump = null;
                }

                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                int sdkInt = version.GetStatic<int>("SDK_INT");

                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null) return;

                using var audioManager = activity.Call<AndroidJavaObject>("getSystemService", "audio");
                if (sdkInt >= 31)
                {
                    if (_listener != null)
                    {
                        audioManager.Call("removeOnCommunicationDeviceChangedListener", _listener);
                        _listener = null;
                    }
                    audioManager.Call("clearCommunicationDevice");
                }
                else
                {
                    audioManager.Call("setSpeakerphoneOn", false);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AndroidAudioRoute] Failed to reset audio route: {e.Message}");
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Fetches the AudioManager and reconciles the route, swallowing JNI errors. Entry point for
        // both the listener callback and the poll pump.
        static void ReconcileRouteSafe()
        {
            if (!_routePinned) return;
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null) return;

                using var audioManager = activity.Call<AndroidJavaObject>("getSystemService", "audio");
                ReconcileRoute(audioManager);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AndroidAudioRoute] Reconcile failed: {e.Message}");
            }
        }

        // Pins the highest-priority available communication device, leaving the route alone when it
        // already is the best choice. Connecting a higher-ranked device switches up, removing one
        // falls back, and an earpiece/null drift is corrected. The no-op-when-optimal check makes
        // this safe to call repeatedly (listener, poll, and our own setCommunicationDevice callback).
        static void ReconcileRoute(AndroidJavaObject audioManager)
        {
            if (!_routePinned) return;

            using var devices = audioManager.Call<AndroidJavaObject>("getAvailableCommunicationDevices");
            using var best = PickPreferred(devices);
            if (best == null)
            {
                Debug.LogWarning("[AndroidAudioRoute] No preferred communication device available.");
                return;
            }

            using var current = audioManager.Call<AndroidJavaObject>("getCommunicationDevice");
            if (current != null && current.Call<int>("getId") == best.Call<int>("getId"))
                return;

            bool ok = audioManager.Call<bool>("setCommunicationDevice", best);
            Debug.Log($"[AndroidAudioRoute] setCommunicationDevice(type={best.Call<int>("getType")}) = {ok}");
        }

        // Returns the highest-priority available communication device (caller disposes), or null.
        static AndroidJavaObject PickPreferred(AndroidJavaObject devices)
        {
            int count = devices.Call<int>("size");
            foreach (int wantType in PreferredTypes)
            {
                for (int i = 0; i < count; i++)
                {
                    var dev = devices.Call<AndroidJavaObject>("get", i);
                    if (dev.Call<int>("getType") == wantType)
                        return dev;
                    dev.Dispose();
                }
            }
            return null;
        }

        // Bridges AudioManager.OnCommunicationDeviceChangedListener back into ReconcileRoute.
        class CommDeviceChangedListener : AndroidJavaProxy
        {
            public CommDeviceChangedListener()
                : base("android.media.AudioManager$OnCommunicationDeviceChangedListener") { }

            // Invoked on the Android main thread (we register with getMainExecutor()). The device
            // argument may be null when the route is cleared; we ignore it and re-query state.
            public void onCommunicationDeviceChanged(AndroidJavaObject device)
            {
                device?.Dispose();
                ReconcileRouteSafe();
            }
        }

        // Ticks ReconcileRoute once a second so the route follows a device that connects mid-call,
        // which OnCommunicationDeviceChangedListener doesn't report. Lives only while the route is
        // pinned (created in ForceHandsFree, destroyed in Reset).
        class RoutePump : MonoBehaviour
        {
            IEnumerator Start()
            {
                var wait = new WaitForSeconds(1f);
                while (true)
                {
                    ReconcileRouteSafe();
                    yield return wait;
                }
            }
        }
#endif
    }
}
