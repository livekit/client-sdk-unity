#if !UNITY_WEBGL

using System;
using System.Security;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;

using FfiHandleId = System.IntPtr;

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

#if !UNITY_STANDALONE_LINUX && !UNITY_EDITOR_LINUX
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_drop_handle")]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        internal extern static bool FfiDropHandle(FfiHandleId handleId);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_request")]
        internal extern static unsafe FfiHandleId FfiNewRequest(byte* data, int len, out byte* dataPtr, out UIntPtr dataLen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_initialize")]
        internal extern static FfiHandleId LiveKitInitialize(FFICallbackDelegate cb, bool captureLogs, string sdk, string sdkVersion);
#else
        private const string ERR_MSG = "Linux is not supported";

        internal extern static bool FfiDropHandle(FfiHandleId handleId)
        {
            throw new PlatformNotSupportedException(ERR_MSG);
        }

        internal extern static unsafe FfiHandleId FfiNewRequest(byte* data, int len, out byte* dataPtr, out UIntPtr dataLen)
        {
            throw new PlatformNotSupportedException(ERR_MSG);
        }

        internal extern static FfiHandleId LiveKitInitialize(FFICallbackDelegate cb, bool captureLogs, string sdk, string sdkVersion)
        {
            throw new PlatformNotSupportedException(ERR_MSG);
        }
#endif
    }
}

#endif
