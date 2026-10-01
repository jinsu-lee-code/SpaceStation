using TMPro;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 버튼 위에 뜨는 툴팁. 피벗은 하단 중앙, 크기는 ContentSizeFitter가 맞춘다.
    /// 레이캐스트를 막지 않아야 한다 (CanvasGroup.blocksRaycasts = false).
    /// </summary>
    public sealed class TooltipView : MonoBehaviour
    {
        [SerializeField] private RectTransform _root;
        [SerializeField] private TMP_Text _text;
        [SerializeField] private float _gap = 8f;

        private void Awake()
        {
            if (_root == null)
                _root = (RectTransform)transform;
            Hide();
        }

        public void Show(string text, RectTransform anchor)
        {
            _text.SetText(text);
            _root.gameObject.SetActive(true);
            _root.pivot = new Vector2(0.5f, 0f);

            var rect = anchor.rect;
            Vector3 top = anchor.TransformPoint(new Vector3(rect.center.x, rect.yMax, 0f));
            _root.position = top + anchor.up * (_gap * anchor.lossyScale.y);
        }

        /// <summary>anchor의 왼쪽에 위쪽을 맞춰 띄운다 (화면 오른쪽 패널용, 5-8).</summary>
        public void ShowLeftOf(string text, RectTransform anchor)
        {
            _text.SetText(text);
            _root.gameObject.SetActive(true);
            _root.pivot = new Vector2(1f, 1f);
            var rect = anchor.rect;
            Vector3 topLeft = anchor.TransformPoint(new Vector3(rect.xMin, rect.yMax, 0f));
            _root.position = topLeft - anchor.right * (_gap * anchor.lossyScale.x);
        }

        public void Hide()
        {
            _root.gameObject.SetActive(false);
        }
    }
}
