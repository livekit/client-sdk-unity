using UnityEngine;
using UnityEngine.UI;

public class MaxWidthLayoutElement : LayoutElement
{
    [SerializeField] float _maxWidth = 400f;

    public override float preferredWidth
    {
        get
        {
            var group = GetComponent<HorizontalLayoutGroup>();
            if (group == null) return base.preferredWidth;
            return Mathf.Min(group.preferredWidth, _maxWidth);
        }
    }
}
