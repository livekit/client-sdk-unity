using System;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    /// <summary>
    /// Generic yield instruction for one-shot FFI callbacks that follow the standard pattern:
    /// register a pending callback, check for a string error on completion, set IsDone.
    /// </summary>
    /// <typeparam name="TCallback">The protobuf callback type (e.g. SetLocalMetadataCallback).</typeparam>
    public class FfiInstruction<TCallback> : YieldInstruction where TCallback : class
    {
        internal FfiInstruction(
            ulong asyncId,
            Func<FfiEvent, TCallback> selector,
            Func<TCallback, string> errorExtractor)
        {
            FfiClient.Instance.RegisterPendingCallback(
                asyncId,
                selector,
                e =>
                {
                    IsError = !string.IsNullOrEmpty(errorExtractor(e));
                    IsDone = true;
                },
                () =>
                {
                    IsError = true;
                    IsDone = true;
                });
        }
    }

    /// <summary>
    /// Generic yield instruction for one-shot FFI callbacks that expose a <see cref="StreamError"/>.
    /// </summary>
    /// <typeparam name="TCallback">The protobuf callback type (e.g. TextStreamWriterWriteCallback).</typeparam>
    public class FfiStreamInstruction<TCallback> : YieldInstruction where TCallback : class
    {
        public StreamError Error { get; private set; }

        internal FfiStreamInstruction(
            ulong asyncId,
            Func<FfiEvent, TCallback> selector,
            Func<TCallback, Proto.StreamError> errorExtractor)
        {
            FfiClient.Instance.RegisterPendingCallback(
                asyncId,
                selector,
                e =>
                {
                    var protoError = errorExtractor(e);
                    if (protoError != null)
                    {
                        Error = new StreamError(protoError);
                        IsError = true;
                    }
                    IsDone = true;
                },
                () =>
                {
                    Error = new StreamError("Canceled");
                    IsError = true;
                    IsDone = true;
                });
        }
    }
}
