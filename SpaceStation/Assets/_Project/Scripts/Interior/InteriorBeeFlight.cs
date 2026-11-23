using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 항성 발전망 내부 꿀벌 (내부 전용 모델 Critter_Bee + 날개 Critter_BeeWings): 고리 길을 따라 나는 벌 · 제자리에서 맴도는 벌 · 꽃 위에서 쉬는 벌.
    /// 바깥 모듈 CritterFx의 BeeLoop · BeeHover · BeeRest와 같은 느낌을 미터 크기로 — 길 = 템플릿 좌표 닫힌 길 (<see cref="StellarGrid.BeePath"/>, Catmull-Rom),
    /// 날개는 세로로 줄였다 폈다 (CritterFx.Buzz와 같은 파닥임). 연출 전용이라 실제 시간으로 움직임 (일시정지 중 방문해도).
    /// 모델 앞 = 로컬 −x (critter_builder +x → FBX), 날개는 몸의 자식 (같은 원점).
    /// 경비 꿀벌 (맴도는 벌 가운데 하나)은 미니게임 "벌 몰래 꿀 먹기" 동안 <see cref="InteriorHoneySneak"/>가 자리 · 방향을 잡고 (<see cref="GuardHeld"/>),
    /// 날갯짓만 여기서 (<see cref="GuardBuzz"/>배 빠르게 · 크게 — 돌아보기 전 경고).
    /// </summary>
    public sealed class InteriorBeeFlight : MonoBehaviour
    {
        [SerializeField] private Vector3[] _path;
        [SerializeField] private Transform[] _flyers;
        [SerializeField] private float[] _flyerPhase;
        [SerializeField] private Transform[] _hovers;
        [SerializeField] private Transform[] _resters;
        [SerializeField] private float _speed = 1.1f;
        [SerializeField] private Quaternion _modelRot = Quaternion.identity;   // 모델 자체 회전 (FBX 자식 회전)
        [Tooltip("미니게임 경비 꿀벌 (맴도는 벌 가운데 하나)")]
        [SerializeField] private Transform _guard;

        private static readonly Quaternion ModelFace = Quaternion.Euler(0f, 90f, 0f);   // 로컬 −x를 +z로
        private Vector3[] _hoverHome;
        private Quaternion[] _hoverRot;
        private Vector3[] _restHome;
        private Transform[] _allWings;
        private Vector3[] _wingScale;
        private float _length = 1f;

        /// <summary>미니게임 경비 꿀벌.</summary>
        public Transform Guard => _guard;
        /// <summary>미니게임이 경비 꿀벌 자리 · 방향을 잡는 중 (여기선 날갯짓만).</summary>
        public bool GuardHeld { get; set; }
        /// <summary>경비 꿀벌 날갯짓 빠르기 · 크기 배율.</summary>
        public float GuardBuzz { get; set; } = 1f;
        /// <summary>방향 dir (템플릿 기준)을 보는 꿀벌 회전 (모델 회전 포함).</summary>
        public Quaternion FacingOf(Vector3 dir) => Facing(dir) * _modelRot;

        public void Configure(Vector3[] path, Transform[] flyers, Transform[] hovers, Transform[] resters, float speed, Quaternion modelRot, Transform guard = null)
        {
            _guard = guard;
            _path = path;
            _flyers = flyers;
            _hovers = hovers;
            _resters = resters;
            _speed = speed;
            _modelRot = modelRot;
            _flyerPhase = new float[flyers.Length];
            for (int i = 0; i < flyers.Length; i++)
                _flyerPhase[i] = (i + 0.37f * Mathf.Sin(i * 2.3f)) / flyers.Length;
        }

        private void Awake()
        {
            _length = Mathf.Max(1f, StellarGrid.PathLength(_path));
            _hoverHome = new Vector3[_hovers.Length];
            _hoverRot = new Quaternion[_hovers.Length];
            for (int i = 0; i < _hovers.Length; i++)
            {
                _hoverHome[i] = _hovers[i].localPosition;
                _hoverRot[i] = _hovers[i].localRotation;
            }
            _restHome = new Vector3[_resters.Length];
            for (int i = 0; i < _resters.Length; i++)
                _restHome[i] = _resters[i].localPosition;
            int n = _flyers.Length + _hovers.Length + _resters.Length;
            _allWings = new Transform[n];
            _wingScale = new Vector3[n];
            int k = 0;
            foreach (var group in new[] { _flyers, _hovers, _resters })
            {
                foreach (var bee in group)
                {
                    _allWings[k] = bee.childCount > 0 ? bee.GetChild(0) : null;
                    _wingScale[k] = _allWings[k] != null ? _allWings[k].localScale : Vector3.one;
                    k++;
                }
            }
        }

        private static Quaternion Facing(Vector3 dir) => Quaternion.LookRotation(dir, Vector3.up) * ModelFace;

        private void Update()
        {
            float t = Time.unscaledTime;
            int k = 0;
            for (int i = 0; i < _flyers.Length; i++, k++)
            {
                float u = _flyerPhase[i] + t * _speed / _length;
                var p = StellarGrid.PathPoint(_path, u);
                var ahead = StellarGrid.PathPoint(_path, u + 0.004f);
                var dir = ahead - p;
                float seed = i * 1.7f;
                // 길 옆으로 조금씩 흔들림 (한 줄로 늘어선 기차처럼 보이지 않게) + 위아래 출렁
                var side = Vector3.Cross(Vector3.up, dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward);
                p += side * (0.22f * Mathf.Sin(0.7f * t + seed)) + Vector3.up * (0.08f * Mathf.Sin(2.6f * t + seed));
                _flyers[i].localPosition = p;
                if (dir.sqrMagnitude > 1e-8f)
                {
                    var flat = new Vector3(dir.x, Mathf.Clamp(dir.y, -0.3f * dir.magnitude, 0.3f * dir.magnitude), dir.z);
                    _flyers[i].localRotation = Quaternion.Slerp(_flyers[i].localRotation, Facing(flat) * _modelRot, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
                }
                Wing(k, t, seed, 1f);
            }
            for (int i = 0; i < _hovers.Length; i++, k++)
            {
                float seed = 11f + i * 2.1f;
                if (GuardHeld && _hovers[i] == _guard)
                {
                    Wing(k, t, seed, 1f, GuardBuzz);
                    continue;
                }
                _hovers[i].localPosition = _hoverHome[i] + new Vector3(0.16f * Mathf.Sin(1.8f * t + seed), 0.06f * Mathf.Sin(2.9f * t + 2f * seed), 0.11f * Mathf.Sin(3.6f * t + seed));
                _hovers[i].localRotation = Quaternion.Euler(0f, 14f * Mathf.Sin(0.9f * t + seed), 0f) * _hoverRot[i];
                Wing(k, t, seed, 1f);
            }
            for (int i = 0; i < _resters.Length; i++, k++)
            {
                float seed = 23f + i * 1.3f;
                float period = 7f + 4f * Mathf.Repeat(seed * 0.618f, 1f);
                float u = Mathf.Repeat((t + seed) / period, 1f);
                _resters[i].localPosition = _restHome[i] + Vector3.up * (0.004f * Mathf.Sin(1.5f * t + seed));
                Wing(k, t, seed, u > 0.1f && u < 0.18f ? 1f : 0f);   // 가끔 날개를 털어 봄
            }
        }

        private void Wing(int k, float t, float seed, float amount, float buzz = 1f)
        {
            var w = _allWings[k];
            if (w == null)
                return;
            // 날개 뿌리 (WingPivot)에서: 펼침 (폭 그대로) ↔ 위로 접음 (폭 줄고 높이 늘어남) — 얇은 날개라 세로만 줄이면 안 보였음
            float f = amount * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 11f * buzz * t + seed));
            float big = Mathf.Max(1f, 0.6f + 0.4f * buzz);   // 경고 = 더 크게 파닥임
            w.localScale = new Vector3(_wingScale[k].x, _wingScale[k].y * (1f + 2.2f * big * f), _wingScale[k].z * (1f - Mathf.Min(0.8f, 0.5f * big) * f));
        }
    }
}
