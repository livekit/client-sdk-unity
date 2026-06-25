using System;
using LiveKit.Internal.FFIClients.Pools;

namespace LiveKit.client_sdk_unity.Runtime.Scripts.Internal.FFIClients
{
    public readonly struct SmartWrap<T> : IDisposable where T : class, new()
    {
        public readonly T value;
        private readonly IMultiPool pool;

        public SmartWrap(T value, IMultiPool pool)
        {
            this.value = value;
            this.pool = pool;
        }

        public void Dispose()
        {
            pool.Release(value);
        }

        public static implicit operator T(SmartWrap<T> wrap)
        {
            return wrap.value;
        }
    }
}