using System;
using System.Collections.Generic;
using System.Linq;
using LiveKit.Internal;
using LiveKit.Proto;

using LiveKit.Internal.Threading;
using LiveKit.Internal.FFI;
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
    /// Models the incoming stream as a single ordered queue whose items are either a
    /// chunk or a terminal end-of-stream marker (carrying an optional <see cref="StreamError"/>).
    /// The consumer advances through the queue one item at a time via <see cref="Reset"/>;
    /// end-of-stream is only observed once every buffered chunk has been drained, mirroring
    /// the JS and Python SDKs. Subclasses own the typed event subscription and convert raw
    /// event payloads via <see cref="OnChunk"/> and <see cref="OnEos"/>.
    /// </summary>
    public abstract class ReadIncrementalInstructionBase<TContent> : StreamYieldInstruction
    {
        private readonly ulong _handleValue;

        // A chunk or the terminal end-of-stream marker. Chunks and the marker share one
        // FIFO so the consumer can never observe end-of-stream while chunks remain buffered.
        private readonly struct Item
        {
            public readonly TContent Chunk;
            public readonly bool IsTerminal;
            public readonly StreamError Error;

            private Item(TContent chunk, bool isTerminal, StreamError error)
            {
                Chunk = chunk;
                IsTerminal = isTerminal;
                Error = error;
            }

            public static Item ForChunk(TContent chunk) => new Item(chunk, false, null);
            public static Item ForTerminal(StreamError error) => new Item(default, true, error);
        }

        // Items buffered beyond the one the consumer is currently positioned on.
        private readonly Queue<Item> _queue = new();
        private Item _current;
        private bool _hasCurrent;

        // Chunk/EOS events arrive on the FFI thread; Reset() and the LatestChunk getter run on
        // the main-thread coroutine. _gate serializes mutations of the queue, the current item,
        // IsCurrentReadDone, IsEos, and Error across both sides.
        private readonly object _gate = new();

        /// <summary>
        /// Error that occurred on the last read, if any.
        /// </summary>
        public StreamError Error { get; private set; }

        /// <summary>
        /// True if an error occurred on the last read.
        /// </summary>
        public bool IsError => Error != null;

        /// <summary>
        /// The chunk from the most recent completed read. Throws the captured
        /// <see cref="StreamError"/> if the stream ended with an error. Returns the default
        /// value once positioned on a normal end-of-stream marker (there is no chunk to read).
        /// Internal so the optional UniTask async-enumerable adapter (which has
        /// InternalsVisibleTo access) can read it generically; the typed <c>Bytes</c>/<c>Text</c>
        /// accessors on the concrete readers delegate here.
        /// </summary>
        internal TContent LatestChunk
        {
            get
            {
                lock (_gate)
                {
                    if (Error != null) throw Error;
                    if (!_hasCurrent) return default;
                    return _current.Chunk;
                }
            }
        }

        protected ReadIncrementalInstructionBase(FfiHandle readerHandle)
        {
            _handleValue = (ulong)readerHandle.DangerousGetHandle();
        }

        protected bool MatchesHandle(ulong eventHandle) => eventHandle == _handleValue;

        protected void OnChunk(TContent content)
        {
            lock (_gate)
            {
                if (_hasCurrent)
                {
                    // Consumer hasn't advanced onto the current item yet; buffer in order.
                    _queue.Enqueue(Item.ForChunk(content));
                }
                else
                {
                    Advance(Item.ForChunk(content));
                }
            }
        }

        public override void Reset()
        {
            // base.Reset() must run under the same lock as OnChunk/OnEos, otherwise the
            // window between IsCurrentReadDone=false (from base) and the dequeue below lets
            // a producer race in, position the current item, and have it immediately
            // overwritten by the dequeue. base.Reset() also throws when the consumer tries to
            // advance past the terminal marker (IsEos), which is the intended guard.
            lock (_gate)
            {
                base.Reset();
                if (_queue.Count > 0)
                {
                    Advance(_queue.Dequeue());
                }
                else
                {
                    // Nothing buffered: park until the next chunk or the terminal marker
                    // arrives. keepWaiting stays true until then.
                    _hasCurrent = false;
                }
            }
        }

        protected void OnEos(Proto.StreamError protoError)
        {
            lock (_gate)
            {
                var error = protoError != null ? new StreamError(protoError) : null;
                if (_hasCurrent)
                {
                    // Ordered after every buffered chunk; the consumer drains them first.
                    _queue.Enqueue(Item.ForTerminal(error));
                }
                else
                {
                    Advance(Item.ForTerminal(error));
                }
            }
        }

        // Positions the consumer on the given item and flips the matching completion flag.
        // Caller must hold _gate.
        private void Advance(Item item)
        {
            _current = item;
            _hasCurrent = true;
            if (item.IsTerminal)
            {
                // Assign Error before flipping IsEos. The IsEos setter fires the awaiter
                // continuation, which inspects IsError/Error on resume; setting IsEos first
                // would let the continuation observe IsError == false and silently swallow
                // the stream error.
                if (item.Error != null) Error = item.Error;
                IsEos = true;
            }
            else
            {
                IsCurrentReadDone = true;
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
