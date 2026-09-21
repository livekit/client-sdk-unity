using System.Collections;
using UnityEngine;
using LiveKit.Internal;
using LiveKit.Internal.Threading;

namespace LiveKit
{
    /// <summary>
    /// Taps the final mix Unity sends to the audio output device and feeds it to the echo
    /// canceller as the far-end reference.
    ///
    /// Attaches itself to the GameObject of the active <see cref="AudioListener"/>, the virtual
    /// microphone in the scene, usually on the camera, which hears the scene and sends the result
    /// to the audio output hardware.
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
    /// the sample rate, listener state and consumer count are cached on the main thread.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayoutReference : MonoBehaviour
    {
        internal delegate void PlayoutAudioDelegate(float[] data, int channels, int sampleRate);

        // Raised on the Unity audio thread with the final mix. Consumers must not modify the
        // buffer: it is on its way to the speaker. The invocation list is the consumer list, so
        // a consumer cannot exist without holding the reference and vice versa.
        private static event PlayoutAudioDelegate AudioRead;
        private static bool HasConsumers => AudioRead != null;

        // Singleton pattern instance
        private static PlayoutReference _instance;

        // The AudioListener we are attached to
        private AudioListener _sceneAudioListener;
        private volatile int _sampleRate;
        private volatile bool _deliver;

        /// <summary>Whether a reference on an enabled listener is delivering audio to a consumer.</summary>
        internal static bool IsAttached => _instance != null && _instance._deliver;

        /// <summary>
        /// Main thread. Registers <paramref name="consumer"/> to receive the final mix and attaches
        /// to the listener if possible. Acquiring the same consumer twice delivers to it twice
        /// until it is released twice.
        /// </summary>
        internal static void Acquire(PlayoutAudioDelegate consumer)
        {
            AudioRead += consumer;
            EnsureAttached();
            if (_instance != null) _instance.RefreshDeliveryState();
        }

        /// <summary>
        /// Main thread. Removes <paramref name="consumer"/>. The component stays on the listener;
        /// once the last consumer is gone it stops delivering. Releasing a consumer that was not
        /// acquired is a no-op.
        /// </summary>
        internal static void Release(PlayoutAudioDelegate consumer)
        {
            AudioRead -= consumer;
            if (_instance != null) _instance.RefreshDeliveryState();
        }

        /// <summary>
        /// Main thread. Attaches to the active AudioListener unless a working reference already
        /// exists. No-op without consumers or without a listener; consumers call this periodically,
        /// which is what covers scene loads and a destroyed listener.
        /// </summary>
        internal static void EnsureAttached()
        {
            if (!HasConsumers) return;
            if (_instance != null && _instance.isActiveAndEnabled &&
                _instance._sceneAudioListener != null && _instance._sceneAudioListener.isActiveAndEnabled)
                return;

            var listener = FindActiveListener();
            if (listener == null) return;

            var existing = listener.GetComponent<PlayoutReference>();
            _instance = existing != null ? existing : listener.gameObject.AddComponent<PlayoutReference>();
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
            _sceneAudioListener = GetComponent<AudioListener>();
            if (_sceneAudioListener == null)
                Utils.Warning("PlayoutReference must be on the AudioListener's GameObject; it will not deliver a reference from here.");

            RefreshDeliveryState();
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            if (_instance == null) _instance = this;
        }

        private void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            _deliver = false;
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            RefreshDeliveryState();
        }

        // Listener state and the output rate are Unity APIs and the consumer list is mutated on the
        // main thread; fold them into one flag so the audio thread reads a single volatile.
        private void RefreshDeliveryState()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            _deliver = HasConsumers && _sceneAudioListener != null && _sceneAudioListener.isActiveAndEnabled;
        }

        // Unity rebuilds the DSP graph on a device change (or AudioSettings.Reset), which can leave
        // filter nodes detached; AudioStream recreates its probe for the same reason. Recreate this
        // component so the tap is registered on the new graph. Only done while something consumes
        // the reference, so a hand-placed component in an idle scene is left alone.
        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            RefreshDeliveryState();
            if (!HasConsumers) return;

            var host = gameObject;
            Destroy(this);
            MonoBehaviourContext.RunCoroutine(Reattach(host));
        }

        private static IEnumerator Reattach(GameObject host)
        {
            // Let the deferred Destroy apply before adding the replacement.
            yield return null;
            if (host == null || !HasConsumers) yield break;
            if (host.GetComponent<PlayoutReference>() == null)
                _instance = host.AddComponent<PlayoutReference>();
        }

        // Unity audio thread.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_deliver) return;
            AudioRead?.Invoke(data, channels, _sampleRate);
        }
    }
}
