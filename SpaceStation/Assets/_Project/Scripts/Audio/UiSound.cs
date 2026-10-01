using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpaceStation.Audio
{
    /// <summary>
    /// 버튼 소리: 마우스 올림 → 작은 틱, 클릭 → 클릭음, 비활성 버튼 클릭 → 거절음.
    /// 클릭 결과에 전용 소리가 있는 버튼(건설·탭·배속)은 Silent로 두어 소리가 겹치지 않게 한다.
    /// </summary>
    public sealed class UiSound : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        public enum ClickMode
        {
            Click,
            Silent,
        }

        [SerializeField] private ClickMode _click = ClickMode.Click;

        private Selectable _selectable;

        public ClickMode Mode
        {
            get => _click;
            set => _click = value;
        }

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
        }

        private bool Interactable => _selectable == null || _selectable.IsInteractable();

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Interactable)
                AudioService.TryPlay(l => l.UiHover);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            if (!Interactable)
                AudioService.TryPlay(l => l.UiError);
            else if (_click == ClickMode.Click)
                AudioService.TryPlay(l => l.UiClick);
        }
    }
}
