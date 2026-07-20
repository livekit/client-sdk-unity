#if LIVEKIT_UNITASK
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;

namespace LiveKit
{
    /// <summary>
    /// Exposes the SDK's incremental stream readers as <see cref="IUniTaskAsyncEnumerable{T}"/>
    /// so chunks can be consumed with <c>await foreach</c>. Available only when the
    /// <c>com.cysharp.unitask</c> package is installed (gated by <c>LIVEKIT_UNITASK</c>).
    /// </summary>
    public static class StreamReaderUniTaskExtensions
    {
        /// <summary>
        /// Adapts an incremental stream read into an async sequence of chunks. Works for both
        /// <see cref="ByteStreamReader.ReadIncrementalInstruction"/> (<c>byte[]</c>) and
        /// <see cref="TextStreamReader.ReadIncrementalInstruction"/> (<c>string</c>).
        /// </summary>
        /// <remarks>
        /// Iteration ends when the stream reaches end-of-stream. Every buffered chunk is
        /// drained before iteration stops, since end-of-stream is an ordered item at the tail
        /// of the chunk queue. If the stream ends with an error, the enumerable throws that
        /// <see cref="StreamError"/> (idiomatic for <c>await foreach</c>; this is the one place
        /// the UniTask surface throws rather than exposing <c>IsError</c>). Cancellation (via
        /// the token or the enumerator) surfaces as <see cref="System.OperationCanceledException"/>
        /// with abandon-awaiter semantics — the underlying FFI read is not cancelled on the wire.
        /// </remarks>
        public static IUniTaskAsyncEnumerable<TChunk> AsAsyncEnumerable<TChunk>(
            this ReadIncrementalInstructionBase<TChunk> instruction,
            CancellationToken cancellationToken = default)
        {
            if (instruction == null) throw new System.ArgumentNullException(nameof(instruction));

            return UniTaskAsyncEnumerable.Create<TChunk>(async (writer, token) =>
            {
                // The enumerator hands us its own token; honor both it and the caller's.
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
                var ct = linked.Token;

                while (true)
                {
                    // Completes when a chunk is ready (IsCurrentReadDone) or the consumer has
                    // advanced onto the terminal marker (IsEos) — the two are mutually exclusive.
                    await instruction.AsUniTask(ct);

                    if (instruction.IsCurrentReadDone)
                    {
                        var chunk = instruction.LatestChunk;
                        await writer.YieldAsync(chunk);
                        instruction.Reset(); // advance to the next chunk or the terminal marker
                        continue;
                    }

                    // Positioned on the terminal marker: the queue is fully drained. Surface an
                    // error close by throwing, otherwise end iteration cleanly.
                    if (instruction.IsError) throw instruction.Error;
                    return;
                }
            });
        }
    }
}
#endif
