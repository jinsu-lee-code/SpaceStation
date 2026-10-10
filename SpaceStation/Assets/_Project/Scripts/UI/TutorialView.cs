using System.Collections.Generic;
using System.Text;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Settings;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// Phase 9 튜토리얼 화면: 왼쪽 위 목표 카드(단계·설명·진행 체크·상태) + 눌러야 할 HUD 위치에 깜빡이는 테두리.
    /// 진행 판정은 <see cref="TutorialRunner"/>(시뮬레이션), 여기서는 카메라 조작·배속을 알리고 보여 주기만 한다.
    /// 튜토리얼을 끝내거나 건너뛰면 설정 "다음 새 게임에서 튜토리얼"을 끈다.
    /// 내용은 코드로 만든다 (씬에는 참조만 연결된 빈 오브젝트).
    /// </summary>
    public sealed class TutorialView : MonoBehaviour
    {
        [SerializeField] private StationController _station;
        [SerializeField] private BuildController _build;
        [SerializeField] private BuildMenu _buildMenu;
        [SerializeField] private SelectionActionsPanel _selectionPanel;
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private OrbitCameraController _camera;
        [SerializeField] private RectTransform _resourcePanel;
        [SerializeField] private RectTransform _timePanel;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [Tooltip("11-15 테크 홀로그램 (없으면 5-8 기본 모양)")]
        [SerializeField] private HoloArt _holoArt;
        [SerializeField] private float _cardWidth = 380f;
        [SerializeField] private float _cardTop = -72f;
        [SerializeField] private float _completedFlashSeconds = 1.6f;
        [SerializeField] private float _supplyMessageSeconds = 5f;
        [SerializeField] private float _skipConfirmSeconds = 3f;

        private HoloUi _ui;
        private StationSimulation _sim;
        private TutorialRunner _runner;

        private RectTransform _card;
        private CanvasGroup _group;
        private UiTween _tween;
        private Image _cardFrame;
        private TMP_Text _header;
        private TMP_Text _title;
        private TMP_Text _body;
        private TMP_Text _checklist;
        private TMP_Text _status;
        private Button _skipButton;
        private TMP_Text _skipLabel;
        private Button _closeButton;
        private RectTransform _frameA;
        private RectTransform _frameB;

        private bool _finished;
        private float _flashUntil = -1f;
        private string _flashText;
        private float _supplyUntil = -1f;
        private string _supplyText;
        private float _skipConfirmUntil = -1f;

        // 카메라 누적량
        private bool _cameraSampled;
        private float _lastYaw, _lastPitch, _lastDistance;
        private Vector3 _lastFocus;
        private float _rotated, _zoomed, _moved;

        private readonly StringBuilder _sb = new StringBuilder();
        private readonly List<string> _lines = new List<string>();

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            _runner = _sim?.Tutorial;
            if (_runner == null || !_runner.Active)
            {
                gameObject.SetActive(false);
                return;
            }
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            BuildCard();
            _frameA = CreateHighlight("HighlightA");
            _frameB = CreateHighlight("HighlightB");
            _runner.StepCompleted += HandleStepCompleted;
            _runner.Supplied += HandleSupplied;
            _runner.Finished += HandleFinished;
            _tween.Play(restart: true);
            Refresh();
        }

        private void OnDestroy()
        {
            if (_runner == null)
                return;
            _runner.StepCompleted -= HandleStepCompleted;
            _runner.Supplied -= HandleSupplied;
            _runner.Finished -= HandleFinished;
        }

        // ---------------- 만들기 ----------------

        private void BuildCard()
        {
            _card = HoloUi.Rect("TutorialCard", transform);
            _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(0f, 1f); // 왼쪽 (오른쪽은 자원 패널)
            _card.anchoredPosition = new Vector2(16f, _cardTop);
            _ui.Panel(_card.gameObject, HudTheme.PanelFill, HudTheme.Accent);
            _cardFrame = _card.Find("Frame").GetComponent<Image>();
            HoloUi.Decorative((RectTransform)_cardFrame.transform); // 테두리는 세로 배치에서 빼고 카드 전체를 덮음
            _group = _card.gameObject.AddComponent<CanvasGroup>();
            var layout = _card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 14);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = _card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _card.sizeDelta = new Vector2(_cardWidth, 0f);

            _header = _ui.Label(_card, "", HudTheme.SmallSize, TextAlignmentOptions.Left);
            _title = _ui.Label(_card, "", HudTheme.TitleSize, TextAlignmentOptions.Left, wrap: true);
            _title.fontStyle = FontStyles.Bold;
            _body = _ui.Label(_card, "", 17f, TextAlignmentOptions.TopLeft, wrap: true);
            _checklist = _ui.Label(_card, "", 17f, TextAlignmentOptions.TopLeft, wrap: true);
            _status = _ui.Label(_card, "", HudTheme.SmallSize, TextAlignmentOptions.TopLeft, wrap: true);

            var buttons = HoloUi.Rect("Buttons", _card);
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            _skipButton = _ui.Button(buttons, "건너뛰기", HudTheme.SmallSize, HandleSkipClicked);
            var srt = (RectTransform)_skipButton.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1f, 0.5f);
            srt.anchoredPosition = Vector2.zero;
            srt.sizeDelta = new Vector2(150f, 32f);
            _skipLabel = _skipButton.GetComponentInChildren<TMP_Text>();
            _closeButton = _ui.Button(buttons, "닫기", HudTheme.BodySize, HandleCloseClicked);
            var crt = (RectTransform)_closeButton.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0.5f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(120f, 32f);
            _closeButton.gameObject.SetActive(false);

            HoloGlitch.Add(_card, _holoArt);
            _tween = new UiTween(_card, _group, new Vector2(-30f, 0f), 0.25f, 0.2f, glitch: true);
        }

        private RectTransform CreateHighlight(string name)
        {
            var rt = HoloUi.Rect(name, transform);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = _frameSprite;
            img.type = Image.Type.Sliced;
            img.color = HudTheme.ButtonWarning;
            img.raycastTarget = false;
            rt.gameObject.SetActive(false);
            return rt;
        }

        // ---------------- 매 프레임 ----------------

        private void Update()
        {
            if (_tween == null)
                return;
            _tween.Update();
            if (_sim.Session.IsGameOver)
            {
                HideHighlights();
                if (_tween.Visible)
                    _tween.Hide();
                return;
            }
            if (_finished)
                return;
            TrackCamera();
            if (_clock != null && !_clock.Clock.IsPaused)
                _runner.ReportSpeed(_clock.Clock.Speed);
            if (_skipConfirmUntil > 0f && Time.unscaledTime > _skipConfirmUntil)
            {
                _skipConfirmUntil = -1f;
                _skipLabel.SetText("건너뛰기");
            }
            Refresh();
        }

        private void LateUpdate()
        {
            if (_finished || _runner == null || !_runner.Active)
            {
                HideHighlights();
                return;
            }
            var step = _runner.Current;
            RectTransform a = null, b = null;
            if (step != null)
            {
                a = BuildTarget(step);
                switch (step.Highlight)
                {
                    case TutorialHighlight.Resources: b = _resourcePanel; break;
                    case TutorialHighlight.TimeControl: b = _timePanel; break;
                    case TutorialHighlight.Repair: b = _selectionPanel != null ? _selectionPanel.RepairButtonIfShown : null; break;
                }
            }
            float pulse = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.5f));
            Follow(_frameA, a, pulse);
            Follow(_frameB, b, pulse);
            var c = HudTheme.Accent;
            _cardFrame.color = new Color(c.r, c.g, c.b, _runner.HoldsTime ? pulse : 1f);
        }

        /// <summary>건설 단계: 다음 모듈의 탭(다른 분류를 보고 있으면) → 버튼. 이미 고른 상태면 강조 없음.</summary>
        private RectTransform BuildTarget(TutorialStep step)
        {
            var next = _runner.NextModule(step);
            if (next == null || _buildMenu == null || _build == null || _build.Selected == next)
                return null;
            return _build.Category != next.Category ? _buildMenu.FindTab(next.Category) : _buildMenu.FindButton(next);
        }

        private void Follow(RectTransform frame, RectTransform target, float alpha)
        {
            bool on = target != null && target.gameObject.activeInHierarchy;
            if (frame.gameObject.activeSelf != on)
                frame.gameObject.SetActive(on);
            if (!on)
                return;
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var parent = (RectTransform)frame.parent;
            Vector2 min = parent.InverseTransformPoint(corners[0]);
            Vector2 max = parent.InverseTransformPoint(corners[2]);
            const float pad = 6f;
            frame.localPosition = (min + max) * 0.5f;
            frame.sizeDelta = new Vector2(max.x - min.x + pad * 2f, max.y - min.y + pad * 2f);
            frame.SetAsLastSibling();
            var img = frame.GetComponent<Image>();
            var c = HudTheme.ButtonWarning;
            img.color = new Color(c.r, c.g, c.b, alpha);
        }

        private void HideHighlights()
        {
            if (_frameA != null && _frameA.gameObject.activeSelf)
                _frameA.gameObject.SetActive(false);
            if (_frameB != null && _frameB.gameObject.activeSelf)
                _frameB.gameObject.SetActive(false);
        }

        private void TrackCamera()
        {
            var rig = _camera != null ? _camera.Rig : null;
            if (rig == null)
                return;
            if (!_cameraSampled)
            {
                _cameraSampled = true;
                Sample(rig);
                return;
            }
            _rotated += Mathf.Abs(Mathf.DeltaAngle(_lastYaw, rig.Yaw)) + Mathf.Abs(rig.Pitch - _lastPitch);
            _zoomed += Mathf.Abs(rig.Distance - _lastDistance);
            _moved += (rig.Focus - _lastFocus).magnitude;
            Sample(rig);
            var data = _runner.Data;
            var use = TutorialRunner.CameraUse.None;
            if (_rotated >= data.CameraRotateDegrees) use |= TutorialRunner.CameraUse.Rotate;
            if (_zoomed >= data.CameraZoomDistance) use |= TutorialRunner.CameraUse.Zoom;
            if (_moved >= data.CameraMoveDistance) use |= TutorialRunner.CameraUse.Move;
            if (use != TutorialRunner.CameraUse.None)
                _runner.ReportCamera(use);
        }

        private void Sample(OrbitCameraRig rig)
        {
            _lastYaw = rig.Yaw;
            _lastPitch = rig.Pitch;
            _lastDistance = rig.Distance;
            _lastFocus = rig.Focus;
        }

        // ---------------- 내용 ----------------

        private void Refresh()
        {
            if (_finished)
                return;
            var step = _runner.Current;
            if (step == null)
                return;
            _header.SetText($"<color={HudTheme.AccentHex}>튜토리얼 {_runner.StepIndex + 1}/{_runner.StepCount}</color>");
            _title.SetText(step.Title);
            _body.SetText(step.Body);
            _checklist.SetText(Checklist(step));
            _status.SetText(Status(step));
        }

        private static string Check(bool done, string text) =>
            done ? $"<color={HudTheme.GreenHex}>■ {text}</color>" : $"<color={HudText.Muted}>□</color> {text}";

        private string Checklist(TutorialStep step)
        {
            _lines.Clear();
            switch (step.Goal)
            {
                case TutorialGoal.Camera:
                    var done = _runner.CameraDone;
                    _lines.Add(Check((done & TutorialRunner.CameraUse.Rotate) != 0, "회전: 마우스 가운데 버튼 드래그"));
                    _lines.Add(Check((done & TutorialRunner.CameraUse.Zoom) != 0, "확대/축소: 마우스 휠"));
                    _lines.Add(Check((done & TutorialRunner.CameraUse.Move) != 0, $"이동: {KeyBindings.Label(GameAction.CameraForward)} {KeyBindings.Label(GameAction.CameraLeft)} " +
                                                                                     $"{KeyBindings.Label(GameAction.CameraBack)} {KeyBindings.Label(GameAction.CameraRight)} 또는 Shift+가운데 드래그"));
                    break;
                case TutorialGoal.Build:
                    foreach (var m in step.Modules)
                    {
                        if (m == null)
                            continue;
                        int n = _runner.CountActive(m);
                        _lines.Add(Check(n > 0, $"{m.DisplayName} 건설 ({Mathf.Min(n, 1)}/1)"));
                    }
                    break;
                case TutorialGoal.SurviveNight:
                    _lines.Add(Check(_runner.SpeedUsed, $"배속 {_runner.Data.RequiredSpeed:0}x 이상: " +
                                                         $"{KeyBindings.Label(GameAction.Speed2)} 또는 오른쪽 위 버튼"));
                    var day = _sim.DayNight;
                    float t = _sim.ElapsedSeconds;
                    int left = Mathf.CeilToInt(day.TimeUntilPhaseChange(t));
                    string clock = $"{left / 60}:{left % 60:00}";
                    _lines.Add(Check(_runner.NightsPassed > 0, day.IsDay(t) ? $"밤 한 번 넘기기 (밤까지 {clock})" : $"밤 한 번 넘기기 (아침까지 {clock})"));
                    break;
                case TutorialGoal.Repair:
                    if (!_runner.MeteorFired)
                    {
                        int eta = Mathf.Max(0, Mathf.CeilToInt(_runner.Data.MeteorDelay - _runner.StepElapsed));
                        _lines.Add($"{HudTheme.Icon("warning")} <color={HudText.Orange}>운석 접근 중 · {eta}초</color>");
                    }
                    else
                    {
                        int damaged = 0, repairing = 0;
                        foreach (var info in _sim.Damage.DamagedModules)
                        {
                            damaged++;
                            if (info.IsRepairing || info.IsQueued)
                                repairing++;
                        }
                        _lines.Add(Check(damaged == repairing, $"파손 모듈 수리 시작 ({repairing}/{damaged}): 클릭 후 [수리] 또는 {KeyBindings.Label(GameAction.Repair)}"));
                        _lines.Add(Check(damaged == 0, "수리 완료까지 기다리기"));
                    }
                    break;
            }
            return string.Join("\n", _lines);
        }

        private string Status(TutorialStep step)
        {
            _sb.Clear();
            if (_flashUntil > Time.unscaledTime)
                _sb.Append($"<color={HudTheme.GreenHex}><b>{_flashText}</b></color>\n");
            if (_runner.HoldsTime)
                _sb.Append($"<color={HudText.Orange}><b>밤이 오기 전에 배터리까지 지어야 합니다 · 그때까지 시간이 멈춰 있습니다</b></color>\n");
            if (_supplyUntil > Time.unscaledTime)
                _sb.Append($"<color={HudTheme.GreenHex}>{_supplyText}</color>\n");
            else
            {
                var cost = _runner.NextCost(step);
                if (cost != null && !_sim.Resources.CanAfford(cost))
                {
                    int eta = Mathf.Max(0, Mathf.CeilToInt(_runner.Data.SupplyDelay - _runner.ShortageElapsed));
                    _sb.Append($"<color={HudText.Yellow}>자원이 모자랍니다 · {eta}초 뒤 보급선이 부족분을 가져옵니다</color>\n");
                }
            }
            if (step.Goal == TutorialGoal.Build && _build != null)
            {
                var next = _runner.NextModule(step);
                if (next != null && _build.Selected == next)
                    _sb.Append($"<color={HudText.Muted}>정거장에 붙여 놓으세요 · {KeyBindings.Label(GameAction.Rotate)} 회전 · 우클릭 취소</color>\n");
            }
            if (_sb.Length > 0 && _sb[_sb.Length - 1] == '\n')
                _sb.Length--;
            _status.gameObject.SetActive(_sb.Length > 0);
            return _sb.ToString();
        }

        // ---------------- 이벤트 ----------------

        private void HandleStepCompleted(int index)
        {
            _flashText = $"★ 완료: {_runner.Data.Steps[index].Title}";
            _flashUntil = Time.unscaledTime + _completedFlashSeconds;
            AudioService.TryPlay(l => l.EventPositive);
            _tween.Play(restart: true);
        }

        private void HandleSupplied(IReadOnlyList<ResourceAmount> amounts)
        {
            _sb.Clear();
            _sb.Append("보급선 도착 ·");
            foreach (var a in amounts)
                _sb.Append($" {HudTheme.Icon(a.Type)}{HudText.ResourceName(a.Type)} +{a.Amount:0}");
            _supplyText = _sb.ToString();
            _supplyUntil = Time.unscaledTime + _supplyMessageSeconds;
        }

        private void HandleSkipClicked()
        {
            if (_skipConfirmUntil < 0f)
            {
                _skipConfirmUntil = Time.unscaledTime + _skipConfirmSeconds;
                _skipLabel.SetText("한 번 더 눌러 건너뛰기");
                return;
            }
            _runner.Skip();
        }

        private void HandleFinished(bool skipped)
        {
            GameSettings.TutorialPending = false;
            _finished = true;
            HideHighlights();
            if (skipped)
            {
                _tween.Hide();
                return;
            }
            AudioService.TryPlay(l => l.EventPositive);
            _header.SetText($"<color={HudTheme.AccentHex}>튜토리얼 완료</color>");
            _title.SetText("이제 정거장은 당신의 것입니다");
            _body.SetText("지금부터 운석·태양 폭풍 같은 무작위 이벤트가 찾아옵니다.\n" +
                          "거주자를 늘려 정거장 등급을 올리고, 연구(" + KeyBindings.Label(GameAction.Research) + ")로 새 기술을 여세요.\n" +
                          $"<color={HudText.Muted}>목표: 대형 정거장 (오른쪽 위 등급 표시)</color>");
            _checklist.gameObject.SetActive(false);
            _status.gameObject.SetActive(false);
            _skipButton.gameObject.SetActive(false);
            _closeButton.gameObject.SetActive(true);
            _tween.Play(restart: true);
        }

        private void HandleCloseClicked()
        {
            _tween.Hide();
        }
    }
}
