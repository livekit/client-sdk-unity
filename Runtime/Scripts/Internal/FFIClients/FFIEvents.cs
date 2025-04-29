using System;
using System.Runtime.InteropServices;
using LiveKit.Proto;

namespace LiveKit.Internal
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]

    internal delegate void FFICallbackDelegate(UIntPtr data, UIntPtr size);

    // Callbacks
    internal delegate void PublishTrackDelegate(PublishTrackCallback e);


    internal delegate void UnpublishTrackDelegate(UnpublishTrackCallback e);


    internal delegate void ConnectReceivedDelegate(ConnectCallback e);


    internal delegate void DisconnectReceivedDelegate(DisconnectCallback e);

    internal delegate void GetSessionStatsDelegate(GetStatsCallback e);

    internal delegate void SetLocalMetadataReceivedDelegate(SetLocalMetadataCallback e);
    internal delegate void SetLocalNameReceivedDelegate(SetLocalNameCallback e);
    internal delegate void SetLocalAttributesReceivedDelegate(SetLocalAttributesCallback e);

    internal delegate void ByteStreamReaderReadAllReceivedDelegate(ByteStreamReaderReadAllCallback e);

    internal delegate void ByteStreamReaderWriteToFileReceivedDelegate(ByteStreamReaderWriteToFileCallback e);

    internal delegate void ByteStreamOpenReceivedDelegate(ByteStreamOpenCallback e);

    internal delegate void ByteStreamWriterWriteReceivedDelegate(ByteStreamWriterWriteCallback e);

    internal delegate void ByteStreamWriterCloseReceivedDelegate(ByteStreamWriterCloseCallback e);

    internal delegate void SendFileReceivedDelegate(StreamSendFileCallback e);

    internal delegate void TextStreamReaderReadAllReceivedDelegate(TextStreamReaderReadAllCallback e);

    internal delegate void TextStreamOpenReceivedDelegate(TextStreamOpenCallback e);

    internal delegate void TextStreamWriterWriteReceivedDelegate(TextStreamWriterWriteCallback e);

    internal delegate void TextStreamWriterCloseReceivedDelegate(TextStreamWriterCloseCallback e);

    internal delegate void SendTextReceivedDelegate(StreamSendTextCallback e);

    internal delegate void CaptureAudioFrameReceivedDelegate(CaptureAudioFrameCallback e);

    // Events
    internal delegate void RoomEventReceivedDelegate(RoomEvent e);


    internal delegate void TrackEventReceivedDelegate(TrackEvent e);

    internal delegate void RpcMethodInvocationReceivedDelegate(RpcMethodInvocationEvent e);


    internal delegate void VideoStreamEventReceivedDelegate(VideoStreamEvent e);


    internal delegate void AudioStreamEventReceivedDelegate(AudioStreamEvent e);

    internal delegate void PerformRpcReceivedDelegate(PerformRpcCallback e);


    internal delegate void ByteStreamReaderEventReceivedDelegate(ByteStreamReaderEvent e);

    internal delegate void TextStreamReaderEventReceivedDelegate(TextStreamReaderEvent e);
}