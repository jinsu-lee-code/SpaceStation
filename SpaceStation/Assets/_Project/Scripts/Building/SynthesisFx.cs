using System.Collections;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 12-3b 합성 연출 (사용자 결정 2026-10-13, 스타크래프트 2 테란 건설 참고 → 비행 고리 방식):
    /// - 규격 둘레를 감싸는 기계 고리(직선 1칸 토막 + 모서리 토막, 보라 형광)가 아래 추진 불꽃과 함께 규격 바닥에서 솟아올라
    ///   진행률만큼 위로 올라간다. 고리 윗면 = 경계: 아래는 상위 모듈, 위는 재료 (높이 잘라내기 셰이더 SpaceStation/SynthesisClip).
    /// - 고리 위 받침의 기계팔 3개가 고리를 따라 둘레를 돌며(모서리에서 90° 회전, 가끔 반대 방향 · 속도 제각각) 멈춰 서서
    ///   경계 바로 위 재료 면을 용접(불꽃, 가끔은 살펴보기만). 2관절 IK(팔꿈치 위). 가는 방향 앞 팔과 가까우면 기다리다 돌아감.
    /// - 바로 위층에 모듈이 있으면 팔이 뚫지 않게: 여유가 줄면 팔을 접어 받침째 고리 바깥으로 제쳐 매달고, 매달린 채 고리 아래로 돌아 들어가
    ///   고리 바로 아래 상위 모듈 면을 마무리 용접하며 계속 이동. 고리 안쪽 가장자리 불꽃도 함께.
    /// - 고리는 살짝 출렁 · 기울었다 바로잡힘(비행 느낌), 일시 정지(파손 재료)면 추진 약해지고 살짝 처지며 팔 · 불꽃 멈춤.
    /// - 완료: 섬광 · 불꽃 폭발, 위가 비었으면 고리가 위로 날아올라 사라짐. 위가 막혔거나 취소: 토막마다 제자리에서 흩어짐.
    /// - 합성 중에는 재료 칸에 닿은 통로를 숨김.
    /// 재료 모듈 자체 표시(보라 틴트)는 없음 — 연출은 고리 · 팔이 맡음 (사용자 결정).
    /// 움직임은 게임 시간(일시정지 · 배속 반영), 시작 · 끝 연출은 실제 시간.
    /// </summary>
    public sealed class SynthesisFx : MonoBehaviour
    {
        private static readonly int ClipYId = Shader.PropertyToID("_ClipY");
        private static readonly int ClipDirId = Shader.PropertyToID("_ClipDir");
        private static readonly int ClipEdgeColorId = Shader.PropertyToID("_ClipEdgeColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private StationController _station;
        [SerializeField] private SimulationClock _clock;
        [Tooltip("SpaceStation/SynthesisClip (재료 위 · 결과 아래만 보이게)")]
        [SerializeField] private Shader _clipShader;
        [Header("모델 (메뉴 SpaceStation/Synthesis/Setup FX)")]
        [SerializeField] private GameObject _ringStraight;
        [SerializeField] private GameObject _ringCorner;
        [SerializeField] private GameObject _arm;
        [Tooltip("고리 보라 형광 재질 (맥동 · 일시 정지 때 어둡게 — 렌더러에서 이 재질 칸을 찾음)")]
        [SerializeField] private Material _glowMaterial;
        [Tooltip("불꽃 · 추진 입자 (가산 입자 재질)")]
        [SerializeField] private Material _sparkMaterial;
        [Header("색 · 세기")]
        [SerializeField, ColorUsage(false, true)] private Color _glowColor = new Color(1.4f, 0.62f, 2.4f, 1f);
        [SerializeField, ColorUsage(false, true)] private Color _edgeColor = new Color(2.0f, 0.9f, 3.4f, 1f);
        [SerializeField] private Color _plumeColor = new Color(0.72f, 0.42f, 1f, 1f);
        [SerializeField] private Color _weldColor = new Color(1f, 0.82f, 0.5f, 1f);
        [Header("움직임")]
        [SerializeField] private float _introSeconds = 1.4f;
        [SerializeField] private float _outroSeconds = 1.1f;
        [SerializeField] private float _bobHeight = 0.012f;
        [SerializeField] private float _tiltDegrees = 0.9f;

        // 모델 축 (Blender 빌더 기준 → Unity 가져오기, BlenderWork/SynthArm/arm_builder.py): 토막 · 받침 = 바깥 −z, 위 +y
        private const float RingHalfHeight = 0.042f;      // 고리 중심선 → 윗면 (윗면 = 경계)
        private const float FaceInset = 0.04f;            // 모듈 겉면 = 칸 경계 안쪽 0.04
        private const float RingTopReach = 0.125f;        // 고리 중심 → 가장 높은 곳 (모서리 결정체 끝)
        private static readonly Vector3[] StraightNozzles = { new Vector3(-0.25f, -0.076f, -0.0575f), new Vector3(0.25f, -0.076f, -0.0575f) };
        private static readonly Vector3 CornerDisc = new Vector3(0.0615f, -0.082f, -0.0615f);
        private static readonly Vector3 CornerOutward = new Vector3(1f, 0f, -1f);
        // 팔 관절 축 (팔 모델 루트 로컬 = Blender 축: 위 = −z, 안쪽 = +y)
        private static readonly Vector3 ArmUp = new Vector3(0f, 0f, -1f);
        private static readonly Vector3 ArmForward = new Vector3(0f, 1f, 0f);

        private sealed class Arm
        {
            public Transform Mount, Model, Turret, Upper, Fore, Head, Tip;
            public Quaternion ModelRotation;   // 모델 루트 원래 회전 (축 변환) — 매달 때 이 위에 돌림
            public float L1, L2;
            // 12-3b 피드백: 고리 둘레를 따라 이동 (S = 둘레 위치, Stop = 다음에 멈출 위치, Dir = 가는 방향 ±1) → 멈춰서 용접 → 다음 자리
            public float S, Stop;
            public int Dir = 1;
            public float Speed = 0.45f, Smooth = 0.16f, Seed, Blocked;
            public bool Moving, Inspect, HasGoal, GoalHanging;
            public float Timer;
            public Vector3 Goal, Current, Velocity;
            public float Weld;
            // 위 모듈 피하기: Hang 0 = 고리 위, 1 = 고리 바깥으로 제쳐 매달림 / HangIK = 매달린 채 다시 작업하는 정도
            public float Hang, HangIK;
            public ParticleSystem Sparks;
            public Light Flash;
        }

        private sealed class Fx
        {
            public SynthesisJob Job;
            public Transform Root, Ring;
            public float Bottom, Top;
            public Vector3 Center;
            public Vector2 Half;               // 고리 반 크기 (x, z)
            public readonly List<Arm> Arms = new List<Arm>();
            public readonly List<ParticleSystem> Plumes = new List<ParticleSystem>();
            public readonly List<(Renderer Renderer, int Slot)> GlowSlots = new List<(Renderer, int)>();
            public Light Under;
            public GameObject Preview;
            public readonly List<(Renderer Renderer, Material[] Original)> Swapped = new List<(Renderer, Material[])>();
            public readonly List<Material> MaterialClips = new List<Material>();
            public readonly List<Material> ResultClips = new List<Material>();
            public readonly List<Renderer> MaterialOthers = new List<Renderer>();   // Lit이 아닌 렌더러: 경계가 지나가면 숨김
            public readonly List<Renderer> ResultOthers = new List<Renderer>();     // 경계가 지나가면 보임
            public float Visual, Velocity;     // 화면 진행률 (틱 단위 진행을 부드럽게)
            public float Intro;                // 0 → 1 (실제 시간)
            public float Clock;                // 게임 시간 누적 (출렁임 · 맥동)
            public float Weight;               // 팔 IK 무게 (시작 · 끝 접힘)
            public float Ceiling = float.PositiveInfinity;   // 바로 위층 모듈 밑면 (없으면 무한) — 팔이 뚫지 않게
            public ParticleSystem RingWeld;    // 팔을 접은 마지막 구간: 고리 안쪽 가장자리 불꽃
            public float Perimeter;            // 둘레 길이 (모서리 회전 구간 포함)
            public Vector3Int Min, Max;        // 규격 칸 범위
            public float CeilingCheck;         // 다음 천장 확인까지 (실제 초)
            public readonly List<Renderer> HiddenConnectors = new List<Renderer>();   // 숨긴 재료 통로 (취소 때 되돌림)
            public float ConnectorCheck;       // 다음 통로 숨기기까지 (실제 초)
        }

        private readonly Dictionary<SynthesisJob, Fx> _fx = new Dictionary<SynthesisJob, Fx>();
        private readonly List<Fx> _scratch = new List<Fx>();
        private MaterialPropertyBlock _block;
        private SynthesisSystem _synthesis;
        private Transform _stash;              // 미리보기 프리팹을 깨우지 않고 정리하는 비활성 부모
        private StationConnectors _connectors;
        private readonly List<Vector3Int> _cells = new List<Vector3Int>();
        private readonly List<Renderer> _renderers = new List<Renderer>();

        private void Start()
        {
            _block = new MaterialPropertyBlock();
            _synthesis = _station != null && _station.Simulation != null ? _station.Simulation.Synthesis : null;
            if (_synthesis == null || _clipShader == null || _ringStraight == null || _ringCorner == null || _arm == null)
            {
                enabled = false;
                return;
            }
            _connectors = _station.GetComponent<StationConnectors>();
            var stash = new GameObject("SynthesisFxStash");
            stash.SetActive(false);
            stash.transform.SetParent(transform, false);
            _stash = stash.transform;
            _synthesis.Started += HandleStarted;
            _synthesis.Completed += HandleCompleted;
            _synthesis.Cancelled += HandleCancelled;
            foreach (var job in _synthesis.Jobs)
                Create(job, false); // 불러온 판: 시작 연출 없이 지금 진행률에서
        }

        private void OnDestroy()
        {
            if (_synthesis != null)
            {
                _synthesis.Started -= HandleStarted;
                _synthesis.Completed -= HandleCompleted;
                _synthesis.Cancelled -= HandleCancelled;
            }
            foreach (var fx in _fx.Values)
                DestroyMaterials(fx);
        }

        private void HandleStarted(SynthesisJob job) => Create(job, true);

        private void HandleCompleted(SynthesisJob job, ModuleInstance result)
        {
            if (!_fx.TryGetValue(job, out var fx))
                return;
            _fx.Remove(job);
            if (fx.Preview != null)
                Destroy(fx.Preview); // 진짜 결과 모듈이 같은 자리에 생김 (재료 뷰는 철거로 사라짐)
            DestroyMaterials(fx);
            StartCoroutine(Outro(fx, true));
        }

        private void HandleCancelled(SynthesisJob job)
        {
            if (!_fx.TryGetValue(job, out var fx))
                return;
            _fx.Remove(job);
            Restore(fx);
            if (fx.Preview != null)
                Destroy(fx.Preview);
            DestroyMaterials(fx);
            StartCoroutine(Outro(fx, false));
        }

        // ---------------- 만들기 ----------------

        private void Create(SynthesisJob job, bool intro)
        {
            if (job?.Recipe?.Result == null || _fx.ContainsKey(job))
                return;
            var fx = new Fx { Job = job, Intro = intro ? 0f : 1f, Weight = intro ? 0f : 1f };
            fx.Visual = job.Progress01;

            // 규격 둘레 (칸 경계, 월드)
            var cells = StationGrid.ResolveCells(job.Recipe.Result.CellOffsets, job.Origin, job.Rotation);
            Vector3Int min = cells[0], max = cells[0];
            foreach (var c in cells)
            {
                min = Vector3Int.Min(min, c);
                max = Vector3Int.Max(max, c);
            }
            float cs = GridConfig.CellSize;
            Vector3 lo = GridConfig.CellToWorld(min) - Vector3.one * (cs * 0.5f);
            Vector3 hi = GridConfig.CellToWorld(max) + Vector3.one * (cs * 0.5f);
            fx.Center = new Vector3((lo.x + hi.x) * 0.5f, 0f, (lo.z + hi.z) * 0.5f);
            fx.Half = new Vector2((hi.x - lo.x) * 0.5f, (hi.z - lo.z) * 0.5f);
            fx.Bottom = lo.y + FaceInset;
            fx.Top = hi.y - FaceInset;
            fx.Min = min;
            fx.Max = max;

            fx.Root = new GameObject($"SynthesisFx {job.Recipe.name}").transform;
            fx.Root.SetParent(transform, false);
            fx.Ring = new GameObject("Ring").transform;
            fx.Ring.SetParent(fx.Root, false);
            BuildRing(fx, Mathf.RoundToInt(fx.Half.x * 2f / cs), Mathf.RoundToInt(fx.Half.y * 2f / cs));
            fx.RingWeld = WeldSparks(fx.Root);
            fx.RingWeld.transform.localPosition = Vector3.zero;

            var light = new GameObject("UnderGlow").AddComponent<Light>();
            light.transform.SetParent(fx.Ring, false);
            light.transform.localPosition = new Vector3(0f, -0.18f, 0f);
            light.type = LightType.Point;
            light.color = _plumeColor;
            light.range = Mathf.Max(fx.Half.x, fx.Half.y) * 1.6f + 0.6f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            fx.Under = light;

            SwapMaterialModules(fx);
            CreatePreview(fx);
            _fx.Add(job, fx);
            Apply(fx, 0f);
        }

        /// <summary>천장이 생긴다면 그 높이 (바로 위층 모듈 밑면 = 맨 위 칸 경계 + 0.04).</summary>
        private static float CeilingHeight(Fx fx) => (fx.Max.y + 0.5f) * GridConfig.CellSize + FaceInset;

        /// <summary>
        /// 바로 위층(규격 + 고리가 지나는 둘레 한 칸)에 모듈이 있으면 그 밑면이 천장 — 팔이 뚫고 들어가지 않게 (12-3b 피드백).
        /// 경계가 천장 높이 근처에 온 뒤부터 주기적으로 다시 봄 (Apply).
        /// </summary>
        private void UpdateCeiling(Fx fx)
        {
            var grid = _station.Grid;
            fx.Ceiling = float.PositiveInfinity;
            for (int x = fx.Min.x - 1; x <= fx.Max.x + 1; x++)
                for (int z = fx.Min.z - 1; z <= fx.Max.z + 1; z++)
                {
                    if (grid.TryGetModule(new Vector3Int(x, fx.Max.y + 1, z), out var above) && !IsMaterial(fx.Job, above))
                    {
                        fx.Ceiling = CeilingHeight(fx);
                        return;
                    }
                }
        }

        private static bool IsMaterial(SynthesisJob job, ModuleInstance module)
        {
            foreach (var m in job.Materials)
                if (m == module)
                    return true;
            return false;
        }

        private void BuildRing(Fx fx, int nx, int nz)
        {
            float hx = fx.Half.x, hz = fx.Half.y, cs = GridConfig.CellSize;
            // 변: (바깥 방향, 변 위 칸 수, 시작점, 변 방향)
            var sides = new (Vector3 Out, int Count, Vector3 Start, Vector3 Along)[]
            {
                (Vector3.back, nx, new Vector3(-hx, 0f, -hz), Vector3.right),
                (Vector3.forward, nx, new Vector3(-hx, 0f, hz), Vector3.right),
                (Vector3.left, nz, new Vector3(-hx, 0f, -hz), Vector3.forward),
                (Vector3.right, nz, new Vector3(hx, 0f, -hz), Vector3.forward),
            };
            foreach (var side in sides)
            {
                var rot = Quaternion.LookRotation(-side.Out, Vector3.up); // 토막 바깥 = 모델 −z
                for (int i = 0; i < side.Count; i++)
                {
                    var piece = Instantiate(_ringStraight, fx.Ring);
                    piece.transform.localPosition = side.Start + side.Along * ((i + 0.5f) * cs);
                    piece.transform.localRotation = rot;
                    Collect(fx, piece);
                    foreach (var n in StraightNozzles)
                        fx.Plumes.Add(Plume(piece.transform, n, 0.022f));
                }
            }
            foreach (var corner in new[] { new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz), new Vector3(-hx, 0f, -hz) })
            {
                var dir = new Vector3(Mathf.Sign(corner.x), 0f, Mathf.Sign(corner.z));
                var piece = Instantiate(_ringCorner, fx.Ring);
                piece.transform.localPosition = corner;
                piece.transform.localRotation = Quaternion.Euler(0f, Vector3.SignedAngle(CornerOutward, dir, Vector3.up), 0f);
                Collect(fx, piece);
                fx.Plumes.Add(Plume(piece.transform, CornerDisc, 0.034f));
            }

            // 팔 3개 (사용자 결정 2026-10-13) — 둘레를 대략 같은 간격으로 나눠 출발, 방향 · 속도 · 반응은 팔마다 조금씩 다르게
            fx.Perimeter = 4f * (hx + hz) + 4f * CornerTurn;
            const int count = 3;
            for (int k = 0; k < count; k++)
            {
                var go = Instantiate(_arm, fx.Ring);
                Collect(fx, go);
                var arm = MakeArm(go.transform);
                arm.S = arm.Stop = Wrap(fx, hx + fx.Perimeter * (k + Random.Range(-0.12f, 0.12f)) / count);
                if (InCorner(fx, arm.S))
                    arm.S = arm.Stop = Wrap(fx, arm.S + CornerTurn + 0.1f);
                arm.Timer = Random.Range(0.2f, 0.9f);
                arm.Dir = Random.value < 0.5f ? 1 : -1;
                arm.Speed = Random.Range(0.32f, 0.58f);
                arm.Smooth = Random.Range(0.12f, 0.24f);
                arm.Seed = Random.Range(0f, 100f);
                fx.Arms.Add(arm);
                PlaceMount(fx, arm);
            }
        }

        // ---------------- 고리 둘레 길 (12-3b 피드백: 팔이 고리를 따라 이동) ----------------
        // 반시계(위에서 볼 때 +x → +z): 아래 변(바깥 −z) → 모서리 → 오른 변(+x) → 모서리 → 위 변(+z) → 모서리 → 왼 변(−x) → 모서리.
        // 모서리에서는 받침이 제자리에서 90° 돌며 CornerTurn 길이만큼 시간을 씀.
        private const float CornerTurn = 0.2f;
        private static readonly Vector3[] SideOut = { Vector3.back, Vector3.right, Vector3.forward, Vector3.left };
        private static readonly Vector3[] SideDir = { Vector3.right, Vector3.forward, Vector3.left, Vector3.back };

        private static float Wrap(Fx fx, float s) => Mathf.Repeat(s, fx.Perimeter);

        private static float SideLength(Fx fx, int side) => side % 2 == 0 ? fx.Half.x * 2f : fx.Half.y * 2f;

        private static Vector3 SideStart(Fx fx, int side)
        {
            float hx = fx.Half.x, hz = fx.Half.y;
            switch (side)
            {
                case 0: return new Vector3(-hx, 0f, -hz);
                case 1: return new Vector3(hx, 0f, -hz);
                case 2: return new Vector3(hx, 0f, hz);
                default: return new Vector3(-hx, 0f, hz);
            }
        }

        /// <summary>둘레 위치 s → 변 번호와 변 위 거리 t (모서리 회전 중이면 corner = true, t = 회전 진행 0~1).</summary>
        private static int Locate(Fx fx, float s, out float t, out bool corner)
        {
            s = Wrap(fx, s);
            for (int side = 0; side < 4; side++)
            {
                float len = SideLength(fx, side);
                if (s < len)
                {
                    t = s;
                    corner = false;
                    return side;
                }
                s -= len;
                if (s < CornerTurn)
                {
                    t = s / CornerTurn;
                    corner = true;
                    return side;
                }
                s -= CornerTurn;
            }
            t = 0f;
            corner = false;
            return 0;
        }

        private static bool InCorner(Fx fx, float s)
        {
            Locate(fx, s, out _, out bool corner);
            return corner;
        }

        /// <summary>받침 위치(고리 로컬) · 회전(안쪽 = +z) · 변 방향 · 안쪽 방향.</summary>
        private static void PathPoint(Fx fx, float s, out Vector3 position, out Quaternion rotation, out Vector3 along, out Vector3 inward)
        {
            int side = Locate(fx, s, out float t, out bool corner);
            if (corner)
            {
                position = SideStart(fx, side) + SideDir[side] * SideLength(fx, side);
                var a = Quaternion.LookRotation(-SideOut[side], Vector3.up);
                var b = Quaternion.LookRotation(-SideOut[(side + 1) % 4], Vector3.up);
                rotation = Quaternion.Slerp(a, b, Mathf.SmoothStep(0f, 1f, t));
                along = SideDir[(side + 1) % 4];
                inward = rotation * Vector3.forward;
                return;
            }
            position = SideStart(fx, side) + SideDir[side] * t;
            rotation = Quaternion.LookRotation(-SideOut[side], Vector3.up);
            along = SideDir[side];
            inward = -SideOut[side];
        }

        private static void PlaceMount(Fx fx, Arm arm)
        {
            PathPoint(fx, arm.S, out var p, out var r, out _, out _);
            arm.Mount.localPosition = p;
            arm.Mount.localRotation = r;
        }

        private void Collect(Fx fx, GameObject piece)
        {
            foreach (var r in piece.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i] == _glowMaterial)
                        fx.GlowSlots.Add((r, i));
                }
            }
        }

        private Arm MakeArm(Transform root)
        {
            var arm = new Arm
            {
                Mount = root,
                Model = Find(root, "SM_SynthArm"),
                Turret = Find(root, "SynthArm_Turret"),
                Upper = Find(root, "SynthArm_Upper"),
                Fore = Find(root, "SynthArm_Fore"),
                Head = Find(root, "SynthArm_Head"),
                Tip = Find(root, "SynthArm_Tip"),
            };
            arm.L1 = arm.Fore.localPosition.magnitude;
            arm.L2 = arm.Head.localPosition.magnitude + arm.Tip.localPosition.magnitude;
            arm.ModelRotation = arm.Model.localRotation;
            arm.Sparks = WeldSparks(arm.Tip);
            var flash = new GameObject("WeldLight").AddComponent<Light>();
            flash.transform.SetParent(arm.Tip, false);
            flash.type = LightType.Point;
            flash.color = _weldColor;
            flash.range = 0.6f;
            flash.intensity = 0f;
            flash.shadows = LightShadows.None;
            arm.Flash = flash;
            return arm;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name)
                return root;
            foreach (Transform c in root)
            {
                var f = Find(c, name);
                if (f != null)
                    return f;
            }
            return null;
        }

        // ---------------- 입자 ----------------

        private ParticleSystem Plume(Transform parent, Vector3 localPosition, float radius)
        {
            var go = new GameObject("Plume");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.LookRotation(Vector3.down);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 1.4f, radius * 2.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(_plumeColor, new Color(1f, 0.85f, 1f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            var emission = ps.emission;
            emission.rateOverTime = 26f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = radius * 0.5f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(_plumeColor, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _sparkMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private ParticleSystem WeldSparks(Transform tip)
        {
            var go = new GameObject("WeldSparks");
            go.transform.SetParent(tip, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.022f);
            main.startColor = new ParticleSystem.MinMaxGradient(_weldColor, new Color(1f, 0.55f, 0.25f, 1f));
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.01f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _sparkMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 1.2f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        private void Burst(Vector3 center, Vector2 half, float y, Color color, int count)
        {
            var go = new GameObject("SynthesisBurst");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(center.x, y, center.z);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.3f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(half.x * 2f, 0.05f, half.y * 2f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _sparkMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
        }

        // ---------------- 재료 · 결과 잘라내기 ----------------

        private void SwapMaterialModules(Fx fx)
        {
            var cache = new Dictionary<Material, Material>();
            foreach (var module in fx.Job.Materials)
            {
                if (module == null || !_station.TryGetView(module, out var view) || view == null)
                    continue;
                foreach (var r in view.GetComponentsInChildren<Renderer>(true))
                    SwapRenderer(fx, r, cache, -1f, fx.MaterialClips, fx.MaterialOthers, true);
            }
        }

        private void CreatePreview(Fx fx)
        {
            var prefab = fx.Job.Recipe.Result.Prefab;
            if (prefab == null)
                return;
            // 비활성 부모 아래에서 만들어 모듈 스크립트(Awake)를 깨우지 않고 껍데기만 남긴다
            var go = Instantiate(prefab, _stash);
            go.name = "SynthesisPreview";
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                DestroyImmediate(c);
            foreach (var b in go.GetComponentsInChildren<MonoBehaviour>(true))
                DestroyImmediate(b);
            go.transform.SetParent(fx.Root, false);
            go.transform.SetPositionAndRotation(GridConfig.CellToWorld(fx.Job.Origin), GridDirections.ToQuaternion(fx.Job.Rotation));
            var cache = new Dictionary<Material, Material>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                SwapRenderer(fx, r, cache, 1f, fx.ResultClips, fx.ResultOthers, false);
            fx.Preview = go;
        }

        private void SwapRenderer(Fx fx, Renderer r, Dictionary<Material, Material> cache, float dir, List<Material> clips, List<Renderer> others, bool remember)
        {
            if (r is ParticleSystemRenderer || r.name.EndsWith("_Rim"))
                return;
            var mats = r.sharedMaterials;
            var swapped = new Material[mats.Length];
            bool anyLit = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m != null && m.shader != null && m.shader.name == "Universal Render Pipeline/Lit")
                {
                    if (!cache.TryGetValue(m, out var clip))
                    {
                        clip = new Material(_clipShader) { name = m.name + " (Synthesis)" };
                        clip.CopyPropertiesFromMaterial(m);
                        clip.renderQueue = m.renderQueue;
                        clip.SetFloat(ClipDirId, dir);
                        clip.SetColor(ClipEdgeColorId, _edgeColor);
                        cache[m] = clip;
                        clips.Add(clip);
                    }
                    swapped[i] = clip;
                    anyLit = true;
                }
                else
                    swapped[i] = m;
            }
            if (anyLit)
            {
                if (remember)
                    fx.Swapped.Add((r, mats));
                r.sharedMaterials = swapped;
            }
            else
                others.Add(r);
        }

        private static void Restore(Fx fx)
        {
            foreach (var (r, original) in fx.Swapped)
            {
                if (r != null)
                    r.sharedMaterials = original;
            }
            foreach (var r in fx.MaterialOthers)
            {
                if (r != null)
                    r.enabled = true;
            }
            foreach (var r in fx.HiddenConnectors)
            {
                if (r != null)
                    r.enabled = true;
            }
            fx.Swapped.Clear();
            fx.HiddenConnectors.Clear();
        }

        /// <summary>
        /// 12-3b 피드백: 합성 중에는 재료 칸에 닿은 통로를 숨김 (상위 모듈 미리보기를 관통해 보였음). 완료되면 재료 철거로 통로가 지워지고
        /// 결과 모듈 통로가 새로 생김. 합성 중 이웃에 새로 지어 생긴 통로도 숨기도록 주기적으로 다시 모음.
        /// </summary>
        private void HideConnectors(Fx fx)
        {
            if (_connectors == null)
                return;
            _cells.Clear();
            foreach (var m in fx.Job.Materials)
                if (m != null)
                    _cells.AddRange(m.Cells);
            _renderers.Clear();
            _connectors.CollectRenderersTouching(_cells, _renderers);
            foreach (var r in _renderers)
            {
                if (r == null || !r.enabled)
                    continue;
                r.enabled = false;
                fx.HiddenConnectors.Add(r);
            }
        }

        private static void DestroyMaterials(Fx fx)
        {
            foreach (var m in fx.MaterialClips)
                if (m != null) Destroy(m);
            foreach (var m in fx.ResultClips)
                if (m != null) Destroy(m);
            fx.MaterialClips.Clear();
            fx.ResultClips.Clear();
        }

        // ---------------- 매 프레임 ----------------

        private void Update()
        {
            if (_fx.Count == 0)
                return;
            var clock = _clock != null ? _clock.Clock : null;
            float gameDt = clock == null ? Time.deltaTime : clock.IsPaused ? 0f : Time.deltaTime * clock.Speed;
            _scratch.Clear();
            _scratch.AddRange(_fx.Values);
            foreach (var fx in _scratch)
                Apply(fx, gameDt);
        }

        private void Apply(Fx fx, float gameDt)
        {
            var job = fx.Job;
            bool paused = job.Paused;
            fx.Clock += gameDt;
            fx.ConnectorCheck -= Time.deltaTime;
            if (fx.ConnectorCheck <= 0f)
            {
                fx.ConnectorCheck = 0.5f;
                HideConnectors(fx);
            }
            // 천장은 생길 수 있는 높이가 정해져 있음 (맨 위 경계 + 0.04) → 경계가 그 아래 여유(팔꿈치 자세 전환 + 0.08)에 들어온 뒤부터만
            // 0.5초마다 확인 (사용자 결정 2026-10-13: 그 전에는 확인할 필요 없음). 합성 중 위에 새로 지은 모듈도 이때 잡힘
            float seamNow = Mathf.Lerp(fx.Bottom, fx.Top, fx.Visual);
            if (CeilingHeight(fx) - seamNow < HangClearance + 0.08f)
            {
                fx.CeilingCheck -= Time.deltaTime;
                if (fx.CeilingCheck <= 0f)
                {
                    fx.CeilingCheck = 0.5f;
                    UpdateCeiling(fx);
                }
            }
            if (fx.Intro < 1f)
                fx.Intro = Mathf.Min(1f, fx.Intro + Time.deltaTime / Mathf.Max(0.01f, _introSeconds));
            fx.Weight = Mathf.MoveTowards(fx.Weight, fx.Intro >= 0.6f ? 1f : 0f, Time.deltaTime * 2.5f);
            // 진행률: 틱(게임 1초) 단위 값을 부드럽게 따라감
            fx.Visual = Mathf.SmoothDamp(fx.Visual, job.Progress01, ref fx.Velocity, 0.9f, Mathf.Infinity, Mathf.Max(gameDt, 1e-5f));
            float seam = Mathf.Lerp(fx.Bottom, fx.Top, fx.Visual);

            // 고리: 시작 때 바닥 아래 0.5에서 솟아오름 · 출렁임 · 기울기 · 일시 정지면 처짐
            float intro = 1f - Mathf.Pow(1f - fx.Intro, 3f);
            float rise = Mathf.Lerp(-0.5f, 0f, intro);
            float bob = Mathf.Sin(fx.Clock * 1.7f) * _bobHeight + (paused ? -0.025f : 0f);
            float ringY = seam - RingHalfHeight + rise + bob;
            // 위가 막혔으면 모서리 결정체 끝(고리 중심 + 0.125)이 위 모듈 밑면에 닿지 않게 (마지막 몇 % 동안 고리만 1~2cm 낮게, 경계는 그대로)
            ringY = Mathf.Min(ringY, fx.Ceiling - RingTopReach - 0.01f);
            fx.Ring.position = new Vector3(fx.Center.x, ringY, fx.Center.z);
            float tilt = paused ? 0.4f : 1f;
            fx.Ring.rotation = Quaternion.Euler(Mathf.Sin(fx.Clock * 1.1f) * _tiltDegrees * tilt, 0f, Mathf.Sin(fx.Clock * 1.37f + 1f) * _tiltDegrees * tilt);

            // 잘라내기 높이 (고리 윗면 = 경계, 출렁임은 빼고 진행률만)
            foreach (var m in fx.MaterialClips)
                m.SetFloat(ClipYId, seam);
            foreach (var m in fx.ResultClips)
                m.SetFloat(ClipYId, seam);
            foreach (var r in fx.MaterialOthers)
                if (r != null) r.enabled = r.bounds.center.y > seam;
            foreach (var r in fx.ResultOthers)
                if (r != null) r.enabled = r.bounds.center.y < seam;

            // 보라 형광 맥동 · 아래 빛 · 추진
            float pulse = paused ? 0.35f + 0.1f * Mathf.Sin(Time.time * 1.2f) : 0.8f + 0.2f * Mathf.Sin(Time.time * 3f);
            var glow = _glowColor * (pulse * Mathf.Lerp(2f, 1f, intro));
            foreach (var (r, slot) in fx.GlowSlots)
            {
                if (r == null)
                    continue;
                r.GetPropertyBlock(_block, slot);
                _block.SetColor(EmissionColorId, glow);
                r.SetPropertyBlock(_block, slot);
            }
            fx.Under.intensity = (paused ? 0.35f : 1.1f) * intro * (0.85f + 0.15f * pulse);
            foreach (var p in fx.Plumes)
            {
                var e = p.emission;
                e.rateOverTime = paused ? 6f : Mathf.Lerp(60f, 26f, intro);
            }

            // 팔
            foreach (var arm in fx.Arms)
                UpdateArm(fx, arm, seam, gameDt, paused);
            RingWeld(fx, seam, gameDt, paused);
        }

        // 팔 이동 · 용접 (12-3b 피드백: 고리를 따라 움직이며 작업, 동작 · 방향에 약간의 무작위)
        private const float ArmMinGap = 0.5f;          // 가는 방향 앞 팔과 이만큼 가까우면 기다림
        private const float ArmBlockedTurn = 1.2f;     // 이만큼 막혀 있으면 반대 방향으로
        // 위 모듈 피하기: 고리 윗면 ~ 위 모듈 밑면 여유가 이보다 작으면 팔을 고리 바깥으로 제쳐 매닮 (팔꿈치 위 자세는 고리 위 약 0.4 필요).
        // 매달린 팔은 고리 아래로 돌아 들어가 고리 바로 아래 상위 모듈 면을 마무리 용접하며 계속 고리를 따라 이동 (사용자 피드백 2026-10-13).
        // "팔꿈치 아래 자세"는 아래팔이 고리를 가로질러 뚫어서 없앰.
        private const float HangClearance = 0.48f;
        private static readonly Vector3 StowHinge = new Vector3(0f, 0.045f, -0.122f);   // 받침 루트 기준: 고리 윗면 바깥 모서리 조금 밖 (바깥 = −z)
        private const float StowDrop = 0.12f;          // 다 넘긴 뒤 고리 바깥면을 따라 내려가는 깊이 (안 내리면 받침이 고리 위 0.15로 솟음 — 측정)
        private const float HangFlipPart = 0.75f;      // Hang 0~0.75 = 접으며 넘기기, 0.75~1 = 내려가기 (동시에 하면 아직 안쪽에 있던 부분이 고리로 내려가 4cm 박혔음 — 감시 측정)
        private const float WeldGap = 0.012f;          // 용접점 = 모듈 면 1.2cm 앞 (4mm면 비스듬한 토치 머리가 1.7cm 박혔음 — 감시 측정, 불꽃은 끝에서 나감)
        private const float WeldAboveMin = 0.05f;      // 고리 위 용접점 최소 높이 (낮으면 아래팔이 고리 안쪽 모서리를 스침)

        private void UpdateArm(Fx fx, Arm arm, float seam, float gameDt, bool paused)
        {
            float clearance = fx.Ceiling - seam;
            if (gameDt > 0f)
            {
                // 매달기: 접어서 넘기기(Hang 0→1) → 매달린 채 IK 다시 켜기(HangIK 0→1). 풀 때는 반대 순서
                bool wantHang = clearance < HangClearance;
                if (wantHang)
                {
                    arm.Hang = Mathf.MoveTowards(arm.Hang, 1f, gameDt * 1.6f);
                    if (arm.Hang >= 1f)
                        arm.HangIK = Mathf.MoveTowards(arm.HangIK, 1f, gameDt * 2f);
                }
                else
                {
                    arm.HangIK = Mathf.MoveTowards(arm.HangIK, 0f, gameDt * 2f);
                    if (arm.HangIK <= 0f)
                        arm.Hang = Mathf.MoveTowards(arm.Hang, 0f, gameDt * 1.6f);
                }
            }
            bool settled = arm.Hang <= 0f || arm.HangIK >= 1f; // 넘어가는 중이 아님
            bool hanging = arm.Hang >= 1f;
            // 목표가 아직 없거나, 넘어가기가 끝나 매달림 상태가 바뀌었으면 지금 상태에 맞는 용접점부터 — IK 무게가 다시 번지기 전에
            // (감시 측정: 첫 목표가 (0,0,0)이라 처음 몇 초 팔이 모듈을 15cm 뚫었고, 매달린 직후 고리 위 목표가 남아 고리 아래에서 위로 뻗으며 고리를 4cm 뚫었음)
            bool atEnd = arm.Hang >= 1f || arm.Hang <= 0f;
            if (!arm.HasGoal || (atEnd && arm.GoalHanging != hanging))
            {
                bool switched = arm.HasGoal;
                arm.Moving = false;
                arm.Timer = Random.Range(0.5f, 1.2f);
                PickWeld(fx, arm, seam, hanging);
                if (switched)
                {
                    arm.Current = arm.Goal; // 접힌 자세에서 새 자세로 IK 무게가 번지므로 끝점은 바로 새 목표에
                    arm.Velocity = Vector3.zero;
                }
            }
            bool working = gameDt > 0f && !paused && fx.Weight > 0.99f && settled;
            if (working)
            {
                if (arm.Moving)
                {
                    float left = arm.Dir > 0 ? Wrap(fx, arm.Stop - arm.S) : Wrap(fx, arm.S - arm.Stop);
                    if (GapInDirection(fx, arm, arm.Dir) > ArmMinGap)
                    {
                        arm.S = Wrap(fx, arm.S + arm.Dir * Mathf.Min(left, arm.Speed * gameDt));
                        arm.Blocked = 0f;
                    }
                    else
                    {
                        arm.Blocked += gameDt;
                        if (arm.Blocked > ArmBlockedTurn)
                        {
                            arm.Dir = -arm.Dir; // 막혔으면 돌아감
                            arm.Blocked = 0f;
                            PickStop(fx, arm);
                        }
                    }
                    float remain = arm.Dir > 0 ? Wrap(fx, arm.Stop - arm.S) : Wrap(fx, arm.S - arm.Stop);
                    if (remain < 1e-3f || remain > fx.Perimeter - 1e-3f)
                    {
                        arm.Moving = false;
                        arm.Timer = Random.Range(0.6f, 1.6f);
                        arm.Inspect = Random.value < 0.25f; // 가끔은 용접 없이 살펴보기만
                        PickWeld(fx, arm, seam, hanging);
                    }
                }
                else
                {
                    arm.Timer -= gameDt;
                    if (arm.Timer <= 0f)
                    {
                        if (Random.value < 0.3f)
                            arm.Dir = -arm.Dir; // 가끔 반대 방향
                        arm.Speed = Random.Range(0.32f, 0.58f);
                        PickStop(fx, arm);
                        arm.Moving = true;
                    }
                    else if (Random.value < gameDt * 1.4f)
                        PickWeld(fx, arm, seam, hanging); // 용접 중에도 조금씩 옮겨 가며
                }
                PlaceMount(fx, arm);
                if (arm.Moving)
                {
                    // 이동 중: 고리 위면 토치를 면 앞으로 들고, 매달렸으면 고리 아래 경계선 근처로
                    // (면 앞 = 경계 안쪽 FaceInset보다 바깥. 예전엔 면 안쪽 7cm로 잡아 토치가 모듈을 뚫었음 — 감시 측정)
                    arm.Goal = arm.Mount.position + arm.Mount.forward * (hanging ? -0.02f : FaceInset - 0.06f);
                    arm.Goal.y = hanging ? arm.Mount.position.y - 0.24f : seam + 0.18f;
                }
                if (!hanging)
                    arm.Goal.y = Mathf.Max(arm.Goal.y, seam + WeldAboveMin - 0.005f);
                if (arm.Current == Vector3.zero)
                    arm.Current = arm.Tip.position; // 쉬는 자세 끝에서 부드럽게 출발
                arm.Current = Vector3.SmoothDamp(arm.Current, arm.Goal, ref arm.Velocity, arm.Smooth, 3f, gameDt);
                bool atGoal = (arm.Current - arm.Goal).sqrMagnitude < 0.0004f;
                arm.Weld = Mathf.MoveTowards(arm.Weld, !arm.Moving && !arm.Inspect && atGoal ? 1f : 0f, gameDt * 8f);
            }
            else
            {
                PlaceMount(fx, arm);
                arm.Weld = Mathf.MoveTowards(arm.Weld, 0f, Time.deltaTime * 6f);
            }

            // 용접 중 토치 끝의 미세한 떨림 (팔마다 다른 씨앗)
            var target = arm.Current == Vector3.zero ? arm.Tip.position : arm.Current;
            if (arm.Weld > 0.01f)
            {
                float t = fx.Clock * 9f;
                target += new Vector3(Mathf.PerlinNoise(t, arm.Seed) - 0.5f, Mathf.PerlinNoise(arm.Seed, t) - 0.5f, Mathf.PerlinNoise(t + 3f, arm.Seed + 7f) - 0.5f) * (0.014f * arm.Weld);
            }
            Solve(arm, target, fx.Weight);

            var em = arm.Sparks.emission;
            em.rateOverTime = arm.Weld * Random.Range(50f, 90f);
            arm.Flash.intensity = arm.Weld * (0.6f + 0.6f * Mathf.PerlinNoise(Time.time * 18f, arm.Seed));
        }

        /// <summary>다음 멈출 자리: 가는 방향으로 0.25~0.7칸 (모서리 회전 구간에 멈추지 않게).</summary>
        private static void PickStop(Fx fx, Arm arm)
        {
            arm.Stop = Wrap(fx, arm.S + arm.Dir * Random.Range(0.25f, 0.7f));
            if (InCorner(fx, arm.Stop))
                arm.Stop = Wrap(fx, arm.Stop + arm.Dir * (CornerTurn + 0.12f));
        }

        /// <summary>
        /// 지금 자리 근처 용접점. 고리 위: 경계 바로 위 재료 면 (아래팔이 고리 안쪽 모서리를 스치지 않게 WeldAboveMin 이상).
        /// 매달림: 고리 바로 아래 상위 모듈 면 (고리 몸통 · 노즐 아래로만 지나감).
        /// </summary>
        private static void PickWeld(Fx fx, Arm arm, float seam, bool hanging)
        {
            PlaceMount(fx, arm);
            arm.HasGoal = true;
            arm.GoalHanging = hanging;
            arm.Goal = arm.Mount.position + arm.Mount.right * Random.Range(-0.14f, 0.14f) + arm.Mount.forward * (FaceInset - WeldGap);
            arm.Goal.y = hanging ? arm.Mount.position.y - Random.Range(0.15f, 0.27f) : seam + Random.Range(WeldAboveMin, 0.13f);
            if (arm.Inspect)
                arm.Goal += arm.Mount.forward * -0.06f; // 살펴보기: 면에서 조금 떨어져 들여다봄
        }

        /// <summary>가는 방향(dir)으로 바로 앞 팔까지 둘레 거리.</summary>
        private static float GapInDirection(Fx fx, Arm arm, int dir)
        {
            float best = float.PositiveInfinity;
            foreach (var other in fx.Arms)
            {
                if (other == arm)
                    continue;
                float d = dir > 0 ? Wrap(fx, other.S - arm.S) : Wrap(fx, arm.S - other.S);
                if (d > 1e-4f && d < best)
                    best = d;
            }
            return best;
        }

        /// <summary>
        /// 2관절 IK (BlenderWork/SynthArm/arm_builder.py solve와 같은 계산, 팔꿈치 위): 회전대가 목표를 보고 어깨 · 팔꿈치로 토치 끝을 댐.
        /// weight 0 = 곧게 선 쉬는 자세. arm.Hang: 받침째 고리 바깥 윗모서리를 축으로 90° 넘어가 매달림 (넘어가는 동안은 바깥으로 납작하게 접은 자세),
        /// 다 넘어가면 arm.HangIK만큼 다시 IK (매달린 모델 축 그대로 계산 — 팔꿈치가 바깥쪽, 고리 아래로 돌아 들어감).
        /// </summary>
        private static void Solve(Arm arm, Vector3 target, float weight)
        {
            arm.Turret.localRotation = Quaternion.identity;
            arm.Upper.localRotation = Quaternion.identity;
            arm.Fore.localRotation = Quaternion.identity;
            arm.Head.localRotation = Quaternion.identity;
            // 매달림 (모델 루트를 먼저 놓고 그 축에서 IK)
            float flip = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(arm.Hang / HangFlipPart));
            float drop = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((arm.Hang - HangFlipPart) / (1f - HangFlipPart)));
            var hinge = Quaternion.AngleAxis(-90f * flip, Vector3.right);
            arm.Model.localRotation = hinge * arm.ModelRotation;
            arm.Model.localPosition = StowHinge + hinge * -StowHinge + Vector3.down * (StowDrop * drop);
            float fold = Mathf.Clamp01(arm.Hang * 4f) * (1f - arm.HangIK);

            var axis = Vector3.Cross(ArmUp, ArmForward);
            Quaternion turret = Quaternion.identity, upper = Quaternion.identity, fore = Quaternion.identity;
            if (weight > 0.001f && fold < 0.999f)
            {
                // 충돌 피하기 (사용자 피드백 2026-10-13 — 팔이 고리 · 재료 모듈을 뚫음): 팔꿈치 위 · 아래 두 해를 모두 보고, 윗팔 · 아래팔이
                // 고리 몸통이나 모듈 면 안쪽에 들어가지 않는 해를 고름. 둘 다 들어가면 목표를 조금씩 옮겨(고리 위 = 위로, 매달림 = 아래로) 다시.
                float shiftDir = arm.Hang >= 0.5f ? -1f : 1f;
                float best = float.PositiveInfinity;
                for (int attempt = 0; attempt < 5 && best > 1e-5f; attempt++)
                {
                    var t = target + Vector3.up * (shiftDir * 0.035f * attempt);
                    // 회전대: 모델 루트 로컬에서 목표 쪽으로 (위 축 기준)
                    var p = arm.Model.InverseTransformPoint(t) - arm.Turret.localPosition;
                    var flat = p - ArmUp * Vector3.Dot(p, ArmUp);
                    float yaw = flat.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(ArmForward, flat, ArmUp) : 0f;
                    var turretTry = Quaternion.AngleAxis(yaw, ArmUp);
                    arm.Turret.localRotation = turretTry;
                    // 어깨 · 팔꿈치: 회전대 로컬 (앞 = ArmForward, 위 = ArmUp)
                    var q = arm.Turret.InverseTransformPoint(t) - arm.Upper.localPosition;
                    float f = Vector3.Dot(q, ArmForward), u = Vector3.Dot(q, ArmUp);
                    float l1 = arm.L1, l2 = arm.L2;
                    float d = Mathf.Clamp(Mathf.Sqrt(f * f + u * u), Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
                    float a = Mathf.Atan2(f, u);
                    float b = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f));
                    float g = Mathf.Acos(Mathf.Clamp((l1 * l1 + l2 * l2 - d * d) / (2f * l1 * l2), -1f, 1f));
                    var shoulder = arm.Upper.position;
                    var up = arm.Turret.TransformDirection(ArmUp);
                    var fwd = arm.Turret.TransformDirection(ArmForward);
                    for (int side = 0; side < 2; side++)
                    {
                        float lean = side == 0 ? a - b : a + b; // 0 = 팔꿈치 위, 1 = 반대쪽
                        var elbow = shoulder + (up * Mathf.Cos(lean) + fwd * Mathf.Sin(lean)) * l1;
                        float score = Collision(arm, shoulder, elbow, t);
                        if (score < best - 1e-6f) // 같으면 먼저 본 팔꿈치 위
                        {
                            best = score;
                            turret = turretTry;
                            upper = Quaternion.AngleAxis(lean * Mathf.Rad2Deg, axis);
                            fore = Quaternion.AngleAxis((Mathf.PI - g) * (side == 0 ? 1f : -1f) * Mathf.Rad2Deg, axis);
                        }
                    }
                }
                turret = Quaternion.Slerp(Quaternion.identity, turret, weight);
                upper = Quaternion.Slerp(Quaternion.identity, upper, weight);
                fore = Quaternion.Slerp(Quaternion.identity, fore, weight);
            }
            if (fold > 0.001f)
            {
                // 넘어가는 동안: 윗팔을 바깥(−앞)으로 눕히고 아래팔을 그 위로 되접음 (고리 위 약 0.2 높이의 납작한 자세)
                float s = Mathf.SmoothStep(0f, 1f, fold);
                turret = Quaternion.Slerp(turret, Quaternion.identity, s);
                upper = Quaternion.Slerp(upper, Quaternion.AngleAxis(-90f, axis), s);
                fore = Quaternion.Slerp(fore, Quaternion.AngleAxis(-186f, axis), s);
            }
            arm.Turret.localRotation = turret;
            arm.Upper.localRotation = upper;
            arm.Fore.localRotation = fore;
        }

        private const float ArmRadius = 0.024f;        // 팔 마디 반 두께 (충돌 여유 — 어깨 관절 원판 반지름 0.022. 0.014면 1.3cm 박혔음, 감시 측정)

        /// <summary>
        /// 팔 자세 하나의 충돌 깊이 합 (받침 로컬: 위 +y, 안쪽 +z, 고리 몸통 = y ±0.042 · z −0.115 ~ 0, 모듈 면 = z FaceInset).
        /// 윗팔(어깨→팔꿈치) · 아래팔(팔꿈치→토치 끝)을 따라 점을 찍어 봄. 토치 끝 주변 5cm는 면에 닿아야 하므로 모듈 검사에서 뺌.
        /// </summary>
        private static float Collision(Arm arm, Vector3 shoulder, Vector3 elbow, Vector3 tip)
        {
            float sum = 0f;
            for (int seg = 0; seg < 2; seg++)
            {
                var a = seg == 0 ? shoulder : elbow;
                var b = seg == 0 ? elbow : tip;
                for (int i = 0; i <= 6; i++)
                {
                    var w = Vector3.Lerp(a, b, i / 6f);
                    var l = arm.Mount.InverseTransformPoint(w);
                    // 고리 몸통
                    float ry = (RingHalfHeight + ArmRadius) - Mathf.Abs(l.y);
                    float rz = Mathf.Min(l.z - (-0.115f - ArmRadius), ArmRadius - l.z);
                    if (ry > 0f && rz > 0f)
                        sum += Mathf.Min(ry, rz);
                    // 모듈 면 안쪽 (토치 끝 근처 제외)
                    float mz = l.z - (FaceInset - ArmRadius);
                    if (mz > 0f && (w - tip).sqrMagnitude > 0.0025f)
                        sum += mz;
                }
            }
            return sum;
        }

        /// <summary>팔이 매달린 마지막 구간: 고리 안쪽 가장자리(경계)를 따라서도 용접 불꽃이 튐.</summary>
        private void RingWeld(Fx fx, float seam, float gameDt, bool paused)
        {
            float hang = 0f;
            foreach (var arm in fx.Arms)
                hang = Mathf.Max(hang, arm.Hang);
            if (fx.RingWeld == null || paused || gameDt <= 0f || hang < 0.5f)
                return;
            int n = Mathf.Min(6, Mathf.FloorToInt(gameDt * 60f * hang + Random.value));
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < n; i++)
            {
                PathPoint(fx, Random.Range(0f, fx.Perimeter), out var p, out _, out _, out var inward);
                var w = fx.Ring.TransformPoint(p + inward * (FaceInset + 0.01f));
                ep.position = new Vector3(w.x, seam + 0.004f, w.z);
                ep.velocity = (Vector3.up * Random.Range(0.4f, 1.2f) - inward * Random.Range(0.1f, 0.5f) + Random.insideUnitSphere * 0.3f);
                fx.RingWeld.Emit(ep, 1);
            }
        }

        // ---------------- 끝 ----------------

        private IEnumerator Outro(Fx fx, bool completed)
        {
            foreach (var arm in fx.Arms)
            {
                var em = arm.Sparks.emission;
                em.rateOverTime = 0f;
                arm.Flash.intensity = 0f;
            }
            if (completed)
                Burst(fx.Center, fx.Half, fx.Top, _plumeColor, Mathf.Clamp(Mathf.RoundToInt((fx.Half.x + fx.Half.y) * 60f), 40, 160));
            // 위가 비어 있는 완료만 위로 날아오름. 위에 모듈이 있거나 취소면 토막마다 제자리에서 흩어짐
            // (사용자 결정 2026-10-13: 고리째 가운데로 줄어들면 완성된 모듈 · 남은 재료를 통과했음)
            if (!(completed && float.IsPositiveInfinity(fx.Ceiling)))
            {
                yield return Dissolve(fx, completed);
                yield break;
            }
            float start = fx.Ring.position.y;
            float t = 0f;
            var scale0 = fx.Ring.localScale;
            while (t < _outroSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / _outroSeconds);
                foreach (var arm in fx.Arms)
                {
                    arm.HangIK = 0f;
                    arm.Hang = Mathf.MoveTowards(arm.Hang, 1f, Time.deltaTime * 4f); // 팔을 접어 고리 바깥으로 제침
                    Solve(arm, arm.Current == Vector3.zero ? arm.Tip.position : arm.Current, fx.Weight);
                }
                // 위로 날아오르며 섬광 → 작아짐
                float y = start + 0.9f * k * k + 0.15f * k;
                fx.Ring.position = new Vector3(fx.Ring.position.x, y, fx.Ring.position.z);
                fx.Ring.localScale = scale0 * Mathf.Lerp(1f, 1.12f, k) * (k > 0.7f ? Mathf.Lerp(1f, 0f, (k - 0.7f) / 0.3f) : 1f);
                float flash = Mathf.Lerp(3.5f, 0f, k);
                foreach (var (r, slot) in fx.GlowSlots)
                {
                    if (r == null)
                        continue;
                    r.GetPropertyBlock(_block, slot);
                    _block.SetColor(EmissionColorId, _glowColor * flash);
                    r.SetPropertyBlock(_block, slot);
                }
                fx.Under.intensity = Mathf.Lerp(4f, 0f, k);
                foreach (var p in fx.Plumes)
                {
                    var e = p.emission;
                    e.rateOverTime = 70f * (1f - k);
                }
                yield return null;
            }
            Destroy(fx.Root.gameObject);
        }

        /// <summary>
        /// 토막마다 제자리에서 흩어짐: 둘레를 따라 차례로(파도처럼) 보라 빛이 번쩍이며 각 토막 · 팔이 자기 원점(고리 경계선 위)으로 작아져 사라짐 + 작은 입자.
        /// 원점이 규격 경계라 작아지는 동안 모듈 안으로 들어가지 않음.
        /// </summary>
        private IEnumerator Dissolve(Fx fx, bool completed)
        {
            const float wave = 0.7f, life = 0.38f;
            var pieces = new List<(Transform T, float Order, Vector3 Scale)>();
            foreach (Transform c in fx.Ring)
            {
                if (c.GetComponentInChildren<Renderer>() == null)
                    continue; // 아래 빛
                float order = Mathf.Repeat(Mathf.Atan2(c.localPosition.z, c.localPosition.x) / (2f * Mathf.PI), 1f);
                pieces.Add((c, order, c.localScale));
            }
            var burst = new bool[pieces.Count];
            float t = 0f;
            while (t < wave + life)
            {
                t += Time.deltaTime;
                for (int i = 0; i < pieces.Count; i++)
                {
                    var (piece, order, scale0) = pieces[i];
                    if (piece == null)
                        continue;
                    float k = (t - order * wave) / life;
                    if (k <= 0f)
                        continue;
                    if (!burst[i])
                    {
                        burst[i] = true;
                        var p = piece.position;
                        Burst(new Vector3(p.x, 0f, p.z), new Vector2(0.08f, 0.08f), p.y + 0.04f, _plumeColor, completed ? 14 : 6);
                    }
                    k = Mathf.Clamp01(k);
                    float s = k < 0.3f ? Mathf.Lerp(1f, 1.15f, k / 0.3f) : Mathf.Lerp(1.15f, 0f, (k - 0.3f) / 0.7f);
                    piece.localScale = scale0 * s;
                }
                float f = Mathf.Clamp01(t / (wave + life));
                float flash = completed ? Mathf.Lerp(3f, 0.5f, f) : Mathf.Lerp(1f, 0.2f, f);
                foreach (var (r, slot) in fx.GlowSlots)
                {
                    if (r == null)
                        continue;
                    r.GetPropertyBlock(_block, slot);
                    _block.SetColor(EmissionColorId, _glowColor * flash);
                    r.SetPropertyBlock(_block, slot);
                }
                fx.Under.intensity = completed ? Mathf.Lerp(3f, 0f, f) : Mathf.Lerp(0.8f, 0f, f);
                foreach (var p in fx.Plumes)
                {
                    if (p == null)
                        continue;
                    var e = p.emission;
                    e.rateOverTime = 20f * (1f - f);
                }
                yield return null;
            }
            Destroy(fx.Root.gameObject);
        }
    }
}
