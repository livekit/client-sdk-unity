using System;
using System.Collections.Concurrent;
using UnityEngine.Pool;

namespace LiveKit.Internal.FFIClients.Pools.ObjectPool
{
    public class ThreadSafeObjectPool<T> : IObjectPool<T> where T : class
    {
        private readonly Func<T> create;
        private readonly Action<T>? actionOnRelease;
        private readonly ConcurrentBag<T> bag = new();

        public ThreadSafeObjectPool(Func<T> create, Action<T>? actionOnRelease = null)
        {
            this.create = create;
            this.actionOnRelease = actionOnRelease;
        }

        public T Get()
        {
            return bag.TryTake(out var result) 
                ? result!
                : create()!;
        }

        public PooledObject<T> Get(out T v)
        {
            v = Get();
            return new PooledObject<T>();
        }

        public void Release(T element)
        {
            actionOnRelease?.Invoke(element);
            bag.Add(element);
        }

        public void Clear()
        {
            bag.Clear();
        }

        public int CountInactive => bag.Count;
    }
}