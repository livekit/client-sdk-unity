using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows the local microphone's mute state by color and label and reports clicks; the
// session owns the actual mute state and pushes it back via SetMuted.
public class MuteButton : MonoBehaviour
{
    [SerializeField] Button _button;
    [SerializeField] Image _background;
    [SerializeField] TMP_Text _label;
    [SerializeField] Color _unmutedColor = new Color(0.2f, 0.65f, 0.3f);
    [SerializeField] Color _mutedColor = new Color(0.8f, 0.2f, 0.2f);
    [SerializeField] string _unmutedText = "Mic On";
    [SerializeField] string _mutedText = "Muted";

    public event Action Clicked;

    public bool Interactable
    {
        set => _button.interactable = value;
    }

    void Awake()
    {
        _button.onClick.AddListener(() => Clicked?.Invoke());
    }

    public void SetMuted(bool muted)
    {
        _background.color = muted ? _mutedColor : _unmutedColor;
        _label.text = muted ? _mutedText : _unmutedText;
    }
}
