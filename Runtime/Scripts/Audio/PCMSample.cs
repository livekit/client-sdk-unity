using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace LiveKit.Audio
{
    [SuppressMessage("ReSharper", "BuiltInTypeReferenceStyle")]
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PCMSample
    {
        public const byte BytesPerSample = 2; // Int16 = Int8 * 2

        private const float S16_MAX_VALUE = 32767f;
        private const float S16_MIN_VALUE = -32768f;
        private const float S16_SCALE_FACTOR = 32768f;

        public readonly Int16 data;

        public PCMSample(Int16 data)
        {
            this.data = data;
        }

        public static PCMSample FromUnitySample(float sample)
        {
            sample *= S16_SCALE_FACTOR;
            if (sample > S16_MAX_VALUE) sample = S16_MAX_VALUE;
            else if (sample < S16_MIN_VALUE) sample = S16_MIN_VALUE;
            return new PCMSample((short)(sample + (sample >= 0 ? 0.5f : -0.5f)));
        }
    }
}