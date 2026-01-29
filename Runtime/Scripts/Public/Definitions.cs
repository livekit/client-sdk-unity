namespace DCL.LiveKit.Public
{
    public enum LKDataPacketKind
    {
        //[pbr::OriginalName("KIND_LOSSY")] 
        KindLossy = 0,
        //[pbr::OriginalName("KIND_RELIABLE")]
        KindReliable = 1,
    }

    public enum LKConnectionState {
        //[pbr::OriginalName("CONN_DISCONNECTED")]
        ConnDisconnected = 0,
        //[pbr::OriginalName("CONN_CONNECTED")]
        ConnConnected = 1,
        //[pbr::OriginalName("CONN_RECONNECTING")]
        ConnReconnecting = 2,
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
