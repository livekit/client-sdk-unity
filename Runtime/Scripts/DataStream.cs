using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Error for data stream operations.
    /// </summary>
    public sealed class StreamError : Exception
    {
        public StreamError(string message) : base(message) { }

        internal StreamError(Proto.StreamError proto) : base(proto.Description) { }
    }

    /// <summary>
    /// Shared state and helpers for incremental stream reader yield instructions.
    /// Holds the latest chunk, end-of-stream flag, and error; subclasses own the
    /// typed event subscription and convert raw event payloads via <see cref="OnChunk"/>
    /// and <see cref="OnEos"/>.
    /// </summary>
    public abstract class ReadIncrementalInstructionBase<TContent> : StreamYieldInstruction
    {
        private readonly ulong _handleValue;
        private TContent _latestChunk;

        /// <summary>
        /// Error that occurred on the last read, if any.
        /// </summary>
        public StreamError Error { get; private set; }

        /// <summary>
        /// True if an error occurred on the last read.
        /// </summary>
        public bool IsError => Error != null;

        protected TContent LatestChunk
        {
            get
            {
                if (Error != null) throw Error;
                return _latestChunk;
            }
        }

        protected ReadIncrementalInstructionBase(FfiHandle readerHandle)
        {
            _handleValue = (ulong)readerHandle.DangerousGetHandle();
        }

        protected bool MatchesHandle(ulong eventHandle) => eventHandle == _handleValue;

        protected void OnChunk(TContent content)
        {
            _latestChunk = content;
            IsCurrentReadDone = true;
        }

        protected void OnEos(Proto.StreamError protoError)
        {
            IsEos = true;
            if (protoError != null)
            {
                Error = new StreamError(protoError);
            }
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

        protected void RequireTopic()
        {
            if (Topic == null)
            {
                throw new InvalidOperationException("Topic field is required");
            }
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
