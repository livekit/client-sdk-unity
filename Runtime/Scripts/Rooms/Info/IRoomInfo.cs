namespace LiveKit.Rooms.Info
{
    public interface IRoomInfo
    {
        public string Sid { get; }
        public string Name { get; }
        public string Metadata { get; }
    }
}