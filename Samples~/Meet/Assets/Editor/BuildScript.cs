using UnityEditor;
using UnityEditor.Build.Reporting;
using System;

public class BuildScript
{
    public static void BuildMac()
    {
        Build(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/MeetApp.unity" },
            locationPathName = "Builds/Mac/Meet.app",
            target = BuildTarget.StandaloneOSX,
        });
    }

    public static void BuildIOS()
    {
        Build(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/MeetApp.unity" },
            locationPathName = "Builds/iOS",
            target = BuildTarget.iOS,
        });
    }

    public static void BuildAndroid()
    {
        Build(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/MeetApp.unity" },
            locationPathName = "Builds/Android/Meet.apk",
            target = BuildTarget.Android,
        });
    }

    private static void Build(BuildPlayerOptions options)
    {
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception($"Build failed for {options.target}: {report.summary.result}");
    }
}