using System;
using LiveKit.Proto;

namespace LiveKit.Internal.FFIClients
{
    /// <summary>
    /// Thread-safe interface for sending requests to the FFI layer
    /// </summary>
    public interface IFFIClient : IDisposable
    {
        FfiResponse SendRequest(FfiRequest request, bool requireResponse = true);

        void Release(FfiResponse response);
    }
}