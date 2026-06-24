using Google.MaterialDesign.Icons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ParticipantTile : MonoBehaviour
{
    public RawImage Image;
    public TMP_Text Label;
    public MaterialIcon MicIcon;

    [Header("Speaking highlight")]
    [SerializeField] private Color speakingColor = new Color(0.2f, 0.78f, 0.4f, 1f);
    [SerializeField] private float speakingBorderThickness = 6f;

    private ResizeTextureController _controller;
    private Texture _placeholder;
    private bool _showingLive;

    private RectTransform _speakingBorder;
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
    /// The border is built lazily on first use, so no prefab wiring is required.
    /// </summary>
    public void SetSpeaking(bool speaking)
    {
        if (_speaking == speaking) return;
        _speaking = speaking;

        if (_speakingBorder == null)
        {
            if (!speaking) return;
            _speakingBorder = CreateSpeakingBorder();
        }

        _speakingBorder.gameObject.SetActive(speaking);
    }

    private RectTransform CreateSpeakingBorder()
    {
        var container = new GameObject("SpeakingBorder", typeof(RectTransform));
        var rt = (RectTransform)container.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetAsLastSibling(); // draw on top of the video and the label

        float t = speakingBorderThickness;
        // top
        AddEdge(rt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -t), Vector2.zero);
        // bottom
        AddEdge(rt, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, t));
        // left
        AddEdge(rt, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(t, 0));
        // right
        AddEdge(rt, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-t, 0), Vector2.zero);

        return rt;
    }

    private void AddEdge(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)edge.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;

        var image = edge.GetComponent<Image>();
        image.color = speakingColor;
        image.raycastTarget = false;
    }

    private void Update() => _controller?.Resize();

    private void OnDestroy()
    {
        _controller?.Dispose();
        _controller = null;
    }
}
