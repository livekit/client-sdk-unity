using System;
using System.Threading;
using System.Runtime.InteropServices;
using System.Runtime.ConstrainedExecution;
using LiveKit.Proto;

namespace LiveKit.Internal
{
    public class FfiHandle : SafeHandle
    {
        internal FfiHandle(IntPtr ptr) : base(ptr, true) { }

        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        protected override bool ReleaseHandle()
        {
            var context = FfiClient.Instance._context;
            if (context != null && SynchronizationContext.Current != context)
            {
                // Called from the GC finalizer thread (or another non-main thread).
                // The Rust drop implementation for some handle types (e.g. outgoing
                // data streams) requires a Tokio runtime, which only exists on the
                // main Unity thread. Marshal the drop there to avoid a Rust panic.
                var h = handle;
                context.Post(_ => NativeMethods.FfiDropHandle(h), null);
                return true;
            }
            return NativeMethods.FfiDropHandle(handle);
        }

        public static FfiHandle FromOwnedHandle(FfiOwnedHandle handle)
        {
            return new FfiHandle((IntPtr)handle.Id);
        }
    }

}
