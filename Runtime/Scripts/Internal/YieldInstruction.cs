using System;
using UnityEngine;

namespace LiveKit
{
    public class YieldInstruction : CustomYieldInstruction
    {
        // Backing fields are volatile because completion may run on the FFI callback
        // thread (pending callbacks registered with dispatchToMainThread:false bypass
        // the main-thread post). The release semantics of a volatile write ensure any
        // state mutated by the completion (Error, ResultValue, etc.) is visible to the
        // main thread before it observes IsDone == true.
        private volatile bool _isDone;
        private volatile bool _isError;

        public bool IsDone { get => _isDone; protected set => _isDone = value; }
        public bool IsError { get => _isError; protected set => _isError = value; }

        public override bool keepWaiting => !_isDone;
    }

    public class StreamYieldInstruction : CustomYieldInstruction
    {
        // Volatile so the main-thread coroutine's keepWaiting poll sees writes
        // performed by the FFI-thread chunk dispatch (which goes through a lock
        // that provides release semantics, but the unlocked reader still needs
        // acquire semantics to observe the updated value promptly).
        private volatile bool _isEos;
        private volatile bool _isCurrentReadDone;

        /// <summary>
        /// True if the stream has reached the end.
        /// </summary>
        public bool IsEos { get => _isEos; protected set => _isEos = value; }

        internal bool IsCurrentReadDone { get => _isCurrentReadDone; set => _isCurrentReadDone = value; }

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
        }
    }
}
