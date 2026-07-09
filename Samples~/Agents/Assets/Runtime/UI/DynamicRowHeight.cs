using UnityEngine;
using UnityEngine.UI;

public class DynamicRowHeight : LayoutElement
{
    [SerializeField] float _extraHeightFactor = 0.2f;

    public float ExtraHeightFactor
    {
        get => _extraHeightFactor;
        set
        {
            _extraHeightFactor = value;
            SetForRebuild();
        }
    }

    public override float preferredHeight
    {
        get
        {
            var group = GetComponent<HorizontalLayoutGroup>();
            if (group == null) return base.preferredHeight;
            return group.preferredHeight * (1f + _extraHeightFactor);
        }
    }

    void SetForRebuild()
    {
        if (!IsActive()) return;
        LayoutRebuilder.MarkLayoutForRebuild(transform as RectTransform);
    }
}
