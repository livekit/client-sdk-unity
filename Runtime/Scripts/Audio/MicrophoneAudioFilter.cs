using System;
using System.Linq;
using LiveKit.Audio;
using LiveKit.Runtime.Scripts.Audio;
using RichTypes;
using RustAudio;

namespace LiveKit.Scripts.Audio
{
    public class MicrophoneAudioFilter : IAudioFilter, IDisposable
    {
        public readonly MicrophoneInfo microphoneInfo;
        private readonly RustAudioSource native;
        private PlaybackMicrophoneAudioSource? lateBindPlaybackProxy;

        private bool disposed;

        public bool IsRecording => native.IsRecording;

        public bool IsValid => disposed == false;

        public event IAudioFilter.OnAudioDelegate? AudioRead;

        private MicrophoneAudioFilter(RustAudioSource native)
        {
            microphoneInfo = native.microphoneInfo;
            this.native = native;

            native.AudioRead += NativeOnAudioRead;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            native.AudioRead -= NativeOnAudioRead;
            native.Dispose();
            if (lateBindPlaybackProxy)
                lateBindPlaybackProxy.Dispose();
        }

        public static Result<MicrophoneAudioFilter> New(
            MicrophoneSelection? microphoneName = null,
            bool withPlayback = false)
        {
            Result<string[]> deviceNames = RustAudioClient.AvailableDeviceNames();
            if (deviceNames.Success == false)
            {
                return Result<MicrophoneAudioFilter>.ErrorResult(
                    $"Cannot get device names: {deviceNames.ErrorMessage}");
            }

            if (deviceNames.Value.Length == 0)
            {
                return Result<MicrophoneAudioFilter>.ErrorResult(
                    "No available input devices");
            }

            string name = microphoneName == null ? deviceNames.Value.First() : microphoneName.Value.name;
            Result<RustAudioSource> source = RustAudioClient.NewStream(name);

            if (source.Success == false)
            {
                return Result<MicrophoneAudioFilter>.ErrorResult($"Cannot create new stream: {source.ErrorMessage}");
            }

            var rustSource = source.Value;

            var instance = new MicrophoneAudioFilter(rustSource);

            if (withPlayback)
            {
                instance.lateBindPlaybackProxy = PlaybackMicrophoneAudioSource.New(instance, name);
            }

            return Result<MicrophoneAudioFilter>.SuccessResult(instance);
        }

        public static string[] AvailableDeviceNamesOrEmpty()
        {
            var result = RustAudioClient.AvailableDeviceNames();
            return result.Success ? result.Value : Array.Empty<string>();
        }

        private void NativeOnAudioRead(Span<float> data)
        {
            AudioRead?.Invoke(data, (int)microphoneInfo.channels, (int)microphoneInfo.sampleRate);
        }

        public void StartCapture()
        {
            native.StartCapture();
        }

        public void StopCapture()
        {
            native.PauseCapture();
        }
    }
}