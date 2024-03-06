using System;
using System.Collections.Generic;

namespace LiveKit.Internal
{
    public class FfiHandleFactory : IFfiHandleFactory
    {
        private readonly Stack<FfiHandle> handles = new();
        private readonly object @lock = new();

        public FfiHandle NewFfiHandle(IntPtr ptr)
        {
            lock (@lock)
            {
                if (handles.TryPop(out var handle) == false)
                {
                    handle = new FfiHandle();
                }

                handle!.Construct(ptr);
                return handle;
            }
        }

        public void Release(FfiHandle handle)
        {
            lock (@lock)
            {
                handle.Clear();
                handles.Push(handle);
            }
        }
    }
}