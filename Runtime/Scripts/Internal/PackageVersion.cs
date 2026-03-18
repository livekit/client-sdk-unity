using UnityEngine;

namespace LiveKit
{
public static class PackageVersion
{
    public static string Get()
    {
        var asset = Resources.Load<TextAsset>("LiveKitSdkVersionInfo");
        return asset != null ? asset.text.Trim() : "unknown";
    }
}
}
