using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 진화 3 · 4단계 모듈 속 동물 · 꿀벌 움직임 (사용자 2026-10-27 "움직이면서 서로를 뚫고 지나가는 일 없도록", StationArtBuilder가 모델의
    /// "Critter_{모듈}_{번호}_{종류}" · "Bee_{모듈}_{번호}" 부품을 찾아 자동 배선 — 부품 원점 = 동물 자리, 머리 방향은 메시에 구워져 있어
    /// 에디터에서 메시 모양으로 앞쪽을 구해 둠):
    /// - 오리 · 백조 = 물 위 작은 타원을 천천히 돎 (머리 쪽으로 출발), 새끼 오리 = 어미가 지나간 자리를 처음 간격 그대로 줄지어 따라감
    /// - 거북 = 더 느리게 떠돎, 물고기 = 물속에서 기다렸다 제자리 위로 뛰어올라 포물선, 날치 = 길고 낮게 미끄러지듯
    /// - 나비 = 제 둘레를 팔랑 (날개 = 위아래로 접었다 폄), 사슴 = 고개 숙여 풀 뜯기 · 가끔 돌아봄, 토끼 = 제자리 깡충 · 방향 바꿈
    /// - 물범 = 숨 쉬며 가끔 고개 듦, 게 = 옆으로 종종걸음, 부엉이 = 휙 돌아봄, 고슴도치 = 킁킁
    /// - 꿀벌 = 정자 ↔ 벌집 입구 고리 길을 같은 속도 · 간격으로 날고, 꽃 곁 벌은 제자리 맴돌기, 꽃 위 벌은 쉬다 가끔 날개를 떪
    /// - 모두: 가까워지면 서로 밀어내 간격 유지 (밀린 만큼은 천천히 제 길로 돌아옴)
    /// 움직임은 게임 시간 (모듈과 함께 멈추지 않음 — 살아 있는 동물). 고스트(배치 미리보기)에서는 멈춤. 동물이 하나도 화면에 없으면 계산하지 않음.
    /// </summary>
    public sealed class CritterFx : MonoBehaviour
    {
        public enum Kind { Swim, Follow, Drift, Leap, Glide, Flutter, Graze, Hop, Bask, Scuttle, Perch, Snuffle, BeeLoop, BeeHover, BeeRest }

        [Serializable]
        public struct Critter
        {
            public Transform Part;
            public Kind Kind;
            [Tooltip("앞쪽 (부모 기준 가로 단위 벡터, 게는 옆걸음 방향)")]
            public Vector3 Heading;
            [Tooltip("간격 유지 반지름")]
            public float Radius;
            [Tooltip("따라가는 어미 (Follow만, 이 배열 번호)")]
            public int Leader;
            [Tooltip("헤엄 · 떠돎: 도는 쪽 ±1 × 크기 (0 = 제자리에서 출렁임만) / 게: 갈 수 있는 쪽 (+1 앞쪽만 · −1 뒤쪽만 · 2 양쪽 · 0 안 감) — 에디터가 물가 · 바위를 피해 고름")]
            public float Side;
            [Tooltip("날개 (꿀벌 Fx 자식 — 떨림)")]
            public Transform Wings;
        }

        [SerializeField] private Critter[] _critters = Array.Empty<Critter>();
        [Tooltip("꿀벌 고리 길 (부모 기준): 가운데 x · z, 반지름 x · z")]
        [SerializeField] private Vector4 _beeLoop;
        [Tooltip("고리 길 높이: 기본 · 출렁임 (sin 3θ) · 정자 쪽 오름 · 오름이 시작되는 x (이보다 작으면 오름)")]
        [SerializeField] private Vector4 _beeLoopHeight;
        [Tooltip("꿀벌 비행 속도 (모델 단위/초) — 모두 같은 속도라 간격이 유지됨")]
        [SerializeField] private float _beeSpeed = 0.14f;
        [Tooltip("밀어내기 여유")]
        [SerializeField] private float _gap = 0.006f;

        private struct State
        {
            public Vector3 Home;
            public Quaternion Rot;
            public Vector3 Scale;
            public Vector3 WingScale;
            public float Seed;
            public Vector3 Pos;    // 이번 프레임 제 길 위치 (밀림 전)
            public Vector3 Offset; // 밀린 만큼
            public bool Solid;     // 이번 프레임 밀어내기에 참여 (물속에 숨은 물고기는 빠짐)
            public float Theta;    // 꿀벌 고리 각
            public float Lift;     // 꿀벌: 고리 높이와 처음 자리 높이 차
            public Vector3 Tangent0;
            public float Pitch0;
            public float Lag;      // 새끼: 어미 뒤 거리
        }

        private State[] _s;
        private Quaternion[] _rots;
        private Vector3[] _scales;
        private Vector3[] _wings;
        private Renderer[] _renderers;
        private Dictionary<int, List<Vector3>> _trails;
        private Dictionary<int, float> _trailLength;
        private bool _built;
        private float _t0;

        public int Count => _critters.Length;

        private void Start()
        {
            var view = GetComponentInParent<ModuleView>();
            if (view != null && view.Module != null)
                Play();
        }

        /// <summary>움직임 준비 (Start가 부름 — 확인용으로 미리보기에서 직접 불러도 됨).</summary>
        public void Play()
        {
            if (_built)
                return;
            _built = true;
            _t0 = Time.time;
            int n = _critters.Length;
            _s = new State[n];
            _rots = new Quaternion[n];
            _scales = new Vector3[n];
            _wings = new Vector3[n];
            _renderers = new Renderer[n];
            for (int i = 0; i < n; i++)
            {
                var c = _critters[i];
                if (c.Part == null)
                    continue;
                ref var s = ref _s[i];
                s.Home = c.Part.localPosition;
                s.Rot = c.Part.localRotation;
                s.Scale = c.Part.localScale;
                s.WingScale = c.Wings != null ? c.Wings.localScale : Vector3.one;
                s.Seed = UnityEngine.Random.value * 100f;
                s.Pos = s.Home;
                _renderers[i] = c.Part.GetComponent<Renderer>();
                if (c.Kind == Kind.BeeLoop)
                {
                    s.Theta = Mathf.Atan2((s.Home.z - _beeLoop.y) / _beeLoop.w, (s.Home.x - _beeLoop.x) / _beeLoop.z);
                    s.Lift = s.Home.y - LoopPoint(s.Theta).y;
                    LoopSlope(s.Theta, out s.Tangent0, out s.Pitch0);
                }
            }
            // 새끼 오리 줄: 어미 길 자국 = 처음 줄 (맨 끝 새끼 → … → 어미, 오래된 것부터) — 처음엔 모두 제자리
            _trails = new Dictionary<int, List<Vector3>>();
            _trailLength = new Dictionary<int, float>();
            for (int i = 0; i < n; i++)
            {
                if (_critters[i].Kind != Kind.Follow || _critters[i].Part == null)
                    continue;
                int leader = _critters[i].Leader;
                if (leader < 0 || leader >= n || _critters[leader].Part == null)
                    continue;
                if (!_trails.TryGetValue(leader, out var trail))
                {
                    trail = new List<Vector3> { _s[leader].Home };
                    _trails[leader] = trail;
                }
                float lag = 0f;
                for (int k = 1; k < trail.Count; k++)
                    lag += Flat(trail[k] - trail[k - 1]).magnitude;
                lag += Flat(_s[i].Home - trail[0]).magnitude;
                trail.Insert(0, _s[i].Home);
                _s[i].Lag = lag;
                _trailLength[leader] = lag;
            }
        }

        private void Update()
        {
            if (!_built)
                return;
            bool seen = false;
            foreach (var r in _renderers)
            {
                if (r != null && r.isVisible)
                {
                    seen = true;
                    break;
                }
            }
            if (seen)
                Step(Time.deltaTime, Time.time - _t0);
        }

        /// <summary>한 프레임 진행 (t = 시작부터 게임 초 — 확인용으로 직접 불러도 됨).</summary>
        public void Step(float dt, float t)
        {
            if (!_built)
                Play();
            int n = _critters.Length;
            for (int i = 0; i < n; i++)
            {
                if (_critters[i].Part == null || _critters[i].Kind == Kind.Follow)
                    continue;
                Pose(i, dt, t, out _rots[i], out _scales[i], out _wings[i]);
            }
            UpdateTrails();
            for (int i = 0; i < n; i++)
            {
                if (_critters[i].Part != null && _critters[i].Kind == Kind.Follow)
                    Follow(i, t, out _rots[i], out _scales[i], out _wings[i]);
            }
            Separate(dt);
            for (int i = 0; i < n; i++)
            {
                var c = _critters[i];
                if (c.Part == null)
                    continue;
                c.Part.localPosition = _s[i].Pos + _s[i].Offset;
                c.Part.localRotation = _rots[i];
                c.Part.localScale = Vector3.Scale(_s[i].Scale, _scales[i]);
                if (c.Wings != null)
                    c.Wings.localScale = Vector3.Scale(_s[i].WingScale, _wings[i]);
            }
        }

        private void Pose(int i, float dt, float t, out Quaternion rot, out Vector3 scale, out Vector3 wing)
        {
            var c = _critters[i];
            ref var s = ref _s[i];
            var h = c.Heading.sqrMagnitude > 1e-6f ? c.Heading.normalized : Vector3.right;
            float seed = s.Seed;
            float yaw = 0f, pitch = 0f, roll = 0f;
            var pos = s.Home;
            scale = Vector3.one;
            wing = Vector3.one;
            s.Solid = true;
            switch (c.Kind)
            {
                case Kind.Swim:
                case Kind.Drift:
                {
                    // 물 위 작은 타원 (머리 쪽으로 출발, 앞뒤로 길게 — 도는 쪽 · 크기는 에디터가 물가를 피해 고름): 거북은 더 작고 느리게
                    bool turtle = c.Kind == Kind.Drift;
                    _trailLength.TryGetValue(i, out float chain);
                    SwimSize(turtle, c.Radius, chain, Mathf.Abs(c.Side), out float ra, out float rb);
                    float side = c.Side < 0f ? -1f : 1f;
                    float yawSway = 0f;
                    if (ra > 1e-4f)
                    {
                        float w = Mathf.Max(c.Radius, 0.01f) * (turtle ? 0.1f : 0.28f) / ra;
                        float ph = w * t + 0.6f * w / 0.21f * (Mathf.Sin(0.21f * t + seed) - Mathf.Sin(seed)); // 빨라졌다 느려졌다 (멈추거나 거꾸로 가지는 않음)
                        pos = s.Home + SwimOffset(h, side, ra, rb, ph);
                        yaw = Vector3.SignedAngle(h, SwimOffset(h, side, ra, rb, ph + 0.01f) - SwimOffset(h, side, ra, rb, ph - 0.01f), Vector3.up);
                    }
                    else
                        yawSway = 12f * Mathf.Sin(0.4f * t + seed); // 갈 데가 없으면 제자리에서 천천히 두리번
                    pos.y += (turtle ? 0.001f : 0.0015f) * Mathf.Sin((turtle ? 0.9f : 2.1f) * t + seed);
                    yaw += yawSway + (turtle ? 3f * Mathf.Sin(0.9f * t + seed) : 0f);
                    roll = (turtle ? 1f : 2.5f) * Mathf.Sin(1.3f * t + seed);
                    pitch = 1.5f * Mathf.Sin(2.1f * t + seed + 1f);
                    break;
                }
                case Kind.Leap:
                case Kind.Glide:
                {
                    // 물속에서 기다렸다 뛰어오름: 처음 자리 = 뛰어오른 한가운데쯤 (모델은 뛰는 모습), 물에 들고 날 때 작아져 사라짐
                    bool glide = c.Kind == Kind.Glide;
                    float period = 5f + 3f * Frac(seed * 0.618f), dur = glide ? 1.6f : 0.9f;
                    float len = glide ? 0.18f : 0.07f, high = glide ? 0.025f : 0.02f, depth = 0.04f;
                    float u = Mathf.Repeat(t + seed, period);
                    if (u < dur)
                    {
                        float k = u / dur, arc = Mathf.Sin(Mathf.PI * k);
                        pos = s.Home + h * ((k - 0.5f) * len) + Vector3.up * ((high + depth) * arc - depth);
                        pitch = glide ? Mathf.Lerp(12f, -30f, k) : Mathf.Lerp(30f, -60f, k);
                        scale = Vector3.one * (Smooth(0f, 0.12f, k) * Smooth(1f, 0.88f, k));
                    }
                    else
                    {
                        pos = s.Home - Vector3.up * depth;
                        scale = Vector3.zero;
                    }
                    s.Solid = scale.x > 0.5f;
                    break;
                }
                case Kind.Flutter:
                    // 제 둘레를 팔랑: 날개 = 몸 위로 접힌 판이라 위아래로 납작하게 했다 펴면 날갯짓
                    pos = s.Home + new Vector3(0.022f * Mathf.Sin(0.9f * t + seed), 0.01f * Mathf.Sin(1.7f * t + 2f * seed), 0.018f * Mathf.Sin(1.1f * t + 3f * seed));
                    yaw = 30f * Mathf.Sin(0.5f * t + seed);
                    scale = new Vector3(1f, 1f - 0.45f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 4.2f * t + seed)), 1f);
                    break;
                case Kind.Graze:
                {
                    // 고개 숙여 풀 뜯기 (오물오물) → 고개 들고 → 가끔 옆을 돌아봄
                    float period = 9f + 4f * Frac(seed * 0.618f);
                    Cycle(t, seed, period, out int cyc, out float u);
                    float graze = Window(u, 0.08f, 0.45f, 0.07f);
                    pitch = -11f * graze + 2f * graze * Mathf.Sin(2f * Mathf.PI * 2.2f * t);
                    yaw = Window(u, 0.58f, 0.82f, 0.05f) * (Hash(cyc, seed) < 0.5f ? -28f : 28f);
                    scale = new Vector3(1f, 1f + 0.012f * Mathf.Sin(1.6f * t + seed), 1f);
                    break;
                }
                case Kind.Hop:
                {
                    // 제자리 깡충 두 번 (뛰는 동안 늘어남) + 그때마다 방향을 조금 바꿈
                    float period = 3f + 3f * Frac(seed * 0.618f);
                    Cycle(t, seed, period, out int cyc, out float u);
                    float us = u * period, k = -1f;
                    if (us < 0.4f)
                        k = us / 0.4f;
                    else if (us > 0.5f && us < 0.9f)
                        k = (us - 0.5f) / 0.4f;
                    if (k >= 0f)
                    {
                        float arc = Mathf.Sin(Mathf.PI * k);
                        pos.y += 0.013f * arc;
                        scale = new Vector3(1f - 0.05f * arc, 1f + 0.12f * arc, 1f - 0.05f * arc);
                    }
                    float from = (Hash(cyc - 1, seed) - 0.5f) * 80f, to = (Hash(cyc, seed) - 0.5f) * 80f;
                    yaw = Mathf.Lerp(from, to, Smooth(0f, 0.9f, us));
                    break;
                }
                case Kind.Bask:
                {
                    // 숨 쉬며 들썩 + 가끔 고개 들고 두리번
                    float breath = Mathf.Sin(1.3f * t + seed);
                    scale = new Vector3(1f + 0.012f * breath, 1f + 0.03f * breath, 1f + 0.012f * breath);
                    float period = 8f + 5f * Frac(seed * 0.618f);
                    Cycle(t, seed, period, out int cyc, out float u);
                    float raise = Window(u, 0.2f, 0.5f, 0.08f);
                    pitch = 9f * raise;
                    yaw = raise * (Hash(cyc, seed) < 0.5f ? -10f : 10f);
                    break;
                }
                case Kind.Scuttle:
                {
                    // 옆으로 종종걸음 갔다 돌아옴 (한 번은 이쪽, 다음엔 저쪽), 걷는 동안 다리 떨림
                    float period = 4f + 3f * Frac(seed * 0.618f);
                    Cycle(t, seed, period, out int cyc, out float u);
                    float sign = c.Side > 1.5f ? ((cyc & 1) == 0 ? 1f : -1f) : c.Side > 0.5f ? 1f : c.Side < -0.5f ? -1f : 0f; // 막힌 쪽으로는 안 감
                    float go = Smooth(0.1f, 0.22f, u) - Smooth(0.5f, 0.62f, u);
                    pos = s.Home + h * (ScuttleReach * sign * go);
                    bool moving = sign != 0f && ((u > 0.1f && u < 0.22f) || (u > 0.5f && u < 0.62f));
                    yaw = moving ? 5f * Mathf.Sin(2f * Mathf.PI * 7f * t) : 0f;
                    break;
                }
                case Kind.Perch:
                {
                    // 휙 돌아보고 잠시 뒤 제자리로
                    float period = 5f + 4f * Frac(seed * 0.618f);
                    Cycle(t, seed, period, out int cyc, out float u);
                    float look = Window(u, 0.3f, 0.62f, 0.04f);
                    float far = Hash(cyc + 17, seed) < 0.3f ? 75f : 40f;
                    yaw = look * (Hash(cyc, seed) < 0.5f ? -far : far);
                    pos.y += 0.0015f * Mathf.Sin(1.1f * t + seed);
                    scale = new Vector3(1f, 1f + 0.01f * Mathf.Sin(1.4f * t + seed), 1f);
                    break;
                }
                case Kind.Snuffle:
                    yaw = 9f * Mathf.Sin(1.3f * t + seed) + 4f * Mathf.Sin(3.7f * t + 2f * seed);
                    float sniff = 0.02f * Mathf.Sin(2.4f * t + seed);
                    scale = new Vector3(1f + sniff, 1f, 1f + sniff);
                    break;
                case Kind.BeeLoop:
                {
                    // 고리 길을 같은 속도로 (가로 거리 기준) — 앞쪽 · 기울기는 처음 자리 접선에서 바뀐 만큼
                    float speed = Flat(LoopDerivative(s.Theta)).magnitude;
                    s.Theta += _beeSpeed * dt / Mathf.Max(speed, 1e-3f);
                    pos = LoopPoint(s.Theta) + Vector3.up * (s.Lift + 0.006f * Mathf.Sin(2.3f * t + seed));
                    LoopSlope(s.Theta, out var tan, out float slope);
                    yaw = Vector3.SignedAngle(s.Tangent0, tan, Vector3.up);
                    h = s.Tangent0;
                    pitch = Mathf.Clamp(slope - s.Pitch0, -35f, 35f);
                    wing = Buzz(t, seed, 1f);
                    break;
                }
                case Kind.BeeHover:
                    pos = s.Home + new Vector3(0.02f * Mathf.Sin(1.8f * t + seed), 0.008f * Mathf.Sin(2.9f * t + 2f * seed), 0.014f * Mathf.Sin(3.6f * t + seed));
                    yaw = 14f * Mathf.Sin(0.9f * t + seed);
                    wing = Buzz(t, seed, 1f);
                    break;
                case Kind.BeeRest:
                {
                    float period = 7f + 4f * Frac(seed * 0.618f);
                    Cycle(t, seed, period, out _, out float u);
                    pos.y += 0.0008f * Mathf.Sin(1.5f * t + seed);
                    wing = Buzz(t, seed, Window(u, 0.1f, 0.18f, 0.02f));
                    break;
                }
            }
            s.Pos = pos;
            rot = Orient(s.Rot, h, yaw, pitch, roll);
        }

        /// <summary>새끼 오리: 어미 길 자국에서 처음 간격만큼 뒤 자리 (높이는 제 높이), 앞쪽 = 자국 방향.</summary>
        private void Follow(int i, float t, out Quaternion rot, out Vector3 scale, out Vector3 wing)
        {
            var c = _critters[i];
            ref var s = ref _s[i];
            scale = Vector3.one;
            wing = Vector3.one;
            s.Solid = true;
            var h = c.Heading.sqrMagnitude > 1e-6f ? c.Heading.normalized : Vector3.right;
            if (!_trails.TryGetValue(c.Leader, out var trail))
            {
                s.Pos = s.Home;
                rot = s.Rot;
                return;
            }
            var head = _s[c.Leader].Pos;
            var prev = head;
            var pos = trail[0];
            var dir = trail.Count > 1 ? trail[1] - trail[0] : h;
            float d = 0f;
            for (int k = trail.Count - 1; k >= 0; k--)
            {
                float seg = Flat(prev - trail[k]).magnitude;
                if (seg > 1e-6f && d + seg >= s.Lag)
                {
                    pos = Vector3.Lerp(prev, trail[k], (s.Lag - d) / seg);
                    dir = prev - trail[k];
                    break;
                }
                d += seg;
                prev = trail[k];
            }
            pos.y = s.Home.y + 0.0015f * Mathf.Sin(2.4f * t + s.Seed);
            s.Pos = pos;
            dir = Flat(dir);
            float yaw = dir.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(h, dir, Vector3.up) : 0f;
            rot = Orient(s.Rot, h, yaw, 1.5f * Mathf.Sin(2.4f * t + s.Seed + 1f), 2.5f * Mathf.Sin(1.5f * t + s.Seed));
        }

        /// <summary>어미가 움직인 자리를 자국에 더하고, 새끼 줄 길이보다 오래된 것은 버림.</summary>
        private void UpdateTrails()
        {
            foreach (var kv in _trails)
            {
                var trail = kv.Value;
                var head = _s[kv.Key].Pos;
                if (Flat(head - trail[trail.Count - 1]).magnitude > 0.002f)
                    trail.Add(head);
                float keep = _trailLength[kv.Key] + 0.05f, sum = Flat(head - trail[trail.Count - 1]).magnitude;
                int cut = 0;
                for (int k = trail.Count - 1; k > 0; k--)
                {
                    sum += Flat(trail[k] - trail[k - 1]).magnitude;
                    if (sum > keep)
                    {
                        cut = k - 1;
                        break;
                    }
                }
                if (cut > 0)
                    trail.RemoveRange(0, cut);
            }
        }

        /// <summary>가까워진 둘을 서로 밀어냄 (땅 · 물 위 동물은 가로로만), 밀린 만큼은 천천히 줄어 제 길로 돌아옴.</summary>
        private void Separate(float dt)
        {
            int n = _critters.Length;
            float relax = Mathf.Exp(-dt * 0.7f);
            for (int i = 0; i < n; i++)
                _s[i].Offset *= relax;
            for (int i = 0; i < n; i++)
            {
                if (_critters[i].Part == null || !_s[i].Solid)
                    continue;
                for (int j = i + 1; j < n; j++)
                {
                    if (_critters[j].Part == null || !_s[j].Solid)
                        continue;
                    var d = (_s[i].Pos + _s[i].Offset) - (_s[j].Pos + _s[j].Offset);
                    float min = _critters[i].Radius + _critters[j].Radius + _gap;
                    float sq = d.sqrMagnitude;
                    if (sq >= min * min)
                        continue;
                    float dist = Mathf.Sqrt(sq);
                    var normal = dist > 1e-5f ? d / dist : Vector3.right;
                    var push = normal * ((min - dist) * 0.5f);
                    var pi = push;
                    var pj = -push;
                    if (!Flies(_critters[i].Kind))
                        pi.y = 0f;
                    if (!Flies(_critters[j].Kind))
                        pj.y = 0f;
                    _s[i].Offset += pi;
                    _s[j].Offset += pj;
                }
            }
        }

        private static bool Flies(Kind k) => k == Kind.Flutter || k == Kind.BeeLoop || k == Kind.BeeHover;

        /// <summary>게 옆걸음 거리.</summary>
        public const float ScuttleReach = 0.022f;

        /// <summary>헤엄 타원 반지름 (앞뒤 ra · 옆 rb) — 오리 · 백조는 몸의 1.4 · 0.6배, 거북 1 · 0.5배, 새끼 줄이 있으면 줄이 뭉치지 않게 앞뒤로 길게. scale = 에디터가 고른 크기.</summary>
        public static void SwimSize(bool turtle, float radius, float chain, float scale, out float ra, out float rb)
        {
            float r = Mathf.Max(radius, 0.01f);
            ra = r * (turtle ? 1f : 1.4f);
            rb = r * (turtle ? 0.5f : 0.6f);
            if (chain > 0f)
                ra = Mathf.Max(ra, 0.5f * chain);
            ra *= scale;
            rb *= scale;
        }

        /// <summary>헤엄 타원 위 자리 (처음 자리 기준): ph = 0이면 제자리, 처음엔 머리 쪽으로 나아가며 side 쪽(+1 = 왼쪽)으로 돎.</summary>
        public static Vector3 SwimOffset(Vector3 heading, float side, float ra, float rb, float ph)
        {
            var left = Vector3.Cross(heading, Vector3.up) * side;
            return heading * (ra * Mathf.Sin(ph)) + left * (rb * (1f - Mathf.Cos(ph)));
        }

        /// <summary>제자리 회전 + 부모 위쪽 축으로 yaw → 돌아간 앞쪽 기준 pitch (+ = 머리 듦) · roll (모두 도).</summary>
        private static Quaternion Orient(Quaternion baseRot, Vector3 heading, float yaw, float pitch, float roll)
        {
            var q = Quaternion.AngleAxis(yaw, Vector3.up);
            var fwd = q * heading;
            var side = Vector3.Cross(fwd, Vector3.up);
            if (side.sqrMagnitude > 1e-6f)
                q = Quaternion.AngleAxis(pitch, side.normalized) * q;
            q = Quaternion.AngleAxis(roll, fwd) * q;
            return q * baseRot;
        }

        /// <summary>꿀벌 날개 떨림 (위아래로 납작하게, amount 0 ~ 1).</summary>
        private static Vector3 Buzz(float t, float seed, float amount) =>
            new Vector3(1f, 1f - 0.32f * amount * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 11f * t + seed)), 1f);

        public Vector3 LoopPoint(float theta)
        {
            float x = _beeLoop.x + _beeLoop.z * Mathf.Cos(theta);
            float z = _beeLoop.y + _beeLoop.w * Mathf.Sin(theta);
            // 정자 쪽은 지붕 위로 오름 (Blender는 계단처럼 — 날 때는 짧게 이어 오름)
            float y = _beeLoopHeight.x + _beeLoopHeight.y * Mathf.Sin(3f * theta) + _beeLoopHeight.z * Smooth(_beeLoopHeight.w + 0.04f, _beeLoopHeight.w - 0.04f, x);
            return new Vector3(x, y, z);
        }

        private Vector3 LoopDerivative(float theta) => (LoopPoint(theta + 0.001f) - LoopPoint(theta - 0.001f)) / 0.002f;

        private void LoopSlope(float theta, out Vector3 tangent, out float pitch)
        {
            var d = LoopDerivative(theta);
            var flat = Flat(d);
            tangent = flat.sqrMagnitude > 1e-8f ? flat.normalized : Vector3.forward;
            pitch = Mathf.Atan2(d.y, flat.magnitude) * Mathf.Rad2Deg;
        }

        private static void Cycle(float t, float seed, float period, out int cycle, out float u)
        {
            float x = (t + seed * period) / period;
            cycle = Mathf.FloorToInt(x);
            u = x - cycle;
        }

        /// <summary>u가 a ~ b 안이면 1 (양끝 ramp 동안 부드럽게), 밖이면 0.</summary>
        private static float Window(float u, float a, float b, float ramp) => Smooth(a, a + ramp, u) * (1f - Smooth(b - ramp, b, u));

        private static float Smooth(float a, float b, float x)
        {
            float k = Mathf.Clamp01((x - a) / (b - a));
            return k * k * (3f - 2f * k);
        }

        private static float Frac(float x) => x - Mathf.Floor(x);

        private static float Hash(int i, float seed) => Frac(Mathf.Sin(i * 12.9898f + seed * 78.233f) * 43758.5453f);

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
