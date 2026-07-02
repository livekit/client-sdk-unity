using UnityEngine;
using UnityEngine.UI;

namespace AgentsRPG
{
    public class ChatLog : MonoBehaviour
    {
        [SerializeField] ScrollRect _scrollRect;
        [SerializeField] RectTransform _bubbleContainer;
        [SerializeField] ChatBubble _playerBubblePrefab;
        [SerializeField] ChatBubble _npcBubblePrefab;
        [SerializeField] float _bubbleSpacingFactor = 0.2f;

        public ChatBubble AddBubble(Speaker speaker)
        {
            var row = new GameObject(
                "BubbleRow",
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup),
                typeof(DynamicRowHeight),
                typeof(ContentSizeFitter));
            var rowTransform = (RectTransform)row.transform;
            rowTransform.SetParent(_bubbleContainer, false);

            var rowLayout = row.GetComponent<DynamicRowHeight>();
            rowLayout.flexibleWidth = 1;
            rowLayout.ExtraHeightFactor = _bubbleSpacingFactor;

            var fitter = row.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = speaker == Speaker.Player ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var prefab = speaker == Speaker.Player ? _playerBubblePrefab : _npcBubblePrefab;
            var bubble = Instantiate(prefab, rowTransform);
            bubble.TextChanged += ScrollToBottom;

            ScrollToBottom();
            return bubble;
        }

        public void ScrollToBottom()
        {
            if (_scrollRect == null) return;
            if (_bubbleContainer != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_bubbleContainer);
            }
            Canvas.ForceUpdateCanvases();
            _scrollRect.verticalNormalizedPosition = 0f;
        }

        public void ClearBubbles()
        {
            for (int i = _bubbleContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(_bubbleContainer.GetChild(i).gameObject);
            }
        }
    }
}
