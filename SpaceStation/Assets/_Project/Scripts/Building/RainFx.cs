using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 바이오스피어 도는 구름 고리의 비 (사용자 2026-10-29 "비가 너무 대강 — 진짜 구름에서 비가 내리는 느낌", StationArtBuilder가 자동 배선):
    /// 모델에 구운 빗줄 막대(구름마다 9가닥, 공중에서 끝남)는 숨기고, 구름마다 아래 넓은 면에서 가는 빗방울 입자가 끊임없이 생겨
    /// 바닥(물 높이)까지 곧게 떨어진다 (월드 공간 — 구름이 돌아도 비는 수직, 지형에 닿는 곳부터는 지형에 가려 안 보임) + 구름 아래 옅은 비 안개 기둥.
    /// 진주 아치 쪽으로 떨어지는 입자는 셰이더(SpaceStation/RainParticle _Shelter)가 감춰 아치를 뚫지 않음 — 모듈 좌표를 재질에 넘김.
    /// 비활성이면 비가 그침. 고스트(배치 미리보기)에서는 만들지 않음.
    /// </summary>
    public sealed class RainFx : MonoBehaviour
    {
        private static readonly int WorldToModuleId = Shader.PropertyToID("_WorldToModule");

        [Tooltip("도는 구름 고리 부품 (비 입자 묶음이 이 아래에서 함께 돎)")]
        [SerializeField] private Transform _clouds;
        [Tooltip("모델 좌표의 기준 (모델 \"Model\")")]
        [SerializeField] private Transform _module;
        [Tooltip("구름 자리 (구름 고리 부품 기준, 구름 밑면 가운데)")]
        [SerializeField] private Vector3[] _cloudSpots = new Vector3[0];
        [Tooltip("비가 생기는 면: 구름을 따라 긴 쪽 · 바깥쪽 너비")]
        [SerializeField] private Vector2 _area = new Vector2(0.22f, 0.1f);
        [SerializeField] private Material _dropMaterial;
        [SerializeField] private Material _mistMaterial;
        [Tooltip("빗방울 떨어지는 빠르기 (모델 단위/초)")]
        [SerializeField] private float _fallSpeed = 1.6f;
        [Tooltip("비가 끝나는 높이 (모델 기준 — 물 높이, 그 위 지형에서는 지형에 가려짐)")]
        [SerializeField] private float _groundY = -0.26f;
        [Tooltip("구름 하나당 초당 빗방울")]
        [SerializeField] private float _rate = 80f;
        [Header("아치 가림 (모델 기준)")]
        [SerializeField] private Vector2 _shelterCenter = new Vector2(1f, 1f);
        [SerializeField] private Vector3 _shelterAnglesDeg = new Vector3(30f, 150f, 270f);
        [SerializeField] private float _shelterHalfWidth = 0.06f;

        private Material _drop;
        private Material _mist;
        private ParticleSystem[] _systems;
        private ModuleView _view;
        private bool _live;
        private bool _raining = true;

        private void Start()
        {
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            if (_live)
                Play();
        }

        /// <summary>구름마다 빗방울 · 비 안개 입자를 만들고 시작 (Start가 부름 — 확인용으로 미리보기에서 직접 불러도 됨).</summary>
        public void Play()
        {
            if (_systems != null || _clouds == null || _module == null || _dropMaterial == null || _mistMaterial == null)
                return;
            _drop = Shelter(_dropMaterial);
            _mist = Shelter(_mistMaterial);
            _systems = new ParticleSystem[_cloudSpots.Length * 2];
            for (int i = 0; i < _cloudSpots.Length; i++)
            {
                var spot = _cloudSpots[i];
                var tangent = new Vector3(-spot.z, 0f, spot.x);
                var holder = new GameObject("Rain_" + i).transform;
                holder.SetParent(_clouds, false);
                holder.localPosition = spot;
                holder.localRotation = tangent.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(tangent.normalized, Vector3.up) : Quaternion.identity;
                // 떨어지는 거리 = 구름 밑면 높이 − 물 높이 (모델 기준) → 끝까지 떨어지는 수명
                float drop = Mathf.Max(0.2f, _module.InverseTransformPoint(holder.position).y - _groundY);
                _systems[i * 2] = Drops(holder, drop / _fallSpeed);
                _systems[i * 2 + 1] = Mist(holder);
            }
            LateUpdate();
        }

        private Material Shelter(Material source)
        {
            var m = new Material(source) { name = source.name + " (Module)" };
            m.SetFloat("_Shelter", 1f);
            m.SetVector("_ShelterCenter", new Vector4(_shelterCenter.x, _shelterCenter.y, _shelterHalfWidth, 0f));
            m.SetVector("_ShelterAngles", new Vector4(_shelterAnglesDeg.x * Mathf.Deg2Rad, _shelterAnglesDeg.y * Mathf.Deg2Rad, _shelterAnglesDeg.z * Mathf.Deg2Rad, 0f));
            return m;
        }

        private ParticleSystem Drops(Transform parent, float life)
        {
            var ps = NewSystem(parent, "RainDrops", out var main, out var renderer);
            main.duration = 4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(0.0035f, 0.0055f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.86f, 1f, 0.45f), new Color(0.92f, 0.96f, 1f, 0.7f));
            main.maxParticles = Mathf.CeilToInt(_rate * life * 1.3f);
            var emission = ps.emission;
            emission.rateOverTime = _rate;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);   // 살짝 흔들리며
            vel.y = new ParticleSystem.MinMaxCurve(-_fallSpeed * 1.12f, -_fallSpeed * 0.9f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
            Fade(ps, 0.06f, 0.85f);
            renderer.renderMode = ParticleSystemRenderMode.Stretch; // 속도 방향으로 늘인 가는 빗줄
            renderer.velocityScale = 0.035f;
            renderer.lengthScale = 1f;
            renderer.cameraVelocityScale = 0f;
            renderer.sharedMaterial = _drop;
            ps.Play();
            return ps;
        }

        private ParticleSystem Mist(Transform parent)
        {
            var ps = NewSystem(parent, "RainMist", out var main, out var renderer);
            main.duration = 4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.84f, 0.92f, 0.03f), new Color(0.88f, 0.92f, 0.97f, 0.055f)); // 진하면 연기 덩이처럼 보임
            main.maxParticles = 12;
            var emission = ps.emission;
            emission.rateOverTime = 1.6f;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.26f, -0.16f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.4f));
            Fade(ps, 0.25f, 0.6f);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = _mist;
            ps.Play();
            return ps;
        }

        private ParticleSystem NewSystem(Transform parent, string name, out ParticleSystem.MainModule main, out ParticleSystemRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            main = ps.main;
            main.loop = true;
            main.prewarm = true; // 모듈이 나타날 때 이미 비가 내리는 중
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 구름이 돌아도 비는 곧게
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(_area.y, 0.01f, _area.x);
            shape.position = Vector3.down * 0.02f;
            renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        /// <summary>나타날 때 · 사라질 때 옅어짐 (inEnd · outStart = 수명 비율).</summary>
        private static void Fade(ParticleSystem ps, float inEnd, float outStart)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inEnd), new GradientAlphaKey(1f, outStart), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        private void LateUpdate()
        {
            if (_systems == null)
                return;
            var toModule = _module.worldToLocalMatrix;
            _drop.SetMatrix(WorldToModuleId, toModule);
            _mist.SetMatrix(WorldToModuleId, toModule);
            bool raining = !_live || _view.Operational;
            if (raining == _raining)
                return;
            _raining = raining;
            foreach (var ps in _systems)
            {
                var emission = ps.emission;
                emission.enabled = raining;
            }
        }

        private void OnDestroy()
        {
            foreach (var m in new[] { _drop, _mist })
            {
                if (m == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(m);
                else
                    DestroyImmediate(m);
            }
        }
    }
}
