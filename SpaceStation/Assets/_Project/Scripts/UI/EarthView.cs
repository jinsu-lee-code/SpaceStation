using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.UI
{
    /// <summary>
    /// 13-7a 꺼져 가는 지구 (REDESIGN T8 · M1, 사용자 결정 2026-10-19): 정거장 바깥 멀리 떠 있는 둥근 지구 (BlenderWork/Earth → SM_Earth · 셰이더 SpaceStation/Earth).
    /// - 기본 시점 너머 아래, 해 반대쪽에 두어 밤쪽 도시 불빛이 보임 · 23.4° 기울어 자전(게임 시간, 멈추면 같이 멈춤) · 구름은 조금 더 빨리 흐름
    /// - 불빛 = 시각적 시계: <see cref="EarthLights.Value"/>(장 안에서 서서히 + 다음 장 시작 때 크게)를 화면에서는 천천히 따라가 꺼짐
    /// - 닿을 수 없는 하늘의 천체 (사용자 질문 2026-10-19: 정거장이 커져 지구까지 이어지면?): 카메라마다 그리기 직전 카메라에서 같은 방향 · 거리에 옮김 →
    ///   카메라가 어디로 가도 크기 · 방향이 그대로(먼 행성처럼)라 다가갈 수 없고, 셰이더가 깊이를 먼 평면에 붙여 정거장 모듈은 언제나 지구 앞에 그려짐
    /// 화면 연출만 — 시뮬레이션은 바꾸지 않는다. 씬에는 모델 · 재질만 연결된 빈 오브젝트 (메뉴 SpaceStation/Chapters/Setup Greybox).
    /// </summary>
    public sealed class EarthView : MonoBehaviour
    {
        private static readonly int LightsId = Shader.PropertyToID("_Lights");
        private static readonly int CloudShiftId = Shader.PropertyToID("_CloudShift");

        [SerializeField] private StationController _station;
        [SerializeField] private SimulationClock _clock;
        [Tooltip("지구 모델 (반지름 1 구, UV = 경위도)")]
        [SerializeField] private GameObject _model;
        [SerializeField] private Material _material;
        [Tooltip("정거장에서 지구 쪽 방향 — 해(앞 위) 반대쪽 아래라 밤쪽이 더 많이 보임")]
        [SerializeField] private Vector3 _direction = new Vector3(0.5f, -0.45f, 0.75f);
        [SerializeField, Min(10f)] private float _distance = 700f;
        [SerializeField, Min(1f)] private float _radius = 140f;
        [Tooltip("자전축 기울기 (도)")]
        [SerializeField] private float _tilt = 23.4f;
        [Tooltip("자전 한 바퀴 (게임 초)")]
        [SerializeField, Min(10f)] private float _dayLength = 600f;
        [Tooltip("시작 때 정면(정거장 쪽)에 오는 경도 — 불빛이 많은 곳부터 보이게")]
        [SerializeField] private float _startLongitude = 100f;
        [Tooltip("구름 흐름: 땅보다 이만큼 더 빨리 (한 바퀴 게임 초)")]
        [SerializeField, Min(10f)] private float _cloudLap = 3000f;
        [Tooltip("화면 불빛이 목표를 따라가는 빠르기 (초당 비율 — 장이 바뀌어 0.12 떨어지면 약 3초에 걸쳐 꺼짐)")]
        [SerializeField, Min(0.001f)] private float _fadeRate = 0.04f;
        [Tooltip("13-7d 메인 메뉴 배경: 켜면 방향 대신 카메라 화면의 한 자리(뷰포트 0~1)에 고정 — 메뉴 카메라가 정거장을 돌아도 같은 곳")]
        [SerializeField] private bool _screenAnchored;
        [SerializeField] private Vector2 _screenPoint = new Vector2(0.86f, 0.78f);

        private StationSimulation _sim;
        private Transform _earth;
        private Renderer _renderer;
        private MaterialPropertyBlock _block;
        private float _spin, _cloud;
        private float _shown = -1f;
        private float _held = -1f; // 13-7b 장 전환 연출이 불빛을 직접 움직이는 동안 (음수 = 시계를 따라감)
        private Vector3 _toward; // 지금 그리는 쪽 (화면 고정이면 카메라마다 바뀜)

        /// <summary>지금 화면에 켜진 불빛 비율 (13-7b 장 전환 연출이 읽음).</summary>
        public float ShownLights => _shown;
        /// <summary>어느 카메라에서든 지구가 보이는 방향 (13-7b 장 전환 연출이 이쪽으로 카메라를 돌림).</summary>
        public Vector3 Direction => _direction.normalized;
        /// <summary>지구가 화면에서 차지하는 반각 (도).</summary>
        public float AngularRadius => Mathf.Asin(Mathf.Clamp01(_radius / _distance)) * Mathf.Rad2Deg;
        /// <summary>시계가 가리키는 불빛 비율 (화면은 이 값을 천천히 따라감).</summary>
        public float TargetLights => EarthLights.Value(_sim?.Chapters);

        /// <summary>13-7b 장 전환 연출: 화면 불빛을 이 값으로 붙잡음 (꺼지는 순간을 연출이 고름).</summary>
        public void HoldLights(float value) => _held = Mathf.Clamp01(value);

        /// <summary>붙잡기를 풀고 다시 시계를 따라감.</summary>
        public void ReleaseLights() => _held = -1f;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            if (_model == null || _material == null)
            {
                enabled = false;
                return;
            }
            _block = new MaterialPropertyBlock();
            var go = Instantiate(_model, transform);
            go.name = "Earth";
            _earth = go.transform;
            _earth.position = _direction.normalized * _distance;
            _earth.localScale = Vector3.one * _radius;
            RenderPipelineManager.beginCameraRendering += FollowCamera;
            foreach (var c in go.GetComponentsInChildren<Collider>())
                Destroy(c); // 배경 — 클릭 · 배치 광선에 걸리지 않게
            _renderer = go.GetComponentInChildren<Renderer>();
            if (_renderer != null)
            {
                _renderer.sharedMaterial = _material;
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
            }
            Apply(0f);
        }

        private void OnDestroy() => RenderPipelineManager.beginCameraRendering -= FollowCamera;

        /// <summary>
        /// 그리기 직전 그 카메라에서 같은 방향 · 거리로 (정거장이 아무리 커져도 다가갈 수 없음).
        /// 보는 거리가 짧은 카메라(내부 바깥 창 = 300)는 그 안으로 당기고 같은 비율로 줄여 겉보기 크기 그대로 — 깊이는 셰이더가 먼 평면에 붙임 (13-7a 남은 것, 2026-10-20).
        /// </summary>
        private void FollowCamera(ScriptableRenderContext context, Camera cam)
        {
            if (_earth == null || cam == null)
                return;
            float d = Mathf.Min(_distance, cam.farClipPlane * 0.8f);
            _toward = _screenAnchored ? cam.ViewportPointToRay(new Vector3(_screenPoint.x, _screenPoint.y, 0f)).direction : _direction.normalized;
            _earth.position = cam.transform.position + _toward * d;
            _earth.localScale = Vector3.one * (_radius * d / _distance);
        }

        // 13-7d 메인 메뉴 배경(시계 없음)은 실제 시간으로 천천히 돎
        private float SimDelta => _clock == null ? Time.deltaTime : _clock.Clock.IsPaused ? 0f : Time.deltaTime * _clock.Clock.Speed;

        private void LateUpdate() => Apply(SimDelta);

        private void Apply(float dt)
        {
            if (_earth == null)
                return;
            _spin += dt * 360f / _dayLength;
            _cloud += dt / _cloudLap;
            if (_cloud > 1f)
                _cloud -= 1f;
            // 모델: 북극 = +Y, 경도 0 = −X, 동경 90 = −Z (거울상 아님, SM_Earth 가져온 뒤 확인) → Y축 a도 돌리면 경도 a − 90이 +Z(정거장 쪽)에 옴.
            // 땅은 동쪽으로 돌아 정면 경도가 줄어듦 = 음의 회전 (+Y 회전은 서쪽으로 감)
            var toStation = -(_screenAnchored && _toward != Vector3.zero ? _toward : _direction.normalized);
            var flat = new Vector3(toStation.x, 0f, toStation.z);
            var face = Quaternion.LookRotation(flat.sqrMagnitude > 1e-6f ? flat : Vector3.forward, Vector3.up);
            _earth.rotation = face * Quaternion.AngleAxis(_tilt, Vector3.right) * Quaternion.AngleAxis(_startLongitude + 90f - _spin, Vector3.up);

            float target = TargetLights;
            if (_held >= 0f)
                _shown = _held;
            else
                _shown = _shown < 0f ? target : Mathf.MoveTowards(_shown, target, Time.unscaledDeltaTime * _fadeRate);
            if (_renderer == null)
                return;
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(LightsId, _shown);
            _block.SetFloat(CloudShiftId, -_cloud); // 구름이 동쪽으로 흐름 (u = 경도)
            _renderer.SetPropertyBlock(_block);
        }
    }
}
