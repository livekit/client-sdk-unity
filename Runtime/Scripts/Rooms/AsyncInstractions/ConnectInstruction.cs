using System.Threading;
using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Rooms;

namespace LiveKit.Rooms.AsyncInstractions
{
    public sealed class ConnectInstruction : AsyncInstruction
    {
        private ulong _asyncId;
        private Room _room;

        internal ConnectInstruction(ulong asyncId, Room room, CancellationToken token) : base(token)
        {
            _asyncId = asyncId;
            _room = room;
            FfiClient.Instance.ConnectReceived += OnConnect;
        }

        void OnConnect(ConnectCallback e)
        {
            Utils.Debug($"OnConnect.... {e}");
            if (_asyncId != e.AsyncId)
                return;

            FfiClient.Instance.ConnectReceived -= OnConnect;

            if (Token.IsCancellationRequested) return;

            bool success = string.IsNullOrEmpty(e.Error);
            Utils.Debug("Connection success: " + success);
            if (success)
                _room.OnConnect(e.Room.Handle, e.Room.Info, e.LocalParticipant, e.Participants);

            IsError = !success;
            IsDone = true;
        }
    }
}