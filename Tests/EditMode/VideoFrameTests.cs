using System;
using LiveKit.Internal;
using LiveKit.Proto;
using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class VideoFrameTests
    {
        static VideoBufferInfo BufferInfo(VideoBufferType type)
        {
            return new VideoBufferInfo { Type = type, Width = 16, Height = 16 };
        }

        [TestCase(VideoBufferType.I420, typeof(I420Buffer))]
        [TestCase(VideoBufferType.I420A, typeof(I420ABuffer))]
        [TestCase(VideoBufferType.I422, typeof(I422Buffer))]
        [TestCase(VideoBufferType.I444, typeof(I444Buffer))]
        [TestCase(VideoBufferType.I010, typeof(I010Buffer))]
        [TestCase(VideoBufferType.Nv12, typeof(NV12Buffer))]
        public void Create_ReturnsCorrectConcreteType(VideoBufferType type, Type expected)
        {
            // IntPtr.Zero ⇒ FfiHandle.IsInvalid is true, so SafeHandle will not call
            // ReleaseHandle (no native drop invoked with a bogus pointer).
            var handle = new FfiHandle(IntPtr.Zero);
            var buffer = VideoFrameBuffer.Create(handle, BufferInfo(type));

            Assert.IsNotNull(buffer);
            Assert.IsInstanceOf(expected, buffer);
            buffer.Dispose();
        }

        [Test]
        public void Create_ReturnsNull_ForUnsupportedType()
        {
            var handle = new FfiHandle(IntPtr.Zero);
            var buffer = VideoFrameBuffer.Create(handle, BufferInfo(VideoBufferType.Rgba));
            Assert.IsNull(buffer);
        }
    }
}
