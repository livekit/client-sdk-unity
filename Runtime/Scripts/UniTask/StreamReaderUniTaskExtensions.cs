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
        /// Iteration ends when the stream reaches end-of-stream. If the stream ends with an
        /// error, the enumerable throws that <see cref="StreamError"/> (idiomatic for
        /// <c>await foreach</c>; this is the one place the UniTask surface throws rather than
        /// exposing <c>IsError</c>). Cancellation (via the token or the enumerator) surfaces as
        /// <see cref="System.OperationCanceledException"/> with abandon-awaiter semantics — the
        /// underlying FFI read is not cancelled on the wire.
        ///
        /// Like the coroutine consumer, this delivers the current chunk on the iteration where
        /// end-of-stream is also observed, then stops. Chunks buffered <em>beyond</em> the
        /// current one when end-of-stream arrives are not drainable — a pre-existing limitation
        /// of the reader (its <c>Reset()</c> is disallowed past end-of-stream), not specific to
        /// this adapter.
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
                    // Completes when a chunk is ready (IsCurrentReadDone) or the stream ends (IsEos).
                    await instruction.AsUniTask(ct);

                    if (instruction.IsCurrentReadDone)
                    {
                        var chunk = instruction.LatestChunk;
                        await writer.YieldAsync(chunk);

                        // Re-check IsEos AFTER yielding: end-of-stream may have arrived while
                        // the consumer was suspended. Reset() throws once IsEos is set, so this
                        // re-check (not a value captured before the yield) is what keeps the
                        // loop safe — mirroring the coroutine consumer's "if (IsEos) break;
                        // else Reset()" ordering.
                        if (instruction.IsEos)
                        {
                            if (instruction.IsError) throw instruction.Error;
                            return;
                        }

                        instruction.Reset();
                        continue;
                    }

                    // Not IsCurrentReadDone => end-of-stream with nothing left to read.
                    if (instruction.IsError) throw instruction.Error;
                    return;
                }
            });
        }
    }
}
#endif
