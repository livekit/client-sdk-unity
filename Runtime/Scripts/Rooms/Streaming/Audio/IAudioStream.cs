using System;

namespace LiveKit.Rooms.Streaming.Audio
{
    public interface IAudioStream : IDisposable
    {
        /// <summary>
        /// Supposed to be called from Unity's audio thread.
        /// </summary>
        /// <returns>buffer filled - true or false.</returns>
        void ReadAudio(float[] data, int channels, int sampleRate);
    }
}