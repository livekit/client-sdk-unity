using System;
using LiveKit.Proto;
using UnityEngine.Pool;

namespace LiveKit.Internal.FFIClients.Pools//
{
    public static class Pools
    {
        public static IObjectPool<FfiRequest> NewFfiRequestPool()
        {
            return NewClearablePool<FfiRequest>(FfiRequestExtensions.EnsureClean);
        }
        
        public static IObjectPool<FfiResponse> NewFfiResponsePool()
        {
            return NewClearablePool<FfiResponse>(FfiRequestExtensions.EnsureClean);
        }
        
        public static IObjectPool<T> NewClearablePool<T>(Action<T> ensureClean) where T : class, new()
        {
            return new ObjectPool<T>(
                () => new T(),
                actionOnRelease: ensureClean
            );
        }
    }
}