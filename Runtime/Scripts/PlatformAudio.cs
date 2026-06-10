using System;
using System.Collections;
using System.Collections.Generic;
using LiveKit.Proto;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif

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
    /// // Select devices (no-op on Android/iOS; routing there is governed by the OS).
    /// // Use the uint overload for quick index-based selection, or the string overload
    /// // with a GUID from GetDevices() to persist a stable selection across hot-plug.
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
        /// Convenience wrapper around <see cref="SetRecordingDevice(string)"/> that looks
        /// up the GUID from <see cref="GetDevices"/>. Prefer the GUID overload for code
        /// that persists a selection — indices can shift when devices are added/removed.
        /// </summary>
        /// <param name="index">Device index from GetDevices().Recording</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the device index is out of range or the operation failed.
        /// </exception>
        public void SetRecordingDevice(uint index)
        {
            var (recording, _) = GetDevices();
            if (index >= recording.Count)
                throw new InvalidOperationException($"Recording device index {index} out of range (max: {recording.Count - 1})");

            SetRecordingDevice(recording[(int)index].Guid ?? "");
        }

        /// <summary>
        /// Sets the recording device (microphone) by device ID (GUID).
        ///
        /// On Android and iOS this is a no-op in the native ADM: input routing is
        /// governed by the OS (AVAudioSession on iOS, AudioManager on Android) and
        /// the call is acknowledged but ignored. The method is still safe to call,
        /// and the response carries no error.
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
        /// Sets the playout device (speaker/headphones) by index.
        ///
        /// Convenience wrapper around <see cref="SetPlayoutDevice(string)"/> that looks
        /// up the GUID from <see cref="GetDevices"/>. Prefer the GUID overload for code
        /// that persists a selection — indices can shift when devices are added/removed.
        /// </summary>
        /// <param name="index">Device index from GetDevices().Playout</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the device index is out of range or the operation failed.
        /// </exception>
        public void SetPlayoutDevice(uint index)
        {
            var (_, playout) = GetDevices();
            if (index >= playout.Count)
                throw new InvalidOperationException($"Playout device index {index} out of range (max: {playout.Count - 1})");

            SetPlayoutDevice(playout[(int)index].Guid ?? "");
        }

        /// <summary>
        /// Sets the playout device (speaker/headphones) by device ID (GUID).
        ///
        /// On Android and iOS this is a no-op in the native ADM: output routing is
        /// governed by the OS (AVAudioSession on iOS, AudioManager on Android) and
        /// the call is acknowledged but ignored. The method is still safe to call,
        /// and the response carries no error.
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
        /// Starts recording from the microphone.
        ///
        /// Recording is started automatically when PlatformAudio is created.
        /// Use this to resume recording after calling StopRecording.
        /// This turns on the system's recording privacy indicator (e.g., on macOS/iOS).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the operation failed.
        /// </exception>
        public IEnumerator StartRecording()
        {
#if PLATFORM_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                // Fire the system permission dialog and yield until the user resolves it.
                // PermissionCallbacks delivers the result asynchronously from the Android OS;
                // we poll the captured flag from this coroutine until one of the callbacks
                // sets it. Without this gate, the WebRTC Android ADM would crash the process
                // when AudioRecord fails to open due to the missing permission.
                bool? granted = null;
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => granted = true;
                callbacks.PermissionDenied += _ => granted = false;
                callbacks.PermissionDeniedAndDontAskAgain += _ => granted = false;
                Permission.RequestUserPermission(Permission.Microphone, callbacks);

                while (granted == null)
                    yield return null;

                if (granted == false)
                    throw new InvalidOperationException(
                        "Microphone permission denied by user; cannot start recording.");
            }
#endif

            using var request = FFIBridge.Instance.NewRequest<StartRecordingRequest>();
            request.request.PlatformAudioHandle = (ulong)Handle.DangerousGetHandle();

            using var response = request.Send();
            FfiResponse res = response;

            if (res.StartRecording.HasError && !string.IsNullOrEmpty(res.StartRecording.Error))
                throw new InvalidOperationException($"Failed to start recording: {res.StartRecording.Error}");

            Utils.Debug("PlatformAudio: started recording");

            // Ensures this method is always a valid iterator even when the PLATFORM_ANDROID
            // branch is compiled out (no `yield return` would otherwise be reachable on
            // non-Android builds, which is a compile error for IEnumerator-returning methods).
            yield break;
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
