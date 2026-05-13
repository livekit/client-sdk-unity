using UnityEngine;

/// <summary>
/// Starts/stops an Android foreground service so the OS keeps the process out of Doze
/// while the user is in a LiveKit call. Without it, the screen lock pauses the network
/// long enough for the SDK to exhaust its 10 reconnect attempts and disconnect.
/// No-op on all non-Android platforms.
/// </summary>
public static class AndroidBackgroundService
{
    private const string ServiceClassName = "io.livekit.unity.sample.LiveKitForegroundService";

    public static void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        WithActivity((cls, activity) => cls.CallStatic("start", activity));
#endif
    }

    public static void Stop()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        WithActivity((cls, activity) => cls.CallStatic("stop", activity));
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static void WithActivity(System.Action<AndroidJavaClass, AndroidJavaObject> body)
    {
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var serviceClass = new AndroidJavaClass(ServiceClassName);
            body(serviceClass, activity);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"AndroidBackgroundService: {e.Message}");
        }
    }
#endif
}
