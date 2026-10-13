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
    /// - 고리 위 받침의 기계팔(규격 크기에 따라 2~4개)이 경계 바로 위 재료 면을 오가며 용접(불꽃). 2관절 IK.
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
        private static readonly Vector3[] StraightNozzles = { new Vector3(-0.25f, -0.076f, -0.0575f), new Vector3(0.25f, -0.076f, -0.0575f) };
        private static readonly Vector3 CornerDisc = new Vector3(0.0615f, -0.082f, -0.0615f);
        private static readonly Vector3 CornerOutward = new Vector3(1f, 0f, -1f);
        // 팔 관절 축 (팔 모델 루트 로컬 = Blender 축: 위 = −z, 안쪽 = +y)
        private static readonly Vector3 ArmUp = new Vector3(0f, 0f, -1f);
        private static readonly Vector3 ArmForward = new Vector3(0f, 1f, 0f);

        private sealed class Arm
        {
            public Transform Model, Turret, Upper, Fore, Head, Tip;
            public float L1, L2;
            public Vector3 Inward, Along;      // 월드 (고리 기준 · 처음 방향)
            public float AlongOffset;          // 고리 중심에서 받침까지 변 방향 거리
            public float SideHalf;             // 이 변 반 길이
            public Vector3 Goal, Current, Velocity;
            public float NextPick, Weld;
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

            fx.Root = new GameObject($"SynthesisFx {job.Recipe.name}").transform;
            fx.Root.SetParent(transform, false);
            fx.Ring = new GameObject("Ring").transform;
            fx.Ring.SetParent(fx.Root, false);
            BuildRing(fx, Mathf.RoundToInt(fx.Half.x * 2f / cs), Mathf.RoundToInt(fx.Half.y * 2f / cs));

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

            // 팔: 두 변 모두 2칸 이상이면 네 변에 하나씩, 아니면 긴 두 변 (같으면 z 쪽 변)
            bool four = nx >= 2 && nz >= 2;
            bool xSides = nz > nx; // 긴 변이 z 방향이면 x 쪽(왼 · 오른) 변에 팔
            var armSides = new List<int>();
            if (four || !xSides) { armSides.Add(0); armSides.Add(1); }
            if (four || xSides) { armSides.Add(2); armSides.Add(3); }
            for (int k = 0; k < armSides.Count; k++)
            {
                var side = sides[armSides[k]];
                float half = side.Count * cs * 0.5f;
                // 마주 보는 변의 팔은 서로 엇갈리게 (변 가운데에서 반대 방향으로 약 0.2칸)
                float offset = (k % 2 == 0 ? -0.18f : 0.18f) * Mathf.Min(1f, half);
                var go = Instantiate(_arm, fx.Ring);
                go.transform.localPosition = side.Start + side.Along * (half + offset);
                go.transform.localRotation = Quaternion.LookRotation(-side.Out, Vector3.up);
                Collect(fx, go);
                fx.Arms.Add(MakeArm(go.transform, -side.Out, side.Along, offset, half));
            }
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

        private Arm MakeArm(Transform root, Vector3 inward, Vector3 along, float alongOffset, float sideHalf)
        {
            var arm = new Arm
            {
                Model = Find(root, "SM_SynthArm"),
                Turret = Find(root, "SynthArm_Turret"),
                Upper = Find(root, "SynthArm_Upper"),
                Fore = Find(root, "SynthArm_Fore"),
                Head = Find(root, "SynthArm_Head"),
                Tip = Find(root, "SynthArm_Tip"),
                Inward = inward,
                Along = along,
                AlongOffset = alongOffset,
                SideHalf = sideHalf,
            };
            arm.L1 = arm.Fore.localPosition.magnitude;
            arm.L2 = arm.Head.localPosition.magnitude + arm.Tip.localPosition.magnitude;
            arm.NextPick = 0f;
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
            fx.Ring.position = new Vector3(fx.Center.x, seam - RingHalfHeight + rise + bob, fx.Center.z);
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
        }

        private void UpdateArm(Fx fx, Arm arm, float seam, float gameDt, bool paused)
        {
            if (gameDt > 0f && !paused && fx.Weight > 0.99f)
            {
                arm.NextPick -= gameDt;
                if (arm.NextPick <= 0f)
                {
                    // 경계 바로 위 재료 면 한 점 (받침 근처 변 위, 팔이 닿는 범위)
                    float span = Mathf.Min(arm.SideHalf - 0.08f, 0.38f);
                    float along = Mathf.Clamp(arm.AlongOffset + Random.Range(-span, span), -arm.SideHalf + 0.08f, arm.SideHalf - 0.08f);
                    var mount = arm.Model.parent.position; // 받침 = 고리 변 위
                    var sideCenter = mount - arm.Along * arm.AlongOffset;
                    arm.Goal = sideCenter + arm.Along * along + arm.Inward * (FaceInset + 0.012f);
                    arm.Goal.y = seam + Random.Range(0.025f, 0.11f);
                    arm.NextPick = Random.Range(0.55f, 1.1f);
                }
                arm.Goal.y = Mathf.Max(arm.Goal.y, seam + 0.02f);
                if (arm.Current == Vector3.zero)
                    arm.Current = arm.Goal;
                arm.Current = Vector3.SmoothDamp(arm.Current, arm.Goal, ref arm.Velocity, 0.18f, 3f, gameDt);
                bool welding = (arm.Current - arm.Goal).sqrMagnitude < 0.0004f;
                arm.Weld = Mathf.MoveTowards(arm.Weld, welding ? 1f : 0f, gameDt * 8f);
            }
            else if (paused || gameDt <= 0f)
                arm.Weld = Mathf.MoveTowards(arm.Weld, 0f, Time.deltaTime * 6f);

            Solve(arm, arm.Current == Vector3.zero ? arm.Tip.position : arm.Current, fx.Weight);

            var em = arm.Sparks.emission;
            em.rateOverTime = arm.Weld * 70f;
            arm.Flash.intensity = arm.Weld * (0.6f + 0.6f * Mathf.PerlinNoise(Time.time * 18f, arm.L1 * 10f));
        }

        /// <summary>2관절 IK (BlenderWork/SynthArm/arm_builder.py solve와 같은 계산): 회전대가 목표를 보고 어깨 · 팔꿈치로 토치 끝을 댐 (팔꿈치 위). weight 0 = 곧게 선 쉬는 자세.</summary>
        private static void Solve(Arm arm, Vector3 target, float weight)
        {
            arm.Turret.localRotation = Quaternion.identity;
            arm.Upper.localRotation = Quaternion.identity;
            arm.Fore.localRotation = Quaternion.identity;
            arm.Head.localRotation = Quaternion.identity;
            if (weight <= 0.001f)
                return;
            // 회전대: 모델 루트 로컬에서 목표 쪽으로 (위 축 기준)
            var p = arm.Model.InverseTransformPoint(target) - arm.Turret.localPosition;
            var flat = p - ArmUp * Vector3.Dot(p, ArmUp);
            float yaw = flat.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(ArmForward, flat, ArmUp) : 0f;
            arm.Turret.localRotation = Quaternion.AngleAxis(yaw * weight, ArmUp);
            // 어깨 · 팔꿈치: 회전대 로컬 (앞 = ArmForward, 위 = ArmUp)
            var q = arm.Turret.InverseTransformPoint(target) - arm.Upper.localPosition;
            float f = Vector3.Dot(q, ArmForward), u = Vector3.Dot(q, ArmUp);
            float l1 = arm.L1, l2 = arm.L2;
            float d = Mathf.Clamp(Mathf.Sqrt(f * f + u * u), Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
            float a = Mathf.Atan2(f, u);
            float b = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f));
            float g = Mathf.Acos(Mathf.Clamp((l1 * l1 + l2 * l2 - d * d) / (2f * l1 * l2), -1f, 1f));
            var axis = Vector3.Cross(ArmUp, ArmForward);
            arm.Upper.localRotation = Quaternion.AngleAxis((a - b) * Mathf.Rad2Deg * weight, axis);
            arm.Fore.localRotation = Quaternion.AngleAxis((Mathf.PI - g) * Mathf.Rad2Deg * weight, axis);
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
            while (t < _outroSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / _outroSeconds);
                float fold = 1f - Mathf.Clamp01(k * 3f);
                foreach (var arm in fx.Arms)
                    Solve(arm, arm.Current == Vector3.zero ? arm.Tip.position : arm.Current, fold * fx.Weight);
                // 완료: 위로 날아오르며 섬광 → 작아짐, 취소: 내려앉으며 작아짐
                float y = completed ? start + 0.9f * k * k + 0.15f * k : start - 0.35f * k;
                fx.Ring.position = new Vector3(fx.Ring.position.x, y, fx.Ring.position.z);
                fx.Ring.localScale = scale0 * Mathf.Lerp(1f, completed ? 1.12f : 0.9f, k) * (k > 0.7f ? Mathf.Lerp(1f, 0f, (k - 0.7f) / 0.3f) : 1f);
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
