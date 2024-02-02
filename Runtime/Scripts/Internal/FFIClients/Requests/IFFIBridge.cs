namespace LiveKit.Internal.FFIClients.Requests
{
    /// <summary>
    /// Thread-safe interface for requests to the FFI layer
    /// </summary>
    public interface IFFIBridge
    {
        FfiRequestWrap<T> NewRequest<T>() where T : class, new();
    }
}