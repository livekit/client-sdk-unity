using System;

namespace LiveKit.Audio
{
    /// <summary>
    /// Guarantees frame is 10 ms and is compatible to what WebRTC expects
    /// </summary>
    public readonly ref struct ApmFrame
    {
        public const uint FRAME_DURATION_MS = 10;

        public readonly ReadOnlySpan<PCMSample> data;
        public readonly uint numChannels;
        public readonly uint samplesPerChannel;
        public readonly SampleRate sampleRate;

        public uint SizeInBytes => numChannels * samplesPerChannel * PCMSample.BytesPerSample;

        private ApmFrame(ReadOnlySpan<PCMSample> data, uint numChannels, uint samplesPerChannel, SampleRate sampleRate)
        {
            this.data = data;
            this.numChannels = numChannels;
            this.samplesPerChannel = samplesPerChannel;
            this.sampleRate = sampleRate;
        }


        /// <summary>
        ///     Cannot use Result due ref limitations in C#
        /// </summary>
        public static ApmFrame New(
            ReadOnlySpan<PCMSample> data,
            uint numChannels,
            uint samplesPerChannel,
            SampleRate sampleRate,
            out string? error)
        {
            error = null;

            if (numChannels == 0)
            {
                error = "Number of channels cannot be zero.";
                return default;
            }

            if (sampleRate.valueHz != SampleRate.Hz48000.valueHz)
            {
                error = "SampleRate must be Hz48000, it's invalid value.";
                return default;
            }

            // Expected samples per 10 ms per channel
            uint expectedSamplesPerChannel = sampleRate.valueHz / 100;

            if (samplesPerChannel != expectedSamplesPerChannel)
            {
                error =
                    $"Frame must be 10 ms long. Expected {expectedSamplesPerChannel} samples per channel, got {samplesPerChannel}.";
                return default;
            }

            if (data.Length != samplesPerChannel * numChannels)
            {
                error =
                    $"Data length ({data.Length}) does not match samplesPerChannel ({samplesPerChannel}) * numChannels ({numChannels}).";
                return default;
            }

            return new ApmFrame(data, numChannels, samplesPerChannel, sampleRate);
        }
    }
}