using System.Collections.Generic;
using System.Text;
using SpaceStation.Audio;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 연구 화면 구성 (11-13 패드 연구 탭 → 11-15 ② 바깥 연구 창과 공용): 왼쪽 = 분야 목록(레벨 · 진행 · 잠김), 오른쪽 = 고른 분야 상세
    /// (현재 · 다음 효과, 시작 비용 · 전력 · 시간, 진행 막대, 시작 · 취소). 정비 자동화는 완료 후 설정(자동 정비 · 재건축, 기준 · 보호선, 일괄 정비).
    /// 패드는 탭 안에, 바깥 <see cref="ResearchPanel"/>은 창 안에 조금 크게(배율) 넣는다. 크기: 너비 width, 높이 <see cref="Height"/>.
    /// </summary>
    public sealed class ResearchView
    {
        private readonly StationSimulation _sim;
        private readonly RectTransform _root;
        private readonly TMP_Text _labs;
        private readonly List<(ResearchCategoryData Category, Button Button, TMP_Text Title, TMP_Text Sub)> _rows =
            new List<(ResearchCategoryData, Button, TMP_Text, TMP_Text)>();
        private readonly TMP_Text _title, _body, _status;
        private readonly RectTransform _bar, _barFill;
        private readonly Button _start, _cancel, _point;
        private bool _usePoint; // 11-17 ② 연구 포인트로 시작 비용 할인
        private readonly RectTransform _auto;
        private readonly Button _autoMaintain, _autoRebuild, _batch;
        private readonly TMP_Text _threshold, _reserve, _autoStatus;
        private ResearchCategoryData _selected;

        /// <summary>맨 위(연구소 사용 줄)부터 목록 · 상세 아래 끝까지의 높이.</summary>
        public float Height { get; }

        /// <param name="top">맨 위 줄의 y (패드는 탭 줄 아래 72)</param>
        public ResearchView(HoloUi ui, RectTransform root, float width, StationSimulation sim, float top = 72f)
        {
            _root = root;
            _sim = sim;
            var accent = HudTheme.Accent;

            _labs = ui.Label(root, "", 14f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(_labs.rectTransform, new Vector2(18f, -top), new Vector2(width - 36f, 24f));

            // 왼쪽: 분야 목록
            const float x0 = 16f, listW = 290f, rowH = 46f; // 분야 7개가 아래 알림줄 위에 들어가게
            float listTop = -top - 28f;
            float y = listTop;
            foreach (var category in sim.Research.Categories)
            {
                var c = category;
                var b = ui.TechButton(root, "", 14f, () => Select(c));
                HoloUi.Place((RectTransform)b.transform, new Vector2(x0, y), new Vector2(listW, rowH));
                b.GetComponentInChildren<TMP_Text>().gameObject.SetActive(false); // 두 줄 글은 따로
                var title = ui.Label(b.transform, "", 16f, TextAlignmentOptions.MidlineLeft);
                HoloUi.Place(title.rectTransform, new Vector2(14f, -3f), new Vector2(listW - 28f, 22f));
                HoloUi.Glow(title, 0.35f);
                var sub = ui.Label(b.transform, "", 12f, TextAlignmentOptions.MidlineLeft);
                HoloUi.Place(sub.rectTransform, new Vector2(14f, -23f), new Vector2(listW - 28f, 18f));
                sub.overflowMode = TextOverflowModes.Ellipsis;
                _rows.Add((c, b, title, sub));
                y -= rowH + 5f;
            }

            // 오른쪽: 상세
            const float panelH = 336f;
            float px = x0 + listW + 14f, pw = width - px - 16f;
            var panel = HoloUi.Rect("ResearchPanel", root);
            HoloUi.Place(panel, new Vector2(px, listTop), new Vector2(pw, panelH));
            ui.TechPanel(panel.gameObject, new Color(0.03f, 0.1f, 0.15f, 0.88f), new Color(accent.r, accent.g, accent.b, 0.7f), "RESEARCH", 0.2f);
            _title = ui.Label(panel, "", 21f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(_title.rectTransform, new Vector2(16f, -24f), new Vector2(pw - 32f, 30f));
            HoloUi.Glow(_title, 0.55f);
            HoloChroma.Add(_title);
            _body = ui.Label(panel, "", 14f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(_body.rectTransform, new Vector2(16f, -60f), new Vector2(pw - 32f, 150f));
            _status = ui.Label(panel, "", 14f, TextAlignmentOptions.MidlineLeft, wrap: true);
            HoloUi.Place(_status.rectTransform, new Vector2(16f, -214f), new Vector2(pw - 32f, 40f));
            _bar = ui.Bar(panel, new Color(0.1f, 0.2f, 0.28f, 1f), accent, out _barFill);
            HoloUi.Place(_bar, new Vector2(16f, -258f), new Vector2(pw - 32f, 6f));
            _start = ui.TechButton(panel, "연구 시작", 16f, StartSelected);
            HoloUi.Place((RectTransform)_start.transform, new Vector2(pw - 176f, -276f), new Vector2(160f, 46f));
            _point = ui.TechButton(panel, "", 14f, () => { _usePoint = !_usePoint; Refresh(); });
            HoloUi.Place((RectTransform)_point.transform, new Vector2(pw - 352f, -276f), new Vector2(168f, 46f));
            _cancel = ui.TechButton(panel, "취소", 16f, CancelSelected);
            HoloUi.Place((RectTransform)_cancel.transform, new Vector2(pw - 176f, -276f), new Vector2(160f, 46f));

            // 정비 자동화 설정 (연구 완료 후)
            _auto = HoloUi.Rect("Automation", panel);
            HoloUi.Stretch(_auto);
            var auto = sim.Automation;
            _autoMaintain = ui.TechButton(_auto, "", 14f, () => { auto.AutoMaintain = !auto.AutoMaintain; Refresh(); });
            HoloUi.Place((RectTransform)_autoMaintain.transform, new Vector2(16f, -62f), new Vector2((pw - 40f) / 2f, 40f));
            _autoRebuild = ui.TechButton(_auto, "", 14f, () => { auto.AutoRebuild = !auto.AutoRebuild; Refresh(); });
            HoloUi.Place((RectTransform)_autoRebuild.transform, new Vector2(24f + (pw - 40f) / 2f, -62f), new Vector2((pw - 40f) / 2f, 40f));
            _threshold = ui.Label(_auto, "", 14f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(_threshold.rectTransform, new Vector2(16f, -112f), new Vector2(pw - 140f, 34f));
            Stepper(ui, _auto, new Vector2(pw - 108f, -112f), () => auto.Threshold -= 5f, () => auto.Threshold += 5f);
            _reserve = ui.Label(_auto, "", 14f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(_reserve.rectTransform, new Vector2(16f, -154f), new Vector2(pw - 140f, 34f));
            Stepper(ui, _auto, new Vector2(pw - 108f, -154f), () => auto.ReserveRatio -= 0.1f, () => auto.ReserveRatio += 0.1f);
            _autoStatus = ui.Label(_auto, "", 13f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(_autoStatus.rectTransform, new Vector2(16f, -196f), new Vector2(pw - 32f, 70f));
            _batch = ui.TechButton(_auto, "지금 일괄 정비", 15f, RunBatch, clickSound: false);
            HoloUi.Place((RectTransform)_batch.transform, new Vector2(pw - 196f, -276f), new Vector2(180f, 46f));

            Height = Mathf.Max(-listTop + panelH, -y) - top;
            if (sim.Research.Categories.Count > 0)
                _selected = sim.Research.Categories[0];
            root.gameObject.SetActive(false);
        }

        public bool Active => _root.gameObject.activeSelf;

        public void SetActive(bool on)
        {
            _root.gameObject.SetActive(on);
            if (on)
                Refresh();
        }

        private void Stepper(HoloUi ui, RectTransform parent, Vector2 topLeft, System.Action down, System.Action up)
        {
            var minus = ui.TechButton(parent, "-", 18f, () => { down(); Refresh(); });
            HoloUi.Place((RectTransform)minus.transform, topLeft, new Vector2(42f, 34f));
            var plus = ui.TechButton(parent, "+", 18f, () => { up(); Refresh(); });
            HoloUi.Place((RectTransform)plus.transform, topLeft + new Vector2(48f, 0f), new Vector2(42f, 34f));
        }

        private void Select(ResearchCategoryData category)
        {
            _selected = category;
            Refresh();
        }

        private void StartSelected()
        {
            if (_selected == null)
                return;
            var result = _sim.TryStartResearch(_selected, UsePoint);
            AudioService.TryPlay(l => result == ResearchStartResult.Ok ? l.BuildSelect : l.UiError);
            Refresh();
        }

        private bool UsePoint => _usePoint && _sim.Supply.ResearchPoints > 0;

        private void CancelSelected()
        {
            if (_selected != null && _sim.CancelResearch(_selected))
                AudioService.TryPlay(l => l.UiClose);
            Refresh();
        }

        private void RunBatch()
        {
            var (maintained, rebuilt) = _sim.Automation.RunBatch();
            AudioService.TryPlay(l => maintained + rebuilt > 0 ? l.BuildSelect : l.UiError);
            Refresh();
        }

        // ---------------- 표시 ----------------

        public void Refresh()
        {
            if (!Active)
                return;
            var research = _sim.Research;
            int running = Mathf.Min(research.Projects.Count, research.LabSlots);
            int points = _sim.Supply.ResearchPoints;
            string pointText = points > 0 ? $"  <color={HudText.Yellow}>연구 포인트 {points}</color>" : "";
            _labs.SetText(research.LabSlots == 0
                ? $"<color={HudText.Orange}>연구소가 없습니다 · 건설 탭의 산업에서 연구소를 지으세요</color>{pointText}"
                : $"연구소 {running} / {research.LabSlots} 사용 중  <color={HudText.Muted}>(연구소마다 동시에 하나씩)</color>{pointText}");

            foreach (var (category, button, title, sub) in _rows)
            {
                int level = research.GetLevel(category);
                var project = research.GetProject(category);
                string lv = category.MaxLevel == 1 ? (level >= 1 ? "완료" : "단일") : $"Lv.{level}/{category.MaxLevel}";
                title.SetText($"<b>{category.DisplayName}</b>  <size=80%><color={HudText.Muted}>{lv}</color></size>");
                string state;
                if (project != null)
                    state = project.Paused ? $"<color={HudText.Orange}>멈춤 {project.Progress * 100f:0}%</color>" : $"<color={HudTheme.AccentHex}>연구 중 {project.Progress * 100f:0}% · {FormatTime(project.RemainingSeconds)}</color>";
                else if (level >= category.MaxLevel)
                    state = $"<color={HudTheme.GreenHex}>최고 레벨</color>";
                else
                {
                    var check = _sim.CanStartResearch(category, UsePoint);
                    string reason = Reason(_sim, check, category, level + 1);
                    state = reason != null ? $"<color={HudText.Muted}>{reason}</color>" : $"<color={HudTheme.GreenHex}>시작 가능</color>";
                }
                sub.SetText(state);
                ((Image)button.targetGraphic).color = category == _selected ? HudTheme.ButtonSelected : HudTheme.ButtonNormal;
            }
            RefreshDetail();
        }

        private void RefreshDetail()
        {
            var category = _selected;
            if (category == null)
                return;
            var research = _sim.Research;
            int level = research.GetLevel(category);
            int max = category.MaxLevel;
            var project = research.GetProject(category);
            string progress = max == 1 ? (level >= 1 ? "완료" : "단일 연구") : $"Lv.{level} / {max}";
            _title.SetText($"<b>{category.DisplayName}</b>  <size=72%><color={HudText.Muted}>{progress}</color></size>");

            bool automationReady = category.Category == ResearchCategory.Automation && level >= max;
            _auto.gameObject.SetActive(automationReady);
            _body.gameObject.SetActive(!automationReady);
            _status.gameObject.SetActive(!automationReady);
            if (automationReady)
            {
                _bar.gameObject.SetActive(false);
                _start.gameObject.SetActive(false);
                _point.gameObject.SetActive(false);
                _cancel.gameObject.SetActive(false);
                RefreshAutomation();
                return;
            }

            var sb = new StringBuilder();
            var current = category.GetLevel(level);
            if (max > 1)
                sb.Append(current != null ? $"<color={HudText.Muted}>현재</color> {current.Description}\n" : $"<color={HudText.Muted}>현재 효과 없음</color>\n");
            var next = category.GetLevel(level + 1);
            if (next == null)
                sb.Append($"<color={HudTheme.GreenHex}>최고 레벨 달성</color>");
            else
            {
                sb.Append(max == 1 ? next.Description : $"<color={HudTheme.AccentHex}>다음 Lv.{level + 1}</color> {next.Description}");
                string discount = UsePoint ? $"  <color={HudText.Yellow}>(연구 포인트 -{_sim.Balance.ResearchPointDiscount * 100f:0}%)</color>" : "";
                sb.Append($"\n\n비용 {HudText.Cost(_sim.GetResearchStartCost(category, UsePoint))}{discount}  <color={HudText.Muted}>· 전력 +{next.PowerDemand:0} · {next.Duration:0}초</color>");
            }
            _body.SetText(sb.ToString());

            bool researching = project != null;
            _bar.gameObject.SetActive(researching);
            _cancel.gameObject.SetActive(researching);
            _start.gameObject.SetActive(!researching && next != null);
            // 11-17 ②: 연구 포인트가 있을 때만 (보급 특별 상자에서 얻음)
            int points = _sim.Supply.ResearchPoints;
            _point.gameObject.SetActive(!researching && next != null && points > 0);
            if (_point.gameObject.activeSelf)
                _point.GetComponentInChildren<TMP_Text>().SetText($"연구 포인트 {OnOff(UsePoint)}\n<size=80%><color={HudText.Muted}>보유 {points} · -{_sim.Balance.ResearchPointDiscount * 100f:0}%</color></size>");
            if (researching)
            {
                HoloUi.SetBar(_barFill, project.Progress);
                _status.SetText(project.Paused
                    ? $"<color={HudText.Orange}>연구 중 {project.Progress * 100f:0}% · 멈춤 (연구소 부족)</color>"
                    : $"연구 중 {project.Progress * 100f:0}% · 남은 {FormatTime(project.RemainingSeconds)}{(project.Rate < 0.999f ? $" <color={HudText.Yellow}>(전력 {project.Rate * 100f:0}%)</color>" : "")}");
                return;
            }
            if (next == null)
            {
                _status.SetText("");
                return;
            }
            var check = _sim.CanStartResearch(category, UsePoint);
            _start.interactable = check == ResearchStartResult.Ok;
            string reason = Reason(_sim, check, category, level + 1);
            _status.SetText(reason != null ? $"<color={HudText.Orange}>{reason}</color>" : $"<color={HudText.Muted}>시작 비용은 취소해도 돌려받지 않습니다</color>");
        }

        private void RefreshAutomation()
        {
            var auto = _sim.Automation;
            _autoMaintain.GetComponentInChildren<TMP_Text>().SetText($"자동 정비  {OnOff(auto.AutoMaintain)}");
            _autoRebuild.GetComponentInChildren<TMP_Text>().SetText($"자동 재건축  {OnOff(auto.AutoRebuild)}");
            _threshold.SetText($"정비 기준 최대치 <b>{auto.Threshold:0}%</b>"); // 12-0: 최대 내구도 대비 %
            _reserve.SetText($"자원 보호선 <b>{auto.ReserveRatio * 100f:0}%</b>");
            var jobs = auto.Plan();
            int rebuilds = 0;
            foreach (var job in jobs)
                if (job.Rebuild)
                    rebuilds++;
            var sb = new StringBuilder();
            sb.Append(jobs.Count == 0 ? $"<color={HudText.Muted}>기준 미만 모듈 없음</color>"
                : $"대상 {jobs.Count}개{(rebuilds > 0 ? $" (재건축 {rebuilds})" : "")}  {HudText.Cost(auto.TotalCost(jobs))}");
            if (auto.WaitingForReserve)
                sb.Append($"  <color={HudText.Orange}>· 보호선 아래라 대기 중</color>");
            sb.Append($"\n<color={HudText.Muted}>자동 처리: 정비 {auto.AutoMaintainCount}회 · 재건축 {auto.AutoRebuildCount}회 · 파손 모듈은 수리가 우선 · 일괄 정비는 보호선을 무시</color>");
            _autoStatus.SetText(sb.ToString());
            _batch.interactable = jobs.Count > 0;
        }

        private static string OnOff(bool on) => on ? $"<color={HudTheme.GreenHex}>켜짐</color>" : $"<color={HudText.Muted}>꺼짐</color>";

        /// <summary>연구를 시작할 수 없는 사유 (없으면 null).</summary>
        public static string Reason(StationSimulation sim, ResearchStartResult result, ResearchCategoryData category, int nextLevel)
        {
            var research = sim.Research;
            switch (result)
            {
                case ResearchStartResult.GradeTooLow:
                    int grade = Mathf.Min(research.RequiredGrade(category, nextLevel), sim.Progression.GradeCount - 1);
                    return $"{sim.Progression.GetGrade(grade).DisplayName} 등급 필요";
                case ResearchStartResult.PopulationTooLow:
                    return $"인구 {research.RequiredPopulation(nextLevel)}명 필요 (현재 {sim.Resources.Population})";
                case ResearchStartResult.NoFreeLab:
                    return sim.Research.LabSlots == 0 ? "연구소 필요" : "빈 연구소 없음";
                case ResearchStartResult.InsufficientResources:
                    return "자원 부족";
                default:
                    return null;
            }
        }

        public static string FormatTime(float seconds)
        {
            if (float.IsInfinity(seconds) || seconds > 99 * 60)
                return "-";
            int s = Mathf.CeilToInt(seconds);
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
