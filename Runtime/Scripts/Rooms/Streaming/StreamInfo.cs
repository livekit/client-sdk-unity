namespace LiveKit.Rooms.Streaming
{
    public readonly struct StreamInfo<TInfo>
    {
        public readonly StreamKey key;
        public readonly TInfo info;

        public StreamInfo(StreamKey key, TInfo info)
        {
            this.key = key;
            this.info = info;
        }
    }
}