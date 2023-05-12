using System;
using System.Runtime.InteropServices;
using LiveKit.Proto;
using UnityEngine;
using Google.Protobuf;
using System.Threading;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LiveKit.Internal
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void FFICallbackDelegate(IntPtr data, int size);

    // Callbacks
    internal delegate void PublishTrackDelegate(PublishTrackCallback e);
    internal delegate void ConnectReceivedDelegate(ConnectCallback e);

    // Events
    internal delegate void RoomEventReceivedDelegate(RoomEvent e);
    internal delegate void TrackEventReceivedDelegate(TrackEvent e);
    internal delegate void ParticipantEventReceivedDelegate(ParticipantEvent e);
    internal delegate void VideoStreamEventReceivedDelegate(VideoStreamEvent e);
    internal delegate void AudioStreamEventReceivedDelegate(AudioStreamEvent e);

#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    internal sealed class FfiClient
    {
        private static readonly Lazy<FfiClient> _instance = new Lazy<FfiClient>(() => new FfiClient());
        public static FfiClient Instance => _instance.Value;

        internal SynchronizationContext _context;

        public event PublishTrackDelegate PublishTrackReceived;
        public event ConnectReceivedDelegate ConnectReceived;
        public event RoomEventReceivedDelegate RoomEventReceived;
        public event TrackEventReceivedDelegate TrackEventReceived;
        public event ParticipantEventReceivedDelegate ParticipantEventReceived;
        public event VideoStreamEventReceivedDelegate VideoStreamEventReceived;
        public event AudioStreamEventReceivedDelegate AudioStreamEventReceived;

#if UNITY_EDITOR
        static FfiClient()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            EditorApplication.quitting += Quit;
            Application.quitting += Quit;
        }

        static void OnBeforeAssemblyReload()
        {
            Dispose();
        }

        static void OnAfterAssemblyReload()
        {
            Initialize();
        }
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            Application.quitting += Quit;
            Instance.Initialize();
        }
#endif

        static void Quit()
        {
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
#endif
            Dispose();
        }

        [RuntimeInitializeOnLoadMethod]
        static void GetMainContext()
        {
            // https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Scripting/UnitySynchronizationContext.cs
            Instance._context = SynchronizationContext.Current;
        }

        static void Initialize()
        {
            FFICallbackDelegate callback = FFICallback;

            var initReq = new InitializeRequest();
            initReq.EventCallbackPtr = (ulong)Marshal.GetFunctionPointerForDelegate(callback);

            var request = new FFIRequest();
            request.Initialize = initReq;
            SendRequest(request);
            Utils.Debug("FFIServer - Initialized");
        }

        static void Dispose()
        {
            // Stop all rooms synchronously
            // The rust lk implementation should also correctly dispose WebRTC
            var disposeReq = new DisposeRequest();

            var request = new FFIRequest();
            request.Dispose = disposeReq;
            SendRequest(request);
            Utils.Debug("FFIServer - Disposed");
        }

        internal static FFIResponse SendRequest(FFIRequest request)
        {
            var data = request.ToByteArray(); // TODO(theomonnom): Avoid more allocations
            FFIResponse response;
            unsafe
            {
                var handle = NativeMethods.FfiNewRequest(data, data.Length, out byte* dataPtr, out int dataLen);
                response = FFIResponse.Parser.ParseFrom(new Span<byte>(dataPtr, dataLen));
                handle.Dispose();
            }

            return response;
        }


        [AOT.MonoPInvokeCallback(typeof(FFICallbackDelegate))]
        static unsafe void FFICallback(IntPtr data, int size)
        {
            var respData = new Span<byte>(data.ToPointer(), size);
            var response = FFIEvent.Parser.ParseFrom(respData);

            // Run on the main thread, the order of execution is guaranteed by Unity
            // It uses a Queue internally
            Instance._context.Post((resp) =>
               {
                   var response = resp as FFIEvent;
                   switch (response.MessageCase)
                   {
                       case FFIEvent.MessageOneofCase.Connect:
                           Instance.ConnectReceived?.Invoke(response.Connect);
                           break;
                       case FFIEvent.MessageOneofCase.PublishTrack:
                           Instance.PublishTrackReceived?.Invoke(response.PublishTrack);
                           break;
                       case FFIEvent.MessageOneofCase.RoomEvent:
                           Instance.RoomEventReceived?.Invoke(response.RoomEvent);
                           break;
                       case FFIEvent.MessageOneofCase.TrackEvent:
                           Instance.TrackEventReceived?.Invoke(response.TrackEvent);
                           break;
                       case FFIEvent.MessageOneofCase.ParticipantEvent:
                           Instance.ParticipantEventReceived?.Invoke(response.ParticipantEvent);
                           break;
                       case FFIEvent.MessageOneofCase.VideoStreamEvent:
                           Instance.VideoStreamEventReceived?.Invoke(response.VideoStreamEvent);
                           break;
                       case FFIEvent.MessageOneofCase.AudioStreamEvent:
                           Instance.AudioStreamEventReceived?.Invoke(response.AudioStreamEvent);
                           break;

                   }
               }, response);
        }
    }
}

