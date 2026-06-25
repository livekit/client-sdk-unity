using System;
using LiveKit.Proto;

namespace LiveKit.Internal.FFIClients
{
    public readonly struct FfiResponseWrap : IDisposable
    {
        private readonly FfiResponse response;
        private readonly IFFIClient client;

        public FfiResponseWrap(FfiResponse response, IFFIClient client)
        {
            this.response = response;
            this.client = client;
        }

        public void Dispose()
        {
            //TODO pooling inner parts
            response.ClearMessage();
            client.Release(response);
        }

        public static implicit operator FfiResponse(FfiResponseWrap wrap)
        {
            return wrap.response;
        }

        public override string ToString()
        {
            return response.ToString()!;
        }
    }
}