using System;
using System.Collections;
using LiveKit.Proto;
using LiveKit.Internal;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit
{
    public enum RtcAudioSourceType
    {
        AudioSourceCustom = 0,
        // if the source is a microphone,
        // we don't want to play the audio locally
        AudioSourceMicrophone = 1,
    }

    public abstract class RtcAudioSource : IRtcSource
    {
        public abstract event Action<float[], int, int> AudioRead;
        public virtual IEnumerator Prepare(float timeout = 0) { yield break; }
        public abstract void Play();

#if UNITY_IOS
        // iOS microphone sample rate is 24k,
        // please make sure when you using
        // sourceType is AudioSourceMicrophone
        public static uint DefaultMirophoneSampleRate = 24000;

        public static uint DefaultSampleRate = 48000;
#else
        public static uint DefaultSampleRate = 48000;
        public static uint DefaultMirophoneSampleRate = DefaultSampleRate;
#endif
        public static uint DefaultChannels = 2;

        private RtcAudioSourceType _sourceType;

        public RtcAudioSourceType SourceType => _sourceType;

        internal readonly FfiHandle Handle;
        protected AudioSourceInfo _info;

        // Possibly used on the AudioThread
        private Thread _readAudioThread;
        private AudioBuffer _captureBuffer = new AudioBuffer();

        private bool _muted = false;
        public override bool Muted => _muted;

        protected RtcAudioSource(int channels = 2, RtcAudioSourceType audioSourceType = RtcAudioSourceType.AudioSourceCustom)
        {
            _sourceType = audioSourceType;

            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = (uint)channels;
            if(_sourceType == RtcAudioSourceType.AudioSourceMicrophone)
            {
                newAudioSource.SampleRate = DefaultMirophoneSampleRate;
            }
            else
            {
                newAudioSource.SampleRate = DefaultSampleRate;
            }
            newAudioSource.Options = request.TempResource<AudioSourceOptions>();
            newAudioSource.Options.EchoCancellation = true;
            newAudioSource.Options.AutoGainControl = true;
            newAudioSource.Options.NoiseSuppression = true;
            using var response = request.Send();
            FfiResponse res = response;
            _info = res.NewAudioSource.Source.Info;
            Handle = FfiHandle.FromOwnedHandle(res.NewAudioSource.Source.Handle);
        }

        public IEnumerator PrepareAndStart()
        {
            yield return Prepare();
            Start();
        }

        public void Start()
        {
            Stop();
            _readAudioThread = new Thread(Update);
            _readAudioThread.Start();
            AudioRead += OnAudioRead;
            Play();
        }

        public virtual void Stop()
        {
            _readAudioThread?.Abort();
            AudioRead -= OnAudioRead;
        }

        private void Update()
        {
            while (true)
            {
                Thread.Sleep(Constants.TASK_DELAY);
                var frame = _captureBuffer.ReadDuration(10); // 10ms
                if (_muted || frame == null) continue;
                Capture(frame);
            }
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            _captureBuffer.Write(data, (uint)channels, (uint)sampleRate);
            if (_sourceType == RtcAudioSourceType.AudioSourceMicrophone)
            {
                // Don't play the audio locally, to avoid echo.
                Array.Clear(data, 0, data.Length);
            }
        }

        private void Capture(AudioFrame frame)
        {
            using var request = FFIBridge.Instance.NewRequest<CaptureAudioFrameRequest>();
            using var audioFrameBufferInfo = request.TempResource<AudioFrameBufferInfo>();

            var pushFrame = request.request;
            pushFrame.SourceHandle = (ulong)Handle.DangerousGetHandle();

            pushFrame.Buffer = audioFrameBufferInfo;
            pushFrame.Buffer.DataPtr = (ulong)frame.Data;
            pushFrame.Buffer.NumChannels = frame.NumChannels;
            pushFrame.Buffer.SampleRate = frame.SampleRate;
            pushFrame.Buffer.SamplesPerChannel = frame.SamplesPerChannel;

            using var response = request.Send();
            FfiResponse res = response;

            // Frame needs to stay alive until receiving the async callback.
            var asyncId = res.CaptureAudioFrame.AsyncId;
            void Callback(CaptureAudioFrameCallback callback)
            {
                if (callback.AsyncId != asyncId) return;
                if (callback.HasError)
                    Utils.Error($"Audio capture failed: {callback.Error}");
                frame.Dispose();
                FfiClient.Instance.CaptureAudioFrameReceived -= Callback;
            }
            FfiClient.Instance.CaptureAudioFrameReceived += Callback;
        }

        public override void SetMute(bool muted)
        {
            _muted = muted;
        }
    }
}