using System;
using UnityEngine;

namespace LiveKit
{
    public class YieldInstruction : CustomYieldInstruction
    {
        public bool IsDone { protected set; get; }
        public bool IsError { protected set; get; }

        public override bool keepWaiting => !IsDone;
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
