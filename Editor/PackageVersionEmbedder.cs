#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PackageVersionEmbedder
{
    const string PackageName = "io.livekit.livekit-sdk";
    const string ResourcePath = "Assets/Resources";
    const string AssetName = "LiveKitSdkVersionInfo";

    [InitializeOnLoadMethod]
    static void Embed()
    {
        // FindForAssetPath works with any asset inside the package
        var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{PackageName}");
        if (info == null) 
        {
            Debug.Log($"Package {PackageName} not found");
            return;
        }

        if (!System.IO.Directory.Exists(ResourcePath))
            System.IO.Directory.CreateDirectory(ResourcePath);

        string path = $"{ResourcePath}/{AssetName}.txt";
        System.IO.File.WriteAllText(path, info.version);
        AssetDatabase.ImportAsset(path);
    }
}
#endif