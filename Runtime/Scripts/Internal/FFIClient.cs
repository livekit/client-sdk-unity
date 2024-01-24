using System;
using System.Runtime.InteropServices;
using LiveKit.Proto;
using UnityEngine;
using Google.Protobuf;
using System.Threading;
using System.Threading.Tasks;

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
    internal delegate void DisconnectReceivedDelegate(DisconnectCallback e);

    // Events
    internal delegate void RoomEventReceivedDelegate(RoomEvent e);
    internal delegate void TrackEventReceivedDelegate(TrackEvent e);
    internal delegate void ParticipantEventReceivedDelegate(OwnedParticipant e);
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
        public event DisconnectReceivedDelegate DisconnectReceived;
        public event RoomEventReceivedDelegate RoomEventReceived;
        public event TrackEventReceivedDelegate TrackEventReceived;
       // participant events are not allowed in the fii protocol public event ParticipantEventReceivedDelegate ParticipantEventReceived;
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
            FfiClient.Initialize();
        }
#endif

        private static void Quit()
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
            Utils.Debug("Main Context created");
        }

        static void Initialize()
        {
            FFICallbackDelegate callback = FFICallback;
#if LK_VERBOSE
            NativeMethods.LiveKitInitialize(callback, true);
#else
            NativeMethods.LiveKitInitialize(callback, false);
#endif
            Utils.Debug("FFIServer - Initialized");
        }

        static void Dispose()
        {
            // Stop all rooms synchronously
            // The rust lk implementation should also correctly dispose WebRTC
            var disposeReq = new DisposeRequest();

            //TODO: object pool
            var request = new FfiRequest();
            request.Dispose = disposeReq;
            SendRequest(request);
            Utils.Debug("FFIServer - Disposed");
        }

        internal static FfiResponse SendRequest(FfiRequest request)
        {
            var data = request.ToByteArray(); // TODO(theomonnom): Avoid more allocations
            unsafe
            {
                try
                {
                    var handle = NativeMethods.FfiNewRequest(data, data.Length, out byte* dataPtr, out int dataLen);
                    var response = FfiResponse.Parser.ParseFrom(new Span<byte>(dataPtr, dataLen));
                    handle.Dispose();
                    return response;
                }
                catch (Exception e)
                {
                    // Since we are in a thread I want to make sure we catch and log
                    Utils.Error(e);
                    // But we aren't actually handling this exception so we should re-throw here 
                    throw e;
                }
            }
        }

        [AOT.MonoPInvokeCallback(typeof(FFICallbackDelegate))]
        static unsafe void FFICallback(IntPtr data, int size)
        {
            var respData = new Span<byte>(data.ToPointer(), size);
            var response = FfiEvent.Parser.ParseFrom(respData);

            Utils.Debug("Callback... "+ response.ToString());
            // Run on the main thread, the order of execution is guaranteed by Unity
            // It uses a Queue internally
            if(Instance != null && Instance._context!=null) Instance._context.Post((resp) =>
            {
                var response = resp as FfiEvent;
                if(response.MessageCase !=  FfiEvent.MessageOneofCase.Logs) Utils.Debug("Callback: " + response.MessageCase);
                switch (response.MessageCase)
                {
                    case FfiEvent.MessageOneofCase.PublishData:
                        break;
                    case FfiEvent.MessageOneofCase.Connect:
                        Instance.ConnectReceived?.Invoke(response.Connect);
                        break;
                    case FfiEvent.MessageOneofCase.PublishTrack:
                        Instance.PublishTrackReceived?.Invoke(response.PublishTrack);
                        break;
                    case FfiEvent.MessageOneofCase.RoomEvent:
                        Utils.Debug("Call back on room event: " + response.RoomEvent.MessageCase);
                        Instance.RoomEventReceived?.Invoke(response.RoomEvent);
                        break;
                    case FfiEvent.MessageOneofCase.TrackEvent:
                        Instance.TrackEventReceived?.Invoke(response.TrackEvent);
                        break;
                    case FfiEvent.MessageOneofCase.Disconnect:
                        Instance.DisconnectReceived?.Invoke(response.Disconnect);
                        break;
                    /*case FfiEvent.MessageOneofCase. ParticipantEvent:
                        Instance.ParticipantEventReceived?.Invoke(response.ParticipantEvent);
                        break;*/
                    case FfiEvent.MessageOneofCase.VideoStreamEvent:
                        Instance.VideoStreamEventReceived?.Invoke(response.VideoStreamEvent);
                        break;
                    case FfiEvent.MessageOneofCase.AudioStreamEvent:
                        Instance.AudioStreamEventReceived?.Invoke(response.AudioStreamEvent);
                        break;
                    case FfiEvent.MessageOneofCase.CaptureAudioFrame:
                        Utils.Debug(response.CaptureAudioFrame);
                        break;
                }
            }, response);
        }
    }
}

