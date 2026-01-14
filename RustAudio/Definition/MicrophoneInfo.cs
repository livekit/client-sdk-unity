namespace RustAudio
{
    public readonly struct MicrophoneInfo
    {
        public readonly string name;
        public readonly uint sampleRate;
        public readonly uint channels;

        public MicrophoneInfo(string name, uint sampleRate, uint channels)
        {
            this.name = name;
            this.sampleRate = sampleRate;
            this.channels = channels;
        }
    }
}
