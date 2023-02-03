using System;
using System.Runtime.InteropServices;
using System.Runtime.ConstrainedExecution;

namespace LiveKit.Internal
{
    public class FFIHandle : SafeHandle
    {
        // An FFIHandle instance is always owned (Getting them from the FFIClient)
        internal FFIHandle(IntPtr ptr) : base(ptr, true) { }

        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        protected override bool ReleaseHandle()
        {
            return NativeMethods.FFIDropHandle(handle);
        }
    }
}
