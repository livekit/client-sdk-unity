using System;
using System.Security;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;

using FfiHandleId = System.IntPtr;

namespace LiveKit.Internal.FFI
{
    [SuppressUnmanagedCodeSecurity]
    internal static class NativeMethods
    {
        #if UNITY_IOS && !UNITY_EDITOR
        const string Lib = "__Internal";
        #else
        const string Lib = "livekit_ffi";
        #endif

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_drop_handle")]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        internal extern static bool FfiDropHandle(FfiHandleId handleId);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_request")]
        internal extern static unsafe FfiHandleId FfiNewRequest(byte* data, int len, out byte* dataPtr, out UIntPtr dataLen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_initialize")]
        internal extern static FfiHandleId LiveKitInitialize(FFICallbackDelegate cb, bool captureLogs, string sdk, string sdkVersion);

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// Initialize Android WebRTC with the application context.
        /// This initializes both the JVM and ContextUtils, which is required for
        /// Android audio (microphone/speaker) to work via PlatformAudio.
        /// </summary>
        /// <param name="javaVmPtr">Pointer to the JavaVM</param>
        /// <param name="contextPtr">The Android application context (jobject)</param>
        /// <returns>true if context initialization succeeded, false otherwise.
        /// Note: JVM initialization happens regardless of return value.</returns>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_initialize_android_context")]
        internal extern static bool LiveKitInitializeAndroidContext(IntPtr javaVmPtr, IntPtr contextPtr);
#endif
    }
}