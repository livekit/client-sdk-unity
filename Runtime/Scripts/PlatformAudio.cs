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
        /// <summary>Device index (0-based). Note: indices can change when devices are added/removed.</summary>
        public uint Index;
        /// <summary>Device name as reported by the operating system.</summary>
        public string Name;
        /// <summary>
        /// Platform-specific unique device identifier (GUID).
        /// This is stable across device additions/removals and should be preferred
        /// over index for device selection.
        /// </summary>
        public string Guid;
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
                recording.Add(new AudioDevice {
                    Index = device.Index,
                    Name = device.Name,
                    Guid = device.HasGuid ? device.Guid : null
                });
            }

            var playout = new List<AudioDevice>();
            foreach (var device in res.GetAudioDevices.PlayoutDevices)
            {
                playout.Add(new AudioDevice {
                    Index = device.Index,
                    Name = device.Name,
                    Guid = device.HasGuid ? device.Guid : null
                });
            }

            return (recording, playout);
        }

        /// <summary>
        /// Sets the recording device (microphone) by index.
        ///
        /// Call this before creating audio tracks to select which microphone to use.
        /// Device indices are 0-based and must be less than RecordingDeviceCount.
        ///
        /// Note: Prefer SetRecordingDevice(string deviceId) for robust device selection across hot-plug events.
        /// </summary>
        /// <param name="index">Device index from GetDevices().Recording</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the device index is invalid or the operation failed.
        /// </exception>
        public void SetRecordingDevice(uint index)
        {
            // Look up the device GUID by index
            var (recording, _) = GetDevices();
            if (index >= recording.Count)
                throw new InvalidOperationException($"Recording device index {index} out of range (max: {recording.Count - 1})");

            var deviceId = recording[(int)index].Guid;
            // Note: On Android, devices don't have GUIDs - they're identified by index only.
            // Android also only reports a single "default" microphone because the system
            // automatically selects the best input source based on the audio mode.
            // If GUID is empty, we pass an empty string which triggers index-0 fallback in native code.
            SetRecordingDevice(deviceId ?? "");
            Utils.Debug($"PlatformAudio: set recording device to index {index} (GUID: {(string.IsNullOrEmpty(deviceId) ? "<empty>" : deviceId)})");
        }

        /// <summary>
        /// Sets the recording device (microphone) by device ID (GUID).
        ///
        /// This is the preferred method for device selection as device IDs are stable
        /// across device hot-plug events, unlike indices which can change.
        /// </summary>
        /// <param name="deviceId">Device ID/GUID from GetDevices().Recording[i].Guid</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the device is not found or the operation failed.
        /// </exception>
        public void SetRecordingDevice(string deviceId)
        {
            using var request = FFIBridge.Instance.NewRequest<SetRecordingDeviceRequest>();
            request.request.PlatformAudioHandle = (ulong)Handle.DangerousGetHandle();
            request.request.DeviceId = deviceId;

            using var response = request.Send();
            FfiResponse res = response;

            if (res.SetRecordingDevice.HasError && !string.IsNullOrEmpty(res.SetRecordingDevice.Error))
                throw new InvalidOperationException($"Failed to set recording device: {res.SetRecordingDevice.Error}");

            Utils.Debug($"PlatformAudio: set recording device to {deviceId}");
        }

        /// <summary>
        /// Sets the recording device (microphone) by GUID.
        /// </summary>
        /// <param name="guid">Device GUID from GetDevices().Recording[i].Guid</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the device is not found or the operation failed.
        /// </exception>
        [Obsolete("Use SetRecordingDevice(string deviceId) instead")]
        public void SetRecordingDeviceByGuid(string guid) => SetRecordingDevice(guid);

        /// <summary>
        /// Sets the playout device (speaker/headphones) by index.
        ///
        /// Call this before connecting to select which speaker to use for remote audio.
        /// Device indices are 0-based and must be less than PlayoutDeviceCount.
        ///
        /// Note: Prefer SetPlayoutDevice(string deviceId) for robust device selection across hot-plug events.
        /// </summary>
        /// <param name="index">Device index from GetDevices().Playout</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the device index is invalid or the operation failed.
        /// </exception>
        public void SetPlayoutDevice(uint index)
        {
            // Look up the device GUID by index
            var (_, playout) = GetDevices();
            if (index >= playout.Count)
                throw new InvalidOperationException($"Playout device index {index} out of range (max: {playout.Count - 1})");

            var deviceId = playout[(int)index].Guid;
            // Note: On Android, devices don't have GUIDs - they're identified by index only.
            // Android also only reports a single "default" device because audio routing
            // (speaker vs earpiece vs Bluetooth) is handled by the system via AudioManager,
            // not through WebRTC device selection. Use Android's AudioManager API to switch outputs.
            // If GUID is empty, we pass an empty string which triggers index-0 fallback in native code.
            SetPlayoutDevice(deviceId ?? "");
            Utils.Debug($"PlatformAudio: set playout device to index {index} (GUID: {(string.IsNullOrEmpty(deviceId) ? "<empty>" : deviceId)})");
        }

        /// <summary>
        /// Sets the playout device (speaker/headphones) by device ID (GUID).
        ///
        /// This is the preferred method for device selection as device IDs are stable
        /// across device hot-plug events, unlike indices which can change.
        /// </summary>
        /// <param name="deviceId">Device ID/GUID from GetDevices().Playout[i].Guid</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the device is not found or the operation failed.
        /// </exception>
        public void SetPlayoutDevice(string deviceId)
        {
            using var request = FFIBridge.Instance.NewRequest<SetPlayoutDeviceRequest>();
            request.request.PlatformAudioHandle = (ulong)Handle.DangerousGetHandle();
            request.request.DeviceId = deviceId;

            using var response = request.Send();
            FfiResponse res = response;

            if (res.SetPlayoutDevice.HasError && !string.IsNullOrEmpty(res.SetPlayoutDevice.Error))
                throw new InvalidOperationException($"Failed to set playout device: {res.SetPlayoutDevice.Error}");

            Utils.Debug($"PlatformAudio: set playout device to {deviceId}");
        }

        /// <summary>
        /// Sets the playout device (speaker/headphones) by GUID.
        /// </summary>
        /// <param name="guid">Device GUID from GetDevices().Playout[i].Guid</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the device is not found or the operation failed.
        /// </exception>
        [Obsolete("Use SetPlayoutDevice(string deviceId) instead")]
        public void SetPlayoutDeviceByGuid(string guid) => SetPlayoutDevice(guid);

        /// <summary>
        /// Starts recording from the microphone.
        ///
        /// Recording is started automatically when PlatformAudio is created.
        /// Use this to resume recording after calling StopRecording.
        /// This turns on the system's recording privacy indicator (e.g., on macOS/iOS).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the operation failed.
        /// </exception>
        public void StartRecording()
        {
            using var request = FFIBridge.Instance.NewRequest<StartRecordingRequest>();
            request.request.PlatformAudioHandle = (ulong)Handle.DangerousGetHandle();

            using var response = request.Send();
            FfiResponse res = response;

            if (res.StartRecording.HasError && !string.IsNullOrEmpty(res.StartRecording.Error))
                throw new InvalidOperationException($"Failed to start recording: {res.StartRecording.Error}");

            Utils.Debug("PlatformAudio: started recording");
        }

        /// <summary>
        /// Stops recording from the microphone.
        ///
        /// Use this to temporarily stop recording without disposing PlatformAudio.
        /// This turns off the system's recording privacy indicator (e.g., on macOS/iOS).
        /// Call StartRecording to resume recording.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the operation failed.
        /// </exception>
        public void StopRecording()
        {
            using var request = FFIBridge.Instance.NewRequest<StopRecordingRequest>();
            request.request.PlatformAudioHandle = (ulong)Handle.DangerousGetHandle();

            using var response = request.Send();
            FfiResponse res = response;

            if (res.StopRecording.HasError && !string.IsNullOrEmpty(res.StopRecording.Error))
                throw new InvalidOperationException($"Failed to stop recording: {res.StopRecording.Error}");

            Utils.Debug("PlatformAudio: stopped recording");
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
