using System;
using LiveKit;
using LiveKit.Audio;
using LiveKit.Internal;
using UnityEngine;

namespace Examples.AudioPlayback
{
    public class AudioPlaybackPlayground : MonoBehaviour
    {
        [SerializeField] private AudioClip audioClip = null!;
        
        private void Start()
        {
            GameObject gm = new GameObject("filter");
            AudioSource audioSource = gm.AddComponent<AudioSource>()!;
            audioSource.clip = audioClip!;
            AudioFilter filter = gm.AddComponent<AudioFilter>()!;
            gm.AddComponent<OmitAudioFilter>();

            audioSource.Play(); 

            PlaybackMicrophoneAudioSource.New(filter, "test");
        }
    }
}