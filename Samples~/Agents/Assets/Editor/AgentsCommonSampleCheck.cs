using UnityEditor;
using UnityEngine;

public static class AgentsCommonSampleCheck
{
    // Canonical GUIDs of the Common sample's ScrollingLog assets; the sample
    // scenes bind to these, so resolving them is what "imported" means.
    const string ScrollingLogScriptGuid = "0c3a683a174e6f34fbe620bca4947e2d";
    const string ScrollingLogPrefabGuid = "b654e1b6fc22b404f857a041468cadb2";

    [InitializeOnLoadMethod]
    static void Check()
    {
        if (IsImported(ScrollingLogScriptGuid) && IsImported(ScrollingLogPrefabGuid))
            return;
        Debug.LogError(
            "[LiveKit] The Agents sample requires the \"Common\" sample. " +
            "Import it via Window > Package Manager > LiveKit SDK > Samples > Common > Import.");
    }

    static bool IsImported(string guid)
    {
        return AssetDatabase.GUIDToAssetPath(guid).StartsWith("Assets/");
    }
}
