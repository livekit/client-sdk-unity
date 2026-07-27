#if UNITY_IOS

using UnityEngine;
using UnityEditor;
using UnityEditor.iOS.Xcode;
using UnityEditor.Callbacks;

public class BuildPostProcessor
{
    [PostProcessBuildAttribute(500)]
    public static void OnPostprocessBuild(BuildTarget buildTarget, string pathToBuiltProject)
    {
        if (buildTarget != BuildTarget.iOS)
        {
            return;
        }
        var projPath = pathToBuiltProject + "/Unity-iPhone.xcodeproj/project.pbxproj";
        PBXProject proj = new PBXProject();
        proj.ReadFromFile(projPath);
#if UNITY_2019_3_OR_NEWER
        string guid = proj.GetUnityFrameworkTargetGuid();
#else
        string guid = proj.TargetGuidByName("Unity-iPhone");
#endif
        proj.AddBuildProperty(guid, "OTHER_LDFLAGS", "-ObjC");
        // Unity 6000.5+ no longer exports Libraries/libiPhone-lib.a.
        string fileGuid = proj.FindFileGuidByProjectPath("Libraries/libiPhone-lib.a");
        if (fileGuid != null)
        {
            proj.RemoveFileFromBuild(guid, fileGuid);
            proj.AddFileToBuild(guid, fileGuid);
        }

        ApplyAutomaticSigning(proj);

        proj.WriteToFile(projPath);
    }

    private static void ApplyAutomaticSigning(PBXProject proj)
    {
        // Team ID lives in EditorPrefs (per-machine, never committed); set it under
        // Edit > Preferences > iOS Signing. Skip signing entirely when it is unset,
        // which leaves a Replace build's signing for you to fill in manually.
        var teamId = EditorPrefs.GetString(IosSigningPreferences.TeamIdKey, string.Empty);
        if (string.IsNullOrEmpty(teamId))
            return;

        // Sign both the app target and the embedded UnityFramework target.
#if UNITY_2019_3_OR_NEWER
        string[] targets = { proj.GetUnityMainTargetGuid(), proj.GetUnityFrameworkTargetGuid() };
#else
        string[] targets = { proj.TargetGuidByName("Unity-iPhone") };
#endif
        foreach (var target in targets)
        {
            proj.SetBuildProperty(target, "CODE_SIGN_STYLE", "Automatic");
            proj.SetBuildProperty(target, "DEVELOPMENT_TEAM", teamId);
            // Automatic signing manages the profile itself; a leftover manual
            // specifier would conflict and re-introduce the signing prompt.
            proj.SetBuildProperty(target, "PROVISIONING_PROFILE_SPECIFIER", "");
        }

        Debug.Log($"iOS automatic signing applied with team {teamId}.");
    }
}

#endif
