using SpaceStation.Audio;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 심우주 통신기지 체험 "전파 접시 신호 모으기" (사용자 2026-11-17 체험 1안, 값 = <see cref="DeepSpaceComm"/>):
    /// 뒤 능선 접시 넷은 평소 저마다 다른 쪽을 겨눔 → 접시 조작반 손바퀴를 보고 F = 손바퀴가 돌며 접시가 신호 별 쪽 (넷이 나란히 — 모델 그대로의 겨눔)으로 돌아섬 →
    /// 맞춘 접시마다 빛 알이 빛 케이블을 따라 가까운 옆 돔으로 달려가고 돔 꼭대기가 반짝임 · 신호 별이 밝아짐 →
    /// 넷을 다 맞추면 두 옆 돔에서 가운데 관측실로 신호 빛줄이 휘어 날아가고 돔 위로 신호 고리가 차례로 퍼지며 깊은 우주의 소리 (만든 소리 — 낮은 울림 · 맥동 별 박자 · 잡음)가 남 →
    /// 끝나면 접시가 천천히 흐트러진 자리로 돌아가 다시 할 수 있음. 위치는 템플릿 루트 (이 물체) 기준, 시간은 실제 시간. 템플릿 빌더가 configure에서 붙임.
    /// 색 대신 밝기 · 크기로 알림 (색약): 빛 알 · 돔 꼭대기 반짝임 · 신호 별 크기, 안내 줄에 맞춘 수.
    /// </summary>
    public sealed class InteriorDishSignal : MonoBehaviour
    {
        public const float TurnTime = 2.6f, CableTime = 1.7f, ArcTime = 2.2f, ShowSeconds = 13f, ReturnTime = 5f;
        private const int Rings = 6;
        private const float RingGap = 0.85f, RingLife = 3.4f;
        /// <summary>신호 고리가 가운데 돔 꼭대기에서 오르는 높이 (가장 넓을 때) — 2026-11-18 바닥을 0.78 m 올려 1.7 → 1.25 (하늘 면 0.55 m 아래).</summary>
        public const float RingRise = 1.25f;
        private static readonly Color SignalWhite = new Color(0.86f, 0.97f, 1f);

        [Tooltip("도는 받침 DishYaw{k} · 접시 DishTilt{k} · 손바퀴 DishWheel{k} (DeepSpaceComm 차례)")]
        [SerializeField] private Transform[] _yaws;
        [SerializeField] private Transform[] _tilts;
        [SerializeField] private Transform[] _wheels;
        [Tooltip("손바퀴 충돌체 (보고 F)")]
        [SerializeField] private Collider[] _wheelUses;
        [Tooltip("빛 알 · 신호 별 (빛 재질)")]
        [SerializeField] private Material _glowMaterial;
        [Tooltip("신호 빛줄 · 신호 고리 (선 재질)")]
        [SerializeField] private Material _lineMaterial;
        [SerializeField] private Material _sparkMaterial;
        [SerializeField] private TMP_FontAsset _font;

        private Quaternion[] _yawRot, _tiltRot, _wheelRot;
        private Vector3[] _tiltAxisLocal;
        private float[] _turn;          // 0 = 흐트러짐 · 1 = 신호 별 쪽
        private bool[] _aligned;
        private float[] _wheelSpin;     // 남은 돌림 시간
        private float[] _wheelAngle;
        private float[] _bead = { -1f, -1f, -1f, -1f };   // 케이블 빛 알 0 ~ 1 (−1 = 없음)
        private Vector3[][] _cablePath;
        private Transform[] _beads;
        private Light[] _domeLights;
        private float[] _domeFlash = new float[2];
        private int _gathered;
        private float _showTime = -1f, _returnTime = -1f;
        private Transform _star;
        private Light _starLight;
        private LineRenderer[] _arcs;
        private LineRenderer[] _rings;
        private ParticleSystem _sparks;
        private AudioSource _voice;
        private InteriorHintLine _hint;

        public bool Showing => _showTime >= 0f;
        public int Gathered => _gathered;
        public bool Aligned(int k) => _aligned != null && _aligned[k];
        /// <summary>접시 k가 신호 별 쪽으로 돌아선 정도 0 ~ 1 (시험용).</summary>
        public float Turn(int k) => _turn != null ? _turn[k] : 0f;

        public void Configure(Transform[] yaws, Transform[] tilts, Transform[] wheels, Collider[] wheelUses, Material glow, Material line, Material spark, TMP_FontAsset font)
        {
            _yaws = yaws;
            _tilts = tilts;
            _wheels = wheels;
            _wheelUses = wheelUses;
            _glowMaterial = glow;
            _lineMaterial = line;
            _sparkMaterial = spark;
            _font = font;
        }

        private void Awake()
        {
            int n = DeepSpaceComm.Dishes.Length;
            _yawRot = new Quaternion[n];
            _tiltRot = new Quaternion[n];
            _wheelRot = new Quaternion[n];
            _tiltAxisLocal = new Vector3[n];
            _turn = new float[n];
            _aligned = new bool[n];
            _wheelSpin = new float[n];
            _wheelAngle = new float[n];
            _cablePath = new Vector3[n][];
            _beads = new Transform[n];
            for (int k = 0; k < n; k++)
            {
                if (_yaws[k] == null || _tilts[k] == null)
                    continue;
                // 접시는 받침과 함께 돎 (FBX에서는 따로 조각 — 받침 아래로 옮김)
                _tilts[k].SetParent(_yaws[k], true);
                _yawRot[k] = _yaws[k].localRotation;
                _tiltRot[k] = _tilts[k].localRotation;
                _tiltAxisLocal[k] = Quaternion.Inverse(_yaws[k].localRotation) * DeepSpaceComm.TiltAxis;
                if (_wheels[k] != null)
                    _wheelRot[k] = _wheels[k].localRotation;
                _cablePath[k] = CablePath(k);
                _beads[k] = GlowBall("SignalBead" + k, 0.07f);
                _beads[k].gameObject.SetActive(false);
                if (_wheelUses != null && k < _wheelUses.Length && _wheelUses[k] != null)
                {
                    int dish = k;
                    var use = _wheelUses[k].gameObject.AddComponent<InteriorInteractable>();
                    use.Tap = true;
                    use.Prompt = () => "접시를 신호 별 쪽으로 맞추기  <size=80%>(" + _gathered + " / " + _aligned.Length + " · 넷을 다 맞추면 가운데 관측실로 신호가 모여요)</size>";
                    use.Blocked = () => Showing ? "깊은 우주 신호 받는 중" : _returnTime >= 0f ? "접시가 제자리로 돌아가는 중" : _aligned[dish] ? $"이미 맞춘 접시예요  ({_gathered} / {_aligned.Length})" : null;
                    use.Completed = () => Align(dish);
                }
            }
            _domeLights = new Light[DeepSpaceComm.SideDomes.Length];
            for (int d = 0; d < _domeLights.Length; d++)
                _domeLights[d] = PointLight("DomeSignal" + d, DeepSpaceComm.SideDomeTop(d) + Vector3.up * 0.3f, 3.2f);
            _star = GlowBall("SignalStar", 0.06f);
            _star.localPosition = DeepSpaceComm.SignalStar;
            _starLight = PointLight("SignalStarLight", DeepSpaceComm.SignalStar, 4f);
            _arcs = new LineRenderer[DeepSpaceComm.SideDomes.Length];
            for (int d = 0; d < _arcs.Length; d++)
                _arcs[d] = NewLine("SignalArc" + d, 26, false, 0.09f);
            _rings = new LineRenderer[Rings];
            for (int r = 0; r < Rings; r++)
                _rings[r] = NewLine("SignalRing" + r, 56, true, 0.06f);
            for (int k = 0; k < n; k++)
                ApplyDish(k);
            _hint = new InteriorHintLine(transform, "DishSignalHint", _font);
        }

        private void OnDisable()
        {
            _hint?.Show(null);
            if (_voice != null)
                _voice.Stop();
        }

        private void OnDestroy() => _hint?.Destroy();

        /// <summary>손바퀴 k: 접시를 신호 별 쪽으로 (이미 맞췄거나 쇼 · 되돌아감 중이면 무시).</summary>
        public void Align(int k)
        {
            if (Showing || _returnTime >= 0f || _aligned[k])
                return;
            _aligned[k] = true;
            _wheelSpin[k] = TurnTime;
            AudioService.TryPlay(l => l.RepairStart);
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            for (int k = 0; k < _turn.Length; k++)
            {
                float want = _returnTime >= 0f ? 0f : _aligned[k] ? 1f : 0f;
                float speed = _returnTime >= 0f ? 1f / ReturnTime : 1f / TurnTime;
                float before = _turn[k];
                _turn[k] = Mathf.MoveTowards(_turn[k], want, dt * speed);
                if (_wheelSpin[k] > 0f)
                {
                    _wheelSpin[k] -= dt;
                    _wheelAngle[k] += dt * 300f * Mathf.Clamp01(_wheelSpin[k] / 0.6f + 0.2f);
                }
                if (before < 1f && _turn[k] >= 1f && _aligned[k])
                    StartBead(k);
                ApplyDish(k);
                MoveBead(k, dt);
            }
            if (_showTime >= 0f)
                StepShow(dt);
            if (_returnTime >= 0f)
            {
                _returnTime += dt;
                if (_returnTime >= ReturnTime)
                    _returnTime = -1f;
            }
            for (int d = 0; d < _domeFlash.Length; d++)
            {
                _domeFlash[d] = Mathf.Max(0f, _domeFlash[d] - dt / 1.6f);
                if (_domeLights[d] != null)
                    _domeLights[d].intensity = 2.4f * _domeFlash[d] + (Showing ? 0.8f : 0f);
            }
            ApplyStar();
            UpdateHint();
        }

        // ---------------- 접시 ----------------

        /// <summary>접시 k 자세: 흐트러진 자리 (받침 · 접시 각을 비킴) ↔ 모델 그대로 (신호 별 쪽), 부드럽게.</summary>
        private void ApplyDish(int k)
        {
            if (_yaws[k] == null || _tilts[k] == null)
                return;
            float e = Mathf.SmoothStep(0f, 1f, _turn[k]);
            _yaws[k].localRotation = Quaternion.AngleAxis(DeepSpaceComm.DishRestYaw[k] * (1f - e), Vector3.up) * _yawRot[k];
            _tilts[k].localRotation = Quaternion.AngleAxis(DeepSpaceComm.DishRestTilt[k] * (1f - e), _tiltAxisLocal[k]) * _tiltRot[k];
            if (_wheels[k] != null)
                _wheels[k].localRotation = Quaternion.AngleAxis(_wheelAngle[k], DeepSpaceComm.WheelAxis(k)) * _wheelRot[k];
        }

        /// <summary>빛 케이블 길 (땅을 따라 — 케이블 위 3 cm, 몸체 충돌 메시로 높이를 잼).</summary>
        private Vector3[] CablePath(int k)
        {
            const int steps = 16;
            var a = DeepSpaceComm.CableStart(k);
            var b = DeepSpaceComm.CableEnd(k);
            var pts = new Vector3[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                float y = DeepSpaceComm.Floor + 0.05f;
                var from = transform.TransformPoint(new Vector3(p.x, DeepSpaceComm.Floor + 1.2f, p.y));
                if (Physics.Raycast(from, -transform.up, out var hit, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    y = transform.InverseTransformPoint(hit.point).y;
                pts[i] = new Vector3(p.x, y + 0.08f, p.y);
            }
            return pts;
        }

        private void StartBead(int k)
        {
            _bead[k] = 0f;
            _beads[k].gameObject.SetActive(true);
            InteriorFireflyShow.Burst(EnsureSparks(), transform.TransformPoint(_cablePath[k][0]), 12, 0.6f);
            AudioService.TryPlay(l => l.UiTab);
        }

        private void MoveBead(int k, float dt)
        {
            if (_bead[k] < 0f)
                return;
            _bead[k] += dt / CableTime;
            var path = _cablePath[k];
            float f = Mathf.Clamp01(_bead[k]) * (path.Length - 1);
            int i = Mathf.Min(path.Length - 2, (int)f);
            var p = Vector3.Lerp(path[i], path[i + 1], f - i);
            _beads[k].localPosition = p;
            var ps = EnsureSparks();
            if (ps != null && Random.value < 0.7f)
                ps.Emit(new ParticleSystem.EmitParams { position = _beads[k].position, velocity = Random.insideUnitSphere * 0.12f + Vector3.up * 0.05f, startLifetime = 0.5f, startSize = 0.05f, startColor = SignalWhite, applyShapeToPosition = false }, 1);
            if (_bead[k] < 1f)
                return;
            _bead[k] = -1f;
            _beads[k].gameObject.SetActive(false);
            int dome = DeepSpaceComm.DishDome[k];
            _domeFlash[dome] = 1f;
            InteriorFireflyShow.Burst(EnsureSparks(), transform.TransformPoint(DeepSpaceComm.SideDomeTop(dome) + Vector3.up * 0.1f), 22, 0.9f);
            _gathered++;
            AudioService.TryPlay(l => _gathered >= _aligned.Length ? l.EventPositive : l.UiClick);
            if (_gathered >= _aligned.Length)
                BeginShow();
        }

        // ---------------- 신호 모임 쇼 ----------------

        private void BeginShow()
        {
            _showTime = 0f;
            PlaySignalSound();
        }

        private void StepShow(float dt)
        {
            _showTime += dt;
            float t = _showTime;
            // 옆 돔 둘 → 가운데 관측실: 휘어 날아가는 신호 빛줄 (머리만 환하고 꼬리는 옅게)
            for (int d = 0; d < _arcs.Length; d++)
            {
                float s = t / ArcTime;
                _arcs[d].enabled = s > 0f && s < 1.35f;
                if (_arcs[d].enabled)
                    DrawArc(_arcs[d], DeepSpaceComm.SideDomeTop(d) + Vector3.up * 0.15f, DeepSpaceComm.CenterDomeTop + Vector3.up * 0.1f, s);
            }
            if (t >= ArcTime && t - dt < ArcTime)
            {
                InteriorFireflyShow.Burst(EnsureSparks(), transform.TransformPoint(DeepSpaceComm.CenterDomeTop + Vector3.up * 0.15f), 40, 1.3f);
                AudioService.TryPlay(l => l.GradeUp);
            }
            // 돔 위 신호 고리: 차례로 위로 떠오르며 넓어지고 옅어짐
            for (int r = 0; r < _rings.Length; r++)
            {
                float age = t - ArcTime - r * RingGap;
                bool on = age > 0f && age < RingLife;
                _rings[r].enabled = on;
                if (on)
                    DrawRing(_rings[r], age / RingLife);
            }
            if (t < ShowSeconds)
                return;
            _showTime = -1f;
            foreach (var a in _arcs)
                a.enabled = false;
            foreach (var r in _rings)
                r.enabled = false;
            // 다시 할 수 있게: 접시가 천천히 흐트러진 자리로
            for (int k = 0; k < _aligned.Length; k++)
                _aligned[k] = false;
            _gathered = 0;
            _returnTime = 0f;
        }

        private void DrawArc(LineRenderer line, Vector3 from, Vector3 to, float s)
        {
            var mid = (from + to) * 0.5f + Vector3.up * 1.7f;
            int n = line.positionCount;
            float head = Mathf.Clamp01(s), tail = Mathf.Clamp01(s - 0.35f);
            for (int i = 0; i < n; i++)
            {
                float u = Mathf.Lerp(tail, head, i / (float)(n - 1));
                var p = Vector3.Lerp(Vector3.Lerp(from, mid, u), Vector3.Lerp(mid, to, u), u);
                line.SetPosition(i, transform.TransformPoint(p));
            }
            if (head < 1f)
            {
                var ps = EnsureSparks();
                if (ps != null)
                    ps.Emit(new ParticleSystem.EmitParams { position = line.GetPosition(n - 1), velocity = Random.insideUnitSphere * 0.2f, startLifetime = 0.6f, startSize = 0.08f, startColor = SignalWhite, applyShapeToPosition = false }, 1);
            }
        }

        private void DrawRing(LineRenderer line, float f)
        {
            float radius = Mathf.Lerp(0.35f, 2.6f, f);
            var c = DeepSpaceComm.CenterDomeTop + Vector3.up * Mathf.Lerp(0.15f, RingRise, Mathf.Sqrt(f));
            int n = line.positionCount;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                line.SetPosition(i, transform.TransformPoint(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius));
            }
            float alpha = Mathf.Clamp01(f * 6f) * (1f - f);
            line.widthMultiplier = 0.06f * (1f - 0.5f * f);
            line.startColor = line.endColor = new Color(SignalWhite.r, SignalWhite.g, SignalWhite.b, alpha);
        }

        /// <summary>신호 별: 맞춘 수만큼 커지고 밝아짐, 쇼 중엔 맥박.</summary>
        private void ApplyStar()
        {
            if (_star == null)
                return;
            float level = 0f;
            for (int k = 0; k < _turn.Length; k++)
                level += Mathf.SmoothStep(0f, 1f, _turn[k]);
            level /= _turn.Length;
            float beat = Showing ? 0.5f + 0.5f * Mathf.Sin(_showTime * 7f) : 0f;
            float size = 0.06f + 0.1f * level + 0.06f * beat;
            _star.localScale = Vector3.one * size;
            if (_starLight != null)
                _starLight.intensity = 0.3f + 1.6f * level + 1.4f * beat;
        }

        // ---------------- 깊은 우주의 소리 (만든 소리) ----------------

        private void PlaySignalSound()
        {
            if (_voice == null)
            {
                var go = new GameObject("DeepSpaceVoice");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = DeepSpaceComm.CenterDomeTop;
                _voice = go.AddComponent<AudioSource>();
                _voice.playOnAwake = false;
                _voice.spatialBlend = 0.6f;
                _voice.minDistance = 4f;
                _voice.maxDistance = 30f;
                _voice.clip = DeepSpaceClip();
            }
            _voice.volume = 0.55f * AudioVolumes.Effects;
            _voice.Play();
        }

        /// <summary>
        /// 깊은 우주 신호 (9초): 낮은 울림 (55 · 82 Hz, 천천히 일렁임) + 맥동 별 박자 (0.36초마다 짧은 높은 똑딱 — 점점 또렷) + 옅은 잡음 쉬익 (처음엔 크고 점점 걷힘) — 끝 2초는 스러짐.
        /// </summary>
        public static AudioClip DeepSpaceClip()
        {
            const int rate = 22050;
            const float length = 9f;
            int n = (int)(rate * length);
            var data = new float[n];
            var rng = new System.Random(1117);
            float noise = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Clamp01(t / 1.2f) * Mathf.Clamp01((length - t) / 2f);
                float drone = (0.55f * Mathf.Sin(2f * Mathf.PI * 55f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 82.4f * t + 0.6f)) * (0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.35f * t));
                float beat = (t % 0.36f);
                float clear = Mathf.Clamp01((t - 1f) / 4f);
                float tick = beat < 0.05f ? Mathf.Sin(2f * Mathf.PI * 1320f * t) * Mathf.Exp(-beat * 70f) * (0.15f + 0.45f * clear) : 0f;
                noise = noise * 0.86f + ((float)rng.NextDouble() * 2f - 1f) * 0.14f;
                float hiss = noise * (0.35f - 0.25f * clear) * (0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * 0.8f * t));
                data[i] = (drone * 0.45f + tick + hiss) * env * 0.8f;
            }
            var clip = AudioClip.Create("DeepSpaceSignal", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ---------------- 만들기 · 안내 ----------------

        private Transform GlowBall(string name, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * size;
            var rend = go.GetComponent<MeshRenderer>();
            rend.sharedMaterial = _glowMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        private Light PointLight(string name, Vector3 at, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.range = range;
            light.intensity = 0f;
            light.color = SignalWhite;
            return light;
        }

        private LineRenderer NewLine(string name, int points, bool loop, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = points;
            line.sharedMaterial = _lineMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.widthMultiplier = width;
            if (!loop)
            {
                line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(1f, 1f));
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(0.55f, 0.8f, 1f), 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 1f) });
                line.colorGradient = g;
            }
            line.enabled = false;
            return line;
        }

        private ParticleSystem EnsureSparks()
        {
            if (_sparks == null)
                _sparks = InteriorFireflyShow.CreateSparks(transform, _sparkMaterial);
            return _sparks;
        }

        private void UpdateHint()
        {
            if (Showing)
            {
                _hint.Show(_showTime < ArcTime ? "신호 넷이 가운데 관측실로 모이는 중…" : "깊은 우주에서 온 신호를 받았어요  <size=80%>(돔 위 신호 고리)</size>");
                return;
            }
            bool busy = false;
            for (int k = 0; k < _turn.Length; k++)
                busy |= _aligned[k] && (_turn[k] < 1f || _bead[k] >= 0f);
            _hint.Show(busy ? $"신호 모으는 중  ·  {_gathered} / {_aligned.Length}" : null);
        }
    }
}
