using System.Collections.Generic;
using System.Linq;
using LiveKit.Audio;
using LiveKit.Runtime.Scripts.Audio;
using LiveKit.Scripts.Audio;
using RichTypes;
using UnityEngine;
using UnityEngine.UI;

namespace Examples
{
    public class MicrophoneDropdown
    {
        private const string Key = "MicrophoneSelection";

        public static Result<MicrophoneSelection> CurrentMicrophoneSelection()
        {
            if (PlayerPrefs.HasKey(Key))
            {
                string raw = PlayerPrefs.GetString(Key)!;
                Result<MicrophoneSelection> result = MicrophoneSelection.FromName(raw);
                if (result.Success)
                    return Result<MicrophoneSelection>.SuccessResult(result.Value);
                Debug.LogError(
                    $"Microphone selection creation from saved failed, fallback to default microphone: {result.ErrorMessage}");
            }

            return MicrophoneSelection.Default();
        }

        public static void Bind(Dropdown dropdown, MicrophoneRtcAudioSource? microphoneRtcAudioSource = null)
        {
            List<string> options = MicrophoneAudioFilter.AvailableDeviceNamesOrEmpty().ToList();
            dropdown.options = options
                .Select(e => new Dropdown.OptionData(e))
                .ToList();

            Result<MicrophoneSelection> currentMicrophone = CurrentMicrophoneSelection();
            if (currentMicrophone.Success)
            {
                int index = options.IndexOf(currentMicrophone.Value.name);
                if (index != -1) dropdown.value = index;
            }

            dropdown.onValueChanged!.AddListener(index =>
            {
                PlayerPrefs.SetString(Key, dropdown.options[index]!.text!);
                Result<MicrophoneSelection> newMicrophoneResult = CurrentMicrophoneSelection();
                if (newMicrophoneResult.Success == false)
                {
                    Debug.LogError($"Cannot find just selected microphone: {newMicrophoneResult.ErrorMessage}");
                    return;
                }

                Result? result = microphoneRtcAudioSource?.SwitchMicrophone(newMicrophoneResult.Value);
                if (result is { Success: false })
                    Debug.LogError($"Cannot switch to microphone: {result.Value.ErrorMessage}");
            });
        }

    }
}