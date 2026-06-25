namespace LiveKit.Internal.FFIClients.Pools
{
    public interface IMultiPool
    {
        T Get<T>() where T : class, new();

        void Release<T>(T poolObject) where T : class, new();
    }
}