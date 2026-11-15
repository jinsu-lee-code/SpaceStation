using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 수리 사령부 미니게임 "드론 순서 기억하기" (TASKS 2026-11-13, 사용자 2026-11-15 "미니게임으로 넘어가자", 판 = <see cref="DroneMemoryGame"/>, 값 = <see cref="RepairCommand"/>):
    /// 그루터기를 보고 F = 그루터기 위에 올라섬 (고목 쪽 등불 다섯을 봄) → 반딧불 드론 한 마리가 등불 사이를 날아다니며 차례로 켬 →
    /// 같은 순서로 등불을 보고 클릭 (또는 숫자 1 ~ 5 = 왼쪽부터 별 · 달 · 하트 · 잎 · 물방울) → 다 맞히면 하나 더 길어지고 빨라짐, 틀리면 끝 (기억한 길이 · 최고 기록).
    /// 등불은 색이 아니라 자리 · 모양 (별 · 달 · 하트 · 잎 · 물방울 표)과 밝기로 구분: 올라서면 모두 어둡게, 겨눈 등불은 반쯤, 켜진 등불은 환히 + 반짝이.
    /// 틀리면 고른 등불은 깜박이고 맞는 등불은 환히 (무엇이 맞았는지). 클릭 = 다시, F · ESC = 내려옴. 실제 시간.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 올라선 채 누른 F · ESC를 여기서 먼저 씀
    public sealed class InteriorDroneMemory : MonoBehaviour
    {
        private const float DimGlow = 0.18f, AimGlow = 0.55f, LitGlow = 2.2f;
        private const float PickFlash = 0.45f, BlinkHz = 3f;
        private const float PerchHop = 0.08f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static int _best = -1;

        [Tooltip("기억 등불 빛 구슬 다섯 (RepairCommand.MemoryLamps 차례 — 그루터기에서 왼쪽부터)")]
        [SerializeField] private Transform[] _lamps;
        [Tooltip("그루터기 충돌체 (보고 F = 올라섬)")]
        [SerializeField] private Collider _stump;
        [SerializeField] private Material _bodyMaterial;
        [SerializeField] private Material _glowMaterial;
        [SerializeField] private Material _sparkMaterial;
        [SerializeField] private TMP_FontAsset _font;

        private readonly DroneMemoryGame _game = new DroneMemoryGame(RepairCommand.MemoryLamps.Length);
        private FirstPersonController _player;
        private bool _onStump;
        private int _mountFrame, _standFrame = -1;
        private Vector3 _standPoint;
        private Renderer[] _lampRenderers;
        private Vector3[] _lampScale;
        private float[] _flash;
        private Color _baseColor, _baseEmission;
        private MaterialPropertyBlock _block;
        private bool _lampsTouched;
        private Transform _drone, _droneGlow;
        private ParticleSystem _sparks;
        private Vector3 _droneFrom, _droneAt;
        private float _droneFly = 1f, _droneTime;
        private int _shownStep = -1, _shownLit = -1;
        private int _aim = -1;
        private bool _recorded;
        private string _notice;
        private float _noticeTimer;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public DroneMemoryGame Game => _game;
        public bool OnStump => _onStump;
        public static int Best => _best;
        /// <summary>지금 겨눈 등불 (없으면 −1).</summary>
        public int Aim => _aim;

        public void Configure(Transform[] lamps, Collider stump, Material body, Material glow, Material spark, TMP_FontAsset font)
        {
            _lamps = lamps;
            _stump = stump;
            _bodyMaterial = body;
            _glowMaterial = glow;
            _sparkMaterial = spark;
            _font = font;
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            int n = _lamps != null ? _lamps.Length : 0;
            _lampRenderers = new Renderer[n];
            _lampScale = new Vector3[n];
            _flash = new float[n];
            for (int k = 0; k < n; k++)
            {
                if (_lamps[k] == null)
                    continue;
                _lampRenderers[k] = _lamps[k].GetComponent<Renderer>();
                _lampScale[k] = _lamps[k].localScale;
                var m = _lampRenderers[k] != null ? _lampRenderers[k].sharedMaterial : null;
                if (m != null && _baseEmission == default)
                {
                    _baseColor = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
                    _baseEmission = m.HasProperty(EmissionId) ? m.GetColor(EmissionId) : Color.white;
                }
            }
            if (_stump != null)
            {
                var use = _stump.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "그루터기에 올라 드론 순서 기억하기" + (_best < 0 ? "" : $"  <size=80%>(최고 기록 {_best}개)</size>");
                use.Completed = Mount;
            }
            _drone = InteriorFireflyShow.BuildDrone(transform, "MemoryDrone", _bodyMaterial, _glowMaterial, out _droneGlow);
            _drone.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            // 방을 나가면 바로 내려오고 판은 접음
            Release();
            _game.Stop();
            if (_drone != null)
                _drone.gameObject.SetActive(false);
            ApplyLamps(0f);
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
            if (_onStump && _player != null && !_player.Riding)
                ClearStump();   // 올라선 채 패드로 빠른 이동 (Teleport가 탈것을 지움)
            if (_noticeTimer > 0f)
                _noticeTimer -= dt;
            _aim = _onStump ? AimedLamp() : -1;
            if (_onStump)
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
            MoveDrone(dt);
            ApplyLamps(dt);
            UpdateHint();
        }

        // ---------------- 올라서기 ----------------

        private void Mount()
        {
            if (_onStump || _player == null || Time.frameCount == _standFrame)
                return; // 내려오기와 같은 프레임의 F (InteriorMode가 뒤에서 다시 부름)
            _standPoint = _player.transform.position;
            var top = new Vector3(RepairCommand.MemoryStump.x, RepairCommand.MemoryStumpTop, RepairCommand.MemoryStump.z);
            _player.RideAt(transform, transform.TransformPoint(top), FirstPersonController.EyeHeight, RepairCommand.MemoryYaw + transform.eulerAngles.y, RepairCommand.MemoryPitch);
            _onStump = true;
            _mountFrame = Time.frameCount;
            if (_stump != null)
                _stump.enabled = false;
            _lampsTouched = true;
            Begin();
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void Begin()
        {
            _game.Begin();
            _recorded = false;
            _shownStep = _shownLit = -1;
            _drone.gameObject.SetActive(true);
            _droneAt = _droneFrom = RepairCommand.MemoryPerch;
            _droneFly = 1f;
            _drone.localPosition = _droneAt;
            EnsureSparks();
            InteriorFireflyShow.Burst(_sparks, _drone.position, 14, 0.8f);
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
                    AudioService.TryPlay(l => l.UiOpen);
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
            _drone.gameObject.SetActive(false);
            AudioService.TryPlay(l => l.UiClose);
        }

        private void Release()
        {
            if (!_onStump)
                return;
            ClearStump();
            if (_player != null)
            {
                _player.RideAt(transform, _standPoint, FirstPersonController.EyeHeight, null, 0f);
                _player.Dismount();
            }
        }

        private void ClearStump()
        {
            _onStump = false;
            if (_stump != null)
                _stump.enabled = true;
        }

        /// <summary>시선과 가장 가까운 등불 (MemoryAimDegrees 안, 없으면 −1).</summary>
        private int AimedLamp()
        {
            var cam = Camera.main;
            if (cam == null || _lamps == null)
                return -1;
            int best = -1;
            float bestAngle = RepairCommand.MemoryAimDegrees;
            for (int k = 0; k < _lamps.Length; k++)
            {
                if (_lamps[k] == null)
                    continue;
                float a = Vector3.Angle(cam.transform.forward, _lamps[k].position - cam.transform.position);
                if (a < bestAngle)
                {
                    bestAngle = a;
                    best = k;
                }
            }
            return best;
        }

        // ---------------- 판 ----------------

        private void Pick(int lamp)
        {
            var r = _game.Pick(lamp);
            if (r == DroneMemoryGame.Result.Ignored)
                return;
            _flash[lamp] = 1f;
            EnsureSparks();
            if (_lamps[lamp] != null)
                InteriorFireflyShow.Burst(_sparks, _lamps[lamp].position, r == DroneMemoryGame.Result.Wrong ? 6 : 16, 0.8f);
            switch (r)
            {
                case DroneMemoryGame.Result.Correct:
                    AudioService.TryPlay(l => l.UiClick);
                    break;
                case DroneMemoryGame.Result.Cleared:
                    Notice($"맞았어요!  다음은 {_game.Length + 1}개", DroneMemoryGame.ClearedTime);
                    AudioService.TryPlay(l => l.EventPositive);
                    break;
                case DroneMemoryGame.Result.Wrong:
                    AudioService.TryPlay(l => l.UiError);
                    break;
            }
        }

        private void Entered(DroneMemoryGame.Phase phase)
        {
            if (phase == DroneMemoryGame.Phase.Show)
                _shownStep = _shownLit = -1;
            if (phase == DroneMemoryGame.Phase.Input || phase == DroneMemoryGame.Phase.Done)
                FlyTo(RepairCommand.MemoryPerch, 0.6f);   // 드론은 쉬는 자리로 돌아와 지켜봄
        }

        /// <summary>보여 주기: 차례마다 드론이 그 등불 옆으로 날아가고, 닿으면 등불이 켜짐.</summary>
        private void ShowStep()
        {
            int i = _game.ShowIndex;
            if (i >= 0 && i != _shownStep)
            {
                _shownStep = i;
                FlyTo(RepairCommand.MemoryHover(_game.Sequence[i]), _game.FlyTime);
            }
            int lit = _game.ShowLit;
            if (lit >= 0 && lit != _shownLit)
            {
                EnsureSparks();
                if (_lamps[lit] != null)
                    InteriorFireflyShow.Burst(_sparks, _lamps[lit].position, 18, 0.9f);
                AudioService.TryPlay(l => l.UiTab);
            }
            _shownLit = lit;
        }

        private void Record()
        {
            _recorded = true;
            bool record = _game.Score > 0 && _game.Score > _best;
            if (record)
                _best = _game.Score;
            AudioService.TryPlay(l => record ? l.GradeUp : l.UiTab);
        }

        // ---------------- 드론 ----------------

        private void FlyTo(Vector3 to, float time)
        {
            _droneFrom = _drone != null ? _drone.localPosition : to;
            _droneAt = to;
            _droneTime = Mathf.Max(0.05f, time);
            _droneFly = 0f;
        }

        private void MoveDrone(float dt)
        {
            if (_drone == null || !_drone.gameObject.activeSelf)
                return;
            var before = _drone.localPosition;
            Vector3 p;
            if (_droneFly < 1f)
            {
                _droneFly = Mathf.Min(1f, _droneFly + dt / _droneTime);
                p = RepairCommand.MemoryFlight(_droneFrom, _droneAt, Mathf.SmoothStep(0f, 1f, _droneFly));
            }
            else
            {
                p = _droneAt + Vector3.up * (PerchHop * Mathf.Sin(Time.unscaledTime * 3f));
            }
            _drone.localPosition = p;
            var move = p - before;
            var flat = new Vector3(move.x, 0f, move.z);
            var face = flat.sqrMagnitude > 1e-6f ? flat : (_onStump && Camera.main != null ? transform.InverseTransformDirection(Camera.main.transform.position - _drone.position) : Vector3.zero);
            face.y = 0f;
            if (face.sqrMagnitude > 1e-6f)
                _drone.localRotation = Quaternion.Slerp(_drone.localRotation, Quaternion.LookRotation(face.normalized, Vector3.up) * Quaternion.Euler(8f, 0f, 0f), 1f - Mathf.Exp(-dt * 8f));
            bool bright = _game.ShowLit >= 0 || _game.Current == DroneMemoryGame.Phase.Cleared;
            float glow = _game.Current == DroneMemoryGame.Phase.Done ? 0.6f : bright ? 1.6f : 1f;
            EnsureSparks();
            InteriorFireflyShow.Shine(_droneGlow, _sparks, glow, 0f, dt);
        }

        private void EnsureSparks()
        {
            if (_sparks == null)
                _sparks = InteriorFireflyShow.CreateSparks(transform, _sparkMaterial);
        }

        // ---------------- 등불 ----------------

        /// <summary>등불 밝기 (재질 칸 0 블록 — InteriorAtmosphere가 빛 재질 칸마다 블록을 걸어 렌더러 블록은 무시됨): 올라서 있지 않으면 원래대로.</summary>
        private void ApplyLamps(float dt)
        {
            if (_lampRenderers == null)
                return;
            bool playing = _onStump && _game.Running;
            if (!playing)
            {
                if (!_lampsTouched)
                    return;
                _lampsTouched = false;
                for (int k = 0; k < _lampRenderers.Length; k++)
                {
                    if (_lampRenderers[k] != null)
                    {
                        _block.Clear();
                        _lampRenderers[k].SetPropertyBlock(_block, 0);
                    }
                    if (_lamps[k] != null)
                        _lamps[k].localScale = _lampScale[k];
                }
                return;
            }
            _lampsTouched = true;
            bool blink = Mathf.Repeat(Time.unscaledTime * BlinkHz, 1f) < 0.5f;
            for (int k = 0; k < _lampRenderers.Length; k++)
            {
                var r = _lampRenderers[k];
                float g = DimGlow;
                bool canAim = _game.Current == DroneMemoryGame.Phase.Input;
                if (canAim && k == _aim)
                    g = AimGlow;
                if (_game.ShowLit == k)
                    g = LitGlow;
                if (_game.Current == DroneMemoryGame.Phase.Cleared)
                    g = Mathf.Lerp(LitGlow, DimGlow, _game.PhaseTime / DroneMemoryGame.ClearedTime);
                if (_game.Current == DroneMemoryGame.Phase.Done)
                {
                    if (k == _game.ExpectedLamp)
                        g = LitGlow;                       // 맞는 등불 = 환히
                    else if (k == _game.WrongLamp)
                        g = blink ? AimGlow : DimGlow;     // 고른 등불 = 깜박
                }
                g = Mathf.Max(g, LitGlow * _flash[k]);
                if (r != null)
                {
                    r.GetPropertyBlock(_block, 0);
                    _block.SetColor(BaseColorId, Color.Lerp(_baseColor * 0.2f, _baseColor, Mathf.Clamp01(g)));
                    _block.SetColor(EmissionId, _baseEmission * g);
                    r.SetPropertyBlock(_block, 0);
                }
                if (_lamps[k] != null)
                    _lamps[k].localScale = _lampScale[k] * (1f + 0.25f * Mathf.Clamp01((g - 1f) / (LitGlow - 1f)));
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
            if (!_onStump)
            {
                ShowHint(null);
                return;
            }
            string f = KeyBindings.Label(GameAction.Interact);
            string legend = "<size=80%>1 별 · 2 달 · 3 하트 · 4 잎 · 5 물방울</size>";
            switch (_game.Current)
            {
                case DroneMemoryGame.Phase.Ready:
                    ShowHint($"반딧불 드론이 등불을 켜는 순서를 잘 보세요  ·  {_game.Length}개");
                    break;
                case DroneMemoryGame.Phase.Show:
                    ShowHint($"순서 보는 중  ·  {Mathf.Max(0, _game.ShowIndex) + 1} / {_game.Length}");
                    break;
                case DroneMemoryGame.Phase.Input:
                    ShowHint($"같은 순서로 등불을 보고 클릭 (또는 숫자)  ·  {_game.Entered} / {_game.Length}\n{legend}");
                    break;
                case DroneMemoryGame.Phase.Done:
                    bool record = _game.Score > 0 && _game.Score >= _best;
                    ShowHint($"틀렸어요 (환한 등불이 맞는 차례)  ·  {_game.Score}개까지 기억했어요  ·  " + (record ? "최고 기록!" : $"최고 {Mathf.Max(_best, 0)}개") +
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
            var go = new GameObject("DroneMemoryHint", typeof(RectTransform));
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
