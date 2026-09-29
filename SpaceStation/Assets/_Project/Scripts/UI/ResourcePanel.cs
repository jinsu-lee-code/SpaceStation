using System.Text;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 우측 자원 패널: 전력(공급/수요/효율), 스톡 자원(재고/한도/순수지 + 생산·소비), 인구·만족도, 경고.
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

        [Header("Population")]
        [SerializeField] private TMP_Text _populationText;

        [Header("Grade")]
        [SerializeField] private ProgressionController _progression;
        [SerializeField] private TMP_Text _gradeText;

        private readonly StringBuilder _sb = new StringBuilder(1024);
        private readonly StringBuilder _popSb = new StringBuilder(256);
        private ResourceSimulation _sim;
        private PopulationSimulation _population;
        private bool _dirty = true;

        private void Start()
        {
            _sim = _resources.Simulation;
            _population = _resources.Population;
            _sim.Changed += MarkDirty;
            _population.Changed += MarkDirty;
            _station.Connectivity.ActiveStateChanged += HandleActiveStateChanged;
            if (_progression != null)
                _progression.Changed += MarkDirty;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_sim != null)
                _sim.Changed -= MarkDirty;
            if (_population != null)
                _population.Changed -= MarkDirty;
            if (_progression != null)
                _progression.Changed -= MarkDirty;
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
            if (eff < 1f) // 전력을 쓰는 모듈의 생산·소비가 효율만큼 함께 줄어듦을 알림
                _sb.Append("<size=80%><color=").Append(effColor).Append("><pos=30%>전력 부족: 전력 사용 모듈 가동률 ")
                   .Append((eff * 100f).ToString("0")).Append("%</color></size>\n");

            // 4-2: 배터리 + 낮/밤
            if (_sim.BatteryCapacity > 0f)
            {
                float flow = _sim.BatteryFlow;
                string flowColor = flow < 0f ? HudText.Orange : "#7CFF9A";
                _sb.Append("<pos=30%>배터리 ").Append(_sim.BatteryCharge.ToString("0")).Append(" / ").Append(_sim.BatteryCapacity.ToString("0"))
                   .Append("<pos=72%><color=").Append(flowColor).Append('>').Append(flow.ToString("+0.#;-0.#;0")).Append("/s</color>\n");
            }
            var cycle = _resources.DayNight;
            if (cycle.Enabled)
            {
                float time = _resources.ElapsedSeconds;
                bool day = cycle.IsDay(time);
                int remaining = Mathf.CeilToInt(cycle.TimeUntilPhaseChange(time));
                _sb.Append("<pos=30%>").Append(day ? "<color=#FFE08A>낮</color>" : "<color=#8FA8FF>밤</color>")
                   .Append(" · ").Append(day ? "밤까지 " : "낮까지 ").Append(remaining).Append("초");
                float solar = cycle.SolarMultiplier(time);
                if (solar < 0.999f)
                    _sb.Append("  <size=80%><color=").Append(HudText.Muted).Append(">태양광 ").Append((solar * 100f).ToString("0")).Append("%</color></size>");
                _sb.Append('\n');
            }
            if (_sim.StoppedModuleCount > 0)
                _sb.Append("<color=").Append(HudText.Red).Append(">입력 자원 부족으로 정지: ").Append(_sim.StoppedModuleCount).Append("개</color>\n");
            _sb.Append('\n');

            foreach (var type in StockOrder)
                AppendStockRow(type);

            int inactive = _station.Grid.ModuleCount - _station.Connectivity.ActiveCount;
            if (inactive > 0)
                _sb.Append("<color=").Append(HudText.Orange).Append(">코어와 분리된 모듈: ").Append(inactive).Append("개 (비활성)</color>\n");

            // 4-3: 노후 모듈 (효율 저하 중)
            int worn = 0;
            var durability = _resources.Durability;
            foreach (var info in durability.Modules)
            {
                if (durability.EfficiencyFor(info.Current) < 1f)
                    worn++;
            }
            if (worn > 0)
                _sb.Append("<color=").Append(HudText.Yellow).Append(">노후 모듈(효율 저하): ").Append(worn).Append("개 · 정비(M) 또는 재건축(B)</color>");

            _body.SetText(_sb);

            if (_populationText != null)
                RefreshPopulation();
            if (_gradeText != null && _progression != null)
                RefreshGrade();
        }

        private void RefreshGrade()
        {
            var p = _progression.Progression;
            _popSb.Clear();
            _popSb.Append("<b>등급</b><pos=30%><b>").Append(p.Current.DisplayName).Append("</b>");
            if (p.LimitedModule != null)
            {
                _popSb.Append("<pos=62%><size=85%>").Append(p.LimitedModule.DisplayName).Append(' ')
                      .Append(p.CountLimited(_station.Grid)).Append('/').Append(p.Current.MaxLimitedModules).Append("</size>");
            }
            _popSb.Append('\n');

            var next = p.Next;
            _popSb.Append("<size=80%><color=").Append(HudText.Muted).Append('>');
            if (next == null)
            {
                _popSb.Append("최고 등급 달성");
            }
            else
            {
                int pop = _sim.Population, modules = _station.Grid.ModuleCount;
                _popSb.Append("다음 ").Append(next.DisplayName).Append(":  인구 ");
                AppendProgress(pop, next.MinPopulation);
                _popSb.Append("  ·  모듈 ");
                AppendProgress(modules, next.MinModules);
            }
            _popSb.Append("</color></size>");
            _gradeText.SetText(_popSb);
        }

        private void AppendProgress(int value, int target)
        {
            if (value >= target)
                _popSb.Append("<color=#7CFF9A>");
            _popSb.Append(value).Append('/').Append(target);
            if (value >= target)
                _popSb.Append("</color>");
        }

        private void RefreshPopulation()
        {
            var pop = _population;
            _popSb.Clear();

            _popSb.Append("<b>인구</b><pos=30%>");
            if (pop.IsOvercrowded)
                _popSb.Append("<color=").Append(HudText.Red).Append('>');
            _popSb.Append(_sim.Population).Append(" / ").Append(_sim.HousingCapacity);
            if (pop.IsOvercrowded)
                _popSb.Append("</color>");
            _popSb.Append('\n');

            float s = pop.Satisfaction;
            string sColor = s < _resources.Balance.LowSatisfactionThreshold ? HudText.Red
                : s < _resources.Balance.GrowthMinSatisfaction ? HudText.Yellow : "#FFFFFF";
            _popSb.Append("<b>만족도</b><pos=30%><color=").Append(sColor).Append('>').Append(s.ToString("0"))
                  .Append("</color><pos=72%>");
            if (pop.SatisfactionRate < 0f)
                _popSb.Append("<color=").Append(HudText.Red).Append('>');
            _popSb.Append(pop.SatisfactionRate.ToString("+0.0#;-0.0#;0")).Append("/s");
            if (pop.SatisfactionRate < 0f)
                _popSb.Append("</color>");
            _popSb.Append('\n');

            _popSb.Append("<size=80%><color=").Append(HudText.Muted).Append('>');
            if (pop.IsGrowing)
            {
                _popSb.Append("다음 주민 ").Append((pop.GrowthProgress * 100f).ToString("0")).Append("%  (")
                      .Append(pop.GetGrowthInterval(s).ToString("0")).Append("초 주기)");
            }
            else if (pop.IsGrowthBlockedByShortage)
            {
                _popSb.Append("증가 정지: 산소·물·식량 고갈");
            }
            else if (_sim.Population >= _sim.HousingCapacity)
            {
                _popSb.Append("증가 정지: 수용 인구 가득 (거주 모듈 필요)");
            }
            else
            {
                _popSb.Append("증가 정지: 만족도 ").Append(_resources.Balance.GrowthMinSatisfaction.ToString("0")).Append(" 미만");
            }
            _popSb.Append("</color></size>");

            if (pop.IsLosingFromOxygen)
                _popSb.Append("\n<color=").Append(HudText.Red).Append(">산소 고갈: 인구 감소 중</color>");
            if (pop.IsLosingFromLowSatisfaction)
                _popSb.Append("\n<color=").Append(HudText.Red).Append(">만족도 낮음: 주민 이탈 중</color>");
            if (pop.IsOvercrowded)
                _popSb.Append("\n<color=").Append(HudText.Red).Append(">수용 인구 초과: 인구 감소 중</color>");

            _populationText.SetText(_popSb);
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
