using System;
using LiveKit.Proto;
using UnityEngine;
using Google.Protobuf;
using System.Threading;
using LiveKit.Internal.FFIClients;
using LiveKit.Internal.FFIClients.Pools;
using LiveKit.Internal.FFIClients.Pools.Memory;
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

        private static bool _isDisposed = false;

        private readonly IObjectPool<FfiResponse> ffiResponsePool;
        private readonly MessageParser<FfiResponse> responseParser;
        private readonly IMemoryPool memoryPool;

        public event PublishTrackDelegate? PublishTrackReceived;
        public event UnpublishTrackDelegate? UnpublishTrackReceived;
        public event ConnectReceivedDelegate? ConnectReceived;
        public event DisconnectReceivedDelegate? DisconnectReceived;
        public event GetSessionStatsDelegate? GetSessionStatsReceived;
        public event RoomEventReceivedDelegate? RoomEventReceived;
        public event TrackEventReceivedDelegate? TrackEventReceived;
        public event RpcMethodInvocationReceivedDelegate? RpcMethodInvocationReceived;

        // participant events are not allowed in the fii protocol public event ParticipantEventReceivedDelegate ParticipantEventReceived;
        public event VideoStreamEventReceivedDelegate? VideoStreamEventReceived;
        public event AudioStreamEventReceivedDelegate? AudioStreamEventReceived;

        public event PerformRpcReceivedDelegate? PerformRpcReceived;

        public FfiClient() : this(Pools.NewFfiResponsePool(), new ArrayMemoryPool())
        {
        }

        public FfiClient(
            IObjectPool<FfiResponse> ffiResponsePool,
            IMemoryPool memoryPool
        ) : this(
            ffiResponsePool,
            new MessageParser<FfiResponse>(ffiResponsePool.Get), memoryPool)
        {
        }

        public FfiClient(
            IObjectPool<FfiResponse> ffiResponsePool,
            MessageParser<FfiResponse> responseParser,
            IMemoryPool memoryPool
        )
        {
            this.responseParser = responseParser;
            this.memoryPool = memoryPool;
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

            NativeMethods.LiveKitInitialize(FFICallback, captureLogs, "unity", ""); // TODO: Get SDK version

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
#if NO_LIVEKIT_MODE
            return;
#endif

            _isDisposed = true;

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
                    using var memory = memoryPool.Memory(request);
                    var data = memory.Span();
                    request.WriteTo(data);
                    fixed (byte* requestDataPtr = data)
                    {
                        var handle = NativeMethods.FfiNewRequest(
                            requestDataPtr,
                            data.Length,
                            out byte* dataPtr,
                            out UIntPtr dataLen
                        );
                        var dataSpan = new Span<byte>(dataPtr, (int)dataLen.ToUInt64());
                        var response = responseParser.ParseFrom(dataSpan)!;
                        NativeMethods.FfiDropHandle(handle);
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
        static unsafe void FFICallback(UIntPtr data, UIntPtr size)
        {
#if NO_LIVEKIT_MODE
            return;
#endif

            if (_isDisposed) return;

            var respData = new Span<byte>(data.ToPointer()!, (int)size.ToUInt64());
            var response = FfiEvent.Parser!.ParseFrom(respData);

            // Run on the main thread, the order of execution is guaranteed by Unity
            // It uses a Queue internally
            Instance._context?.Post((resp) =>
            {
                var r = resp as FfiEvent;
#if LK_VERBOSE
                if (r?.MessageCase != FfiEvent.MessageOneofCase.Logs)
                    Utils.Debug("Callback: " + r?.MessageCase);
#endif
                switch (r?.MessageCase)
                {
                    case FfiEvent.MessageOneofCase.Logs:
                        break;
                    case FfiEvent.MessageOneofCase.PublishData:
                        break;
                    case FfiEvent.MessageOneofCase.Connect:
                        Instance.ConnectReceived?.Invoke(r.Connect!);
                        break;
                    case FfiEvent.MessageOneofCase.PublishTrack:
                        Instance.PublishTrackReceived?.Invoke(r.PublishTrack!);
                        break;
                    case FfiEvent.MessageOneofCase.UnpublishTrack:
                        Instance.UnpublishTrackReceived?.Invoke(r.UnpublishTrack!);
                        break;
                    case FfiEvent.MessageOneofCase.RoomEvent:
                        Instance.RoomEventReceived?.Invoke(r.RoomEvent);
                        break;
                    case FfiEvent.MessageOneofCase.TrackEvent:
                        Instance.TrackEventReceived?.Invoke(r.TrackEvent!);
                        break;
                    case FfiEvent.MessageOneofCase.RpcMethodInvocation:
                        Instance.RpcMethodInvocationReceived?.Invoke(r.RpcMethodInvocation);
                        break;
                    case FfiEvent.MessageOneofCase.Disconnect:
                        Instance.DisconnectReceived?.Invoke(r.Disconnect!);
                        break;
                    case FfiEvent.MessageOneofCase.GetStats:
                        Instance.GetSessionStatsReceived?.Invoke(r.GetStats);
                        break;
                    case FfiEvent.MessageOneofCase.PublishTranscription:
                        break;
                    case FfiEvent.MessageOneofCase.VideoStreamEvent:
                        Instance.VideoStreamEventReceived?.Invoke(r.VideoStreamEvent!);
                        break;
                    case FfiEvent.MessageOneofCase.AudioStreamEvent:
                        Instance.AudioStreamEventReceived?.Invoke(r.AudioStreamEvent!);
                        break;
                    case FfiEvent.MessageOneofCase.CaptureAudioFrame:
                        break;
                    case FfiEvent.MessageOneofCase.PerformRpc:
                        Instance.PerformRpcReceived?.Invoke(r.PerformRpc!);
                        break;
                    case FfiEvent.MessageOneofCase.Panic:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException($"Unknown message type: {r?.MessageCase.ToString() ?? "null"}");
                }
            }, response);
        }
    }
}