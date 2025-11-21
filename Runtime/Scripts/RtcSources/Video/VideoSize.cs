namespace LiveKit.RtcSources.Video
{
    public readonly struct VideoSize
    {
        public readonly uint width;
        public readonly uint height;

        public VideoSize(uint width, uint height)
        {
            this.width = width;
            this.height = height;
        }

        public (int width, int height) AsInts()
        {
            return ((int)width, (int)height);
        }
    }
}