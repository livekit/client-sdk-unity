#if UNITY_WEBGL
using JsConnectionState = LiveKit.ConnectionState;
using JsConnectionQuality = LiveKit.ConnectionQuality;
#endif

namespace DCL.LiveKit.Public
{
    public enum LKDataPacketKind
    {
        //[pbr::OriginalName("KIND_LOSSY")] 
        KindLossy = 0,
        //[pbr::OriginalName("KIND_RELIABLE")]
        KindReliable = 1,
    }

    internal static class LKDataPacketKindUtils
    {
        public static global::LiveKit.Proto.DataPacketKind ToProto(LKDataPacketKind kind)
        {
            return kind switch
            {
                LKDataPacketKind.KindLossy => global::LiveKit.Proto.DataPacketKind.KindLossy,
                LKDataPacketKind.KindReliable => global::LiveKit.Proto.DataPacketKind.KindReliable,
            };
        }

        public static LKDataPacketKind FromProto(global::LiveKit.Proto.DataPacketKind kind)
        {
            return kind switch
            {
                global::LiveKit.Proto.DataPacketKind.KindLossy => LKDataPacketKind.KindLossy,
                global::LiveKit.Proto.DataPacketKind.KindReliable => LKDataPacketKind.KindReliable,
            };
        }
    }

    public enum LKConnectionState {
        //[pbr::OriginalName("CONN_DISCONNECTED")]
        ConnDisconnected = 0,
        //[pbr::OriginalName("CONN_CONNECTED")]
        ConnConnected = 1,
        //[pbr::OriginalName("CONN_RECONNECTING")]
        ConnReconnecting = 2,
    }

    internal static class LKConnectionStateUtils
    {
#if UNITY_WEBGL
        public static LKConnectionState FromJsState(JsConnectionState state)
        {
            return state switch
            {
                JsConnectionState.Disconnected => LKConnectionState.ConnDisconnected,
                JsConnectionState.Connecting => LKConnectionState.ConnDisconnected, // Yes, PROTO doesn't support the 'connecting' state, thus map it to disconnected, 'reconnected' state won't fit because it may have specific logic
                JsConnectionState.Connected => LKConnectionState.ConnConnected,
                JsConnectionState.Reconnecting => LKConnectionState.ConnReconnecting,
            };
        }
#endif

        public static LKConnectionState FromProtoState(global::LiveKit.Proto.ConnectionState state)
        {
            return state switch
            {
                global::LiveKit.Proto.ConnectionState.ConnDisconnected => LKConnectionState.ConnDisconnected,
                global::LiveKit.Proto.ConnectionState.ConnConnected => LKConnectionState.ConnConnected,
                global::LiveKit.Proto.ConnectionState.ConnReconnecting => LKConnectionState.ConnReconnecting
            };
        }
    }

    public enum LKConnectionQuality {
        //[pbr::OriginalName("QUALITY_POOR")]
        QualityPoor = 0,
        //[pbr::OriginalName("QUALITY_GOOD")]
        QualityGood = 1,
        //[pbr::OriginalName("QUALITY_EXCELLENT")]
        QualityExcellent = 2,
        //[pbr::OriginalName("QUALITY_LOST")]
        QualityLost = 3,
    }

    internal static class LKConnectionQualityUtils
    {
#if UNITY_WEBGL
        public static LKConnectionQuality FromJsQuality(JsConnectionQuality quality)
        {
            return quality switch
            {
                JsConnectionQuality.Unknown => LKConnectionQuality.QualityLost,
                JsConnectionQuality.Poor => LKConnectionQuality.QualityPoor,
                JsConnectionQuality.Good => LKConnectionQuality.QualityGood,
                JsConnectionQuality.Excellent => LKConnectionQuality.QualityExcellent,
            };
        }
#endif
        
        public static LKConnectionQuality FromProtoQuality(global::LiveKit.Proto.ConnectionQuality quality)
        {
            return quality switch
            {
                global::LiveKit.Proto.ConnectionQuality.QualityLost => LKConnectionQuality.QualityLost,
                global::LiveKit.Proto.ConnectionQuality.QualityPoor => LKConnectionQuality.QualityPoor,
                global::LiveKit.Proto.ConnectionQuality.QualityGood => LKConnectionQuality.QualityGood,
                global::LiveKit.Proto.ConnectionQuality.QualityExcellent => LKConnectionQuality.QualityExcellent,
            };
        }
    }

    public enum LKDisconnectReason
    {
        //[pbr::OriginalName("UNKNOWN_REASON")]
        UnknownReason = 0,
        /// <summary>
        /// the client initiated the disconnect
        /// </summary>
        //[pbr::OriginalName("CLIENT_INITIATED")]
        ClientInitiated = 1,
        /// <summary>
        /// another participant with the same identity has joined the room
        /// </summary>
        //[pbr::OriginalName("DUPLICATE_IDENTITY")]
        DuplicateIdentity = 2,
        /// <summary>
        /// the server instance is shutting down
        /// </summary>
        //[pbr::OriginalName("SERVER_SHUTDOWN")]
        ServerShutdown = 3,
        /// <summary>
        /// RoomService.RemoveParticipant was called
        /// </summary>
        //[pbr::OriginalName("PARTICIPANT_REMOVED")]
        ParticipantRemoved = 4,
        /// <summary>
        /// RoomService.DeleteRoom was called
        /// </summary>
        //[pbr::OriginalName("ROOM_DELETED")]
        RoomDeleted = 5,
        /// <summary>
        /// the client is attempting to resume a session, but server is not aware of it
        /// </summary>
        //[pbr::OriginalName("STATE_MISMATCH")]
        StateMismatch = 6,
        /// <summary>
        /// client was unable to connect fully
        /// </summary>
        //[pbr::OriginalName("JOIN_FAILURE")]
        JoinFailure = 7,
        /// <summary>
        /// Cloud-only, the server requested Participant to migrate the connection elsewhere
        /// </summary>
        //[pbr::OriginalName("MIGRATION")]
        Migration = 8,
        /// <summary>
        /// the signal websocket was closed unexpectedly
        /// </summary>
        //[pbr::OriginalName("SIGNAL_CLOSE")]
        SignalClose = 9,
        /// <summary>
        /// the room was closed, due to all Standard and Ingress participants having left
        /// </summary>
        //[pbr::OriginalName("ROOM_CLOSED")]
        RoomClosed = 10,
        /// <summary>
        /// SIP callee did not respond in time
        /// </summary>
        //[pbr::OriginalName("USER_UNAVAILABLE")]
        UserUnavailable = 11,
        /// <summary>
        /// SIP callee rejected the call (busy)
        /// </summary>
        //[pbr::OriginalName("USER_REJECTED")]
        UserRejected = 12,
        /// <summary>
        /// SIP protocol failure or unexpected response
        /// </summary>
        //[pbr::OriginalName("SIP_TRUNK_FAILURE")]
        SipTrunkFailure = 13,
        //[pbr::OriginalName("CONNECTION_TIMEOUT")]
        ConnectionTimeout = 14,
    }

}
