using System.Collections.Generic;
using System.Text;
using LiveKit.Scripts.Audio;
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
                .Append(": name - ").Append(source.name)
                .Append(", sampleRate - ").Append(source.sampleRate)
                .Append(", channels - ").Append(source.channels)
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