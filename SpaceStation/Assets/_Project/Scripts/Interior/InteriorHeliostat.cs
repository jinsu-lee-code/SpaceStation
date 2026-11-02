using SpaceStation.Building;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 궤도 미러 내부 거울 해바라기 (사용자 2026-11-02 시안 1 "이대로 옮겨줘" — 머리는 실제 해를 따라 돎, 태양광 어레이 "실제 방향에 맞춰줘"와 같음):
    /// 헬리오스탯 — 머리(<see cref="_heads"/>)마다 거울 면이 실제 태양(Directional Light) 방향과 수신기 방향의 가운데를 보게 머리 가운데를 축으로 돌리고, 빛기둥 시작 면도 같이 옮긴다.
    /// 머리 메시는 쉬는 자세 = 해가 바로 위 (om_builder, 외형과 같음). 템플릿(= 모듈) 회전을 빼고 계산. 처음엔 바로 맞추고, 해 방향이 바뀌면 일정한 속도로 따라 돈다.
    /// </summary>
    public sealed class InteriorHeliostat : MonoBehaviour
    {
        private const float MinSunHeight = 0.2f;    // 해가 갑판 아래로 내려가도 거울이 아래를 보지 않게

        [SerializeField] private Transform[] _heads;
        [Tooltip("머리 가운데 (이 오브젝트 로컬)")]
        [SerializeField] private Vector3[] _centers;
        [SerializeField] private Vector3 _receiver;
        [Tooltip("머리 가운데 → 거울 면 (거울 방향으로, 빛기둥이 시작하는 곳)")]
        [SerializeField] private float _mirrorLift = 0.032f;
        [SerializeField] private LightBeamFx _beams;
        [Tooltip("해 방향이 바뀔 때 따라 도는 속도 (도/초)")]
        [SerializeField] private float _degreesPerSecond = 15f;

        private static Light _sun;
        private Vector3[] _rest;
        private Vector3[] _normal;
        private bool _aimed;

        public void Configure(Transform[] heads, Vector3[] centers, Vector3 receiver, float mirrorLift, LightBeamFx beams)
        {
            _heads = heads;
            _centers = centers;
            _receiver = receiver;
            _mirrorLift = mirrorLift;
            _beams = beams;
        }

        private void Awake()
        {
            _rest = new Vector3[_heads.Length];
            _normal = new Vector3[_heads.Length];
            for (int i = 0; i < _heads.Length; i++)
                _rest[i] = _normal[i] = ((_receiver - _centers[i]).normalized + Vector3.up).normalized;
        }

        private void LateUpdate()
        {
            var sun = FindSun();
            if (sun == null)
                return;
            Vector3 toSun = transform.InverseTransformDirection(-sun.transform.forward);
            toSun.y = Mathf.Max(toSun.y, MinSunHeight);
            toSun.Normalize();
            float step = _degreesPerSecond * Mathf.Deg2Rad * Time.unscaledDeltaTime;
            for (int i = 0; i < _heads.Length; i++)
            {
                Vector3 target = ((_receiver - _centers[i]).normalized + toSun).normalized;
                if (_aimed && Vector3.Angle(_normal[i], target) < 0.05f)
                    continue;
                _normal[i] = _aimed ? Vector3.RotateTowards(_normal[i], target, step, 0f) : target;
                var turn = Quaternion.FromToRotation(_rest[i], _normal[i]);
                _heads[i].localPosition = _centers[i] - turn * _centers[i];
                _heads[i].localRotation = turn;
                if (_beams != null)
                    _beams.SetMirror(i, _centers[i] + _normal[i] * _mirrorLift, _normal[i]);
            }
            // 빛기둥은 첫 Update에서 만들어지므로 그 뒤에 맞춤 (Start 순서와 무관)
            if (_beams == null || _beams.BeamCount > 0)
                _aimed = true;
        }

        private static Light FindSun()
        {
            if (_sun != null)
                return _sun;
            _sun = RenderSettings.sun;
            if (_sun == null)
            {
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l.type == LightType.Directional)
                    {
                        _sun = l;
                        break;
                    }
                }
            }
            return _sun;
        }
    }
}
