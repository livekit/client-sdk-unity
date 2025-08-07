using System;
using System.Runtime.InteropServices;
using RichTypes;

namespace RustAudio
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SystemStatus
    {
        public ulong streamsCount;
        [MarshalAs(UnmanagedType.I1)]
        public bool hasErrorCallback;
    }

    internal static class NativeMethods
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || PLATFORM_STANDALONE_WIN
        private const string EXTENSION = ".dll";
#else
    private const string EXTENSION = ".dylib";
#endif

        private const string LIB = "rust_audio" + EXTENSION;


        [StructLayout(LayoutKind.Sequential)]
        public struct DeviceNamesResult
        {
            public IntPtr names; // *const *const c_char
            public int length; // i32
            public IntPtr errorMessage; // *const c_char
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct InputStreamResult
        {
            public ulong streamId; // u64
            public uint sampleRate; // u32
            public uint channels; // u32
            public IntPtr errorMessage; // *const c_char
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct ConsumeFrameResult
        {
            public IntPtr ptr;                // *const f32
            public int len;                   // i32
            public int capacity;              // i32
            public IntPtr errorMessage;       // *const c_char
        }


        [StructLayout(LayoutKind.Sequential)]
        public struct ResultFFI
        {
            public IntPtr errorMessage; // *const c_char
        }


        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ErrorCallback(IntPtr message);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern ResultFFI rust_audio_init(
            ErrorCallback errorCallback
        );

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void rust_audio_deinit();

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern SystemStatus rust_audio_status();

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern DeviceNamesResult rust_audio_input_device_names();

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern void rust_audio_free_c_char_array(IntPtr ptr, UIntPtr len);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern void rust_audio_free(IntPtr ptr);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern void rust_audio_input_stream_free(ulong streamId);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern InputStreamResult rust_audio_input_stream_new(
            [MarshalAs(UnmanagedType.LPStr)] string deviceName
        );

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern ResultFFI rust_audio_input_stream_start(ulong streamId);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern ResultFFI rust_audio_input_stream_pause(ulong streamId);
        
        [DllImport("rust_audio", CallingConvention = CallingConvention.Cdecl)]
        public static extern ConsumeFrameResult rust_audio_input_stream_consume_frame(ulong streamId);

        [DllImport("rust_audio", CallingConvention = CallingConvention.Cdecl)]
        public static extern void rust_audio_input_stream_free_frame(IntPtr ptr, int len, int capacity);
 

        public static Option<string> PtrToStringAndFree(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return Option<string>.None;
            }

            string result = Marshal.PtrToStringAnsi(ptr);
            rust_audio_free(ptr);
            return Option<string>.Some(result);
        }

        public static Result<string[]> GetDeviceNames()
        {
            var res = rust_audio_input_device_names();

            var error = PtrToStringAndFree(res.errorMessage);
            if (error.Has)
            {
                return Result<string[]>.ErrorResult(error.Value);
            }

            var result = new string[res.length];
            var ptrArray = new IntPtr[res.length];
            Marshal.Copy(res.names, ptrArray, 0, res.length);

            for (int i = 0; i < res.length; i++)
                result[i] = Marshal.PtrToStringAnsi(ptrArray[i]);

            rust_audio_free_c_char_array(res.names, (UIntPtr)res.length);

            return Result<string[]>.SuccessResult(result);
        }
    }
}