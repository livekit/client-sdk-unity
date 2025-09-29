using System;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;
using LiveKit.Rooms.Participants;
using LiveKit.Rooms.Streaming;
using LiveKit.Rooms.Tracks;
using UnityEngine;

namespace LiveKit.Rooms.VideoStreaming
{
    public class VideoStreams : Streams<IVideoStream, VideoStreamInfo>, IVideoStreams
    {
        private readonly VideoBufferType bufferType;
        private readonly TextureFormat textureFormat;

        public VideoStreams(IParticipantsHub participantsHub, VideoBufferType bufferType = VideoBufferType.Bgra) : base(
            participantsHub, TrackKind.KindVideo
        )
        {
            this.bufferType = bufferType;
            textureFormat = FormatFromBufferType(bufferType);
        }

        private static TextureFormat FormatFromBufferType(VideoBufferType videoBufferType) =>
            videoBufferType switch
            {
                VideoBufferType.Bgra => TextureFormat.BGRA32,
                VideoBufferType.Rgba => TextureFormat.RGBA32,
                _ => throw new Exception($"Format conversion for {videoBufferType} is not supported")
            };

        protected override IVideoStream NewStreamInstance(StreamKey streamKey, ITrack track)
        {
            using var request = FFIBridge.Instance.NewRequest<NewVideoStreamRequest>();
            var newVideoStream = request.request;
            newVideoStream.TrackHandle = (ulong)track.Handle!.DangerousGetHandle();
            newVideoStream.Format = bufferType;
            newVideoStream.NormalizeStride = true;
            newVideoStream.Type = VideoStreamType.VideoStreamNative;
            using var response = request.Send();
            FfiResponse res = response;

            var streamInfo = res.NewVideoStream!.Stream;
            return new VideoStream(streamInfo!, textureFormat);
        }

        protected override VideoStreamInfo InfoFromStream(IVideoStream stream)
        {
            return new VideoStreamInfo();
        }
    }
}