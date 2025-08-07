using System.Text;
using LiveKit.Runtime.Scripts.Audio;
using LiveKit.Scripts.Audio;
using RichTypes;
using RustAudio;
using UnityEngine;

namespace Livekit.Examples.Microphone
{
    public class MicrophoneCapturePlayground : MonoBehaviour
    {
        [SerializeField] private string microphoneName;
        private MicrophoneAudioFilter? audioFilter;

        private void Start()
        {
            Result<MicrophoneSelection> selectionResult = MicrophoneSelection.FromName(microphoneName);
            if (selectionResult.Success == false)
            {
                Debug.LogError($"Microphone error: {selectionResult.ErrorMessage}");
                return;
            }

            Result<MicrophoneAudioFilter> microphone = MicrophoneAudioFilter.New(selectionResult.Value, withPlayback: true);
            if (microphone.Success == false)
            {
                Debug.LogError($"Microphone error: {microphone.ErrorMessage}");
                return;
            }

            audioFilter = microphone.Value;
            audioFilter.StartCapture();
        }

        [ContextMenu(nameof(PrintDevices))]
        public void PrintDevices()
        {
            var array = MicrophoneAudioFilter.AvailableDeviceNamesOrEmpty();
            var sb = new StringBuilder();
            sb.AppendLine($"Total count: {array.Length}, Available:");
            foreach (var name in array)
            {
                sb.AppendLine(name);
            }

            Debug.Log(sb.ToString());
        }

        [ContextMenu(nameof(PrintStatus))]
        public void PrintStatus()
        {
            var status = RustAudioClient.SystemStatus();
            var sb = new StringBuilder();
            sb.AppendLine(JsonUtility.ToJson(status));
            Debug.Log(sb.ToString());
        }

        [ContextMenu(nameof(ReInit))]
        public void ReInit()
        {
            RustAudioClient.ForceReInit();
        }

        private void OnDestroy()
        {
            audioFilter?.Dispose();
            audioFilter = null;
        }
    }
}