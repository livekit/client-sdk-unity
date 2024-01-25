using System;
using LiveKit.Proto;

namespace LiveKit.Internal.FFIClients
{
    public interface IFFIClient : IDisposable
    {
        FfiResponse SendRequest(FfiRequest request);

        void Release(FfiResponse response);
    }
}