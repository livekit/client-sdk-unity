using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using AOT;
using RichTypes;
using UnityEditor;
using UnityEngine;

namespace RustAudio
{
    public delegate void OnStreamAudioDelegate(Span<float> data);

    public static class RustAudioClient
    {
        #if UNITY_EDITOR
        static RustAudioClient()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            Application.quitting += Quit;
        }

        static void OnBeforeAssemblyReload()
        {
            Debug.Log(nameof(OnBeforeAssemblyReload));
            DeInit();
        }

        static void OnAfterAssemblyReload()
        {
            Debug.Log(nameof(OnAfterAssemblyReload));
            InitializeSdk();
        }
        #else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            #if NO_LIVEKIT_MODE
            return;
            #endif

            Application.quitting += Quit;
            InitializeSdk();
        }
        #endif

        private static void Quit()
        {
            #if NO_LIVEKIT_MODE
            return;
            #endif
            #if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            #endif
        }

        private static void InitializeSdk()
        {
            var result = NativeMethods.rust_audio_init(ErrorCallback);
            if (result.errorMessage != IntPtr.Zero)
            {
                Debug.LogError(
                    $"Cannot initialize rust audio: {NativeMethods.PtrToStringAndFree(result.errorMessage).Value}"
                );
            }
            else
            {
                Debug.Log("RustAudio initialized");
            }
        }

        public static void ForceReInit()
        {
            DeInit();
            InitializeSdk();
        }

        public static void DeInit()
        {
            NativeMethods.rust_audio_deinit();
            Debug.Log("RustAudio deinitialized");
        }

        public static SystemStatus SystemStatus()
        {
            return NativeMethods.rust_audio_status();
        }

        [MonoPInvokeCallback(typeof(NativeMethods.ErrorCallback))]
        private static void ErrorCallback(IntPtr msg)
        {
            // Message owned by native side
            if (msg != IntPtr.Zero)
            {
                string result = Marshal.PtrToStringAnsi(msg)!;
                Debug.LogError(result);
            }
        }


        public static Result<string[]> AvailableDeviceNames()
        {
            return NativeMethods.GetDeviceNames();
        }

        public static Result<string[]> DeviceQualityOptions(string deviceName)
        {
            return NativeMethods.DeviceQualityOptions(deviceName);
        }

        public static Result<RustAudioSource> NewStream(string deviceName)
        {
            var status = SystemStatus();
            if (status.hasErrorCallback == false)
            {
                Debug.LogWarning("Callbacks are missing, initialize sdk");
                InitializeSdk();
            }

            var result = NativeMethods.rust_audio_input_stream_new(deviceName);
            var error = NativeMethods.PtrToStringAndFree(result.errorMessage);
            if (error.Has)
            {
                return Result<RustAudioSource>.ErrorResult($"Cannot create new stream: {error.Value}");
            }

            return Result<RustAudioSource>.SuccessResult(
                new RustAudioSource(new MicrophoneInfo(deviceName, result.sampleRate, result.channels), result.streamId)
            );
        }
    }

    public class RustAudioSource : IDisposable
    {
        private readonly ulong streamId;
        public readonly MicrophoneInfo microphoneInfo;

        private readonly CancellationTokenSource cancellationTokenSource;

        private bool disposed;

        public event OnStreamAudioDelegate AudioRead;

        public bool IsRecording { get; private set; }

        internal RustAudioSource(MicrophoneInfo microphoneInfo, ulong streamId)
        {
            this.streamId = streamId;
            this.microphoneInfo = microphoneInfo;
            IsRecording = false;

            Debug.Log("RustAudioSource new");

            cancellationTokenSource = new CancellationTokenSource();
            new Thread(Capture).Start();
            #if UNITY_EDITOR
            Info.Register(streamId, this);
            #endif
        }

        // Could be optimised later to don't use a separate thread
        private void Capture()
        {
            while (cancellationTokenSource.IsCancellationRequested == false)
            {
                NativeMethods.ConsumeFrameResult frame = NativeMethods.rust_audio_input_stream_consume_frame(streamId);
                Option<string> error = NativeMethods.PtrToStringAndFree(frame.errorMessage);
                if (error.Has)
                {
                    Debug.LogError($"Error during capture: {error.Value}");
                    continue;
                }

                unsafe
                {
                    if (frame.ptr == IntPtr.Zero)
                    {
                        continue;
                    }

                    Span<float> data = new(frame.ptr.ToPointer(), frame.len);
                    AudioRead?.Invoke(data);
                    NativeMethods.rust_audio_input_stream_free_frame(frame.ptr, frame.len, frame.capacity);
                }
            }
            // ReSharper disable once FunctionNeverReturns
        }


        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            cancellationTokenSource.Cancel();
            IsRecording = false;
            NativeMethods.rust_audio_input_stream_free(streamId);
            Debug.Log("RustAudioSource disposed");
            #if UNITY_EDITOR
            Info.Unregister(streamId);
            #endif
        }

        public void StartCapture()
        {
            if (IsRecording)
                return;

            Debug.Log("RustAudioSource start");
            var result = NativeMethods.rust_audio_input_stream_start(streamId);
            var message = NativeMethods.PtrToStringAndFree(result.errorMessage);
            if (message.Has)
            {
                Debug.LogError($"Cannot start microphone stream '{microphoneInfo.name}' due error: {message.Value}");
                return;
            }

            IsRecording = true;
        }

        public void PauseCapture()
        {
            if (IsRecording == false)
                return;

            Debug.Log("RustAudioSource pause");
            var result = NativeMethods.rust_audio_input_stream_pause(streamId);
            Option<string> message = NativeMethods.PtrToStringAndFree(result.errorMessage);
            if (message.Has)
            {
                Debug.LogError($"Cannot pause microphone stream '{microphoneInfo.name}' due error: {message.Value}");
                return;
            }

            IsRecording = false;
        }

        #if UNITY_EDITOR
        public static class Info
        {
            private static readonly Dictionary<ulong, RustAudioSource> activeSources = new();

            public static void Register(ulong id, RustAudioSource source)
            {
                lock (activeSources)
                {
                    activeSources.Add(id, source);
                }
            }

            public static void Unregister(ulong id)
            {
                lock (activeSources)
                {
                    activeSources.Remove(id);
                }
            }

            public static IReadOnlyDictionary<ulong, RustAudioSource> ActiveSources()
            {
                lock (activeSources)
                {
                    return activeSources;
                }
            }
        }
        #endif
    }
}
