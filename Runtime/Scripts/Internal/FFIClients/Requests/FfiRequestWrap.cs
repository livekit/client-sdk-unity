using System;
using Google.Protobuf;
using LiveKit.client_sdk_unity.Runtime.Scripts.Internal.FFIClients;
using LiveKit.Internal.FFIClients.Pools;
using LiveKit.Proto;

namespace LiveKit.Internal.FFIClients.Requests
{
    public struct FfiRequestWrap<T> : IDisposable where T : class, new()
    {
        public readonly T request;
        private readonly IMultiPool multiPool;
        private readonly IFFIClient ffiClient;
        private readonly FfiRequest ffiRequest;
        private readonly Action<FfiRequest> releaseFfiRequest;
        private readonly Action<T> releaseRequest;

        private bool sent;

        public FfiRequestWrap(IFFIClient ffiClient, IMultiPool multiPool) : this(
            multiPool.Get<T>(),
            multiPool,
            multiPool.Get<FfiRequest>(),
            ffiClient,
            multiPool.Release,
            multiPool.Release
        )
        {
        }

        public FfiRequestWrap(
            T request,
            IMultiPool multiPool,
            FfiRequest ffiRequest,
            IFFIClient ffiClient,
            Action<FfiRequest> releaseFfiRequest,
            Action<T> releaseRequest
        )
        {
            this.request = request;
            this.multiPool = multiPool;
            this.ffiRequest = ffiRequest;
            this.ffiClient = ffiClient;
            this.releaseFfiRequest = releaseFfiRequest;
            this.releaseRequest = releaseRequest;
            sent = false;
        }

        public FfiResponseWrap Send()
        {
            if (sent)
            {
                throw new Exception("Request already sent");
            }

            sent = true;
            ffiRequest.Inject(request);
            var response = ffiClient.SendRequest(ffiRequest);
            return new FfiResponseWrap(response, ffiClient);
        }

        public SmartWrap<TK> TempResource<TK>() where TK : class, IMessage, new()
        {
            var resource = multiPool.Get<TK>();
            return new SmartWrap<TK>(resource, multiPool);
        }

        public void Dispose()
        {
            ffiRequest.ClearMessage();
            releaseRequest(request);
            releaseFfiRequest(ffiRequest);
        }
    }
}