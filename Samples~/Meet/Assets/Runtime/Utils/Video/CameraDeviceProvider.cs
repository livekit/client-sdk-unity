using System;
using System.Collections;
using UnityEngine;
using Application = UnityEngine.Application;

#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif

public static class CameraDeviceProvider
{
    public static IEnumerator Open(int frameRate, Action<WebCamTexture> onReady)
    {
        RequestCameraPermissionIfNeeded();

        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            Debug.LogError("Camera permission not obtained");
            yield break;
        }

        yield return WaitForCameraDevices();

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

    private static IEnumerator WaitForCameraDevices()
    {
        for (int i = 0; i < 300 && WebCamTexture.devices.Length == 0; i++)
            yield return new WaitForEndOfFrame();
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

    private static void RequestCameraPermissionIfNeeded()
    {
#if PLATFORM_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            Permission.RequestUserPermission(Permission.Camera);
#endif
    }
}
