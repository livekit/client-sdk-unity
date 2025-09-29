using System;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;
using RichTypes;
using UnityEngine;

namespace LiveKit.Audio
{
    public class Apm : IDisposable
    {
        private readonly FfiHandle apmHandle;
        private bool disposed;

        public Apm(
            bool echoCancellerEnabled,
            bool noiseSuppressionEnabled,
            bool gainControllerEnabled,
            bool highPassFilterEnabled)
        {
            using var apmRequest = FFIBridge.Instance.NewRequest<NewApmRequest>();
            apmRequest.request.EchoCancellerEnabled = echoCancellerEnabled;
            apmRequest.request.NoiseSuppressionEnabled = noiseSuppressionEnabled;
            apmRequest.request.GainControllerEnabled = gainControllerEnabled;
            apmRequest.request.HighPassFilterEnabled = highPassFilterEnabled;

            using var response = apmRequest.Send();
            FfiResponse apmResponse = response;
            apmHandle = IFfiHandleFactory.Default.NewFfiHandle(apmResponse.NewApm.Apm.Handle.Id);
        }

        public static Apm NewDefault()
        {
            return new Apm(true, true, true, true);
        }

        public void Dispose()
        {
            lock (this)
            {
                if (disposed)
                    return;

                apmHandle.Dispose();
                disposed = true;
            }
        }

        /// <summary>
        /// Processes the stream that goes from far end and is played by speaker
        /// </summary>
        public Result ProcessReverseStream(ApmFrame frame)
        {
            lock (this)
            {
                if (disposed)
                    return Result.ErrorResult("APM instance is already disposed");

                unsafe
                {
                    fixed (void* ptr = frame.data)
                    {
                        using var apmRequest =
                            FFIBridge.Instance.NewRequest<LiveKit.Proto.ApmProcessReverseStreamRequest>();
                        apmRequest.request.ApmHandle = (ulong)apmHandle.DangerousGetHandle().ToInt64();
                        apmRequest.request.DataPtr = new UIntPtr(ptr).ToUInt64();
                        apmRequest.request.NumChannels = frame.numChannels;
                        apmRequest.request.SampleRate = frame.sampleRate.valueHz;
                        apmRequest.request.Size = frame.SizeInBytes;

                        using var wrap = apmRequest.Send();
                        FfiResponse response = wrap;
                        var streamResponse = response.ApmProcessReverseStream;

                        if (streamResponse.HasError)
                            Result.ErrorResult($"Cannot {nameof(ProcessReverseStream)} due error: {streamResponse.Error}");

                        return Result.SuccessResult();
                    }
                }
            }
        }

        /// <summary>
        /// Processes the stream that goes from microphone
        /// </summary>
        public Result ProcessStream(ApmFrame apmFrame)
        {
            lock (this)
            {
                if (disposed)
                    return Result.ErrorResult("APM instance is already disposed");

                unsafe
                {
                    fixed (void* ptr = apmFrame.data)
                    {
                        using var apmRequest = FFIBridge.Instance.NewRequest<LiveKit.Proto.ApmProcessStreamRequest>();
                        apmRequest.request.ApmHandle = (ulong)apmHandle.DangerousGetHandle().ToInt64();
                        apmRequest.request.DataPtr = new UIntPtr(ptr).ToUInt64();
                        apmRequest.request.NumChannels = apmFrame.numChannels;
                        apmRequest.request.SampleRate = apmFrame.sampleRate.valueHz;
                        apmRequest.request.Size = apmFrame.SizeInBytes;

                        using var wrap = apmRequest.Send();
                        FfiResponse response = wrap;
                        var streamResponse = response.ApmProcessStream;

                        if (streamResponse.HasError)
                            Result.ErrorResult($"Cannot {nameof(ProcessStream)} due error: {streamResponse.Error}");

                        return Result.SuccessResult();
                    }
                }
            }
        }

        public Result SetStreamDelay(int delayMs)
        {
            lock (this)
            {
                if (disposed)
                    return Result.ErrorResult("APM instance is already disposed");

                using var apmRequest = FFIBridge.Instance.NewRequest<LiveKit.Proto.ApmSetStreamDelayRequest>();
                apmRequest.request.ApmHandle = (ulong)apmHandle.DangerousGetHandle().ToInt64();
                apmRequest.request.DelayMs = delayMs;

                using var wrap = apmRequest.Send();
                FfiResponse response = wrap;
                var delayResponse = response.ApmSetStreamDelay;

                if (delayResponse.HasError)
                    return Result.ErrorResult($"Cannot {nameof(SetStreamDelay)} due error: {delayResponse.Error}");

                return Result.SuccessResult();
            }
        }

        public static int EstimateStreamDelayMs()
        {
            // TODO: estimate more accurately
            int sampleRate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out var bufferLength, out var numBuffers);
            return 2 * (int)(1000f * bufferLength * numBuffers / sampleRate);
        }
    }
}