using System.Collections.Generic;
using System.Linq;
using System.Text;
using SpaceStation.Audio;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 주민 명단 화면 구성 (11-13 패드 주민 탭 → 11-15 ② 바깥 주민 창과 공용): 위 = 인원 · 집 없음 · 만족도 보정 + 특성별 효과 요약,
    /// 가운데 = 한 쪽 N명 명단(이름 · 특성 · 집 · 본인 보정 · [이사], 불만이 큰 주민이 위), 아래 = 쪽 넘김.
    /// 이사 = 빈자리가 있는 집 목록(그 주민에게 맞는 순)에서 골라 옮김. 옮긴 주민은 집이 없어지기 전까지 고정 (이름 옆 "고정").
    /// REVIEW_1115 5번 [내보내기]: 한 번 누르면 위 줄에 대가 미리보기 → 같은 줄 [확인]으로 내보냄 (ESC 취소). 문제 주민은 이름 옆 "문제".
    /// </summary>
    public sealed class RosterView
    {
        private sealed class Row
        {
            public RectTransform Root;
            public TMP_Text Name, Traits, Home, Mood, ActionLabel, ExileLabel;
            public Button Action, Exile;
            public Resident Resident;
            public ModuleInstance Target;
        }

        private readonly ResidentRoster _roster;
        private readonly ExileSystem _exile;
        private Resident _exiling;
        private readonly RectTransform _root;
        private readonly int _rowsPerPage;
        private readonly TMP_Text _subtitle, _summary, _listHeader, _pageLabel;
        private readonly RectTransform _columns;
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<Resident> _sorted = new List<Resident>();
        private readonly List<ModuleInstance> _targets = new List<ModuleInstance>();
        private int _page;
        private Resident _moving;

        /// <summary>맨 위 줄부터 쪽 넘김 버튼 아래 끝까지의 높이.</summary>
        public float Height { get; }

        /// <param name="top">맨 위 줄의 y (패드는 탭 줄 아래 72)</param>
        /// <param name="exile">REVIEW_1115 5번 주민 추방 (null = 버튼 없음)</param>
        public RosterView(HoloUi ui, RectTransform root, float width, ResidentRoster roster, float top = 72f, int rowsPerPage = 6, ExileSystem exile = null)
        {
            _root = root;
            _roster = roster;
            _exile = exile;
            _rowsPerPage = rowsPerPage;
            _subtitle = ui.Label(root, "", 15f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(_subtitle.rectTransform, new Vector2(18f, -top), new Vector2(width - 36f, 24f));
            _summary = ui.Label(root, "", 12f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(_summary.rectTransform, new Vector2(18f, -top - 26f), new Vector2(width - 36f, 40f));

            float listW = width - 32f, listTop = -top - 70f, listH = 50f + rowsPerPage * 40f;
            var list = HoloUi.Rect("RosterList", root);
            HoloUi.Place(list, new Vector2(16f, listTop), new Vector2(listW, listH));
            ui.TechPanel(list.gameObject, new Color(0.03f, 0.1f, 0.15f, 0.85f), new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, 0.6f), "ROSTER", 0.15f);
            _listHeader = ui.Label(list, "", 13f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(_listHeader.rectTransform, new Vector2(14f, -20f), new Vector2(listW - 28f, 22f));
            _listHeader.overflowMode = TextOverflowModes.Ellipsis;
            _columns = HoloUi.Rect("Columns", list);
            HoloUi.Place(_columns, new Vector2(10f, -20f), new Vector2(listW - 20f, 22f));
            // REVIEW_1115 5번: [내보내기] 자리만큼 특성 · 집 · 보정 칸을 줄임
            Column(ui, "이름", 8f, 170f);
            Column(ui, "특성", 186f, 176f);
            Column(ui, "집 · 주변", 368f, 180f);
            Column(ui, "보정", 552f, 48f);
            for (int i = 0; i < rowsPerPage; i++)
                _rows.Add(BuildRow(ui, list, new Vector2(10f, -44f - i * 40f), listW - 20f));

            float pagerY = listTop - listH - 4f;
            var prev = ui.TechButton(root, "<", 18f, () => { _page--; Refresh(); });
            HoloUi.Place((RectTransform)prev.transform, new Vector2(16f, pagerY), new Vector2(44f, 34f));
            _pageLabel = ui.Label(root, "", 15f, TextAlignmentOptions.Center);
            HoloUi.Place(_pageLabel.rectTransform, new Vector2(64f, pagerY), new Vector2(80f, 34f));
            var next = ui.TechButton(root, ">", 18f, () => { _page++; Refresh(); });
            HoloUi.Place((RectTransform)next.transform, new Vector2(148f, pagerY), new Vector2(44f, 34f));
            Height = -(pagerY - 34f) - top;
            root.gameObject.SetActive(false);
        }

        public bool Active => _root.gameObject.activeSelf;
        public bool Moving => _moving != null;

        public void SetActive(bool on)
        {
            _root.gameObject.SetActive(on);
            if (!on)
            {
                _moving = null;
                _exiling = null;
            }
            else
                Refresh();
        }

        /// <summary>ESC: 이사할 집 고르기 · 내보내기 확인 취소. 취소했으면 true.</summary>
        public bool Cancel()
        {
            if (!Active || (_moving == null && _exiling == null))
                return false;
            if (_exiling != null)
            {
                _exiling = null; // 쪽은 그대로
                Refresh();
                return true;
            }
            _moving = null;
            _page = 0;
            Refresh();
            return true;
        }

        private void Column(HoloUi ui, string text, float x, float w)
        {
            var t = ui.Label(_columns, $"<color={HudText.Muted}>{text}</color>", 12f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(t.rectTransform, new Vector2(x, 0f), new Vector2(w, 22f));
        }

        private Row BuildRow(HoloUi ui, RectTransform parent, Vector2 topLeft, float w)
        {
            var row = new Row { Root = HoloUi.Rect("Row", parent) };
            HoloUi.Place(row.Root, topLeft, new Vector2(w, 36f));
            var bg = row.Root.gameObject.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.14f, 0.2f, 0.6f);
            bg.raycastTarget = false;
            row.Name = Cell(ui, row.Root, 8f, 170f, 15f);
            row.Traits = Cell(ui, row.Root, 186f, 176f, 13f);
            row.Home = Cell(ui, row.Root, 368f, 180f, 13f);
            row.Mood = Cell(ui, row.Root, 552f, 48f, 14f);
            row.Action = ui.TechButton(row.Root, "이사", 13f, () => HandleRowAction(row));
            HoloUi.Place((RectTransform)row.Action.transform, new Vector2(w - 100f, -3f), new Vector2(94f, 30f));
            row.ActionLabel = row.Action.GetComponentInChildren<TMP_Text>();
            row.Exile = ui.TechButton(row.Root, "내보내기", 13f, () => HandleExile(row));
            HoloUi.Place((RectTransform)row.Exile.transform, new Vector2(w - 198f, -3f), new Vector2(94f, 30f));
            row.ExileLabel = row.Exile.GetComponentInChildren<TMP_Text>();
            row.Exile.gameObject.SetActive(false);
            return row;
        }

        private bool ExileOpen => _exile != null && _exile.Unlocked;

        /// <summary>REVIEW_1115 5번: 첫 클릭 = 대가 미리보기 · 둘째 클릭(같은 주민) = 내보냄.</summary>
        private void HandleExile(Row row)
        {
            var r = row.Resident;
            if (r == null || !ExileOpen)
                return;
            if (_exiling != r)
            {
                _exiling = r;
                AudioService.TryPlay(l => l.BuildSelect);
            }
            else
            {
                if (_exile.Exile(r))
                    AudioService.TryPlay(l => l.BuildSelect);
                else
                    AudioService.TryPlay(l => l.UiError);
                _exiling = null;
            }
            Refresh();
        }

        /// <summary>내보내기 대가 미리보기 줄.</summary>
        private string ExileHeader(Resident r)
        {
            var p = _exile.Preview(r);
            string why = p.IsProblem ? $"문제 주민 ({ResidentText.Problem(p.Problem)})" : $"<color={ResidentText.Bad}>이유 없는 추방</color>";
            string suffix = _exile.PreviewSuffix != null ? _exile.PreviewSuffix(r, p) : null;
            string kind = p.KindMultiplier > 1.001f ? $" · 같은 종족 반응 ×{p.KindMultiplier:0.0}" : "";
            string cost = $"만족도 -{p.Satisfaction:0.#}" + (string.IsNullOrEmpty(suffix) ? "" : " · " + suffix);
            return $"<color={HudTheme.AccentHex}><b>{ResidentText.FullName(_roster.Config, r)}</b></color> 내보내기 · {why} · <color={ResidentText.Bad}>{cost}</color>{kind}  <color={HudText.Muted}>[확인] · ESC 취소</color>";
        }

        private static TMP_Text Cell(HoloUi ui, RectTransform parent, float x, float w, float size)
        {
            var t = ui.Label(parent, "", size, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(t.rectTransform, new Vector2(x, 0f), new Vector2(w, 36f));
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private void HandleRowAction(Row row)
        {
            _exiling = null; // 이사를 누르면 내보내기 확인은 접음
            if (_moving == null)
            {
                if (row.Resident == null)
                    return;
                _moving = row.Resident;
                _page = 0;
                AudioService.TryPlay(l => l.BuildSelect);
            }
            else if (row.Target == null)
            {
                Cancel(); // "다른 집이 없습니다" 줄의 [취소]
                return;
            }
            else if (_roster.TryMove(_moving, row.Target))
            {
                AudioService.TryPlay(l => l.BuildSelect);
                _moving = null;
                _page = 0;
            }
            else
                AudioService.TryPlay(l => l.UiError);
            Refresh();
        }

        // ---------------- 표시 ----------------

        public void Refresh()
        {
            if (!Active)
                return;
            if (_moving != null && !_roster.Residents.Contains(_moving))
                _moving = null; // 고르는 중에 떠남
            if (_exiling != null && (!_roster.Residents.Contains(_exiling) || !ExileOpen))
                _exiling = null;
            RefreshHeader();
            if (_moving == null)
                RefreshList();
            else
                RefreshTargets();
        }

        private void RefreshHeader()
        {
            int homeless = 0;
            foreach (var r in _roster.Residents)
                if (r.Home == null)
                    homeless++;
            float mood = _roster.MoodTotal;
            string moodColor = mood > 0.01f ? ResidentText.Good : mood < -0.01f ? ResidentText.Bad : HudText.Muted;
            _subtitle.SetText($"주민 {_roster.Residents.Count}명" + (homeless > 0 ? $" · <color={ResidentText.Bad}>집 없음 {homeless}명</color>" : "") +
                              $" · 특성 만족도 보정 <color={moodColor}>{ResidentText.Signed(mood)}</color> <color={HudText.Muted}>(만족도 상한에 더함)</color>");
            var sb = new StringBuilder();
            var config = _roster.Config;
            int column = 0;
            foreach (var def in config.Traits)
            {
                if (def == null)
                    continue;
                int count = _roster.CountWith(def.Trait);
                if (count == 0)
                    continue;
                sb.Append($"{ResidentText.TraitName(config, def.Trait)} {count} <color={HudText.Muted}>{EffectText(_roster, def)}</color>");
                sb.Append(++column % 4 == 0 ? "\n" : "   ");
            }
            _summary.SetText(sb.Length > 0 ? sb.ToString() : $"<color={HudText.Muted}>주민 없음</color>");
        }

        private void RefreshList()
        {
            _sorted.Clear();
            _sorted.AddRange(_roster.Residents);
            _sorted.Sort((a, b) =>
            {
                int c = _roster.Discontent(b).CompareTo(_roster.Discontent(a));
                return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
            });
            int pages = Mathf.Max(1, Mathf.CeilToInt(_sorted.Count / (float)_rowsPerPage));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _pageLabel.SetText($"{_page + 1} / {pages}");
            bool exileOpen = ExileOpen;
            bool blocked = exileOpen && _exile.Blocked;
            _listHeader.SetText(_exiling != null ? ExileHeader(_exiling) : "");
            _columns.gameObject.SetActive(_exiling == null);
            for (int i = 0; i < _rows.Count; i++)
            {
                int index = _page * _rowsPerPage + i;
                var row = _rows[i];
                row.Target = null;
                if (index >= _sorted.Count)
                {
                    row.Root.gameObject.SetActive(false);
                    row.Resident = null;
                    continue;
                }
                var r = _sorted[index];
                row.Resident = r;
                row.Root.gameObject.SetActive(true);
                string full = ResidentText.FullName(_roster.Config, r);
                if (r.Pinned)
                    full += $" <size=75%><color={HudText.Muted}>고정</color></size>";
                if (exileOpen && _roster.ProblemOf(r) != ResidentProblem.None)
                    full += $" <size=75%><color={ResidentText.Bad}>문제</color></size>";
                row.Name.SetText(full);
                row.Exile.gameObject.SetActive(exileOpen);
                if (exileOpen)
                {
                    row.ExileLabel.SetText(_exiling == r ? "확인" : blocked ? "사면 중" : "내보내기");
                    row.Exile.interactable = !blocked && (_exiling == null || _exiling == r);
                }
                row.Traits.SetText(ResidentText.Traits(_roster.Config, r));
                string env = ResidentText.Environment(_roster.GetEnvironment(r.Home), r) + StressTag(r, r.Home);
                row.Home.SetText(ResidentText.HomeName(_roster, r.Home) + (env.Length > 0 ? "  " + env : ""));
                row.Mood.SetText(MoodText(_roster.PersonalMood(r)));
                row.ActionLabel.SetText("이사");
                row.Action.interactable = true;
            }
        }

        private void RefreshTargets()
        {
            var r = _moving;
            _targets.Clear();
            foreach (var home in _roster.Homes)
                if (home != r.Home && _roster.GetFreeSlots(home) > 0)
                    _targets.Add(home);
            _targets.Sort((a, b) =>
            {
                int c = _roster.MoodAt(r, b).CompareTo(_roster.MoodAt(r, a));
                return c != 0 ? c : _roster.GetFreeSlots(b).CompareTo(_roster.GetFreeSlots(a));
            });
            int pages = Mathf.Max(1, Mathf.CeilToInt(_targets.Count / (float)_rowsPerPage));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _pageLabel.SetText($"{_page + 1} / {pages}");
            _columns.gameObject.SetActive(false);
            _listHeader.SetText($"<color={HudTheme.AccentHex}><b>{ResidentText.FullName(_roster.Config, r)}</b></color> 이사할 집 고르기 · 지금 {ResidentText.HomeName(_roster, r.Home)} {MoodText(_roster.PersonalMood(r))}  <color={HudText.Muted}>ESC 취소</color>");
            for (int i = 0; i < _rows.Count; i++)
            {
                int index = _page * _rowsPerPage + i;
                var row = _rows[i];
                row.Resident = null;
                row.Exile.gameObject.SetActive(false);
                if (index >= _targets.Count)
                {
                    row.Target = null;
                    bool empty = i == 0 && _targets.Count == 0;
                    row.Root.gameObject.SetActive(empty);
                    if (empty)
                    {
                        row.Name.SetText($"<color={HudText.Muted}>빈자리가 있는 다른 집이 없습니다</color>");
                        row.Traits.SetText("");
                        row.Home.SetText("");
                        row.Mood.SetText("");
                        row.ActionLabel.SetText("취소");
                        row.Action.interactable = true;
                    }
                    continue;
                }
                var home = _targets[index];
                row.Target = home;
                row.Root.gameObject.SetActive(true);
                row.Name.SetText(ResidentText.HomeName(_roster, home));
                row.Traits.SetText($"빈자리 {_roster.GetFreeSlots(home)} / {_roster.GetCapacity(home)}");
                string env = ResidentText.Environment(_roster.GetEnvironment(home), r) + StressTag(r, home);
                row.Home.SetText(env.Length > 0 ? env : $"<color={HudText.Muted}>주변 특이 사항 없음</color>");
                row.Mood.SetText(MoodText(_roster.MoodAt(r, home)));
                row.ActionLabel.SetText("여기로");
                row.Action.interactable = true;
            }
        }

        /// <summary>특성 합계 효과 문구 (능력: 비율, 만족도: 상한 보정, 소비·내성: 설명).</summary>
        public static string EffectText(ResidentRoster roster, TraitDefinition def)
        {
            switch (def.Trait)
            {
                case ResidentTrait.Technician: return $"수리 시간 -{roster.Ability(def.Trait) * 100f:0}%";
                case ResidentTrait.Scientist: return $"연구 속도 +{roster.Ability(def.Trait) * 100f:0}%";
                case ResidentTrait.Gardener: return $"식량 생산 +{roster.Ability(def.Trait) * 100f:0}%";
                case ResidentTrait.Mechanic: return $"노후 속도 -{roster.Ability(def.Trait) * 100f:0}%";
                case ResidentTrait.BigEater:
                case ResidentTrait.LightEater:
                    return $"식량 소비 ×{roster.ConsumptionMultiplier(ResourceType.Food):0.00}";
                case ResidentTrait.RadiationTolerant: return "방사선 집 우선";
                default: return $"상한 {ResidentText.Signed(roster.MoodOf(def.Trait))}";
            }
        }

        /// <summary>M-120 이 주민이 이 집에서 힘들면 표시 (집 환경 고리가 없으면 빈 글).</summary>
        private string StressTag(Resident r, ModuleInstance home) =>
            _roster.StressAt(r, home) > 0f ? $"  <color={ResidentText.Bad}>집이 힘듦</color>" : "";

        public static string MoodText(float mood)
        {
            string color = mood > 0.01f ? ResidentText.Good : mood < -0.01f ? ResidentText.Bad : HudText.Muted;
            return $"<color={color}>{ResidentText.Signed(mood)}</color>";
        }
    }
}
