using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace LiveKit.Internal
{
    /// <summary>
    /// Debugging utility for dumping audio buffers to WAV files so they can be inspected offline
    /// (in an audio editor, or analyzed programmatically).
    /// </summary>
    /// <remarks>
    /// This proved decisive when diagnosing microphone capture issues: a raw dump of the mic clip
    /// on macOS with a Bluetooth HFP headset revealed that FMOD writes valid 320-sample audio
    /// fragments at a 1024-sample stride with exact-zero padding between them, while
    /// Microphone.GetPosition advances ~3.2x faster than the data rate. Inspecting the actual
    /// buffer contents settles questions that API values (clip.frequency, GetPosition) cannot.
    /// </remarks>
    internal static class AudioClipDump
    {
        /// <summary>
        /// Snapshots the full contents of a clip to a 16-bit PCM WAV file and returns the path.
        /// For looping microphone clips, call a few seconds after capture started so the ring
        /// buffer contains audio, and produce sound continuously while it fills.
        /// </summary>
        public static string DumpClip(AudioClip clip, string fileName = "lk_clip_dump.wav")
        {
            var data = new float[clip.samples * clip.channels];
            clip.GetData(data, 0);
            var path = Path.Combine(Application.temporaryCachePath, fileName);
            WriteWav(path, data, clip.channels, clip.frequency);
            Utils.Info($"AudioClipDump: wrote {path} ({clip.samples} frames @ {clip.frequency}Hz/{clip.channels}ch)");
            return path;
        }

        /// <summary>
        /// Coroutine that waits, then dumps the clip. Convenient to start alongside capture:
        /// <c>MonoBehaviourContext.RunCoroutine(AudioClipDump.DumpClipAfter(clip, 4f));</c>
        /// </summary>
        public static IEnumerator DumpClipAfter(AudioClip clip, float delaySeconds, string fileName = "lk_clip_dump.wav")
        {
            yield return new WaitForSeconds(delaySeconds);
            if (clip == null) yield break;
            try
            {
                DumpClip(clip, fileName);
            }
            catch (Exception e)
            {
                Utils.Warning($"AudioClipDump: dump failed: {e.Message}");
            }
        }

        /// <summary>
        /// Writes interleaved float samples as a 16-bit PCM WAV file.
        /// </summary>
        public static void WriteWav(string path, float[] samples, int channels, int sampleRate)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(fs);
            int dataBytes = samples.Length * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataBytes);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((short)1);              // PCM
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * 2);
            w.Write((short)(channels * 2)); // block align
            w.Write((short)16);             // bits per sample
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(dataBytes);
            foreach (var s in samples)
                w.Write((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
        }
    }
}
