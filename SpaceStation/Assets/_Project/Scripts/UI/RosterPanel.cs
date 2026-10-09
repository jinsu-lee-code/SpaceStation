using System.Collections.Generic;
using System.Text;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// Phase 10 주민 명단 창. U 키 또는 왼쪽 위 [주민] 버튼으로 연다. 게임 시간은 멈추지 않는다.
    /// - 위: 인원·집 없음·만족도 보정 합계, 특성별 효과 요약
    /// - 목록(쪽 넘김): 이름 · 특성 · 집(환경) · 본인 보정 · [이사] — 불만이 큰 주민이 위
    /// - 이사: 빈자리가 있는 집 목록(그 주민에게 맞는 순)에서 골라 옮김. 옮긴 주민은 집이 없어지기 전까지 고정 (이름 옆 "고정")
    /// 내용은 코드로 만든다 (씬에는 글꼴·스프라이트만 연결된 빈 오브젝트). ESC로 닫으면 다른 ESC 처리는 건너뛴다.
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public sealed class RosterPanel : MonoBehaviour
    {
        [SerializeField] private StationController _station;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [SerializeField] private float _refreshSeconds = 0.5f;
        [SerializeField] private int _rowsPerPage = 13;

        private sealed class Row
        {
            public RectTransform Root;
            public TMP_Text Name;
            public TMP_Text Traits;
            public TMP_Text Home;
            public TMP_Text Mood;
            public Button Action;
            public TMP_Text ActionLabel;
            public Resident Resident;
            public ModuleInstance Target;
        }

        private HoloUi _ui;
        private StationSimulation _sim;
        private ResidentRoster _roster;
        private RectTransform _window;
        private CanvasGroup _group;
        private UiTween _tween;
        private TMP_Text _subtitle;
        private TMP_Text _summary;
        private TMP_Text _listHeader;
        private TMP_Text _pageLabel;
        private TMP_Text _launcherLabel;
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<Resident> _sorted = new List<Resident>();
        private readonly List<ModuleInstance> _targets = new List<ModuleInstance>();
        private bool _open;
        private bool _dirty = true;
        private float _nextRefresh;
        private int _page;
        private Resident _moving; // 이사할 집을 고르는 중인 주민 (null = 명단)

        public bool IsOpen => _open;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            _roster = _sim?.Residents;
            if (_roster == null)
            {
                gameObject.SetActive(false); // 주민 설정이 없는 씬
                return;
            }
            _ui = new HoloUi(_font, _fillSprite, _frameSprite, _buttonSprite);
            BuildLauncher();
            BuildWindow();
            _roster.Changed += MarkDirty;
            KeyBindings.Changed += RefreshLauncher;
        }

        private void OnDestroy()
        {
            KeyBindings.Changed -= RefreshLauncher;
            if (_roster != null)
                _roster.Changed -= MarkDirty;
        }

        private void MarkDirty() => _dirty = true;

        private void Update()
        {
            if (_ui == null)
                return;
            _tween.Update();
            var keyboard = Keyboard.current;
            if (keyboard != null && !InputGate.Blocked)
            {
                if (KeyBindings.WasPressed(GameAction.Roster))
                    Toggle();
                else if (_open && keyboard.escapeKey.wasPressedThisFrame)
                {
                    InputGate.ConsumeEscape();
                    if (_moving != null)
                        CancelMove();
                    else
                        Close();
                }
            }
            if (_open && (_dirty || Time.unscaledTime >= _nextRefresh))
            {
                _dirty = false;
                _nextRefresh = Time.unscaledTime + _refreshSeconds;
                Refresh();
            }
        }

        public void Toggle()
        {
            if (_open)
                Close();
            else
                Open();
        }

        public void Open()
        {
            _open = true;
            _moving = null;
            _group.blocksRaycasts = true;
            _group.interactable = true;
            _tween.Play();
            Refresh();
            AudioService.TryPlay(l => l.UiOpen);
        }

        public void Close()
        {
            if (!_open)
                return;
            _open = false;
            _moving = null;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _tween.Hide();
            AudioService.TryPlay(l => l.UiClose);
        }

        // ---------------- 구성 ----------------

        private static string LauncherText()
            => $"주민  <size=70%><color=#AFC4D8>{KeyBindings.Label(GameAction.Roster)}</color></size>";

        private void RefreshLauncher()
        {
            if (_launcherLabel != null)
                _launcherLabel.SetText(LauncherText());
        }

        private void BuildLauncher()
        {
            var button = _ui.Button((RectTransform)transform, LauncherText(), 19f, Toggle);
            HoloUi.Place((RectTransform)button.transform, new Vector2(202f, -24f), new Vector2(140f, 44f)); // 연구 버튼 오른쪽
            _launcherLabel = button.GetComponentInChildren<TMP_Text>();
        }

        private void BuildWindow()
        {
            var root = (RectTransform)transform;
            _window = HoloUi.Rect("RosterWindow", root);
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.sizeDelta = new Vector2(1180f, 900f);
            _window.anchoredPosition = new Vector2(-90f, 40f);
            _ui.Panel(_window.gameObject, new Color(0.03f, 0.07f, 0.11f, 0.96f), HudTheme.Accent);
            _group = _window.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var title = _ui.Label(_window, "주민 명단", 32f, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            HoloUi.Place(title.rectTransform, new Vector2(40f, -26f), new Vector2(500f, 46f));
            _subtitle = _ui.Label(_window, "", 17f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(_subtitle.rectTransform, new Vector2(42f, -78f), new Vector2(1100f, 28f));
            _summary = _ui.Label(_window, "", 15f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(_summary.rectTransform, new Vector2(42f, -112f), new Vector2(1100f, 96f));

            _listHeader = _ui.Label(_window, "", 15f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(_listHeader.rectTransform, new Vector2(42f, -212f), new Vector2(1100f, 26f));
            _columns = HoloUi.Rect("Columns", _window);
            HoloUi.Place(_columns, new Vector2(36f, -212f), new Vector2(1108f, 26f));
            Column("이름", 12f, 230f);
            Column("특성", 248f, 320f);
            Column("집 · 주변", 574f, 330f);
            Column("본인 보정", 910f, 100f);
            Column("불만이 큰 주민이 위", 984f, 124f);
            for (int i = 0; i < _rowsPerPage; i++)
                _rows.Add(BuildRow(new Vector2(36f, -242f - i * 46f)));

            var prev = _ui.Button(_window, "<", 20f, () => { _page--; Refresh(); });
            Bottom((RectTransform)prev.transform, new Vector2(40f, 28f), new Vector2(48f, 44f));
            _pageLabel = _ui.Label(_window, "", 17f, TextAlignmentOptions.Center);
            Bottom(_pageLabel.rectTransform, new Vector2(94f, 28f), new Vector2(120f, 44f));
            var next = _ui.Button(_window, ">", 20f, () => { _page++; Refresh(); });
            Bottom((RectTransform)next.transform, new Vector2(220f, 28f), new Vector2(48f, 44f));

            var close = _ui.Button(_window, "닫기  <size=70%><color=#AFC4D8>ESC</color></size>", 19f, Close);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-40f, 28f);
            crt.sizeDelta = new Vector2(190f, 48f);

            _tween = new UiTween(_window, _group, new Vector2(0f, -24f), 0.2f, 0.15f);
        }

        private static void Bottom(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private RectTransform _columns;

        private void Column(string text, float x, float width)
        {
            var t = _ui.Label(_columns, $"<color={HudText.Muted}>{text}</color>", 14f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(t.rectTransform, new Vector2(x, 0f), new Vector2(width, 26f));
        }

        private Row BuildRow(Vector2 topLeft)
        {
            var row = new Row();
            row.Root = HoloUi.Rect("Row", _window);
            HoloUi.Place(row.Root, topLeft, new Vector2(1108f, 42f));
            var bg = row.Root.gameObject.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.12f, 0.17f, 0.75f);
            bg.raycastTarget = false;
            row.Name = Cell(row.Root, 12f, 230f, 18f);
            row.Traits = Cell(row.Root, 248f, 320f, 15f);
            row.Home = Cell(row.Root, 574f, 330f, 15f);
            row.Mood = Cell(row.Root, 910f, 70f, 16f);
            row.Action = _ui.Button(row.Root, "이사", 16f, () => HandleRowAction(row));
            HoloUi.Place((RectTransform)row.Action.transform, new Vector2(984f, -4f), new Vector2(116f, 34f));
            row.ActionLabel = row.Action.GetComponentInChildren<TMP_Text>();
            return row;
        }

        private TMP_Text Cell(RectTransform parent, float x, float width, float size)
        {
            var t = _ui.Label(parent, "", size, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(t.rectTransform, new Vector2(x, 0f), new Vector2(width, 42f));
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        // ---------------- 명령 ----------------

        private void HandleRowAction(Row row)
        {
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
                CancelMove(); // "다른 집이 없습니다" 줄의 [취소]
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

        private void CancelMove()
        {
            _moving = null;
            _page = 0;
            Refresh();
        }

        // ---------------- 표시 ----------------

        private void Refresh()
        {
            if (_moving != null && !Contains(_roster.Residents, _moving))
                _moving = null; // 이사 고르는 중에 떠남
            RefreshHeader();
            if (_moving == null)
                RefreshList();
            else
                RefreshTargets();
        }

        private static bool Contains(IReadOnlyList<Resident> list, Resident r)
        {
            foreach (var x in list)
                if (x == r)
                    return true;
            return false;
        }

        private void RefreshHeader()
        {
            int homeless = 0;
            foreach (var r in _roster.Residents)
                if (r.Home == null)
                    homeless++;
            float mood = _roster.MoodTotal;
            string moodColor = mood > 0.01f ? ResidentText.Good : mood < -0.01f ? ResidentText.Bad : HudText.Muted;
            _subtitle.SetText($"주민 {_roster.Residents.Count}명" +
                              (homeless > 0 ? $" · <color={ResidentText.Bad}>집 없음 {homeless}명</color>" : "") +
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
                string effect = EffectText(def);
                sb.Append($"{ResidentText.TraitName(config, def.Trait)} {count}명 <color={HudText.Muted}>{effect}</color>");
                sb.Append(++column % 3 == 0 ? "\n" : "    ");
            }
            _summary.SetText(sb.Length > 0 ? sb.ToString() : $"<color={HudText.Muted}>주민 없음</color>");
        }

        /// <summary>특성 합계 효과 문구 (능력: 비율, 만족도: 상한 보정, 소비·내성: 설명).</summary>
        private string EffectText(TraitDefinition def)
        {
            switch (def.Trait)
            {
                case ResidentTrait.Technician: return $"수리 시간 -{_roster.Ability(def.Trait) * 100f:0}%";
                case ResidentTrait.Scientist: return $"연구 속도 +{_roster.Ability(def.Trait) * 100f:0}%";
                case ResidentTrait.Gardener: return $"식량 생산 +{_roster.Ability(def.Trait) * 100f:0}%";
                case ResidentTrait.Mechanic: return $"노후 속도 -{_roster.Ability(def.Trait) * 100f:0}%";
                case ResidentTrait.BigEater:
                case ResidentTrait.LightEater:
                    return $"식량 소비 ×{_roster.ConsumptionMultiplier(ResourceType.Food):0.00}";
                case ResidentTrait.RadiationTolerant: return "방사선 집 우선";
                default: return $"상한 {ResidentText.Signed(_roster.MoodOf(def.Trait))}";
            }
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
            _listHeader.SetText("");
            _columns.gameObject.SetActive(true);
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
                row.Name.SetText(r.Pinned ? $"{full} <size=75%><color={HudText.Muted}>고정</color></size>" : full);
                row.Traits.SetText(ResidentText.Traits(_roster.Config, r));
                string env = ResidentText.Environment(_roster.GetEnvironment(r.Home), r);
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
            _listHeader.SetText($"<color={HudTheme.AccentHex}><b>{ResidentText.FullName(_roster.Config, r)}</b></color> ({ResidentText.Traits(_roster.Config, r)}) 이사할 집 고르기 · 지금: {ResidentText.HomeName(_roster, r.Home)} {MoodText(_roster.PersonalMood(r))}" +
                                $"  <color={HudText.Muted}>ESC 취소 · 옮긴 주민은 자동 배정에서 고정</color>");
            for (int i = 0; i < _rows.Count; i++)
            {
                int index = _page * _rowsPerPage + i;
                var row = _rows[i];
                row.Resident = null;
                if (index >= _targets.Count)
                {
                    row.Target = null;
                    row.Root.gameObject.SetActive(i == 0 && _targets.Count == 0);
                    if (i == 0 && _targets.Count == 0)
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
                string env = ResidentText.Environment(_roster.GetEnvironment(home), r);
                row.Home.SetText(env.Length > 0 ? env : $"<color={HudText.Muted}>주변 특이 사항 없음</color>");
                row.Mood.SetText(MoodText(_roster.MoodAt(r, home)));
                row.ActionLabel.SetText("여기로");
                row.Action.interactable = true;
            }
        }

        private static string MoodText(float mood)
        {
            string color = mood > 0.01f ? ResidentText.Good : mood < -0.01f ? ResidentText.Bad : HudText.Muted;
            return $"<color={color}>{ResidentText.Signed(mood)}</color>";
        }
    }
}
