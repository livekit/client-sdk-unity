using System;
using System.Collections.Generic;
using UnityEngine.Pool;

namespace LiveKit.Internal.FFIClients.Pools
{
    public class MultiPool : IMultiPool
    {
        private readonly Dictionary<Type, IObjectPool<object>> pools = new();

        public T Get<T>() where T : class, new()
        {
            return (T) Pool<T>().Get()!;
        }

        public void Release<T>(T poolObject) where T : class, new()
        {
            Pool<T>().Release(poolObject);
        }

        private IObjectPool<object> Pool<T>() where T : class, new()
        {
            var type = typeof(T);
            if (!pools.TryGetValue(type, out var pool))
            {
                pool = pools[type] = new ObjectPool<object>(() => new T());
            }

            return pool!;
        }
    }
}