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
    /// - 고리 위 받침의 기계팔(규격 크기에 따라 2~4개)이 고리를 따라 둘레를 돌며(모서리에서 90° 회전) 멈춰 서서
    ///   경계 바로 위 재료 면을 용접(불꽃). 2관절 IK. 앞 팔과 가까우면 기다림.
    /// - 바로 위층에 모듈이 있으면 팔이 뚫지 않게: 여유가 줄면 팔꿈치 아래 자세 → 더 줄면 팔을 접어 고리 속으로, 고리 안쪽 가장자리 불꽃이 마무리.
    ///   완료 때도 위로 날아오르지 않고 제자리에서 퍼지며 사라짐.
    /// - 고리는 살짝 출렁 · 기울었다 바로잡힘(비행 느낌), 일시 정지(파손 재료)면 추진 약해지고 살짝 처지며 팔 · 불꽃 멈춤.
    /// - 완료: 섬광 · 불꽃 폭발, 고리가 위로 날아올라 사라짐. 취소(재료 파괴): 고리가 내려앉으며 사라짐.
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
            public float L1, L2;
            // 12-3b 피드백: 고리 둘레를 따라 이동 (S = 둘레 위치, Stop = 다음에 멈출 위치) → 멈춰서 용접 → 다음 자리
            public float S, Stop;
            public bool Moving;
            public float Timer;
            public Vector3 Goal, Current, Velocity;
            public float Weld;
            // 위 모듈 피하기: ElbowDown 0 = 팔꿈치 위(기본), 1 = 아래(낮은 자세) / Stow 0 = 작업, 1 = 접어서 고리 속으로
            public float ElbowDown, Stow;
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
        }

        private readonly Dictionary<SynthesisJob, Fx> _fx = new Dictionary<SynthesisJob, Fx>();
        private readonly List<Fx> _scratch = new List<Fx>();
        private MaterialPropertyBlock _block;
        private SynthesisSystem _synthesis;
        private Transform _stash;              // 미리보기 프리팹을 깨우지 않고 정리하는 비활성 부모

        private void Start()
        {
            _block = new MaterialPropertyBlock();
            _synthesis = _station != null && _station.Simulation != null ? _station.Simulation.Synthesis : null;
            if (_synthesis == null || _clipShader == null || _ringStraight == null || _ringCorner == null || _arm == null)
            {
                enabled = false;
                return;
            }
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

            // 팔: 두 변 모두 2칸 이상이면 4개, 아니면 2개 — 둘레를 같은 간격으로 나눠 출발, 모두 같은 방향으로 고리를 따라 돎 (12-3b 피드백)
            fx.Perimeter = 4f * (hx + hz) + 4f * CornerTurn;
            int count = nx >= 2 && nz >= 2 ? 4 : 2;
            for (int k = 0; k < count; k++)
            {
                var go = Instantiate(_arm, fx.Ring);
                Collect(fx, go);
                var arm = MakeArm(go.transform);
                // 첫 팔은 첫 변 가운데쯤에서 출발 (모서리 회전 구간은 피함)
                arm.S = arm.Stop = Wrap(fx, hx + fx.Perimeter * k / count);
                if (InCorner(fx, arm.S))
                    arm.S = arm.Stop = Wrap(fx, arm.S + CornerTurn + 0.1f);
                arm.Timer = Random.Range(0.2f, 0.8f);
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
            fx.Swapped.Clear();
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
            // 천장은 생길 수 있는 높이가 정해져 있음 (맨 위 경계 + 0.04) → 경계가 그 아래 여유(팔꿈치 자세 전환 + 0.08)에 들어온 뒤부터만
            // 0.5초마다 확인 (사용자 결정 2026-10-13: 그 전에는 확인할 필요 없음). 합성 중 위에 새로 지은 모듈도 이때 잡힘
            float seamNow = Mathf.Lerp(fx.Bottom, fx.Top, fx.Visual);
            if (CeilingHeight(fx) - seamNow < ElbowDownClearance + 0.08f)
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

        // 팔 이동 · 용접 (12-3b 피드백 1: 고리를 따라 움직이며 작업)
        private const float ArmSpeed = 0.45f;          // 둘레를 따라 칸/게임 초
        private const float ArmMinGap = 0.55f;         // 앞 팔과 이만큼 가까우면 기다림
        // 위 모듈 피하기 (12-3b 피드백 2): 고리 윗면 ~ 위 모듈 밑면 여유가 이보다 작으면
        private const float ElbowDownClearance = 0.5f;  // 팔꿈치 위 자세(고리 위 약 0.4) → 팔꿈치 아래 자세(약 0.16). 측정: 0.42면 전환 직전 천장과 2cm

        private const float StowClearance = 0.2f;      // → 팔을 접어 고리 속으로 (고리 위 약 0.06), 고리 안쪽 가장자리 불꽃이 마무리
        private const float StowSink = 0.11f;          // 접을 때 받침째 고리 속으로 내려가는 깊이

        private void UpdateArm(Fx fx, Arm arm, float seam, float gameDt, bool paused)
        {
            float clearance = fx.Ceiling - seam;
            if (gameDt > 0f)
            {
                arm.ElbowDown = Mathf.MoveTowards(arm.ElbowDown, clearance < ElbowDownClearance ? 1f : 0f, gameDt * 2f);
                arm.Stow = Mathf.MoveTowards(arm.Stow, clearance < StowClearance ? 1f : 0f, gameDt * 2.5f);
            }
            bool working = gameDt > 0f && !paused && fx.Weight > 0.99f && arm.Stow < 0.01f;
            if (working)
            {
                if (arm.Moving)
                {
                    float left = Wrap(fx, arm.Stop - arm.S);
                    if (GapAhead(fx, arm) > ArmMinGap)
                        arm.S = Wrap(fx, arm.S + Mathf.Min(left, ArmSpeed * gameDt));
                    if (Wrap(fx, arm.Stop - arm.S) < 1e-3f || left > fx.Perimeter - 1e-3f)
                    {
                        arm.Moving = false;
                        arm.Timer = Random.Range(0.8f, 1.5f);
                        PickWeld(fx, arm, seam);
                    }
                }
                else
                {
                    arm.Timer -= gameDt;
                    if (arm.Timer <= 0f)
                    {
                        // 다음 자리: 0.3~0.65칸 앞 (모서리 회전 구간에 멈추지 않게)
                        arm.Stop = Wrap(fx, arm.S + Random.Range(0.3f, 0.65f));
                        if (InCorner(fx, arm.Stop))
                            arm.Stop = Wrap(fx, arm.Stop + CornerTurn + 0.12f);
                        arm.Moving = true;
                    }
                    else if (Random.value < gameDt * 1.6f)
                        PickWeld(fx, arm, seam); // 용접 중에도 조금씩 옮겨 가며
                }
                PlaceMount(fx, arm);
                if (arm.Moving)
                {
                    // 이동 중: 토치를 면 앞으로 들고 따라감
                    arm.Goal = arm.Mount.position + arm.Mount.forward * (FaceInset + 0.07f);
                    arm.Goal.y = seam + Mathf.Lerp(0.18f, 0.08f, arm.ElbowDown);
                }
                arm.Goal.y = Mathf.Max(arm.Goal.y, seam + 0.02f);
                if (arm.Current == Vector3.zero)
                    arm.Current = arm.Goal;
                arm.Current = Vector3.SmoothDamp(arm.Current, arm.Goal, ref arm.Velocity, 0.16f, 3f, gameDt);
                bool welding = !arm.Moving && (arm.Current - arm.Goal).sqrMagnitude < 0.0004f;
                arm.Weld = Mathf.MoveTowards(arm.Weld, welding ? 1f : 0f, gameDt * 8f);
            }
            else
            {
                PlaceMount(fx, arm);
                arm.Weld = Mathf.MoveTowards(arm.Weld, 0f, Time.deltaTime * 6f);
            }

            Solve(arm, arm.Current == Vector3.zero ? arm.Tip.position : arm.Current, fx.Weight);

            var em = arm.Sparks.emission;
            em.rateOverTime = arm.Weld * 70f;
            arm.Flash.intensity = arm.Weld * (0.6f + 0.6f * Mathf.PerlinNoise(Time.time * 18f, arm.L1 * 10f));
        }

        /// <summary>지금 자리 근처 재료 면의 용접점 (경계 바로 위). 낮은 자세면 더 낮게.</summary>
        private static void PickWeld(Fx fx, Arm arm, float seam)
        {
            PlaceMount(fx, arm);
            arm.Goal = arm.Mount.position + arm.Mount.right * Random.Range(-0.12f, 0.12f) + arm.Mount.forward * (FaceInset + 0.012f);
            arm.Goal.y = seam + (arm.ElbowDown > 0.5f ? Random.Range(0.02f, 0.05f) : Random.Range(0.025f, 0.11f));
        }

        /// <summary>같은 방향으로 도는 팔 중 바로 앞 팔까지 둘레 거리.</summary>
        private static float GapAhead(Fx fx, Arm arm)
        {
            float best = float.PositiveInfinity;
            foreach (var other in fx.Arms)
            {
                if (other == arm)
                    continue;
                float d = Wrap(fx, other.S - arm.S);
                if (d > 1e-4f && d < best)
                    best = d;
            }
            return best;
        }

        /// <summary>
        /// 2관절 IK (BlenderWork/SynthArm/arm_builder.py solve와 같은 계산): 회전대가 목표를 보고 어깨 · 팔꿈치로 토치 끝을 댐.
        /// weight 0 = 곧게 선 쉬는 자세. arm.ElbowDown 1 = 팔꿈치 아래 자세(바깥 아래로 돌아 올라옴 — 위 모듈 아래 좁은 틈),
        /// arm.Stow 1 = 접어서 받침째 고리 속으로.
        /// </summary>
        private static void Solve(Arm arm, Vector3 target, float weight)
        {
            arm.Turret.localRotation = Quaternion.identity;
            arm.Upper.localRotation = Quaternion.identity;
            arm.Fore.localRotation = Quaternion.identity;
            arm.Head.localRotation = Quaternion.identity;
            arm.Model.localPosition = Vector3.zero;
            var axis = Vector3.Cross(ArmUp, ArmForward);
            Quaternion turret = Quaternion.identity, upper = Quaternion.identity, fore = Quaternion.identity;
            if (weight > 0.001f && arm.Stow < 0.999f)
            {
                // 회전대: 모델 루트 로컬에서 목표 쪽으로 (위 축 기준)
                var p = arm.Model.InverseTransformPoint(target) - arm.Turret.localPosition;
                var flat = p - ArmUp * Vector3.Dot(p, ArmUp);
                float yaw = flat.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(ArmForward, flat, ArmUp) : 0f;
                turret = Quaternion.AngleAxis(yaw, ArmUp);
                arm.Turret.localRotation = turret;
                // 어깨 · 팔꿈치: 회전대 로컬 (앞 = ArmForward, 위 = ArmUp)
                var q = arm.Turret.InverseTransformPoint(target) - arm.Upper.localPosition;
                float f = Vector3.Dot(q, ArmForward), u = Vector3.Dot(q, ArmUp);
                float l1 = arm.L1, l2 = arm.L2;
                float d = Mathf.Clamp(Mathf.Sqrt(f * f + u * u), Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
                float a = Mathf.Atan2(f, u);
                float b = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f));
                float g = Mathf.Acos(Mathf.Clamp((l1 * l1 + l2 * l2 - d * d) / (2f * l1 * l2), -1f, 1f));
                float side = 1f - 2f * arm.ElbowDown; // +1 팔꿈치 위, −1 아래
                upper = Quaternion.AngleAxis((a - b * side) * Mathf.Rad2Deg, axis);
                fore = Quaternion.AngleAxis((Mathf.PI - g) * side * Mathf.Rad2Deg, axis);
                turret = Quaternion.Slerp(Quaternion.identity, turret, weight);
                upper = Quaternion.Slerp(Quaternion.identity, upper, weight);
                fore = Quaternion.Slerp(Quaternion.identity, fore, weight);
            }
            if (arm.Stow > 0.001f)
            {
                // 접은 자세: 고리를 따라 눕고(회전대 90°) 아래팔을 윗팔 위로 접음, 받침째 고리 속으로
                float s = Mathf.SmoothStep(0f, 1f, arm.Stow);
                turret = Quaternion.Slerp(turret, Quaternion.AngleAxis(90f, ArmUp), s);
                upper = Quaternion.Slerp(upper, Quaternion.AngleAxis(86f, axis), s);
                fore = Quaternion.Slerp(fore, Quaternion.AngleAxis(172f, axis), s);
                arm.Model.localPosition = Vector3.down * (StowSink * s);
            }
            arm.Turret.localRotation = turret;
            arm.Upper.localRotation = upper;
            arm.Fore.localRotation = fore;
        }

        /// <summary>팔을 다 접은 마지막 구간: 고리 안쪽 가장자리(경계)를 따라 용접 불꽃이 튐.</summary>
        private void RingWeld(Fx fx, float seam, float gameDt, bool paused)
        {
            float stow = 0f;
            foreach (var arm in fx.Arms)
                stow = Mathf.Max(stow, arm.Stow);
            if (fx.RingWeld == null || paused || gameDt <= 0f || stow < 0.5f)
                return;
            int n = Mathf.Min(6, Mathf.FloorToInt(gameDt * 90f * stow + Random.value));
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
            float start = fx.Ring.position.y;
            float t = 0f;
            var scale0 = fx.Ring.localScale;
            // 위에 모듈이 있으면 위로 날아오르지 않음 (뚫고 지나가지 않게) — 제자리에서 퍼지며 사라짐
            bool fly = completed && float.IsPositiveInfinity(fx.Ceiling);
            while (t < _outroSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / _outroSeconds);
                foreach (var arm in fx.Arms)
                {
                    arm.Stow = Mathf.MoveTowards(arm.Stow, 1f, Time.deltaTime * 4f); // 팔을 접어 넣음
                    Solve(arm, arm.Current == Vector3.zero ? arm.Tip.position : arm.Current, fx.Weight);
                }
                // 완료: 위로 날아오르며 섬광 → 작아짐 (위가 막혔으면 제자리), 취소: 내려앉으며 작아짐
                float y = fly ? start + 0.9f * k * k + 0.15f * k : completed ? start : start - 0.35f * k;
                fx.Ring.position = new Vector3(fx.Ring.position.x, y, fx.Ring.position.z);
                fx.Ring.localScale = scale0 * Mathf.Lerp(1f, completed ? (fly ? 1.12f : 1.2f) : 0.9f, k) * (k > 0.7f ? Mathf.Lerp(1f, 0f, (k - 0.7f) / 0.3f) : 1f);
                float flash = completed ? Mathf.Lerp(3.5f, 0f, k) : Mathf.Lerp(1f, 0f, k);
                foreach (var (r, slot) in fx.GlowSlots)
                {
                    if (r == null)
                        continue;
                    r.GetPropertyBlock(_block, slot);
                    _block.SetColor(EmissionColorId, _glowColor * flash);
                    r.SetPropertyBlock(_block, slot);
                }
                fx.Under.intensity = completed ? Mathf.Lerp(4f, 0f, k) : Mathf.Lerp(0.8f, 0f, k);
                foreach (var p in fx.Plumes)
                {
                    var e = p.emission;
                    e.rateOverTime = completed ? 70f * (1f - k) : 10f * (1f - k);
                }
                yield return null;
            }
            Destroy(fx.Root.gameObject);
        }
    }
}
