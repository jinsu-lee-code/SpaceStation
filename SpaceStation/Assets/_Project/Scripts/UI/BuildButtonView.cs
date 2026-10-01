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
        [Tooltip("5-8 모듈 썸네일 (ModuleData.Icon)")]
        [SerializeField] private Image _thumb;

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
            if (_thumb != null)
            {
                _thumb.sprite = data.Icon;
                _thumb.enabled = data.Icon != null;
                _thumb.preserveAspect = true;
            }
            _button.onClick.AddListener(() => _menu.HandleButtonClicked(Data));
            SetState(true, null);
            SetSelected(false);
        }

        /// <param name="interactable">해금 + 설치 가능 + 비용 충분</param>
        /// <param name="blockedStatus">둘째 줄에 비용 대신 표시할 사유 (잠김/최대). null이면 비용 표시</param>
        public void SetState(bool interactable, string blockedStatus)
        {
            _button.interactable = interactable;
            if (_thumb != null)
                _thumb.color = interactable ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            if (_shownStatus == blockedStatus && _shownStatus != null)
                return;
            _shownStatus = blockedStatus;
            string second = blockedStatus != null ? $"<color={HudText.Orange}>{blockedStatus}</color>" : HudText.Cost(Data.BuildCost);
            _label.SetText($"<size=72%><color={HudTheme.AccentHex}>{_hotkey}</color></size> {Data.DisplayName}\n<size=78%>{second}</size>");
        }

        public void SetSelected(bool selected)
        {
            if (_button.targetGraphic != null)
                _button.targetGraphic.color = selected ? HudTheme.ButtonSelected : HudTheme.ButtonNormal;
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
