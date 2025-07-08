using System;
using LiveKit.Internal;

namespace LiveKit
{
    /// <summary>
    ///     Interface for RTC audio sources that can capture and stream audio data
    /// </summary>
    public interface IRtcAudioSource
    {
        /// <summary>
        ///     Internal handle for FFI communication
        /// </summary>
        FfiHandle Handle { get; }

        void Start();

        void Stop();
    }
}