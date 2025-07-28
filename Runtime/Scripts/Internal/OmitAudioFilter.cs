using System;
using UnityEngine;

namespace LiveKit.Internal
{
    public class OmitAudioFilter : MonoBehaviour
    {
        private void OnAudioFilterRead(float[] data, int channels)
        {
            Array.Clear(data, 0, data.Length);
        }
    }
}