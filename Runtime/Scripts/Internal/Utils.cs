using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using LiveKit.Proto;
using System.Collections.Generic;

namespace LiveKit.Internal
{
    /// <summary>
    /// The class <c>Utils</c> contains internal utilities used for FfiClient
    /// The log part is useful to print messages only when "LK_DEBUG" is defined.
    /// </summary>
    internal static class Utils
    {
        private const string PREFIX = "LiveKit";
        private const string LK_DEBUG = "LK_DEBUG";

        /// <summary>
        /// Log a message at the info level.
        /// </summary>
        public static void Info(object msg)
        {
            UnityEngine.Debug.unityLogger.Log(PREFIX, msg);
        }

        /// <summary>
        /// Log a message at the error level.
        /// </summary>
        public static void Error(object msg)
        {
            UnityEngine.Debug.unityLogger.LogError(PREFIX, msg);
        }

        /// <summary>
        /// Log a message at the warning level.
        /// </summary>
        public static void Warning(object msg)
        {
            UnityEngine.Debug.unityLogger.LogWarning(PREFIX, msg);
        }

        /// <summary>
        /// Log a message at the debug level.
        /// </summary>
        [Conditional(LK_DEBUG)]
        public static void Debug(object msg)
        {
            UnityEngine.Debug.unityLogger.Log(PREFIX, msg);
        }

        // <summary>
        /// Forwards a log batch received over FFI to the Unity logging system.
        /// </summary>
        internal static void HandleLogBatch(LogBatch batch)
        {
            if (batch == null) return;
            foreach (var record in batch.Records)
            {
                if (record == null || !record.HasMessage || string.IsNullOrEmpty(record.Message)) continue;
                var formatted = FormatLogMessage(record);
                switch (record.Level)
                {
                    case LogLevel.LogError:
                        Error(formatted);
                        break;
                    case LogLevel.LogWarn:
                        Warning(formatted);
                        break;
                    case LogLevel.LogInfo:
                        Info(formatted);
                        break;
                    case LogLevel.LogDebug:
                    case LogLevel.LogTrace:
                        Debug(formatted);
                        break;
                    default:
                        Info(formatted);
                        break;
                }
            }
        }

        private static string FormatLogMessage(LogRecord record)
        {
            var parts = new List<string> {  record.Message };
            if (record.HasFile && !string.IsNullOrEmpty(record.File))
            {
                var location = record.HasLine && record.Line > 0
                    ? $"{record.File}:{record.Line}"
                    : record.File;
                parts.Add($"Source: {location} (FFI)");
            }
            if (record.HasModulePath && !string.IsNullOrEmpty(record.ModulePath))
                parts.Add($"Module: {record.ModulePath}");

            if (record.HasTarget && !string.IsNullOrEmpty(record.Target))
                parts.Add($"Target: {record.Target}");

            return string.Join("\n", parts);
        }

        public static GraphicsFormat GetSupportedGraphicsFormat(GraphicsDeviceType type)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                switch (type)
                {
                    case GraphicsDeviceType.Direct3D11:
                    case GraphicsDeviceType.Direct3D12:
                    case GraphicsDeviceType.Vulkan:
                        return GraphicsFormat.B8G8R8A8_SRGB;
                    case GraphicsDeviceType.OpenGLCore:
                    case GraphicsDeviceType.OpenGLES2:
                    case GraphicsDeviceType.OpenGLES3:
                        return GraphicsFormat.R8G8B8A8_SRGB;
                    case GraphicsDeviceType.Metal:
                        return GraphicsFormat.B8G8R8A8_SRGB;
                }
            }
            else
            {
                switch (type)
                {
                    case GraphicsDeviceType.Vulkan:
                        return GraphicsFormat.B8G8R8A8_UNorm;
                    case GraphicsDeviceType.Direct3D12: // Gamma and 3D12 required R8
                    case GraphicsDeviceType.Direct3D11: // Gamma and 3D11 required R8
                    case GraphicsDeviceType.OpenGLCore:
                    case GraphicsDeviceType.OpenGLES2:
                    case GraphicsDeviceType.OpenGLES3:
                        return GraphicsFormat.R8G8B8A8_UNorm;
                    case GraphicsDeviceType.Metal:
                        return GraphicsFormat.R8G8B8A8_UNorm;
                }
            }

            throw new ArgumentException($"Graphics device type {type} not supported");
        }
    }
}
