using System;

namespace LiveKit.PlayModeTests.Utils
{
    /// <summary>
    /// A programmatic audio source for testing. Allows pushing audio frames
    /// directly without requiring a Unity AudioSource or microphone.
    /// </summary>
    public class TestAudioSource : RtcAudioSource
    {
        public override event Action<float[], int, int> AudioRead;

        public TestAudioSource(int channels = 1)
            : base(channels, RtcAudioSourceType.AudioSourceCustom) { }

        /// <summary>
        /// Push an audio frame into the FFI capture pipeline.
        /// Must call Start() first so the base class is subscribed to AudioRead.
        /// </summary>
        public void PushFrame(float[] data, int channels, int sampleRate)
        {
            AudioRead?.Invoke(data, channels, sampleRate);
        }
    }
}
