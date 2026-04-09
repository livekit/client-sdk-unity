using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Reflection;
using LiveKit.Proto;

namespace LiveKit.Internal.FFIClients
{
    public static class FfiRequestExtensions
    {
        private static long nextRequestAsyncId;
        private static readonly ConcurrentDictionary<Type, Action<object, ulong>?> requestAsyncIdSetters = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong InitializeRequestAsyncId<T>(this T request)
        {
            // The async request path needs a client-generated request ID before the request
            // crosses the FFI boundary. That allows Unity to register a completion handler first,
            // then send the request, which removes the race where Rust could emit the callback
            // before Unity had subscribed.
            //
            // Historically this method used a large type switch and set RequestAsyncId on each
            // generated protobuf request class explicitly. That is fast, but it tightly couples
            // the code to the exact list of generated request types.
            //
            // This implementation is intentionally based on normal .NET reflection instead of
            // protobuf reflection APIs. The only contract it relies on is:
            //   1. the generated request type has a public instance property named RequestAsyncId
            //   2. that property is writable
            //   3. that property has type ulong
            //
            // Because of that, the code will keep working even if the protobuf runtime changes
            // (for example, switching to a lighter runtime such as "protolite"), as long as the
            // generated C# surface still exposes the same property shape.
            //
            // The expensive part here is reflection lookup, not assigning one ulong. We therefore
            // cache a setter delegate per concrete request type. The first request of a given type
            // pays the reflection cost; subsequent requests reuse the cached delegate.
            //
            // The cached delegate below still calls PropertyInfo.SetValue internally. That is not
            // as fast as a fully compiled expression setter, but it is simpler and safer for Unity
            // runtimes, especially IL2CPP / AOT targets where expression compilation can be less
            // predictable. In practice, this cost is tiny compared with protobuf serialization,
            // the native FFI call, and response parsing.
            if (request == null)
            {
                return 0;
            }

            var setter = requestAsyncIdSetters.GetOrAdd(request.GetType(), static type =>
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
                var property = type.GetProperty("RequestAsyncId", flags);

                if (property == null || !property.CanWrite || property.PropertyType != typeof(ulong))
                {
                    return null;
                }

                return (target, value) => property.SetValue(target, value);
            });

            if (setter == null)
            {
                return 0;
            }

            var requestAsyncId = (ulong)Interlocked.Increment(ref nextRequestAsyncId);
            setter(request, requestAsyncId);
            return requestAsyncId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Inject<T>(this FfiRequest ffiRequest, T request)
        {
            switch (request)
            {
                case DisposeRequest disposeRequest:
                    ffiRequest.Dispose = disposeRequest;
                    break;
                // Room
                case ConnectRequest connectRequest:
                    ffiRequest.Connect = connectRequest;
                    break;
                case DisconnectRequest disconnectRequest:
                    ffiRequest.Disconnect = disconnectRequest;
                    break;
                case PublishTrackRequest publishTrackRequest:
                    ffiRequest.PublishTrack = publishTrackRequest;
                    break;
                case UnpublishTrackRequest unpublishTrackRequest:
                    ffiRequest.UnpublishTrack = unpublishTrackRequest;
                    break;
                case PublishDataRequest publishDataRequest:
                    ffiRequest.PublishData = publishDataRequest;
                    break;
                case SetSubscribedRequest setSubscribedRequest:
                    ffiRequest.SetSubscribed = setSubscribedRequest;
                    break;
                case SetLocalMetadataRequest updateLocalMetadataRequest:
                    ffiRequest.SetLocalMetadata = updateLocalMetadataRequest;
                    break;
                case SetLocalNameRequest updateLocalNameRequest:
                    ffiRequest.SetLocalName = updateLocalNameRequest;
                    break;
                case SetLocalAttributesRequest setLocalAttributesRequest:
                    ffiRequest.SetLocalAttributes = setLocalAttributesRequest;
                    break;
                case GetSessionStatsRequest getSessionStatsRequest:
                    ffiRequest.GetSessionStats = getSessionStatsRequest;
                    break;
                // Track
                case CreateVideoTrackRequest createVideoTrackRequest:
                    ffiRequest.CreateVideoTrack = createVideoTrackRequest;
                    break;
                case CreateAudioTrackRequest createAudioTrackRequest:
                    ffiRequest.CreateAudioTrack = createAudioTrackRequest;
                    break;
                case GetStatsRequest getStatsRequest:
                    ffiRequest.GetStats = getStatsRequest;
                    break;
                // Video
                case NewVideoStreamRequest newVideoStreamRequest:
                    ffiRequest.NewVideoStream = newVideoStreamRequest;
                    break;
                case NewVideoSourceRequest newVideoSourceRequest:
                    ffiRequest.NewVideoSource = newVideoSourceRequest;
                    break;
                case CaptureVideoFrameRequest captureVideoFrameRequest:
                    ffiRequest.CaptureVideoFrame = captureVideoFrameRequest;
                    break;
                case VideoConvertRequest videoConvertRequest:
                    ffiRequest.VideoConvert = videoConvertRequest;
                    break;
                // Audio
                case NewAudioStreamRequest newAudioStreamRequest:
                    ffiRequest.NewAudioStream = newAudioStreamRequest;
                    break;
                case NewAudioSourceRequest newAudioSourceRequest:
                    ffiRequest.NewAudioSource = newAudioSourceRequest;
                    break;
                case CaptureAudioFrameRequest captureAudioFrameRequest:
                    ffiRequest.CaptureAudioFrame = captureAudioFrameRequest;
                    break;
                case NewAudioResamplerRequest newAudioResamplerRequest:
                    ffiRequest.NewAudioResampler = newAudioResamplerRequest;
                    break;
                case RemixAndResampleRequest remixAndResampleRequest:
                    ffiRequest.RemixAndResample = remixAndResampleRequest;
                    break;
                case LocalTrackMuteRequest localTrackMuteRequest:
                    ffiRequest.LocalTrackMute = localTrackMuteRequest;
                    break;
                case E2eeRequest e2EeRequest:
                    ffiRequest.E2Ee = e2EeRequest;
                    break;
                // Rpc
                case RegisterRpcMethodRequest registerRpcMethodRequest:
                    ffiRequest.RegisterRpcMethod = registerRpcMethodRequest;
                    break;
                case UnregisterRpcMethodRequest unregisterRpcMethodRequest:
                    ffiRequest.UnregisterRpcMethod = unregisterRpcMethodRequest;
                    break;
                case PerformRpcRequest performRpcRequest:
                    ffiRequest.PerformRpc = performRpcRequest;
                    break;
                case RpcMethodInvocationResponseRequest rpcMethodInvocationResponseRequest:
                    ffiRequest.RpcMethodInvocationResponse = rpcMethodInvocationResponseRequest;
                    break;
                // Data stream
                case TextStreamReaderReadIncrementalRequest textStreamReaderReadIncrementalRequest:
                    ffiRequest.TextReadIncremental = textStreamReaderReadIncrementalRequest;
                    break;
                case TextStreamReaderReadAllRequest textStreamReaderReadAllRequest:
                    ffiRequest.TextReadAll = textStreamReaderReadAllRequest;
                    break;
                case ByteStreamReaderReadIncrementalRequest byteStreamReaderReadIncrementalRequest:
                    ffiRequest.ByteReadIncremental = byteStreamReaderReadIncrementalRequest;
                    break;
                case ByteStreamReaderReadAllRequest byteStreamReaderReadAllRequest:
                    ffiRequest.ByteReadAll = byteStreamReaderReadAllRequest;
                    break;
                case ByteStreamReaderWriteToFileRequest byteStreamReaderWriteToFileRequest:
                    ffiRequest.ByteWriteToFile = byteStreamReaderWriteToFileRequest;
                    break;
                case StreamSendFileRequest streamSendFileRequest:
                    ffiRequest.SendFile = streamSendFileRequest;
                    break;
                case StreamSendTextRequest streamSendTextRequest:
                    ffiRequest.SendText = streamSendTextRequest;
                    break;
                case ByteStreamOpenRequest byteStreamOpenRequest:
                    ffiRequest.ByteStreamOpen = byteStreamOpenRequest;
                    break;
                case ByteStreamWriterWriteRequest byteStreamWriterWriteRequest:
                    ffiRequest.ByteStreamWrite = byteStreamWriterWriteRequest;
                    break;
                case ByteStreamWriterCloseRequest byteStreamWriterCloseRequest:
                    ffiRequest.ByteStreamClose = byteStreamWriterCloseRequest;
                    break;
                case TextStreamOpenRequest textStreamOpenRequest:
                    ffiRequest.TextStreamOpen = textStreamOpenRequest;
                    break;
                case TextStreamWriterWriteRequest textStreamWriterWriteRequest:
                    ffiRequest.TextStreamWrite = textStreamWriterWriteRequest;
                    break;
                case TextStreamWriterCloseRequest textStreamWriterCloseRequest:
                    ffiRequest.TextStreamClose = textStreamWriterCloseRequest;
                    break;
                case SetRemoteTrackPublicationQualityRequest setRemoteTrackPublicationQualityRequest:
                    ffiRequest.SetRemoteTrackPublicationQuality = setRemoteTrackPublicationQualityRequest;
                    break;
                // Data Track
                case PublishDataTrackRequest publishDataTrackRequest:
                    ffiRequest.PublishDataTrack = publishDataTrackRequest;
                    break;
                case LocalDataTrackTryPushRequest localDataTrackTryPushRequest:
                    ffiRequest.LocalDataTrackTryPush = localDataTrackTryPushRequest;
                    break;
                case LocalDataTrackUnpublishRequest localDataTrackUnpublishRequest:
                    ffiRequest.LocalDataTrackUnpublish = localDataTrackUnpublishRequest;
                    break;
                case LocalDataTrackIsPublishedRequest localDataTrackIsPublishedRequest:
                    ffiRequest.LocalDataTrackIsPublished = localDataTrackIsPublishedRequest;
                    break;
                case SubscribeDataTrackRequest subscribeDataTrackRequest:
                    ffiRequest.SubscribeDataTrack = subscribeDataTrackRequest;
                    break;
                case RemoteDataTrackIsPublishedRequest remoteDataTrackIsPublishedRequest:
                    ffiRequest.RemoteDataTrackIsPublished = remoteDataTrackIsPublishedRequest;
                    break;
                case DataTrackStreamReadRequest dataTrackStreamReadRequest:
                    ffiRequest.DataTrackStreamRead = dataTrackStreamReadRequest;
                    break;
                default:
                    throw new Exception($"Unknown request type: {request?.GetType().FullName ?? "null"}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureClean(this FfiResponse response)
        {
            if (response.MessageCase != FfiResponse.MessageOneofCase.None)
                throw new InvalidOperationException($"Response is not cleared: {response.MessageCase}");
        }
    }
}
