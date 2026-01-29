#if !UNITY_WEBGL

using System;

namespace LiveKit.Internal
{
    public interface IFfiHandleFactory
    {
        FfiHandle NewFfiHandle(IntPtr ptr);

        FfiHandle NewFfiHandle(ulong ptr) => NewFfiHandle((IntPtr)ptr);

        void Release(FfiHandle ffiHandle);

        static readonly IFfiHandleFactory Default = new FfiHandleFactory();
    }
}

#endif
