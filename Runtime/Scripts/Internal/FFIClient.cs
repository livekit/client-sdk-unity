using System;
using AOT;
using LiveKit.Proto;
using UnityEngine;
using Google.Protobuf;
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

        public static FfiClient Instance => instance.Value!;
        private static bool isDisposed;

        private readonly IObjectPool<FfiResponse> ffiResponsePool;
        private readonly MessageParser<FfiResponse> responseParser;
        private readonly IMemoryPool memoryPool;

        public event PublishTrackDelegate? PublishTrackReceived;
        public event UnpublishTrackDelegate? UnpublishTrackReceived;
        public event ConnectReceivedDelegate? ConnectReceived;
        public event DisconnectReceivedDelegate? DisconnectReceived;
        public event RoomEventReceivedDelegate? RoomEventReceived;
        public event TrackEventReceivedDelegate? TrackEventReceived;
        // participant events are not allowed in the fii protocol public event ParticipantEventReceivedDelegate ParticipantEventReceived;
        public event VideoStreamEventReceivedDelegate? VideoStreamEventReceived;
        public event AudioStreamEventReceivedDelegate? AudioStreamEventReceived;

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
            #if NO_LIVEKIT_MODE
            return;
            #endif
            #if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            #endif
            Instance.Dispose();
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

            try
            {
                NativeMethods.LiveKitInitialize(FFICallback, captureLogs);
            }
            catch (DllNotFoundException) {}

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

            if (isDisposed)
            {
                Utils.Debug("FFIServer - Already Disposed");
                return;
            }
            
            isDisposed = true;

            // Stop all rooms synchronously
            // The rust lk implementation should also correctly dispose WebRTC
            initialized = false;
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
            #if NO_LIVEKIT_MODE
            return new FfiResponse();
            #endif
            try
            {
                unsafe
                {
                    using var memory = memoryPool.Memory(request);
                    var data = memory.Span();
                    request.WriteTo(data);

                    var dataInLen = new UIntPtr((ulong)data.Length);

                    fixed (byte* requestDataPtr = data)
                    {
                        var handle = NativeMethods.FfiNewRequest(
                            requestDataPtr,
                            dataInLen,
                            out byte* dataPtr,
                            out var dataLen
                        );

                        var dataSpan = new Span<byte>(dataPtr, (int)dataLen.ToUInt32());
                        var response = responseParser.ParseFrom(dataSpan)!;
                        NativeMethods.FfiDropHandle(handle);
                        
                        #if LK_VERBOSE
                        Debug.Log($"FFIClient response of type: {response.MessageCase} with asyncId: {AsyncId(response)}");
                        #endif
                            
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

        [MonoPInvokeCallback(typeof(FFICallbackDelegate))]
        private static unsafe void FFICallback(IntPtr data, UIntPtr size)
        {
            #if NO_LIVEKIT_MODE
            return;
            #endif

            if (isDisposed) return;
            var respData = new Span<byte>(data.ToPointer()!, (int)size.ToUInt32());
            var response = FfiEvent.Parser!.ParseFrom(respData);
            
            #if LK_VERBOSE
            if (response?.MessageCase != FfiEvent.MessageOneofCase.Logs)
                Utils.Debug("Callback: " + response?.MessageCase);
            #endif
            switch (response?.MessageCase)
            {
                case FfiEvent.MessageOneofCase.Logs:

                    Debug.Log($"LK_DEBUG: {response.Logs.Records}");
                    break;
                case FfiEvent.MessageOneofCase.PublishData:
                    break;
                case FfiEvent.MessageOneofCase.Connect:
                    Instance.ConnectReceived?.Invoke(response.Connect!);
                    break;
                case FfiEvent.MessageOneofCase.PublishTrack:
                    Instance.PublishTrackReceived?.Invoke(response.PublishTrack!);
                    break;
                case FfiEvent.MessageOneofCase.UnpublishTrack:
                    Instance.UnpublishTrackReceived?.Invoke(response.UnpublishTrack!);
                    break;
                case FfiEvent.MessageOneofCase.RoomEvent:
                    Instance.RoomEventReceived?.Invoke(response.RoomEvent);
                    break;
                case FfiEvent.MessageOneofCase.TrackEvent:
                    Instance.TrackEventReceived?.Invoke(response.TrackEvent!);
                    break;
                case FfiEvent.MessageOneofCase.Disconnect:
                    Instance.DisconnectReceived?.Invoke(response.Disconnect!);
                    break;
                case FfiEvent.MessageOneofCase.PublishTranscription:
                    break;
                case FfiEvent.MessageOneofCase.VideoStreamEvent:
                    Instance.VideoStreamEventReceived?.Invoke(response.VideoStreamEvent!);
                    break;
                case FfiEvent.MessageOneofCase.AudioStreamEvent:
                    Instance.AudioStreamEventReceived?.Invoke(response.AudioStreamEvent!);
                    break;
                case FfiEvent.MessageOneofCase.CaptureAudioFrame:
                    break;
                case FfiEvent.MessageOneofCase.GetStats:
                    break;
                case FfiEvent.MessageOneofCase.Panic:
                    Debug.LogError($"Panic received from FFI: {response.Panic?.Message}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        $"Unknown message type: {response?.MessageCase.ToString() ?? "null"}");
            }
        }

        private static ulong AsyncId(FfiResponse response)
        {
            return response.MessageCase switch
            {
                FfiResponse.MessageOneofCase.None => 0,
                FfiResponse.MessageOneofCase.Dispose => response.Dispose!.AsyncId,
                FfiResponse.MessageOneofCase.Connect => response.Connect!.AsyncId,
                FfiResponse.MessageOneofCase.Disconnect => response.Disconnect!.AsyncId,
                FfiResponse.MessageOneofCase.PublishTrack => response.PublishTrack!.AsyncId,
                FfiResponse.MessageOneofCase.UnpublishTrack => response.UnpublishTrack!.AsyncId,
                FfiResponse.MessageOneofCase.PublishData => response.PublishData!.AsyncId,
                FfiResponse.MessageOneofCase.SetSubscribed => 0,
                FfiResponse.MessageOneofCase.SetLocalMetadata => response.SetLocalMetadata!.AsyncId,
                FfiResponse.MessageOneofCase.SetLocalName => response.SetLocalName!.AsyncId,
                FfiResponse.MessageOneofCase.SetLocalAttributes => response.SetLocalAttributes!.AsyncId,
                FfiResponse.MessageOneofCase.GetSessionStats => response.GetSessionStats!.AsyncId,
                FfiResponse.MessageOneofCase.PublishTranscription => response.PublishTranscription!.AsyncId,
                FfiResponse.MessageOneofCase.PublishSipDtmf => response.PublishSipDtmf!.AsyncId,
                FfiResponse.MessageOneofCase.CreateVideoTrack => 0,
                FfiResponse.MessageOneofCase.CreateAudioTrack => 0,
                FfiResponse.MessageOneofCase.GetStats => response.GetStats!.AsyncId,
                FfiResponse.MessageOneofCase.NewVideoStream => 0,
                FfiResponse.MessageOneofCase.NewVideoSource => 0,
                FfiResponse.MessageOneofCase.CaptureVideoFrame => 0,
                FfiResponse.MessageOneofCase.VideoConvert => 0,
                FfiResponse.MessageOneofCase.NewAudioStream => 0,
                FfiResponse.MessageOneofCase.NewAudioSource => 0,
                FfiResponse.MessageOneofCase.CaptureAudioFrame => response.CaptureAudioFrame!.AsyncId,
                FfiResponse.MessageOneofCase.NewAudioResampler => 0,
                FfiResponse.MessageOneofCase.RemixAndResample => 0,
                FfiResponse.MessageOneofCase.E2Ee => 0,
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }
}