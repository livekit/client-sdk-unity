using System;
using LiveKit.Proto;
using UnityEngine;
using Google.Protobuf;
using System.Threading;
using LiveKit.Internal.FFIClients;
using LiveKit.Internal.FFIClients.Pools;
using UnityEngine.Pool;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LiveKit.Internal
{
    #if UNITY_EDITOR
    [InitializeOnLoad]
    #endif
    internal sealed class FfiClient : IFFIClient
    {
        private static bool initialized = false;
        private static readonly Lazy<FfiClient> instance = new(() => new FfiClient());
        public static FfiClient Instance => instance.Value;

        internal SynchronizationContext? _context;

        private readonly IObjectPool<FfiResponse> ffiResponsePool;
        private readonly MessageParser<FfiResponse> responseParser;

        public event PublishTrackDelegate PublishTrackReceived;
        public event ConnectReceivedDelegate ConnectReceived;
        public event DisconnectReceivedDelegate DisconnectReceived;
        public event RoomEventReceivedDelegate RoomEventReceived;
        public event TrackEventReceivedDelegate TrackEventReceived;
        // participant events are not allowed in the fii protocol public event ParticipantEventReceivedDelegate ParticipantEventReceived;
        public event VideoStreamEventReceivedDelegate VideoStreamEventReceived;
        public event AudioStreamEventReceivedDelegate AudioStreamEventReceived;

        public FfiClient() : this(Pools.NewFfiResponsePool())
        {
        }

        public FfiClient(IObjectPool<FfiResponse> ffiResponsePool) : this(
            ffiResponsePool,
            new MessageParser<FfiResponse>(ffiResponsePool.Get)
        )
        {
        }

        public FfiClient(
            IObjectPool<FfiResponse> ffiResponsePool,
            MessageParser<FfiResponse> responseParser
        )
        {
            this.responseParser = responseParser;
            this.ffiResponsePool = ffiResponsePool;
        }

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
            Instance.Dispose();
        }

        static void OnAfterAssemblyReload()
        {
            InitializeSdk();
        }
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            Application.quitting += Quit;
            InitializeSdk();
        }
#endif

        private static void Quit()
        {
            #if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            #endif
            Instance.Dispose();
        }

        [RuntimeInitializeOnLoadMethod]
        static void GetMainContext()
        {
            // https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Scripting/UnitySynchronizationContext.cs
            Instance._context = SynchronizationContext.Current;
            Utils.Debug("Main Context created");
        }

        private static void InitializeSdk()
        {
#if NO_LIVEKIT_MODE
            return;
#endif

#if LK_VERBOSE
            const bool captureLogs = true;
#else
            const bool captureLogs = false;
#endif

            NativeMethods.LiveKitInitialize(FFICallback, captureLogs);

            Utils.Debug("FFIServer - Initialized");
            initialized = true;
        }

        public void Initialize()
        {
            InitializeSdk();
        }

        public bool Initialized()
        {
            return initialized;
        }

        public void Dispose()
        {
            // Stop all rooms synchronously
            // The rust lk implementation should also correctly dispose WebRTC
            SendRequest(
                new FfiRequest
                {
                    Dispose = new DisposeRequest()
                }
            );
            Utils.Debug("FFIServer - Disposed");
        }

        public void Release(FfiResponse response)
        {
            ffiResponsePool.Release(response);
        }

        public FfiResponse SendRequest(FfiRequest request)
        {
            try
            {
                unsafe
                {
                    var data = request.ToByteArray()!; //TODO use spans
                    fixed (byte* requestDataPtr = data)
                    {
                        var handle = NativeMethods.FfiNewRequest(
                            requestDataPtr,
                            data.Length,
                            out byte* dataPtr,
                            out int dataLen
                        );

                        var dataSpan = new Span<byte>(dataPtr, dataLen);
                        var response = responseParser.ParseFrom(dataSpan)!;
                        handle.Dispose();
                        return response;
                    }
                }
            }
            catch (Exception e)
            {
                // Since we are in a thread I want to make sure we catch and log
                Utils.Error(e);
                // But we aren't actually handling this exception so we should re-throw here 
                throw new Exception("Cannot send request", e);
            }
        }


        [AOT.MonoPInvokeCallback(typeof(FFICallbackDelegate))]
        static unsafe void FFICallback(IntPtr data, int size)
        {
            var respData = new Span<byte>(data.ToPointer(), size);
            var response = FfiEvent.Parser.ParseFrom(respData);

            Utils.Debug("Callback... " + response.ToString());
            // Run on the main thread, the order of execution is guaranteed by Unity
            // It uses a Queue internally
            if (Instance != null && Instance._context != null)
                Instance._context.Post((resp) =>
                {
                    var response = resp as FfiEvent;
                    if (response.MessageCase != FfiEvent.MessageOneofCase.Logs)
                        Utils.Debug("Callback: " + response.MessageCase);
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