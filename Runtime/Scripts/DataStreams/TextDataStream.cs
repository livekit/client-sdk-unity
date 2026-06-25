using System;
using System.Collections.Generic;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    /// <summary>
    /// Information about a text data stream.
    /// </summary>
    public sealed class TextStreamInfo : StreamInfo
    {
        /// <summary>
        /// Operation type for text streams.
        /// </summary>
        public enum OperationType
        {
            Create = 0,
            Update = 1,
            Delete = 2,
            Reaction = 3
        }

        public OperationType Operation { get; }
        public int Version { get; }
        public string ReplyToStreamId { get; }
        public IReadOnlyList<string> AttachedStreamIds { get; }
        public bool Generated { get; }

        internal TextStreamInfo(Proto.TextStreamInfo proto) : base(
            proto.StreamId,
            proto.Topic,
            proto.Timestamp,
            proto.TotalLength,
            proto.Attributes,
            proto.MimeType)
        {
            Operation = (OperationType)proto.OperationType;
            Version = proto.Version;
            ReplyToStreamId = proto.ReplyToStreamId;
            AttachedStreamIds = proto.AttachedStreamIds;
            Generated = proto.Generated;
        }
    }

    /// <summary>
    /// Delegate for handling incoming text data streams.
    /// </summary>
    public delegate void TextStreamHandler(TextStreamReader reader, string identity);

    /// <summary>
    /// Reader for an incoming text data stream.
    /// </summary>
    public sealed class TextStreamReader : IDisposable
    {
        private readonly FfiHandle _handle;
        private readonly TextStreamInfo _info;
        private bool _disposed;

        internal TextStreamReader(OwnedTextStreamReader info)
        {
            _handle = FfiHandle.FromOwnedHandle(info.Handle);
            _info = new TextStreamInfo(info.Info);
        }

        public TextStreamInfo Info => _info;

        /// <summary>
        /// Reads all incoming chunks from the stream, concatenating them into a single value
        /// once the stream closes normally.
        /// </summary>
        /// <remarks>Calling this method consumes the stream reader.</remarks>
        /// <returns>
        /// A <see cref="ReadAllInstruction"/> that completes when the stream is complete or errors.
        /// Check <see cref="ReadAllInstruction.IsError"/> and access <see cref="ReadAllInstruction.Text"/>
        /// properties to handle the result.
        /// </returns>
        public ReadAllInstruction ReadAll()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            using var request = FFIBridge.Instance.NewRequest<TextStreamReaderReadAllRequest>();
            var readAllReq = request.request;
            readAllReq.ReaderHandle = (ulong)_handle.DangerousGetHandle();

            var instruction = new ReadAllInstruction(request.RequestAsyncId);
            using var response = request.Send();
            return instruction;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _handle?.Dispose();
        }

        /// <summary>
        /// YieldInstruction for <see cref="ReadAll"/>.
        /// </summary>
        /// <remarks>
        /// Access <see cref="Text"/> after checking <see cref="IsError"/>
        /// </remarks>
        public sealed class ReadAllInstruction : FfiStreamResultInstruction<TextStreamReaderReadAllCallback, string>
        {
            internal ReadAllInstruction(ulong asyncId)
                : base(asyncId, static e => e.TextStreamReaderReadAll, static e => e.Error, static e => e.Content) { }

            public string Text => ResultValue;
        }

        /// <summary>
        /// Reads incoming chunks from the stream incrementally.
        /// </summary>
        /// <returns>
        /// A <see cref="ReadIncrementalInstruction"/> that allows reading the stream incrementally.
        /// </returns>
        public ReadIncrementalInstruction ReadIncremental()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            using var request = FFIBridge.Instance.NewRequest<TextStreamReaderReadIncrementalRequest>();
            var readIncReq = request.request;
            readIncReq.ReaderHandle = (ulong)_handle.DangerousGetHandle();
            request.Send();

            return new ReadIncrementalInstruction(_handle);
        }

        /// <summary>
        /// YieldInstruction for <see cref="ReadIncremental"/>.
        /// </summary>
        /// <remarks>
        /// Usage: while <see cref="IsEos"/> is false (i.e. the stream has not ended),
        /// call <see cref="Reset"/>, yield the instruction, and then access <see cref="Text"/>.
        /// </remarks>
        public sealed class ReadIncrementalInstruction : ReadIncrementalInstructionBase<string>
        {
            internal ReadIncrementalInstruction(FfiHandle readerHandle) : base(readerHandle)
            {
                FfiClient.Instance.TextStreamReaderEventReceived += OnStreamEvent;
            }

            private void OnStreamEvent(TextStreamReaderEvent e)
            {
                if (!MatchesHandle(e.ReaderHandle)) return;

                switch (e.DetailCase)
                {
                    case TextStreamReaderEvent.DetailOneofCase.ChunkReceived:
                        OnChunk(e.ChunkReceived.Content);
                        break;
                    case TextStreamReaderEvent.DetailOneofCase.Eos:
                        OnEos(e.Eos.Error);
                        FfiClient.Instance.TextStreamReaderEventReceived -= OnStreamEvent;
                        break;
                }
            }

            public string Text => LatestChunk;
        }
    }

    /// <summary>
    /// Options used when opening an outgoing text data stream.
    /// </summary>
    public class StreamTextOptions : StreamOptions
    {
        public TextStreamInfo.OperationType? OperationType { get; set; }
        public int? Version { get; set; }
        public string ReplyToStreamId { get; set; }
        public List<string> AttachedStreamIds { get; set; } = new List<string>();
        public bool? Generated { get; set; }

        internal Proto.StreamTextOptions ToProto()
        {
            RequireTopic();
            var proto = new Proto.StreamTextOptions();
            proto.Topic = Topic;
            proto.Attributes.Add(Attributes);
            proto.DestinationIdentities.AddRange(DestinationIdentities);

            // TODO: these fields are optional, but the generated proto is not allowing null values
            if (Id != null) proto.Id = Id;
            if (OperationType != null) proto.OperationType = (Proto.TextStreamInfo.Types.OperationType)OperationType;
            if (Version != null) proto.Version = Version.Value;
            if (ReplyToStreamId != null) proto.ReplyToStreamId = ReplyToStreamId;
            proto.AttachedStreamIds.AddRange(AttachedStreamIds);
            if (Generated != null) proto.Generated = Generated.Value;
            return proto;
        }
    }

    /// <summary>
    /// Writer for an outgoing text data stream.
    /// </summary>
    public class TextStreamWriter : IDisposable
    {
        private FfiHandle _handle;
        private readonly TextStreamInfo _info;
        private bool _disposed;

        internal TextStreamWriter(OwnedTextStreamWriter info)
        {
            _handle = FfiHandle.FromOwnedHandle(info.Handle);
            _info = new TextStreamInfo(info.Info);
        }

        public TextStreamInfo Info => _info;

        /// <summary>
        /// Writes text to the stream.
        /// </summary>
        /// <param name="text">The text to write.</param>
        /// <returns>
        /// A <see cref="WriteInstruction"/> that completes when the write operation is complete or errors.
        /// Check <see cref="WriteInstruction.Error"/> to see if the operation was successful.
        /// </returns>
        public WriteInstruction Write(string text)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            using var request = FFIBridge.Instance.NewRequest<TextStreamWriterWriteRequest>();
            var writeReq = request.request;
            writeReq.WriterHandle = (ulong)_handle.DangerousGetHandle();
            writeReq.Text = text;

            var instruction = new WriteInstruction(request.RequestAsyncId);
            using var response = request.Send();
            return instruction;
        }

        /// <summary>
        /// Closes the stream.
        /// </summary>
        /// <param name="reason">A string specifying the reason for closure, if the stream is not being closed normally.</param>
        /// <returns>
        /// A <see cref="CloseInstruction"/> that completes when the close operation is complete or errors.
        /// Check <see cref="CloseInstruction.Error"/> to see if the operation was successful.
        /// </returns>
        public CloseInstruction Close(string reason = "")
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            using var request = FFIBridge.Instance.NewRequest<TextStreamWriterCloseRequest>();
            var closeReq = request.request;
            closeReq.WriterHandle = (ulong)_handle.DangerousGetHandle();
            closeReq.Reason = reason;

            var instruction = new CloseInstruction(request.RequestAsyncId);
            using var response = request.Send();
            return instruction;
        }

        /// <summary>
        /// YieldInstruction for <see cref="Write"/>.
        /// </summary>
        /// <remarks>
        /// Check if the operation was successful by accessing <see cref="Error"/>.
        /// </remarks>
        public sealed class WriteInstruction : FfiStreamInstruction<TextStreamWriterWriteCallback>
        {
            internal WriteInstruction(ulong asyncId)
                : base(asyncId, static e => e.TextStreamWriterWrite, static e => e.Error) { }
        }

        /// <summary>
        /// YieldInstruction for <see cref="Close"/>.
        /// </summary>
        /// <remarks>
        /// Check if the operation was successful by accessing <see cref="Error"/>.
        /// </remarks>
        public sealed class CloseInstruction : FfiStreamInstruction<TextStreamWriterCloseCallback>
        {
            internal CloseInstruction(ulong asyncId)
                : base(asyncId, static e => e.TextStreamWriterClose, static e => e.Error) { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _handle?.Dispose();
        }
    }
}
