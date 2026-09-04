using System;
using System.Runtime.InteropServices;

/// <summary>
/// Computes the <c>set_stream_delay_ms</c> seed:
/// <c>(t_render - t_analyze) + (t_process - t_capture)</c>.
///
/// AEC3 runs its own correlation-based delay estimator
/// (<c>use_external_delay_estimator = false</c>), so this is a convergence hint, not a hard
/// alignment — but a wildly wrong value slows convergence. iOS sources the platform terms from
/// <c>AVAudioSession</c> (see <c>Plugins/iOS/AudioSessionLatency.mm</c>); Android has no
/// equivalent accessor, so it starts from the buffering this pipeline adds and lets AEC3 find
/// the rest.
///
/// Known gap: the reference is tapped on the decoder side, so any playout queue between the
/// decoder and the loudspeaker (Unity's AudioStream ring buffer, ~30-200 ms) is not part of
/// this estimate.
/// </summary>
internal static class AudioProcessingDelaySeed
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern double MeetSample_AudioSessionOutputLatency();
    [DllImport("__Internal")] private static extern double MeetSample_AudioSessionInputLatency();
    [DllImport("__Internal")] private static extern double MeetSample_AudioSessionIOBufferDuration();
#endif

    private const int MinDelayMs = 0;
    private const int MaxDelayMs = 500;

    /// <summary>Seed used when no platform latency is readable (Android, or an inactive session).</summary>
    private const int FallbackDelayMs = 60;

    /// <param name="captureBlockMs">
    /// Measured duration of one <c>OnAudioFilterRead</c> block, which is how far behind
    /// real-time the capture stream already is when it reaches the APM.
    /// </param>
    public static int Estimate(double captureBlockMs)
    {
        var platformMs = PlatformLatencyMs();
        var seed = (int)Math.Round(platformMs + captureBlockMs);
        return Math.Min(MaxDelayMs, Math.Max(MinDelayMs, seed));
    }

    public static double PlatformLatencyMs()
    {
#if UNITY_IOS && !UNITY_EDITOR
        try
        {
            var output = MeetSample_AudioSessionOutputLatency();
            var input = MeetSample_AudioSessionInputLatency();
            var ioBuffer = MeetSample_AudioSessionIOBufferDuration();
            if (output > 0d || input > 0d)
                return (output + input + ioBuffer) * 1000d;
        }
        catch (Exception)
        {
            // Session not yet active; fall through to the platform-agnostic seed.
        }
        return FallbackDelayMs;
#else
        return FallbackDelayMs;
#endif
    }
}
