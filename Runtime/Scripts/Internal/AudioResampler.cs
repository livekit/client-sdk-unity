using System;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    public sealed class AudioResampler : IDisposable
    {
        private readonly FfiHandle _handle;
        private bool _disposed;

        public AudioResampler()
        {
            using var request = FFIBridge.Instance.NewRequest<NewAudioResamplerRequest>();
            using var response = request.Send();
            FfiResponse res = response;
            _handle = FfiHandle.FromOwnedHandle(res.NewAudioResampler.Resampler.Handle);
        }

        public AudioFrame RemixAndResample(AudioFrame frame, uint numChannels, uint sampleRate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AudioResampler));
            }

            using var request = FFIBridge.Instance.NewRequest<RemixAndResampleRequest>();
            using var audioFrameBufferInfo = request.TempResource<AudioFrameBufferInfo>();
            var remix = request.request;
            remix.ResamplerHandle = (ulong)_handle.DangerousGetHandle();
            remix.Buffer = frame.Info;
            remix.NumChannels = numChannels;
            remix.SampleRate = sampleRate;

            using var response = request.Send();
            FfiResponse res = response;
            if (res.RemixAndResample == null)
            {
                return null;
            }
            var newBuffer = res.RemixAndResample.Buffer;
            return new AudioFrame(newBuffer);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _handle.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        ~AudioResampler()
        {
            if (_disposed)
            {
                return;
            }

            _handle.Dispose();
            _disposed = true;
        }
    }
}
