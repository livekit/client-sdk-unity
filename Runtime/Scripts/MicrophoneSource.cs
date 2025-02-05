using System.Collections;
using UnityEngine;

namespace LiveKit
{
    public class MicrophoneSource : BasicAudioSource
    {
        private string _deviceName;

        public MicrophoneSource(AudioSource source) : base(source, 2, RtcAudioSourceType.AudioSourceMicrophone)
        {
        }

        public void Configure(string device, bool loop, int lenghtSec, int frequency)
        {
            _deviceName = device;
            Source.clip = Microphone.Start(device, loop, lenghtSec, frequency);
            Source.loop = true;
        }
        
        public override IEnumerator Prepare(float timeout = 0)
        {
            return new WaitUntil(() => Microphone.GetPosition(_deviceName) > 0);
        }

    }
}