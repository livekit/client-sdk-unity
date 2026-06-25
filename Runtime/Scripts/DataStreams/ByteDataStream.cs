using System;
using System.Collections.Generic;
using System.Linq;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
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
    /// Delegate for handling incoming byte data streams.
    /// </summary>
    public delegate void ByteStreamHandler(ByteStreamReader reader, string identity);

    /// <summary>
    /// Reader for an incoming byte data stream.
    /// </summary>
    public sealed class ByteStreamReader : IDisposable
    {
        private FfiHandle _handle;
        private readonly ByteStreamInfo _info;
        private bool _disposed;

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
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            using var request = FFIBridge.Instance.NewRequest<ByteStreamReaderReadAllRequest>();
            var readAllReq = request.request;
            readAllReq.ReaderHandle = (ulong)_handle.DangerousGetHandle();

            var instruction = new ReadAllInstruction(request.RequestAsyncId);
            using var response = request.Send();
            return instruction;
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
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            using var request = FFIBridge.Instance.NewRequest<ByteStreamReaderWriteToFileRequest>();
            var writeToFileReq = request.request;
            writeToFileReq.ReaderHandle = (ulong)_handle.DangerousGetHandle();
            writeToFileReq.Directory = directory;
            writeToFileReq.NameOverride = nameOverride;

            var instruction = new WriteToFileInstruction(request.RequestAsyncId);
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
        /// Access <see cref="Bytes"/> after checking <see cref="IsError"/>
        /// </remarks>
        public sealed class ReadAllInstruction : FfiStreamResultInstruction<ByteStreamReaderReadAllCallback, byte[]>
        {
            internal ReadAllInstruction(ulong asyncId)
                : base(asyncId, static e => e.ByteStreamReaderReadAll, static e => e.Error, static e => e.Content.ToArray()) { }

            public byte[] Bytes => ResultValue;
        }

        /// <summary>
        /// YieldInstruction for <see cref="ReadIncremental"/>.
        /// </summary>
        /// <remarks>
        /// Usage: while <see cref="IsEos"/> is false (i.e. the stream has not ended),
        /// call <see cref="Reset"/>, yield the instruction, and then access <see cref="Bytes"/>.
        /// </remarks>
        public sealed class ReadIncrementalInstruction : ReadIncrementalInstructionBase<byte[]>
        {
            internal ReadIncrementalInstruction(FfiHandle readerHandle) : base(readerHandle)
            {
                FfiClient.Instance.ByteStreamReaderEventReceived += OnStreamEvent;
            }

            private void OnStreamEvent(ByteStreamReaderEvent e)
            {
                if (!MatchesHandle(e.ReaderHandle)) return;

                switch (e.DetailCase)
                {
                    case ByteStreamReaderEvent.DetailOneofCase.ChunkReceived:
                        OnChunk(e.ChunkReceived.Content.ToByteArray());
                        break;
                    case ByteStreamReaderEvent.DetailOneofCase.Eos:
                        OnEos(e.Eos.Error);
                        FfiClient.Instance.ByteStreamReaderEventReceived -= OnStreamEvent;
                        break;
                }
            }

            public byte[] Bytes => LatestChunk;
        }

        /// <summary>
        /// YieldInstruction for <see cref="WriteToFile"/>.
        /// </summary>
        /// <remarks>
        /// Access <see cref="FilePath"/> after checking <see cref="IsError"/>
        /// </remarks>
        public sealed class WriteToFileInstruction : FfiStreamResultInstruction<ByteStreamReaderWriteToFileCallback, string>
        {
            internal WriteToFileInstruction(ulong asyncId)
                : base(asyncId, static e => e.ByteStreamReaderWriteToFile, static e => e.Error, static e => e.FilePath) { }

            /// <summary>
            /// Path to the file that was written.
            /// </summary>
            public string FilePath => ResultValue;
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
            RequireTopic();
            var proto = new Proto.StreamByteOptions();
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
    /// Writer for an outgoing byte data stream.
    /// </summary>
    public class ByteStreamWriter : IDisposable
    {
        private FfiHandle _handle;
        private readonly ByteStreamInfo _info;
        private bool _disposed;

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
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            using var request = FFIBridge.Instance.NewRequest<ByteStreamWriterWriteRequest>();
            var writeReq = request.request;
            writeReq.WriterHandle = (ulong)_handle.DangerousGetHandle();
            writeReq.Bytes = Google.Protobuf.ByteString.CopyFrom(bytes);

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
        /// </returns>
        public CloseInstruction Close(string reason = "")
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            using var request = FFIBridge.Instance.NewRequest<ByteStreamWriterCloseRequest>();
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
        public sealed class WriteInstruction : FfiStreamInstruction<ByteStreamWriterWriteCallback>
        {
            internal WriteInstruction(ulong asyncId)
                : base(asyncId, static e => e.ByteStreamWriterWrite, static e => e.Error) { }
        }

        /// <summary>
        /// YieldInstruction for <see cref="Close"/>.
        /// </summary>
        /// <remarks>
        /// Check if the operation was successful by accessing <see cref="Error"/>.
        /// </remarks>
        public sealed class CloseInstruction : FfiStreamInstruction<ByteStreamWriterCloseCallback>
        {
            internal CloseInstruction(ulong asyncId)
                : base(asyncId, static e => e.ByteStreamWriterClose, static e => e.Error) { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _handle?.Dispose();
        }
    }
}
