using System;
using System.Threading;

namespace LiveKit
{
    /// <summary>
    /// Test-only <see cref="RtcAudioSource"/> that emits a deterministic sine wave.
    /// Uses a <see cref="Timer"/> to push 20ms PCM frames via the base class's
    /// FFI capture path; avoids the Unity audio subsystem entirely so it works in
    /// batch-mode test runners.
    /// </summary>
    public sealed class SineWaveAudioSource : RtcAudioSource
    {
        public override event Action<float[], int, int> AudioRead;

        private const int FrameDurationMs = 20;

        private readonly int _channels;
        private readonly int _sampleRate;
        private readonly int _samplesPerFrame;
        private readonly double _phaseIncrement;
        private readonly float _amplitude;

        private Timer _timer;
        private double _phase;
        private bool _running;
        private bool _disposed;

        public SineWaveAudioSource(
            int channels = 2,
            int sampleRate = 48000,
            double frequencyHz = 440.0,
            float amplitude = 0.1f)
            : base(channels, RtcAudioSourceType.AudioSourceCustom)
        {
            _channels = channels;
            _sampleRate = sampleRate;
            _samplesPerFrame = sampleRate * FrameDurationMs / 1000;
            _phaseIncrement = 2.0 * Math.PI * frequencyHz / sampleRate;
            _amplitude = amplitude;
        }

        public override void Start()
        {
            base.Start();
            if (_running || _disposed) return;
            _running = true;
            _timer = new Timer(PumpFrame, null, 0, FrameDurationMs);
        }

        public override void Stop()
        {
            base.Stop();
            if (!_running) return;
            _running = false;
            _timer?.Dispose();
            _timer = null;
        }

        private void PumpFrame(object _)
        {
            if (!_running || _disposed) return;
            try
            {
                var buffer = new float[_samplesPerFrame * _channels];
                for (int i = 0; i < _samplesPerFrame; i++)
                {
                    var sample = (float)(_amplitude * Math.Sin(_phase));
                    _phase += _phaseIncrement;
                    if (_phase >= 2.0 * Math.PI) _phase -= 2.0 * Math.PI;
                    for (int c = 0; c < _channels; c++)
                        buffer[i * _channels + c] = sample;
                }
                AudioRead?.Invoke(buffer, _channels, _sampleRate);
            }
            catch
            {
                // Timer fires independently of FFI lifecycle; swallow errors during teardown.
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing) Stop();
            _disposed = true;
            base.Dispose(disposing);
        }

        ~SineWaveAudioSource()
        {
            Dispose(false);
        }
    }
}
