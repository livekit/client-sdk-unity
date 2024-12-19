using System;
using System.Runtime.CompilerServices;
using LiveKit.Proto;

namespace LiveKit.Internal.FFIClients
{
    public static class FfiRequestExtensions
    {
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
                case NewAudioStreamRequest wewAudioStreamRequest:
                    ffiRequest.NewAudioStream = wewAudioStreamRequest;
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
                default:
                    throw new Exception($"Unknown request type: {request?.GetType().FullName ?? "null"}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureClean(this FfiRequest request)
        {
            // list of messages is taken from: livekit-ffi/protocol/ffi.proto
            // https://github.com/livekit/rust-sdks/blob/cf34856e78892a639c4d3c1d6a27e9aba0a4a8ff/livekit-ffi/protocol/ffi.proto#L4

            if (
                request.Dispose != null
                ||

                // Room
                request.Connect != null
                || request.Disconnect != null
                || request.PublishTrack != null
                || request.UnpublishTrack != null
                || request.PublishData != null
                || request.SetSubscribed != null
                || request.SetLocalMetadata != null
                || request.SetLocalName != null
                || request.SetLocalAttributes != null
                || request.GetSessionStats != null
                ||

                // Track
                request.CreateVideoTrack != null
                || request.CreateAudioTrack != null
                || request.GetStats != null
                ||

                // Video
                request.NewVideoStream != null
                || request.NewVideoSource != null
                || request.CaptureVideoFrame != null
                || request.VideoConvert != null
                ||

                // Audio
                request.NewAudioStream != null
                || request.NewAudioSource != null
                || request.CaptureAudioFrame != null
                || request.NewAudioResampler != null
                || request.RemixAndResample != null
                || request.E2Ee != null
                ||

                // Rpc
                request.RegisterRpcMethod != null
                || request.UnregisterRpcMethod != null
                || request.PerformRpc != null
                || request.RpcMethodInvocationResponse != null
            )
            {
                throw new InvalidOperationException("Request is not cleared");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureClean(this FfiResponse response)
        {
            // list of messages is taken from: livekit-ffi/protocol/ffi.proto
            // https://github.com/livekit/rust-sdks/blob/cf34856e78892a639c4d3c1d6a27e9aba0a4a8ff/livekit-ffi/protocol/ffi.proto#L4

            if (
                response.Dispose != null
                ||

                // Room
                response.Connect != null
                || response.Disconnect != null
                || response.PublishTrack != null
                || response.UnpublishTrack != null
                || response.PublishData != null
                || response.SetSubscribed != null
                || response.SetLocalMetadata != null
                || response.SetLocalName != null
                || response.SetLocalAttributes != null
                || response.GetSessionStats != null
                ||

                // Track
                response.CreateVideoTrack != null
                || response.CreateAudioTrack != null
                || response.GetStats != null
                ||

                // Video
                response.NewVideoStream != null
                || response.NewVideoSource != null
                || response.CaptureVideoFrame != null
                || response.VideoConvert != null
                ||

                // Audio
                response.NewAudioStream != null
                || response.NewAudioSource != null
                || response.CaptureAudioFrame != null
                || response.NewAudioResampler != null
                || response.RemixAndResample != null
                || response.E2Ee != null
                ||

                // Rpc
                response.RegisterRpcMethod != null
                || response.UnregisterRpcMethod != null
                || response.PerformRpc != null
                || response.RpcMethodInvocationResponse != null
            )
            {
                throw new InvalidOperationException("Response is not cleared: ");
            }
        }
    }
}