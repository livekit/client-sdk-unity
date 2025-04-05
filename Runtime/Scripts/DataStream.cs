using System;
using System.Collections.Generic;
using LiveKit.Internal.FFIClients.Requests;
using System.Linq;
using System.Threading.Tasks;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    /// <summary>
    /// Information about a data stream.
    /// </summary>
    public class StreamInfo
    {
        /// <summary>
        /// Unique identifier of the stream.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Topic name used to route the stream to the appropriate handler.
        /// </summary>
        public string Topic { get; }

        /// <summary>
        /// When the stream was created.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Total expected size in bytes, if known.
        /// </summary>
        public ulong? TotalLength { get; }

        /// <summary>
        /// Additional attributes as needed for your application.
        /// </summary>
        public IReadOnlyDictionary<string, string> Attributes { get; }

        /// <summary>
        /// The MIME type of the stream data.
        /// </summary>
        public string MimeType { get; }

        internal StreamInfo(
            string id,
            string topic,
            long timestamp,
            ulong? totalLength,
            Google.Protobuf.Collections.MapField<string, string> attributes,
            string mimeType)
        {
            Id = id;
            Topic = topic;
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
            TotalLength = totalLength;
            Attributes = attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            MimeType = mimeType;
        }
    }

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
    /// Information about a byte data stream.
    /// </summary>
    public sealed class ByteStreamInfo : StreamInfo
    {
        public string Name { get; }

        internal ByteStreamInfo(Proto.ByteStreamInfo proto) : base(
            proto.StreamId,
            proto.Topic,
            proto.Timestamp,
            proto.TotalLength,
            proto.Attributes,
            proto.MimeType)
        {
            Name = proto.Name;
        }
    }

    /// <summary>
    /// Delegate for handling incoming text data streams.
    /// </summary>
    public delegate void TextStreamHandler(TextStreamReader reader, string identity);

    /// <summary>
    /// Delegate for handling incoming byte data streams.
    /// </summary>
    public delegate void ByteStreamHandler(ByteStreamReader reader, string identity);

    /// <summary>
    /// Error for data stream operations.
    /// </summary>
    public sealed class StreamError : Exception
    {
        public StreamError(string message) : base(message) { }

        internal StreamError(Proto.StreamError proto) : base(proto.Description) { }
    }

    /// <summary>
    /// Reader for an incoming text data stream.
    /// </summary>
    public sealed class TextStreamReader
    {
        private readonly FfiHandle _handle;
        private readonly TextStreamInfo _info;

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
            using var request = FFIBridge.Instance.NewRequest<TextStreamReaderReadAllRequest>();
            var readAllReq = request.request;
            readAllReq.ReaderHandle = (ulong)_handle.DangerousGetHandle();

            using var response = request.Send();
            FfiResponse res = response;
            return new ReadAllInstruction(res.TextReadAll.AsyncId);
        }

        /// <summary>
        /// YieldInstruction for <see cref="ReadAll"/>.
        /// </summary>
        /// <remarks>
        /// Access <see cref="Text"/> after checking <see cref="IsError"/>
        /// </remarks>
        public sealed class ReadAllInstruction : YieldInstruction
        {
            private ulong _asyncId;
            private string _text;

            internal ReadAllInstruction(ulong asyncId)
            {
                _asyncId = asyncId;
                FfiClient.Instance.TextStreamReaderReadAllReceived += OnReadAll;
            }

            internal void OnReadAll(TextStreamReaderReadAllCallback e)
            {
                if (e.AsyncId != _asyncId)
                    return;

                switch (e.ResultCase)
                {
                    case TextStreamReaderReadAllCallback.ResultOneofCase.Error:
                        Error = new StreamError(e.Error);
                        IsError = true;
                        break;
                    case TextStreamReaderReadAllCallback.ResultOneofCase.Content:
                        _text = e.Content;
                        break;
                }
                IsDone = true;
                FfiClient.Instance.TextStreamReaderReadAllReceived -= OnReadAll;
            }

            public string Text
            {
                get
                {
                    if (IsError) throw Error;
                    return _text;
                }
            }

            public StreamError Error { get; private set; }
        }

        /// <summary>
        /// Reads incoming chunks from the stream incrementally.
        /// </summary>
        /// <returns>
        /// A <see cref="ReadIncrementalInstruction"/> that allows reading the stream incrementally.
        /// </returns>
        public ReadIncrementalInstruction ReadIncremental()
        {
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
        public sealed class ReadIncrementalInstruction : StreamYieldInstruction
        {
            private readonly FfiHandle _handle;
            private string _latestChunk;

            internal ReadIncrementalInstruction(FfiHandle readerHandle)
            {
                _handle = readerHandle;
                FfiClient.Instance.TextStreamReaderEventReceived += OnStreamEvent;
            }

            private void OnStreamEvent(TextStreamReaderEvent e)
            {
                if (e.ReaderHandle != (ulong)_handle.DangerousGetHandle())
                    return;

                switch (e.DetailCase)
                {
                    case TextStreamReaderEvent.DetailOneofCase.ChunkReceived:
                        _latestChunk = e.ChunkReceived.Content;
                        IsCurrentReadDone = true;
                        break;
                    case TextStreamReaderEvent.DetailOneofCase.Eos:
                        IsEos = true;
                        if (e.Eos.Error != null)
                        {
                            Error = new StreamError(e.Eos.Error);
                        }
                        FfiClient.Instance.TextStreamReaderEventReceived -= OnStreamEvent;
                        break;
                }
            }

            public string Text
            {
                get
                {
                    if (Error != null) throw Error;
                    return _latestChunk;
                }
            }

            /// <summary>
            /// True if an error occurred on the last read.
            /// </summary>
            public bool IsError => Error != null;

            /// <summary>
            /// Error that occurred on the last read, if any.
            /// </summary>
            public StreamError Error { get; private set; }
        }
    }

    /// <summary>
    /// Reader for an incoming byte data stream.
    /// </summary>
    public sealed class ByteStreamReader
    {
        private FfiHandle _handle;
        private readonly ByteStreamInfo _info;

        internal ByteStreamReader(OwnedByteStreamReader info)
        {
            _handle = FfiHandle.FromOwnedHandle(info.Handle);
            _info = new ByteStreamInfo(info.Info);
        }

        public ByteStreamInfo Info => _info;

        /// <summary>
        /// Reads all incoming chunks from the stream, concatenating them into a single value
        /// once the stream closes normally.
        /// </summary>
        /// <remarks>Calling this method consumes the stream reader.</remarks>
        /// <returns>
        /// A <see cref="ReadAllInstruction"/> that completes when the stream is complete or errors.
        /// Check <see cref="ReadAllInstruction.IsError"/> and access <see cref="ReadAllInstruction.Bytes"/>
        /// properties to handle the result.
        /// </returns>
        public ReadAllInstruction ReadAll()
        {
            using var request = FFIBridge.Instance.NewRequest<ByteStreamReaderReadAllRequest>();
            var readAllReq = request.request;
            readAllReq.ReaderHandle = (ulong)_handle.DangerousGetHandle();

            using var response = request.Send();
            FfiResponse res = response;
            return new ReadAllInstruction(res.ByteReadAll.AsyncId);
        }

        /// <summary>
        /// Reads incoming chunks from the stream incrementally.
        /// </summary>
        /// <returns>
        /// A <see cref="ReadIncrementalInstruction"/> that allows reading the stream incrementally.
        /// </returns>
        public ReadIncrementalInstruction ReadIncremental()
        {
            using var request = FFIBridge.Instance.NewRequest<ByteStreamReaderReadIncrementalRequest>();
            var readIncReq = request.request;
            readIncReq.ReaderHandle = (ulong)_handle.DangerousGetHandle();
            request.Send();

            return new ReadIncrementalInstruction(_handle);
        }

        /// <summary>
        /// Reads incoming chunks from the byte stream, writing them to a file as they are received.
        /// </summary>
        /// <param name="directory">The directory to write the file in. The system temporary directory is used if not specified.</param>
        /// <param name="nameOverride">The name to use for the written file, overriding stream name.</param>
        /// <remarks>
        /// Calling this method consumes the stream reader.
        /// </remarks>
        /// <returns>
        /// A <see cref="WriteToFileInstruction"/> that completes when the stream is complete or errors.
        /// Check <see cref="WriteToFileInstruction.IsError"/> and access <see cref="WriteToFileInstruction.FilePath"/>
        /// properties to handle the result.
        /// </returns>
        public WriteToFileInstruction WriteToFile(string directory = null, string nameOverride = null)
        {
            using var request = FFIBridge.Instance.NewRequest<ByteStreamReaderWriteToFileRequest>();
            var writeToFileReq = request.request;
            writeToFileReq.ReaderHandle = (ulong)_handle.DangerousGetHandle();
            writeToFileReq.Directory = directory;
            writeToFileReq.NameOverride = nameOverride;

            using var response = request.Send();
            FfiResponse res = response;
            return new WriteToFileInstruction(res.ByteWriteToFile.AsyncId);
        }

        /// <summary>
        /// YieldInstruction for <see cref="ReadAll"/>.
        /// </summary>
        /// <remarks>
        /// Access <see cref="Bytes"/> after checking <see cref="IsError"/>
        /// </remarks>
        public sealed class ReadAllInstruction : YieldInstruction
        {
            private ulong _asyncId;
            private byte[] _bytes;

            internal ReadAllInstruction(ulong asyncId)
            {
                _asyncId = asyncId;
                FfiClient.Instance.ByteStreamReaderReadAllReceived += OnReadAll;
            }

            internal void OnReadAll(ByteStreamReaderReadAllCallback e)
            {
                if (e.AsyncId != _asyncId)
                    return;

                switch (e.ResultCase)
                {
                    case ByteStreamReaderReadAllCallback.ResultOneofCase.Error:
                        Error = new StreamError(e.Error);
                        IsError = true;
                        break;
                    case ByteStreamReaderReadAllCallback.ResultOneofCase.Content:
                        _bytes = e.Content.ToArray();
                        break;
                }
                IsDone = true;
                FfiClient.Instance.ByteStreamReaderReadAllReceived -= OnReadAll;
            }

            public byte[] Bytes
            {
                get
                {
                    if (IsError) throw Error;
                    return _bytes;
                }
            }

            public StreamError Error { get; private set; }
        }

        /// <summary>
        /// YieldInstruction for <see cref="ReadIncremental"/>.
        /// </summary>
        /// <remarks>
        /// Usage: while <see cref="IsEos"/> is false (i.e. the stream has not ended),
        /// call <see cref="Reset"/>, yield the instruction, and then access <see cref="Bytes"/>.
        /// </remarks>
        public sealed class ReadIncrementalInstruction : StreamYieldInstruction
        {
            private readonly FfiHandle _handle;
            private byte[] _latestChunk;

            internal ReadIncrementalInstruction(FfiHandle readerHandle)
            {
                _handle = readerHandle;
                FfiClient.Instance.ByteStreamReaderEventReceived += OnStreamEvent;
            }

            private void OnStreamEvent(ByteStreamReaderEvent e)
            {
                if (e.ReaderHandle != (ulong)_handle.DangerousGetHandle())
                    return;

                switch (e.DetailCase)
                {
                    case ByteStreamReaderEvent.DetailOneofCase.ChunkReceived:
                        _latestChunk = e.ChunkReceived.Content.ToByteArray();
                        IsCurrentReadDone = true;
                        break;
                    case ByteStreamReaderEvent.DetailOneofCase.Eos:
                        IsEos = true;
                        if (e.Eos.Error != null)
                        {
                            Error = new StreamError(e.Eos.Error);
                        }
                        FfiClient.Instance.ByteStreamReaderEventReceived -= OnStreamEvent;
                        break;
                }
            }

            public byte[] Bytes
            {
                get
                {
                    if (Error != null) throw Error;
                    return _latestChunk;
                }
            }

            /// <summary>
            /// True if an error occurred on the last read.
            /// </summary>
            public bool IsError => Error != null;

            /// <summary>
            /// Error that occurred on the last read, if any.
            /// </summary>
            public StreamError Error { get; private set; }
        }

        /// <summary>
        /// YieldInstruction for <see cref="WriteToFile"/>.
        /// </summary>
        /// <remarks>
        /// Access <see cref="FilePath"/> after checking <see cref="IsError"/>
        /// </remarks>
        public sealed class WriteToFileInstruction : YieldInstruction
        {
            private ulong _asyncId;
            private string _filePath;

            internal WriteToFileInstruction(ulong asyncId)
            {
                _asyncId = asyncId;
                FfiClient.Instance.ByteStreamReaderWriteToFileReceived += OnWriteToFile;
            }

            internal void OnWriteToFile(ByteStreamReaderWriteToFileCallback e)
            {
                if (e.AsyncId != _asyncId)
                    return;

                switch (e.ResultCase)
                {
                    case ByteStreamReaderWriteToFileCallback.ResultOneofCase.Error:
                        Error = new StreamError(e.Error);
                        IsError = true;
                        break;
                    case ByteStreamReaderWriteToFileCallback.ResultOneofCase.FilePath:
                        _filePath = e.FilePath;
                        break;
                }
                IsDone = true;
                FfiClient.Instance.ByteStreamReaderWriteToFileReceived -= OnWriteToFile;
            }

            /// <summary>
            /// Path to the file that was written.
            /// </summary>
            public string FilePath
            {
                get
                {
                    if (IsError) throw Error;
                    return _filePath;
                }
            }

            public StreamError Error { get; private set; }
        }
    }

    /// <summary>
    /// Options used when opening an outgoing data stream.
    /// </summary>
    public class StreamOptions
    {
        public string Topic { get; set; }
        public IDictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
        public List<string> DestinationIdentities { get; set; } = new List<string>();
        public string Id { get; set; }
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
            var proto = new Proto.StreamTextOptions();
            if (Topic == null)
            {
                throw new InvalidOperationException("Topic field is required");
            }
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
    /// Options used when opening an outgoing byte data stream.
    /// </summary>
    public class StreamByteOptions : StreamOptions
    {
        public string MimeType { get; set; }
        public string Name { get; set; }
        public ulong? TotalLength { get; set; }

        internal Proto.StreamByteOptions ToProto()
        {
            var proto = new Proto.StreamByteOptions();
            if (Topic == null)
            {
                throw new InvalidOperationException("Topic field is required");
            }
            proto.Topic = Topic;
            proto.Attributes.Add(Attributes);
            proto.DestinationIdentities.AddRange(DestinationIdentities);
            // TODO: these fields are optional, but the generated proto is not allowing null values
            if (Id != null) proto.Id = Id;
            if (MimeType != null) proto.MimeType = MimeType;
            if (Name != null) proto.Name = Name;
            if (TotalLength != null) proto.TotalLength = TotalLength.Value;
            return proto;
        }
    }

    /// <summary>
    /// Writer for an outgoing text data stream.
    /// </summary>
    public class TextStreamWriter
    {
        private FfiHandle _handle;
        private readonly TextStreamInfo _info;

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
            using var request = FFIBridge.Instance.NewRequest<TextStreamWriterWriteRequest>();
            var writeReq = request.request;
            writeReq.WriterHandle = (ulong)_handle.DangerousGetHandle();
            writeReq.Text = text;

            using var response = request.Send();
            FfiResponse res = response;
            return new WriteInstruction(res.TextStreamWrite.AsyncId);
        }

        /// <summary>
        /// Closes the stream.
        /// </summary>
        /// <param name="reason">A string specifying the reason for closure, if the stream is not being closed normally.</param>
        /// <returns>
        /// A <see cref="CloseInstruction"/> that completes when the close operation is complete or errors.
        /// Check <see cref="CloseInstruction.Error"/> to see if the operation was successful.
        /// </returns>
        public CloseInstruction Close(string reason = null)
        {
            using var request = FFIBridge.Instance.NewRequest<TextStreamWriterCloseRequest>();
            var closeReq = request.request;
            closeReq.WriterHandle = (ulong)_handle.DangerousGetHandle();
            closeReq.Reason = reason;

            using var response = request.Send();
            FfiResponse res = response;
            return new CloseInstruction(res.TextStreamWrite.AsyncId);
        }

        /// <summary>
        /// YieldInstruction for <see cref="Write"/>.
        /// </summary>
        /// <remarks>
        /// Check if the operation was successful by accessing <see cref="Error"/>.
        /// </remarks>
        public sealed class WriteInstruction : YieldInstruction
        {
            private ulong _asyncId;

            internal WriteInstruction(ulong asyncId)
            {
                _asyncId = asyncId;
                FfiClient.Instance.ByteStreamWriterWriteReceived += OnWrite;
            }

            internal void OnWrite(ByteStreamWriterWriteCallback e)
            {
                if (e.AsyncId != _asyncId)
                    return;

                if (e.Error != null)
                {
                    Error = new StreamError(e.Error);
                    IsError = true;
                }
                IsDone = true;
                FfiClient.Instance.ByteStreamWriterWriteReceived -= OnWrite;
            }

            public StreamError Error { get; private set; }
        }

        /// <summary>
        /// YieldInstruction for <see cref="Close"/>.
        /// </summary>
        /// <remarks>
        /// Check if the operation was successful by accessing <see cref="Error"/>.
        /// </remarks>
        public sealed class CloseInstruction : YieldInstruction
        {
            private ulong _asyncId;

            internal CloseInstruction(ulong asyncId)
            {
                _asyncId = asyncId;
                FfiClient.Instance.ByteStreamWriterCloseReceived += OnClose;
            }

            internal void OnClose(ByteStreamWriterCloseCallback e)
            {
                if (e.AsyncId != _asyncId)
                    return;

                if (e.Error != null)
                {
                    Error = new StreamError(e.Error);
                    IsError = true;
                }
                IsDone = true;
                FfiClient.Instance.ByteStreamWriterCloseReceived -= OnClose;
            }

            public StreamError Error { get; private set; }
        }
    }

    /// <summary>
    /// Writer for an outgoing byte data stream.
    /// </summary>
    public class ByteStreamWriter
    {
        private FfiHandle _handle;
        private readonly ByteStreamInfo _info;

        internal ByteStreamWriter(OwnedByteStreamWriter info)
        {
            _handle = FfiHandle.FromOwnedHandle(info.Handle);
            _info = new ByteStreamInfo(info.Info);
        }

        public ByteStreamInfo Info => _info;

        /// <summary>
        /// Writes bytes to the stream.
        /// </summary>
        /// <param name="bytes">The bytes to write.</param>
        /// <returns>
        /// A <see cref="WriteInstruction"/> that completes when the write operation is complete or errors.
        /// </returns>
        public WriteInstruction Write(byte[] bytes)
        {
            using var request = FFIBridge.Instance.NewRequest<ByteStreamWriterWriteRequest>();
            var writeReq = request.request;
            writeReq.WriterHandle = (ulong)_handle.DangerousGetHandle();
            writeReq.Bytes = Google.Protobuf.ByteString.CopyFrom(bytes);

            using var response = request.Send();
            FfiResponse res = response;
            return new WriteInstruction(res.ByteStreamWrite.AsyncId);
        }

        /// <summary>
        /// Closes the stream.
        /// </summary>
        /// <param name="reason">A string specifying the reason for closure, if the stream is not being closed normally.</param>
        /// <returns>
        /// A <see cref="CloseInstruction"/> that completes when the close operation is complete or errors.
        /// </returns>
        public CloseInstruction Close(string reason = null)
        {
            using var request = FFIBridge.Instance.NewRequest<ByteStreamWriterCloseRequest>();
            var closeReq = request.request;
            closeReq.WriterHandle = (ulong)_handle.DangerousGetHandle();
            closeReq.Reason = reason;

            using var response = request.Send();
            FfiResponse res = response;
            return new CloseInstruction(res.ByteStreamWrite.AsyncId);
        }

        /// <summary>
        /// YieldInstruction for <see cref="Write"/>.
        /// </summary>
        /// <remarks>
        /// Check if the operation was successful by accessing <see cref="Error"/>.
        /// </remarks>
        public sealed class WriteInstruction : YieldInstruction
        {
            private ulong _asyncId;

            internal WriteInstruction(ulong asyncId)
            {
                _asyncId = asyncId;
                FfiClient.Instance.TextStreamWriterWriteReceived += OnWrite;
            }

            internal void OnWrite(TextStreamWriterWriteCallback e)
            {
                if (e.AsyncId != _asyncId)
                    return;

                if (e.Error != null)
                {
                    Error = new StreamError(e.Error);
                    IsError = true;
                }
                IsDone = true;
                FfiClient.Instance.TextStreamWriterWriteReceived -= OnWrite;
            }

            public StreamError Error { get; private set; }
        }

        /// <summary>
        /// YieldInstruction for <see cref="Close"/>.
        /// </summary>
        /// <remarks>
        /// Check if the operation was successful by accessing <see cref="Error"/>.
        /// </remarks>
        public sealed class CloseInstruction : YieldInstruction
        {
            private ulong _asyncId;

            internal CloseInstruction(ulong asyncId)
            {
                _asyncId = asyncId;
                FfiClient.Instance.TextStreamWriterCloseReceived += OnClose;
            }

            internal void OnClose(TextStreamWriterCloseCallback e)
            {
                if (e.AsyncId != _asyncId)
                    return;

                if (e.Error != null)
                {
                    Error = new StreamError(e.Error);
                    IsError = true;
                }
                IsDone = true;
                FfiClient.Instance.TextStreamWriterCloseReceived -= OnClose;
            }

            public StreamError Error { get; private set; }
        }
    }

    internal sealed class StreamHandlerRegistry
    {
        private readonly Dictionary<string, TextStreamHandler> _textStreamHandlers = new();
        private readonly Dictionary<string, ByteStreamHandler> _byteStreamHandlers = new();

        internal void RegisterTextStreamHandler(string topic, TextStreamHandler handler)
        {
            if (!_textStreamHandlers.TryAdd(topic, handler))
            {
                throw new StreamError($"Text stream handler already registered for topic: {topic}");
            }
        }

        internal void RegisterByteStreamHandler(string topic, ByteStreamHandler handler)
        {
            if (!_byteStreamHandlers.TryAdd(topic, handler))
            {
                throw new StreamError($"Byte stream handler already registered for topic: {topic}");
            }
        }

        internal void UnregisterTextStreamHandler(string topic) => _textStreamHandlers.Remove(topic);
        internal void UnregisterByteStreamHandler(string topic) => _byteStreamHandlers.Remove(topic);

        internal bool Dispatch(TextStreamReader reader, string participantIdentity)
        {
            if (_textStreamHandlers.TryGetValue(reader.Info.Topic, out var handler))
            {
                handler(reader, participantIdentity);
                return true;
            }
            return false;
        }

        internal bool Dispatch(ByteStreamReader reader, string participantIdentity)
        {
            if (_byteStreamHandlers.TryGetValue(reader.Info.Topic, out var handler))
            {
                handler(reader, participantIdentity);
                return true;
            }
            return false;
        }
    }
}