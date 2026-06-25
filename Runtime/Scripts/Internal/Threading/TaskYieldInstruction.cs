using System;
using System.Threading.Tasks;

namespace LiveKit
{
    /// <summary>
    /// Adapts a <see cref="Task{TResult}"/> to a coroutine-friendly <see cref="YieldInstruction"/>.
    /// Yield on it from a coroutine, then read <see cref="Result"/> on success or
    /// <see cref="Exception"/> when <see cref="YieldInstruction.IsError"/> is true.
    /// </summary>
    public sealed class TaskYieldInstruction<T> : YieldInstruction
    {
        public T Result { get; private set; }
        public Exception Exception { get; private set; }

        internal TaskYieldInstruction(Task<T> task)
        {
            // Continuation may run on a thread-pool thread; the volatile IsDone/IsError fields in
            // the base class give the polling main thread acquire semantics for Result/Exception.
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Exception = t.Exception?.InnerException ?? t.Exception;
                    IsError = true;
                }
                else if (t.IsCanceled)
                {
                    IsError = true;
                }
                else
                {
                    Result = t.Result;
                }
                IsDone = true;
            });
        }
    }
}
