using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiveKit
{
    public class PerformRpcParams
    {
        public string DestinationIdentity { get; set; }
        public string Method { get; set; }
        public string Payload { get; set; }
        public float ResponseTimeout { get; set; } = 10f;
    }

    public class RpcInvocationData
    {
        public string RequestId { get; set; }
        public string CallerIdentity { get; set; }
        public string Payload { get; set; }
        public float ResponseTimeout { get; set; }
    }

    public class RpcError : Exception
    {
        public uint Code { get; private set; }
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
