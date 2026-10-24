using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 8-4 회전 링 연출 (모델의 "Spin" 부품에 붙임, StationArtBuilder가 자동 배선). 모듈 로컬 Y축으로 천천히 회전 (`_axis`가 있으면 그 축).
    /// ModuleView 상태: 비활성 = 서서히 멈춤 / 파손 = 걸렸다 풀렸다 하며 느려짐. 배치 고스트에서는 그냥 돈다.
    /// </summary>
    public sealed class ModuleSpinner : MonoBehaviour
    {
        [Tooltip("정상 회전 속도 (도/초)")]
        [SerializeField] private float _speed = 12f;
        [Tooltip("멈추거나 다시 돌 때 속도 변화 (정상 속도 대비 초당 비율)")]
        [SerializeField] private float _acceleration = 0.25f;
        [Tooltip("회전축 (모듈 루트 기준). 0이면 위쪽 — 진화 연구 단지 궤도 고리처럼 기울어진 축은 StationArtBuilder가 메시에서 계산해 넣음")]
        [SerializeField] private Vector3 _axis;

        private ModuleView _view;
        private bool _live;
        private float _spin = 1f;
        private float _seed;

        public float Spin => _spin;

        private void Start()
        {
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            _seed = Random.value * 100f;
        }

        private void Update() => Step(Time.deltaTime, Time.time);

        /// <summary>한 프레임 진행 (테스트에서 직접 호출).</summary>
        public void Step(float dt, float time)
        {
            bool operational = !_live || _view.Operational;
            float target = operational ? 1f : 0f;
            float rate = _acceleration;
            if (_live && _view.DamageVisual == ModuleDamageVisual.Damaged)
            {
                target *= Mathf.PerlinNoise(time * 2f, _seed) > 0.55f ? 0.05f : 0.4f;
                rate = 1.5f;
            }
            _spin = Mathf.MoveTowards(_spin, target, dt * rate);
            // 모듈은 Y축으로만 회전하고, FBX 부품의 로컬 축은 임포트 축 보정이 섞여 있으므로 월드 위쪽 기준 (피벗 = 링 중심).
            // 기울어진 축은 모듈 루트 기준으로 저장 → 모듈 회전(90° 단위)을 따라 월드 축으로
            var axis = Vector3.up;
            if (_axis != Vector3.zero)
                axis = (_view != null ? _view.transform : transform.root).TransformDirection(_axis);
            transform.Rotate(axis, _speed * _spin * dt, Space.World);
        }
    }
}
