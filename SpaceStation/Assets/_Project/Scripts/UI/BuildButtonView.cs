using SpaceStation.Data;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>건설 메뉴 버튼 하나. 이름·단축키·비용 표시, 호버 시 툴팁, 비용 부족 시 비활성.</summary>
    public sealed class BuildButtonView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Button _button;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Color _normalColor = new Color(0.18f, 0.2f, 0.24f, 0.92f);
        [SerializeField] private Color _selectedColor = new Color(0.25f, 0.55f, 0.9f, 1f);

        private BuildMenu _menu;

        public ModuleData Data { get; private set; }

        private int _hotkey;
        private string _shownStatus;

        public void Initialize(BuildMenu menu, ModuleData data, int hotkey)
        {
            _menu = menu;
            Data = data;
            _hotkey = hotkey;
            name = "Build_" + data.name;
            _button.onClick.AddListener(() => _menu.HandleButtonClicked(Data));
            SetState(true, null);
            SetSelected(false);
        }

        /// <param name="interactable">해금 + 설치 가능 + 비용 충분</param>
        /// <param name="blockedStatus">둘째 줄에 비용 대신 표시할 사유 (잠김/최대). null이면 비용 표시</param>
        public void SetState(bool interactable, string blockedStatus)
        {
            _button.interactable = interactable;
            if (_shownStatus == blockedStatus && _shownStatus != null)
                return;
            _shownStatus = blockedStatus;
            string second = blockedStatus ?? HudText.Cost(Data.BuildCost);
            _label.SetText($"<size=75%><color={HudText.Muted}>{_hotkey}</color></size>  {Data.DisplayName}\n<size=80%>{second}</size>");
        }

        public void SetSelected(bool selected)
        {
            if (_button.targetGraphic != null)
                _button.targetGraphic.color = selected ? _selectedColor : _normalColor;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _menu.ShowTooltip(Data, (RectTransform)transform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _menu.HideTooltip();
        }
    }
}
