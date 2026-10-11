using System;
using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Simulation;
using SpaceStation.UI;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-17 ① 현장 긴급 수리: 파손된 방의 벽에 손상 지점 2~3곳(BALANCE 28번)을 놓는다 — 뜯긴 점검 패널 · 터진 배관 · 불탄 배전함 소품에서
    /// 스파크 · 연기 · 김이 나와 "왜 연기가 나는지" 보이게 (사용자 피드백: 파손된 모습이 보여야 납득됨).
    /// 바라보고 F를 길게 누르면 그 지점을 고침(덧댄 판으로 바뀜). 마지막 지점에서 현장 수리 비용(보통 수리 × 0.5, 이미 냈으면 0)을 내고
    /// 수리 슬롯 · 시간 없이 바로 복구 (<see cref="StationSimulation.TryFieldRepair"/>). 바깥에서 수리가 끝나도 남은 지점은 덧댄 판으로.
    /// 지점 자리는 모듈 원점으로 정한 씨앗으로 고르므로 내부를 다시 만들어도 같은 자리. 고친 지점은 <see cref="DamageInfo.FieldFixedMask"/>.
    /// </summary>
    public sealed class InteriorFieldRepair : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("뜯긴 점검 패널 (BlenderWork/InteriorProps/damage_builder.py)")]
            public GameObject Panel;
            [Tooltip("터진 배관")]
            public GameObject Pipe;
            [Tooltip("불탄 배전함")]
            public GameObject Box;
            [Tooltip("고친 자리 (덧댄 판)")]
            public GameObject Patch;
            [Tooltip("손상 지점 가운데 높이 (바닥에서, m)")]
            public float Height = 1.35f;
            [Tooltip("지점끼리 최소 거리 (m)")]
            public float MinSpacing = 1.8f;
        }

        private enum Kind { Panel, Pipe, Box }

        private sealed class Point
        {
            public int Index;
            public Kind Kind;
            public Vector3 Position, Normal;
            public GameObject Prop;
            public ParticleSystem Plume;
            public Light Glow;
            public float Seed;
            public float NextSpark;
            public float NextWeld;
            public bool Fixed;
        }

        private sealed class Site
        {
            public ModuleInstance Module;
            public Transform Parent;
            public readonly List<Point> Points = new List<Point>();
            public bool Done; // 수리 끝 (남은 덧댄 판만)
        }

        private static readonly Color SmokeColor = new Color(0.22f, 0.21f, 0.2f, 1f);
        private static readonly Color SteamColor = new Color(0.5f, 0.53f, 0.56f, 0.7f); // 밝으면 지점 불빛 · Bloom에 하얗게 탔음

        private StationController _station;
        private InteriorBuilder _builder;
        private Settings _s;
        private Material _smoke, _spark;
        private InteriorLayout _layout;
        private readonly Dictionary<ModuleInstance, Site> _sites = new Dictionary<ModuleInstance, Site>();
        private readonly List<(Vector3 Position, Vector3 Normal)> _candidates = new List<(Vector3, Vector3)>();

        /// <summary>플레이어가 있는 방 (스파크 소리는 이 방에서만).</summary>
        public ModuleInstance CurrentRoom { get; set; }
        /// <summary>알림 (글, 실패 여부).</summary>
        public event Action<string, bool> Message;

        private StationSimulation Sim => _station.Simulation;

        public static InteriorFieldRepair Create(Transform parent, StationController station, InteriorBuilder builder, Settings settings, Material smoke, Material spark)
        {
            var go = new GameObject("InteriorFieldRepair");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<InteriorFieldRepair>();
            r._station = station;
            r._builder = builder;
            r._s = settings ?? new Settings();
            r._smoke = smoke;
            r._spark = spark;
            station.Simulation.Damage.Damaged += r.HandleDamaged;
            station.Simulation.Damage.Repaired += r.HandleRepaired;
            return r;
        }

        private void OnDestroy()
        {
            if (_station != null && _station.Simulation != null)
            {
                Sim.Damage.Damaged -= HandleDamaged;
                Sim.Damage.Repaired -= HandleRepaired;
            }
        }

        /// <summary>남은(고치지 않은) 손상 지점이 있는 방인지 — 있으면 방 연출(<see cref="InteriorAtmosphere"/>)은 지점에서만 연기 · 스파크.</summary>
        public bool HasPoints(ModuleInstance module) => module != null && _sites.TryGetValue(module, out var site) && !site.Done && site.Points.Count > 0;

        /// <summary>내부를 (다시) 만든 직후. 소품은 방 오브젝트 아래라 이미 지워졌음 → 파손된 방마다 다시 놓음.</summary>
        public void Rebuild(InteriorLayout layout)
        {
            _layout = layout;
            _sites.Clear();
            if (layout == null)
                return;
            foreach (var room in layout.Rooms)
            {
                if (Sim.Damage.TryGetInfo(room.Module, out var info))
                    Spawn(room.Module, info);
            }
        }

        private void HandleDamaged(DamageInfo info)
        {
            if (_layout != null && _layout.Contains(info.Module) && !_sites.ContainsKey(info.Module))
                Spawn(info.Module, info);
        }

        /// <summary>바깥 수리 · 현장 수리로 복구: 남은 손상 소품은 덧댄 판으로 (정비 인력이 고친 자리).</summary>
        private void HandleRepaired(ModuleInstance module)
        {
            if (!_sites.TryGetValue(module, out var site))
                return;
            foreach (var p in site.Points)
            {
                if (!p.Fixed)
                    Fix(site, p, quiet: true);
            }
            site.Done = true;
            _sites.Remove(module);
        }

        // ---------------- 지점 놓기 ----------------

        private void Spawn(ModuleInstance module, DamageInfo info)
        {
            var parent = _builder.RoomParent(module);
            if (parent == null || _s.Panel == null || _s.Pipe == null || _s.Box == null)
                return;
            var site = new Site { Module = module, Parent = parent };
            int seed = module.Origin.x * 73856093 ^ module.Origin.y * 19349663 ^ module.Origin.z * 83492791;
            var rng = new System.Random(seed);
            InteriorSpots.Walls(_builder, module, rng, _s.Height, _candidates);
            int want = Sim.Balance.FieldRepairPoints(module.Cells.Count);
            var chosen = new List<(Vector3 Position, Vector3 Normal)>();
            foreach (var c in _candidates)
            {
                bool far = true;
                foreach (var o in chosen)
                {
                    if ((o.Position - c.Position).sqrMagnitude < _s.MinSpacing * _s.MinSpacing)
                    {
                        far = false;
                        break;
                    }
                }
                if (!far)
                    continue;
                chosen.Add(c);
                if (chosen.Count >= want)
                    break;
            }
            if (chosen.Count == 0)
                return; // 붙일 벽이 없으면 예전 연출(방 가운데 연기 · 아무 벽 스파크)만
            for (int i = 0; i < chosen.Count; i++)
            {
                var p = new Point
                {
                    Index = i,
                    Kind = (Kind)rng.Next(3),
                    Position = chosen[i].Position,
                    Normal = chosen[i].Normal,
                    Fixed = (info.FieldFixedMask & (1 << i)) != 0,
                    NextSpark = Time.unscaledTime + (float)rng.NextDouble() * 1.5f,
                };
                site.Points.Add(p);
                if (p.Fixed)
                    PlacePatch(site, p);
                else
                    PlaceDamage(site, p);
            }
            _sites[module] = site;
        }

        private GameObject Prefab(Kind kind) => kind == Kind.Panel ? _s.Panel : kind == Kind.Pipe ? _s.Pipe : _s.Box;

        private GameObject PlaceProp(GameObject prefab, Site site, Point p)
        {
            var go = Instantiate(prefab, site.Parent);
            go.transform.SetPositionAndRotation(p.Position, Quaternion.LookRotation(p.Normal, Vector3.up));
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // 벽에 붙은 얇은 소품 — 그림자 여드름 방지
            return go;
        }

        private void PlaceDamage(Site site, Point p)
        {
            p.Prop = PlaceProp(Prefab(p.Kind), site, p);
            p.Prop.name = "Damage" + p.Index;
            var mf = p.Prop.GetComponentInChildren<MeshFilter>();
            var col = p.Prop.AddComponent<BoxCollider>();
            if (mf != null && mf.sharedMesh != null)
            {
                var b = mf.sharedMesh.bounds;
                col.center = b.center;
                col.size = Vector3.Max(b.size, new Vector3(0.3f, 0.3f, 0.12f));
            }
            var it = p.Prop.AddComponent<InteriorInteractable>();
            it.HoldSeconds = Sim.Balance.FieldRepairHoldSeconds;
            it.Prompt = () => PromptFor(site, p);
            it.Blocked = () => BlockedFor(site, p);
            it.Holding = progress => Weld(p);
            it.Completed = () => Complete(site, p);

            // 파손 방은 붉은 비상 조명뿐이라 어두워 지점이 안 보였음 → 지점마다 깜박이는 작은 불빛 (전기 = 주황, 배관 = 흐린 흰빛)
            var fx = p.Prop.transform;
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(fx, false);
            glowGo.transform.localPosition = FxPoint(p.Kind) + new Vector3(0f, 0.3f, 0.5f); // 바로 앞이면 소품이 하얗게 탔음
            p.Glow = glowGo.AddComponent<Light>();
            p.Glow.type = LightType.Point;
            p.Glow.shadows = LightShadows.None;
            // 11-17 피드백: 세기 0.9는 눈이 아플 만큼 셌음 → 은은하게 (벽 한 뼘이 물드는 정도), 색도 덜 쨍하게
            p.Glow.color = p.Kind == Kind.Pipe ? new Color(0.7f, 0.78f, 0.88f) : new Color(1f, 0.62f, 0.36f);
            p.Glow.range = 2.4f;
            p.Seed = UnityEngine.Random.value * 10f;

            // 연기 · 김 (계속)
            var up = Vector3.up;
            switch (p.Kind)
            {
                case Kind.Panel:
                    p.Plume = InteriorFx.Plume(fx, fx.TransformPoint(new Vector3(0f, 0.05f, 0.06f)), p.Normal + up, _smoke, SmokeColor, 5f, 0.35f, 0.25f);
                    break;
                case Kind.Pipe:
                    p.Plume = InteriorFx.Plume(fx, fx.TransformPoint(new Vector3(0f, -0.03f, 0.15f)), p.Normal * 2f + up * 0.3f, _smoke, SteamColor, 8f, 0.28f, 0.7f);
                    break;
                case Kind.Box:
                    p.Plume = InteriorFx.Plume(fx, fx.TransformPoint(new Vector3(0f, 0.22f, 0.12f)), up, _smoke, SmokeColor, 6f, 0.4f, 0.2f);
                    break;
            }
        }

        private void PlacePatch(Site site, Point p)
        {
            if (_s.Patch == null)
                return;
            p.Prop = PlaceProp(_s.Patch, site, p);
            p.Prop.name = "Patch" + p.Index;
        }

        // ---------------- 고치기 ----------------

        private int Remaining(Site site)
        {
            int n = 0;
            foreach (var p in site.Points)
            {
                if (!p.Fixed)
                    n++;
            }
            return n;
        }

        private string PromptFor(Site site, Point p)
        {
            int total = site.Points.Count;
            int done = total - Remaining(site);
            if (Remaining(site) > 1)
                return $"현장 수리  <color={HudText.Muted}>손상 {done + 1}/{total}</color>";
            var cost = Sim.GetFieldRepairCost(site.Module);
            string price = Sim.FieldRepairUsesFreeRepair(site.Module) ? $"<color={HudText.Yellow}>무료 수리권 사용</color>"
                : cost.Count > 0 ? HudText.Cost(cost) : $"<color={HudText.Muted}>수리비 이미 냄</color>";
            return $"현장 수리 마무리  {price}";
        }

        private string BlockedFor(Site site, Point p)
        {
            if (Remaining(site) > 1)
                return null;
            var cost = Sim.GetFieldRepairCost(site.Module);
            return Sim.Resources.CanAfford(cost) ? null : $"자원 부족  {HudText.Cost(cost)}";
        }

        /// <summary>누르는 동안: 지점에서 작은 용접 불꽃 (0.12초마다).</summary>
        private void Weld(Point p)
        {
            if (p.Prop == null || Time.unscaledTime < p.NextWeld)
                return;
            p.NextWeld = Time.unscaledTime + 0.12f;
            var at = p.Prop.transform.TransformPoint(FxPoint(p.Kind)) + UnityEngine.Random.insideUnitSphere * 0.04f;
            InteriorFx.Spark(p.Prop.transform, at, p.Normal + Vector3.up * 0.3f, _spark, 4, 8, 0.8f);
            AudioService.TryPlay(l => l.StormSpark, 0.35f);
        }

        private void Complete(Site site, Point p)
        {
            if (p.Fixed)
                return;
            if (Remaining(site) == 1)
            {
                var result = Sim.TryFieldRepair(site.Module);
                if (result == RepairResult.InsufficientResources)
                {
                    Message?.Invoke("자원이 부족해 마무리하지 못했습니다", true);
                    AudioService.TryPlay(l => l.UiError);
                    return;
                }
                if (result != RepairResult.Completed)
                    return;
                Fix(site, p, quiet: false); // Repaired 이벤트로 이미 덧댄 판이 됐으면 건너뜀
                Message?.Invoke($"{(site.Module.Data != null ? site.Module.Data.DisplayName : "모듈")} 현장 수리 완료", false);
                return;
            }
            Fix(site, p, quiet: false);
            if (Sim.Damage.TryGetInfo(site.Module, out var info))
                info.FieldFixedMask |= 1 << p.Index;
            AudioService.TryPlay(l => l.Maintain);
        }

        private void Fix(Site site, Point p, bool quiet)
        {
            if (p.Fixed)
                return;
            p.Fixed = true;
            if (p.Prop != null)
            {
                if (!quiet)
                    InteriorFx.Spark(site.Parent, p.Prop.transform.TransformPoint(FxPoint(p.Kind)), p.Normal, _spark, 10, 16, 1.2f);
                Destroy(p.Prop);
            }
            p.Plume = null;
            PlacePatch(site, p);
        }

        private static Vector3 FxPoint(Kind kind)
        {
            switch (kind)
            {
                case Kind.Pipe: return new Vector3(0f, -0.03f, 0.13f);
                case Kind.Box: return new Vector3(0f, 0.02f, 0.1f);
                default: return new Vector3(0f, -0.04f, 0.1f);
            }
        }

        // ---------------- 매 프레임: 스파크 ----------------

        private void Update()
        {
            float now = Time.unscaledTime;
            foreach (var site in _sites.Values)
            {
                foreach (var p in site.Points)
                {
                    if (p.Fixed || p.Prop == null)
                        continue;
                    if (p.Glow != null)
                    {
                        // 잔잔한 출렁임 + 가끔 살짝 어두워짐 (꺼졌다 켜지는 큰 깜박임은 눈이 피로했음)
                        float n = Mathf.PerlinNoise(p.Seed, now * 3f);
                        float level = Mathf.PerlinNoise(p.Seed + 3f, now * 1.7f) > 0.8f ? 0.6f : 0.8f + 0.25f * n;
                        p.Glow.intensity = (p.Kind == Kind.Pipe ? 0.2f : 0.3f) * level;
                    }
                    if (p.Kind == Kind.Pipe || now < p.NextSpark)
                        continue;
                    p.NextSpark = now + UnityEngine.Random.Range(1.6f, 4f);
                    InteriorFx.Spark(p.Prop.transform, p.Prop.transform.TransformPoint(FxPoint(p.Kind)), p.Normal + Vector3.down * 0.2f, _spark, 10, 18, 1.2f);
                    if (site.Module == CurrentRoom)
                        AudioService.TryPlay(l => l.StormSpark, 0.4f);
                }
            }
        }
    }
}
