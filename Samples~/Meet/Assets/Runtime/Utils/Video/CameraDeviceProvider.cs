using System;
using System.Collections;
using UnityEngine;

#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif

public static class CameraDeviceProvider
{
    public static IEnumerator Open(int frameRate, Action<WebCamTexture> onReady)
    {
        var granted = false;
        yield return RequestCameraPermission(g => granted = g);
        if (!granted)
        {
            Debug.LogError("Camera permission not obtained");
            yield break;
        }

        for (int i = 0; i < 300 && WebCamTexture.devices.Length == 0; i++)
            yield return null;

        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("No camera device available");
            yield break;
        }

        var device = PickPreferredCamera(WebCamTexture.devices);
        var (width, height) = GetCameraResolution();

        var texture = new WebCamTexture(device.name, width, height, frameRate)
        {
            wrapMode = TextureWrapMode.Repeat
        };
        texture.Play();
        onReady?.Invoke(texture);
    }

    private static IEnumerator RequestCameraPermission(Action<bool> onResult)
    {
#if PLATFORM_ANDROID
        if (Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            onResult(true);
            yield break;
        }
        var done = false;
        var granted = false;
        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ => { granted = true; done = true; };
        callbacks.PermissionDenied += _ => done = true;
        callbacks.PermissionDeniedAndDontAskAgain += _ => done = true;
        Permission.RequestUserPermission(Permission.Camera, callbacks);
        while (!done) yield return null;
        onResult(granted);
#else
        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        onResult(Application.HasUserAuthorization(UserAuthorization.WebCam));
#endif
    }

    private static WebCamDevice PickPreferredCamera(WebCamDevice[] devices)
    {
        foreach (var d in devices)
            if (d.isFrontFacing) return d;
        return devices[0];
    }

    private static (int width, int height) GetCameraResolution()
    {
        return Screen.height > Screen.width
            ? (Screen.height, Screen.width)
            : (Screen.width, Screen.height);
    }
}
