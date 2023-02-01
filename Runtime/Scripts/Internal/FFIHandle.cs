using System;
using System.Runtime.InteropServices;
using LiveKit.Proto;

namespace LiveKit.Internal
{
    public class FFIHandle : SafeHandle
    {
        private FFIHandleId _handle;

        // An FFIHandle instance is always owned (Getting them from the FFIClient)
        internal FFIHandle(IntPtr ptr) : base(ptr, true)
        {
            _handle = new FFIHandleId();
            _handle.Id = unchecked((uint)ptr.ToInt32());
        }

        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        protected override bool ReleaseHandle()
        {
            var releaseHandle = new ReleaseHandleRequest();
            releaseHandle.Handle = _handle;

            var request = new FFIRequest();
            request.ReleaseHandle = releaseHandle;

            FFIClient.Instance.SendRequest(request);
            return true;
        }
    }
}
