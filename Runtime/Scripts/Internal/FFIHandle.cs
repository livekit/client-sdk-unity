using System;
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
            return NativeMethods.FfiDropHandle(handle);
        }

        public static FfiHandle FromOwnedHandle(FfiOwnedHandle handle)
        {
            return new FfiHandle((IntPtr)handle.Id);
        }
    }

}
