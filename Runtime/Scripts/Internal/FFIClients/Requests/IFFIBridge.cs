namespace LiveKit.Internal.FFIClients.Requests
{
    public interface IFFIBridge
    {
        FfiRequestWrap<T> NewRequest<T>() where T : class, new();
    }
}