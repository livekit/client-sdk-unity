using Google.MaterialDesign.Icons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ParticipantTile : MonoBehaviour
{
    public RawImage Image;
    public TMP_Text Label;
    public MaterialIcon MicIcon;

    private ResizeTextureController _controller;
    private Texture _placeholder;
    private bool _showingLive;

    private void Awake()
    {
        SetMicMuted(true);
    }

    public void SetPlaceholder(Texture tex)
    {
        _placeholder = tex;
        if (!_showingLive) Image.texture = tex;
    }

    public void BindLiveSource(Texture source)
    {
        _controller?.Dispose();
        var rect = ((RectTransform)transform).rect;
        _controller = new ResizeTextureController(
            source, rect.width, rect.height,
            ResizeTextureController.CropMode.FillCrop);
        _showingLive = true;
        Image.texture = _controller.GetTargetTexture();
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

    private void Update() => _controller?.Resize();

    private void OnDestroy()
    {
        _controller?.Dispose();
        _controller = null;
    }
}
