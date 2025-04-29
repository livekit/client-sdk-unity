using System;
using System.Collections;
using LiveKit.Proto;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using UnityEngine;

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
        private AudioBuffer _captureBuffer = new AudioBuffer();
        private readonly AudioProcessingModule _apm;
        private readonly ApmReverseStream _apmReverseStream;

        private bool _muted = false;
        public override bool Muted => _muted;

        protected RtcAudioSource(int channels = 2, RtcAudioSourceType audioSourceType = RtcAudioSourceType.AudioSourceCustom)
        {
            _sourceType = audioSourceType;

            var isMicrophone = audioSourceType == RtcAudioSourceType.AudioSourceMicrophone;
            _apm = new AudioProcessingModule(isMicrophone, true, true, true);
            if (isMicrophone)
            {
                _apmReverseStream = new ApmReverseStream(_apm);
                _apm.SetStreamDelayMs(EstimateStreamDelayMs());
            }

            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = (uint)channels;
            newAudioSource.SampleRate = isMicrophone ? DefaultMirophoneSampleRate : DefaultSampleRate;

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
            _apmReverseStream?.Start();
            AudioRead += OnAudioRead;
            Play();
        }

        public virtual void Stop()
        {
            _apmReverseStream?.Stop();
            AudioRead -= OnAudioRead;
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            _captureBuffer.Write(data, (uint)channels, (uint)sampleRate);
            while (true)
            {
                var frame = _captureBuffer.ReadDuration(AudioProcessingModule.FRAME_DURATION_MS);
                if (_muted || frame == null) break;

                if (_apm != null) _apm.ProcessStream(frame);
                Capture(frame);
            }
            // Don't play the audio locally, to avoid echo.
            if (_sourceType == RtcAudioSourceType.AudioSourceMicrophone)
                Array.Clear(data, 0, data.Length);
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

        private int EstimateStreamDelayMs()
        {
            // TODO: estimate more accurately
            int bufferLength, numBuffers;
            int sampleRate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out bufferLength, out numBuffers);
            return 2 * (int)(1000f * bufferLength * numBuffers / sampleRate);
        }
    }
}