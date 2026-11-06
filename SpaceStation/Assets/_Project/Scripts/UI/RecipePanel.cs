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
    /// 12-3 합성 레시피(도감) 창 (PHASESET A7 · A9). K 키 또는 왼쪽 위 [관리] → [합성]으로 연다. 게임 시간은 멈추지 않는다.
    /// 왼쪽 = 레시피 목록, 오른쪽 = 고른 레시피의 재료 · 규격 모양(입체 그림, 합성 틀이면 재료 놓는 모양 → 완성 모양) · 결과 효과 · 비용 · 시간 · 당위성 + [위치 지정].
    /// [위치 지정]을 누르면 창이 닫히고 <see cref="SynthesisController"/>가 규격 고스트를 띄운다.
    /// 왼쪽 위 추적기: 진행 중인 합성 (연구 추적기 아래).
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public sealed class RecipePanel : MonoBehaviour
    {
        private const float ListWidth = 250f;
        private const float DetailWidth = 640f;
        private const float RowHeight = 46f;
        private const float ShapeWidth = 220f, ShapeHeight = 280f;
        private const float BodyHeight = 560f;

        [SerializeField] private StationController _station;
        [SerializeField] private SynthesisController _synthesis;
        [Tooltip("연구 추적기 높이만큼 합성 추적기를 아래로")]
        [SerializeField] private ResearchPanel _research;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [SerializeField] private HoloArt _holoArt;
        [SerializeField] private float _refreshSeconds = 0.25f;
        [Tooltip("M-52 창 위치 (화면 가운데 기준). 서사판 = 왼쪽 위 안내 카드 · 오른쪽 장 카드 사이로 (-50, 40)")]
        [SerializeField] private Vector2 _windowOffset = new Vector2(-150f, 40f);

        private HoloUi _ui;
        private StationSimulation _sim;
        private SynthesisSystem _syn;
        private CanvasGroup _group;
        private RectTransform _window;
        private UiTween _tween;
        private HoloFx _fx;
        private bool _open;
        private float _nextRefresh;
        private RecipeData _selected;
        private readonly List<(RecipeData recipe, Button button, TMP_Text label)> _rows = new List<(RecipeData, Button, TMP_Text)>();
        private TMP_Text _title, _body, _effects, _tracker;
        private RectTransform _shape;
        private RecipeData _drawnRecipe;
        private bool _drawnSilhouette;
        private readonly List<ShapeTiling.Piece> _tiling = new List<ShapeTiling.Piece>();
        private readonly Dictionary<(RecipeData, int), Texture2D> _shapeTextures = new Dictionary<(RecipeData, int), Texture2D>();
        private Button _place;
        // 12-4 그림자 · 해금 연구
        private Button _researchButton;
        private TMP_Text _researchStatus, _slots;
        private float _listTop, _listX;
        private RectTransform _listContent;   // 진화 모듈 48종: 목록이 본문 높이를 넘으면 휠 · 끌기로 스크롤

        public bool IsOpen => _open;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            _syn = _sim?.Synthesis;
            if (_syn == null || _syn.Recipes.Count == 0)
            {
                gameObject.SetActive(false); // 레시피가 없는 씬
                return;
            }
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            BuildLauncher();
            BuildWindow();
            HudWindows.Opened += HandleWindowOpened;
            _syn.Completed += HandleCompleted;
            Select(_syn.Recipes[0]);
            Refresh();
        }

        private void OnDestroy()
        {
            HudWindows.Opened -= HandleWindowOpened;
            if (_syn != null)
                _syn.Completed -= HandleCompleted;
            foreach (var tex in _shapeTextures.Values)
                if (tex != null)
                    Destroy(tex);
        }

        private void Update()
        {
            if (_ui == null)
                return;
            _tween.Update();
            var keyboard = Keyboard.current;
            if (keyboard != null && !InputGate.Blocked)
            {
                if (KeyBindings.WasPressed(GameAction.Recipes))
                    Toggle();
                else if (_open && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
                {
                    InputGate.ConsumeEscape();
                    Close();
                }
            }
            if (Time.unscaledTime >= _nextRefresh)
            {
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
            HudWindows.NotifyOpened(this); // 다른 창(연구 · 주민)은 닫힘
            _synthesis?.Begin(null);
            _open = true;
            _group.blocksRaycasts = true;
            _group.interactable = true;
            _tween.Play();
            _fx?.Replay();
            Refresh();
            AudioService.TryPlay(l => l.UiOpen);
            _sim.Tutorial?.ReportSynthesis(TutorialGoal.OpenRecipes); // M-51 합성 안내
        }

        private void HandleWindowOpened(object window)
        {
            if (!ReferenceEquals(window, this))
                Close(sound: false);
        }

        public void Close() => Close(sound: true);

        private void Close(bool sound)
        {
            if (!_open)
                return;
            _open = false;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _tween.Hide();
            if (sound)
                AudioService.TryPlay(l => l.UiClose);
        }

        private void HandleCompleted(SynthesisJob job, ModuleInstance result)
        {
            AudioService.TryPlay(l => l.EventPositive, 0.8f);
            Refresh();
        }

        private void Select(RecipeData recipe)
        {
            _selected = recipe;
            RefreshDetail();
            RefreshList();
        }

        private void PlaceSelected()
        {
            if (_selected == null || !_syn.IsUnlocked(_selected) || _synthesis == null)
                return;
            Close(sound: false);
            _synthesis.Begin(_selected);
        }

        /// <summary>12-4 그림자 레시피 해금 연구 시작 / 진행 중이면 취소.</summary>
        private void ResearchSelected()
        {
            var ex = _sim.ResearchExpansion;
            if (_selected == null || ex == null)
                return;
            if (ex.ProjectOf(_selected) != null)
            {
                ex.Cancel(_selected);
                AudioService.TryPlay(l => l.UiClose);
            }
            else
            {
                var result = ex.TryStart(_selected);
                AudioService.TryPlay(l => result == ExpansionStartResult.Ok ? l.BuildSelect : l.UiError);
            }
            Refresh();
        }

        private bool AnyRecipeShown()
        {
            foreach (var r in _syn.Recipes)
                if (_syn.IsUnlocked(r) || (_sim.ResearchExpansion != null && StateOf(r) == RecipeState.Shadow)) // 자동 해금 레시피도 등급 · 규칙 전에는 안 보임
                    return true;
            return false;
        }

        private RecipeState StateOf(RecipeData r)
        {
            var ex = _sim.ResearchExpansion;
            return ex != null ? ex.StateOf(r) : _syn.IsOpenedDirectly(r) ? RecipeState.Unlocked : RecipeState.Hidden;
        }

        private static string Reason(ExpansionStartResult result, RecipeResearchCost cost, int points) => result switch
        {
            ExpansionStartResult.NoFreeSlot => "레시피 연구 슬롯 없음 (연구 단지 등 연구소 계통 진화 모듈이 슬롯을 줌)",
            ExpansionStartResult.NoResearchPoints => $"연구 포인트 {cost.ResearchPoints} 필요 (보유 {points} · 보급 특별 상자에서 얻음)",
            ExpansionStartResult.InsufficientResources => "자원 부족",
            _ => null,
        };

        // ---------------- 구성 ----------------

        /// <summary>13-6a: 왼쪽 위 [관리] 메뉴에 등록 — 레시피가 하나라도 보이거나(그림자 포함) 합성이 열리면.</summary>
        private void BuildLauncher()
        {
            var root = (RectTransform)transform;
            HudLauncher.For(_ui, root).Add(new HudLauncher.Entry
            {
                Key = "recipes", Order = HudLauncher.Order.Recipes, Name = "합성",
                KeyLabel = () => KeyBindings.Label(GameAction.Recipes), IsOpen = () => _open, Click = Toggle, Visible = AnyRecipeShown,
            });
            _tracker = _ui.Label(root, "", 15f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(_tracker.rectTransform, new Vector2(28f, -76f), new Vector2(420f, 120f));
        }

        private void BuildWindow()
        {
            var root = (RectTransform)transform;
            _window = HoloUi.Rect("RecipeWindow", root);
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = _windowOffset;
            const float head = 96f, foot = 92f, side = 33f, bodyH = BodyHeight;
            _window.sizeDelta = new Vector2(side * 2f + ListWidth + 24f + DetailWidth, head + bodyH + foot);
            _group = _window.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _fx = _ui.Window(_window.gameObject, new Color(0.03f, 0.07f, 0.11f, 0.96f), "SYNTHESIS");

            var title = _ui.Label(_window, "합성 레시피", 32f, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            HoloUi.Place(title.rectTransform, new Vector2(40f, -30f), new Vector2(500f, 46f));
            HoloUi.Glow(title, 0.5f);

            // 왼쪽 목록 (12-4: 미공개 레시피는 빼고 위로 채움 — RefreshList). 진화 모듈 48종: 보이는 줄이 본문 높이를 넘으면 스크롤 (가린 부분은 잘림)
            var viewport = HoloUi.Rect("ListViewport", _window);
            HoloUi.Place(viewport, new Vector2(side, -head), new Vector2(ListWidth, bodyH));
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // 빈 곳에서도 휠을 받게
            _listContent = HoloUi.Rect("ListContent", viewport);
            HoloUi.Place(_listContent, Vector2.zero, new Vector2(ListWidth, bodyH));
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = _listContent;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = RowHeight * 0.6f;
            _listTop = 0f;
            _listX = 0f;
            float y = 0f;
            foreach (var recipe in _syn.Recipes)
            {
                var r = recipe;
                var b = _ui.TechButton(_listContent, "", 15f, () => Select(r));
                HoloUi.Place((RectTransform)b.transform, new Vector2(0f, y), new Vector2(ListWidth, RowHeight - 6f));
                var label = b.GetComponentInChildren<TMP_Text>();
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(14f, 0f, 8f, 0f);
                _rows.Add((recipe, b, label));
                y -= RowHeight;
            }

            // 오른쪽 상세
            float x = side + ListWidth + 24f;
            var panel = HoloUi.Rect("Detail", _window);
            HoloUi.Place(panel, new Vector2(x, -head), new Vector2(DetailWidth, bodyH));
            _ui.Panel(panel.gameObject, new Color(0.05f, 0.1f, 0.15f, 0.85f), new Color(0.3f, 0.75f, 0.95f, 0.5f));

            _title = _ui.Label(panel, "", 24f, TextAlignmentOptions.TopLeft);
            _title.fontStyle = FontStyles.Bold;
            HoloUi.Place(_title.rectTransform, new Vector2(20f, -14f), new Vector2(DetailWidth - 40f, 34f));
            _body = _ui.Label(panel, "", 16f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(_body.rectTransform, new Vector2(20f, -54f), new Vector2(DetailWidth - ShapeWidth - 50f, 250f));
            _shape = HoloUi.Rect("Shape", panel);
            HoloUi.Place(_shape, new Vector2(DetailWidth - ShapeWidth - 20f, -54f), new Vector2(ShapeWidth, ShapeHeight));
            _effects = _ui.Label(panel, "", 15f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(_effects.rectTransform, new Vector2(20f, -64f - ShapeHeight), new Vector2(DetailWidth - 40f, 150f));

            _place = _ui.TechButton(panel, "위치 지정", 18f, PlaceSelected);
            HoloUi.Place((RectTransform)_place.transform, new Vector2(DetailWidth - 200f, -bodyH + 66f), new Vector2(180f, 48f));
            _researchButton = _ui.TechButton(panel, "해금 연구 시작", 18f, ResearchSelected);
            HoloUi.Place((RectTransform)_researchButton.transform, new Vector2(DetailWidth - 220f, -bodyH + 66f), new Vector2(200f, 48f));
            _researchStatus = _ui.Label(panel, "", 15f, TextAlignmentOptions.MidlineLeft, wrap: true);
            HoloUi.Place(_researchStatus.rectTransform, new Vector2(20f, -bodyH + 70f), new Vector2(DetailWidth - 260f, 56f));
            _slots = _ui.Label(_window, "", 16f, TextAlignmentOptions.MidlineRight);
            HoloUi.Place(_slots.rectTransform, new Vector2(side + ListWidth + 24f, -40f), new Vector2(DetailWidth, 30f));

            var warn = _ui.Label(_window, $"<color={HudText.Muted}>재료를 규격 모양 안에 맞닿게 지어 두고 [위치 지정] · 합성 중에는 재료 가동이 멈추고 철거할 수 없습니다 · 분해할 수 없습니다</color>", 15f, TextAlignmentOptions.MidlineLeft);
            warn.rectTransform.anchorMin = warn.rectTransform.anchorMax = warn.rectTransform.pivot = new Vector2(0f, 0f);
            warn.rectTransform.anchoredPosition = new Vector2(42f, 30f);
            warn.rectTransform.sizeDelta = new Vector2(860f, 40f);
            var close = _ui.Button(_window, "닫기 (ESC)", 19f, Close);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-40f, 26f);
            crt.sizeDelta = new Vector2(190f, 48f);

            HoloGlitch.Add(_window, _holoArt);
            _tween = new UiTween(_window, _group, new Vector2(0f, -24f), 0.2f, 0.15f, glitch: true);
        }

        // ---------------- 표시 ----------------

        private void Refresh()
        {
            if (_open)
            {
                RefreshList();
                RefreshDetail();
            }
            RefreshTracker();
        }

        private void RefreshList()
        {
            var ex = _sim.ResearchExpansion;
            float y = _listTop;
            RecipeData firstVisible = null;
            bool selectedVisible = false;
            foreach (var (recipe, button, label) in _rows)
            {
                var state = StateOf(recipe);
                bool visible = state != RecipeState.Hidden;
                if (button.gameObject.activeSelf != visible)
                    button.gameObject.SetActive(visible);
                if (!visible)
                    continue;
                firstVisible ??= recipe;
                selectedVisible |= recipe == _selected;
                ((RectTransform)button.transform).anchoredPosition = new Vector2(_listX, y);
                y -= RowHeight;
                bool usable = _syn.IsUnlocked(recipe);
                string color = recipe == _selected ? "#7FD8FF" : usable ? "#FFFFFF" : HudText.Muted;
                string name = recipe == _selected ? $"<b>{recipe.DisplayName}</b>" : recipe.DisplayName;
                string tag;
                var project = ex != null ? ex.ProjectOf(recipe) : null;
                if (project != null)
                    tag = $" <size=80%><color={HudTheme.AccentHex}>연구 {project.Progress * 100f:0}%</color></size>";
                else if (state == RecipeState.Shadow)
                    tag = $" <size=80%><color={HudText.Yellow}>그림자</color></size>";
                else
                    tag = usable ? "" : $" <size=80%><color={HudText.Muted}>등급 잠김</color></size>";
                label.SetText($"<color={color}>{name}</color>{tag}");
            }
            if (_listContent != null)
                _listContent.sizeDelta = new Vector2(ListWidth, Mathf.Max(BodyHeight, _listTop - y));
            if (!selectedVisible && firstVisible != null && firstVisible != _selected)
            {
                _selected = firstVisible;
                RefreshDetail();
            }
        }

        private void RefreshDetail()
        {
            if (_selected == null || _title == null)
                return;
            var r = _selected;
            var ex = _sim.ResearchExpansion;
            var state = StateOf(r);
            if (_slots != null)
                _slots.SetText(ex != null
                    ? $"<color={HudText.Muted}>레시피 연구 슬롯</color> {ex.RecipeProjects.Count} / {ex.RecipeSlots}   <color={HudText.Muted}>연구 포인트</color> {_sim.Supply.ResearchPoints}"
                    : "");
            if (state == RecipeState.Shadow)
            {
                RefreshShadow(r, ex);
                return;
            }
            _researchButton.gameObject.SetActive(false);
            _researchStatus.gameObject.SetActive(false);
            _place.gameObject.SetActive(true);
            _effects.gameObject.SetActive(true);
            bool unlocked = _syn.IsUnlocked(r);
            _title.SetText($"{r.DisplayName}  <size=70%><color={HudText.Muted}>{r.Stage}단계 진화 · {r.Result.CellOffsets.Count}칸</color></size>");

            var sb = new StringBuilder();
            if (!unlocked)
            {
                string grade = r.UnlockGrade < _sim.Progression.GradeCount ? _sim.Progression.GetGrade(r.UnlockGrade).DisplayName : "?";
                sb.Append($"<color={HudText.Orange}>잠김: {grade} 등급부터</color>\n");
            }
            sb.Append($"<color={HudText.Muted}>재료</color>  ");
            bool first = true;
            foreach (var ing in r.Ingredients)
            {
                if (ing.Module == null)
                    continue;
                if (!first)
                    sb.Append(" + ");
                first = false;
                sb.Append(ing.Module.DisplayName).Append(" ×").Append(ing.Count);
            }
            sb.Append('\n');
            sb.Append($"<color={HudText.Muted}>합성 비용</color>  ").Append(HudText.Cost(_syn.GetCost(r)))
              .Append($"   <color={HudText.Muted}>시간</color>  ").Append((r.Duration / _sim.Effects.SynthesisSpeedMultiplier).ToString("0")).Append("초\n"); // M-50 손재주 반영
            sb.Append($"<color={HudText.Muted}>모듈 수</color>  재료 {r.Result.GradeModuleCount}개로 셈 (등급 조건)\n");
            if (!string.IsNullOrEmpty(r.Rationale))
                sb.Append($"\n<i>{r.Rationale}</i>\n");
            sb.Append(r.HasTemplate
                ? $"\n<size=90%><color={HudText.Muted}>오른쪽 그림처럼 재료를 붙여 지으면 합성 · 완성되면 아래 모양으로 바뀜 (완성 모양에서 새로 생기는 칸은 비워 둬야 함, 위치 지정 때 작은 상자로 보임) · 회전 가능</color></size>"
                : $"\n<size=90%><color={HudText.Muted}>규격 모양 안을 재료로 빈틈없이 채우면 합성 (어느 칸에 무엇이 있든 상관없음, 회전 가능)</color></size>");
            _body.SetText(sb.ToString());

            _effects.SetText($"<color=#7FD8FF><b>결과: {r.Result.DisplayName}</b></color>  <size=85%><color={HudText.Muted}>내구도 100%로 새로 시작</color></size>\n"
                             + HudText.ModuleEffects(r.Result, _sim.Effects));
            DrawShape(r, silhouette: false);
            _place.interactable = unlocked;
        }

        /// <summary>
        /// 12-4 그림자 레시피: 이름 + 검은 실루엣(규격 모양)만 보이고, 재료 · 효과는 해금 연구 뒤 공개.
        /// 해금 연구 비용(자원 + 연구 포인트 + 전력 · 시간) · 슬롯 · 진행 · 시작/취소.
        /// </summary>
        private void RefreshShadow(RecipeData r, ResearchExpansionSystem ex)
        {
            _place.gameObject.SetActive(false);
            _effects.gameObject.SetActive(false);
            _researchButton.gameObject.SetActive(true);
            _researchStatus.gameObject.SetActive(true);
            _title.SetText($"{r.DisplayName}  <size=70%><color={HudText.Yellow}>그림자 · {r.Stage}단계 진화</color></size>");
            var cost = ex.CostOf(r);
            var sb = new StringBuilder();
            sb.Append($"<color={HudText.Muted}>재료 · 결과 효과 · 합성 비용은 해금 연구를 마치면 공개됩니다</color>\n\n");
            sb.Append($"<b>해금 연구</b>  {HudText.Cost(cost.StartCost)}  <color={HudText.Yellow}>연구 포인트 {cost.ResearchPoints}</color>\n");
            sb.Append($"<color={HudText.Muted}>전력 +{cost.PowerDemand:0} · {cost.Duration:0}초 · 레시피 연구 슬롯 1개 (일반 연구와 따로) · 시작 비용은 돌려받지 않음</color>");
            if (r.UnlockGrade > _sim.Progression.GradeIndex && r.UnlockGrade < _sim.Progression.GradeCount)
                sb.Append($"\n<color={HudText.Orange}>합성은 {_sim.Progression.GetGrade(r.UnlockGrade).DisplayName} 등급부터</color>");
            _body.SetText(sb.ToString());
            DrawShape(r, silhouette: true);

            var project = ex.ProjectOf(r);
            var label = _researchButton.GetComponentInChildren<TMP_Text>();
            if (project != null)
            {
                label.SetText("연구 취소");
                _researchButton.interactable = true;
                _researchStatus.SetText(project.Paused
                    ? $"<color={HudText.Orange}>해금 연구 {project.Progress * 100f:0}% · 멈춤 (레시피 연구 슬롯 부족)</color>"
                    : $"해금 연구 {project.Progress * 100f:0}% · 남은 {ResearchView.FormatTime(project.RemainingSeconds)}{(project.Rate < 0.999f ? $" <color={HudText.Yellow}>(전력 {project.Rate * 100f:0}%)</color>" : "")}");
                return;
            }
            label.SetText("해금 연구 시작");
            var check = ex.CanStart(r);
            _researchButton.interactable = check == ExpansionStartResult.Ok;
            string reason = Reason(check, cost, _sim.Supply.ResearchPoints);
            _researchStatus.SetText(reason != null ? $"<color={HudText.Orange}>{reason}</color>" : $"<color={HudTheme.GreenHex}>해금 연구를 시작할 수 있습니다</color>");
        }

        // 입체 그림 밝기 (색약 사용자: 재료마다 색이 아니라 밝기 + 번호로 나눔)
        private static readonly Color[] PiecePalette =
        {
            new Color(0.55f, 0.85f, 1f), new Color(0.93f, 0.93f, 0.93f), new Color(0.3f, 0.55f, 0.8f), new Color(0.62f, 0.62f, 0.66f),
        };
        private static readonly Color[] ResultPalette = { new Color(0.55f, 0.85f, 1f) };
        private static readonly Color[] SilhouettePalette = { new Color(0.07f, 0.07f, 0.09f) };
        private static readonly Color ShapeEdge = new Color(0.02f, 0.05f, 0.08f);
        private static readonly Color SilhouetteEdge = new Color(0.55f, 0.6f, 0.7f);

        /// <summary>
        /// 규격 모양: 비스듬히 위에서 본 입체 상자 그림 (<see cref="IsoShapeRenderer"/>, 사용자 2026-11-06 "레시피 표시 알아보기 너무 불편" — 층별 칸 그림을 바꿈).
        /// 그림자 레시피는 결과 모양의 검은 실루엣. 합성 틀이 있는 레시피는 "재료 놓는 모양"(재료마다 번호 · 밝기) → "완성 모양" 두 그림.
        /// 같은 레시피 · 상태면 다시 그리지 않음 (창은 0.25초마다 새로 고침).
        /// </summary>
        private void DrawShape(RecipeData recipe, bool silhouette)
        {
            if (recipe == _drawnRecipe && silhouette == _drawnSilhouette)
                return;
            _drawnRecipe = recipe;
            _drawnSilhouette = silhouette;
            for (int i = _shape.childCount - 1; i >= 0; i--)
                Destroy(_shape.GetChild(i).gameObject);
            var result = recipe.Result.CellOffsets;
            const float captionHeight = 20f;

            _tiling.Clear();
            if (recipe.HasTemplate && !silhouette)
            {
                var pieces = new List<ModuleData>();
                foreach (var ing in recipe.Ingredients)
                    for (int i = 0; i < ing.Count; i++)
                        pieces.Add(ing.Module);
                ShapeTiling.TryTile(recipe.MaterialCells, pieces, _tiling);
            }
            if (_tiling.Count == 0)
            {
                Caption(silhouette ? "규격" : $"규격 ({result.Count}칸, 회전 가능)", 0f);
                var tex = ShapeTexture(recipe, silhouette ? 2 : 1, result, null, silhouette ? SilhouettePalette : ResultPalette,
                    silhouette ? SilhouetteEdge : ShapeEdge, ShapeHeight - captionHeight, null);
                Picture(tex, -captionHeight, ShapeHeight - captionHeight, null);
                return;
            }

            // 합성 틀: 재료 놓는 모양 (재료마다 번호) → 완성 모양
            var cells = new List<Vector3Int>();
            var groups = new List<int>();
            var names = new StringBuilder();
            for (int p = 0; p < _tiling.Count; p++)
            {
                var piece = _tiling[p];
                foreach (var c in StationGrid.ResolveCells(piece.Data.CellOffsets, piece.Origin, piece.Rotation))
                {
                    cells.Add(c);
                    groups.Add(p % PiecePalette.Length);
                }
                names.Append(p == 0 ? "" : "  ").Append("<b>").Append(p + 1).Append("</b> ").Append(piece.Data.DisplayName);
            }
            const float materialHeight = 112f, legendHeight = 34f;
            Caption("재료 놓는 모양", 0f);
            var labels = new List<Vector2>();
            var materialTex = ShapeTexture(recipe, 0, cells, groups, PiecePalette, ShapeEdge, materialHeight, labels);
            Picture(materialTex, -captionHeight, materialHeight, labels);
            var legend = _ui.Label(_shape, names.ToString(), 12f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(legend.rectTransform, new Vector2(0f, -captionHeight - materialHeight - 2f), new Vector2(ShapeWidth, legendHeight));
            float y = captionHeight + materialHeight + legendHeight + 4f;
            Caption("완성되면 이 모양", -y);
            var resultTex = ShapeTexture(recipe, 1, result, null, ResultPalette, ShapeEdge, ShapeHeight - y - captionHeight, null);
            Picture(resultTex, -y - captionHeight, ShapeHeight - y - captionHeight, null);
        }

        private void Caption(string text, float y)
        {
            var label = _ui.Label(_shape, text, 13f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(label.rectTransform, new Vector2(0f, y), new Vector2(ShapeWidth, 20f));
        }

        /// <summary>레시피마다 그림 텍스처 (kind 0 = 재료 놓는 모양, 1 = 완성 · 규격, 2 = 실루엣). 2배 해상도로 그려 둠.</summary>
        private Texture2D ShapeTexture(RecipeData recipe, int kind, IReadOnlyList<Vector3Int> cells, IReadOnlyList<int> groups, Color[] palette, Color edge,
            float boxHeight, List<Vector2> labels)
        {
            var key = (recipe, kind);
            var zeros = groups ?? new int[cells.Count];
            if (_shapeTextures.TryGetValue(key, out var cached) && labels == null)
                return cached;
            if (cached != null)
                Destroy(cached);
            var tex = IsoShapeRenderer.Render(cells, zeros, palette, edge, Mathf.RoundToInt(ShapeWidth * 2f), Mathf.RoundToInt(boxHeight * 2f), labels);
            _shapeTextures[key] = tex;
            return tex;
        }

        /// <summary>그림을 상자(너비 ShapeWidth × boxHeight) 가운데 위에 비율대로 · 재료 번호 표시. 2배 해상도 텍스처라 그보다 키우지 않음.</summary>
        private void Picture(Texture2D tex, float y, float boxHeight, List<Vector2> labels)
        {
            float scale = Mathf.Min(0.5f, Mathf.Min(ShapeWidth / tex.width, boxHeight / tex.height));
            var size = new Vector2(tex.width * scale, tex.height * scale);
            var rt = HoloUi.Rect("ShapePicture", _shape);
            float x = (ShapeWidth - size.x) * 0.5f;
            HoloUi.Place(rt, new Vector2(x, y), size);
            var image = rt.gameObject.AddComponent<RawImage>();
            image.texture = tex;
            image.raycastTarget = false;
            if (labels == null)
                return;
            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i].x < 0f)
                    continue;
                // 번호 딱지: 어두운 바탕 + 흰 숫자 (그 재료 윗면 가운데)
                const float badge = 16f;
                var at = new Vector2(x + labels[i].x * scale - badge * 0.5f, y - labels[i].y * scale + badge * 0.5f);
                var back = HoloUi.Rect("Badge", _shape);
                HoloUi.Place(back, at, new Vector2(badge, badge));
                var backImage = back.gameObject.AddComponent<Image>();
                backImage.sprite = _fillSprite;
                backImage.raycastTarget = false;
                backImage.color = new Color(0.03f, 0.06f, 0.1f, 0.92f);
                var number = _ui.Label(back, (i + 1).ToString(), 12f, TextAlignmentOptions.Center);
                number.fontStyle = FontStyles.Bold;
                HoloUi.Place(number.rectTransform, Vector2.zero, new Vector2(badge, badge));
            }
        }

        private void RefreshTracker()
        {
            float top = -76f - (_research != null ? _research.TrackerHeight + (_research.TrackerHeight > 0f ? 6f : 0f) : 0f);
            _tracker.rectTransform.anchoredPosition = new Vector2(28f, top);
            var ex = _sim.ResearchExpansion;
            if (_syn.Jobs.Count == 0 && (ex == null || ex.RecipeProjects.Count == 0))
            {
                _tracker.SetText("");
                return;
            }
            var sb = new StringBuilder();
            if (ex != null)
            {
                foreach (var p in ex.RecipeProjects)
                {
                    if (sb.Length > 0)
                        sb.Append('\n');
                    sb.Append($"<color={HudText.Yellow}>레시피 연구</color> ").Append(p.DisplayName).Append("  ").Append((p.Progress * 100f).ToString("0")).Append('%');
                    sb.Append(p.Paused ? $" <color={HudText.Orange}>멈춤</color>" : $" <color={HudText.Muted}>{ResearchView.FormatTime(p.RemainingSeconds)}</color>");
                }
            }
            foreach (var job in _syn.Jobs)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append("<color=#B79CFF>합성</color> ").Append(job.Recipe.DisplayName).Append("  ").Append((job.Progress01 * 100f).ToString("0")).Append('%');
                sb.Append(job.Paused ? $" <color={HudText.Orange}>멈춤 (파손 재료)</color>" : $" <color={HudText.Muted}>{ResearchView.FormatTime(_syn.SecondsLeft(job))}</color>");
            }
            _tracker.SetText(sb.ToString());
        }
    }
}
