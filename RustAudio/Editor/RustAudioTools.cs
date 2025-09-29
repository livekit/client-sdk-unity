using System.Collections.Generic;
using System.Text;
using LiveKit.Scripts.Audio;
using RichTypes;
using RustAudio;
using UnityEditor;
using UnityEngine;

public static class RustAudioTools
{
    public const string PATH_PREFIX = "Tools/RustAudio/";


    [MenuItem(PATH_PREFIX + nameof(PrintDevices))]
    public static void PrintDevices()
    {
        var array = MicrophoneAudioFilter.AvailableDeviceNamesOrEmpty();
        var sb = new StringBuilder();
        sb.AppendLine($"Total count: {array.Length}, Available:");
        foreach (var name in array)
        {
            sb.AppendLine(name);

            Result<string[]> options = RustAudioClient.DeviceQualityOptions(name);
            if (options.Success)
            {
                foreach (string quality in options.Value)
                {
                    sb.Append("- ").AppendLine(quality);
                }
            }
        }

        Debug.Log(sb.ToString());
    }

    [MenuItem(PATH_PREFIX + nameof(PrintStatus))]
    public static void PrintStatus()
    {
        var status = RustAudioClient.SystemStatus();
        var sb = new StringBuilder();
        sb.AppendLine(JsonUtility.ToJson(status));
        Debug.Log(sb.ToString());
    }

    [MenuItem(PATH_PREFIX + nameof(PrintActiveSources))]
    public static void PrintActiveSources()
    {
        IReadOnlyDictionary<ulong, RustAudioSource> sources = RustAudioSource.Info.ActiveSources();
        var sb = new StringBuilder();
        sb.AppendLine("ActiveSources:");
        foreach (KeyValuePair<ulong, RustAudioSource> rustAudioSource in sources)
        {
            var source = rustAudioSource.Value!;
            sb.Append(rustAudioSource.Key)
                .Append(": name - ").Append(source.microphoneInfo.name)
                .Append(", sampleRate - ").Append(source.microphoneInfo.sampleRate)
                .Append(", channels - ").Append(source.microphoneInfo.channels)
                .Append(", recording - ").Append(source.IsRecording)
                .AppendLine();
        }

        Debug.Log(sb.ToString());
    }

    [MenuItem(PATH_PREFIX + nameof(ReInit))]
    public static void ReInit()
    {
        RustAudioClient.ForceReInit();
        Debug.Log("ReInit complete");
    }
}