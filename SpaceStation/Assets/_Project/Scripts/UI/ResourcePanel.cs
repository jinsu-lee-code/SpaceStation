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

        [Header("Hover (5-8)")]
        [Tooltip("자원 줄에 마우스를 올리면 생산·소비 상세를 보여줄 툴팁")]
        [SerializeField] private TooltipView _tooltip;
        private string _hoverLink;

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
            UpdateHover();
        }

        /// <summary>자원 줄(&lt;link&gt;) 위에 마우스가 있으면 상세 툴팁을 패널 왼쪽에 띄운다.</summary>
        private void UpdateHover()
        {
            if (_tooltip == null)
                return;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            string link = null;
            if (mouse != null)
            {
                Vector2 pos = mouse.position.ReadValue();
                link = FindLink(_body, pos) ?? FindLink(_populationText, pos);
            }
            if (link == null)
            {
                if (_hoverLink != null)
                    _tooltip.Hide();
                _hoverLink = null;
                return;
            }
            _hoverLink = link;
            _tooltip.ShowLeftOf(HoverText(link), (RectTransform)transform);
        }

        private static string FindLink(TMP_Text text, Vector2 screen)
        {
            if (text == null || !text.isActiveAndEnabled)
                return null;
            int index = TMP_TextUtilities.FindIntersectingLink(text, screen, null);
            return index >= 0 ? text.textInfo.linkInfo[index].GetLinkID() : null;
        }

        private string HoverText(string link)
        {
            if (link == "power")
            {
                return $"<b>{HudTheme.Icon("power")} 전력</b>\n공급 {_sim.PowerSupply:0.#}  ·  수요 {_sim.PowerDemand:0.#}\n" +
                       $"<color={HudText.Muted}>효율이 100% 미만이면 전력을 쓰는 모듈의 생산·소비가 함께 줄어듭니다</color>";
            }
            if (link == "population")
            {
                return $"<b>{HudTheme.Icon("population")} 인구</b>\n주민 {_sim.Population} / 수용 {_sim.HousingCapacity}\n" +
                       $"<color={HudText.Muted}>거주 모듈로 수용 인구를 늘리고, 만족도가 높을수록 빨리 늘어납니다</color>";
            }
            foreach (var type in StockOrder)
            {
                if (link != HudTheme.IconName(type))
                    continue;
                return $"<b>{HudTheme.Icon(type)} {HudText.ResourceName(type)}</b>\n" +
                       $"재고 {_sim.GetStock(type):0.0} / 한도 {_sim.GetCapacity(type):0}\n" +
                       $"<color={HudTheme.GreenHex}>생산 +{_sim.GetProduction(type):0.0#}/s</color>   <color={HudText.Red}>소비 -{_sim.GetConsumption(type):0.0#}/s</color>\n" +
                       $"<color={HudText.Muted}>창고 모듈로 한도를 늘릴 수 있습니다</color>";
            }
            return string.Empty;
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
            // 5-8: 아이콘 + 이름 / 값 / 증감 3열, 보조 정보는 작고 흐리게. 상세는 마우스를 올리면 툴팁
            float eff = _sim.PowerEfficiency;
            string effColor = eff < 0.5f ? HudText.Red : eff < 1f ? HudText.Yellow : HudTheme.GreenHex;
            _sb.Append("<link=power>").Append(HudTheme.Icon("power")).Append(" <b>전력</b><pos=36%>")
               .Append(_sim.PowerSupply.ToString("0.#")).Append("<size=78%><color=").Append(HudText.Muted).Append("> 공급  / </color></size>")
               .Append(_sim.PowerDemand.ToString("0.#")).Append("<size=78%><color=").Append(HudText.Muted).Append("> 수요</color></size>")
               .Append("<pos=80%><color=").Append(effColor).Append('>').Append((eff * 100f).ToString("0")).Append("%</color></link>\n");
            if (eff < 1f) // 전력을 쓰는 모듈의 생산·소비가 효율만큼 함께 줄어듦을 알림
                _sb.Append("<size=80%><color=").Append(effColor).Append("><pos=36%>전력 부족 · 모듈 가동률 ")
                   .Append((eff * 100f).ToString("0")).Append("%</color></size>\n");

            // 4-2: 배터리 + 낮/밤
            if (_sim.BatteryCapacity > 0f)
            {
                float flow = _sim.BatteryFlow;
                string flowColor = flow < 0f ? HudText.Orange : HudTheme.GreenHex;
                _sb.Append("<size=88%>").Append(HudTheme.Icon("battery")).Append(" 배터리<pos=36%>").Append(_sim.BatteryCharge.ToString("0"))
                   .Append("<color=").Append(HudText.Muted).Append("> / ").Append(_sim.BatteryCapacity.ToString("0")).Append("</color>")
                   .Append("<pos=80%><color=").Append(flowColor).Append('>').Append(flow.ToString("+0.#;-0.#;0")).Append("/s</color></size>\n");
            }
            var cycle = _resources.DayNight;
            if (cycle.Enabled)
            {
                float time = _resources.ElapsedSeconds;
                bool day = cycle.IsDay(time);
                int remaining = Mathf.CeilToInt(cycle.TimeUntilPhaseChange(time));
                _sb.Append("<size=88%>").Append(HudTheme.Icon(day ? "sun" : "moon")).Append(day ? " 낮" : " 밤")
                   .Append("<pos=36%><color=").Append(HudText.Muted).Append('>').Append(day ? "밤까지 " : "낮까지 ").Append(remaining).Append("초</color>");
                float solar = cycle.SolarMultiplier(time);
                if (solar < 0.999f)
                    _sb.Append("<pos=80%><color=").Append(HudText.Muted).Append(">태양광 ").Append((solar * 100f).ToString("0")).Append("%</color>");
                _sb.Append("</size>\n");
            }
            // 4-10: 실패 조건 진행 중 경고 (남은 시간)
            var failure = _resources.Failure;
            AppendFailureWarning(failure.OxygenRemaining, "산소 고갈");
            AppendFailureWarning(failure.SatisfactionRemaining, "만족도 0 · 폭동");
            AppendFailureWarning(failure.CoreRemaining, "코어 주변 전부 파손");
            if (_sim.StoppedModuleCount > 0)
                _sb.Append("<color=").Append(HudText.Red).Append(">입력 자원 부족으로 정지: ").Append(_sim.StoppedModuleCount).Append("개</color>\n");
            _sb.Append("<size=40%>\n</size>"); // 구역 사이 작은 간격

            foreach (var type in StockOrder)
                AppendStockRow(type);
            _sb.Append("<size=40%>\n</size>");

            int inactive = _station.Grid.ModuleCount - _station.Connectivity.ActiveCount;
            if (inactive > 0)
                _sb.Append("<color=").Append(HudText.Orange).Append(">코어와 분리된 모듈: ").Append(inactive).Append("개 (비활성)</color>\n");

            // 4-6: 수리 슬롯 (코어 1 + 정비 베이)
            var damage = _resources.Damage;
            int repairing = damage.RepairingCount, capacity = damage.RepairCapacity, waiting = damage.Queue.Count;
            string slotColor = waiting > 0 ? HudText.Red : repairing >= capacity && repairing > 0 ? HudText.Yellow : HudText.Muted;
            _sb.Append("<size=88%>").Append(HudTheme.Icon("repair")).Append(" 수리 슬롯<pos=36%><color=").Append(slotColor).Append('>')
               .Append(repairing).Append(" / ").Append(capacity);
            if (waiting > 0)
                _sb.Append("  · 대기 ").Append(waiting).Append(" (정비 베이로 슬롯 추가)");
            _sb.Append("</color></size>\n");

            // 4-3: 노후 모듈 (효율 저하 중)
            int worn = 0;
            var durability = _resources.Durability;
            foreach (var info in durability.Modules)
            {
                if (durability.EfficiencyFor(info.Current) < 1f)
                    worn++;
            }
            if (worn > 0)
                _sb.Append("<color=").Append(HudText.Yellow).Append(">노후 모듈(효율 저하): ").Append(worn).Append("개 · 정비(").Append(KeyBindings.Label(GameAction.Maintain))
                   .Append(") 또는 재건축(").Append(KeyBindings.Label(GameAction.Rebuild)).Append(")</color>");

            _body.SetText(_sb);

            if (_populationText != null)
                RefreshPopulation();
            if (_gradeText != null && _progression != null)
                RefreshGrade();
        }

        private void AppendFailureWarning(float remaining, string what)
        {
            if (remaining < 0f)
                return;
            _sb.Append("<color=").Append(HudText.Red).Append("><b>").Append(HudTheme.Icon("warning")).Append(' ').Append(what).Append(" · ")
               .Append(Mathf.CeilToInt(remaining)).Append("초 후 게임 오버</b></color>\n");
        }

        private void RefreshGrade()
        {
            var p = _progression.Progression;
            _popSb.Clear();
            _popSb.Append("<size=118%>").Append(HudTheme.Icon("grade")).Append(" <b><color=").Append(HudTheme.AccentHex).Append('>')
                  .Append(p.Current.DisplayName).Append(" 정거장</color></b></size>");
            if (p.LimitedModule != null)
            {
                _popSb.Append("<pos=66%><size=80%><color=").Append(HudText.Muted).Append('>').Append(p.LimitedModule.DisplayName).Append(' ')
                      .Append(p.CountLimited(_station.Grid)).Append('/').Append(p.CurrentLimit(_station.Grid)).Append("</color></size>");
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
                int pop = _sim.Population, modules = StationProgression.CountGradeModules(_station.Grid); // 8-6: 장갑 격벽 제외
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

            _popSb.Append("<link=population>").Append(HudTheme.Icon("population")).Append(" <b>인구</b><pos=36%>");
            if (pop.IsOvercrowded)
                _popSb.Append("<color=").Append(HudText.Red).Append('>');
            _popSb.Append(_sim.Population);
            if (pop.IsOvercrowded)
                _popSb.Append("</color>");
            _popSb.Append("<size=78%><color=").Append(HudText.Muted).Append("> / ").Append(_sim.HousingCapacity).Append("</color></size></link>\n");

            float s = pop.Satisfaction;
            string sColor = s < _resources.Balance.LowSatisfactionThreshold ? HudText.Red
                : s < _resources.Balance.GrowthMinSatisfaction ? HudText.Yellow : HudTheme.TextHex;
            _popSb.Append(HudTheme.Icon("satisfaction")).Append(" <b>만족도</b><pos=36%><color=").Append(sColor).Append('>').Append(s.ToString("0"))
                  .Append("</color><pos=80%>");
            string rateColor = pop.SatisfactionRate < -0.001f ? HudText.Red : pop.SatisfactionRate > 0.001f ? HudTheme.GreenHex : HudText.Muted;
            _popSb.Append("<color=").Append(rateColor).Append('>').Append(pop.SatisfactionRate.ToString("+0.0#;-0.0#;0")).Append("/s</color>\n");

            // 4-9: 거주자 요구 충족 (등급이 오르면 생김) → 만족도 상한
            var needs = _resources.Needs;
            if (needs.Statuses.Count > 0)
            {
                _popSb.Append("<size=85%><color=").Append(HudText.Muted).Append(">요구</color><pos=36%>");
                for (int i = 0; i < needs.Statuses.Count; i++)
                {
                    var n = needs.Statuses[i];
                    float ratio = n.Ratio;
                    string c = ratio >= 0.999f ? "#7CFF9A" : ratio >= 0.5f ? HudText.Yellow : HudText.Red;
                    if (i > 0)
                        _popSb.Append("  ");
                    _popSb.Append(n.Need.DisplayName()).Append(" <color=").Append(c).Append('>')
                          .Append(Mathf.FloorToInt(n.Served + 1e-3f)).Append('/').Append(Mathf.CeilToInt(n.Demand - 1e-3f)).Append("</color>");
                }
                if (needs.SatisfactionCap < PopulationSimulation.MaxSatisfaction - 0.5f)
                    _popSb.Append("<pos=80%><color=").Append(HudText.Yellow).Append(">상한 ").Append(needs.SatisfactionCap.ToString("0")).Append("</color>");
                _popSb.Append("</size>\n");
            }

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
            else if (needs.SatisfactionCap < _resources.Balance.GrowthMinSatisfaction)
            {
                _popSb.Append("증가 정지: 요구 미충족으로 만족도 상한 ").Append(needs.SatisfactionCap.ToString("0"))
                      .Append(" (의료·여가 모듈을 거주 모듈 근처에)");
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

            float capacity = _sim.GetCapacity(type);
            // 재고 20% 미만은 노랑으로 미리 경고
            string stockColor = depleted ? HudText.Red : capacity > 0f && stock < capacity * 0.2f ? HudText.Yellow : HudTheme.TextHex;
            _sb.Append("<link=").Append(HudTheme.IconName(type)).Append('>').Append(HudTheme.Icon(type)).Append(" <b>")
               .Append(HudText.ResourceName(type)).Append("</b><pos=36%><color=").Append(stockColor).Append('>')
               .Append(stock.ToString("0")).Append("</color><size=78%><color=").Append(HudText.Muted).Append("> / ")
               .Append(capacity.ToString("0")).Append("</color></size><pos=80%>");
            string netColor = net < -0.001f ? HudText.Red : net > 0.001f ? HudTheme.GreenHex : HudText.Muted;
            _sb.Append("<color=").Append(netColor).Append('>').Append(net.ToString("+0.0;-0.0;0")).Append("/s</color>");
            if (depleted && type != ResourceType.Metal)
                _sb.Append(" <color=").Append(HudText.Red).Append("><b>고갈</b></color>");
            _sb.Append("</link>\n");
        }
    }
}
