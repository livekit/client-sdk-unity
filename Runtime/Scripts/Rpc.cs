using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiveKit
{
    /// <summary>
    /// Parameters for <see cref="LocalParticipant.PerformRpc"/>.
    /// 
    /// </summary>
    public class PerformRpcParams
    {
        /// <summary>
        /// The identity of the RemoteParticipant to send the request to.
        /// </summary>
        public string DestinationIdentity { get; set; }

        /// <summary>
        /// The name of the RPC method to call. Max 64 bytes UTF-8.
        /// </summary>
        public string Method { get; set; }

        /// <summary>
        /// String payload data to send to the remote participant. Max 15KiB UTF-8.
        /// </summary>
        public string Payload { get; set; }

        /// <summary>
        /// The maximum time to wait for a response from the remote participant. Default is 10 seconds.
        /// </summary>
        public float ResponseTimeout { get; set; } = 10f;
    }

    /// <summary>
    /// Data supplied to an RPC method handler registered with <see cref="LocalParticipant.RegisterRpcMethod"/>.
    /// </summary>
    public class RpcInvocationData
    {
        /// <summary>
        /// A unique identifier for the RPC request.
        /// </summary>
        public string RequestId { get; set; }

        /// <summary>
        /// The identity of the RemoteParticipant that made the RPC call.
        /// </summary>
        public string CallerIdentity { get; set; }

        /// <summary>
        /// The string payload data sent by the caller.
        /// </summary>
        public string Payload { get; set; }

        /// <summary>
        /// The maximum time available to respond before the caller times out.
        /// </summary>
        public float ResponseTimeout { get; set; }
    }


    /// <summary>
    /// Errors thrown by RPC method handlers or generated due to a failure in the RPC call.
    /// 
    /// Built-in error codes are listed in <see cref="RpcError.ErrorCode"/>. 
    /// 
    /// You may also throw custom errors with your own code and data.  
    /// Errors of this type thrown in your handler will be transmitted as-is without modification. 
    /// All other errors will be converted to a generic APPLICATION_ERROR (1500).
    /// </summary>
    public class RpcError : Exception
    {
        /// <summary>
        /// Integer error code. Values 1000-1999 are reserved for the framework, see <see cref="ErrorCode"/>.
        /// </summary>
        public uint Code { get; private set; }

        /// <summary>
        /// String error data. Max 15KiB UTF-8.
        /// </summary>
        public string RpcData { get; private set; }

        public RpcError(uint code, string message, string rpcData = null) : base(message)
        {
            Code = code;
            RpcData = rpcData;
        }

        internal static RpcError FromProto(Proto.RpcError proto)
        {
            Debug.Log($"RPC error received: {proto.Code} {proto.Message} {proto.Data}");
            return new RpcError(proto.Code, proto.Message, proto.Data);
        }

        internal Proto.RpcError ToProto()
        {
            return new Proto.RpcError
            {
                Code = Code,
                Message = Message,
                Data = RpcData ?? ""
            };
        }

        /// <summary>
        /// Built-in error codes. See https://docs.livekit.io/home/client/data/rpc/#errors for more information.
        /// </summary>
        public enum ErrorCode : uint
        {
            APPLICATION_ERROR = 1500,
            CONNECTION_TIMEOUT = 1501,
            RESPONSE_TIMEOUT = 1502,
            RECIPIENT_DISCONNECTED = 1503,
            RESPONSE_PAYLOAD_TOO_LARGE = 1504,
            SEND_FAILED = 1505,
            UNSUPPORTED_METHOD = 1400,
            RECIPIENT_NOT_FOUND = 1401,
            REQUEST_PAYLOAD_TOO_LARGE = 1402,
            UNSUPPORTED_SERVER = 1403
        }

        private static readonly Dictionary<ErrorCode, string> ErrorMessages = new()
        {
            { ErrorCode.APPLICATION_ERROR, "Application error in method handler" },
            { ErrorCode.CONNECTION_TIMEOUT, "Connection timeout" },
            { ErrorCode.RESPONSE_TIMEOUT, "Response timeout" },
            { ErrorCode.RECIPIENT_DISCONNECTED, "Recipient disconnected" },
            { ErrorCode.RESPONSE_PAYLOAD_TOO_LARGE, "Response payload too large" },
            { ErrorCode.SEND_FAILED, "Failed to send" },
            { ErrorCode.UNSUPPORTED_METHOD, "Method not supported at destination" },
            { ErrorCode.RECIPIENT_NOT_FOUND, "Recipient not found" },
            { ErrorCode.REQUEST_PAYLOAD_TOO_LARGE, "Request payload too large" },
            { ErrorCode.UNSUPPORTED_SERVER, "RPC not supported by server" }
        };

        internal static RpcError BuiltIn(ErrorCode code, string rpcData = null)
        {
            return new RpcError((uint)code, ErrorMessages[code], rpcData);
        }
    }
}
