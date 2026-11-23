using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 식량 공장 가공 라인: 벨트마다 물건 원형(프로틴 바 · 작물 상자)을 복제해 시작 → 끝으로 움직이고 (양 끝에서 커지고 작아짐 = 기계로 들어감), 로봇 팔을 좌우로 까딱임.
    /// 모듈이 멈추면(비활성 · 전력 부족) 라인 전체가 그 자리에 멈춤 (단백질 배양실 <see cref="InteriorConveyor"/>와 같음). 템플릿 빌더가 configure에서 붙임.
    /// </summary>
    public sealed class InteriorFoodLine : InteriorRoomDisplay
    {
        private const float EndFade = 0.25f;   // 양 끝에서 커지고 작아지는 길이 (m)

        [SerializeField] private Transform[] _prototypes;
        [Tooltip("벨트마다: 시작 · 끝 (템플릿 기준, 벨트 윗면)")]
        [SerializeField] private Vector3[] _from;
        [SerializeField] private Vector3[] _to;
        [Tooltip("벨트마다 물건 원형 번호")]
        [SerializeField] private int[] _kind;
        [SerializeField] private float _speed = 0.45f;
        [SerializeField] private float _spacing = 1.05f;
        [Tooltip("로봇 팔 (어깨 피벗)")]
        [SerializeField] private Transform[] _arms;
        [SerializeField] private float _armDegrees = 20f;
        [SerializeField] private float _armSeconds = 3.2f;

        private Transform[][] _items;
        private float[] _length;
        private Quaternion[] _armRest;
        private float _time;

        public void Configure(Transform[] prototypes, Vector3[] from, Vector3[] to, int[] kind, Transform[] arms)
        {
            _prototypes = prototypes;
            _from = from;
            _to = to;
            _kind = kind;
            _arms = arms;
        }

        private void Awake()
        {
            if (_prototypes == null)
                return;
            _items = new Transform[_from.Length][];
            _length = new float[_from.Length];
            for (int b = 0; b < _from.Length; b++)
            {
                _length[b] = Vector3.Distance(_from[b], _to[b]);
                int n = Mathf.Max(1, Mathf.RoundToInt(_length[b] / _spacing));
                _items[b] = new Transform[n];
                for (int i = 0; i < n; i++)
                {
                    var go = Instantiate(_prototypes[_kind[b]].gameObject, transform);
                    go.name = "BeltItem";
                    go.SetActive(true);
                    _items[b][i] = go.transform;
                }
            }
            foreach (var p in _prototypes)
                p.gameObject.SetActive(false);
            _armRest = new Quaternion[_arms.Length];
            for (int i = 0; i < _arms.Length; i++)
                _armRest[i] = _arms[i].localRotation;
            Place();
        }

        protected override void Refresh()
        {
        }

        private void Update()
        {
            if (_items == null || (Polled && !Active))
                return;
            _time += Time.deltaTime;
            Place();
        }

        private void Place()
        {
            for (int b = 0; b < _items.Length; b++)
            {
                var row = _items[b];
                float loop = _length[b] / row.Length;
                for (int i = 0; i < row.Length; i++)
                {
                    float d = Mathf.Repeat(_time * _speed + i * loop, _length[b]);
                    row[i].localPosition = Vector3.Lerp(_from[b], _to[b], d / _length[b]);
                    row[i].localScale = Vector3.one * Mathf.Clamp01(Mathf.Min(d, _length[b] - d) / EndFade);
                }
            }
            for (int i = 0; i < _arms.Length; i++)
            {
                float a = Mathf.Sin(_time * 2f * Mathf.PI / _armSeconds + i * 1.7f) * _armDegrees;
                _arms[i].localRotation = _armRest[i] * Quaternion.Euler(0f, a, 0f);
            }
        }
    }
}
