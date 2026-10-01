using System.Collections;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 5-7 운석 연출. 시뮬레이션이 판정한 운석 한 발의 경로(MeteorFlight)를 그대로 보여준다 (판정은 바꾸지 않음).
    /// - 명중: 우주에서 꼬리를 끌며 날아와 모듈 표면에 충돌 → 섬광 + 파편, 이때 모듈 파손 표시 적용
    /// - 포탑 격추: 비행 중 가까운 포탑이 머리를 돌려 레이저 → 공중 폭발
    /// - 실드 빗겨냄: 실드 범위 구면에 닿는 순간 파문 + 실드 코어 번쩍임 → 튕겨서 다른 모듈로(명중/격추) 또는 우주로
    /// 시간은 게임 시간(Time.deltaTime)을 따른다. 카메라 흔들림 없음.
    /// </summary>
    public sealed class MeteorFx : MonoBehaviour
    {
        [SerializeField] private StationController _station;

        [Header("Materials")]
        [SerializeField] private Material _rockMaterial;
        [SerializeField] private Material _glowMaterial;
        [SerializeField] private Material _trailMaterial;
        [SerializeField] private Material _flashMaterial;
        [SerializeField] private Material _laserMaterial;
        [SerializeField] private Material _rippleMaterial;
        [SerializeField] private Material _sparkMaterial;

        [Header("Timing")]
        [SerializeField] private float _flightTime = 1.6f;
        [SerializeField] private float _startDistance = 22f;
        [Tooltip("여러 발이 동시에 오면 이 간격으로 나눠 도착")]
        [SerializeField] private float _stagger = 0.22f;
        [SerializeField] private float _ricochetTime = 0.7f;
        [Tooltip("포탑이 쏘는 시점 (비행 진행률)")]
        [SerializeField, Range(0.3f, 0.95f)] private float _interceptAt = 0.85f;

        [Header("Look")]
        [SerializeField] private float _meteorSize = 0.22f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private readonly List<Mesh> _rocks = new List<Mesh>();
        private MaterialPropertyBlock _block;
        private int _pending;

        private void Start()
        {
            if (_station == null)
                _station = GetComponent<StationController>();
            _block = new MaterialPropertyBlock();
            for (int i = 0; i < 6; i++)
                _rocks.Add(RockMesh.Create(1000 + i));
            _station.DeferMeteorVisuals = true;
            _station.Simulation.MeteorResolved += HandleMeteor;
        }

        private void OnDestroy()
        {
            if (_station != null && _station.Simulation != null)
            {
                _station.Simulation.MeteorResolved -= HandleMeteor;
                _station.DeferMeteorVisuals = false;
            }
        }

        /// <summary>판정 없이 연출만 재생 (확인·디버그용). 피해 표시는 건드리지 않는다.</summary>
        public void Preview(MeteorFlight flight)
        {
            StartCoroutine(Play(flight, 0f));
        }

        private void HandleMeteor(MeteorFlight flight)
        {
            float delay = _pending * _stagger;
            _pending++;
            if (flight.Hit != null)
            {
                float legs = _flightTime + (flight.Deflected ? _ricochetTime : 0f);
                _station.HoldDamageVisual(flight.Hit, delay + legs + 0.5f);
            }
            StartCoroutine(Play(flight, delay));
        }

        private void LateUpdate() => _pending = 0; // 같은 프레임에 온 운석끼리만 간격을 둔다

        // ---------------- 경로 ----------------

        private IEnumerator Play(MeteorFlight flight, float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            if (flight.Target == null)
                yield break;

            Vector3 target = ModuleCenter(flight.Target);
            Vector3 dir = RandomIncoming(flight.Target.GetHashCode());
            Vector3 start = target + dir * _startDistance;
            var meteor = SpawnMeteor(start);

            if (flight.Deflected)
            {
                // 실드 구면에 닿는 지점까지
                Vector3 center = ModuleCenter(flight.DeflectedBy);
                float radius = flight.DeflectedBy.Data != null ? flight.DeflectedBy.Data.ShieldRadius + 0.6f : 2.5f;
                Vector3 contact = SphereEntry(start, target, center, radius, out Vector3 normal);
                yield return Fly(meteor, start, contact, _flightTime * Vector3.Distance(start, contact) / Vector3.Distance(start, target), null);
                Ripple(center, radius, contact);
                if (_station.TryGetView(flight.DeflectedBy, out var shieldView))
                    shieldView.PlayImpulse();

                if (flight.RicochetTarget != null)
                {
                    Vector3 next = ModuleCenter(flight.RicochetTarget);
                    var turret = flight.InterceptedAt == flight.RicochetTarget ? FindTurret(flight.RicochetTarget) : null;
                    if (turret != null)
                    {
                        yield return Fly(meteor, contact, next, _ricochetTime, (_, p) => p >= _interceptAt);
                        yield return Intercept(meteor, turret);
                        yield break;
                    }
                    Vector3 surface = Surface(next, (next - contact).normalized);
                    yield return Fly(meteor, contact, surface, _ricochetTime, null);
                    Impact(meteor, surface, flight.Hit);
                }
                else
                {
                    // 반사되어 우주로
                    Vector3 incoming = (contact - start).normalized;
                    Vector3 away = Vector3.Reflect(incoming, normal);
                    yield return Fly(meteor, contact, contact + away * _startDistance, _flightTime, null, fade: true);
                    Destroy(meteor.Root);
                }
                yield break;
            }

            if (flight.Intercepted)
            {
                var turret = FindTurret(flight.InterceptedAt);
                yield return Fly(meteor, start, target, _flightTime, (_, p) => p >= _interceptAt);
                if (turret != null)
                    yield return Intercept(meteor, turret);
                else
                {
                    Explode(meteor.Root.transform.position, 0.6f);
                    Destroy(meteor.Root);
                }
                yield break;
            }

            Vector3 hitPoint = Surface(target, -dir);
            yield return Fly(meteor, start, hitPoint, _flightTime, null);
            Impact(meteor, hitPoint, flight.Hit);
        }

        private sealed class Meteor
        {
            public GameObject Root;
            public Transform Rock;
            public Vector3 Spin;
        }

        private Meteor SpawnMeteor(Vector3 position)
        {
            var m = new Meteor { Root = new GameObject("Meteor") };
            m.Root.transform.SetParent(transform, false);
            m.Root.transform.position = position;
            var rock = Part(m.Root.transform, _rocks[Random.Range(0, _rocks.Count)], _rockMaterial, Vector3.one * _meteorSize);
            m.Rock = rock.transform;
            Part(m.Root.transform, _rocks[0], _glowMaterial, Vector3.one * _meteorSize * 1.5f);
            var trail = m.Root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = _trailMaterial;
            trail.time = 0.45f;
            trail.widthMultiplier = _meteorSize * 0.9f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = g;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            m.Spin = Random.insideUnitSphere * 360f;
            return m;
        }

        /// <summary>from → to 이동. stopWhen(m, 진행률)이 참이면 그 자리에서 멈춘다.</summary>
        private IEnumerator Fly(Meteor m, Vector3 from, Vector3 to, float duration, System.Func<Meteor, float, bool> stopWhen, bool fade = false)
        {
            float t = 0f;
            duration = Mathf.Max(0.05f, duration);
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / duration);
                m.Root.transform.position = Vector3.Lerp(from, to, t);
                m.Rock.Rotate(m.Spin * Time.deltaTime, Space.Self);
                if (fade)
                    m.Root.transform.localScale = Vector3.one * (1f - t);
                if (stopWhen != null && stopWhen(m, t))
                    yield break;
                yield return null;
            }
        }

        private IEnumerator Intercept(Meteor m, ModuleInstance turret)
        {
            Vector3 hitAt = m.Root.transform.position;
            Vector3 muzzle = ModuleCenter(turret) + Vector3.up * 0.3f;
            if (_station.TryGetView(turret, out var view))
            {
                var head = FindChild(view.transform, "Head");
                if (head != null)
                {
                    Vector3 flat = Vector3.ProjectOnPlane(hitAt - head.position, Vector3.up);
                    if (flat.sqrMagnitude > 1e-4f)
                        head.rotation = Quaternion.LookRotation(flat, Vector3.up);
                    muzzle = head.position + head.forward * 0.4f;
                }
            }
            var laser = new GameObject("Laser").AddComponent<LineRenderer>();
            laser.transform.SetParent(transform, false);
            laser.sharedMaterial = _laserMaterial;
            laser.positionCount = 2;
            laser.SetPosition(0, muzzle);
            laser.SetPosition(1, hitAt);
            laser.widthMultiplier = 0.05f;
            laser.shadowCastingMode = ShadowCastingMode.Off;
            Explode(hitAt, 0.6f);
            Destroy(m.Root);
            float t = 0f;
            while (t < 0.18f)
            {
                t += Time.deltaTime;
                laser.widthMultiplier = 0.05f * (1f - t / 0.18f);
                yield return null;
            }
            Destroy(laser.gameObject);
        }

        private void Impact(Meteor m, Vector3 point, ModuleInstance hit)
        {
            Destroy(m.Root);
            Explode(point, 1f);
            if (hit != null)
                _station.ReleaseDamageVisual(hit);
        }

        // ---------------- 효과 ----------------

        private void Explode(Vector3 position, float scale)
        {
            StartCoroutine(Flash(position, scale));
            Sparks(position, scale);
        }

        private IEnumerator Flash(Vector3 position, float scale)
        {
            var flash = Part(transform, SphereMesh(), _flashMaterial, Vector3.zero);
            flash.transform.position = position;
            var light = flash.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.2f);
            light.range = 2.5f * scale;
            float t = 0f;
            const float duration = 0.4f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = t / duration;
                flash.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, 1.0f, k) * scale;
                light.intensity = (1f - k) * 6f * scale;
                _block.SetColor(BaseColorId, new Color(2.5f, 1.2f, 0.4f, 1f) * (1f - k));
                flash.SetPropertyBlock(_block);
                yield return null;
            }
            Destroy(flash.gameObject);
        }

        private void Sparks(Vector3 position, float scale)
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f * scale, 3.5f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.35f), new Color(0.6f, 0.55f, 0.5f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(18 * scale), (short)(28 * scale)) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _sparkMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
        }

        private void Ripple(Vector3 center, float radius, Vector3 contact)
        {
            StartCoroutine(RippleRoutine(center, radius));
            Sparks(contact, 0.5f);
        }

        private IEnumerator RippleRoutine(Vector3 center, float radius)
        {
            var go = new GameObject("ShieldRipple");
            go.transform.SetParent(transform, false);
            go.transform.position = center;
            go.AddComponent<MeshFilter>().sharedMesh = SphereMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _rippleMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            float t = 0f;
            const float duration = 0.6f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = t / duration;
                go.transform.localScale = Vector3.one * radius * 2f * Mathf.Lerp(0.96f, 1.04f, k);
                _block.Clear();
                _block.SetColor(BaseColorId, new Color(1f, 1f, 1f, 1f - k));
                r.SetPropertyBlock(_block);
                yield return null;
            }
            Destroy(go);
        }

        private static Mesh _sphere;
        private static Mesh SphereMesh()
        {
            if (_sphere == null)
                _sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            return _sphere;
        }

        private MeshRenderer Part(Transform parent, Mesh mesh, Material material, Vector3 scale)
        {
            var go = new GameObject(mesh != null ? mesh.name : "Part");
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        // ---------------- 위치 계산 ----------------

        private static Vector3 ModuleCenter(ModuleInstance module)
        {
            Vector3 sum = Vector3.zero;
            foreach (var c in module.Cells)
                sum += GridConfig.CellToWorld(c);
            return sum / Mathf.Max(1, module.Cells.Count);
        }

        /// <summary>모듈 중심에서 dir 쪽 표면 근처 (칸 크기 기준 대략).</summary>
        private static Vector3 Surface(Vector3 center, Vector3 towardSource) => center + towardSource.normalized * 0.35f;

        /// <summary>위쪽 반구에서 들어오는 방향 (모듈마다 다르게, 매번 약간 랜덤).</summary>
        private static Vector3 RandomIncoming(int salt)
        {
            Vector3 d = Random.onUnitSphere;
            d.y = Mathf.Abs(d.y) * 0.6f + 0.15f;
            return d.normalized;
        }

        private static Vector3 SphereEntry(Vector3 from, Vector3 to, Vector3 center, float radius, out Vector3 normal)
        {
            Vector3 d = (to - from).normalized;
            Vector3 m = from - center;
            float b = Vector3.Dot(m, d);
            float c = Vector3.Dot(m, m) - radius * radius;
            float disc = b * b - c;
            Vector3 point = to - d * 0.6f;
            if (disc >= 0f)
            {
                float t = -b - Mathf.Sqrt(disc);
                if (t > 0f && t < Vector3.Distance(from, to))
                    point = from + d * t;
            }
            normal = (point - center).normalized;
            return point;
        }

        private ModuleInstance FindTurret(ModuleInstance near)
        {
            if (near == null)
                return null;
            ModuleInstance best = null;
            int bestDist = int.MaxValue;
            foreach (var m in _station.Grid.Modules)
            {
                if (m.Data == null || !m.Data.IsTurret || !_station.Connectivity.IsActive(m))
                    continue;
                int d = Chebyshev(m, near);
                if (d <= m.Data.TurretRadius && d < bestDist)
                {
                    best = m;
                    bestDist = d;
                }
            }
            return best;
        }

        private static int Chebyshev(ModuleInstance a, ModuleInstance b)
        {
            int best = int.MaxValue;
            foreach (var ca in a.Cells)
            {
                foreach (var cb in b.Cells)
                {
                    var d = ca - cb;
                    best = Mathf.Min(best, Mathf.Max(Mathf.Abs(d.x), Mathf.Max(Mathf.Abs(d.y), Mathf.Abs(d.z))));
                }
            }
            return best;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }
    }
}
