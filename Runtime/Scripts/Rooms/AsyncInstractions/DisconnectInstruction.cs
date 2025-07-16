using System.Threading;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit.Rooms.AsyncInstractions
{
    public sealed class DisconnectInstruction : AsyncInstruction
    {
        private readonly ulong asyncId;
        private readonly Room room;

        internal DisconnectInstruction(ulong asyncId, Room room, CancellationToken token) : base(token)
        {
            this.asyncId = asyncId;
            this.room = room;
            FfiClient.Instance.DisconnectReceived += OnDisconnect;
        }

        private void OnDisconnect(DisconnectCallback e)
        {
            Utils.Debug($"OnConnect.... {e}");
            if (asyncId != e.AsyncId)
                return;

            FfiClient.Instance.DisconnectReceived -= OnDisconnect;

            room.OnDisconnect();

            ErrorMessage = string.Empty;
            IsDone = true;
        }
    }
}