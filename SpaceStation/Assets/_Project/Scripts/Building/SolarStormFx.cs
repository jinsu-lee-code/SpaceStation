using SpaceStation.Data;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpaceStation.Building
{
    /// <summary>
    /// 5-7 태양 폭풍 연출 (폭풍 이벤트가 진행 중인 동안).
    /// - 화면 톤: 전용 전역 Volume(주황 색 필터 + 노출 일렁임)의 가중치를 서서히 올리고 내림
    /// - 패널 스파크: 활성 태양광 모듈의 패널 위에 불규칙하게 전기 불꽃
    /// </summary>
    public sealed class SolarStormFx : MonoBehaviour
    {
        [SerializeField] private StationController _station;
        [SerializeField] private Material _sparkMaterial;

        [Header("Screen")]
        [SerializeField] private Color _tint = new Color(1f, 0.88f, 0.72f);
        [Tooltip("폭풍 화면 톤 최대 세기 (Volume 가중치)")]
        [SerializeField, Range(0f, 1f)] private float _strength = 0.8f;
        [SerializeField] private float _fadeSeconds = 1.5f;
        [Tooltip("노출 일렁임 폭 (EV)")]
        [SerializeField] private float _flicker = 0.35f;
        [Tooltip("기본 Volume(PP_Main)의 노출과 같게 — 이 Volume이 덮어써도 평균 밝기는 유지")]
        [SerializeField] private float _baseExposure = 0.35f;

        [Header("Sparks")]
        [Tooltip("태양광 모듈 하나당 초당 스파크 횟수")]
        [SerializeField] private float _sparksPerSecond = 1.2f;

        private Volume _volume;
        private ColorAdjustments _color;
        private float _weight;
        private float _seed;

        public bool StormActive { get; private set; }
        /// <summary>패널 스파크 (월드 위치, 5-8 소리용).</summary>
        public event System.Action<Vector3> Sparked;

        private void Start()
        {
            if (_station == null)
                _station = GetComponent<StationController>();
            _seed = Random.value * 100f;
            var go = new GameObject("SolarStormVolume");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10f;
            _volume.weight = 0f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _color = profile.Add<ColorAdjustments>(true);
            _color.colorFilter.Override(_tint);
            _color.postExposure.Override(0f);
            _color.saturation.Override(8f);
            _volume.sharedProfile = profile;
        }

        private void Update()
        {
            StormActive = IsStormActive();
            float dt = Time.deltaTime;
            _weight = Mathf.MoveTowards(_weight, StormActive ? _strength : 0f, dt / Mathf.Max(0.05f, _fadeSeconds));
            _volume.weight = _weight;
            if (_weight > 0f)
            {
                // 느린 일렁임 + 가끔 짧게 번쩍
                float n = Mathf.PerlinNoise(Time.time * 1.7f, _seed) - 0.5f;
                float spike = Mathf.PerlinNoise(Time.time * 9f, _seed + 3f) > 0.78f ? 0.5f : 0f;
                _color.postExposure.Override(_baseExposure + n * 2f * _flicker + spike * _flicker);
            }
            if (StormActive && dt > 0f)
                SpawnSparks(dt);
        }

        private bool IsStormActive()
        {
            var sim = _station != null ? _station.Simulation : null;
            if (sim == null)
                return false;
            foreach (var a in sim.Events.ActiveEvents)
            {
                if (a.Data is SolarStormEventData)
                    return true;
            }
            return false;
        }

        private void SpawnSparks(float dt)
        {
            foreach (var module in _station.Grid.Modules)
            {
                if (module.Data == null || !module.Data.SolarPowered || !_station.Connectivity.IsActive(module))
                    continue;
                if (Random.value > _sparksPerSecond * dt)
                    continue;
                if (!_station.TryGetView(module, out var view))
                    continue;
                var panel = view.GetComponentInChildren<SunFacingPanel>();
                Transform t = panel != null ? panel.transform : view.transform;
                Vector3 local = new Vector3(Random.Range(-0.3f, 0.3f), 0.03f, Random.Range(-0.3f, 0.3f));
                Spark(t.TransformPoint(local), t.up);
            }
        }

        private void Spark(Vector3 position, Vector3 normal)
        {
            Sparked?.Invoke(position);
            var go = new GameObject("StormSpark");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(normal));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.9f, 1f), new Color(1f, 0.85f, 0.4f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 16) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 60f;
            shape.radius = 0.01f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _sparkMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.08f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
        }
    }
}
