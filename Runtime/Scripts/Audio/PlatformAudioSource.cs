using System;
using LiveKit.Proto;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit
{
    /// <summary>
    /// Options for audio processing when creating a PlatformAudioSource.
    /// </summary>
    public struct AudioProcessingOptions
    {
        /// <summary>Enable echo cancellation (AEC). Default: true.</summary>
        public bool EchoCancellation;
        /// <summary>Enable noise suppression (NS). Default: true.</summary>
        public bool NoiseSuppression;
        /// <summary>Enable automatic gain control (AGC). Default: true.</summary>
        public bool AutoGainControl;
        /// <summary>Prefer hardware audio processing (e.g., iOS VPIO). Lower latency. Default: true.</summary>
        public bool PreferHardware;

        /// <summary>
        /// Default audio processing options with all processing enabled and hardware preferred.
        /// </summary>
        public static AudioProcessingOptions Default => new AudioProcessingOptions
        {
            EchoCancellation = true,
            NoiseSuppression = true,
            AutoGainControl = true,
            PreferHardware = true
        };
    }

    /// <summary>
    /// Audio source that captures from the platform microphone via WebRTC's ADM.
    ///
    /// Unlike MicrophoneSource which uses Unity's Microphone API and manually pushes
    /// audio frames, PlatformAudioSource lets WebRTC's ADM handle capture directly.
    /// This provides:
    /// - Echo cancellation (AEC) that works with speaker playout
    /// - Lower latency (single audio path, no Unity intermediate)
    /// - Automatic gain control and noise suppression
    ///
    /// Requires PlatformAudio to be created first to enable the ADM.
    /// </summary>
    /// <example>
    /// <code>
    /// // Create PlatformAudio first (enables ADM)
    /// var platformAudio = new PlatformAudio();
    ///
    /// // Create audio source with default options
    /// var source = new PlatformAudioSource(platformAudio);
    ///
    /// // Or with custom options
    /// var options = new AudioProcessingOptions {
    ///     EchoCancellation = true,
    ///     NoiseSuppression = true,
    ///     AutoGainControl = true,
    ///     PreferHardware = true
    /// };
    /// var source = new PlatformAudioSource(platformAudio, options);
    ///
    /// // Create and publish track
    /// var track = LocalAudioTrack.CreateAudioTrack("microphone", source, room);
    /// await room.LocalParticipant.PublishTrack(track, options);
    /// </code>
    /// </example>
    public sealed class PlatformAudioSource : IRtcSource, IDisposable
    {
        internal readonly FfiHandle Handle;
        private readonly PlatformAudio _platformAudio;
        private bool _disposed = false;
        private bool _muted = false;

        /// <summary>
        /// Whether the audio source is muted.
        /// </summary>
        public override bool Muted => _muted;

        /// <summary>
        /// Creates a new platform audio source with default audio processing options.
        ///
        /// The source will capture audio from the microphone selected via
        /// PlatformAudio.SetRecordingDevice(), or the default device if none selected.
        /// </summary>
        /// <param name="platformAudio">
        /// The PlatformAudio instance. Must be kept alive while this source is in use.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if platformAudio is null.
        /// </exception>
        public PlatformAudioSource(PlatformAudio platformAudio)
            : this(platformAudio, AudioProcessingOptions.Default)
        {
        }

        /// <summary>
        /// Creates a new platform audio source with custom audio processing options.
        ///
        /// The source will capture audio from the microphone selected via
        /// PlatformAudio.SetRecordingDevice(), or the default device if none selected.
        /// </summary>
        /// <param name="platformAudio">
        /// The PlatformAudio instance. Must be kept alive while this source is in use.
        /// </param>
        /// <param name="options">Audio processing options to configure on the ADM.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if platformAudio is null.
        /// </exception>
        public PlatformAudioSource(PlatformAudio platformAudio, AudioProcessingOptions options)
        {
            if (platformAudio == null)
                throw new ArgumentNullException(nameof(platformAudio));

            _platformAudio = platformAudio;

            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourcePlatform;
            newAudioSource.NumChannels = 2;
            newAudioSource.SampleRate = 48000;

            // Pass the platform audio handle so the Rust side can configure audio processing
            newAudioSource.PlatformAudioHandle = (ulong)platformAudio.Handle.DangerousGetHandle();

            // Configure audio processing options
            newAudioSource.Options = request.TempResource<Proto.AudioSourceOptions>();
            newAudioSource.Options.EchoCancellation = options.EchoCancellation;
            newAudioSource.Options.AutoGainControl = options.AutoGainControl;
            newAudioSource.Options.NoiseSuppression = options.NoiseSuppression;
            newAudioSource.Options.PreferHardware = options.PreferHardware;

            using var response = request.Send();
            FfiResponse res = response;

            Handle = FfiHandle.FromOwnedHandle(res.NewAudioSource.Source.Handle);
            Utils.Debug($"PlatformAudioSource created: handle={Handle.DangerousGetHandle()}, AEC={options.EchoCancellation}, NS={options.NoiseSuppression}, AGC={options.AutoGainControl}, HW={options.PreferHardware}");
        }

        /// <summary>
        /// Mutes or unmutes the audio source.
        /// </summary>
        /// <param name="muted">True to mute, false to unmute.</param>
        public override void SetMute(bool muted)
        {
            _muted = muted;
            Utils.Debug($"PlatformAudioSource: muted={muted}");
        }

        /// <summary>
        /// Releases the audio source resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            Handle.Dispose();
            _disposed = true;
            Utils.Debug("PlatformAudioSource disposed");
        }
    }
}
