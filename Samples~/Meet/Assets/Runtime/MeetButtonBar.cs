using System;
using Google.MaterialDesign.Icons;
using UnityEngine;
using UnityEngine.UI;

public class MeetButtonBar : MonoBehaviour
{
    private const string MicOnIcon = "e029";
    private const string MicOffIcon = "e02b";
    private const string CamOnIcon = "e04b";
    private const string CamOffIcon = "e04c";

    [SerializeField] private Button cameraButton;
    [SerializeField] private Button microphoneButton;
    [SerializeField] private Button startCallButton;
    [SerializeField] private Button endCallButton;
    [SerializeField] private Button publishDataButton;

    public event Action StartCallClicked;
    public event Action EndCallClicked;
    public event Action ToggleCameraClicked;
    public event Action ToggleMicrophoneClicked;
    public event Action PublishDataClicked;

    private void Awake()
    {
        startCallButton.onClick.AddListener(() => StartCallClicked?.Invoke());
        endCallButton.onClick.AddListener(() => EndCallClicked?.Invoke());
        cameraButton.onClick.AddListener(() => ToggleCameraClicked?.Invoke());
        microphoneButton.onClick.AddListener(() => ToggleMicrophoneClicked?.Invoke());
        publishDataButton.onClick.AddListener(() => PublishDataClicked?.Invoke());
    }

    public void SetConnected(bool connected)
    {
        cameraButton.interactable = connected;
        microphoneButton.interactable = connected;
        endCallButton.interactable = connected;
        publishDataButton.interactable = connected;
        startCallButton.interactable = !connected;

        if (!connected)
        {
            SetIcon(microphoneButton, MicOffIcon);
            SetIcon(cameraButton, CamOffIcon);
        }
    }

    public void SetCameraOn(bool on) => SetIcon(cameraButton, on ? CamOnIcon : CamOffIcon);
    public void SetMicrophoneOn(bool on) => SetIcon(microphoneButton, on ? MicOnIcon : MicOffIcon);

    private static void SetIcon(Button button, string unicode)
    {
        button.GetComponentInChildren<MaterialIcon>().iconUnicode = unicode;
    }
}
