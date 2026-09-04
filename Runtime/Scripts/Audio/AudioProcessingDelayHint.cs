using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LiveKit
{
    /// <summary>
    /// Estimates the render-to-capture delay hint handed to
    /// <see cref="AudioProcessingModule.SetStreamDelayMs"/>.
    /// </summary>
    /// <remarks>
    /// AEC3 runs its own correlation-based delay estimator; the hint only sets the initial
    /// alignment after a reset, so it has to be in the right ballpark rather than exact. The echo
    /// path in Unity-audio mode is: <see cref="PlayoutReference"/> tap → Unity's output queue (a
    /// few DSP blocks) → device output → air → device input → <c>Microphone</c> clip → the
    /// AudioSource reading that clip → capture probe. Only the DSP block size and, on iOS, the
    /// audio session latencies are readable; the remaining terms are constants.
    ///
    /// Main thread only: reads <see cref="AudioSettings"/>.
    /// </remarks>
    internal static class AudioProcessingDelayHint
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern double LiveKit_AudioSessionOutputLatency();
        [DllImport("__Internal")] private static extern double LiveKit_AudioSessionInputLatency();
        [DllImport("__Internal")] private static extern double LiveKit_AudioSessionIOBufferDuration();
#endif

        internal const int MinDelayMs = 0;
        internal const int MaxDelayMs = 500;

        /// <summary>Output queue depth assumed between the listener tap and the device, in DSP blocks.</summary>
        internal const int OutputQueueBlocks = 2;

        /// <summary>
        /// How far the AudioSource reading the microphone clip trails the clip's write head.
        /// <see cref="MicrophoneSource"/> starts reading once <c>Microphone.GetPosition</c> first
        /// reports data, polled at 50 ms, and that offset persists for the life of the clip.
        /// </summary>
        internal const int MicrophoneReadBehindMs = 50;

        /// <summary>Device input plus output latency when the platform does not report it.</summary>
        internal const int FallbackDeviceLatencyMs = 30;

        public static int EstimateMs()
        {
            var config = AudioSettings.GetConfiguration();
            return EstimateMs(config.dspBufferSize, config.sampleRate, PlatformLatencyMs());
        }

        internal static int EstimateMs(int dspBufferSize, int sampleRate, double deviceLatencyMs)
        {
            var blockMs = sampleRate > 0 ? dspBufferSize * 1000.0 / sampleRate : 0.0;
            var estimate = blockMs * OutputQueueBlocks + deviceLatencyMs + MicrophoneReadBehindMs;
            return (int)Math.Round(Math.Min(MaxDelayMs, Math.Max(MinDelayMs, estimate)));
        }

        internal static double PlatformLatencyMs()
        {
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                // AVAudioSession reports zero until the session is active, hence the fallback.
                var output = LiveKit_AudioSessionOutputLatency();
                var input = LiveKit_AudioSessionInputLatency();
                var ioBuffer = LiveKit_AudioSessionIOBufferDuration();
                if (output > 0d || input > 0d)
                    return (output + input + ioBuffer) * 1000d;
            }
            catch (Exception)
            {
                // Fall through to the constant.
            }
#endif
            return FallbackDeviceLatencyMs;
        }
    }
}
