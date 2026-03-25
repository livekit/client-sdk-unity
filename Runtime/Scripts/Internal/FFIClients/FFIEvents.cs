using System;
using System.Runtime.InteropServices;
using LiveKit.Proto;

namespace LiveKit.Internal
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FFICallbackDelegate(IntPtr data, UIntPtr size);

    // Callbacks
    internal delegate void PublishTrackDelegate(PublishTrackCallback e);

    internal delegate void UnpublishTrackDelegate(UnpublishTrackCallback e);

    internal delegate void ConnectReceivedDelegate(ConnectCallback e);


    internal delegate void DisconnectReceivedDelegate(DisconnectCallback e);


    // Events
    internal delegate void RoomEventReceivedDelegate(LiveKit.Proto.RoomEvent e);


    internal delegate void TrackEventReceivedDelegate(LiveKit.Proto.TrackEvent e);


    internal delegate void VideoStreamEventReceivedDelegate(VideoStreamEvent e);


    internal delegate void AudioStreamEventReceivedDelegate(AudioStreamEvent e);

}
