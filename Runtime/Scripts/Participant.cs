using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Protobuf.Collections;
using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit
{
    public delegate Task<string> RpcHandler(RpcInvocationData data);

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
        public MapField<string, string> Attributes => _info.Attributes;
        public ConnectionQuality ConnectionQuality { internal set; get; }
        public event PublishDelegate TrackPublished;
        public event PublishDelegate TrackUnpublished;

        public readonly WeakReference<Room> Room;
        public IReadOnlyDictionary<string, TrackPublication> Tracks => _tracks;

        protected Dictionary<string, RpcHandler> _rpcHandlers = new();

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


        /// <summary>
        /// Performs RPC on another participant in the room.
        /// This allows you to execute a custom method on a remote participant and await their response.
        /// </summary>
        /// <param name="rpcParams">Parameters for the RPC call including:
        /// - DestinationIdentity: The identity of the participant to call
        /// - Method: Name of the method to call (up to 64 bytes UTF-8)
        /// - Payload: String payload (max 15KiB UTF-8)
        /// - ResponseTimeout: Maximum time to wait for response (defaults to 10 seconds)</param>
        /// <returns>
        /// A <see cref="PerformRpcInstruction"/> that completes when the RPC call receives a response or errors.
        /// Check <see cref="PerformRpcInstruction.IsError"/> and access <see cref="PerformRpcInstruction.Payload"/>/<see cref="PerformRpcInstruction.Error"/> properties to handle the result.
        /// </returns>
        /// <remarks>
        /// See https://docs.livekit.io/home/client/data/rpc/#errors for a list of possible error codes.
        /// </remarks>
        public PerformRpcInstruction PerformRpc(PerformRpcParams rpcParams)
        {
            using var request = FFIBridge.Instance.NewRequest<PerformRpcRequest>();
            var rpcReq = request.request;
            rpcReq.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            rpcReq.DestinationIdentity = rpcParams.DestinationIdentity;
            rpcReq.Method = rpcParams.Method;
            rpcReq.Payload = rpcParams.Payload;
            rpcReq.ResponseTimeoutMs = (uint)(rpcParams.ResponseTimeout * 1000);

            using var response = request.Send();
            FfiResponse res = response;
            return new PerformRpcInstruction(res.PerformRpc.AsyncId);
        }

        /// <summary>
        /// Registers a new RPC method handler.
        /// </summary>
        /// <param name="method">The name of the RPC method to register</param>
        /// <param name="handler">The async callback that handles incoming RPC requests. It receives an RpcInvocationData object 
        /// containing the caller's identity, payload (up to 15KiB UTF-8), and response timeout. Must return a string response or throw 
        /// an RpcError. Any other exceptions will be converted to a generic APPLICATION_ERROR (1500).</param>
        public void RegisterRpcMethod(string method, RpcHandler handler)
        {
            _rpcHandlers[method] = handler;

            using var request = FFIBridge.Instance.NewRequest<RegisterRpcMethodRequest>();
            var registerReq = request.request;
            registerReq.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            registerReq.Method = method;
            var resp = request.Send();
        }

        /// <summary>
        /// Unregisters a previously registered RPC method handler.
        /// </summary>
        /// <param name="method">The name of the RPC method to unregister</param>
        public void UnregisterRpcMethod(string method)
        {
            _rpcHandlers.Remove(method);

            using var request = FFIBridge.Instance.NewRequest<UnregisterRpcMethodRequest>();
            var unregisterReq = request.request;
            unregisterReq.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            unregisterReq.Method = method;
            var resp = request.Send();
        }

        internal async void HandleRpcMethodInvocation(
            ulong invocationId,
            string method,
            string requestId,
            string callerIdentity,
            string payload,
            float responseTimeout)
        {
            if (!_rpcHandlers.TryGetValue(method, out var handler))
            {
                SendRpcResponse(invocationId, null, RpcError.BuiltIn(RpcError.ErrorCode.UNSUPPORTED_METHOD));
                return;
            }

            try
            {
                var invocationData = new RpcInvocationData
                {
                    RequestId = requestId,
                    CallerIdentity = callerIdentity,
                    Payload = payload,
                    ResponseTimeout = responseTimeout
                };

                var result = await handler(invocationData);
                if (result == null)
                {
                    Utils.Error("RPC handler must return a string result");
                    SendRpcResponse(invocationId, null, RpcError.BuiltIn(RpcError.ErrorCode.APPLICATION_ERROR));
                    return;
                }

                SendRpcResponse(invocationId, result, null);
            }
            catch (RpcError rpcError)
            {
                SendRpcResponse(invocationId, null, rpcError);
            }
            catch (Exception e)
            {
                Utils.Error($"Uncaught error in RPC handler: {e}");
                SendRpcResponse(invocationId, null, RpcError.BuiltIn(RpcError.ErrorCode.APPLICATION_ERROR));
            }
        }

        private void SendRpcResponse(ulong invocationId, string responsePayload, RpcError responseError)
        {
            using var request = FFIBridge.Instance.NewRequest<RpcMethodInvocationResponseRequest>();
            var rpcResp = request.request;
            rpcResp.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            rpcResp.InvocationId = invocationId;

            if (responseError != null)
                rpcResp.Error = responseError.ToProto();
            if (responsePayload != null)
                rpcResp.Payload = responsePayload;

            var response = request.Send();
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

    /// <summary>
    /// YieldInstruction for RPC calls. Returned by <see cref="LocalParticipant.PerformRpc"/>.
    /// </summary>
    /// <remarks>
    /// Access <see cref="Payload"/> after checking <see cref="IsError"/>
    /// </remarks>
    public sealed class PerformRpcInstruction : YieldInstruction
    {
        private ulong _asyncId;
        private string _payload;

        internal PerformRpcInstruction(ulong asyncId)
        {
            _asyncId = asyncId;
            FfiClient.Instance.PerformRpcReceived += OnRpcResponse;
        }

        internal void OnRpcResponse(PerformRpcCallback e)
        {
            if (e.AsyncId != _asyncId)
                return;


            if (e.Error != null)
            {
                Error = RpcError.FromProto(e.Error);
                IsError = true;
                Utils.Error($"RPC error received: {Error}");
            }
            else
            {
                _payload = e.Payload;
            }
            IsDone = true;
            FfiClient.Instance.PerformRpcReceived -= OnRpcResponse;
        }

        /// <summary>
        /// Getter for the RPC response payload. Check <see cref="IsError"/> before calling this method.
        /// </summary>
        /// <exception cref="RpcError">Thrown if the RPC call resulted in an error</exception>
        public string Payload
        {
            get
            {
                if (IsError)
                    throw Error;
                return _payload;
            }
        }

        /// <summary>
        /// Getter for RPC response error.
        /// </summary>
        /// <remarks>
        /// See <see cref="RpcError"/> for more information on error codes.
        /// </remarks>
        public RpcError Error { get; private set; }
    }
}
