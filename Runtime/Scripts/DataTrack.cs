using System;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;
using UnityEngine;

namespace LiveKit
{
    /// <summary>
    /// Information about a published data track.
    /// </summary>
    public sealed class DataTrackInfo
    {
        /// <summary>
        /// Unique track identifier assigned by the SFU.
        /// </summary>
        /// <remarks>
        /// This identifier may change if a reconnect occurs. Use <see cref="Name"/>
        /// if a stable identifier is needed.
        /// </remarks>
        public string Sid { get; }

        /// <summary>
        /// Name of the track assigned by the publisher.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Whether or not frames sent on the track use end-to-end encryption.
        /// </summary>
        public bool UsesE2EE { get; }

        internal DataTrackInfo(Proto.DataTrackInfo proto)
        {
            Sid = proto.Sid;
            Name = proto.Name;
            UsesE2EE = proto.UsesE2Ee;
        }
    }

    /// <summary>
    /// Options for publishing a data track.
    /// </summary>
    public class DataTrackOptions
    {
        /// <summary>
        /// The track name is used to identify the track to other participants.
        /// Must not be empty and must be unique per publisher.
        /// </summary>
        public string Name { get; set; }

        public DataTrackOptions(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Options for subscribing to a remote data track.
    /// </summary>
    public class DataTrackSubscribeOptions
    {
        /// <summary>
        /// Sets the maximum number of received frames buffered internally.
        /// </summary>
        /// <remarks>
        /// Zero is not a valid buffer size; if a value of zero is provided, it will be clamped to one.
        ///
        /// If there is already an active subscription for a given track, specifying a
        /// different buffer size when obtaining a new subscription will have no effect.
        /// </remarks>
        public uint? BufferSize { get; set; }
    }

    /// <summary>
    /// A frame published on a data track, consisting of a payload and optional metadata.
    /// </summary>
    public class DataTrackFrame
    {
        /// <summary>
        /// The frame's payload.
        /// </summary>
        public byte[] Payload { get; }

        /// <summary>
        /// The frame's user timestamp in milliseconds, if one is associated.
        /// </summary>
        public ulong? UserTimestamp { get; }

        /// <summary>
        /// Creates a new data track frame.
        /// </summary>
        /// <param name="payload">The frame's payload.</param>
        /// <param name="userTimestamp">Optional user timestamp in milliseconds.</param>
        public DataTrackFrame(byte[] payload, ulong? userTimestamp = null)
        {
            Payload = payload;
            UserTimestamp = userTimestamp;
        }

        internal DataTrackFrame(Proto.DataTrackFrame proto)
        {
            Payload = proto.Payload.ToByteArray();
            UserTimestamp = proto.HasUserTimestamp ? proto.UserTimestamp : null;
        }

        private static ulong TimestampNowMs()
        {
            return (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Associates the current Unix timestamp (in milliseconds) with the frame.
        /// Returns a new <see cref="DataTrackFrame"/> with the timestamp set; the original is
        /// not modified.
        /// </summary>
        /// <returns>A new <see cref="DataTrackFrame"/> with the current timestamp.</returns>
        public DataTrackFrame WithUserTimestampNow()
        {
            return new DataTrackFrame(Payload, TimestampNowMs());
        }

        /// <summary>
        /// If the frame has a user timestamp, calculates how long has passed
        /// relative to the current system time.
        /// </summary>
        /// <remarks>
        /// The timestamp is assumed to be a Unix timestamp in milliseconds
        /// (as set by <see cref="WithUserTimestampNow"/> on the publisher side).
        /// </remarks>
        /// <returns>The elapsed duration in seconds, or <c>null</c> if no timestamp is set.</returns>
        public double? DurationSinceTimestamp()
        {
            if (UserTimestamp == null)
                return null;
            var elapsedMs = TimestampNowMs() - UserTimestamp.Value;
            return elapsedMs / 1000.0;
        }
    }

    /// <summary>
    /// An error that can occur when pushing a frame to a data track.
    /// </summary>
    /// <remarks>
    /// Pushing a frame can fail for several reasons:
    /// <list type="bullet">
    /// <item><description>The track has been unpublished by the local participant or SFU</description></item>
    /// <item><description>The room is no longer connected</description></item>
    /// <item><description>Frames are being pushed too fast</description></item>
    /// </list>
    /// </remarks>
    public sealed class PushFrameError : Exception
    {
        public PushFrameError(string message) : base(message) { }
    }

    /// <summary>
    /// An error that can occur when subscribing to a data track.
    /// </summary>
    public sealed class SubscribeDataTrackError : Exception
    {
        public SubscribeDataTrackError(string message) : base(message) { }
    }

    /// <summary>
    /// An error that can occur when publishing a data track.
    /// </summary>
    public sealed class PublishDataTrackError : Exception
    {
        public PublishDataTrackError(string message) : base(message) { }
    }

    /// <summary>
    /// Data track published by the local participant.
    /// </summary>
    public sealed class LocalDataTrack
    {
        private readonly FfiHandle _handle;
        private readonly DataTrackInfo _info;

        internal LocalDataTrack(OwnedLocalDataTrack owned)
        {
            _handle = FfiHandle.FromOwnedHandle(owned.Handle);
            _info = new DataTrackInfo(owned.Info);
        }

        /// <summary>
        /// Information about the data track.
        /// </summary>
        public DataTrackInfo Info => _info;

        /// <summary>
        /// Try pushing a frame to subscribers of the track.
        /// </summary>
        /// <remarks>
        /// See <see cref="DataTrackFrame"/> for how to construct a frame and attach metadata.
        /// </remarks>
        /// <param name="frame">The data track frame to send.</param>
        /// <exception cref="PushFrameError">Thrown if the push fails.</exception>
        public void TryPush(DataTrackFrame frame)
        {
            using var request = FFIBridge.Instance.NewRequest<LocalDataTrackTryPushRequest>();
            var pushReq = request.request;
            pushReq.TrackHandle = (ulong)_handle.DangerousGetHandle();

            var protoFrame = new Proto.DataTrackFrame
            {
                Payload = Google.Protobuf.ByteString.CopyFrom(frame.Payload)
            };
            if (frame.UserTimestamp != null)
            {
                protoFrame.UserTimestamp = frame.UserTimestamp.Value;
            }
            pushReq.Frame = protoFrame;

            using var response = request.Send();
            FfiResponse res = response;
            if (res.LocalDataTrackTryPush.HasError)
            {
                throw new PushFrameError(res.LocalDataTrackTryPush.Error);
            }
        }

        /// <summary>
        /// Whether or not the track is still published.
        /// </summary>
        /// <returns><c>true</c> if the track is published; <c>false</c> otherwise.</returns>
        public bool IsPublished()
        {
            using var request = FFIBridge.Instance.NewRequest<LocalDataTrackIsPublishedRequest>();
            var isPublishedReq = request.request;
            isPublishedReq.TrackHandle = (ulong)_handle.DangerousGetHandle();

            using var response = request.Send();
            FfiResponse res = response;
            return res.LocalDataTrackIsPublished.IsPublished;
        }

        /// <summary>
        /// Unpublishes the track.
        /// </summary>
        public void Unpublish()
        {
            using var request = FFIBridge.Instance.NewRequest<LocalDataTrackUnpublishRequest>();
            var unpublishReq = request.request;
            unpublishReq.TrackHandle = (ulong)_handle.DangerousGetHandle();
            request.Send();
        }
    }

    /// <summary>
    /// Data track published by a remote participant.
    /// </summary>
    public sealed class RemoteDataTrack
    {
        private readonly FfiHandle _handle;
        private readonly DataTrackInfo _info;
        private readonly string _publisherIdentity;

        internal RemoteDataTrack(OwnedRemoteDataTrack owned)
        {
            _handle = FfiHandle.FromOwnedHandle(owned.Handle);
            _info = new DataTrackInfo(owned.Info);
            _publisherIdentity = owned.PublisherIdentity;
        }

        /// <summary>
        /// Information about the data track.
        /// </summary>
        public DataTrackInfo Info => _info;

        /// <summary>
        /// Identity of the participant who published the track.
        /// </summary>
        public string PublisherIdentity => _publisherIdentity;

        /// <summary>
        /// Subscribes to the data track to receive frames.
        /// </summary>
        /// <param name="options">Options for the subscription, such as buffer size.</param>
        /// <returns>
        /// A <see cref="SubscribeDataTrackInstruction"/> that completes when the subscription
        /// is established or errors.
        /// Check <see cref="SubscribeDataTrackInstruction.IsError"/> and access
        /// <see cref="SubscribeDataTrackInstruction.Subscription"/> to handle the result.
        /// </returns>
        public SubscribeDataTrackInstruction Subscribe(DataTrackSubscribeOptions options)
        {
            using var request = FFIBridge.Instance.NewRequest<SubscribeDataTrackRequest>();
            var subReq = request.request;
            subReq.TrackHandle = (ulong)_handle.DangerousGetHandle();

            var protoOptions = new Proto.DataTrackSubscribeOptions();
            if (options.BufferSize.HasValue)
                protoOptions.BufferSize = options.BufferSize.Value;
            subReq.Options = protoOptions;

            var instruction = new SubscribeDataTrackInstruction(request.RequestAsyncId);
            using var response = request.Send();
            return instruction;
        }

        /// <summary>
        /// Subscribes to the data track to receive frames using default options.
        /// </summary>
        /// <remarks>
        /// Use the <see cref="Subscribe(DataTrackSubscribeOptions)"/> overload to configure subscription options.
        /// </remarks>
        /// <returns>
        /// A <see cref="SubscribeDataTrackInstruction"/> that completes when the subscription
        /// is established or errors.
        /// Check <see cref="SubscribeDataTrackInstruction.IsError"/> and access
        /// <see cref="SubscribeDataTrackInstruction.Subscription"/> to handle the result.
        /// </returns>
        public SubscribeDataTrackInstruction Subscribe()
        {
            return Subscribe(new DataTrackSubscribeOptions());
        }

        /// <summary>
        /// Whether or not the track is still published.
        /// </summary>
        /// <returns><c>true</c> if the track is published; <c>false</c> otherwise.</returns>
        public bool IsPublished()
        {
            using var request = FFIBridge.Instance.NewRequest<RemoteDataTrackIsPublishedRequest>();
            var isPublishedReq = request.request;
            isPublishedReq.TrackHandle = (ulong)_handle.DangerousGetHandle();

            using var response = request.Send();
            FfiResponse res = response;
            return res.RemoteDataTrackIsPublished.IsPublished;
        }
    }

    /// <summary>
    /// YieldInstruction for <see cref="RemoteDataTrack.Subscribe"/>.
    /// </summary>
    /// <remarks>
    /// Access <see cref="Subscription"/> after checking <see cref="YieldInstruction.IsError"/>.
    /// </remarks>
    public sealed class SubscribeDataTrackInstruction : YieldInstruction
    {
        private ulong _asyncId;
        private DataTrackSubscription _subscription;

        internal SubscribeDataTrackInstruction(ulong asyncId)
        {
            _asyncId = asyncId;
            FfiClient.Instance.RegisterPendingCallback(asyncId, static e => e.SubscribeDataTrack, OnSubscribe, OnCanceled);
        }

        internal void OnSubscribe(SubscribeDataTrackCallback callback)
        {
            if (callback.AsyncId != _asyncId)
                return;

            switch (callback.ResultCase)
            {
                case SubscribeDataTrackCallback.ResultOneofCase.Error:
                    Error = new SubscribeDataTrackError(callback.Error);
                    IsError = true;
                    break;
                case SubscribeDataTrackCallback.ResultOneofCase.Subscription:
                    _subscription = new DataTrackSubscription(callback.Subscription);
                    break;
            }
            IsDone = true;
        }

        void OnCanceled()
        {
            Error = new SubscribeDataTrackError("Canceled");
            IsError = true;
            IsDone = true;
        }

        /// <summary>
        /// The active subscription to the data track.
        /// </summary>
        /// <exception cref="SubscribeDataTrackError">Thrown if the subscription failed.</exception>
        public DataTrackSubscription Subscription
        {
            get
            {
                if (IsError) throw Error;
                return _subscription;
            }
        }

        public SubscribeDataTrackError Error { get; private set; }
    }

    /// <summary>
    /// An active subscription to a remote data track.
    /// </summary>
    /// <remarks>
    /// Use <see cref="ReadFrame"/> in a coroutine loop to receive frames:
    /// <code>
    /// while (!subscription.IsEos)
    /// {
    ///     var frameInstruction = subscription.ReadFrame();
    ///     yield return frameInstruction;
    ///     if (frameInstruction.IsCurrentReadDone)
    ///     {
    ///         ProcessFrame(frameInstruction.Frame);
    ///     }
    /// }
    /// </code>
    /// Calling <see cref="Close"/> or dropping the subscription unsubscribes from the track.
    /// </remarks>
    public sealed class DataTrackSubscription
    {
        private readonly FfiHandle _handle;
        private readonly ulong _handleId;
        private ReadFrameInstruction _currentInstruction;
        private bool _closed;

        internal DataTrackSubscription(OwnedDataTrackSubscription owned)
        {
            _handle = FfiHandle.FromOwnedHandle(owned.Handle);
            _handleId = owned.Handle.Id;
            FfiClient.Instance.DataTrackSubscriptionEventReceived += OnSubscriptionEvent;
        }

        /// <summary>
        /// True if the subscription has ended (end of stream).
        /// </summary>
        public bool IsEos { get; private set; }

        /// <summary>
        /// Prepares to read the next frame from the subscription.
        /// </summary>
        /// <returns>
        /// A <see cref="ReadFrameInstruction"/> that completes when a frame is received
        /// or the subscription ends.
        /// </returns>
        public ReadFrameInstruction ReadFrame()
        {
            _currentInstruction = new ReadFrameInstruction();

            using var request = FFIBridge.Instance.NewRequest<DataTrackSubscriptionReadRequest>();
            request.request.SubscriptionHandle = _handleId;
            using var response = request.Send();

            return _currentInstruction;
        }

        private void OnSubscriptionEvent(DataTrackSubscriptionEvent callback)
        {
            if (callback.SubscriptionHandle != _handleId)
                return;

            switch (callback.DetailCase)
            {
                case DataTrackSubscriptionEvent.DetailOneofCase.FrameReceived:
                    _currentInstruction?.SetFrame(new DataTrackFrame(callback.FrameReceived.Frame));
                    break;
                case DataTrackSubscriptionEvent.DetailOneofCase.Eos:
                    IsEos = true;
                    _currentInstruction?.SetEos();
                    Cleanup();
                    break;
            }
        }

        /// <summary>
        /// Explicitly close the subscription and unsubscribe from the track.
        /// </summary>
        public void Close()
        {
            if (_closed) return;
            Cleanup();
            _handle.Dispose();
        }

        private void Cleanup()
        {
            if (_closed) return;
            _closed = true;
            FfiClient.Instance.DataTrackSubscriptionEventReceived -= OnSubscriptionEvent;
        }

        /// <summary>
        /// YieldInstruction for reading a single frame from a <see cref="DataTrackSubscription"/>.
        /// </summary>
        /// <remarks>
        /// Usage: while <see cref="IsEos"/> is false (i.e. the subscription has not ended),
        /// call <see cref="DataTrackSubscription.ReadFrame"/>, yield the instruction,
        /// then check <see cref="IsCurrentReadDone"/> and access <see cref="Frame"/>.
        /// </remarks>
        public sealed class ReadFrameInstruction : CustomYieldInstruction
        {
            private DataTrackFrame _frame;

            internal ReadFrameInstruction() { }

            /// <summary>
            /// True if the subscription has ended (end of stream).
            /// </summary>
            public bool IsEos { get; private set; }

            /// <summary>
            /// True if a frame has been received for the current read.
            /// </summary>
            public bool IsCurrentReadDone { get; private set; }

            public override bool keepWaiting => !IsCurrentReadDone && !IsEos;

            internal void SetFrame(DataTrackFrame frame)
            {
                _frame = frame;
                IsCurrentReadDone = true;
            }

            internal void SetEos()
            {
                IsEos = true;
            }

            /// <summary>
            /// The received data track frame.
            /// </summary>
            public DataTrackFrame Frame => _frame;
        }
    }
}
