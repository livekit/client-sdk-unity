using RichTypes;
using UnityEngine;

namespace LiveKit.Audio
{
    /// <summary>
    /// Guarantees exact one instance per time, listens for AudioListener on the scene
    /// </summary>
    [DisallowMultipleComponent]
    public class GlobalListenerAudioFilter : AudioFilter
    {
        public static Result<GlobalListenerAudioFilter> NewOrExisting()
        {
            var audioListener = FindFirstObjectByType<AudioListener>();
            if (audioListener == null)
            {
                return Result<GlobalListenerAudioFilter>.ErrorResult("AudioListener not found in scene");
            }

            if (audioListener.gameObject.TryGetComponent(out GlobalListenerAudioFilter audioFilter))
            {
                return Result<GlobalListenerAudioFilter>.SuccessResult(audioFilter);
            }

            audioFilter = audioListener.gameObject.AddComponent<GlobalListenerAudioFilter>();
            if (audioFilter == null)
            {
                return Result<GlobalListenerAudioFilter>.ErrorResult("Cannot add audioFilter");
            }

            return Result<GlobalListenerAudioFilter>.SuccessResult(audioFilter);
        }

    }
}