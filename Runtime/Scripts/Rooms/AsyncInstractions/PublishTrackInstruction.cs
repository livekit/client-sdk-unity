using System.Threading;
using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Rooms;

namespace LiveKit.Rooms.AsyncInstractions
{
    public sealed class PublishTrackInstruction : AsyncInstruction
    {
        private ulong _asyncId;
        private Room _room;

        internal PublishTrackInstruction(ulong asyncId, Room room, CancellationToken token) : base(token)
        {
            _asyncId = asyncId;
            _room = room;
            FfiClient.Instance.PublishTrackReceived += OnPublish;
        }

        void OnPublish(PublishTrackCallback e)
        {
            if (e.AsyncId != _asyncId)
                return;

            IsError = !string.IsNullOrEmpty(e.Error);
            IsDone = true;

            FfiClient.Instance.PublishTrackReceived -= OnPublish;
        }
    }
}