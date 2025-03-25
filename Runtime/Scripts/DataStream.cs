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
    public delegate Task TextStreamHandler(TextStreamReader reader, string identity);

    /// <summary>
    /// Delegate for handling incoming byte data streams.
    /// </summary>
    public delegate Task ByteStreamHandler(ByteStreamReader reader, string identity);

    /// <summary>
    /// Error for data stream operations.
    /// </summary>
    public class StreamError : Exception
    {
        public StreamError(string message) : base(message) { }

        internal StreamError(Proto.StreamError proto) : base(proto.Description) { }
    }

    /// <summary>
    /// Reader for an incoming text data stream.
    /// </summary>
    public class TextStreamReader
    {
        private FfiHandle _handle;
        private readonly TextStreamInfo _info;

        internal TextStreamReader(OwnedTextStreamReader info)
        {
            _handle = FfiHandle.FromOwnedHandle(info.Handle);
            _info = new TextStreamInfo(info.Info);
        }

        public TextStreamInfo Info => _info;

        public ReadAllInstruction ReadAll()
        {
            using var request = FFIBridge.Instance.NewRequest<TextStreamReaderReadAllRequest>();
            var readAllReq = request.request;
            readAllReq.ReaderHandle = (ulong)_handle.DangerousGetHandle();

            using var response = request.Send();
            FfiResponse res = response;
            return new ReadAllInstruction(res.TextReadAll.AsyncId);
        }

        // TODO: Read incremental

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
    }

    /// <summary>
    /// Reader for an incoming byte data stream.
    /// </summary>
    public class ByteStreamReader
    {
        private FfiHandle _handle;
        private readonly ByteStreamInfo _info;

        internal ByteStreamReader(OwnedByteStreamReader info)
        {
            _handle = FfiHandle.FromOwnedHandle(info.Handle);
            _info = new ByteStreamInfo(info.Info);
        }

        public ByteStreamInfo Info => _info;

        public ReadAllInstruction ReadAll()
        {
            using var request = FFIBridge.Instance.NewRequest<ByteStreamReaderReadAllRequest>();
            var readAllReq = request.request;
            readAllReq.ReaderHandle = (ulong)_handle.DangerousGetHandle();

            using var response = request.Send();
            FfiResponse res = response;
            return new ReadAllInstruction(res.ByteReadAll.AsyncId);
        }

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

        // TODO: Read incremental

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
             throw new NotImplementedException();
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
            throw new NotImplementedException();
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
}