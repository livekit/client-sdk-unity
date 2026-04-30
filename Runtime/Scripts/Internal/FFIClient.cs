using System;
using System.Collections.Concurrent;
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
        // One-shot async request completions keyed by the client-generated request_async_id.
        //
        // Thread-safety / race model:
        // - registration happens before the request is sent to Rust
        // - completion removes the pending entry exactly once
        // - cancellation (send failure / dispose) also removes the same entry exactly once
        // - the side that wins TryRemove is the only side allowed to invoke user completion
        //
        // Wire contract:
        // - Unity writes the generated ID into request.RequestAsyncId
        // - Rust echoes that same numeric value back through callback.AsyncId
        // - the pending map stays keyed by the original request ID, and callback dispatch
        //   extracts AsyncId from the completion event for lookup
        //
        // ConcurrentDictionary is sufficient here because we only need atomic add/remove
        // semantics per request ID; there is no requirement for cross-entry transactions.
        private readonly ConcurrentDictionary<ulong, PendingCallbackBase> pendingCallbacks = new();

        public event DisconnectReceivedDelegate? DisconnectReceived;
        public event RoomEventReceivedDelegate? RoomEventReceived;
        public event TrackEventReceivedDelegate? TrackEventReceived;
        public event RpcMethodInvocationReceivedDelegate? RpcMethodInvocationReceived;

        // participant events are not allowed in the fii protocol public event ParticipantEventReceivedDelegate ParticipantEventReceived;
        public event VideoStreamEventReceivedDelegate? VideoStreamEventReceived;
        public event AudioStreamEventReceivedDelegate? AudioStreamEventReceived;

        public event ByteStreamReaderEventReceivedDelegate? ByteStreamReaderEventReceived;
        public event TextStreamReaderEventReceivedDelegate? TextStreamReaderEventReceived;

        // Data Track
        public event DataTrackStreamEventReceivedDelegate? DataTrackStreamEventReceived;

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

            var sdkVersion = PackageVersion.Get();
            NativeMethods.LiveKitInitialize(FFICallback, captureLogs, "unity", sdkVersion);

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
            ClearPendingCallbacks();
            Utils.Debug("FFIServer - Disposed");
        }

        internal void RegisterPendingCallback<TCallback>(
            ulong requestAsyncId,
            Func<FfiEvent, TCallback?> selector,
            Action<TCallback> onComplete,
            Action? onCancel = null,
            bool rawSafe = false
        ) where TCallback : class
        {
            // Request registration must happen before the request is sent. That ordering is what
            // removes the original race: Rust can no longer produce the callback before Unity has
            // somewhere to store it.
            //
            // The request is registered under request.RequestAsyncId. The eventual callback comes
            // back with callback.AsyncId carrying the same value.
            //
            // Duplicate IDs are treated as a hard error because they would allow two unrelated
            // requests to compete for the same completion slot.
            //
            // rawSafe == true means the onComplete is safe to invoke directly on the FFI callback
            // thread (no Unity APIs touched, only volatile state mutations). The dispatcher will
            // then bypass the main-thread SynchronizationContext post.
            var pending = new PendingCallback<TCallback>(selector, onComplete, onCancel, rawSafe);
            if (!pendingCallbacks.TryAdd(requestAsyncId, pending))
            {
                throw new InvalidOperationException($"Duplicate pending callback for request_async_id={requestAsyncId}");
            }
        }

        internal bool CancelPendingCallback(ulong requestAsyncId)
        {
            // Cancellation is intentionally symmetric with completion: both paths try to remove the
            // same entry. Only one side can win, which prevents double-complete / double-cancel.
            if (pendingCallbacks.TryRemove(requestAsyncId, out var pending))
            {
                pending.Cancel();
                return true;
            }

            return false;
        }

        private void ClearPendingCallbacks()
        {
            // Snapshot-style iteration over Keys is acceptable here. Dispose marks the client as
            // disposed first, so no new native callbacks will be enqueued, and each cancellation
            // still re-validates ownership with TryRemove before running OnCanceled.
            foreach (var requestAsyncId in pendingCallbacks.Keys)
            {
                CancelPendingCallback(requestAsyncId);
            }
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
            RouteFfiEvent(response);
        }

        // Routing logic split out from FFICallback so tests can drive it from a
        // chosen thread without going through the P/Invoke entry point. Running
        // production traffic still always lands here via FFICallback above.
        internal static void RouteFfiEvent(FfiEvent response)
        {
            if (_isDisposed) return;

            // Audio stream events are handled directly on the FFI callback thread
            // to bypass the main thread, since the audio thread consumes the data
            if (response.MessageCase == FfiEvent.MessageOneofCase.AudioStreamEvent)
            {
                Instance.AudioStreamEventReceived?.Invoke(response.AudioStreamEvent!);
                return;
            }

            // Log batches are forwarded directly. UnityEngine.Debug.unityLogger is
            // documented thread-safe; Unity's logger queues to its console drain
            // internally. Skipping the main-thread post means logs reach the
            // console without a one-frame delay — useful during error storms,
            // panics, or LK_VERBOSE noise where the post queue could otherwise
            // back up.
            if (response.MessageCase == FfiEvent.MessageOneofCase.Logs)
            {
                Utils.HandleLogBatch(response.Logs);
                return;
            }

            // Byte stream reader events feed an internal incremental-read buffer that
            // already serializes mutations under its own lock. Skipping the main-thread
            // post lets chunks land in the buffer immediately rather than waiting for
            // the next frame drain.
            if (response.MessageCase == FfiEvent.MessageOneofCase.ByteStreamReaderEvent)
            {
                Instance.ByteStreamReaderEventReceived?.Invoke(response.ByteStreamReaderEvent!);
                return;
            }

            // Same treatment for text stream readers — they share
            // ReadIncrementalInstructionBase<TContent> with the byte path, so the
            // lock added there already protects all state mutations.
            if (response.MessageCase == FfiEvent.MessageOneofCase.TextStreamReaderEvent)
            {
                Instance.TextStreamReaderEventReceived?.Invoke(response.TextStreamReaderEvent!);
                return;
            }

            // Raw-safe one-shot completions also bypass the main thread. The pending
            // callback's onComplete only mutates volatile YieldInstruction fields, so
            // resolving it here saves up to one frame of latency on async ops like
            // SetMetadata / UnpublishTrack / stream Read/Write/Close.
            var requestAsyncId = ExtractRequestAsyncId(response);
            if (requestAsyncId.HasValue && Instance.TryDispatchRawSafe(requestAsyncId.Value, response))
            {
                return;
            }

            // Run on the main thread, the order of execution is guaranteed by Unity
            // It uses a Queue internally
            Instance._context?.Post(static (resp) =>
            {
                var r = resp as FfiEvent;
                if (r == null)
                {
                    return;
                }
                DispatchEvent(r);
            }, response);
        }

        private static void DispatchEvent(FfiEvent ffiEvent)
        {
#if LK_VERBOSE
            if (ffiEvent.MessageCase != FfiEvent.MessageOneofCase.Logs)
                Utils.Debug("Callback: " + ffiEvent.MessageCase);
#endif
            var requestAsyncId = ExtractRequestAsyncId(ffiEvent);
            if (requestAsyncId.HasValue && Instance.TryDispatchPendingCallback(requestAsyncId.Value, ffiEvent))
            {
                // Async request/response callbacks are one-shot. Once matched, they should not
                // also flow through the general event switch below.
                return;
            }

            switch (ffiEvent.MessageCase)
            {
                case FfiEvent.MessageOneofCase.PublishData:
                    break;
                case FfiEvent.MessageOneofCase.RoomEvent:
                    Instance.RoomEventReceived?.Invoke(ffiEvent.RoomEvent);
                    break;
                case FfiEvent.MessageOneofCase.TrackEvent:
                    Instance.TrackEventReceived?.Invoke(ffiEvent.TrackEvent!);
                    break;
                case FfiEvent.MessageOneofCase.RpcMethodInvocation:
                    Instance.RpcMethodInvocationReceived?.Invoke(ffiEvent.RpcMethodInvocation);
                    break;
                case FfiEvent.MessageOneofCase.Disconnect:
                    Instance.DisconnectReceived?.Invoke(ffiEvent.Disconnect!);
                    break;
                case FfiEvent.MessageOneofCase.PublishTranscription:
                    break;
                case FfiEvent.MessageOneofCase.VideoStreamEvent:
                    Instance.VideoStreamEventReceived?.Invoke(ffiEvent.VideoStreamEvent!);
                    break;
                // Logs, AudioStreamEvent, ByteStreamReaderEvent, and TextStreamReaderEvent
                // are dispatched directly on the FFI callback thread by RouteFfiEvent and
                // never reach this switch — see the fast-path early-returns there.
                case FfiEvent.MessageOneofCase.DataTrackStreamEvent:
                    Instance.DataTrackStreamEventReceived?.Invoke(ffiEvent.DataTrackStreamEvent!);
                    break;
                case FfiEvent.MessageOneofCase.Panic:
                    break;
                default:
                    break;
            }
        }

        internal bool TryDispatchPendingCallback(ulong requestAsyncId, FfiEvent ffiEvent)
        {
            // Remove-first dispatch is the key race-proofing step.
            //
            // If cancellation wins first, TryRemove here fails and the callback is ignored.
            // If completion wins first, cancellation later sees no entry and becomes a no-op.
            // That guarantees at-most-once completion for each request_async_id.
            if (!pendingCallbacks.TryRemove(requestAsyncId, out var pending))
            {
                return false;
            }

            if (pending.TryComplete(ffiEvent))
            {
                return true;
            }

            // This branch is defensive. In normal operation ExtractRequestAsyncId and the caller's
            // selector should agree on the callback type, so TryComplete should succeed. If they do
            // not, we reinsert the entry rather than losing it.
            if (!pendingCallbacks.TryAdd(requestAsyncId, pending))
            {
                pending.Cancel();
            }

            return false;
        }

        // FFI-thread fast path for one-shot completions whose onComplete only mutates
        // volatile YieldInstruction state (no Unity APIs, no user-event invocations).
        // Same race model as TryDispatchPendingCallback: the side that wins TryRemove
        // is the only side that may invoke completion. If the entry isn't raw-safe we
        // return false and let the caller fall through to the main-thread post.
        internal bool TryDispatchRawSafe(ulong requestAsyncId, FfiEvent ffiEvent)
        {
            if (!pendingCallbacks.TryGetValue(requestAsyncId, out var pending))
            {
                return false;
            }
            if (!pending.RawSafe)
            {
                return false;
            }
            return TryDispatchPendingCallback(requestAsyncId, ffiEvent);
        }

        private static ulong? ExtractRequestAsyncId(FfiEvent ffiEvent)
        {
            // This switch is only concerned with one-shot async completion callbacks that echo
            // request.RequestAsyncId back through callback.AsyncId. Streaming/incremental events
            // such as RoomEvent or TextStreamReaderEvent are intentionally excluded because they
            // are not modeled as pending one-shot completions.
            return ffiEvent.MessageCase switch
            {
                FfiEvent.MessageOneofCase.Connect => ffiEvent.Connect?.AsyncId,
                FfiEvent.MessageOneofCase.PublishTrack => ffiEvent.PublishTrack?.AsyncId,
                FfiEvent.MessageOneofCase.UnpublishTrack => ffiEvent.UnpublishTrack?.AsyncId,
                FfiEvent.MessageOneofCase.SetLocalName => ffiEvent.SetLocalName?.AsyncId,
                FfiEvent.MessageOneofCase.SetLocalMetadata => ffiEvent.SetLocalMetadata?.AsyncId,
                FfiEvent.MessageOneofCase.SetLocalAttributes => ffiEvent.SetLocalAttributes?.AsyncId,
                FfiEvent.MessageOneofCase.GetStats => ffiEvent.GetStats?.AsyncId,
                FfiEvent.MessageOneofCase.CaptureAudioFrame => ffiEvent.CaptureAudioFrame?.AsyncId,
                FfiEvent.MessageOneofCase.PerformRpc => ffiEvent.PerformRpc?.AsyncId,
                FfiEvent.MessageOneofCase.ByteStreamReaderReadAll => ffiEvent.ByteStreamReaderReadAll?.AsyncId,
                FfiEvent.MessageOneofCase.ByteStreamReaderWriteToFile => ffiEvent.ByteStreamReaderWriteToFile?.AsyncId,
                FfiEvent.MessageOneofCase.ByteStreamOpen => ffiEvent.ByteStreamOpen?.AsyncId,
                FfiEvent.MessageOneofCase.ByteStreamWriterWrite => ffiEvent.ByteStreamWriterWrite?.AsyncId,
                FfiEvent.MessageOneofCase.ByteStreamWriterClose => ffiEvent.ByteStreamWriterClose?.AsyncId,
                FfiEvent.MessageOneofCase.SendFile => ffiEvent.SendFile?.AsyncId,
                FfiEvent.MessageOneofCase.TextStreamReaderReadAll => ffiEvent.TextStreamReaderReadAll?.AsyncId,
                FfiEvent.MessageOneofCase.TextStreamOpen => ffiEvent.TextStreamOpen?.AsyncId,
                FfiEvent.MessageOneofCase.TextStreamWriterWrite => ffiEvent.TextStreamWriterWrite?.AsyncId,
                FfiEvent.MessageOneofCase.TextStreamWriterClose => ffiEvent.TextStreamWriterClose?.AsyncId,
                FfiEvent.MessageOneofCase.SendText => ffiEvent.SendText?.AsyncId,
                FfiEvent.MessageOneofCase.PublishDataTrack => ffiEvent.PublishDataTrack?.AsyncId,
                _ => null,
            };
        }

        private abstract class PendingCallbackBase
        {
            public abstract bool RawSafe { get; }
            public abstract bool TryComplete(FfiEvent ffiEvent);
            public abstract void Cancel();
        }

        private sealed class PendingCallback<TCallback> : PendingCallbackBase where TCallback : class
        {
            private readonly Func<FfiEvent, TCallback?> selector;
            private readonly Action<TCallback> onComplete;
            private readonly Action? onCancel;
            private readonly bool rawSafe;

            public override bool RawSafe => rawSafe;

            public PendingCallback(
                Func<FfiEvent, TCallback?> selector,
                Action<TCallback> onComplete,
                Action? onCancel,
                bool rawSafe
            )
            {
                this.selector = selector;
                this.onComplete = onComplete;
                this.onCancel = onCancel;
                this.rawSafe = rawSafe;
            }

            public override bool TryComplete(FfiEvent ffiEvent)
            {
                var callback = selector(ffiEvent);
                if (callback == null)
                {
                    return false;
                }

                // onComplete executes on Unity's main-thread SynchronizationContext because
                // FFICallback posts the FfiEvent before dispatch. That keeps completion behavior
                // aligned with the pre-refactor implementation.
                onComplete(callback);
                return true;
            }

            public override void Cancel()
            {
                onCancel?.Invoke();
            }
        }
    }
}
