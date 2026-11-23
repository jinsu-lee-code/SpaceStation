using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 3단계 궤도 항만 미니게임 "진주 조개 잡기" (사용자 2026-11-16 "미니게임 진행해줘"): 잡기 단에서 큰 조개 쪽을 보고 F = 시작 →
    /// 뚜껑이 모두 닫히고 3 · 2 · 1 → 30초 동안 조개가 번갈아 잠깐 열림 (판 규칙 = <see cref="PearlClamGame"/>). 열린 조개를 가운데로 보고 F = 속을 잡음 —
    /// 진주는 날아올라 품으로 (+1), 금빛 진주는 더 크고 반짝임 (+3), 게는 집게를 들고 들썩임 (−1). 끝나면 점수 · 최고 기록 (글자)을 보이고 조개가 다시 열려 쉼.
    /// 뚜껑 = op3_builder ClamLid{k} (원점 = 경첩, 52° 열린 채) 를 경첩 축으로 돌림, 진주 = ClamPearl{k}, 게 = 내부 동물 게 모델 (critter_builder).
    /// 색 대신 크기 · 반짝임 · 글로 구분 (색약). 위치는 템플릿 루트 (이 물체) 기준, 시간은 실제 시간. 템플릿 빌더가 configure에서 붙임.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 잡는 F를 여기서 먼저 씀
    public sealed class InteriorPearlClams : MonoBehaviour
    {
        private const float AimDegrees = 13f, CatchReach = 6f, FlySeconds = 0.45f, ResultSeconds = 5f;
        /// <summary>게 모델 배율 (내부 게 약 0.3 m × 이만큼 — 3.6 m 큰 조개 속에서 1.6배는 작아 안 보였음).</summary>
        private const float CrabScale = 2.4f;
        private static readonly Color GoldTint = new Color(1f, 0.78f, 0.3f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Transform[] _lids;
        [SerializeField] private Transform[] _pearls;
        [Tooltip("시작 상자 (보고 F · 판 중에는 꺼짐)")]
        [SerializeField] private Collider _start;
        [SerializeField] private Mesh _crabMesh;
        [SerializeField] private Material _crabMaterial;
        [SerializeField] private Material _sparkMaterial;
        [SerializeField] private TMP_FontAsset _font;

        private static int _best = -1;

        private readonly PearlClamGame _game = new PearlClamGame(System.Environment.TickCount);
        private Quaternion[] _lidBase;
        private Vector3[] _pearlScale;
        private Transform[] _crabs;
        private Renderer[] _pearlRenderers;
        private MaterialPropertyBlock _block;
        private Transform[] _fly;
        private float[] _flyT;
        private Vector3[] _flyFrom;
        private bool[] _flyGold;
        private float[] _crabJump;
        private ParticleSystem _sparks;
        private float _twinkle;
        private float _resultTimer;
        private bool _record;
        private int _streak;   // 연달아 진주를 잡은 수 (점수 "띵" 음높이) — 게 · 헛잡기 · 새 판 = 0
        private Transform _eye;
        private TextMeshPro _signText;
        private float _signTimer;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;
        private string _notice;
        private float _noticeTimer;
        private bool _wasGold;

        public PearlClamGame Game => _game;
        public static int Best => _best;
        public bool Busy => _game.Current != PearlClamGame.Phase.Idle;

        public void Configure(Transform[] lids, Transform[] pearls, Collider start, Mesh crabMesh, Material crabMaterial, Material spark, TMP_FontAsset font)
        {
            _lids = lids;
            _pearls = pearls;
            _start = start;
            _crabMesh = crabMesh;
            _crabMaterial = crabMaterial;
            _sparkMaterial = spark;
            _font = font;
        }

        private void Awake()
        {
            if (_lids == null || _lids.Length < PearlClamGame.ClamCount || _pearls == null || _pearls.Length < PearlClamGame.ClamCount)
            {
                enabled = false;
                return;
            }
            int n = PearlClamGame.ClamCount;
            _lidBase = new Quaternion[n];
            _pearlScale = new Vector3[n];
            _pearlRenderers = new Renderer[n];
            _crabs = new Transform[n];
            _fly = new Transform[n];
            _flyT = new float[n];
            _flyFrom = new Vector3[n];
            _flyGold = new bool[n];
            _crabJump = new float[n];
            _block = new MaterialPropertyBlock();
            for (int k = 0; k < n; k++)
            {
                _lidBase[k] = _lids[k].localRotation;
                _pearlScale[k] = _pearls[k].localScale;
                _pearlRenderers[k] = _pearls[k].GetComponent<Renderer>();
                _crabs[k] = BuildCrab(k);
                _fly[k] = BuildFlyingPearl(k);
                _flyT[k] = -1f;
                _crabJump[k] = -1f;
            }
            if (_start != null)
            {
                var use = _start.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "진주 조개 잡기  <size=80%>(30초" + (_best < 0 ? ")" : $" · 최고 {_best}점)") + "</size>";
                use.Blocked = () => !OnStage() ? "잡기 단에 올라서서 해요" : null;
                use.Completed = Begin;
            }
            BuildSign();
            Apply(0f);
        }

        private void OnDisable()
        {
            // 방을 나가면 바로 끝 · 쉬는 모습
            _game.Stop();
            _resultTimer = 0f;
            for (int k = 0; _flyT != null && k < _flyT.Length; k++)
                _flyT[k] = -1f;
            if (_start != null)
                _start.enabled = true;
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        // ---------------- 진행 ----------------

        private void Begin()
        {
            if (Busy)
                return;
            _game.Start();
            _resultTimer = 0f;
            _streak = 0;
            if (_start != null)
                _start.enabled = false;     // 시선이 조개에 닿게
            Notice("조개가 닫혀요… 열리는 조개를 가운데로 보고 F", PearlClamGame.CountdownTime);
            AudioService.TryPlay(l => l.GameStart);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_eye == null && Camera.main != null)
                _eye = Camera.main.transform;
            if (_noticeTimer > 0f)
                _noticeTimer -= dt;
            var before = _game.Current;
            _game.Tick(dt);         // 쉬는 동안도 (뚜껑이 천천히 열림)
            if (_game.Playing && KeyBindings.WasPressed(GameAction.Interact))
                TryCatch();
            if ((_game.Playing || _game.Current == PearlClamGame.Phase.Countdown) && !NearStage())
            {
                _game.Finish();
                Notice("잡기 단을 떠나서 끝났어요", 2f);
            }
            if (before != PearlClamGame.Phase.Done && _game.Current == PearlClamGame.Phase.Done)
                EndGame();
            if (_game.Current == PearlClamGame.Phase.Done)
            {
                _resultTimer -= dt;
                if (_resultTimer <= 0f)
                {
                    _game.Stop();
                    if (_start != null)
                        _start.enabled = true;
                }
            }
            Apply(dt);
            UpdateSign(dt);
            UpdateHint();
        }

        private void EndGame()
        {
            _record = _game.Score > 0 && (_best < 0 || _game.Score > _best);
            if (_best < 0 || _game.Score > _best)
                _best = _game.Score;
            string extra = _game.Golds > 0 ? $" · 금빛 {_game.Golds}개" : "";
            Notice($"진주 {_game.Score}점!{extra}" + (_record ? "  ·  최고 기록!" : $"  ·  최고 {Mathf.Max(_best, 0)}점"), ResultSeconds);
            _resultTimer = ResultSeconds;
            AudioService.TryPlay(l => _record ? l.GameRecord : _game.Score > 0 ? l.GameClear : l.GameFail);
        }

        /// <summary>시선 가운데에 가장 가까운 조개 (13° 안 · 6 m 안) 속을 잡음.</summary>
        private void TryCatch()
        {
            if (_eye == null)
                return;
            int pick = -1;
            float bestAngle = AimDegrees;
            for (int k = 0; k < PearlClamGame.ClamCount; k++)
            {
                var to = _pearls[k].position - _eye.position;
                if (to.magnitude > CatchReach)
                    continue;
                float a = Vector3.Angle(_eye.forward, to);
                if (a < bestAngle)
                {
                    bestAngle = a;
                    pick = k;
                }
            }
            var got = pick >= 0 ? _game.TryCatch(pick) : PearlClamGame.Catch.Miss;
            switch (got)
            {
                case PearlClamGame.Catch.Pearl:
                case PearlClamGame.Catch.Gold:
                    bool gold = got == PearlClamGame.Catch.Gold;
                    _flyT[pick] = 0f;
                    _flyFrom[pick] = _pearls[pick].position;
                    _flyGold[pick] = gold;
                    Burst(_pearls[pick].position, gold ? 28 : 12, gold);
                    Notice(gold ? "+3  금빛 진주!" : "+1  진주", 1.1f);
                    AudioService.TryPlayScore(++_streak);
                    break;
                case PearlClamGame.Catch.Crab:
                    _crabJump[pick] = 0f;
                    Notice("앗, 게가 꼬집었어요  -1", 1.4f);
                    _streak = 0;
                    AudioService.TryPlay(l => l.GameMiss);
                    break;
                default:
                    _streak = 0;
                    AudioService.TryPlay(l => l.GameMiss);
                    break;
            }
        }

        private bool OnStage() => Horizontal(OrbitalPort.ClamStage) <= OrbitalPort.ClamStageReach;

        /// <summary>판을 이어 가는 거리 (단 둘레 — 단 뒤 모래톱까지 한 걸음 물러나도 됨).</summary>
        private bool NearStage() => Horizontal(OrbitalPort.ClamStage) <= OrbitalPort.ClamStageReach + 2.5f;

        private float Horizontal(Vector3 local)
        {
            if (_eye == null)
                return float.MaxValue;
            var p = transform.InverseTransformPoint(_eye.position) - local;
            return new Vector2(p.x, p.z).magnitude;
        }

        // ---------------- 조개 · 진주 · 게 ----------------

        private void Apply(float dt)
        {
            _twinkle += dt;
            bool round = _game.Current == PearlClamGame.Phase.Countdown || _game.Playing;
            for (int k = 0; k < PearlClamGame.ClamCount; k++)
            {
                float close = -OrbitalPort.ClamLidOpen * (1f - Smooth(_game.Lid(k)));
                _lids[k].localRotation = Quaternion.AngleAxis(close, OrbitalPort.ClamHingeAxis(k)) * _lidBase[k];
                var content = round ? _game.ContentOf(k) : PearlClamGame.Content.Pearl;
                bool show = _game.ShowsContent(k);
                bool pearl = show && content != PearlClamGame.Content.Crab;
                if (_pearls[k].gameObject.activeSelf != pearl)
                    _pearls[k].gameObject.SetActive(pearl);
                if (pearl)
                {
                    bool gold = content == PearlClamGame.Content.Gold;
                    _pearls[k].localScale = _pearlScale[k] * (gold ? 1.14f : 1f);
                    SetGold(_pearlRenderers[k], gold);
                    if (gold)
                        Twinkle(k);
                }
                bool crab = show && content == PearlClamGame.Content.Crab && round;
                if (_crabs[k] != null)
                {
                    if (_crabs[k].gameObject.activeSelf != (crab || _crabJump[k] >= 0f))
                        _crabs[k].gameObject.SetActive(crab || _crabJump[k] >= 0f);
                    PoseCrab(k, dt);
                }
                FlyPearl(k, dt);
            }
            bool goldOpen = false;
            for (int k = 0; k < PearlClamGame.ClamCount; k++)
                goldOpen |= _game.Playing && _game.IsOpen(k) && _game.ContentOf(k) == PearlClamGame.Content.Gold;
            if (goldOpen && !_wasGold)
                Notice("반짝! 금빛 진주 (+3)", 1.2f);
            _wasGold = goldOpen;
        }

        private void SetGold(Renderer r, bool gold)
        {
            if (r == null)
                return;
            if (!gold)
            {
                r.SetPropertyBlock(null);
                return;
            }
            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, GoldTint);
            _block.SetColor(EmissionId, GoldTint * 2.2f);
            r.SetPropertyBlock(_block);
        }

        /// <summary>금빛 진주 둘레 반짝이 (열려 있는 동안 틈틈이).</summary>
        private void Twinkle(int k)
        {
            if (Random.value > 0.25f)
                return;
            var ps = Sparks();
            if (ps == null)
                return;
            ps.Emit(new ParticleSystem.EmitParams
            {
                position = _pearls[k].position + Random.onUnitSphere * 0.38f,
                velocity = Vector3.up * Random.Range(0.1f, 0.35f),
                startLifetime = Random.Range(0.35f, 0.7f),
                startSize = Random.Range(0.05f, 0.1f),
                startColor = new Color(1f, 0.92f, 0.6f),
                applyShapeToPosition = false,
            }, 1);
        }

        /// <summary>게 = 그릇 안 바닥에서 입 쪽을 봄, 열려 있는 동안 집게를 들썩이듯 살짝 들썩 · 잡히면 한 번 뛰어오름.</summary>
        private void PoseCrab(int k, float dt)
        {
            var c = _crabs[k];
            var basePos = OrbitalPort.ClamPearls[k] + Vector3.down * 0.24f;   // 그릇 안 바닥 (진주 가운데 − 0.29) 위 — 둥근 바닥이 둘레로 4 cm 솟아 발끝이 묻히지 않게
            float bob = 0.015f * Mathf.Sin(Time.unscaledTime * 14f + k);
            if (_crabJump[k] >= 0f)
            {
                _crabJump[k] += dt;
                float t = _crabJump[k] / 0.5f;
                bob = 0.32f * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                if (t >= 1f)
                    _crabJump[k] = -1f;
            }
            c.localPosition = basePos + Vector3.up * bob;
            c.localRotation = Quaternion.Euler(0f, 180f - OrbitalPort.Clams[k].z, 3f * Mathf.Sin(Time.unscaledTime * 9f + k));
        }

        /// <summary>잡은 진주가 품 (눈 앞 아래)으로 날아와 작아지며 사라짐.</summary>
        private void FlyPearl(int k, float dt)
        {
            var f = _fly[k];
            if (f == null)
                return;
            if (_flyT[k] < 0f || _eye == null)
            {
                if (f.gameObject.activeSelf)
                    f.gameObject.SetActive(false);
                return;
            }
            _flyT[k] += dt;
            float t = Mathf.Clamp01(_flyT[k] / FlySeconds);
            var to = _eye.position + _eye.forward * 0.55f - _eye.up * 0.3f;
            var p = Vector3.Lerp(_flyFrom[k], to, Smooth(t)) + Vector3.up * (0.8f * Mathf.Sin(t * Mathf.PI));
            if (!f.gameObject.activeSelf)
            {
                f.gameObject.SetActive(true);
                SetGold(f.GetComponent<Renderer>(), _flyGold[k]);
            }
            f.position = p;
            f.localScale = _pearlScale[k] * Mathf.Lerp(_flyGold[k] ? 1.14f : 1f, 0.15f, t);
            if (t >= 1f)
            {
                _flyT[k] = -1f;
                f.gameObject.SetActive(false);
                Burst(to, 6, _flyGold[k]);
            }
        }

        private Transform BuildCrab(int k)
        {
            if (_crabMesh == null)
                return null;
            var go = new GameObject("ClamCrab" + k);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * CrabScale;
            go.AddComponent<MeshFilter>().sharedMesh = _crabMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _crabMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go.transform;
        }

        private Transform BuildFlyingPearl(int k)
        {
            var src = _pearls[k].GetComponent<MeshFilter>();
            if (src == null || _pearlRenderers[k] == null)
                return null;
            var go = new GameObject("CaughtPearl" + k);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = src.sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = _pearlRenderers[k].sharedMaterials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go.transform;
        }

        private ParticleSystem Sparks()
        {
            if (_sparks != null || _sparkMaterial == null)
                return _sparks;
            _sparks = ShootingStar.CreateSparks(transform, _sparkMaterial);
            _sparks.name = "ClamSparks";
            var limit = _sparks.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = 2.4f;
            return _sparks;
        }

        private void Burst(Vector3 at, int count, bool gold)
        {
            var ps = Sparks();
            if (ps == null)
                return;
            for (int i = 0; i < count; i++)
            {
                ps.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = Random.onUnitSphere * Random.Range(0.8f, gold ? 2.6f : 1.6f),
                    startLifetime = Random.Range(0.4f, 0.9f),
                    startSize = Random.Range(0.05f, gold ? 0.14f : 0.1f),
                    startColor = gold ? new Color(1f, 0.9f, 0.55f) : new Color(0.95f, 0.97f, 1f),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        // ---------------- 안내판 · 안내 줄 (색 대신 글) ----------------

        private void BuildSign()
        {
            var font = _font != null ? _font : InteriorRoomDisplay.Font;
            if (font == null)
                return;
            var go = new GameObject("PearlClamSign");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = OrbitalPort.ClamGameSign;
            _signText = go.AddComponent<TextMeshPro>();
            _signText.font = font;
            _signText.fontSize = 1.4f;
            _signText.color = new Color(0.98f, 0.95f, 0.86f);
            _signText.alignment = TextAlignmentOptions.Center;
            _signText.textWrappingMode = TextWrappingModes.NoWrap;
            _signText.rectTransform.sizeDelta = new Vector2(2.2f, 0.5f);
        }

        private void UpdateSign(float dt)
        {
            if (_signText == null)
                return;
            var cam = Camera.main;
            if (cam != null)
            {
                var away = _signText.transform.position - cam.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-4f)
                    _signText.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
            _signTimer -= dt;
            if (_signTimer > 0f)
                return;
            _signTimer = 0.25f;
            string status;
            if (_game.Playing)
                status = $"남은 {Mathf.CeilToInt(Mathf.Max(0f, _game.Remaining))}초 · {_game.Score}점";
            else if (_game.Current == PearlClamGame.Phase.Countdown)
                status = "곧 시작";
            else
                status = _best < 0 ? $"조개 쪽을 보고 {KeyBindings.Label(GameAction.Interact)}" : $"최고 {_best}점";
            _signText.SetText($"진주 조개 잡기\n<size=58%>{status}</size>");
        }

        private void Notice(string text, float seconds)
        {
            _notice = text;
            _noticeTimer = seconds;
        }

        private void UpdateHint()
        {
            string text = null;
            string key = KeyBindings.Label(GameAction.Interact);
            if (_game.Current == PearlClamGame.Phase.Countdown)
                text = $"곧 시작해요  ·  {Mathf.CeilToInt(Mathf.Max(0f, PearlClamGame.CountdownTime - _game.PhaseTime))}";
            else if (_game.Playing)
                text = $"진주 {_game.Score}점  ·  남은 시간 {Mathf.CeilToInt(Mathf.Max(0f, _game.Remaining))}초\n"
                     + $"<size=80%>열린 조개를 가운데로 보고 {key}  ·  금빛 진주 +3  ·  게는 -1</size>";
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
            var go = new GameObject("PearlClamHint", typeof(RectTransform));
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
            Place(back.rectTransform, new Vector2(1060f, 104f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(1030f, 96f));
            var font = _font != null ? _font : InteriorRoomDisplay.Font;
            if (font != null)
                _hint.font = font;
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
