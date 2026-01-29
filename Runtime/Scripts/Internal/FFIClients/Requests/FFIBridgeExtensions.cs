#if !UNITY_WEBGL

using LiveKit.Proto;
using LiveKit.Rooms;

namespace LiveKit.Internal.FFIClients.Requests
{
    public static class FFIBridgeExtensions
    {
        public static FfiResponseWrap SendConnectRequest(this IFFIBridge ffiBridge, string url, string authToken, bool autoSubscribe)
        {
            Utils.Debug("Connect....");
            using var request = ffiBridge.NewRequest<ConnectRequest>();
            using var roomOptions = request.TempResource<RoomOptions>();
            var connect = request.request;
            connect.Url = url;
            connect.Token = authToken;
            connect.Options = roomOptions;
            connect.Options.AutoSubscribe = autoSubscribe;
            var response = request.Send();
            Utils.Debug($"Connect response.... {response}");
            return response;
        }
        
        public static FfiResponseWrap SendDisconnectRequest(this IFFIBridge ffiBridge, FfiHandle roomHandle)
        {
            using var request = ffiBridge.NewRequest<DisconnectRequest>();
            var disconnect = request.request;
            disconnect.RoomHandle = (ulong)roomHandle.DangerousGetHandle();
            Utils.Debug($"Disconnect.... {disconnect.RoomHandle}");
            var response = request.Send();
            // ReSharper disable once RedundantAssignment
            Utils.Debug($"Disconnect response.... {response}");
            return response;
        }
    }
}

#endif
