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
        string fileGuid = proj.FindFileGuidByProjectPath("Libraries/libiPhone-lib.a");
        proj.RemoveFileFromBuild(guid, fileGuid);
        proj.AddFileToBuild(guid, fileGuid);
        proj.WriteToFile(projPath);
    }
}

#endif
