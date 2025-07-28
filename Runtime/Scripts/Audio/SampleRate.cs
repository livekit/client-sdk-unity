namespace LiveKit.Audio
{
    public readonly struct SampleRate
    {
        public static readonly SampleRate Hz48000 = new(48000);

        public readonly uint valueHz;

        public SampleRate(uint value)
        {
            valueHz = value;
        }
    }
}