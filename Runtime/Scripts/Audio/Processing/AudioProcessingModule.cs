using System;
using LiveKit.Internal.FFI;
using LiveKit.Internal.FFI.Requests;
using LiveKit.Proto;

namespace LiveKit
{
    /// <summary>
    /// libwebrtc's <c>AudioProcessingModule</c> (AEC3 echo cancellation, noise suppression, gain
    /// control, high-pass filter), driven over the FFI.
    /// </summary>
    /// <remarks>
    /// Use this to run echo cancellation over a capture path that does not go through the
    /// platform audio device module (e.g. Unity's <c>Microphone</c>): feed the audio that is
    /// played out of the loudspeaker to <see cref="ProcessReverseStream"/> and the captured
    /// microphone audio to <see cref="ProcessStream"/>, which processes it in place.
    ///
    /// Both accept exactly one 10 ms chunk of interleaved int16 PCM (<see cref="FrameSizeFor"/>
    /// samples per channel) and nothing else. libwebrtc's own contract is a capture thread calling
    /// <see cref="ProcessStream"/> and a render thread calling <see cref="ProcessReverseStream"/>;
    /// the native module is internally synchronised for exactly that split, and the SDK's request
    /// plumbing is safe to use from both.
    /// </remarks>
    public sealed class AudioProcessingModule : IDisposable
    {
        /// <summary>libwebrtc's <c>kChunkSizeMs</c> — the APM accepts nothing else.</summary>
        public const int ChunkSizeMs = 10;

        /// <summary>
        /// libwebrtc's internal processing rates. The APM resamples any other API rate onto one of
        /// these itself, so this list is diagnostic information — NOT an admission requirement. See
        /// <see cref="IsSupportedApiRate"/>.
        /// </summary>
        private static readonly int[] NativeSampleRates = { 8000, 16000, 32000, 48000 };

        private readonly FfiHandle _handle;
        private bool _disposed;

        /// <summary>The native handle id, for diagnostics.</summary>
        public ulong Handle => (ulong)_handle.DangerousGetHandle();

        public AudioProcessingModule(
            bool echoCancellerEnabled,
            bool gainControllerEnabled,
            bool highPassFilterEnabled,
            bool noiseSuppressionEnabled)
        {
            using var request = FFIBridge.Instance.NewRequest<NewApmRequest>();
            var newApm = request.request;
            newApm.EchoCancellerEnabled = echoCancellerEnabled;
            newApm.GainControllerEnabled = gainControllerEnabled;
            newApm.HighPassFilterEnabled = highPassFilterEnabled;
            newApm.NoiseSuppressionEnabled = noiseSuppressionEnabled;

            using var response = request.Send();
            FfiResponse res = response;
            var owned = res.NewApm?.Apm;
            if (owned?.Handle == null || owned.Handle.Id == 0)
                throw new InvalidOperationException("FFI returned no APM handle");

            _handle = FfiHandle.FromOwnedHandle(owned.Handle);
        }

        public static bool IsNativeSampleRate(int sampleRate)
        {
            foreach (var rate in NativeSampleRates)
                if (rate == sampleRate) return true;
            return false;
        }

        /// <summary>
        /// Whether the APM accepts this rate on its API surface.
        /// </summary>
        /// <remarks>
        /// The only hard requirement is that one 10 ms chunk is a whole number of samples: both
        /// <see cref="FrameSizeFor"/> here and libwebrtc's own <c>StreamConfig::num_frames()</c>
        /// derive the chunk with integer division, so a rate that is not a multiple of 100 Hz would
        /// short every chunk and drift the two feeds apart.
        ///
        /// A non-native rate is NOT rejected — the rate goes straight into a <c>StreamConfig</c>
        /// and libwebrtc resamples to a native processing rate internally. A 24 kHz output rate
        /// (iPad) is cancelled just as well as 48 kHz.
        /// </remarks>
        public static bool IsSupportedApiRate(int sampleRate) =>
            sampleRate > 0 && sampleRate % (1000 / ChunkSizeMs) == 0;

        /// <summary>Samples per channel in one APM chunk at the given rate.</summary>
        public static int FrameSizeFor(int sampleRate) => sampleRate / (1000 / ChunkSizeMs);

        /// <summary>
        /// Processes the near-end (capture) stream in place. <paramref name="byteCount"/> is bytes,
        /// not samples — the buffer is interleaved int16. Returns the FFI error, or null on success.
        /// </summary>
        public string ProcessStream(IntPtr dataPtr, int byteCount, int sampleRate, int channels)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AudioProcessingModule));

            using var request = FFIBridge.Instance.NewRequest<ApmProcessStreamRequest>();
            var process = request.request;
            process.ApmHandle = Handle;
            process.DataPtr = (ulong)dataPtr.ToInt64();
            process.Size = (uint)byteCount;
            process.SampleRate = (uint)sampleRate;
            process.NumChannels = (uint)channels;

            using var response = request.Send();
            FfiResponse res = response;
            return ErrorOrNull(res.ApmProcessStream?.Error);
        }

        /// <summary>
        /// Processes the far-end (render) reference stream in place. Same buffer contract as
        /// <see cref="ProcessStream"/>.
        /// </summary>
        public string ProcessReverseStream(IntPtr dataPtr, int byteCount, int sampleRate, int channels)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AudioProcessingModule));

            using var request = FFIBridge.Instance.NewRequest<ApmProcessReverseStreamRequest>();
            var reverse = request.request;
            reverse.ApmHandle = Handle;
            reverse.DataPtr = (ulong)dataPtr.ToInt64();
            reverse.Size = (uint)byteCount;
            reverse.SampleRate = (uint)sampleRate;
            reverse.NumChannels = (uint)channels;

            using var response = request.Send();
            FfiResponse res = response;
            return ErrorOrNull(res.ApmProcessReverseStream?.Error);
        }

        /// <summary>
        /// Seeds the render/capture delay. AEC3 runs its own correlation estimator, so this is a
        /// convergence hint rather than a hard alignment. Returns the FFI error, or null on success.
        /// </summary>
        public string SetStreamDelayMs(int delayMs)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AudioProcessingModule));

            using var request = FFIBridge.Instance.NewRequest<ApmSetStreamDelayRequest>();
            var delay = request.request;
            delay.ApmHandle = Handle;
            delay.DelayMs = delayMs;

            using var response = request.Send();
            FfiResponse res = response;
            return ErrorOrNull(res.ApmSetStreamDelay?.Error);
        }

        private static string ErrorOrNull(string error) => string.IsNullOrEmpty(error) ? null : error;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _handle.Dispose();
            GC.SuppressFinalize(this);
        }

        ~AudioProcessingModule()
        {
            if (_disposed) return;
            _disposed = true;
            _handle.Dispose();
        }
    }
}
