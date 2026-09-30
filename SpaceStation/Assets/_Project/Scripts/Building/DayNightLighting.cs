using SpaceStation.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 낮/밤 주기를 조명에 반영 (5-1). 태양광 발전 배율(0~1, 전환 구간 포함)에 맞춰
    /// 태양(Directional Light)의 밝기·색·기울기, 환경광, 스카이박스 노출을 보간한다.
    /// 밤에도 정거장이 보이도록 최소 밝기를 유지한다.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class DayNightLighting : MonoBehaviour
    {
        [SerializeField] private ResourceController _resources;

        [Header("Sun (낮 → 밤)")]
        [SerializeField] private Color _dayColor = new Color(1f, 0.86f, 0.66f);   // 따뜻한 항성광
        [SerializeField] private Color _nightColor = new Color(0.62f, 0.55f, 0.85f); // 행성 그림자 속 반사광
        [SerializeField, Min(0f)] private float _dayIntensity = 1.35f;
        [SerializeField, Min(0f)] private float _nightIntensity = 0.3f;
        [Tooltip("태양 기울기 (낮, 밤). 밤엔 낮게 깔려 그림자가 길어짐")]
        [SerializeField] private Vector2 _pitch = new Vector2(48f, 12f);
        [SerializeField] private float _yaw = 330f;

        [Header("Ambient")]
        [SerializeField] private Color _dayAmbient = new Color(0.34f, 0.27f, 0.24f);
        [SerializeField] private Color _nightAmbient = new Color(0.11f, 0.09f, 0.16f);

        [Header("Skybox (_Exposure가 있는 재질만)")]
        [SerializeField] private Vector2 _skyExposure = new Vector2(1f, 0.45f);

        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        private Light _light;
        private float _applied = -1f;

        private void Awake()
        {
            _light = GetComponent<Light>();
            RenderSettings.ambientMode = AmbientMode.Flat;
        }

        private void LateUpdate()
        {
            var cycle = _resources.DayNight;
            float t = cycle.Enabled ? Mathf.Clamp01(cycle.SolarMultiplier(_resources.ElapsedSeconds)) : 1f; // 1 = 낮
            if (Mathf.Abs(t - _applied) < 0.002f)
                return;
            _applied = t;
            Apply(t);
        }

        /// <summary>1 = 한낮, 0 = 한밤.</summary>
        public void Apply(float day)
        {
            if (_light == null)
                _light = GetComponent<Light>();
            _light.color = Color.Lerp(_nightColor, _dayColor, day);
            _light.intensity = Mathf.Lerp(_nightIntensity, _dayIntensity, day);
            transform.rotation = Quaternion.Euler(Mathf.Lerp(_pitch.y, _pitch.x, day), _yaw, 0f);
            RenderSettings.ambientLight = Color.Lerp(_nightAmbient, _dayAmbient, day);
            var sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty(ExposureId))
                sky.SetFloat(ExposureId, Mathf.Lerp(_skyExposure.y, _skyExposure.x, day));
        }

        private void OnDisable()
        {
            // 플레이 종료 후 공유 스카이박스 재질이 밤 노출로 남지 않도록
            var sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty(ExposureId))
                sky.SetFloat(ExposureId, _skyExposure.x);
        }
    }
}
