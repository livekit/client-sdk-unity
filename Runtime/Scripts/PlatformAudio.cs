using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LiveKit.Proto;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit
{
#if UNITY_IOS && !UNITY_EDITOR
    internal static class IOSAudioSessionHelper
    {
        /// <summary>
        /// Configures the iOS audio session for VoIP/WebRTC.
        /// Must be called before creating PlatformAudio.
        /// </summary>
        [DllImport("__Internal")]
        internal static extern void LiveKit_ConfigureAudioSessionForVoIP();

        /// <summary>
        /// Restores the iOS audio session to ambient mode.
        /// </summary>
        [DllImport("__Internal")]
        internal static extern void LiveKit_RestoreDefaultAudioSession();
    }
#endif

    /// <summary>
    /// Information about an audio device (microphone or speaker).
    /// </summary>
    public struct AudioDevice
    {
        /// <summary>Device index (0-based).</summary>
        public uint Index;
        /// <summary>Device name as reported by the operating system.</summary>
        public string Name;
    }

    /// <summary>
    /// Platform audio device management using WebRTC's Audio Device Module (ADM).
    ///
    /// PlatformAudio provides access to the platform's audio devices (microphones and
    /// speakers) and enables automatic audio capture and playback through WebRTC's ADM.
    ///
    /// Key features:
    /// - Echo cancellation (AEC)
    /// - Automatic gain control (AGC)
    /// - Noise suppression (NS)
    /// - Automatic speaker playout for remote audio
    ///
    /// Usage:
    /// 1. Create a PlatformAudio instance (enables ADM)
    /// 2. Optionally enumerate and select devices
    /// 3. Create audio tracks using PlatformAudioSource
    /// 4. Remote audio automatically plays through speakers
    /// </summary>
    /// <example>
    /// <code>
    /// // Create PlatformAudio (enables ADM)
    /// var platformAudio = new PlatformAudio();
    ///
    /// // Enumerate devices
    /// var (recording, playout) = platformAudio.GetDevices();
    /// foreach (var device in recording)
    ///     Debug.Log($"Mic {device.Index}: {device.Name}");
    ///
    /// // Select devices
    /// platformAudio.SetRecordingDevice(0);
    /// platformAudio.SetPlayoutDevice(0);
    ///
    /// // Create audio source and track
    /// var source = new PlatformAudioSource(platformAudio);
    /// var track = LocalAudioTrack.CreateAudioTrack("microphone", source, room);
    ///
    /// // Publish track
    /// await room.LocalParticipant.PublishTrack(track, options);
    ///
    /// // Dispose when done
    /// platformAudio.Dispose();
    /// </code>
    /// </example>
    public sealed class PlatformAudio : IDisposable
    {
        internal readonly FfiHandle Handle;
        private readonly PlatformAudioInfo _info;
        private bool _disposed = false;

        /// <summary>
        /// Number of available recording (microphone) devices.
        /// </summary>
        public int RecordingDeviceCount => _info.RecordingDeviceCount;

        /// <summary>
        /// Number of available playout (speaker) devices.
        /// </summary>
        public int PlayoutDeviceCount => _info.PlayoutDeviceCount;

        /// <summary>
        /// Creates a new PlatformAudio instance, enabling the platform ADM.
        ///
        /// This must be called before creating any PlatformAudioSource or connecting
        /// to a room if you want automatic speaker playout for remote audio.
        ///
        /// On iOS, this automatically configures the audio session for VoIP mode
        /// (PlayAndRecord category with VoiceChat mode) to enable hardware echo
        /// cancellation and microphone input.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the platform ADM could not be initialized (e.g., no audio devices,
        /// missing permissions).
        /// </exception>
        public PlatformAudio()
        {
#if UNITY_IOS && !UNITY_EDITOR
            // Configure iOS audio session for VoIP before initializing WebRTC ADM.
            // This sets PlayAndRecord category with VoiceChat mode for hardware AEC.
            IOSAudioSessionHelper.LiveKit_ConfigureAudioSessionForVoIP();
#endif

            using var request = FFIBridge.Instance.NewRequest<NewPlatformAudioRequest>();
            using var response = request.Send();
            FfiResponse res = response;

            if (res.NewPlatformAudio.MessageCase == NewPlatformAudioResponse.MessageOneofCase.Error)
                throw new InvalidOperationException($"Failed to create PlatformAudio: {res.NewPlatformAudio.Error}");

            var platformAudio = res.NewPlatformAudio.PlatformAudio;
            Handle = FfiHandle.FromOwnedHandle(platformAudio.Handle);
            _info = platformAudio.Info;

            Utils.Debug($"PlatformAudio created: {RecordingDeviceCount} recording devices, {PlayoutDeviceCount} playout devices");
        }

        /// <summary>
        /// Gets the lists of available recording and playout devices.
        /// </summary>
        /// <returns>
        /// A tuple containing:
        /// - Recording: List of available microphones
        /// - Playout: List of available speakers/headphones
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if device enumeration failed.
        /// </exception>
        public (List<AudioDevice> Recording, List<AudioDevice> Playout) GetDevices()
        {
            using var request = FFIBridge.Instance.NewRequest<GetAudioDevicesRequest>();
            request.request.PlatformAudioHandle = (ulong)Handle.DangerousGetHandle();

            using var response = request.Send();
            FfiResponse res = response;

            if (res.GetAudioDevices.HasError && !string.IsNullOrEmpty(res.GetAudioDevices.Error))
                throw new InvalidOperationException($"Failed to get audio devices: {res.GetAudioDevices.Error}");

            var recording = new List<AudioDevice>();
            foreach (var device in res.GetAudioDevices.RecordingDevices)
            {
                recording.Add(new AudioDevice { Index = device.Index, Name = device.Name });
            }

            var playout = new List<AudioDevice>();
            foreach (var device in res.GetAudioDevices.PlayoutDevices)
            {
                playout.Add(new AudioDevice { Index = device.Index, Name = device.Name });
            }

            return (recording, playout);
        }

        /// <summary>
        /// Sets the recording device (microphone) by index.
        ///
        /// Call this before creating audio tracks to select which microphone to use.
        /// Device indices are 0-based and must be less than RecordingDeviceCount.
        /// </summary>
        /// <param name="index">Device index from GetDevices().Recording</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the device index is invalid or the operation failed.
        /// </exception>
        public void SetRecordingDevice(uint index)
        {
            using var request = FFIBridge.Instance.NewRequest<SetRecordingDeviceRequest>();
            request.request.PlatformAudioHandle = (ulong)Handle.DangerousGetHandle();
            request.request.Index = index;

            using var response = request.Send();
            FfiResponse res = response;

            if (res.SetRecordingDevice.HasError && !string.IsNullOrEmpty(res.SetRecordingDevice.Error))
                throw new InvalidOperationException($"Failed to set recording device: {res.SetRecordingDevice.Error}");

            Utils.Debug($"PlatformAudio: set recording device to index {index}");
        }

        /// <summary>
        /// Sets the playout device (speaker/headphones) by index.
        ///
        /// Call this before connecting to select which speaker to use for remote audio.
        /// Device indices are 0-based and must be less than PlayoutDeviceCount.
        /// </summary>
        /// <param name="index">Device index from GetDevices().Playout</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the device index is invalid or the operation failed.
        /// </exception>
        public void SetPlayoutDevice(uint index)
        {
            using var request = FFIBridge.Instance.NewRequest<SetPlayoutDeviceRequest>();
            request.request.PlatformAudioHandle = (ulong)Handle.DangerousGetHandle();
            request.request.Index = index;

            using var response = request.Send();
            FfiResponse res = response;

            if (res.SetPlayoutDevice.HasError && !string.IsNullOrEmpty(res.SetPlayoutDevice.Error))
                throw new InvalidOperationException($"Failed to set playout device: {res.SetPlayoutDevice.Error}");

            Utils.Debug($"PlatformAudio: set playout device to index {index}");
        }

        /// <summary>
        /// Releases the PlatformAudio resources.
        ///
        /// When disposed, the platform ADM may be disabled if this was the last
        /// PlatformAudio instance.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            Handle.Dispose();
            _disposed = true;
            Utils.Debug("PlatformAudio disposed");
        }
    }
}
