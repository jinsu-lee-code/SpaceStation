using System.Text;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 우측 자원 패널: 전력(공급/수요/효율), 스톡 자원(재고/한도/순수지 + 생산·소비), 인구, 경고.
    /// 값이 바뀐 프레임에만 문자열을 다시 만든다.
    /// </summary>
    public sealed class ResourcePanel : MonoBehaviour
    {
        private static readonly ResourceType[] StockOrder =
        {
            ResourceType.Oxygen, ResourceType.Water, ResourceType.Food, ResourceType.Metal,
        };

        [SerializeField] private ResourceController _resources;
        [SerializeField] private StationController _station;
        [SerializeField] private TMP_Text _body;

        [Header("Population (3-1 전까지 임시 디버그 버튼)")]
        [SerializeField] private TMP_Text _populationText;
        [SerializeField] private Button _populationMinus;
        [SerializeField] private Button _populationPlus;

        private readonly StringBuilder _sb = new StringBuilder(1024);
        private ResourceSimulation _sim;
        private bool _dirty = true;

        private void Start()
        {
            _sim = _resources.Simulation;
            _sim.Changed += MarkDirty;
            _station.Connectivity.ActiveStateChanged += HandleActiveStateChanged;
            if (_populationMinus != null)
                _populationMinus.onClick.AddListener(() => _sim.TryAdjustPopulation(-1));
            if (_populationPlus != null)
                _populationPlus.onClick.AddListener(() => _sim.TryAdjustPopulation(1));
            Refresh();
        }

        private void OnDestroy()
        {
            if (_sim != null)
                _sim.Changed -= MarkDirty;
            if (_station != null && _station.Connectivity != null)
                _station.Connectivity.ActiveStateChanged -= HandleActiveStateChanged;
        }

        private void LateUpdate()
        {
            if (_dirty)
                Refresh();
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        private void HandleActiveStateChanged(ModuleInstance module, bool active)
        {
            _dirty = true;
        }

        private void Refresh()
        {
            _dirty = false;
            _sb.Clear();

            // 전력: BALANCE 1번 UI 경고 (100% 미만 노랑, 50% 미만 빨강)
            float eff = _sim.PowerEfficiency;
            string effColor = eff < 0.5f ? HudText.Red : eff < 1f ? HudText.Yellow : "#FFFFFF";
            _sb.Append("<b>전력</b><pos=30%>공급 ").Append(_sim.PowerSupply.ToString("0.#"))
               .Append("  /  수요 ").Append(_sim.PowerDemand.ToString("0.#")).Append('\n');
            _sb.Append("<pos=30%><color=").Append(effColor).Append(">효율 ").Append((eff * 100f).ToString("0")).Append("%</color>\n");
            if (_sim.StoppedModuleCount > 0)
                _sb.Append("<color=").Append(HudText.Red).Append(">입력 자원 부족으로 정지: ").Append(_sim.StoppedModuleCount).Append("개</color>\n");
            _sb.Append('\n');

            foreach (var type in StockOrder)
                AppendStockRow(type);

            int inactive = _station.Grid.ModuleCount - _station.Connectivity.ActiveCount;
            if (inactive > 0)
                _sb.Append("<color=").Append(HudText.Orange).Append(">코어와 분리된 모듈: ").Append(inactive).Append("개 (비활성)</color>");

            _body.SetText(_sb);

            if (_populationText != null)
                _populationText.SetText("<b>인구</b>  {0:0} / {1:0}", _sim.Population, _sim.HousingCapacity);
        }

        private void AppendStockRow(ResourceType type)
        {
            float stock = _sim.GetStock(type);
            float net = _sim.GetNetRate(type);
            bool depleted = _sim.IsDepleted(type);

            _sb.Append("<b>").Append(HudText.ResourceName(type)).Append("</b><pos=30%>");
            if (depleted)
                _sb.Append("<color=").Append(HudText.Red).Append('>');
            _sb.Append(stock.ToString("0.0"));
            if (depleted)
                _sb.Append("</color>");
            _sb.Append(" / ").Append(_sim.GetCapacity(type).ToString("0"));
            _sb.Append("<pos=72%>");
            if (net < 0f)
                _sb.Append("<color=").Append(HudText.Red).Append('>');
            _sb.Append(net.ToString("+0.0#;-0.0#;0")).Append("/s");
            if (net < 0f)
                _sb.Append("</color>");
            if (depleted && type != ResourceType.Metal)
                _sb.Append("  <color=").Append(HudText.Red).Append(">고갈</color>");
            _sb.Append('\n');

            _sb.Append("<size=80%><color=").Append(HudText.Muted).Append("><pos=30%>생산 +")
               .Append(_sim.GetProduction(type).ToString("0.0#"))
               .Append("<pos=62%>소비 -").Append(_sim.GetConsumption(type).ToString("0.0#"))
               .Append("</color></size>\n");
        }
    }
}
