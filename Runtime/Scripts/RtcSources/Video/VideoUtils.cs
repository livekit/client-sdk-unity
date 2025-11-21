using System;
using LiveKit.Proto;
using UnityEngine;

namespace LiveKit.RtcSources.Video
{
    public static class VideoUtils
    {
        public static TextureFormat TextureFormatFromVideoBufferType(VideoBufferType type) =>
            // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
            type switch
            {
                VideoBufferType.Rgba => TextureFormat.RGBA32,
                VideoBufferType.Argb => TextureFormat.ARGB32,
                VideoBufferType.Bgra => TextureFormat.BGRA32,
                VideoBufferType.Rgb24 => TextureFormat.RGB24,
                _ => throw new NotImplementedException("TODO: Add TextureFormat support for type: " + type)
            };

        public static int StrideFromVideoBufferType(VideoBufferType type) =>
            type switch
            {
                VideoBufferType.Rgba or VideoBufferType.Argb or VideoBufferType.Bgra => 4,
                VideoBufferType.Rgb24 => 3,
                _ => throw new NotImplementedException("TODO: Add stride support for type: " + type)
            };

        public static RenderTextureFormat RenderTextureFormatFrom(TextureFormat format) =>
            format switch
            {
                TextureFormat.Alpha8 => RenderTextureFormat.R8,
                TextureFormat.R8 => RenderTextureFormat.R8,
                TextureFormat.R16 => RenderTextureFormat.R16,
                TextureFormat.RG16 => RenderTextureFormat.RG16,
                TextureFormat.RG32 => RenderTextureFormat.RG32,
                TextureFormat.RGBA32 => RenderTextureFormat.ARGB32, // hack, no true rgba32
                TextureFormat.ARGB32 => RenderTextureFormat.ARGB32,
                TextureFormat.BGRA32 =>
// hack, BGRA doesn't support read on MacOS :'B8G8R8A8_UNorm' doesn't support ReadPixels usage on this platform. Async GPU readback failed.
                    RenderTextureFormat.ARGB32,
                TextureFormat.ARGB4444 or TextureFormat.RGBA4444 =>
                    RenderTextureFormat.ARGB4444, // hack, only this 4444 format

                TextureFormat.RGB565 => RenderTextureFormat.RGB565,
                TextureFormat.RHalf => RenderTextureFormat.RHalf,
                TextureFormat.RGHalf => RenderTextureFormat.RGHalf,
                TextureFormat.RGBAHalf => RenderTextureFormat.ARGBHalf,
                TextureFormat.RFloat => RenderTextureFormat.RFloat,
                TextureFormat.RGFloat => RenderTextureFormat.RGFloat,
                TextureFormat.RGBAFloat => RenderTextureFormat.ARGBFloat,
                TextureFormat.RGB48 => RenderTextureFormat.ARGB64,
                TextureFormat.RGBA64 => RenderTextureFormat.ARGB64,
                // Compressed / special formats: no direct RT support
                _ => throw new NotSupportedException($"Format not supported: {format.ToString()}"),
            };

        public static int BytesPerPixel(RenderTextureFormat format) =>
            format switch
            {
                RenderTextureFormat.ARGB32 or RenderTextureFormat.BGRA32 => // R8G8B8A8
                    4,
                RenderTextureFormat.RGB565 or RenderTextureFormat.ARGB4444 or RenderTextureFormat.R16 => // 16-bit
                    // 16-bit
                    2,
                RenderTextureFormat.R8 => 1,
                RenderTextureFormat.RHalf => // 16-bit float
                    2,
                RenderTextureFormat.RGHalf => // 2×16-bit float
                    4,
                RenderTextureFormat.ARGBHalf => // 4×16-bit float
                    8,
                RenderTextureFormat.RFloat => // 32-bit float
                    4,
                RenderTextureFormat.RGFloat => // 2×32-bit float
                    8,
                RenderTextureFormat.ARGBFloat => // 4×32-bit float
                    16,
                RenderTextureFormat.RGB111110Float => // packed 3×10-bit floats
                    4,
                RenderTextureFormat.ARGB64 => // 16 bits ×4
                    8,
                RenderTextureFormat.RG16 => // 16 bits ×2
                    4,
                RenderTextureFormat.RG32 => // 32 bits ×2
                    8,
                _ => throw new Exception($"BytesPerPixel not defined for {format}, falling back to 4 (ARGB32).")
            };
    }
}