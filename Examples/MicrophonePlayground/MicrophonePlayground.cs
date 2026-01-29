#if !UNITY_WEBGL

using LiveKit.Audio;
using RichTypes;
using UnityEngine;

namespace livekit.unity.Examples.client_sdk_unity.Examples.MicrophonePlayground
{
    public class MicrophonePlayground : MonoBehaviour
    {
        private MicrophoneRtcAudioSource? microphoneRtcAudioSource;

        private void Start()
        {
            Result<MicrophoneRtcAudioSource> result = MicrophoneRtcAudioSource.New();
            if (result.Success == false)
            {
                Debug.LogError(result.ErrorMessage);
                return;
            }

            microphoneRtcAudioSource = result.Value;
            microphoneRtcAudioSource.Start();
        }

        private void OnDestroy()
        {
            microphoneRtcAudioSource?.Dispose();
        }

    }
}

#endif
