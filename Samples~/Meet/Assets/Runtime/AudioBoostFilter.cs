using UnityEngine;

/// <summary>
/// Boosts the audio of the <see cref="AudioSource"/> on this GameObject by a linear
/// gain multiplier, e.g. for devices that play WebRTC audio at very low volume.
/// Values above 1 are hard-clipped at full scale, so expect distortion on loud
/// passages at higher settings.
///
/// Must be added AFTER the <see cref="LiveKit.AudioStream"/> for this source has been
/// created: Unity runs audio filters in component order and the SDK's probe overwrites
/// the buffer, so this filter only has an effect while it sits below the probe.
/// </summary>
public class AudioBoostFilter : MonoBehaviour
{
    [Tooltip("Linear gain applied to the audio. 1 = unchanged.")]
    [Range(1f, 10f)]
    public float multiplier = 1f;

    private void OnEnable()
    {
        AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
    }

    private void OnDisable()
    {
        AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
    }

    // AudioStream reacts to this same event by destroying and re-adding its probe,
    // which appends the new probe below this filter — the probe would then overwrite
    // the boosted output. Re-adding this component moves the gain back to the end of
    // the filter chain. AudioStream subscribed to the event before this component
    // existed, so the new probe is already in place when this handler runs.
    private void OnAudioConfigurationChanged(bool deviceWasChanged)
    {
        var replacement = gameObject.AddComponent<AudioBoostFilter>();
        replacement.multiplier = multiplier;
        Destroy(this);
    }

    // Called by Unity on the audio thread.
    private void OnAudioFilterRead(float[] data, int channels)
    {
        var gain = multiplier;
        if (gain == 1f)
            return;

        for (int i = 0; i < data.Length; i++)
        {
            var boosted = data[i] * gain;
            data[i] = boosted > 1f ? 1f : (boosted < -1f ? -1f : boosted);
        }
    }
}
