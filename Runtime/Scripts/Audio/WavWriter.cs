using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using LiveKit.Internal;
using RichTypes;

namespace LiveKit.Audio
{
    /// <summary>
    /// WavWriter must be disposed to proper completition
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public struct WavWriter : IDisposable
    {
        private static readonly byte[] RIFF = { (byte)'R', (byte)'I', (byte)'F', (byte)'F' };
        private static readonly byte[] WAVE = { (byte)'W', (byte)'A', (byte)'V', (byte)'E' };
        private static readonly byte[] FMT = { (byte)'f', (byte)'m', (byte)'t', (byte)' ' };
        private static readonly byte[] DATA = { (byte)'d', (byte)'a', (byte)'t', (byte)'a' };

        private const short PCM_FORMAT_TAG = 1;
        private const short BITS_PER_SAMPLE = 16;
        private const int BYTES_PER_SAMPLE = BITS_PER_SAMPLE / 8; // 2
        private const int HEADER_SIZE = 44;
        private const int FMT_CHUNK_SIZE = 16;

        private readonly Stream outputStream;
        private long dataSize;
        private uint sampleRate;
        private ushort channels;
        private bool notDisposed; // 'not' for empty creation via default ctor

        private WavWriter(Stream outputStream)
        {
            this.outputStream = outputStream;
            dataSize = 0;
            sampleRate = 0;
            channels = 0;
            notDisposed = true;
        }

        public static Result<WavWriter> NewFromStream(Stream outputStream)
        {
            if (outputStream.CanSeek == false)
                Result<WavWriter>.ErrorResult("Output stream must be seekable to finalize WAV header");

            return Result<WavWriter>.SuccessResult(new WavWriter(outputStream));
        }

        public static Result<WavWriter> NewFromPath(string filePath)
        {
            try
            {
                string? dir = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(dir!) == false && Directory.Exists(dir) == false)
                {
                    Directory.CreateDirectory(dir);
                }

                FileStream fs = new(
                    filePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: false
                );

                return NewFromStream(fs);
            }
            catch (Exception e)
            {
                return Result<WavWriter>.ErrorResult($"Failed to create WAV file at '{filePath}': {e.Message}");
            }
        }


        public Result Write<TFrame>(TFrame frame) where TFrame : IAudioFrame
        {
            if (frame.Disposed)
            {
                return Result.ErrorResult("Frame is already disposed");
            }

            return Write(frame.AsPCMSampleSpan(), frame.NumChannels, frame.SampleRate);
        }

        public Result Write(ReadOnlySpan<PCMSample> samples, uint channels, uint sampleRate)
        {
            if (IsDisposed())
            {
                return Result.ErrorResult("WavWriter is already disposed");
            }

            // First write: reserve header & lock format
            if (IsStreamEmpty())
            {
                this.sampleRate = sampleRate;
                this.channels = (ushort)channels;
                outputStream.Write(stackalloc byte[HEADER_SIZE]);
            }
            else if (this.sampleRate != sampleRate || this.channels != channels)
            {
                return Result.ErrorResult("All frames must have same format (channels, sample rate)");
            }

            ReadOnlySpan<byte> span = MemoryMarshal.AsBytes(samples);
            outputStream.Write(span);
            dataSize += span.Length;
            return Result.SuccessResult();
        }

        private readonly bool IsStreamEmpty()
        {
            return outputStream.Position == 0;
        }

        public readonly bool IsDisposed()
        {
            return notDisposed == false;
        }

        private void FinalizeHeader()
        {
            if (dataSize == 0) return;

            int byteRate = (int)(sampleRate * channels * BYTES_PER_SAMPLE);
            int blockAlign = channels * BYTES_PER_SAMPLE;
            int riffSize = (int)(36 + dataSize);

            try
            {
                outputStream.Seek(0, SeekOrigin.Begin);
                using var bw = new BinaryWriter(outputStream, System.Text.Encoding.ASCII, leaveOpen: true);

                bw.Write(RIFF);
                bw.Write(riffSize);
                bw.Write(WAVE);
                bw.Write(FMT);
                bw.Write(FMT_CHUNK_SIZE);
                bw.Write(PCM_FORMAT_TAG);
                bw.Write(channels);
                bw.Write(sampleRate);
                bw.Write(byteRate);
                bw.Write((short)blockAlign);
                bw.Write(BITS_PER_SAMPLE);
                bw.Write(DATA);
                bw.Write((int)dataSize);
            }
            catch (Exception e)
            {
                Utils.Error($"Cannot instantiate BinaryWriter: {e.Message}");
            }
        }

        public void Dispose()
        {
            if (IsDisposed())
            {
                Utils.Error("WavWriter is already disposed");
                return;
            }

            notDisposed = false;
            FinalizeHeader();
            outputStream.Dispose();
        }
    }
}