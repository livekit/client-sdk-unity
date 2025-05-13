using System;
using System.Collections;
using LiveKit.Proto;
using LiveKit.Internal;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;
using UnityEngine;

namespace LiveKit
{
    /// <summary>
    /// Defines the type of audio source, influencing processing behavior.
    /// </summary>
    public enum RtcAudioSourceType
    {
        AudioSourceCustom = 0,
        AudioSourceMicrophone = 1
    }

    public abstract class RtcAudioSource : IRtcSource, IDisposable
    {
        /// <summary>
        /// Event triggered when audio samples are captured from the underlying source.
        /// Provides the audio data, channel count, and sample rate.
        /// </summary>
        /// <remarks>
        /// This event is not guaranteed to be called on the main thread.
        /// </remarks>
        public abstract event Action<float[], int, int> AudioRead;

#if UNITY_IOS
        // iOS microphone sample rate is 24k
        public static uint DefaultMicrophoneSampleRate = 24000;

        public static uint DefaultSampleRate = 48000;
#else
        public static uint DefaultSampleRate = 48000;
        public static uint DefaultMicrophoneSampleRate = DefaultSampleRate;
#endif
        public static uint DefaultChannels = 2;

        private readonly RtcAudioSourceType _sourceType;
        public RtcAudioSourceType SourceType => _sourceType;

        internal readonly FfiHandle Handle;
        protected AudioSourceInfo _info;

        // Possibly used on the AudioThread
        private Thread _readAudioThread;
        private ThreadSafeQueue<AudioFrame> _frameQueue = new ThreadSafeQueue<AudioFrame>();

        private bool _muted = false;
        public override bool Muted => _muted;

        private bool _started = false;
        private bool _disposed = false;

        protected RtcAudioSource(int channels = 2, RtcAudioSourceType audioSourceType = RtcAudioSourceType.AudioSourceCustom)
        {
            _sourceType = audioSourceType;

            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = (uint)channels;
            newAudioSource.SampleRate = _sourceType == RtcAudioSourceType.AudioSourceMicrophone ?
                DefaultMicrophoneSampleRate : DefaultSampleRate;

            newAudioSource.Options = request.TempResource<AudioSourceOptions>();
            newAudioSource.Options.EchoCancellation = true;
            newAudioSource.Options.AutoGainControl = true;
            newAudioSource.Options.NoiseSuppression = true;
            using var response = request.Send();
            FfiResponse res = response;
            _info = res.NewAudioSource.Source.Info;
            Handle = FfiHandle.FromOwnedHandle(res.NewAudioSource.Source.Handle);
        }

        /// <summary>
        /// Begin capturing audio samples from the underlying source.
        /// </summary>
        public virtual void Start()
        {
            if (_started) return;
            _frameQueue.Clear();
            _readAudioThread = new Thread(Update);
            _readAudioThread.Start();
            AudioRead += OnAudioRead;
            _started = true;
        }

        /// <summary>
        /// Stop capturing audio samples from the underlying source.
        /// </summary>
        public virtual void Stop()
        {
            if (!_started) return;
            _readAudioThread?.Abort();
            AudioRead -= OnAudioRead;
            _started = false;
        }

        private void Update()
        {
            while (true)
            {
                ReadAudio();
                Thread.Sleep(Constants.TASK_DELAY);
            }
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            var samplesPerChannel = data.Length / channels;
            var frame = new AudioFrame((uint)sampleRate, (uint)channels, (uint)samplesPerChannel);

            static short FloatToS16(float v)
            {
                v *= 32768f;
                v = Math.Min(v, 32767f);
                v = Math.Max(v, -32768f);
                return (short)(v + Math.Sign(v) * 0.5f);
            }
            unsafe
            {
                var frameData = new Span<short>(frame.Data.ToPointer(), frame.Length / sizeof(short));
                for (int i = 0; i < data.Length; i++)
                {
                    frameData[i] = FloatToS16(data[i]);
                }
            }
            _frameQueue.Enqueue(frame);
        }

        private void ReadAudio()
        {
            while (_frameQueue.Count > 0)
            {
                try
                {
                    AudioFrame frame = _frameQueue.Dequeue();

                    if(_muted)
                    {
                        continue;
                    }
                    unsafe
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
                    }
                }
                catch (Exception e)
                {
                    Utils.Error("Audio Framedata error: " + e.Message);
                }
            }
        }

        /// <summary>
        /// Mutes or unmutes the audio source.
        /// </summary>
        public override void SetMute(bool muted)
        {
            _muted = muted;
        }

        /// <summary>
        /// Disposes of the audio source, stopping it first if necessary.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing) Stop();
            _disposed = true;
        }

        ~RtcAudioSource()
        {
            Dispose(false);
        }

        [Obsolete("No longer used, audio sources should perform any preparation in Start() asynchronously")]
        public virtual IEnumerator Prepare(float timeout = 0) { yield break; }

        [Obsolete("Use Start() instead")]
        public IEnumerator PrepareAndStart()
        {
            Start();
            yield break;
        }
    }
}
