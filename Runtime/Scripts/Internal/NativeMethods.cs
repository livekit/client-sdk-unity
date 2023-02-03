using System;
using System.Security;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;

namespace LiveKit.Internal
{
    [SuppressUnmanagedCodeSecurity]
    internal static class NativeMethods
    {
#if UNITY_IOS
        const string Lib = "__Internal";
#else
        const string Lib = "livekit_ffi";
#endif

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_drop_handle")]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        internal static extern bool FFIDropHandle(IntPtr handleId);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_request")]
        internal static extern IntPtr FFIRequest(byte[] data, uint len, out FFIHandle handleId);
    }
}
