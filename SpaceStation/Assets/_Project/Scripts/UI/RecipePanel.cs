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
    /// 12-3 합성 레시피(도감) 창 (PHASESET A7 · A9). K 키 또는 왼쪽 위 [합성] 버튼으로 연다. 게임 시간은 멈추지 않는다.
    /// 왼쪽 = 레시피 목록, 오른쪽 = 고른 레시피의 재료 · 규격 모양(층별 칸 그림) · 결과 효과 · 비용 · 시간 · 당위성 + [위치 지정].
    /// [위치 지정]을 누르면 창이 닫히고 <see cref="SynthesisController"/>가 규격 고스트를 띄운다.
    /// 왼쪽 위 추적기: 진행 중인 합성 (연구 추적기 아래).
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public sealed class RecipePanel : MonoBehaviour
    {
        private const float ListWidth = 250f;
        private const float DetailWidth = 640f;
        private const float RowHeight = 46f;
        private const float Cell = 22f;

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
        private TMP_Text _title, _body, _effects, _tracker, _launcherLabel;
        private RectTransform _shape;
        private Button _place;

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
            KeyBindings.Changed -= RefreshLauncher;
            HudWindows.Opened -= HandleWindowOpened;
            if (_syn != null)
                _syn.Completed -= HandleCompleted;
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

        // ---------------- 구성 ----------------

        private static string LauncherText() => $"합성 ({KeyBindings.Label(GameAction.Recipes)})";

        private void RefreshLauncher()
        {
            if (_launcherLabel != null)
                _launcherLabel.SetText(LauncherText());
        }

        private void BuildLauncher()
        {
            var root = (RectTransform)transform;
            var button = _ui.TechButton(root, LauncherText(), 19f, Toggle);
            HoloUi.Place((RectTransform)button.transform, new Vector2(350f, -24f), new Vector2(140f, 44f)); // 주민 버튼 오른쪽
            _launcherLabel = button.GetComponentInChildren<TMP_Text>();
            KeyBindings.Changed += RefreshLauncher;
            _tracker = _ui.Label(root, "", 15f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(_tracker.rectTransform, new Vector2(28f, -76f), new Vector2(420f, 120f));
        }

        private void BuildWindow()
        {
            var root = (RectTransform)transform;
            _window = HoloUi.Rect("RecipeWindow", root);
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = new Vector2(-150f, 40f);
            const float head = 96f, foot = 92f, side = 33f, bodyH = 560f;
            _window.sizeDelta = new Vector2(side * 2f + ListWidth + 24f + DetailWidth, head + bodyH + foot);
            _group = _window.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _fx = _ui.Window(_window.gameObject, new Color(0.03f, 0.07f, 0.11f, 0.96f), "SYNTHESIS");

            var title = _ui.Label(_window, "합성 레시피", 32f, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            HoloUi.Place(title.rectTransform, new Vector2(40f, -30f), new Vector2(500f, 46f));
            HoloUi.Glow(title, 0.5f);

            // 왼쪽 목록
            float y = -head;
            foreach (var recipe in _syn.Recipes)
            {
                var r = recipe;
                var b = _ui.TechButton(_window, "", 15f, () => Select(r));
                HoloUi.Place((RectTransform)b.transform, new Vector2(side, y), new Vector2(ListWidth, RowHeight - 6f));
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
            HoloUi.Place(_body.rectTransform, new Vector2(20f, -54f), new Vector2(DetailWidth - 220f, 250f));
            _shape = HoloUi.Rect("Shape", panel);
            HoloUi.Place(_shape, new Vector2(DetailWidth - 190f, -54f), new Vector2(170f, 200f));
            _effects = _ui.Label(panel, "", 15f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(_effects.rectTransform, new Vector2(20f, -310f), new Vector2(DetailWidth - 40f, 170f));

            _place = _ui.TechButton(panel, "위치 지정", 18f, PlaceSelected);
            HoloUi.Place((RectTransform)_place.transform, new Vector2(DetailWidth - 200f, -bodyH + 66f), new Vector2(180f, 48f));

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
            foreach (var (recipe, _, label) in _rows)
            {
                bool unlocked = _syn.IsUnlocked(recipe);
                string color = recipe == _selected ? "#7FD8FF" : unlocked ? "#FFFFFF" : HudText.Muted;
                string name = recipe == _selected ? $"<b>{recipe.DisplayName}</b>" : recipe.DisplayName;
                label.SetText($"<color={color}>{name}</color>{(unlocked ? "" : $" <size=80%><color={HudText.Muted}>잠김</color></size>")}");
            }
        }

        private void RefreshDetail()
        {
            if (_selected == null || _title == null)
                return;
            var r = _selected;
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
              .Append($"   <color={HudText.Muted}>시간</color>  ").Append(r.Duration.ToString("0")).Append("초\n");
            sb.Append($"<color={HudText.Muted}>모듈 수</color>  재료 {r.Result.GradeModuleCount}개로 셈 (등급 조건)\n");
            if (!string.IsNullOrEmpty(r.Rationale))
                sb.Append($"\n<i>{r.Rationale}</i>\n");
            sb.Append($"\n<size=90%><color={HudText.Muted}>규격 모양 안을 재료로 빈틈없이 채우면 합성 (어느 칸에 무엇이 있든 상관없음, 회전 가능)</color></size>");
            _body.SetText(sb.ToString());

            _effects.SetText($"<color=#7FD8FF><b>결과: {r.Result.DisplayName}</b></color>  <size=85%><color={HudText.Muted}>내구도 100%로 새로 시작</color></size>\n"
                             + HudText.ModuleEffects(r.Result, _sim.Effects));
            DrawShape(r.Result);
            _place.interactable = unlocked;
        }

        /// <summary>규격 모양: 층마다 위에서 본 칸 그림 (아래층부터 왼쪽→오른쪽).</summary>
        private void DrawShape(ModuleData result)
        {
            for (int i = _shape.childCount - 1; i >= 0; i--)
                Destroy(_shape.GetChild(i).gameObject);
            var cells = result.CellOffsets;
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            foreach (var c in cells)
            {
                minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
                minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y);
                minZ = Mathf.Min(minZ, c.z); maxZ = Mathf.Max(maxZ, c.z);
            }
            int w = maxX - minX + 1, d = maxZ - minZ + 1, layers = maxY - minY + 1;
            var caption = _ui.Label(_shape, layers > 1 ? $"규격 ({layers}층, 위에서 본 모양)" : "규격 (위에서 본 모양)", 13f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(caption.rectTransform, Vector2.zero, new Vector2(170f, 20f));
            for (int layer = 0; layer < layers; layer++)
            {
                float ox = layer * (w * Cell + 14f), oy = -26f;
                for (int x = 0; x < w; x++)
                    for (int z = 0; z < d; z++)
                    {
                        bool filled = false;
                        foreach (var c in cells)
                            filled |= c.x - minX == x && c.y - minY == layer && c.z - minZ == z;
                        var rt = HoloUi.Rect("Cell", _shape);
                        HoloUi.Place(rt, new Vector2(ox + x * Cell, oy - (d - 1 - z) * Cell), new Vector2(Cell - 3f, Cell - 3f));
                        var img = rt.gameObject.AddComponent<Image>();
                        img.sprite = _fillSprite;
                        img.raycastTarget = false;
                        img.color = filled ? new Color(0.45f, 0.85f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.06f);
                    }
                if (layers > 1)
                {
                    var l = _ui.Label(_shape, $"{layer + 1}층", 12f, TextAlignmentOptions.TopLeft);
                    HoloUi.Place(l.rectTransform, new Vector2(ox, oy - d * Cell - 2f), new Vector2(60f, 18f));
                }
            }
        }

        private void RefreshTracker()
        {
            float top = -76f - (_research != null ? _research.TrackerHeight + (_research.TrackerHeight > 0f ? 6f : 0f) : 0f);
            _tracker.rectTransform.anchoredPosition = new Vector2(28f, top);
            if (_syn.Jobs.Count == 0)
            {
                _tracker.SetText("");
                return;
            }
            var sb = new StringBuilder();
            foreach (var job in _syn.Jobs)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append("<color=#B79CFF>합성</color> ").Append(job.Recipe.DisplayName).Append("  ").Append((job.Progress01 * 100f).ToString("0")).Append('%');
                sb.Append(job.Paused ? $" <color={HudText.Orange}>멈춤 (파손 재료)</color>" : $" <color={HudText.Muted}>{ResearchView.FormatTime(job.Remaining)}</color>");
            }
            _tracker.SetText(sb.ToString());
        }
    }
}
