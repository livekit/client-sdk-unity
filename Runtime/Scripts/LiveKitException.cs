using System;

namespace LiveKit
{
    /// <summary>
    /// Thrown when an awaited LiveKit operation completes with an error (its
    /// <see cref="YieldInstruction.IsError"/> is true) and the instruction has no more specific
    /// typed error to surface (e.g. <see cref="StreamError"/>, <c>RpcError</c>).
    /// </summary>
    /// <remarks>
    /// Only the <c>await</c> / UniTask paths throw. Coroutine consumers (<c>yield return</c>)
    /// are unaffected — they inspect <see cref="YieldInstruction.IsError"/> on the instruction.
    /// </remarks>
    public class LiveKitException : Exception
    {
        public LiveKitException(string message) : base(message) { }
    }
}
