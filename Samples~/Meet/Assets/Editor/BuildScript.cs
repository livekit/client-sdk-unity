using UnityEditor;
using UnityEditor.Build.Reporting;
using System;

namespace LiveKit.Editor
{
    public class BuildScript
    {
        private const string ScenePath = "Assets/Scenes/MeetApp.unity";

        public static void BuildMac()
        {
            Build(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Mac/Meet.app",
                target = BuildTarget.StandaloneOSX,
            });
        }

        public static void BuildIOS()
        {
            Build(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/iOS",
                target = BuildTarget.iOS,
            });
        }

        public static void BuildAndroid()
        {
            Build(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
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
}
