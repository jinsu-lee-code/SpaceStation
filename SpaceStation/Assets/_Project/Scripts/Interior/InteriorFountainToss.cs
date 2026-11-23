using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 3단계 궤도 항만 미니게임 "인어 분수 진주 던지기" (사용자 2026-11-16 "미니게임 진행해줘"): 분수 마당에서 분수 물을 보고 F = 시작 →
    /// 물 위에 빛 고리 셋이 꼬리 둘레를 돎 (큰 고리 1점 · 중간 2점 · 작은 고리 3점 — 고리 위 숫자, 작을수록 빨리). 물을 보고 F = 보는 자리 (물 위 작은 겨냥 고리)로 진주가
    /// 포물선을 그리며 날아감 — 고리가 도는 만큼 앞을 보고 던져야 함 (판 규칙 = <see cref="FountainToss"/>). 고리에 들면 반짝이 + 꼬리 끝에서 빛 물기둥, 물이면 물보라.
    /// 진주 일곱 개가 다 떨어지면 점수 · 최고 기록 (글자). 색 대신 고리 크기 · 숫자 · 글로 구분 (색약). 위치는 템플릿 루트 (이 물체) 기준, 시간은 실제 시간.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 던지는 F를 여기서 먼저 씀
    public sealed class InteriorFountainToss : MonoBehaviour
    {
        private const int RingPoints = 48;
        private const float ResultSeconds = 5f, SinkSeconds = 0.5f;
        /// <summary>던지는 진주 크기 (조개 진주 메시 0.3 m × 이만큼 = 4.5 cm) — 손 앞 0.55 m에서 출발 (0.3 m 앞 0.06 m는 눈앞을 크게 가렸음).</summary>
        private const float PearlScale = 0.15f;

        [Tooltip("시작 상자 (분수 물 위, 보고 F · 판 중에는 꺼짐)")]
        [SerializeField] private Collider _start;
        [SerializeField] private Mesh _pearlMesh;
        [SerializeField] private Material _pearlMaterial;
        [SerializeField] private Material _lineMaterial;
        [SerializeField] private Material _sparkMaterial;
        [SerializeField] private TMP_FontAsset _font;

        private static int _best = -1;

        private FountainToss _game;
        private float _waterY;
        private LineRenderer[] _rings;
        private TextMeshPro[] _labels;
        private float[] _flash;
        private LineRenderer _aim;
        private readonly Transform[] _pearls = new Transform[FountainToss.PearlCount];
        private readonly Vector3[] _from = new Vector3[FountainToss.PearlCount];
        private readonly Vector3[] _to = new Vector3[FountainToss.PearlCount];
        private readonly float[] _t = new float[FountainToss.PearlCount];
        private ParticleSystem _sparks;
        private float _resultTimer;
        private float _show;           // 고리 보임 0 ~ 1
        private bool _record;
        private int _streak;           // 연달아 고리에 넣은 수 (점수 "띵" 음높이) — 물 · 밖 · 새 판 = 0
        private Transform _eye;
        private TextMeshPro _signText;
        private float _signTimer;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;
        private string _notice;
        private float _noticeTimer;

        public FountainToss Game => _game;
        public static int Best => _best;
        public bool Busy => _game != null && _game.Current != FountainToss.Phase.Idle;

        public void Configure(Collider start, Mesh pearlMesh, Material pearlMaterial, Material line, Material spark, TMP_FontAsset font)
        {
            _start = start;
            _pearlMesh = pearlMesh;
            _pearlMaterial = pearlMaterial;
            _lineMaterial = line;
            _sparkMaterial = spark;
            _font = font;
        }

        private void Awake()
        {
            var c = OrbitalPort.Fountain;
            _game = new FountainToss(new Vector2(c.x, c.z), OrbitalPort.FountainWaterRadius);
            _waterY = c.y;
            BuildRings();
            for (int i = 0; i < _pearls.Length; i++)
            {
                _pearls[i] = BuildPearl(i);
                _t[i] = -1f;
            }
            if (_start != null)
            {
                var use = _start.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "인어 분수 진주 던지기  <size=80%>(진주 7개" + (_best < 0 ? ")" : $" · 최고 {_best}점)") + "</size>";
                use.Completed = Begin;
            }
            BuildSign();
            ShowRings(0f);
        }

        private void OnDisable()
        {
            // 방을 나가면 바로 끝
            _game?.Stop();
            _resultTimer = 0f;
            _show = 0f;
            for (int i = 0; i < _pearls.Length; i++)
            {
                _t[i] = -1f;
                if (_pearls[i] != null)
                    _pearls[i].gameObject.SetActive(false);
            }
            ShowRings(0f);
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
                _start.enabled = false;     // 시선이 물 위 고리에 닿게
            Notice("빛 고리를 보고 F · 진주가 보는 자리로 날아가요", 2.5f);
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
            if (_game.Playing)
            {
                if (KeyBindings.WasPressed(GameAction.Interact))
                    Throw();
                if (Horizontal(OrbitalPort.Fountain) > OrbitalPort.TossLeave)
                {
                    _game.Finish();
                    Notice("분수를 떠나서 끝났어요", 2f);
                }
            }
            _game.Tick(dt);
            while (_game.PopLanding(out var landing))
                Land(landing);
            if (before == FountainToss.Phase.Play && _game.Current == FountainToss.Phase.Done)
                EndGame();
            if (_game.Current == FountainToss.Phase.Done)
            {
                _resultTimer -= dt;
                if (_resultTimer <= 0f)
                {
                    _game.Stop();
                    if (_start != null)
                        _start.enabled = true;
                }
            }
            _show = Mathf.MoveTowards(_show, _game.Current == FountainToss.Phase.Idle ? 0f : 1f, dt * 2f);
            ShowRings(dt);
            FlyPearls(dt);
            UpdateSign(dt);
            UpdateHint();
        }

        private void EndGame()
        {
            _record = _game.Score > 0 && (_best < 0 || _game.Score > _best);
            if (_best < 0 || _game.Score > _best)
                _best = _game.Score;
            string grade = _game.Score >= 14 ? "인어가 아주 기뻐해요!" : _game.Score >= 7 ? "인어가 기뻐해요" : "인어가 웃어요";
            Notice($"{_game.Score}점 ({_game.Hits}개 고리에)  ·  {grade}" + (_record ? "  ·  최고 기록!" : $"  ·  최고 {Mathf.Max(_best, 0)}점"), ResultSeconds);
            _resultTimer = ResultSeconds;
            AudioService.TryPlay(l => _record ? l.GameRecord : _game.Score > 0 ? l.GameClear : l.GameFail);
        }

        /// <summary>보는 자리 = 시선이 분수 수면에 닿는 점 (수면 위를 보면 그 방향으로 가장 멀리 6 m).</summary>
        private bool AimPoint(out Vector3 local)
        {
            local = default;
            if (_eye == null)
                return false;
            var eye = transform.InverseTransformPoint(_eye.position);
            var dir = transform.InverseTransformDirection(_eye.forward);
            var flat = new Vector2(dir.x, dir.z);
            if (flat.sqrMagnitude < 1e-4f)
                return false;
            float reach = FountainToss.MaxThrow;
            if (dir.y < -1e-3f)
                reach = Mathf.Min(reach, (eye.y - _waterY) / -dir.y * flat.magnitude);
            var p = new Vector2(eye.x, eye.z) + flat.normalized * reach;
            local = new Vector3(p.x, _waterY, p.y);
            return true;
        }

        private void Throw()
        {
            if (!_game.CanThrow || !AimPoint(out var to))
                return;
            int slot = -1;
            for (int i = 0; i < _t.Length && slot < 0; i++)
                if (_t[i] < 0f)
                    slot = i;
            if (slot < 0 || !_game.Throw(new Vector2(to.x, to.z)))
                return;
            _from[slot] = transform.InverseTransformPoint(_eye.position + _eye.forward * 0.55f + _eye.right * 0.2f - _eye.up * 0.3f);
            if (!InBasin(to, 0f))
                to.y = OrbitalPort.PaveTop + 0.06f;       // 분수 밖 = 마당 돌판에 떨어짐
            _to[slot] = to;
            _t[slot] = 0f;
            AudioService.TryPlay(l => l.UiClick);
        }

        private void Land(FountainToss.Landing l)
        {
            var at = transform.TransformPoint(new Vector3(l.At.x, _waterY + 0.02f, l.At.y));
            if (l.Points > 0)
            {
                _flash[l.Ring] = 1f;
                Burst(at, 10 + 8 * l.Points, new Color(1f, 0.93f, 0.7f), 0.8f, 2f);
                Spout(l.Points);
                Notice(l.Points == 3 ? "+3  작은 고리!" : l.Points == 2 ? "+2  중간 고리" : "+1  큰 고리", 1.1f);
                AudioService.TryPlayScore(++_streak);
            }
            else if (l.Ring == FountainToss.Water)
            {
                Splash(at);
                Notice("퐁당!", 0.8f);
                _streak = 0;
                AudioService.TryPlay(lib => lib.GameMiss);
            }
            else
            {
                Notice("분수 밖으로 빗나갔어요", 1f);
                _streak = 0;
                AudioService.TryPlay(lib => lib.GameMiss);
            }
        }

        private static bool InBasin(Vector3 local, float margin)
        {
            var c = OrbitalPort.Fountain;
            return new Vector2(local.x - c.x, local.z - c.z).magnitude <= OrbitalPort.FountainWaterRadius + margin;
        }

        private float Horizontal(Vector3 local)
        {
            if (_eye == null)
                return 0f;
            var p = transform.InverseTransformPoint(_eye.position) - local;
            return new Vector2(p.x, p.z).magnitude;
        }

        // ---------------- 고리 · 겨냥 · 진주 ----------------

        private void BuildRings()
        {
            int n = FountainToss.Rings.Length;
            _rings = new LineRenderer[n];
            _labels = new TextMeshPro[n];
            _flash = new float[n];
            for (int i = 0; i < n; i++)
            {
                var r = FountainToss.Rings[i];
                _rings[i] = Circle("TossRing" + i, r.Radius, 0.035f + 0.012f * r.Points);
                var font = _font != null ? _font : InteriorRoomDisplay.Font;
                if (font == null)
                    continue;
                var go = new GameObject("TossRingLabel" + i);
                go.transform.SetParent(transform, false);
                var t = go.AddComponent<TextMeshPro>();
                t.font = font;
                t.fontSize = 1.5f;
                t.color = new Color(1f, 0.97f, 0.88f);
                t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.rectTransform.sizeDelta = new Vector2(0.5f, 0.4f);
                t.SetText(r.Points.ToString());
                _labels[i] = t;
            }
            _aim = Circle("TossAim", 0.08f, 0.02f);
        }

        /// <summary>물 위에 눕힌 빛 고리 (LineRenderer — 고리 면이 위를 봄).</summary>
        private LineRenderer Circle(string name, float radius, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);    // 로컬 z = 아래 → 선 면이 수면에 누움, 로컬 x-y = 수평
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.alignment = LineAlignment.TransformZ;
            line.positionCount = RingPoints;
            for (int k = 0; k < RingPoints; k++)
            {
                float a = 2f * Mathf.PI * k / RingPoints;
                line.SetPosition(k, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
            line.widthMultiplier = width;
            line.sharedMaterial = _lineMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = 0;
            go.SetActive(false);
            return line;
        }

        private void ShowRings(float dt)
        {
            if (_rings == null)
                return;
            bool on = _show > 0.001f;
            var cam = Camera.main;
            for (int i = 0; i < _rings.Length; i++)
            {
                var ring = _rings[i];
                if (ring.gameObject.activeSelf != on)
                    ring.gameObject.SetActive(on);
                if (_labels[i] != null && _labels[i].gameObject.activeSelf != on)
                    _labels[i].gameObject.SetActive(on);
                if (!on)
                    continue;
                _flash[i] = Mathf.Max(0f, _flash[i] - dt * 1.6f);
                var c2 = _game.RingCenter(i, _game.Time);
                float bob = 0.012f * Mathf.Sin(Time.unscaledTime * 2.3f + i * 1.7f);
                ring.transform.localPosition = new Vector3(c2.x, _waterY + 0.025f + bob, c2.y);
                ring.transform.localScale = Vector3.one * (1f + 0.25f * _flash[i]);
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * (2f + FountainToss.Rings[i].Points) + i);
                var col = new Color(1f, 0.95f, 0.8f, _show * Mathf.Clamp01(pulse + _flash[i]));
                ring.startColor = ring.endColor = col;
                if (_labels[i] != null)
                {
                    var lt = _labels[i].transform;
                    lt.localPosition = new Vector3(c2.x, _waterY + 0.32f + bob, c2.y);
                    if (cam != null)
                    {
                        var away = lt.position - cam.transform.position;
                        away.y = 0f;
                        if (away.sqrMagnitude > 1e-4f)
                            lt.rotation = Quaternion.LookRotation(away, Vector3.up);
                    }
                    _labels[i].alpha = _show;
                }
            }
            var at = Vector3.zero;
            bool aim = _game.Playing && AimPoint(out at) && InBasin(at, 0.1f);    // 분수 밖이면 겨냥 고리 없음 (마당 위 허공에 떠 보였음)
            if (_aim.gameObject.activeSelf != aim)
                _aim.gameObject.SetActive(aim);
            if (aim)
            {
                _aim.transform.localPosition = at + Vector3.up * 0.03f;
                _aim.startColor = _aim.endColor = new Color(1f, 1f, 1f, _game.CanThrow ? 0.9f : 0.35f);
            }
        }

        private Transform BuildPearl(int i)
        {
            if (_pearlMesh == null)
                return null;
            var go = new GameObject("TossPearl" + i);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * PearlScale;
            go.AddComponent<MeshFilter>().sharedMesh = _pearlMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _pearlMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go.transform;
        }

        /// <summary>날아가는 진주: 손 → 떨어질 자리 포물선 (FlightTime초 — 판 규칙과 같은 시각에 닿음), 물에 닿으면 가라앉으며 작아짐.</summary>
        private void FlyPearls(float dt)
        {
            for (int i = 0; i < _pearls.Length; i++)
            {
                var p = _pearls[i];
                if (p == null)
                    continue;
                if (_t[i] < 0f)
                {
                    if (p.gameObject.activeSelf)
                        p.gameObject.SetActive(false);
                    continue;
                }
                if (!p.gameObject.activeSelf)
                    p.gameObject.SetActive(true);
                _t[i] += dt;
                float u = _t[i] / FountainToss.FlightTime;
                if (u <= 1f)
                {
                    float h = Mathf.Max(0.5f, 0.22f * Vector3.Distance(_from[i], _to[i]));
                    p.localPosition = Vector3.Lerp(_from[i], _to[i], u) + Vector3.up * (h * 4f * u * (1f - u));
                    p.localScale = Vector3.one * PearlScale;
                }
                else
                {
                    float s = (_t[i] - FountainToss.FlightTime) / SinkSeconds;
                    p.localPosition = _to[i] + Vector3.down * (0.12f * s);
                    p.localScale = Vector3.one * (PearlScale * Mathf.Max(0f, 1f - s));
                    if (s >= 1f)
                        _t[i] = -1f;
                }
            }
        }

        private ParticleSystem Sparks()
        {
            if (_sparks != null || _sparkMaterial == null)
                return _sparks;
            _sparks = ShootingStar.CreateSparks(transform, _sparkMaterial);
            _sparks.name = "TossSparks";
            var limit = _sparks.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = 2.2f;
            return _sparks;
        }

        private void Burst(Vector3 at, int count, Color color, float min, float max)
        {
            var ps = Sparks();
            if (ps == null)
                return;
            for (int i = 0; i < count; i++)
            {
                var v = Random.insideUnitCircle;
                ps.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = new Vector3(v.x, Random.Range(0.6f, 1.2f), v.y) * Random.Range(min, max),
                    startLifetime = Random.Range(0.5f, 1f),
                    startSize = Random.Range(0.05f, 0.12f),
                    startColor = Color.Lerp(color, Color.white, Random.value * 0.4f),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        /// <summary>물에 떨어진 진주: 흰 물보라 (위로 튀었다 잦아듦).</summary>
        private void Splash(Vector3 at)
        {
            var ps = Sparks();
            if (ps == null)
                return;
            for (int i = 0; i < 14; i++)
            {
                var v = Random.insideUnitCircle * 0.5f;
                ps.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = new Vector3(v.x, Random.Range(0.9f, 1.6f), v.y),
                    startLifetime = Random.Range(0.35f, 0.7f),
                    startSize = Random.Range(0.04f, 0.08f),
                    startColor = Color.Lerp(new Color(0.75f, 0.9f, 1f), Color.white, Random.value),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        /// <summary>고리에 들면 꼬리 끝에서 빛 물기둥이 솟음 (점수만큼 높고 많이).</summary>
        private void Spout(int points)
        {
            var ps = Sparks();
            if (ps == null)
                return;
            var tip = transform.TransformPoint(OrbitalPort.FountainTip);
            for (int i = 0; i < 14 * points; i++)
            {
                var v = Random.insideUnitCircle * 0.35f;
                ps.Emit(new ParticleSystem.EmitParams
                {
                    position = tip,
                    velocity = new Vector3(v.x, Random.Range(1.6f, 2.2f + 0.5f * points), v.y),
                    startLifetime = Random.Range(0.7f, 1.2f),
                    startSize = Random.Range(0.06f, 0.13f),
                    startColor = Color.Lerp(new Color(1f, 0.93f, 0.7f), new Color(0.8f, 0.95f, 1f), Random.value),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        // ---------------- 안내판 · 안내 줄 (색 대신 글) ----------------

        private void BuildSign()
        {
            var font = _font != null ? _font : InteriorRoomDisplay.Font;
            if (font == null)
                return;
            var go = new GameObject("FountainTossSign");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = OrbitalPort.TossSign;
            _signText = go.AddComponent<TextMeshPro>();
            _signText.font = font;
            _signText.fontSize = 1.4f;
            _signText.color = new Color(0.98f, 0.95f, 0.86f);
            _signText.alignment = TextAlignmentOptions.Center;
            _signText.textWrappingMode = TextWrappingModes.NoWrap;
            _signText.rectTransform.sizeDelta = new Vector2(2.6f, 0.5f);
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
            string status = _game.Playing ? $"진주 {_game.PearlsLeft}개 남음 · {_game.Score}점"
                : _best < 0 ? $"분수 물을 보고 {KeyBindings.Label(GameAction.Interact)}" : $"최고 {_best}점";
            _signText.SetText($"인어 분수 진주 던지기\n<size=58%>{status}</size>");
        }

        private void Notice(string text, float seconds)
        {
            _notice = text;
            _noticeTimer = seconds;
        }

        private void UpdateHint()
        {
            string text = null;
            if (_game.Playing)
                text = $"진주 {_game.PearlsLeft}/{FountainToss.PearlCount}  ·  {_game.Score}점\n"
                     + $"<size=80%>물 위 빛 고리를 보고 {KeyBindings.Label(GameAction.Interact)}  ·  작은 고리 3점 · 중간 2점 · 큰 고리 1점  ·  고리가 도는 앞쪽을 노려요</size>";
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
            var go = new GameObject("FountainTossHint", typeof(RectTransform));
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
            Place(back.rectTransform, new Vector2(1160f, 104f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(1130f, 96f));
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
