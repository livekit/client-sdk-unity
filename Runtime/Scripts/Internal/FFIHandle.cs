using System;
using System.Runtime.InteropServices;
using System.Runtime.ConstrainedExecution;

namespace LiveKit.Internal
{
    public class FfiHandle : SafeHandle
    {
        internal FfiHandle(IntPtr ptr) : base(ptr, true) { }

        public FfiHandle() : base(IntPtr.Zero, true) { }

        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        protected override bool ReleaseHandle()
        {
            return NativeMethods.FfiDropHandle(handle);
        }
    }
}
