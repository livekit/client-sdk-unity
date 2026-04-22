using System;
using LiveKit.Internal;
using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class FfiHandleTests
    {
        // Only the invalid cases are exercised: an FfiHandle wrapping a non-zero/non-(-1)
        // pointer would try to call FfiDropHandle on finalization with a bogus pointer
        // (undefined behavior). IsInvalid=true prevents ReleaseHandle from being called.
        [TestCase(0)]
        [TestCase(-1)]
        public void IsInvalid_ReturnsTrueForKnownInvalidPointers(long pointerValue)
        {
            var handle = new FfiHandle(new IntPtr(pointerValue));
            Assert.IsTrue(handle.IsInvalid);
        }
    }
}
