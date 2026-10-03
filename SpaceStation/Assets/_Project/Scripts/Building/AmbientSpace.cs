using System.Collections;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 5-7 평상시 배경.
    /// - 우주 먼지: 카메라 주변에 미세한 입자가 천천히 떠다녀 카메라를 돌릴 때 깊이감
    /// - 소행성: 먼 거리의 저폴리 바위들이 천천히 자전·공전
    /// - 우주선: 가끔 먼 배경을 가로질러 감 / 보급선 이벤트 때는 정거장 옆으로 날아와 잠시 머물다 떠남
    /// </summary>
    public sealed class AmbientSpace : MonoBehaviour
    {
        [SerializeField] private StationController _station;

        [Header("Dust")]
        [SerializeField] private Material _dustMaterial;
        [SerializeField] private int _dustMax = 350;
        [SerializeField] private float _dustRange = 28f;

        [Header("Asteroids")]
        [SerializeField] private Material _rockMaterial;
        [SerializeField] private int _asteroidCount = 14;
        [SerializeField] private Vector2 _asteroidDistance = new Vector2(45f, 90f);
        [SerializeField] private Vector2 _asteroidScale = new Vector2(1.5f, 5.5f);
        [Tooltip("공전 속도 (도/초)")]
        [SerializeField] private float _orbitSpeed = 0.6f;

        [Header("Ships")]
        [SerializeField] private Mesh _shipMesh;
        [SerializeField] private Material _shipMaterial;
        [SerializeField] private Material _engineMaterial;
        [SerializeField] private Vector2 _shipInterval = new Vector2(40f, 90f);
        [SerializeField] private float _shipCrossSeconds = 28f;
        [SerializeField] private float _shipScale = 0.9f;
        [Tooltip("8-5 화물 터미널 화물선 길이 (격자 칸 기준, 모듈 크기에 맞춤)")]
        [SerializeField] private float _cargoShipLength = 0.7f;

        private readonly List<(Transform t, Vector3 spin)> _asteroids = new List<(Transform, Vector3)>();
        private Transform _asteroidRoot;
        private ParticleSystem _dust;
        private Camera _camera;
        private float _nextShip;

        private void Start()
        {
            if (_station == null)
                _station = FindFirstObjectByType<StationController>();
            _camera = Camera.main;
            CreateDust();
            CreateAsteroids();
            _nextShip = Time.time + Random.Range(8f, 20f);
            if (_station != null && _station.Simulation != null)
            {
                _station.Simulation.Events.EventStarted += HandleEventStarted;
                _station.Simulation.Cargo.Delivered += HandleCargoDelivered;
            }
        }

        private void OnDestroy()
        {
            if (_station != null && _station.Simulation != null)
            {
                _station.Simulation.Events.EventStarted -= HandleEventStarted;
                _station.Simulation.Cargo.Delivered -= HandleCargoDelivered;
            }
        }

        private void HandleCargoDelivered(ModuleInstance terminal, SpaceStation.Data.ResourceType type, float amount)
        {
            if (terminal?.Data != null)
                StartCoroutine(CargoShip(terminal));
        }

        /// <summary>8-5 화물선: 터미널 앞 접근로를 따라 들어와 입구 앞에 잠시 머물렀다가 같은 길로 나간다.</summary>
        private IEnumerator CargoShip(ModuleInstance terminal)
        {
            if (_shipMesh == null)
                yield break;
            var front = (Vector3)PlacementRules.DockFrontWorld(terminal.Data, terminal.Rotation);
            Vector3 center = Vector3.zero;
            foreach (var c in terminal.Cells)
                center += GridConfig.CellToWorld(c);
            center /= terminal.Cells.Count;
            // 모듈 크기에 맞춘 작은 화물선 (기존 보급선 메시를 길이 기준으로 축소), 접근로 첫 칸에 머묾
            float length = Mathf.Max(0.01f, _shipMesh.bounds.size.z);
            float scale = _cargoShipLength * GridConfig.CellSize / length;
            Vector3 hold = center + front * (GridConfig.CellSize * 1.0f) + Vector3.up * 0.05f;
            Vector3 far = hold + front * 30f + Vector3.up * 5f;
            var ship = SpawnShip(scale);
            yield return Move(ship.transform, far, hold, 4.5f, ease: true);
            ship.transform.rotation = Quaternion.LookRotation(-front); // 입구를 바라봄
            float wait = 0f;
            while (wait < 2.5f)
            {
                wait += Time.deltaTime;
                ship.transform.position = hold + Vector3.up * Mathf.Sin(wait * 2f) * 0.02f;
                yield return null;
            }
            yield return Move(ship.transform, hold, far + Vector3.up * 4f, 5f, ease: false);
            Destroy(ship);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_dust != null && _camera != null)
                _dust.transform.position = _camera.transform.position;
            if (_asteroidRoot != null)
                _asteroidRoot.Rotate(0f, _orbitSpeed * dt, 0f, Space.World);
            foreach (var (t, spin) in _asteroids)
                t.Rotate(spin * dt, Space.Self);
            if (Time.time >= _nextShip)
            {
                _nextShip = Time.time + Random.Range(_shipInterval.x, _shipInterval.y);
                StartCoroutine(PassingShip());
            }
        }

        // ---------------- 먼지 ----------------

        private void CreateDust()
        {
            var go = new GameObject("SpaceDust");
            go.transform.SetParent(transform, false);
            _dust = go.AddComponent<ParticleSystem>();
            _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _dust.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 10f;
            main.maxParticles = _dustMax;
            main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 24f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.7f, 0.75f, 0.9f, 0.18f), new Color(1f, 0.9f, 0.8f, 0.35f));
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 카메라가 움직이면 시차가 생김
            var emission = _dust.emission;
            emission.rateOverTime = _dustMax / 20f;
            var shape = _dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(_dustRange, _dustRange * 0.6f, _dustRange);
            var color = _dust.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _dustMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            _dust.Play();
        }

        // ---------------- 소행성 ----------------

        private void CreateAsteroids()
        {
            _asteroidRoot = new GameObject("Asteroids").transform;
            _asteroidRoot.SetParent(transform, false);
            var rng = new System.Random(7);
            for (int i = 0; i < _asteroidCount; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = Mathf.Lerp(_asteroidDistance.x, _asteroidDistance.y, (float)rng.NextDouble());
                float height = Mathf.Lerp(-18f, 22f, (float)rng.NextDouble());
                var go = new GameObject("Asteroid" + i);
                go.transform.SetParent(_asteroidRoot, false);
                go.transform.localPosition = new Vector3(Mathf.Cos(angle) * dist, height, Mathf.Sin(angle) * dist);
                go.transform.localRotation = Random.rotation;
                go.transform.localScale = Vector3.one * Mathf.Lerp(_asteroidScale.x, _asteroidScale.y, (float)rng.NextDouble());
                go.AddComponent<MeshFilter>().sharedMesh = RockMesh.Create(200 + i, 0.35f, i % 3 == 0 ? 2 : 1);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _rockMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                // 작은 위성 바위를 가끔 곁에
                if (i % 4 == 0)
                {
                    var moon = new GameObject("Pebble");
                    moon.transform.SetParent(go.transform, false);
                    moon.transform.localPosition = new Vector3(1.1f, 0.3f, 0.4f);
                    moon.transform.localScale = Vector3.one * 0.25f;
                    moon.AddComponent<MeshFilter>().sharedMesh = RockMesh.Create(500 + i);
                    var mr = moon.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _rockMaterial;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                }
                var spin = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 12f;
                _asteroids.Add((go.transform, spin));
            }
        }

        // ---------------- 우주선 ----------------

        private GameObject SpawnShip(float scale)
        {
            var go = new GameObject("Ship");
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = _shipMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _shipMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            // 엔진 불꽃 꼬리 (뒤 = -Z)
            for (int s = -1; s <= 1; s += 2)
            {
                var engine = new GameObject("Engine");
                engine.transform.SetParent(go.transform, false);
                engine.transform.localPosition = new Vector3(s * 0.12f, 0f, -0.62f);
                var trail = engine.AddComponent<TrailRenderer>();
                trail.sharedMaterial = _engineMaterial;
                trail.time = 0.8f;
                trail.widthMultiplier = 0.09f * scale;
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = g;
                trail.shadowCastingMode = ShadowCastingMode.Off;
            }
            return go;
        }

        /// <summary>먼 배경을 가로지르는 우주선 (정거장 중심을 지나지 않도록 옆으로 비켜서).</summary>
        private IEnumerator PassingShip()
        {
            if (_shipMesh == null)
                yield break;
            Vector3 center = StationCenter();
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 side = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 across = Vector3.Cross(Vector3.up, side);
            float offset = Random.Range(18f, 35f);
            float height = Random.Range(-4f, 10f);
            Vector3 from = center + side * offset - across * 60f + Vector3.up * height;
            Vector3 to = center + side * offset + across * 60f + Vector3.up * (height + Random.Range(-3f, 3f));
            var ship = SpawnShip(_shipScale);
            ship.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / _shipCrossSeconds;
                ship.transform.position = Vector3.Lerp(from, to, t);
                yield return null;
            }
            Destroy(ship);
        }

        private void HandleEventStarted(GameEventData data)
        {
            if (data is SupplyShipEventData)
                StartCoroutine(SupplyShip());
        }

        /// <summary>보급선: 멀리서 날아와 정거장 위 옆에 잠시 머물렀다가 떠난다.</summary>
        private IEnumerator SupplyShip()
        {
            if (_shipMesh == null)
                yield break;
            Vector3 center = StationCenter();
            float radius = StationRadius() + 2.5f;
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 hold = center + dir * radius + Vector3.up * 2.2f;
            Vector3 far = hold + (dir * 40f + Vector3.up * 8f);
            var ship = SpawnShip(_shipScale * 1.3f);
            yield return Move(ship.transform, far, hold, 5f, ease: true);
            float wait = 0f;
            while (wait < 3.5f)
            {
                wait += Time.deltaTime;
                ship.transform.position = hold + Vector3.up * Mathf.Sin(wait * 1.5f) * 0.08f;
                yield return null;
            }
            Vector3 leave = hold + Vector3.Cross(Vector3.up, dir) * 45f + Vector3.up * 6f;
            yield return Move(ship.transform, hold, leave, 6f, ease: false);
            Destroy(ship);
        }

        private static IEnumerator Move(Transform t, Vector3 from, Vector3 to, float seconds, bool ease)
        {
            t.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
            float k = 0f;
            while (k < 1f)
            {
                k += Time.deltaTime / seconds;
                float e = ease ? 1f - (1f - k) * (1f - k) : k * k; // 도착은 감속, 출발은 가속
                t.position = Vector3.Lerp(from, to, Mathf.Clamp01(e));
                yield return null;
            }
        }

        private Vector3 StationCenter()
        {
            if (_station == null || _station.Grid == null || _station.Grid.ModuleCount == 0)
                return Vector3.zero;
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var m in _station.Grid.Modules)
            {
                sum += GridConfig.CellToWorld(m.Origin);
                n++;
            }
            return sum / n;
        }

        private float StationRadius()
        {
            Vector3 c = StationCenter();
            float r = 2f;
            if (_station != null && _station.Grid != null)
                foreach (var m in _station.Grid.Modules)
                    r = Mathf.Max(r, Vector3.Distance(c, GridConfig.CellToWorld(m.Origin)) + 1f);
            return r;
        }
    }
}
