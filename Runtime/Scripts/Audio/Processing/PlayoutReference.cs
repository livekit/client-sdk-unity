using System.Collections;
using UnityEngine;
using LiveKit.Internal;
using LiveKit.Internal.Threading;

namespace LiveKit
{
    /// <summary>
    /// Taps the final mix Unity sends to the audio device and feeds it to the echo canceller as
    /// the far-end reference. Lives on the GameObject of the active <see cref="AudioListener"/>.
    /// </summary>
    /// <remarks>
    /// An <see cref="RtcAudioSource"/> created with <see cref="AudioProcessingOptions.EchoCancellation"/>
    /// attaches this component to the active listener when it starts and re-attaches it after
    /// scene loads and audio device changes. Adding it to the listener yourself is supported and
    /// does the same thing.
    ///
    /// Because the tap sits after every AudioSource, mixer group and spatializer, the reference is
    /// exactly what the loudspeaker plays: every remote participant plus the game's own audio.
    /// The <see cref="MicrophoneSource"/> capture probe clears its buffer after reading, so the
    /// local microphone never appears in the mix.
    ///
    /// <c>OnAudioFilterRead</c> runs on the Unity audio thread and must not touch Unity APIs, so
    /// the sample rate and listener state are cached on the main thread.
    /// </remarks>
    [AddComponentMenu("LiveKit/Playout Reference")]
    public sealed class PlayoutReference : MonoBehaviour
    {
        internal delegate void PlayoutAudioDelegate(float[] data, int channels, int sampleRate);

        /// <summary>
        /// Raised on the Unity audio thread with the final mix. Subscribers must not modify the
        /// buffer: it is on its way to the speaker.
        /// </summary>
        internal static event PlayoutAudioDelegate AudioRead;

        private static PlayoutReference _active;
        private static int _consumers;

        private AudioListener _listener;
        private volatile int _sampleRate;
        private volatile bool _deliver;

        /// <summary>Whether a reference on an enabled listener is delivering audio.</summary>
        internal static bool IsAttached => _active != null && _active._deliver;

        /// <summary>Main thread. Registers a consumer and attaches to the listener if possible.</summary>
        internal static void Acquire()
        {
            _consumers++;
            EnsureAttached();
        }

        /// <summary>Main thread. The component stays on the listener; it is inert without consumers.</summary>
        internal static void Release()
        {
            if (_consumers > 0) _consumers--;
        }

        /// <summary>
        /// Main thread. Attaches to the active AudioListener unless a working reference already
        /// exists. No-op without consumers or without a listener; consumers call this periodically,
        /// which is what covers scene loads and a destroyed listener.
        /// </summary>
        internal static void EnsureAttached()
        {
            if (_consumers == 0) return;
            if (_active != null && _active.isActiveAndEnabled &&
                _active._listener != null && _active._listener.isActiveAndEnabled)
                return;

            var listener = FindActiveListener();
            if (listener == null) return;

            var existing = listener.GetComponent<PlayoutReference>();
            _active = existing != null ? existing : listener.gameObject.AddComponent<PlayoutReference>();
        }

        private static AudioListener FindActiveListener()
        {
            var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            foreach (var listener in listeners)
            {
                if (listener.isActiveAndEnabled) return listener;
            }
            return null;
        }

        private void OnEnable()
        {
            _listener = GetComponent<AudioListener>();
            if (_listener == null)
                Utils.Warning("PlayoutReference must be on the AudioListener's GameObject; it will not deliver a reference from here.");

            RefreshDeliveryState();
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            if (_active == null) _active = this;
        }

        private void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            _deliver = false;
            if (_active == this) _active = null;
        }

        private void Update()
        {
            RefreshDeliveryState();
        }

        // Listener state and the output rate are Unity APIs; sample them here for the audio thread.
        private void RefreshDeliveryState()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            _deliver = _listener != null && _listener.isActiveAndEnabled;
        }

        // Unity rebuilds the DSP graph on a device change (or AudioSettings.Reset), which can leave
        // filter nodes detached; AudioStream recreates its probe for the same reason. Recreate this
        // component so the tap is registered on the new graph. Only done while something consumes
        // the reference, so a hand-placed component in an idle scene is left alone.
        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            RefreshDeliveryState();
            if (_consumers == 0) return;

            var host = gameObject;
            Destroy(this);
            MonoBehaviourContext.RunCoroutine(Reattach(host));
        }

        private static IEnumerator Reattach(GameObject host)
        {
            // Let the deferred Destroy apply before adding the replacement.
            yield return null;
            if (host == null || _consumers == 0) yield break;
            if (host.GetComponent<PlayoutReference>() == null)
                _active = host.AddComponent<PlayoutReference>();
        }

        // Unity audio thread.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_deliver) return;
            AudioRead?.Invoke(data, channels, _sampleRate);
        }
    }
}
