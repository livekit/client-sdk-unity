using System;
using LiveKit.Internal;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace LiveKit.Runtime.Scripts.Audio
{
    public class DeviceMicrophoneAudioSource : IAudioFilter, IDisposable
    {
        private readonly AudioMixerGroup? audioMixerGroup;

        private Internals internals;
        private bool disposed;

        public bool IsRecording => disposed == false && internals.audioSource.isPlaying;

        private DeviceMicrophoneAudioSource(Internals internals, AudioMixerGroup? audioMixerGroup)
        {
            this.internals = internals;
            this.audioMixerGroup = audioMixerGroup;
            internals.audioFilter.AudioRead += AudioFilterOnAudioRead;
        }

        public static DeviceMicrophoneAudioSource New(
            MicrophoneSelection microphoneSelection,
            AudioMixerGroup? audioMixerGroup)
        {
            Internals internals = Internals.New(microphoneSelection, audioMixerGroup);
            return new DeviceMicrophoneAudioSource(internals, audioMixerGroup);
        }

        public void Dispose()
        {
            internals.audioFilter.AudioRead -= AudioFilterOnAudioRead;
            internals.Dispose();
            disposed = true;
        }

        public void SwitchMicrophone(MicrophoneSelection microphoneSelection)
        {
            bool wasRecording = IsRecording;

            internals.audioFilter.AudioRead -= AudioFilterOnAudioRead;
            internals.Dispose();
            internals = Internals.New(microphoneSelection, audioMixerGroup);

            if (wasRecording)
                StartCapture();
        }

        private void AudioFilterOnAudioRead(Span<float> data, int channels, int sampleRate)
        {
            AudioRead?.Invoke(data, channels, sampleRate);
        }

        public void StartCapture()
        {
            internals.audioSource.Play();
        }

        public void StopCapture()
        {
            internals.audioSource.Stop();
        }

        public bool IsValid => disposed == false && internals.audioFilter.IsValid;

        public event IAudioFilter.OnAudioDelegate? AudioRead;

        private readonly struct Internals : IDisposable
        {
            public readonly AudioSource audioSource;
            public readonly IAudioFilter audioFilter;
            public readonly GameObject microphoneGameObject;
            public readonly MicrophoneSelection currentMicrophone;

            private Internals(
                AudioSource audioSource,
                IAudioFilter audioFilter,
                GameObject microphoneGameObject,
                MicrophoneSelection currentMicrophone)
            {
                this.audioSource = audioSource;
                this.audioFilter = audioFilter;
                this.microphoneGameObject = microphoneGameObject;
                this.currentMicrophone = currentMicrophone;
            }

            public static Internals New(MicrophoneSelection microphoneSelection, AudioMixerGroup? audioMixerGroup)
            {
                GameObject microphoneObject = new GameObject($"microphone: {microphoneSelection.name}");

                var audioSource = microphoneObject.AddComponent<AudioSource>();
                audioSource.loop = true;
                audioSource.clip = Microphone.Start(
                    microphoneSelection.name,
                    true,
                    1,
                    48000
                )!; //frequency is not guaranteed by Unity
                if (audioMixerGroup != null)
                    audioSource.outputAudioMixerGroup = audioMixerGroup;

                var audioFilter = microphoneObject.AddComponent<AudioFilter>()!;
                // Prevent microphone feedback
                microphoneObject.AddComponent<OmitAudioFilter>();

                return new Internals(audioSource, audioFilter, microphoneObject, microphoneSelection);
            }


            public void Dispose()
            {
                Microphone.End(currentMicrophone.name);
                Object.Destroy(microphoneGameObject);
            }
        }
    }
}