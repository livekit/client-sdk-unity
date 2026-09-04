using System;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    /// <summary>
    /// A decoded video frame, extracted from the raw FFI event into a protobuf-free value.
    /// Carries the native plane pointers and geometry only — no managed wrappers and no
    /// <c>Google.Protobuf</c> surface, so consumers can use frames without inheriting a
    /// compile-time protobuf dependency.
    /// </summary>
    /// <remarks>
    /// The <c>DataPtr*</c> values point at native buffers that are valid for the duration of
    /// the <see cref="FfiFrameObserver.VideoFrameReceived"/> callback ONLY — copy out
    /// synchronously if you need them past return.
    /// </remarks>
    public readonly struct RawVideoFrame
    {
        /// <summary>Stream handle the frame arrived on, for correlating to a <see cref="VideoStream"/>.</summary>
        public readonly ulong StreamHandle;
        public readonly IntPtr DataPtrY, DataPtrU, DataPtrV;
        public readonly int StrideY, StrideU, StrideV;
        public readonly int Width, Height;

        public RawVideoFrame(
            ulong streamHandle,
            IntPtr dataPtrY, int strideY,
            IntPtr dataPtrU, int strideU,
            IntPtr dataPtrV, int strideV,
            int width, int height)
        {
            StreamHandle = streamHandle;
            DataPtrY = dataPtrY; StrideY = strideY;
            DataPtrU = dataPtrU; StrideU = strideU;
            DataPtrV = dataPtrV; StrideV = strideV;
            Width = width; Height = height;
        }
    }

    /// <summary>
    /// A decoded audio frame, extracted from the raw FFI event into a protobuf-free value.
    /// <see cref="DataPtr"/> points at interleaved S16 PCM valid for the callback duration ONLY.
    /// </summary>
    public readonly struct RawAudioFrame
    {
        /// <summary>Stream handle the frame arrived on, for correlating to an <see cref="AudioStream"/>.</summary>
        public readonly ulong StreamHandle;
        public readonly IntPtr DataPtr;
        public readonly int SamplesPerChannel, NumChannels, SampleRate;

        public RawAudioFrame(ulong streamHandle, IntPtr dataPtr, int samplesPerChannel, int numChannels, int sampleRate)
        {
            StreamHandle = streamHandle;
            DataPtr = dataPtr;
            SamplesPerChannel = samplesPerChannel;
            NumChannels = numChannels;
            SampleRate = sampleRate;
        }
    }

    /// <summary>
    /// Opt-in extension point for raw decoded frames.
    /// </summary>
    /// <remarks>
    /// <see cref="VideoFrameReceived"/> / <see cref="AudioFrameReceived"/> are invoked
    /// synchronously on the FFI callback thread from the event router, BEFORE the event's
    /// <c>FfiHandle</c>s wrap or free the underlying native buffers — so the <c>DataPtr</c>
    /// values each frame carries are valid for the duration of the callback ONLY.
    ///
    /// The SDK extracts the protobuf event into the plain <see cref="RawVideoFrame"/> /
    /// <see cref="RawAudioFrame"/> structs here, on the FFI thread, so subscribers consume
    /// decoded frames WITHOUT a compile-time dependency on <c>Google.Protobuf</c>.
    ///
    /// Subscriber contract:
    /// <list type="bullet">
    ///   <item>Runs on the FFI thread, not Unity's main loop — do not touch Unity APIs.</item>
    ///   <item>Must be non-blocking; it sits in the frame-delivery hot path.</item>
    ///   <item>Must NOT retain any <c>DataPtr</c> past return — copy out synchronously if needed.</item>
    /// </list>
    ///
    /// No subscriber == zero cost: extraction is skipped entirely when the matching delegate
    /// is null. This lets consumers build native Picture-in-Picture, echo-cancellation
    /// references, frame capture, custom GPU upload, or analytics on top of the decoded
    /// stream without patching the SDK.
    /// </remarks>
    public static class FfiFrameObserver
    {
        public static event Action<RawVideoFrame> VideoFrameReceived;
        public static event Action<RawAudioFrame> AudioFrameReceived;

        internal static void Dispatch(FfiEvent ev)
        {
            switch (ev.MessageCase)
            {
                case FfiEvent.MessageOneofCase.VideoStreamEvent:
                    ExtractVideo(ev.VideoStreamEvent);
                    break;
                case FfiEvent.MessageOneofCase.AudioStreamEvent:
                    ExtractAudio(ev.AudioStreamEvent);
                    break;
            }
        }

        private static void ExtractVideo(VideoStreamEvent vse)
        {
            var handler = VideoFrameReceived;
            if (handler == null) return;
            if (vse.MessageCase != VideoStreamEvent.MessageOneofCase.FrameReceived) return;

            var buf = vse.FrameReceived?.Buffer;
            if (buf?.Info == null || buf.Info.Components.Count < 3) return;

            var info = buf.Info;
            var yc = info.Components[0];
            var uc = info.Components[1];
            var vc = info.Components[2];
            if (yc.DataPtr == 0 || uc.DataPtr == 0 || vc.DataPtr == 0) return;

            Invoke(handler, new RawVideoFrame(
                vse.StreamHandle,
                (IntPtr)(long)yc.DataPtr, (int)yc.Stride,
                (IntPtr)(long)uc.DataPtr, (int)uc.Stride,
                (IntPtr)(long)vc.DataPtr, (int)vc.Stride,
                (int)info.Width, (int)info.Height));
        }

        private static void ExtractAudio(AudioStreamEvent ase)
        {
            var handler = AudioFrameReceived;
            if (handler == null) return;
            if (ase.MessageCase != AudioStreamEvent.MessageOneofCase.FrameReceived) return;

            var frame = ase.FrameReceived?.Frame;
            if (frame?.Info == null || frame.Info.DataPtr == 0) return;

            var info = frame.Info;
            Invoke(handler, new RawAudioFrame(
                ase.StreamHandle,
                (IntPtr)(long)info.DataPtr,
                (int)info.SamplesPerChannel,
                (int)info.NumChannels,
                (int)info.SampleRate));
        }

        // A subscriber exception must not escape the native callback: this runs on the FFI
        // thread inside a reverse P/Invoke, where an unhandled managed exception is fatal.
        private static void Invoke<T>(Action<T> handler, T frame)
        {
            try
            {
                handler(frame);
            }
            catch (Exception e)
            {
                Utils.Error($"FfiFrameObserver subscriber threw: {e}");
            }
        }
    }
}
