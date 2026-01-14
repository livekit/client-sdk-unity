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
#if !UNITY_WEBGL
        private readonly RustAudioSource native;
#endif

        private PlaybackMicrophoneAudioSource? lateBindPlaybackProxy;
        private bool disposed;

#if !UNITY_WEBGL
        public MicrophoneInfo MicrophoneInfo => native.microphoneInfo;

        public bool IsRecording => native.IsRecording;
#else
        // Actually is impossible to call, because the ctor cannot produce the instance of the class.
        // But to keep the contracts alive I'll preserve the signature
        public MicrophoneInfo MicrophoneInfo => new MicrophoneInfo(string.Empty, 0, 0);

        public bool IsRecording => false;
#endif

        public bool IsValid => disposed == false;

        public event IAudioFilter.OnAudioDelegate? AudioRead;


#if !UNITY_WEBGL
        private MicrophoneAudioFilter(RustAudioSource native)
        {
            this.native = native;

            native.AudioRead += NativeOnAudioRead;
        }
#endif

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

#if !UNITY_WEBGL
            native.AudioRead -= NativeOnAudioRead;
            native.Dispose();
#endif

            if (lateBindPlaybackProxy)
                lateBindPlaybackProxy.Dispose();
        }

        public static Result<MicrophoneAudioFilter> New(
            MicrophoneSelection? microphoneName = null,
            bool withPlayback = false)
        {
#if UNITY_WEBGL
            return Result<MicrophoneAudioFilter>.ErrorResult(
                $"MicrophoneAudioFilter is not supported on WEBGL");
#else
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
#endif
        }

        public static string[] AvailableDeviceNamesOrEmpty()
        {
            var result = RustAudioClient.AvailableDeviceNames();
            return result.Success ? result.Value : Array.Empty<string>();
        }

        private void NativeOnAudioRead(Span<float> data)
        {
            MicrophoneInfo info = MicrophoneInfo;
            AudioRead?.Invoke(data, (int)info.channels, (int)info.sampleRate);
        }

        public void StartCapture()
        {
#if !UNITY_WEBGL
            native.StartCapture();
#endif
        }

        public void StopCapture()
        {
#if !UNITY_WEBGL
            native.PauseCapture();
#endif
        }
    }
}
