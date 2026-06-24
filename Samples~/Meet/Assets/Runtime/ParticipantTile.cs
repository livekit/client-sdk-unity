using Google.MaterialDesign.Icons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ParticipantTile : MonoBehaviour
{
    public RawImage Image;
    public TMP_Text Label;
    public MaterialIcon MicIcon;
    public RectTransform SpeakingBorder;

    private ResizeTextureController _controller;
    private Texture _placeholder;
    private bool _showingLive;

    private bool _speaking;

    private void Awake()
    {
        SetMicMuted(true);
    }

    public void SetPlaceholder(Texture tex)
    {
        _placeholder = tex;
        if (!_showingLive) Image.texture = tex;
    }

    public void BindLiveSource(Texture source, int rotationDegrees = 0, bool mirror = false)
    {
        _controller?.Dispose();
        var rect = ((RectTransform)transform).rect;
        _controller = new ResizeTextureController(
            source, rect.width, rect.height,
            ResizeTextureController.CropMode.FillCrop,
            rotationDegrees, mirror);
        _showingLive = true;
        Image.texture = _controller.GetTargetTexture();
    }

    public void SetLiveRotation(int rotationDegrees, bool mirror)
    {
        _controller?.SetRotation(rotationDegrees, mirror);
    }

    public void ShowLive()
    {
        if (_controller == null) return;
        _showingLive = true;
        Image.texture = _controller.GetTargetTexture();
    }

    public void ShowPlaceholder()
    {
        _showingLive = false;
        Image.texture = _placeholder;
    }

    public void ClearLive()
    {
        _controller?.Dispose();
        _controller = null;
        _showingLive = false;
        Image.texture = _placeholder;
    }

    public void SetMicMuted(bool muted)
    {
        if (MicIcon == null) return;
        MicIcon.gameObject.SetActive(muted);
    }

    /// <summary>
    /// Highlights the tile with a colored border while the participant is speaking.
    /// </summary>
    public void SetSpeaking(bool speaking)
    {
        if (_speaking == speaking) return;
        _speaking = speaking;

        SpeakingBorder.gameObject.SetActive(speaking);
    }

    private void Update() => _controller?.Resize();

    private void OnDestroy()
    {
        _controller?.Dispose();
        _controller = null;
    }
}
