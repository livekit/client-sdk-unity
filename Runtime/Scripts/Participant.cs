using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Internal.FFIClients.Requests;
using System.Diagnostics;

namespace LiveKit
{
    public delegate Task<string> RpcHandler(RpcInvocationData data);

    public class Participant
    {
        public delegate void PublishDelegate(RemoteTrackPublication publication);

        internal ParticipantInfo _info; // Can be updated by the server through room events.
        internal readonly Dictionary<string, TrackPublication> _tracks = new();
        public FfiHandle Handle;
        public string Sid => _info.Sid;
        public string Identity => _info.Identity;
        public string Name => _info.Name;
        public string Metadata => _info.Metadata;
        public IReadOnlyDictionary<string, string> Attributes => _info.Attributes;

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
            _info = participant.Info;
        }

        [Obsolete("Use SetMetadata on LocalParticipant instead; this method has no effect")]
        public void SetMeta(string meta) {}

        [Obsolete("Use SetName on LocalParticipant instead; this method has no effect")]
        public void SetName(string name) {}

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

        [Obsolete("Use SetMetadata instead")]
        public void UpdateMetadata(string metadata)
        {
            SetMetadata(metadata);
        }

        [Obsolete("Use SetName instead")]
        public void UpdateName(string name)
        {
            SetName(name);
        }

        /// <summary>
        /// Set the metadata for the local participant.
        /// </summary>
        /// <remarks>
        /// This requires `canUpdateOwnMetadata` permission.
        /// </remarks>
        /// <param name="metadata">The new metadata.</param>
        public SetLocalMetadataInstruction SetMetadata(string metadata)
        {
            using var request = FFIBridge.Instance.NewRequest<SetLocalMetadataRequest>();
            var setReq = request.request;
            setReq.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            setReq.Metadata = metadata;

            using var response = request.Send();
            FfiResponse res = response;
            return new SetLocalMetadataInstruction(res.SetLocalMetadata.AsyncId);
        }

        /// <summary>
        /// Set the name for the local participant.
        /// </summary>
        /// <remarks>
        /// This requires `canUpdateOwnMetadata` permission.
        /// </remarks>
        /// <param name="name">The new name.</param>
        public new SetLocalNameInstruction SetName(string name)
        {
            using var request = FFIBridge.Instance.NewRequest<SetLocalNameRequest>();
            var setReq = request.request;
            setReq.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            setReq.Name = name;

            using var response = request.Send();
            FfiResponse res = response;
            return new SetLocalNameInstruction(res.SetLocalName.AsyncId);
        }

        /// <summary>
        /// Set custom attributes for the local participant.
        /// </summary>
        /// <remarks>
        /// This requires `canUpdateOwnMetadata` permission.
        /// </remarks>
        /// <param name="attributes">The new attributes. Existing attributes that
        /// are not overridden will remain unchanged.</param>
        public SetLocalAttributesInstruction SetAttributes(IDictionary<string, string> attributes)
        {
            using var request = FFIBridge.Instance.NewRequest<SetLocalAttributesRequest>();
            var setReq = request.request;
            setReq.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();

            var newAttributes = new Dictionary<string, string>(Attributes);
            foreach (var kvp in attributes)
            {
                // Override existing attributes
                newAttributes[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in newAttributes)
            {
                var entry = new AttributesEntry
                {
                    Key = kvp.Key,
                    Value = kvp.Value
                };
                setReq.Attributes.Add(entry);
            }

            using var response = request.Send();
            FfiResponse res = response;
            return new SetLocalAttributesInstruction(res.SetLocalAttributes.AsyncId);
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

            // Clear previous values of conditional fields
            publish.DestinationIdentities.Clear();
            publish.ClearTopic();

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

        /// <summary>
        /// Send text to participants in the room.
        /// </summary>
        /// <param name="text">The text content to send.</param>
        /// <param name="options">Configuration options for the text stream, including topic and
        /// destination participants.</param>
        /// <returns>
        /// A <see cref="SendTextInstruction"/> that completes when the text is sent or errors.
        /// Check <see cref="SendTextInstruction.IsError"/> and access <see cref="SendTextInstruction.Info"/>
        /// properties to handle the result.
        /// </returns>
        ///
        public SendTextInstruction SendText(string text, StreamTextOptions options)
        {
            using var request = FFIBridge.Instance.NewRequest<StreamSendTextRequest>();
            var sendTextReq = request.request;
            sendTextReq.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            sendTextReq.Text = text;
            sendTextReq.Options = options.ToProto();

            using var response = request.Send();
            FfiResponse res = response;
            return new SendTextInstruction(res.SendText.AsyncId);
        }

        /// <summary>
        /// Send text to participants in the room.
        /// </summary>
        /// <param name="text">The text content to send.</param>
        /// <param name="topic">Topic identifier used to route the stream to appropriate handlers.</param>
        /// <remarks>
        /// Use the <see cref="SendText(string, StreamTextOptions)"/> overload to set custom stream options.
        /// </remarks>
        /// <returns>
        /// A <see cref="SendTextInstruction"/> that completes when the text is sent or errors.
        /// Check <see cref="SendTextInstruction.IsError"/> and access <see cref="SendTextInstruction.Info"/>
        /// properties to handle the result.
        /// </returns>
        ///
        public SendTextInstruction SendText(string text, string topic)
        {
            var options = new StreamTextOptions();
            options.Topic = topic;
            return SendText(text, options);
        }

        /// <summary>
        /// Send a file on disk to participants in the room.
        /// </summary>
        /// <param name="path">Path to the file to be sent.</param>
        /// <param name="options">Configuration options for the byte stream, including topic and
        /// destination participants.</param>
        /// <returns>
        /// A <see cref="SendFileInstruction"/> that completes when the file is sent or errors.
        /// Check <see cref="SendFileInstruction.IsError"/> and access <see cref="SendFileInstruction.Info"/>
        /// properties to handle the result.
        /// </returns>
        ///
        public SendFileInstruction SendFile(string path, StreamByteOptions options)
        {
            using var request = FFIBridge.Instance.NewRequest<StreamSendFileRequest>();
            var sendFileReq = request.request;
            sendFileReq.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            sendFileReq.FilePath = path;
            sendFileReq.Options = options.ToProto();

            using var response = request.Send();
            FfiResponse res = response;
            return new SendFileInstruction(res.SendFile.AsyncId);
        }

        /// <summary>
        /// Send a file on disk to participants in the room.
        /// </summary>
        /// <param name="path">Path to the file to be sent.</param>
        /// <param name="topic">Topic identifier used to route the stream to appropriate handlers.</param>
        /// <remarks>
        /// Use the <see cref="SendFile(string, StreamByteOptions)"/> overload to set custom stream options.
        /// </remarks>
        /// <returns>
        /// A <see cref="SendFileInstruction"/> that completes when the file is sent or errors.
        /// Check <see cref="SendFileInstruction.IsError"/> and access <see cref="SendFileInstruction.Info"/>
        /// properties to handle the result.
        /// </returns>
        ///
        public SendFileInstruction SendFile(string path, string topic)
        {
            var options = new StreamByteOptions();
            options.Topic = topic;
            return SendFile(path, options);
        }

        /// <summary>
        /// Stream text incrementally to participants in the room.
        /// </summary>
        /// <remarks>
        /// This method allows sending text data in chunks as it becomes available.
        /// Unlike <see cref="SendText"/>, which sends the entire text at once, this method allows
        /// using a writer to send text incrementally.
        /// </remarks>
        /// <param name="options">Configuration options for the text stream, including topic and
        /// destination participants.</param>
        /// <returns>
        /// A <see cref="StreamTextInstruction"/> that completes once the stream is open or errors.
        /// Check <see cref="StreamTextInstruction.IsError"/> and access <see cref="StreamTextInstruction.Writer"/>
        /// to access the writer for the opened stream.
        /// </returns>
        public StreamTextInstruction StreamText(StreamTextOptions options)
        {
            using var request = FFIBridge.Instance.NewRequest<TextStreamOpenRequest>();
            var streamTextReq = request.request;
            streamTextReq.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            streamTextReq.Options = options.ToProto();

            using var response = request.Send();
            FfiResponse res = response;
            return new StreamTextInstruction(res.TextStreamOpen.AsyncId);
        }

        /// <summary>
        /// Stream bytes incrementally to participants in the room.
        /// </summary>
        /// <remarks>
        /// This method allows sending byte data in chunks as it becomes available.
        /// Unlike <see cref="SendFile"/>, which sends the entire file at once, this method allows
        /// using a writer to send byte data incrementally.
        /// </remarks>
        /// <param name="options">Configuration options for the byte stream, including topic and
        /// destination participants.</param>
        /// <returns>
        /// A <see cref="StreamBytesInstruction"/> that completes once the stream is open or errors.
        /// Check <see cref="StreamBytesInstruction.IsError"/> and access <see cref="StreamBytesInstruction.Writer"/>
        /// to access the writer for the opened stream.
        /// </returns>
        public StreamBytesInstruction StreamBytes(StreamByteOptions options)
        {
            using var request = FFIBridge.Instance.NewRequest<ByteStreamOpenRequest>();
            var streamBytesReq = request.request;
            streamBytesReq.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            streamBytesReq.Options = options.ToProto();

            using var response = request.Send();
            FfiResponse res = response;
            return new StreamBytesInstruction(res.ByteStreamOpen.AsyncId);
        }

        /// <summary>
        /// Stream bytes to participants in the room.
        /// </summary>
        /// <remarks>
        /// Use the <see cref="StreamBytes(StreamByteOptions)"/> overload to set custom stream options.
        /// </remarks>
        /// <param name="topic">Topic identifier used to route the stream to appropriate handlers.</param>
        /// <returns>
        /// A <see cref="StreamBytesInstruction"/> that completes once the stream is open or errors.
        /// Check <see cref="StreamBytesInstruction.IsError"/> and access <see cref="StreamBytesInstruction.Writer"/>
        /// to access the writer for the opened stream.
        /// </returns>
        public StreamBytesInstruction StreamBytes(string topic)
        {
            var options = new StreamByteOptions { Topic = topic };
            return StreamBytes(options);
        }

        /// <summary>
        /// Stream text to participants in the room.
        /// </summary>
        /// <remarks>
        /// Use the <see cref="StreamText(StreamTextOptions)"/> overload to set custom stream options.
        /// </remarks>
        /// <param name="topic">Topic identifier used to route the stream to appropriate handlers.</param>
        /// <returns>
        /// A <see cref="StreamTextInstruction"/> that completes once the stream is open or errors.
        /// Check <see cref="StreamTextInstruction.IsError"/> and access <see cref="StreamTextInstruction.Writer"/>
        /// to access the writer for the opened stream.
        /// </returns>
        public StreamTextInstruction StreamText(string topic)
        {
            var options = new StreamTextOptions { Topic = topic };
            return StreamText(options);
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

    public sealed class SetLocalMetadataInstruction : YieldInstruction
    {
        private ulong _asyncId;

        internal SetLocalMetadataInstruction(ulong asyncId)
        {
            _asyncId = asyncId;
            FfiClient.Instance.SetLocalMetadataReceived += OnSetLocalMetadata;
        }

        internal void OnSetLocalMetadata(SetLocalMetadataCallback e)
        {
            if (e.AsyncId != _asyncId)
                return;

            IsError = !string.IsNullOrEmpty(e.Error);
            IsDone = true;
            FfiClient.Instance.SetLocalMetadataReceived -= OnSetLocalMetadata;
        }
    }

    public sealed class SetLocalNameInstruction : YieldInstruction
    {
        private ulong _asyncId;

        internal SetLocalNameInstruction(ulong asyncId)
        {
            _asyncId = asyncId;
            FfiClient.Instance.SetLocalNameReceived += OnSetLocalName;
        }

        internal void OnSetLocalName(SetLocalNameCallback e)
        {
            if (e.AsyncId != _asyncId)
                return;

            IsError = !string.IsNullOrEmpty(e.Error);
            IsDone = true;
            FfiClient.Instance.SetLocalNameReceived -= OnSetLocalName;
        }
    }

    public sealed class SetLocalAttributesInstruction : YieldInstruction
    {
        private ulong _asyncId;

        internal SetLocalAttributesInstruction(ulong asyncId)
        {
            _asyncId = asyncId;
            FfiClient.Instance.SetLocalAttributesReceived += OnSetLocalAttributes;
        }

        internal void OnSetLocalAttributes(SetLocalAttributesCallback e)
        {
            if (e.AsyncId != _asyncId)
                return;

            IsError = !string.IsNullOrEmpty(e.Error);
            IsDone = true;
            FfiClient.Instance.SetLocalAttributesReceived -= OnSetLocalAttributes;
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

    /// <summary>
    /// YieldInstruction for send text. Returned by <see cref="LocalParticipant.SendText"/>.
    /// </summary>
    /// <remarks>
    /// Access <see cref="Info"/> after checking <see cref="IsError"/>
    /// </remarks>
    public sealed class SendTextInstruction : YieldInstruction
    {
        private ulong _asyncId;
        private TextStreamInfo _info;

        internal SendTextInstruction(ulong asyncId)
        {
            _asyncId = asyncId;
            FfiClient.Instance.SendTextReceived += OnSendText;
        }

        internal void OnSendText(StreamSendTextCallback e)
        {
            if (e.AsyncId != _asyncId)
                return;

            switch (e.ResultCase)
            {
                case StreamSendTextCallback.ResultOneofCase.Error:
                    Error = new StreamError(e.Error);
                    IsError = true;
                    break;
                case StreamSendTextCallback.ResultOneofCase.Info:
                    _info = new TextStreamInfo(e.Info);
                    break;
            }
            IsDone = true;
            FfiClient.Instance.SendTextReceived -= OnSendText;
        }

        public TextStreamInfo Info
        {
            get
            {
                if (IsError) throw Error;
                return _info;
            }
        }

        public StreamError Error { get; private set; }
    }

    /// <summary>
    /// YieldInstruction for send file. Returned by <see cref="LocalParticipant.SendFile"/>.
    /// </summary>
    /// <remarks>
    /// Access <see cref="Info"/> after checking <see cref="IsError"/>
    /// </remarks>
    public sealed class SendFileInstruction : YieldInstruction
    {
        private ulong _asyncId;
        private ByteStreamInfo _info;

        internal SendFileInstruction(ulong asyncId)
        {
            _asyncId = asyncId;
            FfiClient.Instance.SendFileReceived += OnSendFile;
        }

        internal void OnSendFile(StreamSendFileCallback e)
        {
            if (e.AsyncId != _asyncId)
                return;

            switch (e.ResultCase)
            {
                case StreamSendFileCallback.ResultOneofCase.Error:
                    Error = new StreamError(e.Error);
                    IsError = true;
                    break;
                case StreamSendFileCallback.ResultOneofCase.Info:
                    _info = new ByteStreamInfo(e.Info);
                    break;
            }
            IsDone = true;
            FfiClient.Instance.SendFileReceived -= OnSendFile;
        }

        public ByteStreamInfo Info
        {
            get
            {
                if (IsError) throw Error;
                return _info;
            }
        }

        public StreamError Error { get; private set; }
    }

    /// <summary>
    /// YieldInstruction for stream text. Returned by <see cref="LocalParticipant.StreamText"/>.
    /// </summary>
    /// <remarks>
    /// Access <see cref="Writer"/> after checking <see cref="IsError"/>
    /// </remarks>
    public sealed class StreamTextInstruction : YieldInstruction
    {
        private ulong _asyncId;
        private TextStreamWriter _writer;

        internal StreamTextInstruction(ulong asyncId)
        {
            _asyncId = asyncId;
            FfiClient.Instance.TextStreamOpenReceived += OnStreamOpen;
        }

        internal void OnStreamOpen(TextStreamOpenCallback e)
        {
            if (e.AsyncId != _asyncId)
                return;

            switch (e.ResultCase)
            {
                case TextStreamOpenCallback.ResultOneofCase.Error:
                    Error = new StreamError(e.Error);
                    IsError = true;
                    break;
                case TextStreamOpenCallback.ResultOneofCase.Writer:
                    _writer = new TextStreamWriter(e.Writer);
                    break;
            }
            IsDone = true;
            FfiClient.Instance.TextStreamOpenReceived -= OnStreamOpen;
        }

        public TextStreamWriter Writer
        {
            get
            {
                if (IsError) throw Error;
                return _writer;
            }
        }

        public StreamError Error { get; private set; }
    }

    /// <summary>
    /// YieldInstruction for stream bytes. Returned by <see cref="LocalParticipant.StreamBytes"/>.
    /// </summary>
    /// <remarks>
    /// Access <see cref="Writer"/> after checking <see cref="IsError"/>
    /// </remarks>
    public sealed class StreamBytesInstruction : YieldInstruction
    {
        private ulong _asyncId;
        private ByteStreamWriter _writer;

        internal StreamBytesInstruction(ulong asyncId)
        {
            _asyncId = asyncId;
            FfiClient.Instance.ByteStreamOpenReceived += OnStreamOpen;
        }

        internal void OnStreamOpen(ByteStreamOpenCallback e)
        {
            if (e.AsyncId != _asyncId)
                return;

            switch (e.ResultCase)
            {
                case ByteStreamOpenCallback.ResultOneofCase.Error:
                    Error = new StreamError(e.Error);
                    IsError = true;
                    break;
                case ByteStreamOpenCallback.ResultOneofCase.Writer:
                    _writer = new ByteStreamWriter(e.Writer);
                    break;
            }
            IsDone = true;
            FfiClient.Instance.ByteStreamOpenReceived -= OnStreamOpen;
        }

        public ByteStreamWriter Writer
        {
            get
            {
                if (IsError) throw Error;
                return _writer;
            }
        }

        public StreamError Error { get; private set; }
    }
}
