using SpaceStation.Audio;
using SpaceStation.Core;
using SpaceStation.Data;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 항성 발전망 미니게임 "벌 몰래 꿀 먹기" (사용자 제안 — 선생님 몰래 춤추기처럼 곰 주민이 되어 벌 몰래 꿀을 먹음, 사용자 결정: 옆에서 보는 무대 ·
    /// 한 마리가 돌아봄 · 목숨 셋 · 규칙 = <see cref="HoneySneak"/>): 꿀 샘 옆 그루터기 위 꿀단지를 보고 F = 시작 → 화면이 옆에서 본 무대로 고정되고
    /// 그루터기에 곰 (나)이 꿀단지를 안고 앉음, 꿀 샘 너머 경비 꿀벌은 벌집 쪽으로 등을 돌리고 있음. F 꾹 = 꿀 퍼먹기 (숟갈마다 한 번), 떼면 시치미.
    /// 꿀벌이 돌아보기 전엔 날갯짓이 빨라지고 머리 위 "!"가 깜박임 (색 대신 깜박임 · 글), 돌아보는 척만 하는 속임수도 있음. 보고 있을 때 먹으면 콕 쏘이고 목숨 하나,
    /// 셋째나 꿀단지 (30숟갈)를 비우면 끝 — 먹은 숟갈 · 최고 기록 (글자, 그루터기 옆 안내판). ESC = 그만. 시간 = 실제 시간. 값 = <see cref="StellarGrid"/>.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 게임 중 ESC를 여기서 먼저 씀
    public sealed class InteriorHoneySneak : MonoBehaviour
    {
        private enum Phase { Idle, Playing, Result }

        private const float BearScale = 1.3f, SitDrop = 0.41f, ResultSeconds = 3.6f;   // 곰 = 주민보다 조금 크게 (옆 카메라 3.7 m에서 1.15는 작고 어두웠음)
        private static readonly int CullModeId = Shader.PropertyToID("_CullMode");
        private static readonly int UnderlayColorId = Shader.PropertyToID("_UnderlayColor");
        private static readonly int UnderlayDilateId = Shader.PropertyToID("_UnderlayDilate");
        private static readonly int UnderlaySoftnessId = Shader.PropertyToID("_UnderlaySoftness");
        /// <summary>경비 꿀벌 → 머리 위 표시 (곰 쪽 위 — 하늘 앞).</summary>
        public static readonly Vector3 MarkOffset = new Vector3(-0.55f, 0.5f, 0f);

        [Tooltip("그루터기 위 꿀단지 (보고 F = 시작, 게임 중엔 곰 무릎에)")]
        [SerializeField] private Transform _pot;
        [SerializeField] private Collider _start;
        [SerializeField] private InteriorBeeFlight _bees;
        [SerializeField] private AnimalModelSet _animals;
        [SerializeField] private TMP_FontAsset _font;
        [Tooltip("옆에서 보는 카메라 (템플릿 기준 눈 자리) · 바라보는 곳")]
        [SerializeField] private Vector3 _camera;
        [SerializeField] private Vector3 _lookAt;
        [Tooltip("그루터기 윗면 가운데 (곰이 앉는 곳) · 곰이 보는 방향 (도, 템플릿 기준 — 90 = +x 꿀 샘 · 꿀벌 쪽)")]
        [SerializeField] private Vector3 _seat;
        [SerializeField] private float _seatYaw = 90f;
        [Tooltip("안내판 글자 자리 (템플릿 기준) · 바라보는 yaw")]
        [SerializeField] private Vector3 _boardPosition;
        [SerializeField] private float _boardYaw;

        private static int _best = -1;

        private Phase _phase = Phase.Idle;
        private readonly HoneySneak _rules = new HoneySneak();
        private FirstPersonController _player;
        private Vector3 _back;
        private float _backYaw, _backPitch;
        private ResidentFigure _bear;
        private InteriorHoneyBear _pose;
        private Transform _potParent;
        private Vector3 _potPos, _potScale;
        private Quaternion _potRot;
        private bool _needRelease;
        private bool _wasReady;
        private float _resultT;
        private float _eat, _innocent, _ouch;
        private TextMeshPro _mark, _bubble, _pop;
        private float _popT = 9f;
        private TextMeshPro[] _boards;
        private string _notice;
        private float _noticeTimer;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public bool Playing => _phase != Phase.Idle;
        public HoneySneak Rules => _rules;
        public static int Best => _best;

        public void Configure(Transform pot, Collider start, InteriorBeeFlight bees, AnimalModelSet animals, TMP_FontAsset font,
            Vector3 camera, Vector3 lookAt, Vector3 seat, float seatYaw, Vector3 boardPosition, float boardYaw)
        {
            _pot = pot;
            _start = start;
            _bees = bees;
            _animals = animals;
            _font = font;
            _camera = camera;
            _lookAt = lookAt;
            _seat = seat;
            _seatYaw = seatYaw;
            _boardPosition = boardPosition;
            _boardYaw = boardYaw;
        }

        private void Awake()
        {
            if (_start != null)
            {
                var use = _start.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "곰이 되어 벌 몰래 꿀 먹기";
                use.Blocked = () => _phase != Phase.Idle ? "꿀을 먹는 중이에요" : BearSpecies() == null ? "곰 주민 모델이 없어요" : null;
                use.Completed = Begin;
            }
            if (_pot != null)
            {
                _potParent = _pot.parent;
                _potPos = _pot.localPosition;
                _potRot = _pot.localRotation;
                _potScale = _pot.localScale;
            }
            BuildBoards();
            UpdateBoards();
        }

        private void OnDisable()
        {
            if (_phase != Phase.Idle)
                Restore();
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        private AnimalSpecies BearSpecies()
        {
            if (_animals == null)
                return null;
            foreach (var s in _animals.Species)
                if (s != null && s.Id == "bear" && s.Model != null)
                    return s;
            return null;
        }

        // ---------------- 시작 · 끝 ----------------

        private void Begin()
        {
            var species = BearSpecies();
            if (_phase != Phase.Idle || species == null)
                return;
            _player = FindFirstObjectByType<FirstPersonController>();
            if (_player == null)
                return;
            _back = transform.InverseTransformPoint(_player.transform.position);
            _backYaw = _player.transform.eulerAngles.y - transform.eulerAngles.y;
            _backPitch = _player.Pitch;
            // 곰 (나): 그루터기에 앉음 (원점 = 앉는 면 − 골반 아래), 꿀 샘 · 꿀벌 쪽을 봄
            var rot = transform.rotation * Quaternion.Euler(0f, _seatYaw, 0f);
            var fur = species.FurColors.Length > 0 ? species.FurColors[0] : Color.white;
            _bear = ResidentFigure.Create(_animals, species, fur, transform, transform.TransformPoint(_seat) - transform.up * SitDrop, rot, ResidentPose.Sit, BearScale, SitDrop);
            _bear.name = "HoneyBear";
            _bear.Configure("", 0.8f, null, 0f);   // 플레이어 (= 곰 자신)를 쳐다보지 않음
            if (_bear.Motion != null)
                _bear.Motion.Hold("None", 0f);      // 주민 평소 동작 (두리번 · 기지개) 멈춤 — 게임 중 고개가 카메라 쪽으로 돌았음
            foreach (var c in _bear.GetComponentsInChildren<Collider>(true))
                c.enabled = false;
            _pose = _bear.gameObject.AddComponent<InteriorHoneyBear>();
            _pose.Init(_pot, BearScale);
            if (_bees != null)
                _bees.GuardHeld = true;
            // 화면 = 옆에서 본 무대 (마우스 둘러보기 · 걷기 멈춤)
            var eye = transform.TransformPoint(_camera);
            var look = transform.TransformPoint(_lookAt) - eye;
            float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            _player.RideAt(transform, eye, 0f, yaw, pitch);
            _player.Frozen = true;
            if (_start != null)
                _start.enabled = false;
            _rules.Start(System.Environment.TickCount);
            _needRelease = true;
            _wasReady = true;
            _eat = _innocent = _ouch = 0f;
            _phase = Phase.Playing;
            EnsureMarks();
            Notice("준비…  꿀벌이 등을 돌렸을 때 F 꾹!", HoneySneak.ReadySeconds);
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void EndGame()
        {
            _rules.Stop();
            bool record = _best < 0 || _rules.Spoons > _best;
            if (record)
                _best = _rules.Spoons;
            string head = _rules.Emptied ? $"꿀단지를 다 비웠어요! {_rules.Spoons}숟갈" : $"들켜 버렸어요… 꿀 {_rules.Spoons}숟갈";
            Notice(head + (record ? "  ·  최고 기록!" : $"  ·  최고 {_best}숟갈"), ResultSeconds + 0.5f);
            _phase = Phase.Result;
            _resultT = 0f;
            if (_pose != null)
                _pose.Weight = 0f;
            if (_bear != null && _bear.Motion != null)
            {
                _bear.Motion.Hold(null, 0f);
                _bear.Motion.Celebrate();
            }
            UpdateBoards();
            AudioService.TryPlay(l => record ? l.GradeUp : l.UiClose);
        }

        /// <summary>곰 · 꿀단지 · 경비 꿀벌 · 플레이어를 처음대로.</summary>
        private void Restore()
        {
            _phase = Phase.Idle;
            _rules.Stop();
            if (_pot != null)
            {
                _pot.SetParent(_potParent, false);
                _pot.localPosition = _potPos;
                _pot.localRotation = _potRot;
                _pot.localScale = _potScale;
            }
            if (_bear != null)
                Destroy(_bear.gameObject);
            _bear = null;
            _pose = null;
            if (_bees != null)
            {
                _bees.GuardHeld = false;
                _bees.GuardBuzz = 1f;
            }
            if (_start != null)
                _start.enabled = true;
            foreach (var t in new[] { _mark, _bubble, _pop })
                if (t != null)
                    t.gameObject.SetActive(false);
            if (_player != null)
            {
                _player.Frozen = false;
                _player.RideAt(transform, transform.TransformPoint(_back), FirstPersonController.EyeHeight, transform.eulerAngles.y + _backYaw, _backPitch);
                _player.Dismount();
            }
        }

        // ---------------- 진행 ----------------

        private void Update()
        {
            if (_phase == Phase.Idle)
                return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_noticeTimer > 0f)
                _noticeTimer -= dt;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                InputGate.ConsumeEscape();   // 내부 나가기보다 먼저
                Restore();
                AudioService.TryPlay(l => l.UiClose);
                ShowHint(null);
                return;
            }
            if (_player != null)
                _player.Frozen = true;   // 패드를 열고 닫아도 화면 고정
            if (_phase == Phase.Playing)
                Play(dt);
            else
            {
                _resultT += dt;
                _eat = Mathf.MoveTowards(_eat, 0f, dt * 6f);
                if (_resultT >= ResultSeconds)
                {
                    Restore();
                    ShowHint(null);
                    return;
                }
            }
            AnimateGuard(dt);
            AnimateBear(dt);
            UpdateHint();
        }

        private void Play(float dt)
        {
            bool held = KeyBindings.IsPressed(GameAction.Interact);
            if (!held)
                _needRelease = false;   // 시작한 F를 떼야 먹기 시작
            bool eating = held && !_needRelease && !_rules.Ready && _rules.State != HoneySneak.Guard.Sting;
            var ev = _rules.Step(dt, eating);
            if (_wasReady && !_rules.Ready)
                Notice("시작!  F 꾹 = 꿀 퍼먹기 · 떼면 시치미", 2f);
            _wasReady = _rules.Ready;
            _eat = Mathf.MoveTowards(_eat, eating ? 1f : 0f, dt / 0.12f);
            switch (ev)
            {
                case HoneySneak.Event.Spoon:
                    AudioService.TryPlay(l => l.UiHover);
                    break;
                case HoneySneak.Event.Caught:
                    _needRelease = true;   // 손을 뗐다 다시 눌러야 먹음 (누른 채면 쏘인 뒤 돌아서는 동안 또 들켜 목숨이 둘 줄었음)
                    Notice(_rules.LivesLeft > 0 ? $"콕! 들켰어요  ·  목숨 {_rules.LivesLeft}" : "콕! 또 들켰어요", 2f);
                    _popT = 0f;
                    AudioService.TryPlay(l => l.UiError);
                    break;
                case HoneySneak.Event.Finished:
                    EndGame();
                    break;
            }
        }

        // ---------------- 경비 꿀벌 ----------------

        private void AnimateGuard(float dt)
        {
            if (_bees == null || _bees.Guard == null)
                return;
            var guard = _bees.Guard;
            float t = Time.unscaledTime;
            var home = StellarGrid.GuardBee;
            var state = _phase == Phase.Playing ? _rules.State : HoneySneak.Guard.Watch;
            float turn = _phase == Phase.Playing ? _rules.Turn : 1f;
            var dir = new Vector3(Mathf.Cos(Mathf.PI * turn), 0f, -Mathf.Sin(Mathf.PI * turn));   // 등 (+x 벌집) → 카메라 쪽 → 곰 (−x)
            var pos = home + new Vector3(0.05f * Mathf.Sin(1.8f * t), 0.04f * Mathf.Sin(2.9f * t), 0.03f * Mathf.Sin(3.6f * t));
            bool warn = state == HoneySneak.Guard.Warn;
            _bees.GuardBuzz = warn ? 1.8f : 1f;
            if (warn)
                pos += new Vector3(0.012f * Mathf.Sin(53f * t), 0.04f + 0.012f * Mathf.Sin(71f * t), 0f);   // 부르르 떨며 살짝 떠오름
            if (state == HoneySneak.Guard.Sting && _pose != null)
            {
                // 곰 코로 날아가 콕 → 제자리로 (곰을 본 채)
                var nose = transform.InverseTransformPoint(_pose.Nose);
                var stop = nose + (home - nose).normalized * (0.11f * StellarGrid.GuardScale);
                float p = _rules.StateProgress;
                if (p < 0.25f)
                {
                    float k = Smooth(p / 0.25f);
                    pos = Vector3.Lerp(home, stop, k) + Vector3.up * (0.25f * Mathf.Sin(k * Mathf.PI));
                    dir = stop - home;
                }
                else if (p < 0.45f)
                {
                    float jab = Mathf.Max(0f, Mathf.Sin((p - 0.25f) / 0.2f * Mathf.PI * 3f));
                    pos = stop + (nose - stop).normalized * (0.05f * jab);
                    dir = nose - stop;
                }
                else
                {
                    float k = Smooth((p - 0.45f) / 0.55f);
                    pos = Vector3.Lerp(stop, home, k) + Vector3.up * (0.2f * Mathf.Sin(k * Mathf.PI));
                    dir = nose - home;
                }
                _bees.GuardBuzz = 1.8f;
            }
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f)
                dir = Vector3.left;
            guard.localPosition = pos;
            guard.localRotation = Quaternion.Slerp(guard.localRotation, _bees.FacingOf(dir.normalized), 1f - Mathf.Exp(-dt * 30f));
            // "!" (돌아보기 전 깜박임 · 속임수도) · "…" (보는 중) · "!!" (쏘러 감) — 흰 글자 + 어두운 그림자 테, 곰 쪽 위 (하늘 앞):
            //  꿀벌 바로 위는 뒤 꿀 언덕 꼭대기의 꿀 방울 장식이 "!"처럼 생겨 묻혔음
            if (_mark != null)
            {
                string text = null;
                float alpha = 1f, size = 1f;
                if (_phase == Phase.Playing)
                {
                    switch (state)
                    {
                        case HoneySneak.Guard.Warn:
                        case HoneySneak.Guard.Feint:
                            text = "!";
                            alpha = Mathf.Repeat(t * 7f, 1f) < 0.55f ? 1f : 0.3f;
                            size = 1f + 0.18f * Mathf.Sin(t * 22f);
                            break;
                        case HoneySneak.Guard.Turn:
                        case HoneySneak.Guard.Watch:
                            text = "…";
                            size = 0.5f;
                            break;
                        case HoneySneak.Guard.Sting:
                            text = "!!";
                            size = 0.75f;
                            break;
                    }
                }
                Show(_mark, text, transform.TransformPoint(pos + MarkOffset), alpha, size);
            }
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // ---------------- 곰 ----------------

        private void AnimateBear(float dt)
        {
            if (_pose == null)
                return;
            bool playing = _phase == Phase.Playing;
            var state = _rules.State;
            bool sting = playing && state == HoneySneak.Guard.Sting;
            bool alert = playing && !_rules.Ready && state != HoneySneak.Guard.Back && !sting;
            _innocent = Mathf.MoveTowards(_innocent, alert && _eat < 0.5f ? 1f : 0f, dt / 0.25f);
            float ouchWant = sting ? Mathf.Clamp01((_rules.StateProgress - 0.3f) / 0.1f) * (1f - Smooth((_rules.StateProgress - 0.7f) / 0.3f)) : 0f;
            _ouch = Mathf.MoveTowards(_ouch, ouchWant, dt / 0.12f);
            _pose.Eat = _eat;
            _pose.Scoop = _rules.SpoonProgress;
            _pose.Innocent = _innocent;
            _pose.Ouch = _ouch;
            // 말풍선: 아야! (쏘임) · 흠흠~ (보고 있을 때 시치미) · 냠냠 (먹는 중)
            string say = null;
            if (_phase == Phase.Result)
                say = _rules.Emptied ? "배불러!" : null;
            else if (sting && _rules.StateProgress > 0.5f && _rules.StateProgress < 0.9f)
                say = "아야!";   // "콕!" (0.27 ~ 0.5) 다음 — 같이 띄우면 겹쳤음
            else if (alert && _innocent > 0.5f && _rules.Seeing)
                say = "흠흠~";
            else if (_eat > 0.5f)
                say = "냠냠";
            if (_bubble != null)
                Show(_bubble, say, _pose.Top, say == "냠냠" ? 0.85f : 1f, 1f);
            // 콕! (코에서 튀어 오름)
            _popT += dt;
            if (_pop != null)
            {
                bool on = sting && _rules.StateProgress > 0.27f && _rules.StateProgress < 0.5f;
                Show(_pop, on ? "콕!" : null, _pose.Nose + transform.up * (0.14f + 0.12f * Smooth((_rules.StateProgress - 0.27f) / 0.23f)) - transform.forward * 0.2f, 1f, 1.1f);
            }
        }

        // ---------------- 글자 ----------------

        private void EnsureMarks()
        {
            if (_font == null || _mark != null)
                return;
            _mark = WorldText("GuardMark", 6.5f, Color.white, true);    // 글자 크기 1 ≈ 0.05 m ("!" 기준) — 경고는 놓치면 안 되니 크게 (2.2 · 4.2는 작고 뒤 꿀 방울 장식과 비슷했음)
            _bubble = WorldText("BearBubble", 1.1f, new Color(1f, 0.98f, 0.92f), false);
            _pop = WorldText("StingPop", 1.4f, new Color(1f, 1f, 1f), true);
        }

        private TextMeshPro WorldText(string name, float size, Color color, bool bold)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var text = go.AddComponent<TextMeshPro>();
            text.font = _font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.rectTransform.sizeDelta = new Vector2(1.6f, 0.5f);
            text.outlineWidth = 0.25f;               // 어두운 테두리 + 그림자 테 (밝은 하늘 · 꿀 앞에서도 읽히게 — 색 대신 밝기 대비)
            text.outlineColor = new Color32(30, 22, 10, 255);
            var mat = text.fontMaterial;
            if (mat.HasProperty(UnderlayColorId))
            {
                mat.EnableKeyword("UNDERLAY_ON");
                mat.SetColor(UnderlayColorId, new Color(0.06f, 0.04f, 0.02f, 0.85f));
                mat.SetFloat(UnderlayDilateId, 1f);
                mat.SetFloat(UnderlaySoftnessId, 0.7f);
            }
            text.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return text;
        }

        private void Show(TextMeshPro text, string value, Vector3 at, float alpha, float scale)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (text.gameObject.activeSelf)
                    text.gameObject.SetActive(false);
                return;
            }
            if (!text.gameObject.activeSelf)
                text.gameObject.SetActive(true);
            if (text.text != value)
                text.SetText(value);
            var cam = Camera.main;
            text.transform.position = at;
            if (cam != null)
                text.transform.rotation = cam.transform.rotation;   // 카메라를 봄
            text.transform.localScale = Vector3.one * scale;
            text.alpha = alpha;
        }

        private void BuildBoards()
        {
            if (_font == null)
                return;
            _boards = new TextMeshPro[2];
            for (int k = 0; k < 2; k++)   // 앞뒤 두 장 (한 장이면 뒤에서 거울 글씨)
            {
                var go = new GameObject("HoneyBoard" + k);
                go.transform.SetParent(transform, false);
                go.transform.localRotation = Quaternion.Euler(0f, _boardYaw + 180f * k, 0f);
                go.transform.localPosition = _boardPosition + go.transform.localRotation * new Vector3(0f, 0f, -0.04f);
                var text = go.AddComponent<TextMeshPro>();
                text.font = _font;
                text.enableAutoSizing = true;
                text.fontSizeMin = 0.3f;
                text.fontSizeMax = 1f;     // 판 0.95 × 0.5 m에 맞춰 줄임 (글자 크기 1 ≈ 0.1 m)
                text.fontSize = 1f;
                text.color = new Color(0.36f, 0.24f, 0.08f);
                text.alignment = TextAlignmentOptions.Center;
                text.rectTransform.sizeDelta = new Vector2(0.86f, 0.42f);
                text.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (text.fontMaterial.HasProperty(CullModeId))   // 앞뒤 두 장: 뒷면 안 그림 (뒤집힌 글자가 겹쳐 보였음)
                    text.fontMaterial.SetFloat(CullModeId, (float)UnityEngine.Rendering.CullMode.Back);
                _boards[k] = text;
            }
        }

        private void UpdateBoards()
        {
            if (_boards == null)
                return;
            string best = _best < 0 ? "최고 기록 없음" : $"최고 {_best}숟갈";
            foreach (var b in _boards)
                b.SetText($"벌 몰래 꿀 먹기\n<size=60%>그루터기 꿀단지를 보고 F · {best}</size>");
        }

        private void Notice(string text, float seconds)
        {
            _notice = text;
            _noticeTimer = seconds;
        }

        private void UpdateHint()
        {
            string text = null;
            if (_phase == Phase.Playing)
            {
                bool warn = _rules.State == HoneySneak.Guard.Warn || _rules.State == HoneySneak.Guard.Feint;
                string alert = warn ? (Mathf.Repeat(Time.unscaledTime * 7f, 1f) < 0.55f ? "<b>꿀벌이 부르르!</b>  ·  " : "<alpha=#55><b>꿀벌이 부르르!</b><alpha=#FF>  ·  ") : "";
                text = $"{alert}꿀 {_rules.Spoons}숟갈  ·  꿀단지 {HoneySneak.PotSpoons - _rules.Spoons}숟갈 남음  ·  목숨 {_rules.LivesLeft} / {HoneySneak.Lives}\n"
                     + $"<size=80%>{KeyBindings.Label(GameAction.Interact)} 꾹 = 꿀 퍼먹기 · 떼면 시치미 · 꿀벌이 부르르 떨며 ! 하면 손 떼기 · ESC 그만</size>";
            }
            if (_noticeTimer > 0f && !string.IsNullOrEmpty(_notice))
                text = text == null ? _notice : _notice + "\n<size=80%>" + text + "</size>";
            ShowHint(text);
        }

        private void ShowHint(string text)
        {
            if (text == _hintShown)
                return;
            _hintShown = text;
            if (text == null)
            {
                if (_canvas != null)
                    _canvas.gameObject.SetActive(false);
                return;
            }
            EnsureCanvas();
            _canvas.gameObject.SetActive(true);
            _hint.SetText(text);
        }

        private void EnsureCanvas()
        {
            if (_canvas != null)
                return;
            var go = new GameObject("HoneySneakHint", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 300;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var back = new GameObject("Back", typeof(RectTransform)).AddComponent<Image>();
            back.transform.SetParent(go.transform, false);
            Place(back.rectTransform, new Vector2(1100f, 112f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(1070f, 104f));
            if (_font != null)
                _hint.font = _font;
            _hint.fontSize = 26f;
            _hint.enableAutoSizing = true;
            _hint.fontSizeMin = 15f;
            _hint.fontSizeMax = 26f;
            _hint.color = new Color(0.92f, 0.96f, 1f);
            _hint.alignment = TextAlignmentOptions.Center;
            _hint.raycastTarget = false;
        }

        private static void Place(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 92f);
            rt.sizeDelta = size;
        }
    }
}
