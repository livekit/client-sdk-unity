using System;
using System.Runtime.InteropServices;
using LiveKit.Proto;

namespace LiveKit.Internal
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FFICallbackDelegate(IntPtr data, UIntPtr size);

    // Callbacks
    internal delegate void PublishTrackDelegate(PublishTrackCallback e);


    internal delegate void ConnectReceivedDelegate(ConnectCallback e);


    internal delegate void DisconnectReceivedDelegate(DisconnectCallback e);


    // Events
    internal delegate void RoomEventReceivedDelegate(RoomEvent e);


    internal delegate void TrackEventReceivedDelegate(TrackEvent e);


    internal delegate void VideoStreamEventReceivedDelegate(VideoStreamEvent e);


    internal delegate void AudioStreamEventReceivedDelegate(AudioStreamEvent e);

}