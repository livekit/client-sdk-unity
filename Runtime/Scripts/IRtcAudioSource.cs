using LiveKit.Internal;

namespace LiveKit
{
    /// <summary>
    ///     Interface for RTC audio sources that can capture and stream audio data
    /// </summary>
    public interface IRtcAudioSource
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