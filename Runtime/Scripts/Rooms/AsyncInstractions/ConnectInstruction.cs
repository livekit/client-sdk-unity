#if !UNITY_WEBGL

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

            ErrorMessage = e.Error;
            bool success = IsError == false;
            Utils.Debug("Connection success: " + success);
            if (success)
                _room.OnConnect(e.Result.Room.Handle, e.Result.Room.Info, e.Result.LocalParticipant, e.Result.Participants);

            IsDone = true;
        }
    }
}

#endif
