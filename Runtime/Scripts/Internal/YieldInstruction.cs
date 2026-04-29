using System;
using UnityEngine;

namespace LiveKit
{
    public class YieldInstruction : CustomYieldInstruction
    {
        // Backing fields are volatile because completion may run on the FFI callback
        // thread (raw-safe pending callbacks bypass the main-thread post). The release
        // semantics of a volatile write ensure any state mutated by the completion
        // (Error, ResultValue, etc.) is visible to the main thread before it observes
        // IsDone == true.
        private volatile bool _isDone;
        private volatile bool _isError;

        public bool IsDone { get => _isDone; protected set => _isDone = value; }
        public bool IsError { get => _isError; protected set => _isError = value; }

        public override bool keepWaiting => !_isDone;
    }

    public class StreamYieldInstruction : CustomYieldInstruction
    {
        /// <summary>
        /// True if the stream has reached the end.
        /// </summary>
        public bool IsEos { protected set; get; }

        internal bool IsCurrentReadDone { get; set; }

        public override bool keepWaiting => !IsCurrentReadDone && !IsEos;

        /// <summary>
        /// Resets the yield instruction for the next read.
        /// </summary>
        /// <remarks>
        /// Calling this method after <see cref="IsEos"/> is true will throw an exception.
        /// </remarks>
        public override void Reset()
        {
            if (IsEos)
            {
                throw new InvalidOperationException("Cannot reset after end of stream");
            }
            IsCurrentReadDone = false;
        }
    }
}
