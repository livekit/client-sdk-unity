using System;
using LiveKit.Proto;

namespace LiveKit
{
    /// <summary>
    /// Generic one-shot async instruction for FFI operations that only need
    /// error/done status from the callback.
    /// </summary>
    /// <typeparam name="TCallback">The protobuf callback type returned by Rust.</typeparam>
    public sealed class FfiInstruction<TCallback> : YieldInstruction where TCallback : class
    {
        internal FfiInstruction(
            ulong asyncId,
            Func<FfiEvent, TCallback?> selector,
            Func<TCallback, string?> errorExtractor)
        {
            Internal.FfiClient.Instance.RegisterPendingCallback(
                asyncId,
                selector,
                callback =>
                {
                    var error = errorExtractor(callback);
                    IsError = !string.IsNullOrEmpty(error);
                    IsDone = true;
                },
                () =>
                {
                    IsError = true;
                    IsDone = true;
                }
            );
        }
    }
}
