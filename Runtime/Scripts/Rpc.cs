using LiveKit.Proto;

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
        public int ResponseTimeout { get; set; }
    }

    public class RpcError : Exception
    {
        public int Code { get; private set; }
        public string Data { get; private set; }

        public RpcError(int code, string message, string data = null) : base(message)
        {
            Code = code;
            Data = data;
        }

        internal static RpcError FromProto(Proto.RpcError proto)
        {
            return new RpcError(proto.Code, proto.Message, proto.Data);
        }

        internal Proto.RpcError ToProto()
        {
            return new Proto.RpcError
            {
                Code = Code,
                Message = Message,
                Data = Data
            };
        }

        public static class ErrorCode
        {
            public const int APPLICATION_ERROR = 1500;
            public const int CONNECTION_TIMEOUT = 1501;
            public const int RESPONSE_TIMEOUT = 1502;
            public const int RECIPIENT_DISCONNECTED = 1503;
            public const int RESPONSE_PAYLOAD_TOO_LARGE = 1504;
            public const int SEND_FAILED = 1505;
            public const int UNSUPPORTED_METHOD = 1400;
            public const int RECIPIENT_NOT_FOUND = 1401;
            public const int REQUEST_PAYLOAD_TOO_LARGE = 1402;
            public const int UNSUPPORTED_SERVER = 1403;
        }

        private static readonly Dictionary<int, string> ErrorMessages = new()
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

        internal static RpcError BuiltIn(int code, string data = null)
        {
            return new RpcError(code, ErrorMessages[code], data);
        }
    }
}
