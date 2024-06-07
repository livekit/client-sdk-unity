using System;
using System.Security;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;

using FfiHandleId = System.UInt64;

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
        internal extern static bool FfiDropHandle(IntPtr handleId);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_request")]
        internal extern static unsafe IntPtr FfiNewRequest(byte* data, UIntPtr len, out byte* dataPtr, out UIntPtr dataLen);

        //TODO optimise FfiHandle, can be replaced by FfiHandleId = uint64_t
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_initialize")]
        internal extern static IntPtr LiveKitInitialize(FFICallbackDelegate cb, bool captureLogs);
    }
}