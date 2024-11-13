using System;
using System.Linq;
using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Internal.FFIClients.Requests;
using System.Threading.Tasks;

namespace LiveKit
{
    public class Participant
    {
        public delegate void PublishDelegate(RemoteTrackPublication publication);


        private ParticipantInfo _info;
        internal readonly Dictionary<string, TrackPublication> _tracks = new();
        public FfiHandle Handle;
        public string Sid => _info.Sid;
        public string Identity => _info.Identity;
        public string Name => _info.Name;
        public string Metadata => _info.Metadata;
        public ConnectionQuality ConnectionQuality { internal set; get; }
        public event PublishDelegate TrackPublished;
        public event PublishDelegate TrackUnpublished;

        public readonly WeakReference<Room> Room;
        public IReadOnlyDictionary<string, TrackPublication> Tracks => _tracks;

        private Dictionary<string, Func<RpcInvocationData, Task<string>>> _rpcHandlers = new();

        protected Participant(OwnedParticipant participant, Room room)
        {
            Room = new WeakReference<Room>(room);
            Handle = FfiHandle.FromOwnedHandle(participant.Handle);
            UpdateInfo(participant.Info);
        }

        public void SetMeta(string meta)
        {
            _info.Metadata = meta;
        }

        public void SetName(string name)
        {
            _info.Name = name;
        }

        internal void UpdateInfo(ParticipantInfo info)
        {
            _info = info;
        }

        internal void OnTrackPublished(RemoteTrackPublication publication)
        {
            TrackPublished?.Invoke(publication);
        }

        internal void OnTrackUnpublished(RemoteTrackPublication publication)
        {
            TrackUnpublished?.Invoke(publication);
        }

    }

    public sealed class LocalParticipant : Participant
    {
        public new IReadOnlyDictionary<string, LocalTrackPublication> Tracks =>
            base.Tracks.ToDictionary(p => p.Key, p => (LocalTrackPublication)p.Value);

        internal LocalParticipant(OwnedParticipant participant, Room room) : base(participant, room) { }

        public PublishTrackInstruction PublishTrack(ILocalTrack localTrack, TrackPublishOptions options)
        {
            if (!Room.TryGetTarget(out var room))
                throw new Exception("room is invalid");

            var track = (Track)localTrack;

            using var request = FFIBridge.Instance.NewRequest<PublishTrackRequest>();
            var publish = request.request;
            publish.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            publish.TrackHandle = (ulong)track.Handle.DangerousGetHandle();
            publish.Options = options;
            using var response = request.Send();
            FfiResponse res = response;
            return new PublishTrackInstruction(res.PublishTrack.AsyncId, localTrack, _tracks);
        }

        public UnpublishTrackInstruction UnpublishTrack(ILocalTrack localTrack, bool stopOnUnpublish)
        {
            if (!Room.TryGetTarget(out var room))
                throw new Exception("room is invalid");

            using var request = FFIBridge.Instance.NewRequest<UnpublishTrackRequest>();
            var unpublish = request.request;
            unpublish.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            unpublish.StopOnUnpublish = false;
            unpublish.TrackSid = localTrack.Sid;
            using var response = request.Send();
            FfiResponse res = response;
            _tracks.Remove(localTrack.Sid);
            return new UnpublishTrackInstruction(res.UnpublishTrack.AsyncId);
        }

        public void PublishData(byte[] data, IReadOnlyCollection<string> destination_identities = null, bool reliable = true, string topic = null)
        {
            PublishData(new Span<byte>(data), destination_identities, reliable, topic);
        }

        public void PublishData(Span<byte> data, IReadOnlyCollection<string> destination_identities = null, bool reliable = true, string topic = null)
        {
            unsafe
            {
                fixed (byte* pointer = data)
                {
                    PublishData(pointer, data.Length, destination_identities, reliable, topic);
                }
            }
        }

        public void UpdateMetadata(string metadata)
        {
            using var request = FFIBridge.Instance.NewRequest<SetLocalMetadataRequest>();
            var updateReq = request.request;
            updateReq.Metadata = metadata;
            var resp = request.Send();
        }

        public void UpdateName(string name)
        {
            using var request = FFIBridge.Instance.NewRequest<SetLocalNameRequest>();
            var updateReq = request.request;
            updateReq.Name = name;
            var resp = request.Send();
        }

        public async Task<string> PerformRpc(PerformRpcParams rpcParams)
        {
            using var request = FFIBridge.Instance.NewRequest<PerformRpcRequest>();
            var rpcReq = request.request;
            rpcReq.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            rpcReq.DestinationIdentity = rpcParams.DestinationIdentity;
            rpcReq.Method = rpcParams.Method;
            rpcReq.Payload = rpcParams.Payload;
            rpcReq.ResponseTimeoutMs = (int)(rpcParams.ResponseTimeout * 1000);

            var response = request.Send();
            var asyncId = response.PerformRpc.AsyncId;

            // Wait for callback
            var callback = await FfiClient.Instance.WaitForEvent<PerformRpcCallback>(
                evt => evt.MessageCase == FfiEvent.MessageOneofCase.PerformRpc &&
                       evt.PerformRpc.AsyncId == asyncId
            );

            if (callback.Error != null)
            {
                throw RpcError.FromProto(callback.Error);
            }

            return callback.Payload;
        }

        public void RegisterRpcMethod(string method, Func<RpcInvocationData, Task<string>> handler)
        {
            _rpcHandlers[method] = handler;

            using var request = FFIBridge.Instance.NewRequest<RegisterRpcMethodRequest>();
            var registerReq = request.request;
            registerReq.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            registerReq.Method = method;
            var resp = request.Send();
        }

        public void UnregisterRpcMethod(string method)
        {
            _rpcHandlers.Remove(method);

            using var request = FFIBridge.Instance.NewRequest<UnregisterRpcMethodRequest>();
            var unregisterReq = request.request;
            unregisterReq.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            unregisterReq.Method = method;
            var resp = request.Send();
        }

        internal async Task HandleRpcMethodInvocation(
            ulong invocationId,
            string method,
            string requestId,
            string callerIdentity,
            string payload,
            int responseTimeout)
        {
            RpcError? responseError = null;
            string? responsePayload = null;

            if (!_rpcHandlers.TryGetValue(method, out var handler))
            {
                responseError = RpcError.BuiltIn(RpcError.ErrorCode.UNSUPPORTED_METHOD);
            }
            else
            {
                try
                {
                    var invocationData = new RpcInvocationData
                    {
                        RequestId = requestId,
                        CallerIdentity = callerIdentity,
                        Payload = payload,
                        ResponseTimeout = responseTimeout
                    };
                    responsePayload = await handler(invocationData);
                }
                catch (RpcError error)
                {
                    responseError = error;
                }
                catch (Exception error)
                {
                    Utils.Warn($"Uncaught error returned by RPC handler for {method}. Returning APPLICATION_ERROR instead.", error);
                    responseError = RpcError.BuiltIn(RpcError.ErrorCode.APPLICATION_ERROR);
                }
            }

            using var request = FFIBridge.Instance.NewRequest<RpcMethodInvocationResponseRequest>();
            var rpcResp = request.request;
            rpcResp.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            rpcResp.InvocationId = invocationId;
            if (responseError != null)
            {
                rpcResp.Error = responseError.ToProto();
            }
            if (responsePayload != null)
            {
                rpcResp.Payload = responsePayload;
            }

            var response = request.Send();
            if (response.Error != null)
            {
                Utils.Warn($"Error sending rpc method invocation response: {response.Error}");
            }
        }

        private unsafe void PublishData(byte* data, int len, IReadOnlyCollection<string> destination_identities = null, bool reliable = true, string topic = null)
        {
            if (!Room.TryGetTarget(out var room))
                throw new Exception("room is invalid");

            using var request = FFIBridge.Instance.NewRequest<PublishDataRequest>();

            var publish = request.request;
            publish.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            publish.Reliable = reliable;

            if (destination_identities is not null)
            {
                publish.DestinationIdentities.AddRange(destination_identities);
            }

            if (topic is not null)
            {
                publish.Topic = topic;
            }

            unsafe
            {
                publish.DataLen = (ulong)len;
                publish.DataPtr = (ulong)data;
            }
            Utils.Debug("Sending message: " + topic);
            var response = request.Send();
        }
    }

    public sealed class RemoteParticipant : Participant
    {
        public new IReadOnlyDictionary<string, RemoteTrackPublication> Tracks =>
            base.Tracks.ToDictionary(p => p.Key, p => (RemoteTrackPublication)p.Value);

        internal RemoteParticipant(OwnedParticipant participant, Room room) : base(participant, room) { }
    }

    public sealed class PublishTrackInstruction : YieldInstruction
    {
        private ulong _asyncId;
        private Dictionary<string, TrackPublication> _internalTracks;
        private ILocalTrack _localTrack;

        internal PublishTrackInstruction(ulong asyncId, ILocalTrack localTrack, Dictionary<string, TrackPublication> internalTracks)
        {
            _asyncId = asyncId;
            _internalTracks = internalTracks;
            _localTrack = localTrack;
            FfiClient.Instance.PublishTrackReceived += OnPublish;
        }

        internal void OnPublish(PublishTrackCallback e)
        {
            if (e.AsyncId != _asyncId)
                return;

            IsError = !string.IsNullOrEmpty(e.Error);
            IsDone = true;
            var publication = new LocalTrackPublication(e.Publication.Info);
            publication.UpdateTrack(_localTrack as Track);
            _localTrack.UpdateSid(publication.Sid);
            _internalTracks.Add(e.Publication.Info.Sid, publication);
            FfiClient.Instance.PublishTrackReceived -= OnPublish;
        }
    }
    public sealed class UnpublishTrackInstruction : YieldInstruction
    {
        private ulong _asyncId;

        internal UnpublishTrackInstruction(ulong asyncId)
        {
            _asyncId = asyncId;
            FfiClient.Instance.UnpublishTrackReceived += OnUnpublish;
        }

        internal void OnUnpublish(UnpublishTrackCallback e)
        {
            if (e.AsyncId != _asyncId)
                return;

            IsError = !string.IsNullOrEmpty(e.Error);
            IsDone = true;
            FfiClient.Instance.UnpublishTrackReceived -= OnUnpublish;
        }
    }
}
