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

        public void Initialize(BuildMenu menu, ModuleData data, int hotkey)
        {
            _menu = menu;
            Data = data;
            name = "Build_" + data.name;
            _label.SetText($"<size=75%><color={HudText.Muted}>{hotkey}</color></size>  {data.DisplayName}\n<size=80%>{HudText.Cost(data.BuildCost)}</size>");
            _button.onClick.AddListener(() => _menu.HandleButtonClicked(Data));
            SetSelected(false);
        }

        public void SetAffordable(bool affordable)
        {
            _button.interactable = affordable;
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
