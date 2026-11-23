using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// AI 중추 미니게임 "시냅스 따라 켜기" (사용자 2026-11-16 미니게임 3안, 판 = <see cref="DroneMemoryGame"/> — 수리 사령부 기억 등불과 같은 규칙, 값 = <see cref="AICore"/>):
    /// 정자 시냅스 수정 부채를 보고 F = 정자 앞 서는 자리에서 수정 뇌를 봄 → 뇌 뿌리에서 빛 알이 금빛 줄기를 타고 시냅스로 날아가 차례로 켬 →
    /// 같은 순서로 시냅스를 보고 클릭 (또는 숫자 1 ~ 6 = 왼쪽부터) → 다 맞히면 하나 더 길어지고 빨라짐, 틀리면 끝 (기억한 길이 · 최고 기록).
    /// 시냅스는 색이 아니라 자리 · 숫자와 밝기로 구분: 수정 속 빛 알맹이 크기 · 밝기 (어둡게 · 겨눔 반쯤 · 켜짐 환히 + 반짝이), 틀리면 고른 시냅스는 깜박이고 맞는 시냅스는 환히.
    /// 빛 쇼 중에는 시작하지 않음. 클릭 = 다시, F · ESC = 내려옴. 실제 시간.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 선 채 누른 F · ESC를 여기서 먼저 씀
    public sealed class InteriorSynapseMemory : MonoBehaviour
    {
        private const float DimCore = 0.07f, AimCore = 0.12f, LitCore = 0.19f, CoreRest = 0.045f;
        private const float PickFlash = 0.45f, BlinkHz = 3f;
        private const float RunnerSize = 0.07f;
        private static int _best = -1;

        [Tooltip("시냅스 수정 여섯 (AICore.Synapses 차례 — 서는 자리에서 왼쪽부터)")]
        [SerializeField] private Transform[] _synapses;
        [Tooltip("시작 상자 (보이지 않음 — 보고 F)")]
        [SerializeField] private Collider _start;
        [SerializeField] private Material _glowMaterial;
        [SerializeField] private Material _sparkMaterial;
        [SerializeField] private InteriorBrainShow _show;
        [SerializeField] private TMP_FontAsset _font;

        private readonly DroneMemoryGame _game = new DroneMemoryGame(AICore.Synapses.Length, 1116);
        private FirstPersonController _player;
        private bool _standing;
        private int _mountFrame, _standFrame = -1;
        private Vector3 _standPoint;
        private Transform[] _cores;
        private float[] _flash;
        private Transform _runner;
        private ParticleSystem _sparks;
        private int _shownStep = -1, _shownLit = -1;
        private int _aim = -1;
        private bool _recorded;
        private string _notice;
        private float _noticeTimer;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public DroneMemoryGame Game => _game;
        public bool Standing => _standing;
        public bool Running => _standing && _game.Running;
        public static int Best => _best;
        /// <summary>지금 겨눈 시냅스 (없으면 −1).</summary>
        public int Aim => _aim;
        /// <summary>시냅스 k 속 빛 알맹이 지름 (밝기 — 시험용).</summary>
        public float CoreSize(int k) => _cores != null && _cores[k] != null ? _cores[k].lossyScale.x : 0f;

        public void Configure(Transform[] synapses, Collider start, Material glow, Material spark, InteriorBrainShow show, TMP_FontAsset font)
        {
            _synapses = synapses;
            _start = start;
            _glowMaterial = glow;
            _sparkMaterial = spark;
            _show = show;
            _font = font;
        }

        private void Awake()
        {
            int n = _synapses != null ? _synapses.Length : 0;
            _cores = new Transform[n];
            _flash = new float[n];
            for (int k = 0; k < n; k++)
            {
                if (_synapses[k] == null)
                    continue;
                _cores[k] = GlowBall("SynapseCore", _synapses[k]);
                _cores[k].localPosition = Vector3.zero;
            }
            _runner = GlowBall("SynapseRunner", transform);
            _runner.gameObject.SetActive(false);
            if (_start != null)
            {
                var use = _start.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "시냅스 따라 켜기" + (_best < 0 ? "" : $"  <size=80%>(최고 기록 {_best}개)</size>");
                use.Blocked = () => _show != null && _show.Showing ? "수정 뇌 빛 쇼 중" : null;
                use.Completed = Mount;
            }
            ApplyCores();
        }

        private Transform GlowBall(string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            var rend = go.GetComponent<MeshRenderer>();
            rend.sharedMaterial = _glowMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        private void OnDisable()
        {
            Release();
            _game.Stop();
            if (_runner != null)
                _runner.gameObject.SetActive(false);
            ApplyCores();
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_standing && _player != null && !_player.Riding)
                ClearStand();   // 선 채 패드로 빠른 이동 (Teleport가 탈것을 지움)
            if (_noticeTimer > 0f)
                _noticeTimer -= dt;
            _aim = _standing ? AimedSynapse() : -1;
            if (_standing)
                Controls();
            var before = _game.Current;
            _game.Step(dt);
            if (_game.Current != before)
                Entered(_game.Current);
            ShowStep();
            if (_game.Current == DroneMemoryGame.Phase.Done && !_recorded)
                Record();
            for (int k = 0; k < _flash.Length; k++)
                _flash[k] = Mathf.Max(0f, _flash[k] - dt / PickFlash);
            MoveRunner();
            ApplyCores();
            UpdateHint();
        }

        // ---------------- 서기 ----------------

        private void Mount()
        {
            if (_standing || _player == null || Time.frameCount == _standFrame)
                return; // 내려오기와 같은 프레임의 F (InteriorMode가 뒤에서 다시 부름)
            if (_show != null && _show.Showing)
                return;
            _standPoint = _player.transform.position;
            _player.RideAt(transform, transform.TransformPoint(AICore.SynapseStand), FirstPersonController.EyeHeight, AICore.SynapseYaw + transform.eulerAngles.y, AICore.SynapsePitch);
            _standing = true;
            _mountFrame = Time.frameCount;
            if (_start != null)
                _start.enabled = false;
            Begin();
            AudioService.TryPlay(l => l.GameStart);
        }

        private void Begin()
        {
            _game.Begin();
            _recorded = false;
            _shownStep = _shownLit = -1;
            _runner.gameObject.SetActive(false);
            InteriorFireflyShow.Burst(EnsureSparks(), transform.TransformPoint(AICore.SynapseRoot), 14, 0.8f);
        }

        private void Controls()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                InputGate.ConsumeEscape();   // 내부 나가기보다 먼저
                StepDown();
                return;
            }
            if (Time.frameCount != _mountFrame && KeyBindings.WasPressed(GameAction.Interact))
            {
                StepDown();
                return;
            }
            int pick = -1;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                if (_game.Current == DroneMemoryGame.Phase.Done)
                {
                    Begin();
                    AudioService.TryPlay(l => l.GameStart);
                    return;
                }
                pick = _aim;
            }
            if (keyboard != null)
            {
                for (int k = 0; k < _flash.Length && k < 9; k++)
                {
                    if (keyboard[Key.Digit1 + k].wasPressedThisFrame || keyboard[Key.Numpad1 + k].wasPressedThisFrame)
                        pick = k;
                }
            }
            if (pick >= 0)
                Pick(pick);
        }

        private void StepDown()
        {
            _standFrame = Time.frameCount;
            Release();
            _game.Stop();
            _runner.gameObject.SetActive(false);
            AudioService.TryPlay(l => l.RideLeave);
        }

        private void Release()
        {
            if (!_standing)
                return;
            ClearStand();
            if (_player != null)
            {
                _player.RideAt(transform, _standPoint, FirstPersonController.EyeHeight, null, 0f);
                _player.Dismount();
            }
        }

        private void ClearStand()
        {
            _standing = false;
            if (_start != null)
                _start.enabled = true;
        }

        /// <summary>시선과 가장 가까운 시냅스 (SynapseAimDegrees 안, 없으면 −1).</summary>
        private int AimedSynapse()
        {
            var cam = Camera.main;
            if (cam == null || _synapses == null)
                return -1;
            int best = -1;
            float bestAngle = AICore.SynapseAimDegrees;
            for (int k = 0; k < _synapses.Length; k++)
            {
                if (_synapses[k] == null)
                    continue;
                float a = Vector3.Angle(cam.transform.forward, _synapses[k].position - cam.transform.position);
                if (a < bestAngle)
                {
                    bestAngle = a;
                    best = k;
                }
            }
            return best;
        }

        // ---------------- 판 ----------------

        private void Pick(int synapse)
        {
            var r = _game.Pick(synapse);
            if (r == DroneMemoryGame.Result.Ignored)
                return;
            _flash[synapse] = 1f;
            if (_synapses[synapse] != null)
                InteriorFireflyShow.Burst(EnsureSparks(), _synapses[synapse].position, r == DroneMemoryGame.Result.Wrong ? 6 : 16, 0.8f);
            switch (r)
            {
                case DroneMemoryGame.Result.Correct:
                    AudioService.TryPlayScore(_game.Entered);   // 이번 순서에서 맞힌 수 (틀리면 판이 끝나므로 따로 세지 않음)
                    break;
                case DroneMemoryGame.Result.Cleared:
                    Notice($"맞았어요!  다음은 {_game.Length + 1}개", DroneMemoryGame.ClearedTime);
                    AudioService.TryPlayScore(_game.Entered);
                    break;
                case DroneMemoryGame.Result.Wrong:
                    AudioService.TryPlay(l => l.GameMiss);
                    break;
            }
        }

        private void Entered(DroneMemoryGame.Phase phase)
        {
            if (phase == DroneMemoryGame.Phase.Show)
                _shownStep = _shownLit = -1;
            if (phase == DroneMemoryGame.Phase.Input || phase == DroneMemoryGame.Phase.Done)
                _runner.gameObject.SetActive(false);
        }

        /// <summary>보여 주기: 차례마다 빛 알이 뇌 뿌리에서 줄기를 타고 그 시냅스로 날아가고, 닿으면 시냅스가 켜짐.</summary>
        private void ShowStep()
        {
            int i = _game.ShowIndex;
            if (i >= 0 && i != _shownStep)
            {
                _shownStep = i;
                _runner.gameObject.SetActive(true);
            }
            int lit = _game.ShowLit;
            if (lit >= 0 && lit != _shownLit)
            {
                if (_synapses[lit] != null)
                    InteriorFireflyShow.Burst(EnsureSparks(), _synapses[lit].position, 18, 0.9f);
                AudioService.TryPlay(l => l.UiTab);
            }
            _shownLit = lit;
        }

        /// <summary>빛 알: 뿌리 → 시냅스 (줄기처럼 위로 휜 길), 닿아 켜지는 동안은 숨음.</summary>
        private void MoveRunner()
        {
            if (_runner == null || !_runner.gameObject.activeSelf)
                return;
            int i = _game.ShowIndex;
            if (i < 0 || _game.ShowLit >= 0)
            {
                _runner.gameObject.SetActive(_game.ShowLit < 0 && i >= 0);
                return;
            }
            var to = AICore.Synapses[_game.Sequence[i]];
            var from = AICore.SynapseRoot;
            float t = Mathf.SmoothStep(0f, 1f, _game.ShowFly);
            var mid = Vector3.Lerp(from, to, 0.55f) + new Vector3(0f, 0.12f, 0.06f);
            var p = Vector3.Lerp(Vector3.Lerp(from, mid, t), Vector3.Lerp(mid, to, t), t);
            _runner.localPosition = p;
            _runner.localScale = Vector3.one * RunnerSize;
            var ps = EnsureSparks();
            if (ps != null && Random.value < 0.6f)
                ps.Emit(new ParticleSystem.EmitParams { position = _runner.position, velocity = Random.insideUnitSphere * 0.1f, startLifetime = 0.4f, startSize = 0.05f, startColor = Color.white, applyShapeToPosition = false }, 1);
        }

        private void Record()
        {
            _recorded = true;
            bool record = _game.Score > 0 && _game.Score > _best;
            if (record)
                _best = _game.Score;
            AudioService.TryPlay(l => record ? l.GameRecord : _game.Score > 0 ? l.GameClear : l.GameFail);
        }

        private ParticleSystem EnsureSparks()
        {
            if (_sparks == null)
                _sparks = InteriorFireflyShow.CreateSparks(transform, _sparkMaterial);
            return _sparks;
        }

        // ---------------- 시냅스 밝기 (수정 속 빛 알맹이 크기) ----------------

        private void ApplyCores()
        {
            if (_cores == null)
                return;
            bool playing = _standing && _game.Running;
            bool blink = Mathf.Repeat(Time.unscaledTime * BlinkHz, 1f) < 0.5f;
            for (int k = 0; k < _cores.Length; k++)
            {
                if (_cores[k] == null)
                    continue;
                float size = CoreRest;
                if (playing)
                {
                    size = DimCore;
                    if (_game.Current == DroneMemoryGame.Phase.Input && k == _aim)
                        size = AimCore;
                    if (_game.ShowLit == k)
                        size = LitCore;
                    if (_game.Current == DroneMemoryGame.Phase.Cleared)
                        size = Mathf.Lerp(LitCore, DimCore, _game.PhaseTime / DroneMemoryGame.ClearedTime);
                    if (_game.Current == DroneMemoryGame.Phase.Done)
                    {
                        if (k == _game.ExpectedLamp)
                            size = LitCore;                       // 맞는 시냅스 = 환히
                        else if (k == _game.WrongLamp)
                            size = blink ? AimCore : DimCore;     // 고른 시냅스 = 깜박
                    }
                    size = Mathf.Max(size, LitCore * _flash[k]);
                }
                if (_show != null && _show.Showing)
                    size = Mathf.Max(size, _show.SynapseGlow(k) * LitCore);
                var parent = _cores[k].parent;
                float s = parent != null ? parent.lossyScale.x : 1f;
                _cores[k].localScale = Vector3.one * (size / Mathf.Max(1e-4f, s));
            }
        }

        // ---------------- 안내 ----------------

        private void UpdateHint()
        {
            if (_noticeTimer > 0f)
            {
                ShowHint(_notice);
                return;
            }
            if (!_standing)
            {
                ShowHint(null);
                return;
            }
            string f = KeyBindings.Label(GameAction.Interact);
            const string legend = "<size=80%>숫자 1 ~ 6 = 왼쪽 시냅스부터</size>";
            switch (_game.Current)
            {
                case DroneMemoryGame.Phase.Ready:
                    ShowHint($"빛 알이 시냅스를 켜는 순서를 잘 보세요  ·  {_game.Length}개");
                    break;
                case DroneMemoryGame.Phase.Show:
                    ShowHint($"순서 보는 중  ·  {Mathf.Max(0, _game.ShowIndex) + 1} / {_game.Length}");
                    break;
                case DroneMemoryGame.Phase.Input:
                    ShowHint($"같은 순서로 시냅스를 보고 클릭 (또는 숫자)  ·  {_game.Entered} / {_game.Length}\n{legend}");
                    break;
                case DroneMemoryGame.Phase.Done:
                    bool record = _game.Score > 0 && _game.Score >= _best;
                    ShowHint($"틀렸어요 (환한 시냅스가 맞는 차례)  ·  {_game.Score}개까지 기억했어요  ·  " + (record ? "최고 기록!" : $"최고 {Mathf.Max(_best, 0)}개") +
                             $"\n<size=80%>클릭 = 다시 · {f} · ESC = 내려오기</size>");
                    break;
                default:
                    ShowHint(null);
                    break;
            }
        }

        private void Notice(string text, float seconds)
        {
            _notice = text;
            _noticeTimer = seconds;
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
            var go = new GameObject("SynapseMemoryHint", typeof(RectTransform));
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
            Place(back.rectTransform, new Vector2(980f, 88f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(960f, 80f));
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
            rt.anchoredPosition = new Vector2(0f, 160f);
            rt.sizeDelta = size;
        }
    }
}
