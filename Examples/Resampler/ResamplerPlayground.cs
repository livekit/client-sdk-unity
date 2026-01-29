#if !UNITY_WEBGL

using System.IO;
using System.Text;
using LiveKit.Audio;
using LiveKit.Internal;
using LiveKit.Rooms.Streaming.Audio;
using RichTypes;
using UnityEngine;

namespace Examples.Resampler
{
    public class ResamplerPlayground : MonoBehaviour
    {
        [SerializeField] private string originalWavPath = string.Empty;
        [SerializeField] private int numChannels;
        [SerializeField] private int sampleRate;

        [SerializeField] private string outputWavPath = string.Empty;
        [SerializeField] private int targetNumChannels;
        [SerializeField] private int targetSampleRate;

        [ContextMenu(nameof(Process))]
        public void Process()
        {
            Result<WavWriter> wavResult = WavWriter.NewFromPath(outputWavPath);
            if (wavResult.Success == false)
            {
                Debug.LogError($"Cannot create wav writer: {wavResult.ErrorMessage}");
                return;
            }

            WavWriter wavWriter = wavResult.Value;
            using FileStream original = new FileStream(originalWavPath, FileMode.Open, FileAccess.Read);
            using BinaryReader binaryReader = new BinaryReader(original, Encoding.Default, true);
            using AudioResampler resampler = AudioResampler.New();

            // Read frame for 10 ms, 100 stands for 1000 / 100 = 10 ms sample rate, int16 = PCM sample size
            uint samplesPerChannelFor10Ms = (uint)sampleRate / 100;
            using AudioFrame audioFrame = new AudioFrame((uint)sampleRate, (uint)numChannels, samplesPerChannelFor10Ms);

            int readAmount = 0;
            while ((readAmount = original.Read(audioFrame.AsSpan())) != 0)
            {
                using OwnedAudioFrame outputFrame = resampler.RemixAndResample(
                    audioFrame,
                    (uint)targetNumChannels,
                    (uint)targetSampleRate
                );

                wavWriter.Write(outputFrame);
            }

            Debug.Log($"Write complete: {outputWavPath}");
            wavWriter.Dispose();
        }
    }
}

#endif
