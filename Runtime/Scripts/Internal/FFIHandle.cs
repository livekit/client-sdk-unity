using System;
using System.Runtime.InteropServices;
using System.Runtime.ConstrainedExecution;

namespace LiveKit.Internal
{
    public class FfiHandle : SafeHandle
    {
        // An FFIHandle instance is always owned (Getting them from the FfiClient)
        internal FfiHandle(IntPtr ptr, bool ownsHandle) : base(ptr, ownsHandle) { }

        public FfiHandle() : base(IntPtr.Zero, true) { }

        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        protected override bool ReleaseHandle()
        {
            return NativeMethods.FfiDropHandle(handle);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
                return false;

            FfiHandle other = (FfiHandle)obj;
            return handle == other.handle;
        }

        public override int GetHashCode()
        {
            return handle.GetHashCode();
        }

        public static bool operator ==(FfiHandle lhs, FfiHandle rhs)
        {
            return lhs.handle == rhs.handle;
        }

        public static bool operator !=(FfiHandle lhs, FfiHandle rhs)
        {
            return lhs.handle != rhs.handle;
        }
    }
}
