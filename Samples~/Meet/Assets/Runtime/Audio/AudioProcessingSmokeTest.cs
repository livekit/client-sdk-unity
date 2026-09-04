using System;
using System.Runtime.InteropServices;
using LiveKit;
using UnityEngine;

/// <summary>
/// Go/no-go check for the FFI <see cref="AudioProcessingModule"/>, runnable without joining a
/// room. Exercises the full call shape: create a handle, seed a delay, and process one 10 ms
/// chunk in each direction. The APM is compiled into each platform's FFI binary separately, so
/// a per-platform surprise is far cheaper to find here than after the pipeline is wired.
/// </summary>
internal static class AudioProcessingSmokeTest
{
    public static string Run()
    {
        var rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        var frameSize = AudioProcessingModule.FrameSizeFor(rate);

        AudioProcessingModule apm;
        try
        {
            apm = new AudioProcessingModule(
                echoCancellerEnabled: true,
                gainControllerEnabled: false,
                highPassFilterEnabled: false,
                noiseSuppressionEnabled: false);
        }
        catch (Exception e)
        {
            return $"FAIL create_apm: {e.GetType().Name}: {e.Message}";
        }

        using (apm)
        {
            var delayError = apm.SetStreamDelayMs(100);
            if (delayError != null) return $"FAIL set_stream_delay_ms: {delayError}";

            var chunk = new short[frameSize];
            var pin = GCHandle.Alloc(chunk, GCHandleType.Pinned);
            try
            {
                var ptr = pin.AddrOfPinnedObject();
                var bytes = chunk.Length * sizeof(short);

                var reverseError = apm.ProcessReverseStream(ptr, bytes, rate, 1);
                if (reverseError != null) return $"FAIL process_reverse_stream: {reverseError}";

                var processError = apm.ProcessStream(ptr, bytes, rate, 1);
                if (processError != null) return $"FAIL process_stream: {processError}";
            }
            finally
            {
                pin.Free();
            }

            return $"PASS handle={apm.Handle} rate={rate} frameSize={frameSize} " +
                   $"nativeRate={AudioProcessingModule.IsNativeSampleRate(rate)} " +
                   $"delaySeed={AudioProcessingDelaySeed.Estimate(0d)}ms";
        }
    }
}
