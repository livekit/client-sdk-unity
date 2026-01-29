#if !UNITY_WEBGL

using System;
using LiveKit.Internal;

namespace LiveKit
{
    /// <summary>
    ///     Interface for RTC audio sources that can capture and stream audio data
    /// </summary>
    public interface IRtcAudioSource : IDisposable
    {
        /// <summary>
        /// Borrow ownership of native resources
        /// </summary>
        /// <returns></returns>
        FfiHandle BorrowHandle();

        void Start();

        void Stop();
    }
}

#endif
