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
    /// 13-4 장 직전 컷신 (사용자 결정 2026-10-18): 검은 화면에 대사가 한 줄씩 타자로 나오고, 끝나면 화면이 켜지며 게임이 시작된다.
    /// 1장 = 인간이 남긴 방주 시스템 음성 → 마지막에 깨어나는 코어의 첫 말("코어:"로 시작하는 줄 = 코어 색 · 코어 기록에 남음).
    /// 새 캠페인마다(불러오기는 아님) · 클릭 · Space · Enter = 다음 줄(타자 중이면 줄 끝까지), ESC = 건너뛰기. 보는 동안 시간 · 조작 멈춤.
    /// 13-7b: 마지막 줄 뒤 검은 화면 위 장 제목 카드("1장" · "깨어남", 장 전환 연출과 같은 모양) → 화면이 켜짐. 내용은 코드로 만든다.
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public sealed class ChapterIntroView : MonoBehaviour
    {
        private const string CorePrefix = "코어:";
        private const string Soft = "#8E9AAB";

        [SerializeField] private StationController _station;
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [SerializeField] private HoloArt _holoArt;
        [Tooltip("타자 속도 (글자 / 초)")]
        [SerializeField] private float _charsPerSecond = 28f;
        [Tooltip("다 보고 화면이 켜지는 시간 (초)")]
        [SerializeField] private float _fadeSeconds = 1.2f;
        [Tooltip("13-7b 마지막 줄 뒤 장 제목 카드('1장' · '깨어남')가 떠 있는 시간 (초, 클릭 · Space · ESC로 넘김)")]
        [SerializeField] private float _titleSeconds = 2.4f;

        private HoloUi _ui;
        private StationSimulation _sim;
        private ChapterData _chapter;
        private CanvasGroup _group;
        private TMP_Text _speaker, _history, _line, _hint, _title, _subtitle;
        private int _index = -1;
        private float _typed;
        private bool _fading, _titling, _wasPaused, _wasLocked, _wasBlocked;
        private float _fadeStart, _titleStart;
        private readonly StringBuilder _sb = new StringBuilder();

        public bool Playing { get; private set; }

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            var chapters = _sim?.Chapters;
            var host = FindFirstObjectByType<SimulationHost>();
            bool fresh = host != null && host.LoadedSave == null && chapters != null && chapters.Index == 0 && chapters.ChapterSeconds < 0.5f;
            _chapter = chapters != null ? chapters.Current : null;
            if (!fresh || _chapter == null || _chapter.IntroLines.Count == 0)
            {
                gameObject.SetActive(false);
                return;
            }
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            Build();
            Play();
        }

        private void Build()
        {
            var root = (RectTransform)transform;
            var black = root.gameObject.AddComponent<Image>();
            black.color = Color.black; // 화면 전체를 덮고 클릭을 막음
            _group = root.gameObject.AddComponent<CanvasGroup>();

            var column = HoloUi.Rect("Column", root);
            column.anchorMin = column.anchorMax = column.pivot = new Vector2(0.5f, 0.5f);
            column.sizeDelta = new Vector2(1180f, 560f);
            _history = _ui.Label(column, "", 24f, TextAlignmentOptions.BottomLeft, wrap: true);
            HoloUi.Place(_history.rectTransform, new Vector2(0f, 0f), new Vector2(1180f, 300f));
            _speaker = _ui.Label(column, "", 18f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(_speaker.rectTransform, new Vector2(0f, -318f), new Vector2(1180f, 28f));
            _line = _ui.Label(column, "", 32f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(_line.rectTransform, new Vector2(0f, -350f), new Vector2(1180f, 200f));
            HoloUi.Glow(_line, 0.35f);

            // 13-7b 장 제목 카드 (장 전환 연출과 같은 모양)
            CinematicStage.TitleLabels(_ui, root, out _title, out _subtitle);
            _title.alpha = _subtitle.alpha = 0f;

            _hint = _ui.Label(root, $"<color={Soft}>클릭 · Space  다음      ESC  건너뛰기</color>", 17f, TextAlignmentOptions.Bottom);
            var hrt = _hint.rectTransform;
            hrt.anchorMin = new Vector2(0f, 0f);
            hrt.anchorMax = new Vector2(1f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.anchoredPosition = new Vector2(0f, 48f);
            hrt.sizeDelta = new Vector2(0f, 30f);
        }

        private void Play()
        {
            transform.SetAsLastSibling(); // 다른 HUD 위
            Playing = true;
            _wasPaused = _clock.Clock.IsPaused;
            _wasLocked = _clock.InputLocked;
            _wasBlocked = InputGate.Blocked;
            _clock.Clock.SetPaused(true);
            _clock.InputLocked = true;
            InputGate.Blocked = true;
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            Next();
        }

        private static bool IsCore(string line) => line != null && line.StartsWith(CorePrefix);

        private static string Body(string line) => IsCore(line) ? line.Substring(CorePrefix.Length).TrimStart() : line;

        private void Next()
        {
            var lines = _chapter.IntroLines;
            if (_index >= 0 && _index < lines.Count && IsCore(lines[_index]))
                _sim.GetSystem<CorePersonalitySystem>()?.Log(Body(lines[_index])); // 코어의 첫 말은 코어 기록에도
            _index++;
            if (_index >= lines.Count)
            {
                Finish();
                return;
            }
            // 지난 줄은 위에 흐리게 (최근 3줄)
            _sb.Clear();
            for (int i = Mathf.Max(0, _index - 3); i < _index; i++)
                _sb.Append($"<color={Soft}>{Body(lines[i])}</color>\n");
            _history.SetText(_sb.ToString());
            string line = lines[_index];
            bool core = IsCore(line);
            _speaker.SetText(core ? $"<color={ChapterView.CoreTint}>코어</color>" : $"<color={Soft}>{_chapter.IntroSpeaker}</color>");
            _line.SetText(core ? $"<color={ChapterView.CoreTint}>{Body(line)}</color>" : Body(line));
            _line.maxVisibleCharacters = 0;
            _typed = 0f;
            AudioService.TryPlay(l => l.UiClick);
        }

        private int LineLength => _line.textInfo != null ? _line.textInfo.characterCount : 0;

        private void Update()
        {
            if (!Playing)
                return;
            if (_fading)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - _fadeStart) / Mathf.Max(0.01f, _fadeSeconds));
                _group.alpha = 1f - k;
                if (k >= 1f)
                    End();
                return;
            }
            if (_titling)
            {
                // 장 제목 카드: 0.5초에 걸쳐 나타남 → 정해진 시간 또는 클릭 · Space · ESC → 화면이 켜짐
                float e = Time.unscaledTime - _titleStart;
                _title.alpha = _subtitle.alpha = Mathf.Clamp01(e / 0.5f);
                var kb = Keyboard.current;
                var ms = Mouse.current;
                bool pass = e > 0.6f && ((ms != null && ms.leftButton.wasPressedThisFrame)
                    || (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)));
                if (kb != null && kb.escapeKey.wasPressedThisFrame)
                    InputGate.ConsumeEscape();
                if (e >= _titleSeconds || pass)
                {
                    _titling = false;
                    _fading = true;
                    _fadeStart = Time.unscaledTime;
                    _group.blocksRaycasts = false;
                }
                return;
            }
            _typed += Time.unscaledDeltaTime * _charsPerSecond;
            _line.ForceMeshUpdate();
            int length = LineLength;
            bool typing = _typed < length;
            _line.maxVisibleCharacters = typing ? Mathf.FloorToInt(_typed) : 99999;

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                InputGate.ConsumeEscape();
                Finish();
                return;
            }
            bool advance = (mouse != null && mouse.leftButton.wasPressedThisFrame)
                           || (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame));
            if (!advance)
                return;
            if (typing)
                _typed = length; // 줄 끝까지
            else
                Next();
        }

        private void Finish()
        {
            if (_fading || _titling)
                return;
            var lines = _chapter.IntroLines;
            for (int i = Mathf.Max(0, _index); i < lines.Count; i++) // 건너뛴 코어의 말도 기록에는 남김
                if (IsCore(lines[i]))
                    _sim.GetSystem<CorePersonalitySystem>()?.Log(Body(lines[i]));
            _index = lines.Count;
            _hint.gameObject.SetActive(false);
            _history.SetText("");
            _speaker.SetText("");
            _line.SetText("");
            // 13-7b 검은 화면 위 장 제목 카드 → 화면이 켜짐 (장 전환 연출과 같은 모양)
            _title.SetText(_chapter.DisplayName);
            _subtitle.SetText(string.IsNullOrEmpty(_chapter.Subtitle) ? "" : $"<color={CinematicStage.Soft}>{_chapter.Subtitle}</color>");
            _titling = true;
            _titleStart = Time.unscaledTime;
        }

        private void End()
        {
            Playing = false;
            _clock.InputLocked = _wasLocked;
            _clock.Clock.SetPaused(_wasPaused);
            InputGate.Blocked = _wasBlocked;
            gameObject.SetActive(false);
        }
    }
}
