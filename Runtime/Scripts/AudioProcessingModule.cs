using LiveKit.Proto;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Internal;
using System;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Tests")]

namespace LiveKit
{
    /// <summary>
    /// Provides WebRTC audio processing capabilities including echo cancellation, noise suppression,
    /// high-pass filtering, and gain control.
    /// </summary>
    public sealed class AudioProcessingModule
    {
        internal readonly FfiHandle Handle;

        /// <summary>
        /// Initializes an <see cref="AudioProcessingModule" /> instance with the specified audio processing features.
        /// </summary>
        /// <param name="echoCancellationEnabled">Whether to enable echo cancellation.</param>
        /// <param name="noiseSuppressionEnabled">Whether to enable noise suppression.</param>
        /// <param name="highPassFilterEnabled">Whether to enable high-pass filtering.</param>
        /// <param name="gainControllerEnabled">Whether to enable gain control.</param>
        public AudioProcessingModule(
            bool echoCancellationEnabled,
            bool noiseSuppressionEnabled,
            bool highPassFilterEnabled,
            bool gainControllerEnabled)
        {
            using var request = FFIBridge.Instance.NewRequest<NewApmRequest>();
            var newApm = request.request;
            newApm.EchoCancellerEnabled = echoCancellationEnabled;
            newApm.NoiseSuppressionEnabled = noiseSuppressionEnabled;
            newApm.HighPassFilterEnabled = highPassFilterEnabled;
            newApm.GainControllerEnabled = gainControllerEnabled;

            using var response = request.Send();
            FfiResponse res = response;
            Handle = FfiHandle.FromOwnedHandle(res.NewApm.Apm.Handle);
        }

        /// <summary>
        /// Process the provided audio frame using the configured audio processing features.
        /// </summary>
        /// <param name="data">The audio frame to process.</param>
        /// <remarks>
        /// Important: Audio frames must be exactly 10 ms in duration.
        ///
        /// The input audio frame is modified in-place (if applicable) by the underlying audio
        /// processing module (e.g., echo cancellation, noise suppression, etc.).
        /// </remarks>
        public void ProcessStream(AudioFrame data)
        {
            using var request = FFIBridge.Instance.NewRequest<ApmProcessStreamRequest>();
            var processStream = request.request;
            processStream.ApmHandle = (ulong)Handle.DangerousGetHandle();
            processStream.DataPtr = (ulong)data.Data;
            processStream.Size = (uint)data.Length;
            processStream.SampleRate = data.SampleRate;
            processStream.NumChannels = data.NumChannels;

            using var response = request.Send();
            FfiResponse res = response;
            if (res.ApmProcessStream.HasError)
            {
                throw new Exception(res.ApmProcessStream.Error);
            }
        }

        /// <summary>
        /// Process the reverse audio frame (typically used for echo cancellation in a full-duplex setup).
        /// </summary>
        /// <param name="data">The audio frame to process.</param>
        /// <remarks>
        /// Important: Audio frames must be exactly 10 ms in duration.
        ///
        /// In an echo cancellation scenario, this method is used to process the "far-end" audio
        /// prior to mixing or feeding it into the echo canceller. Like <see cref="ProcessStream"/>, the
        /// input audio frame is modified in-place by the underlying processing module.
        /// </remarks>
        public void ProcessReverseStream(AudioFrame data)
        {
            using var request = FFIBridge.Instance.NewRequest<ApmProcessReverseStreamRequest>();
            var processReverseStream = request.request;
            processReverseStream.ApmHandle = (ulong)Handle.DangerousGetHandle();
            processReverseStream.DataPtr = (ulong)data.Data;
            processReverseStream.Size = (uint)data.Length;
            processReverseStream.SampleRate = data.SampleRate;
            processReverseStream.NumChannels = data.NumChannels;

            using var response = request.Send();
            FfiResponse res = response;
            if (res.ApmProcessReverseStream.HasError)
            {
                throw new Exception(res.ApmProcessReverseStream.Error);
            }
        }

        /// <summary>
        /// This must be called if and only if echo processing is enabled.
        /// </summary>
        /// <remarks>
        /// Sets the `delay` in milliseconds between receiving a far-end frame in <see cref="ProcessReverseStream"/>
        /// and receiving the corresponding echo in a near-end frame in <see cref="ProcessStream"/>.
        ///
        /// The delay can be calculated as: delay = (t_render - t_analyze) + (t_process - t_capture)
        ///
        /// Where:
        /// - t_analyze: Time when frame is passed to <see cref="ProcessReverseStream"/>
        /// - t_render: Time when first sample of frame is rendered by audio hardware
        /// - t_capture: Time when first sample of frame is captured by audio hardware
        /// - t_process: Time when frame is passed to <see cref="ProcessStream"/>
        /// </remarks>
        public void SetStreamDelayMs(int delayMs)
        {
            using var request = FFIBridge.Instance.NewRequest<ApmSetStreamDelayRequest>();
            var setStreamDelay = request.request;
            setStreamDelay.ApmHandle = (ulong)Handle.DangerousGetHandle();
            setStreamDelay.DelayMs = delayMs;

            using var response = request.Send();
            FfiResponse res = response;
            if (res.ApmSetStreamDelay.HasError)
            {
                throw new Exception(res.ApmSetStreamDelay.Error);
            }
        }
    }
}