using System.Threading;
using LiveKit.Internal;
using UnityEngine;

namespace LiveKit
{
    /// <summary>
    /// Captures and processes the reverse audio stream using an <see cref="AudioProcessingModule"/>.
    /// </summary>
    /// <remarks>
    /// The reverse stream is captured from the scene's audio listener.
    /// </remarks>
    internal class ApmReverseStream
    {
        private readonly AudioBuffer _captureBuffer = new AudioBuffer();
        private readonly AudioProcessingModule _apm; // APM is thread safe
        private Thread _thread;
        private AudioFilter _audioFilter;

        internal ApmReverseStream(AudioProcessingModule apm)
        {
            _apm = apm;
        }

        internal void Start()
        {
            var audioListener = GameObject.FindObjectOfType<AudioListener>();
            if (audioListener == null)
            {
                Utils.Error("AudioListener not found in scene");
                return;
            }
            _audioFilter = audioListener.gameObject.AddComponent<AudioFilter>();
            _audioFilter.AudioRead += OnAudioRead;

            _thread = new Thread(ProcessReverseStream);
            _thread.Start();
        }

        internal void Stop()
        {
            _thread?.Abort();
            if (_audioFilter != null)
                Object.Destroy(_audioFilter);
        }

        private void ProcessReverseStream()
        {
            while (true)
            {
                Thread.Sleep(Constants.TASK_DELAY);
                using var frame = _captureBuffer.ReadDuration(AudioProcessingModule.FRAME_DURATION_MS);
                if (frame == null) continue;
                _apm.ProcessReverseStream(frame);
            }
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            _captureBuffer.Write(data, (uint)channels, (uint)sampleRate);
        }
    }
}