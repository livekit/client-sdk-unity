#if LIVEKIT_UNITASK
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiveKit
{
    /// <summary>
    /// Bridges the SDK's <see cref="YieldInstruction"/> / <see cref="StreamYieldInstruction"/>
    /// surface to UniTask, adding <see cref="CancellationToken"/> support. Available only when
    /// the <c>com.cysharp.unitask</c> package is installed; the assembly is otherwise excluded
    /// via a defineConstraint on <c>LIVEKIT_UNITASK</c>.
    /// </summary>
    public static class YieldInstructionUniTaskExtensions
    {
        /// <summary>
        /// Wraps the instruction as a <see cref="UniTask"/>. The task completes when the
        /// instruction's <see cref="YieldInstruction.IsDone"/> transitions to true, or
        /// faults with <see cref="System.OperationCanceledException"/> if the token fires
        /// first.
        /// </summary>
        /// <remarks>
        /// Cancellation has "abandon awaiter" semantics: the underlying FFI request keeps
        /// running and any result is discarded. Wire-level cancellation is not yet
        /// supported. Error inspection stays on the instruction itself — the awaiter does
        /// not throw on <see cref="YieldInstruction.IsError"/>, matching the existing
        /// <c>yield return</c> / <c>await</c> behavior.
        /// </remarks>
        public static UniTask AsUniTask(this YieldInstruction instruction, CancellationToken cancellationToken = default)
        {
            if (instruction == null) throw new System.ArgumentNullException(nameof(instruction));
            if (instruction.IsDone) return UniTask.CompletedTask;
            if (cancellationToken.IsCancellationRequested) return UniTask.FromCanceled(cancellationToken);

            var source = new UniTaskCompletionSource();
            CancellationTokenRegistration registration = default;

            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(static state =>
                {
                    var s = (UniTaskCompletionSource)state;
                    s.TrySetCanceled();
                }, source);
            }

            // YieldInstruction.RegisterContinuation fires the callback exactly once and is
            // race-safe between FFI-thread completion and main-thread registration. Either
            // TrySetResult or TrySetCanceled wins; the loser is a no-op.
            instruction.GetAwaiter().OnCompleted(() =>
            {
                registration.Dispose();
                source.TrySetResult();
            });

            return source.Task;
        }

        /// <summary>
        /// UniTask-bridged equivalent of awaiting a <see cref="StreamYieldInstruction"/> once.
        /// Call <see cref="StreamYieldInstruction.Reset"/> between chunks; each
        /// <c>AsUniTask</c> call awaits the next chunk or end-of-stream.
        /// </summary>
        public static UniTask AsUniTask(this StreamYieldInstruction instruction, CancellationToken cancellationToken = default)
        {
            if (instruction == null) throw new System.ArgumentNullException(nameof(instruction));
            // GetAwaiter().IsCompleted folds together IsCurrentReadDone || IsEos and is
            // the only public way to check the combined state from outside the LiveKit asm.
            if (instruction.GetAwaiter().IsCompleted) return UniTask.CompletedTask;
            if (cancellationToken.IsCancellationRequested) return UniTask.FromCanceled(cancellationToken);

            var source = new UniTaskCompletionSource();
            CancellationTokenRegistration registration = default;

            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(static state =>
                {
                    var s = (UniTaskCompletionSource)state;
                    s.TrySetCanceled();
                }, source);
            }

            instruction.GetAwaiter().OnCompleted(() =>
            {
                registration.Dispose();
                source.TrySetResult();
            });

            return source.Task;
        }
    }
}
#endif
