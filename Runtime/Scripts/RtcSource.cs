

namespace LiveKit
{
    public abstract class IRtcSource
    {
        public abstract void SetMute(bool muted);

        public abstract bool Muted { get; }
    }
}