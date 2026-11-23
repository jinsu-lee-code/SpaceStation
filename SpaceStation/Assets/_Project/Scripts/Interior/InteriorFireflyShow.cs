using System.Collections.Generic;
using SpaceStation.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 수리 사령부 체험 ② "경종 → 반딧불 드론 등불 쇼" (TASKS 2026-11-13, 사용자 2026-11-15 "체험으로 넘어가자", 순서 = <see cref="FireflyLanternShow"/>, 값 = <see cref="RepairCommand"/>):
    /// 나무집 마루의 종 줄 손잡이를 보고 F → 지붕 종각 금빛 경종이 흔들리며 울리고, 켜져 있던 가지 등불이 스러짐 → 공방 둘에서 반딧불 드론 떼 (진주 몸 · 빛나는 꼬리)가 나와
    /// 고목 둘레를 한 바퀴 돌며, 떼가 지나는 가지 등불마다 드론 하나가 날아가 켜고 돌아옴 → 나선으로 관측대 위까지 올라 반짝이로 흩어졌다가 공방으로 돌아감.
    /// 등불 밝기 = 재질 블록 (희미한 불씨 ↔ 환히, 색 대신 밝기), 잎 구름 아래 등도 같이 밝아짐. 등불은 쇼가 끝나도 켜진 채 (방을 나가면 꺼짐). 실제 시간.
    /// </summary>
    public sealed class InteriorFireflyShow : MonoBehaviour
    {
        private const float DimEmission = 0.03f;        // 꺼진 등불 = 불씨 (발광 × 0.03 — 0.12는 점등 빛을 받은 흰 유리라 멀리서 켜진 것과 같아 보였음)
        private const float BellSwing = 14f, BellDecay = 0.9f, BellPeriod = 1.3f, RopePull = 0.12f;
        private const float DroneBody = 0.3f, DroneGlow = 0.16f;
        private const float HaloSize = 0.6f;             // 드론마다 꼬리 빛 둘레 빛무리 (가산 반짝이 한 알 — 멀리서도 반딧불로 보이게, 몸 0.26 m만으로는 12 m 밖에서 안 보였음)
        private const float TrailRate = 7f;              // 드론마다 초당 반짝이
        private const float LitEmission = 1.8f;          // 켜진 등불 = 재질 발광 × 1.8 (블룸), 꺼진 등불 = DimEmission · 몸 색 × 0.12 (어두운 유리)
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly Vector3[] CanopyLights = { new Vector3(13f, 7.2f, 6f), new Vector3(3f, 7.2f, 4f) };
        private static readonly Color SparkWarm = new Color(1f, 0.9f, 0.55f);

        [Tooltip("지붕 종각 금빛 경종 (원점 = 매달린 꼭대기)")]
        [SerializeField] private Transform _bell;
        [Tooltip("종 줄 (원점 = 마루 위 손잡이 고리)")]
        [SerializeField] private Transform _bellRope;
        [Tooltip("손잡이 충돌체 (보고 F = 종 울리기, 종 줄 자식)")]
        [SerializeField] private Collider _handle;
        [Tooltip("가지 등불 빛 구슬 (RepairCommand.Lanterns 차례)")]
        [SerializeField] private Transform[] _lanterns;
        [Tooltip("드론 몸 (진주)")]
        [SerializeField] private Material _bodyMaterial;
        [Tooltip("드론 꼬리 빛 (발광)")]
        [SerializeField] private Material _glowMaterial;
        [Tooltip("꼬리 · 등불 · 흩어질 때 반짝이 (M_FireworkSpark)")]
        [SerializeField] private Material _sparkMaterial;
        [SerializeField] private TMP_FontAsset _font;

        private sealed class Drone
        {
            public Transform Root, Glow;
            public float A, B, C;        // −0.5 ~ 0.5 흩어짐 (떼 안 자리 · 공방 안 자리)
            public float Workshop;       // 공방 각
            public Vector3 Pos, FinaleFrom, ReturnFrom;
        }

        private FireflyLanternShow _show;
        private readonly List<Drone> _drones = new List<Drone>();
        private readonly List<int> _lit = new List<int>();
        private Renderer[] _lanternRenderers;
        private Vector3[] _lanternBaseScale;
        private Color _baseColor, _baseEmission;
        private MaterialPropertyBlock _block;
        private readonly List<(Light Light, float Intensity)> _canopy = new List<(Light, float)>();
        private Quaternion _bellRest;
        private Vector3 _ropeBase;
        private float _bellT = 99f;
        private int _rings;
        private ParticleSystem _sparks;
        private FireflyLanternShow.Phase _phaseShown;
        private string _notice;
        private float _noticeTimer;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public FireflyLanternShow Show => _show;
        public int DroneCount => _drones.Count;

        public void Configure(Transform bell, Transform bellRope, Collider handle, Transform[] lanterns, Material body, Material glow, Material spark, TMP_FontAsset font)
        {
            _bell = bell;
            _bellRope = bellRope;
            _handle = handle;
            _lanterns = lanterns;
            _bodyMaterial = body;
            _glowMaterial = glow;
            _sparkMaterial = spark;
            _font = font;
        }

        private void Awake()
        {
            var angles = new float[RepairCommand.Lanterns.Length];
            for (int k = 0; k < angles.Length; k++)
                angles[k] = RepairCommand.AngleAroundTree(RepairCommand.Lanterns[k]);
            _show = new FireflyLanternShow(angles);
            _block = new MaterialPropertyBlock();
            if (_bell != null)
                _bellRest = _bell.localRotation;
            if (_bellRope != null)
                _ropeBase = _bellRope.localPosition;
            if (_handle != null)
            {
                var use = _handle.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "경종 울리기  <size=80%>(반딧불 드론이 가지 등불을 켜요)</size>";
                use.Blocked = () => _show.Running ? "반딧불 드론이 등불을 켜는 중이에요" : null;
                use.Completed = Ring;
            }
            int n = _lanterns != null ? _lanterns.Length : 0;
            _lanternRenderers = new Renderer[n];
            _lanternBaseScale = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                if (_lanterns[k] == null)
                    continue;
                _lanternRenderers[k] = _lanterns[k].GetComponent<Renderer>();
                _lanternBaseScale[k] = _lanterns[k].localScale;
                if (_lanternRenderers[k] != null && _lanternRenderers[k].sharedMaterial != null && _baseEmission == default)
                {
                    var m = _lanternRenderers[k].sharedMaterial;
                    _baseColor = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
                    _baseEmission = m.HasProperty(EmissionId) ? m.GetColor(EmissionId) : Color.white;
                }
            }
            foreach (var l in GetComponentsInChildren<Light>(true))
            {
                var p = transform.InverseTransformPoint(l.transform.position);
                foreach (var c in CanopyLights)
                    if ((p - c).sqrMagnitude < 0.25f)
                        _canopy.Add((l, l.intensity));
            }
            BuildDrones();
        }

        private void OnDisable()
        {
            // 방을 나가면 쇼를 접고 등불을 끔 (다음에 들어오면 종부터)
            _show?.Reset();
            _bellT = 99f;
            _noticeTimer = 0f;
            foreach (var d in _drones)
                if (d.Root != null)
                    d.Root.gameObject.SetActive(false);
            if (_sparks != null)
                _sparks.Clear();
            ApplyBell();
            ApplyLanterns();
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        private void Ring()
        {
            if (!_show.Start())
                return;
            _bellT = 0f;
            _rings = 0;
            _phaseShown = FireflyLanternShow.Phase.Ring;
            foreach (var d in _drones)
            {
                d.Pos = Home(d);
                d.Root.gameObject.SetActive(true);
            }
            Notice("경종이 울려요  ·  반딧불 드론이 공방에서 날아올라 가지 등불을 켜요", 4f);
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_noticeTimer > 0f)
                _noticeTimer -= dt;
            _bellT += dt;
            // 딩 — 딩 (종이 흔들리는 동안 두 번)
            if (_bellT < FireflyLanternShow.RingTime && _rings < 2 && _bellT >= _rings * 1.1f)
            {
                _rings++;
                AudioService.TryPlayAt(l => l.RcmdBellToll, _bell != null ? _bell.position : transform.TransformPoint(RepairCommand.Bell));
            }
            if (_show.Running)
            {
                _lit.Clear();
                _show.Step(dt, _lit);
                foreach (int k in _lit)
                    Ignite(k);
                if (_show.Current != _phaseShown)
                    EnterPhase(_show.Current);
                MoveDrones(dt);
            }
            ApplyBell();
            ApplyLanterns();
            ShowHint(_noticeTimer > 0f ? _notice : null);
        }

        private void EnterPhase(FireflyLanternShow.Phase phase)
        {
            _phaseShown = phase;
            switch (phase)
            {
                case FireflyLanternShow.Phase.Rise:
                    AudioService.TryPlay(l => l.RcmdDronesRise);   // 드론 떼가 공방에서 날아오름
                    break;
                case FireflyLanternShow.Phase.Finale:
                    foreach (var d in _drones)
                    {
                        d.FinaleFrom = d.Pos;
                        Sparkle(d.Pos, 10, 0.9f);
                    }
                    Notice($"가지 등불 {_show.LitCount}개가 모두 켜졌어요!", 4f);
                    AudioService.TryPlay(l => l.RcmdShowFinale);
                    break;
                case FireflyLanternShow.Phase.Return:
                    foreach (var d in _drones)
                        d.ReturnFrom = d.Pos;
                    break;
                case FireflyLanternShow.Phase.Idle:
                    foreach (var d in _drones)
                        d.Root.gameObject.SetActive(false);
                    break;
            }
        }

        private void Ignite(int k)
        {
            if (_lanterns == null || k >= _lanterns.Length || _lanterns[k] == null)
                return;
            Sparkle(_lanterns[k].position, 30, 1.1f);
            AudioService.TryPlayNoteAt(l => l.RcmdLanternShimmer, _lanterns[k].position, _show.LitCount - 1);   // 켠 차례대로 음계로 오름 (도5부터)
            Notice($"반딧불 드론이 가지 등불을 켰어요  {_show.LitCount} / {_show.LanternCount}", 1.6f);
        }

        // ---------------- 드론 ----------------

        private void BuildDrones()
        {
            int n = RepairCommand.Lanterns.Length + RepairCommand.EscortDrones;
            for (int i = 0; i < n; i++)
            {
                var root = BuildDrone(transform, $"FireflyDrone{i}", _bodyMaterial, _glowMaterial, out var glow);
                root.gameObject.SetActive(false);
                float a = Hash(i, 1), b = Hash(i, 2), c = Hash(i, 3);
                _drones.Add(new Drone
                {
                    Root = root,
                    Glow = glow,
                    A = a,
                    B = b,
                    C = c,
                    Workshop = RepairCommand.WorkshopAngles[i % RepairCommand.WorkshopAngles.Length],
                });
            }
        }

        /// <summary>반딧불 드론 하나 (진주 납작 몸 + 배 · 꼬리 쪽 빛 구슬) — 미니게임 드론 순서 기억하기도 씀. 충돌 없음.</summary>
        public static Transform BuildDrone(Transform parent, string name, Material bodyMaterial, Material glowMaterial, out Transform glow)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(body.GetComponent<Collider>());
            body.name = "Body";
            body.transform.SetParent(root, false);
            body.transform.localScale = new Vector3(DroneBody, DroneBody * 0.5f, DroneBody);
            Style(body, bodyMaterial);
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.name = "Glow";
            g.transform.SetParent(root, false);
            g.transform.localPosition = new Vector3(0f, -0.05f, -DroneBody * 0.32f);   // 배 · 꼬리 쪽 빛
            g.transform.localScale = Vector3.one * DroneGlow;
            Style(g, glowMaterial);
            glow = g.transform;
            return root;
        }

        /// <summary>드론 꼬리 빛 크기 · 둘레 빛무리 (반딧불처럼 숨 쉬듯 깜박 — 밝기 · 크기, strength 0 = 꺼짐).</summary>
        public static void Shine(Transform glow, ParticleSystem sparks, float strength, float blinkPhase, float dt)
        {
            float blink = 0.75f + 0.35f * Mathf.Sin(Time.unscaledTime * 5.5f + blinkPhase);
            float g = Mathf.Max(0f, strength) * blink;
            if (glow != null)
                glow.localScale = Vector3.one * (DroneGlow * g);
            if (sparks == null || g < 0.05f || glow == null)
                return;
            sparks.Emit(new ParticleSystem.EmitParams
            {
                position = glow.position,
                velocity = Vector3.zero,
                startLifetime = Mathf.Max(0.02f, dt * 1.5f),
                startSize = HaloSize * Mathf.Min(1.6f, g),
                startColor = new Color(SparkWarm.r, SparkWarm.g, SparkWarm.b, Mathf.Clamp01(0.35f + 0.4f * g)),
                applyShapeToPosition = false,
            }, 1);
        }

        /// <summary>반딧불 반짝이 입자 (빛무리 · 꼬리 · 터짐 — 실제 시간, 많이).</summary>
        public static ParticleSystem CreateSparks(Transform parent, Material spark)
        {
            if (spark == null)
                return null;
            var ps = ShootingStar.CreateSparks(parent, spark);
            var main = ps.main;
            main.maxParticles = 1200;   // 빛무리 (드론마다 한 프레임 한 알) + 꼬리 + 등불 · 흩어질 때 반짝이
            return ps;
        }

        /// <summary>한 자리에서 따뜻한 반짝이가 사방으로 (등불 켜짐 · 드론 나타남).</summary>
        public static void Burst(ParticleSystem sparks, Vector3 at, int count, float speed)
        {
            if (sparks == null)
                return;
            for (int k = 0; k < count; k++)
            {
                sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = Random.insideUnitSphere * speed + Vector3.up * 0.2f,
                    startLifetime = Random.Range(0.5f, 1.1f),
                    startSize = Random.Range(0.05f, 0.12f),
                    startColor = Color.Lerp(SparkWarm, Color.white, Random.value),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        private static void Style(GameObject go, Material m)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (m != null)
                r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        /// <summary>정해진 흩어짐 (드론 번호마다 같은 값, −0.5 ~ 0.5).</summary>
        private static float Hash(int i, int salt)
        {
            float x = Mathf.Sin(i * 12.9898f + salt * 78.233f) * 43758.5453f;
            return x - Mathf.Floor(x) - 0.5f;
        }

        private static float Hill => RepairCommand.HillTop;

        private static Vector3 Home(Drone d) => RepairCommand.AroundTree(RepairCommand.DroneHomeRadius + 0.5f * d.B, d.Workshop + 8f * d.A, Hill + RepairCommand.DroneHomeLift + 0.5f * d.C);
        private static float DoorLift(Drone d) => Hill + RepairCommand.DroneDoorLift + 0.3f * d.C;
        /// <summary>문 안 · 문 앞 = 문 가운데 줄 (비스듬히 나가면 문틀 · 벽에 닿음).</summary>
        private static Vector3 DoorIn(Drone d) => RepairCommand.AroundTree(RepairCommand.DroneDoorInRadius, d.Workshop, DoorLift(d));
        private static Vector3 Door(Drone d) => RepairCommand.AroundTree(RepairCommand.DroneDoorRadius, d.Workshop, DoorLift(d));
        /// <summary>문 앞에서 곧게 올라온 고리 높이 자리.</summary>
        private static Vector3 OverDoor(Drone d) => RepairCommand.AroundTree(RepairCommand.DroneDoorRadius, d.Workshop, RepairCommand.SwarmY + 0.3f * d.C);

        /// <summary>떼 안 자리: 떼 머리 각에서 뒤로 0 ~ 28° 늘어서고 반지름 · 높이가 조금씩 다르게 출렁임 (tighten 1 = 머리 자리에 한데).</summary>
        private Vector3 Swarm(Drone d, int i, float head, float t, float tighten = 0f)
        {
            float spread = 1f - tighten;
            float deg = head - (14f + 28f * d.A) * spread;
            float r = RepairCommand.SwarmRadius + (0.5f * d.B + 0.15f * Mathf.Sin(t * 1.3f + i)) * spread;
            float y = RepairCommand.SwarmY + (RepairCommand.SwarmWobble * Mathf.Sin(t * 1.7f + i * 1.1f) + 0.3f * d.C) * spread;
            return RepairCommand.AroundTree(r, deg, y);
        }

        /// <summary>고목 축 둘레 고리를 따라 (반지름 · 각 · 높이를 따로) 옮겨 감 — 곧은 줄이면 줄기를 가로질렀음.</summary>
        private static Vector3 Polar(Vector3 from, Vector3 to, float s)
        {
            var ax = RepairCommand.TreeAxis;
            float r0 = new Vector2(from.x - ax.x, from.z - ax.z).magnitude, r1 = new Vector2(to.x - ax.x, to.z - ax.z).magnitude;
            float a0 = RepairCommand.AngleAroundTree(from), a1 = RepairCommand.AngleAroundTree(to);
            return RepairCommand.AroundTree(Mathf.Lerp(r0, r1, s), a0 + Mathf.DeltaAngle(a0, a1) * s, Mathf.Lerp(from.y, to.y, s));
        }

        /// <summary>여러 토막 길의 한 토막: s가 from ~ to 사이에서 0 ~ 1 (부드럽게).</summary>
        private static float Seg(float s, float from, float to) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - from) / (to - from)));

        private void MoveDrones(float dt)
        {
            float t = Time.unscaledTime;
            var phase = _show.Current;
            float pt = _show.PhaseTime;
            float head = _show.SwarmAngle;
            for (int i = 0; i < _drones.Count; i++)
            {
                var d = _drones[i];
                Vector3 p;
                float glow = 1f;
                switch (phase)
                {
                    case FireflyLanternShow.Phase.Ring:
                        p = Home(d) + Vector3.up * (0.05f * Mathf.Sin(t * 3f + i));
                        glow = Mathf.Clamp01(pt / FireflyLanternShow.RingTime * 1.5f - 0.2f * (d.A + 0.5f));
                        break;
                    case FireflyLanternShow.Phase.Rise:
                    {
                        // 공방 안 → 문 가운데 줄로 나옴 → 문 앞에서 곧게 고리 높이로 → 고리를 따라 떼 자리로 (드론마다 조금씩 늦게)
                        float u = Mathf.Clamp01((pt - 0.05f * i) / (FireflyLanternShow.RiseTime - 1.3f));
                        if (u < 0.12f)
                            p = Vector3.Lerp(Home(d), DoorIn(d), Seg(u, 0f, 0.12f));
                        else if (u < 0.2f)
                            p = Vector3.Lerp(DoorIn(d), Door(d), Seg(u, 0.12f, 0.2f));
                        else if (u < 0.5f)
                            p = Vector3.Lerp(Door(d), OverDoor(d), Seg(u, 0.2f, 0.5f));
                        else
                            p = Polar(OverDoor(d), Swarm(d, i, head, t), Seg(u, 0.5f, 1f));
                        break;
                    }
                    case FireflyLanternShow.Phase.Circle:
                    {
                        p = Swarm(d, i, head, t);
                        float v = i < RepairCommand.Lanterns.Length ? _show.Visit(i) : 0f;
                        if (v > 0f)
                        {
                            p = Vector3.Lerp(p, RepairCommand.LanternHover(i), v);
                            glow = 1f + 0.8f * v;
                        }
                        break;
                    }
                    case FireflyLanternShow.Phase.Finale:
                    {
                        // 다 켠 자리 (떼 고리)에서 바깥으로 살짝 퍼지며 환히 — 위쪽은 나무집 받침 가지가 있어 수평으로만
                        float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(pt / 0.8f));
                        var o = new Vector3(d.FinaleFrom.x - RepairCommand.TreeAxis.x, 0f, d.FinaleFrom.z - RepairCommand.TreeAxis.z).normalized;
                        p = d.FinaleFrom + o * (RepairCommand.FinaleOut * u) + Vector3.up * (0.05f * Mathf.Sin(t * 2.2f + i));
                        glow = 1f + 1.4f * Mathf.Exp(-pt * 2f) + 0.3f * Mathf.Sin(t * 9f + i);
                        break;
                    }
                    case FireflyLanternShow.Phase.Return:
                    {
                        // 고리를 따라 공방 문 위로 → 문 앞으로 곧게 내려 문 가운데 줄로 들어감
                        float u = Mathf.Clamp01((pt - 0.04f * i) / (FireflyLanternShow.ReturnTime - 1f));
                        if (u < 0.75f)
                            p = Polar(d.ReturnFrom, OverDoor(d), Seg(u, 0f, 0.75f));
                        else if (u < 0.88f)
                            p = Vector3.Lerp(OverDoor(d), Door(d), Seg(u, 0.75f, 0.88f));
                        else if (u < 0.94f)
                            p = Vector3.Lerp(Door(d), DoorIn(d), Seg(u, 0.88f, 0.94f));
                        else
                            p = Vector3.Lerp(DoorIn(d), Home(d), Seg(u, 0.94f, 1f));
                        glow = 1f - Seg(u, 0.9f, 1f);
                        break;
                    }
                    default:
                        p = Home(d);
                        break;
                }
                var move = p - d.Pos;
                d.Pos = p;
                d.Root.localPosition = p;
                var flat = new Vector3(move.x, 0f, move.z);
                if (flat.sqrMagnitude > 1e-6f)
                    d.Root.localRotation = Quaternion.Slerp(d.Root.localRotation, Quaternion.LookRotation(flat.normalized, Vector3.up) * Quaternion.Euler(8f, 0f, 0f), 1f - Mathf.Exp(-dt * 8f));
                // 반딧불처럼 꼬리 빛이 숨 쉬듯 깜박 (밝기 · 크기)
                EnsureSparks();
                Shine(d.Glow, _sparks, glow, i * 2.1f, dt);
                if (glow > 0.2f && Random.value < TrailRate * dt && move.sqrMagnitude > 1e-6f)
                    Trail(d.Root.position, -move.normalized);
            }
        }

        private void EnsureSparks()
        {
            if (_sparks == null)
                _sparks = CreateSparks(transform, _sparkMaterial);
        }

        private void Trail(Vector3 at, Vector3 back)
        {
            EnsureSparks();
            if (_sparks == null)
                return;
            _sparks.Emit(new ParticleSystem.EmitParams
            {
                position = at + back * 0.08f,
                velocity = back * Random.Range(0.1f, 0.35f) + Random.insideUnitSphere * 0.12f,
                startLifetime = Random.Range(0.4f, 0.8f),
                startSize = Random.Range(0.04f, 0.08f),
                startColor = Color.Lerp(SparkWarm, Color.white, Random.value * 0.5f),
                applyShapeToPosition = false,
            }, 1);
        }

        private void Sparkle(Vector3 at, int count, float speed)
        {
            EnsureSparks();
            Burst(_sparks, at, count, speed);
        }

        // ---------------- 종 · 등불 ----------------

        private void ApplyBell()
        {
            float t = _bellT;
            if (_bell != null)
            {
                float a = t < 8f ? BellSwing * Mathf.Exp(-BellDecay * t) * Mathf.Sin(2f * Mathf.PI * t / BellPeriod) : 0f;
                _bell.localRotation = _bellRest * Quaternion.Euler(a, 0f, 0.4f * a);
            }
            if (_bellRope != null)
                _bellRope.localPosition = _ropeBase + Vector3.down * (t < 0.6f ? RopePull * Mathf.Sin(Mathf.PI * t / 0.6f) : 0f);
        }

        private void ApplyLanterns()
        {
            if (_lanternRenderers == null || _show == null)
                return;
            float sum = 0f;
            for (int k = 0; k < _lanternRenderers.Length; k++)
            {
                var r = _lanternRenderers[k];
                float g = k < _show.LanternCount ? _show.Glow(k) : 1f;
                float flash = k < _show.LanternCount ? _show.Flash(k) : 0f;
                sum += g;
                if (r == null)
                    continue;
                float e = Mathf.Lerp(DimEmission, LitEmission, g) + 2.5f * flash;
                // 재질 칸 0의 블록 (InteriorAtmosphere가 빛 재질 칸마다 블록을 걸어 렌더러 전체 블록은 무시됐음 — 2026-11-15 플레이)
                r.GetPropertyBlock(_block, 0);
                _block.SetColor(BaseColorId, Color.Lerp(_baseColor * 0.12f, _baseColor, g));
                _block.SetColor(EmissionId, _baseEmission * e);
                r.SetPropertyBlock(_block, 0);
                if (_lanterns[k] != null)
                    _lanterns[k].localScale = _lanternBaseScale[k] * (1f + 0.35f * flash);
            }
            float avg = _lanternRenderers.Length > 0 ? sum / _lanternRenderers.Length : 1f;
            foreach (var (l, i) in _canopy)
                if (l != null)
                    l.intensity = i * Mathf.Lerp(0.25f, 1.6f, avg);
        }

        // ---------------- 안내 ----------------

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
            var go = new GameObject("FireflyShowHint", typeof(RectTransform));
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
            Place(back.rectTransform, new Vector2(900f, 56f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(880f, 48f));
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
            rt.anchoredPosition = new Vector2(0f, 210f);   // 바구니 안내 (150) 위
            rt.sizeDelta = size;
        }
    }
}
