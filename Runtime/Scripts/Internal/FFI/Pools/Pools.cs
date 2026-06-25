using System;
using LiveKit.Proto;
using UnityEngine.Pool;

namespace LiveKit.Internal.FFI.Pools
{
    public static class Pools
    {
        public static IObjectPool<FfiResponse> NewFfiResponsePool()
        {
            return NewClearablePool<FfiResponse>(FfiRequestExtensions.EnsureClean);
        }
        
        public static IObjectPool<T> NewClearablePool<T>(Action<T> ensureClean) where T : class, new()
        {
            return new ThreadSafeObjectPool<T>(
                () => new T(),
                actionOnRelease: ensureClean
            );
        }
    }
}