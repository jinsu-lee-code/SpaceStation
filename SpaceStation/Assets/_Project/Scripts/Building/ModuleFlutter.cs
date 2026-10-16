using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 12-5 행정 관제소 깃발 펄럭임 (모델의 "Flutter_{이름}" 조각에 붙임, StationArtBuilder가 자동 배선).
    /// 사슬 = "Flutter_{이름}"(피벗 = 깃대 축) → 같은 부모 아래 "Flutter_{이름}_1", "_2" … (피벗 = 앞 조각과 이어진 세로 이음선).
    /// 조각은 계층으로 겹쳐 두지 않는다: Blender FBX 내보내기(bake_space_transform)가 자식의 자식 축을 한 번 더 돌려 놓기 때문.
    /// 그래서 부모 공간에서 사슬을 직접 계산한다 — 이음선마다 모듈 위쪽 축으로 사인 꺾임을 주고,
    /// 바깥 조각일수록 늦게 따라와 물결이 깃대에서 끝으로 흐른다. ModuleView 상태: 비활성 = 서서히 멈춤. 배치 고스트에서는 그냥 펄럭인다.
    /// </summary>
    public sealed class ModuleFlutter : MonoBehaviour
    {
        public const string Prefix = "Flutter";
        private static readonly Regex FollowerSuffix = new Regex(@"_\d+$");

        [Tooltip("이음선마다 최대 꺾임 (도). 깃대 쪽 이음선은 절반")]
        [SerializeField] private float _amplitude = 16f;
        [Tooltip("흔들림 빠르기 (라디안/초)")]
        [SerializeField] private float _speed = 5f;
        [Tooltip("조각 하나 건널 때마다 늦어지는 위상 (라디안)")]
        [SerializeField] private float _lag = 1.1f;
        [Tooltip("멈추거나 다시 펄럭일 때 변화 (초당 비율)")]
        [SerializeField] private float _acceleration = 0.5f;

        private Transform[] _segments;
        private Vector3[] _basePos;
        private Quaternion[] _baseRot;
        private Vector3 _axis;
        private ModuleView _view;
        private bool _live;
        private float _flutter = 1f;
        private float _seed;

        public int SegmentCount => _segments?.Length ?? 0;

        /// <summary>사슬 맨 앞 조각 이름인가 ("Flutter_…"이면서 뒤가 "_숫자"가 아님).</summary>
        public static bool IsChainRoot(string name) => name.StartsWith(Prefix) && !FollowerSuffix.IsMatch(name);

        private void Start() => Init();

        private void Init()
        {
            var list = new List<Transform> { transform };
            var parent = transform.parent;
            for (int i = 1; parent != null; i++)
            {
                var next = parent.Find(name + "_" + i);
                if (next == null)
                    break;
                list.Add(next);
            }
            _segments = list.ToArray();
            _basePos = new Vector3[_segments.Length];
            _baseRot = new Quaternion[_segments.Length];
            for (int i = 0; i < _segments.Length; i++)
            {
                _basePos[i] = _segments[i].localPosition;
                _baseRot[i] = _segments[i].localRotation;
            }
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            // FBX 부품의 로컬 축에는 임포트 축 보정이 섞여 있으므로 모듈 위쪽을 부모 공간으로 옮겨 둔다
            var up = _view != null ? _view.transform.up : Vector3.up;
            _axis = parent != null ? parent.InverseTransformDirection(up) : up;
            _seed = Random.value * Mathf.PI * 2f;
        }

        private void Update() => Step(Time.deltaTime, Time.time);

        /// <summary>한 프레임 진행 (테스트 · 에디터 미리보기에서 직접 호출).</summary>
        public void Step(float dt, float time)
        {
            if (_segments == null)
                Init();
            bool operational = !_live || _view.Operational;
            _flutter = Mathf.MoveTowards(_flutter, operational ? 1f : 0f, dt * _acceleration);
            var bend = Quaternion.identity;
            var pos = _basePos[0];
            for (int i = 0; i < _segments.Length; i++)
            {
                if (i > 0)
                    pos += bend * (_basePos[i] - _basePos[i - 1]); // 앞 조각까지 꺾인 만큼 이음선 위치도 따라 돈다
                float amp = _amplitude * _flutter * (i == 0 ? 0.5f : 1f);
                bend *= Quaternion.AngleAxis(amp * Mathf.Sin(time * _speed - i * _lag + _seed), _axis);
                _segments[i].localPosition = pos;
                _segments[i].localRotation = bend * _baseRot[i];
            }
        }
    }
}
