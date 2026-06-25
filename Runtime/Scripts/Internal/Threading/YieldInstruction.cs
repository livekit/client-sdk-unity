using System;
using System.Runtime.CompilerServices;
using System.Threading;
using LiveKit.Internal;
using UnityEngine;

using LiveKit.Internal.FFI;
namespace LiveKit.Internal.Threading
{
    // Resumes awaiter continuations on Unity's main thread. Completion may be signalled on the
    // FFI callback thread (operations registered dispatchToMainThread:false, and data-stream
    // chunk events), but a custom awaiter otherwise resumes inline on the completing thread —
    // leaving callers unable to touch Unity APIs after an await. Posting through the captured
    // main-thread SynchronizationContext keeps the await path's threading identical to the
    // coroutine path. When already on the main thread (e.g. Connect, which completes there) the
    // continuation runs inline to avoid an extra frame of latency.
    internal static class AwaiterScheduler
    {
        internal static void Resume(Action continuation)
        {
            var context = FfiClient.Instance._context;
            if (context == null || SynchronizationContext.Current == context)
                continuation();
            else
                context.Post(static state => ((Action)state)(), continuation);
        }
    }

    public class YieldInstruction : CustomYieldInstruction
    {
        // Backing fields are volatile because completion may run on the FFI callback
        // thread (pending callbacks registered with dispatchToMainThread:false bypass
        // the main-thread post). The release semantics of a volatile write ensure any
        // state mutated by the completion (Error, ResultValue, etc.) is visible to the
        // main thread before it observes IsDone == true.
        private volatile bool _isDone;
        private volatile bool _isError;

        // Sentinel published once completion has fired so any continuation registered
        // afterwards runs inline instead of being silently dropped.
        private static readonly Action s_completedSentinel = () => { };
        private Action? _continuation;

        public bool IsDone
        {
            get => _isDone;
            protected set
            {
                _isDone = value;
                if (value) InvokeContinuation();
            }
        }
        public bool IsError { get => _isError; protected set => _isError = value; }

        public override bool keepWaiting => !_isDone;

        /// <summary>
        /// Returns an awaiter so callers can <c>await</c> this instruction directly.
        /// </summary>
        /// <remarks>
        /// The awaiter completes when <see cref="IsDone"/> becomes true. As with the
        /// coroutine path, success vs. failure is inspected on the instruction itself
        /// (<see cref="IsError"/> and any subclass-specific result fields); <c>GetResult</c>
        /// does not throw.
        /// </remarks>
        public YieldInstructionAwaiter GetAwaiter() => new YieldInstructionAwaiter(this);

        internal void RegisterContinuation(Action continuation)
        {
            // Race between completion-side (FFI thread writes sentinel) and await-side
            // (registers continuation): CompareExchange decides who wrote first.
            //   null     -> we won, completion will invoke our continuation later
            //   sentinel -> completion already fired; invoke inline
            //   other    -> a second awaiter beat us here, which we don't support
            var prev = Interlocked.CompareExchange(ref _continuation, continuation, null);
            if (prev == null) return;
            if (ReferenceEquals(prev, s_completedSentinel))
            {
                AwaiterScheduler.Resume(continuation);
                return;
            }
            throw new InvalidOperationException(
                "YieldInstruction does not support multiple awaiters; await it only once.");
        }

        private void InvokeContinuation()
        {
            var prev = Interlocked.Exchange(ref _continuation, s_completedSentinel);
            if (prev != null && !ReferenceEquals(prev, s_completedSentinel))
            {
                AwaiterScheduler.Resume(prev);
            }
        }
    }

    public readonly struct YieldInstructionAwaiter : ICriticalNotifyCompletion
    {
        private readonly YieldInstruction _instruction;

        internal YieldInstructionAwaiter(YieldInstruction instruction)
        {
            _instruction = instruction;
        }

        public bool IsCompleted => _instruction.IsDone;

        public void OnCompleted(Action continuation) => _instruction.RegisterContinuation(continuation);

        // ICriticalNotifyCompletion lets the async state machine skip ExecutionContext capture
        // on the hot path. We don't depend on the flowed context (AwaiterScheduler marshals to
        // the main thread on its own), so this is safe and avoids a per-await allocation.
        public void UnsafeOnCompleted(Action continuation) => _instruction.RegisterContinuation(continuation);

        // Intentionally a no-op. Parity with the coroutine path: callers inspect IsError
        // and subclass-specific result fields on the instruction itself.
        public void GetResult() { }
    }

    public class StreamYieldInstruction : CustomYieldInstruction
    {
        // Volatile so the main-thread coroutine's keepWaiting poll sees writes
        // performed by the FFI-thread chunk dispatch (which goes through a lock
        // that provides release semantics, but the unlocked reader still needs
        // acquire semantics to observe the updated value promptly).
        private volatile bool _isEos;
        private volatile bool _isCurrentReadDone;

        private static readonly Action s_completedSentinel = () => { };
        private Action? _continuation;

        /// <summary>
        /// True if the stream has reached the end.
        /// </summary>
        public bool IsEos
        {
            get => _isEos;
            protected set
            {
                _isEos = value;
                if (value) InvokeContinuation();
            }
        }

        /// <summary>
        /// True once a chunk is ready for the current read (before <see cref="Reset"/> is
        /// called for the next one). Public getter mirrors the sibling
        /// <c>DataTrack.ReadFrameInstruction.IsCurrentReadDone</c>; the setter stays internal
        /// because only the SDK's stream readers advance this state.
        /// </summary>
        public bool IsCurrentReadDone
        {
            get => _isCurrentReadDone;
            internal set
            {
                _isCurrentReadDone = value;
                if (value) InvokeContinuation();
            }
        }

        public override bool keepWaiting => !_isCurrentReadDone && !_isEos;

        /// <summary>
        /// Resets the yield instruction for the next read.
        /// </summary>
        /// <remarks>
        /// Calling this method after <see cref="IsEos"/> is true will throw an exception.
        /// </remarks>
        public override void Reset()
        {
            if (_isEos)
            {
                throw new InvalidOperationException("Cannot reset after end of stream");
            }
            _isCurrentReadDone = false;
            // Drop the sentinel published by the previous completion so the next awaiter
            // can install a fresh continuation. Safe because Reset is only called after the
            // previous read's await has already resumed.
            Volatile.Write(ref _continuation, null);
        }

        /// <summary>
        /// Returns an awaiter that completes when the next chunk is ready or the stream ends.
        /// Call <see cref="Reset"/> between iterations to await the following chunk.
        /// </summary>
        public StreamYieldInstructionAwaiter GetAwaiter() => new StreamYieldInstructionAwaiter(this);

        internal void RegisterContinuation(Action continuation)
        {
            var prev = Interlocked.CompareExchange(ref _continuation, continuation, null);
            if (prev == null) return;
            if (ReferenceEquals(prev, s_completedSentinel))
            {
                AwaiterScheduler.Resume(continuation);
                return;
            }
            throw new InvalidOperationException(
                "StreamYieldInstruction does not support multiple concurrent awaiters; await it once per chunk.");
        }

        private void InvokeContinuation()
        {
            var prev = Interlocked.Exchange(ref _continuation, s_completedSentinel);
            if (prev != null && !ReferenceEquals(prev, s_completedSentinel))
            {
                AwaiterScheduler.Resume(prev);
            }
        }
    }

    public readonly struct StreamYieldInstructionAwaiter : ICriticalNotifyCompletion
    {
        private readonly StreamYieldInstruction _instruction;

        internal StreamYieldInstructionAwaiter(StreamYieldInstruction instruction)
        {
            _instruction = instruction;
        }

        public bool IsCompleted => _instruction.IsCurrentReadDone || _instruction.IsEos;

        public void OnCompleted(Action continuation) => _instruction.RegisterContinuation(continuation);

        // See YieldInstructionAwaiter.UnsafeOnCompleted — skips ExecutionContext capture; the
        // continuation is marshalled to the main thread by AwaiterScheduler regardless.
        public void UnsafeOnCompleted(Action continuation) => _instruction.RegisterContinuation(continuation);

        public void GetResult() { }
    }
}
