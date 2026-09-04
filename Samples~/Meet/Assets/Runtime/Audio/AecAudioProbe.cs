using System;
using UnityEngine;

/// <summary>
/// Intercepts the microphone clip's audio on the Unity audio thread.
///
/// Sample-side re-implementation of the SDK's <c>AudioProbe</c>, which is internal. Behaviour is
/// deliberately identical, including <see cref="ClearAfterInvocation"/> — without it the
/// microphone is played back through the local loudspeaker.
/// </summary>
internal sealed class AecAudioProbe : MonoBehaviour
{
    public delegate void OnAudioDelegate(float[] data, int channels, int sampleRate);

    public event OnAudioDelegate AudioRead;

    private int _sampleRate;
    private volatile bool _clearAfterInvocation;

    public void ClearAfterInvocation()
    {
        _clearAfterInvocation = true;
    }

    private void OnEnable()
    {
        OnAudioConfigurationChanged(false);
        AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
    }

    private void OnDisable()
    {
        AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
    }

    private void OnAudioConfigurationChanged(bool deviceWasChanged)
    {
        _sampleRate = AudioSettings.outputSampleRate;
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        AudioRead?.Invoke(data, channels, _sampleRate);
        if (_clearAfterInvocation) data.AsSpan().Clear();
    }

    private void OnDestroy()
    {
        AudioRead = null;
    }
}
