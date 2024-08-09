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

        internal static bool FfiDropHandle(ulong handleId) => FfiDropHandle((IntPtr)handleId);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_drop_handle")]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        internal extern static bool FfiDropHandle(IntPtr handleId);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_request")]
        internal static extern unsafe IntPtr FfiNewRequest(byte* data, UIntPtr len, out byte* dataPtr,
            out UIntPtr dataLen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_initialize")]
        internal extern static IntPtr LiveKitInitialize(FFICallbackDelegate cb, bool captureLogs);
    }
}