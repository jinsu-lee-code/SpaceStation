using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 12-5 행정 관제소 깃발 펄럭임 (모델의 "Flutter*" 사슬 맨 앞 조각에 붙임, StationArtBuilder가 자동 배선).
    /// 사슬 = 깃대 쪽 조각 → 자식 "Flutter*" 조각 → … (피벗 = 앞 조각과 이어진 세로 이음선, 맨 앞은 깃대 축).
    /// 이음선마다 모듈 위쪽 축으로 사인 흔들림을 주고, 바깥 조각일수록 늦게 따라와 물결이 깃대에서 끝으로 흐른다.
    /// ModuleView 상태: 비활성 = 서서히 멈춤. 배치 고스트에서는 그냥 펄럭인다.
    /// </summary>
    public sealed class ModuleFlutter : MonoBehaviour
    {
        public const string Prefix = "Flutter";

        [Tooltip("이음선마다 최대 꺾임 (도). 깃대 쪽 이음선은 절반")]
        [SerializeField] private float _amplitude = 16f;
        [Tooltip("흔들림 빠르기 (라디안/초)")]
        [SerializeField] private float _speed = 5f;
        [Tooltip("조각 하나 건널 때마다 늦어지는 위상 (라디안)")]
        [SerializeField] private float _lag = 1.1f;
        [Tooltip("멈추거나 다시 펄럭일 때 변화 (초당 비율)")]
        [SerializeField] private float _acceleration = 0.5f;

        private Transform[] _segments;
        private Quaternion[] _base;
        private Vector3[] _axis;
        private ModuleView _view;
        private bool _live;
        private float _flutter = 1f;
        private float _seed;

        public int SegmentCount => _segments?.Length ?? 0;

        private void Start() => Init();

        private void Init()
        {
            var list = new List<Transform>();
            for (var t = transform; t != null; t = NextSegment(t))
                list.Add(t);
            _segments = list.ToArray();
            _base = new Quaternion[_segments.Length];
            _axis = new Vector3[_segments.Length];
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            // FBX 부품의 로컬 축에는 임포트 축 보정이 섞여 있으므로 모듈 위쪽을 각 조각의 부모 공간으로 옮겨 둔다
            // (같은 축으로만 돌리므로 부모가 흔들려도 이 축은 그대로)
            var up = _view != null ? _view.transform.up : Vector3.up;
            for (int i = 0; i < _segments.Length; i++)
            {
                _base[i] = _segments[i].localRotation;
                var parent = _segments[i].parent;
                _axis[i] = parent != null ? parent.InverseTransformDirection(up) : up;
            }
            _seed = Random.value * Mathf.PI * 2f;
        }

        private static Transform NextSegment(Transform t)
        {
            foreach (Transform c in t)
                if (c.name.StartsWith(Prefix))
                    return c;
            return null;
        }

        private void Update() => Step(Time.deltaTime, Time.time);

        /// <summary>한 프레임 진행 (테스트 · 에디터 미리보기에서 직접 호출).</summary>
        public void Step(float dt, float time)
        {
            if (_segments == null)
                Init();
            bool operational = !_live || _view.Operational;
            _flutter = Mathf.MoveTowards(_flutter, operational ? 1f : 0f, dt * _acceleration);
            for (int i = 0; i < _segments.Length; i++)
            {
                float amp = _amplitude * _flutter * (i == 0 ? 0.5f : 1f);
                float angle = amp * Mathf.Sin(time * _speed - i * _lag + _seed);
                _segments[i].localRotation = Quaternion.AngleAxis(angle, _axis[i]) * _base[i];
            }
        }
    }
}
