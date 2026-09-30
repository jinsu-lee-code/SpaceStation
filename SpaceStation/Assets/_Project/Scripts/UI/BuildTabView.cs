using SpaceStation.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>건설 메뉴 위 탭 버튼 하나 (4-5).</summary>
    public sealed class BuildTabView : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Color _normalColor = new Color(0.14f, 0.16f, 0.2f, 0.92f);
        [SerializeField] private Color _selectedColor = new Color(0.25f, 0.55f, 0.9f, 1f);

        public ModuleCategory Category { get; private set; }

        public void Initialize(BuildMenu menu, ModuleCategory category, int moduleCount)
        {
            Category = category;
            name = "Tab_" + category;
            _label.SetText($"{category.DisplayName()} <size=75%><color={HudText.Muted}>{moduleCount}</color></size>");
            _button.onClick.AddListener(() => menu.HandleTabClicked(Category));
            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            if (_button.targetGraphic != null)
                _button.targetGraphic.color = selected ? _selectedColor : _normalColor;
            _label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
        }
    }
}
