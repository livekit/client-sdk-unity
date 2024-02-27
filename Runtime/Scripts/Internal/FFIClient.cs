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
    //internal delegate void ParticipantEventReceivedDelegate(ParticipantEvent e);
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
        //public event ParticipantEventReceivedDelegate ParticipantEventReceived;
        public event VideoStreamEventReceivedDelegate VideoStreamEventReceived;
        public event AudioStreamEventReceivedDelegate AudioStreamEventReceived;

#if UNITY_EDITOR
        static FfiClient()
        {
            FFICallbackDelegate callback = FFICallback;
            NativeMethods.FfiInitialize((ulong)Marshal.GetFunctionPointerForDelegate(callback), true);
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
 
        }
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            Application.quitting += Quit;
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

        static void Dispose()
        {
            // Stop all rooms synchronously
            // The rust lk implementation should also correctly dispose WebRTC
            var disposeReq = new DisposeRequest();

            var request = new FfiRequest();
            request.Dispose = disposeReq;
            SendRequest(request);
            Utils.Debug("FFIServer - Disposed");
        }

        internal static FfiResponse SendRequest(FfiRequest request)
        {
            var data = request.ToByteArray();
            FfiResponse response;
            unsafe
            {
                var handle = NativeMethods.FfiNewRequest(data, data.Length, out byte* dataPtr, out int dataLen);
                response = FfiResponse.Parser.ParseFrom(new Span<byte>(dataPtr, dataLen));
                handle.Dispose();
            }

            return response;
        }


        [AOT.MonoPInvokeCallback(typeof(FFICallbackDelegate))]
        static unsafe void FFICallback(IntPtr data, int size)
        {
            var respData = new Span<byte>(data.ToPointer(), size);
            var response = FfiEvent.Parser.ParseFrom(respData);

            // Run on the main thread, the order of execution is guaranteed by Unity
            // It uses a Queue internally
            Instance._context.Post((resp) =>
               {
                   var response = resp as FfiEvent;
                   switch (response.MessageCase)
                   {
                       case FfiEvent.MessageOneofCase.Connect:
                           Instance.ConnectReceived?.Invoke(response.Connect);
                           break;
                       case FfiEvent.MessageOneofCase.PublishTrack:
                           Instance.PublishTrackReceived?.Invoke(response.PublishTrack);
                           break;
                       case FfiEvent.MessageOneofCase.RoomEvent:
                           Instance.RoomEventReceived?.Invoke(response.RoomEvent);
                           break;
                       case FfiEvent.MessageOneofCase.TrackEvent:
                           Instance.TrackEventReceived?.Invoke(response.TrackEvent);
                           break;
                       case FfiEvent.MessageOneofCase.VideoStreamEvent:
                           Instance.VideoStreamEventReceived?.Invoke(response.VideoStreamEvent);
                           break;
                       case FfiEvent.MessageOneofCase.AudioStreamEvent:
                           Instance.AudioStreamEventReceived?.Invoke(response.AudioStreamEvent);
                           break;

                   }
               }, response);
        }
    }
}

