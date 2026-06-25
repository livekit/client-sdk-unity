using System;
using LiveKit.Internal.FFIClients.Pools;

namespace LiveKit.Internal.FFIClients.Requests
{
    public class FFIBridge : IFFIBridge
    {
        //TODO should be without singleton, remove it
        private static readonly Lazy<FFIBridge> instance = new(() =>
            new FFIBridge(
                FfiClient.Instance,
                new ThreadSafeMultiPool()
            )
        );

        public static FFIBridge Instance => instance.Value;

        private readonly IFFIClient ffiClient;
        private readonly IMultiPool multiPool;

        public FFIBridge(IFFIClient client, IMultiPool multiPool)
        {
            ffiClient = client;
            this.multiPool = multiPool;
        }


        public FfiRequestWrap<T> NewRequest<T>() where T : class, new()
        {
            return new FfiRequestWrap<T>(ffiClient, multiPool);
        }
    }
}