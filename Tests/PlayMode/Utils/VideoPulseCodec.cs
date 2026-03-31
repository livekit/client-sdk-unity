using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LiveKit;

namespace LiveKit.PlayModeTests.Utils
{
    /// <summary>
    /// Encodes and decodes pulse indices for video latency measurement using
    /// spatial binary encoding. The frame is divided into vertical strips,
    /// each either black or white, representing bits of the pulse index.
    /// </summary>
    public static class VideoPulseCodec
    {
        /// <summary>
        /// Encode a pulse index for use with <see cref="TestVideoSource.SetPulseIndex"/>.
        /// Returns a 1-based encoded index so that 0 (all black) means "no pulse".
        /// Pass -1 to clear (send black frames between pulses).
        /// </summary>
        public static int Encode(int pulseIndex)
        {
            return pulseIndex < 0 ? -1 : pulseIndex + 1;
        }

        /// <summary>
        /// Decode a pulse index from a received I420 video frame buffer by sampling
        /// the Y plane at the center of each vertical strip.
        /// Returns the 0-based pulse index, or -1 if no pulse is detected.
        /// </summary>
        public static (int, long) Decode(VideoFrameBuffer buffer, int stripThreshold = 128)
        {
            long receiveTimeTicks = Stopwatch.GetTimestamp();
            
            if (buffer == null || !buffer.IsValid)
                return (-1, 0);

            if (buffer.Info.Components.Count == 0)
                return (-1, 0);

            var yComponent = buffer.Info.Components[0];
            if (!yComponent.HasDataPtr)
                return (-1, 0);

            var yPtr = (IntPtr)yComponent.DataPtr;
            int width = (int)buffer.Width;
            int height = (int)buffer.Height;
            int stripWidth = width / TestVideoSource.NumStrips;
            int[] sampleRows = { height / 4, height / 2, height * 3 / 4 };

            int decodedIndex = 0;
            for (int strip = 0; strip < TestVideoSource.NumStrips; strip++)
            {
                int centerX = strip * stripWidth + stripWidth / 2;
                int ySum = 0;
                foreach (int row in sampleRows)
                {
                    int offset = row * width + centerX;
                    ySum += Marshal.ReadByte(yPtr, offset);
                }
                int avgY = ySum / sampleRows.Length;

                if (avgY > stripThreshold)
                    decodedIndex |= (1 << strip);
            }

            // 0 = all black = no pulse; otherwise subtract 1 to get 0-based pulse index
            if (decodedIndex == 0)
                return (-1, 0);
            else
                return (decodedIndex - 1, receiveTimeTicks);
        }
    }
}
