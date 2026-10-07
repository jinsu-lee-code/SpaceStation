using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-11 주민 인물 하나 (정지 자세 프리팹, 메뉴 SpaceStation/Interior/Build Resident Figures가 만듦).
    /// 걷지 않는다: 숨쉬기(몸이 살짝 부풂) + 머리를 천천히 두리번 + 가까이 온 플레이어를 바라봄.
    /// 색(작업복 · 띠 · 피부 · 머리)은 재질 이름으로 칸을 찾아 MaterialPropertyBlock으로 칠한다.
    /// </summary>
    public sealed class ResidentFigure : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Renderer _body;
        [SerializeField] private Renderer _head;
        [SerializeField] private Transform _headPivot;

        private MaterialPropertyBlock _block;
        private Transform _watcher;
        private float _seed;
        private float _lookRange;
        private float _yaw;
        private float _pitch;
        private Vector3 _bodyScale;

        /// <summary>이름표에 쓰는 글 (이름 · 특성 · 지금 하는 일).</summary>
        public string Label { get; private set; }
        /// <summary>이름표를 띄울 곳 (머리 위).</summary>
        public Vector3 TagPoint => (_headPivot != null ? _headPivot.position : transform.position + Vector3.up * 1.5f) + Vector3.up * 0.42f;

        public void Configure(string label, Color suit, Color accent, Color skin, Color hair, float seed, Transform watcher, float lookRange)
        {
            Label = label;
            _seed = seed;
            _watcher = watcher;
            _lookRange = lookRange;
            _block ??= new MaterialPropertyBlock();
            Paint(_body, suit, accent, skin, hair);
            Paint(_head, suit, accent, skin, hair);
            if (_body != null)
                _bodyScale = _body.transform.localScale;
        }

        /// <summary>프리팹 빌더용.</summary>
        public void EditorSet(Renderer body, Renderer head, Transform headPivot)
        {
            _body = body;
            _head = head;
            _headPivot = headPivot;
        }

        private void Paint(Renderer r, Color suit, Color accent, Color skin, Color hair)
        {
            if (r == null)
                return;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null)
                    continue;
                string n = mats[i].name;
                Color? c = n.Contains("ResidentSuit") ? suit : n.Contains("ResidentAccent") ? accent
                    : n.Contains("ResidentSkin") ? skin : n.Contains("ResidentHair") ? hair : (Color?)null;
                if (c == null)
                    continue;
                _block.Clear();
                _block.SetColor(BaseColorId, c.Value);
                r.SetPropertyBlock(_block, i);
            }
        }

        private void Update()
        {
            float t = Time.unscaledTime + _seed;
            // 숨쉬기: 약 4초 주기로 몸이 0.8% 부풂 (위아래 위주)
            if (_body != null)
            {
                float b = Mathf.Sin(t * 1.6f) * 0.008f;
                _body.transform.localScale = new Vector3(_bodyScale.x * (1f + b * 0.5f), _bodyScale.y * (1f + b), _bodyScale.z * (1f + b * 0.7f));
            }
            if (_headPivot == null)
                return;
            // 머리: 평소엔 천천히 두리번, 플레이어가 가까우면 그쪽을 봄 (좌우 60° 안)
            float target = Mathf.Sin(t * 0.23f) * 18f + Mathf.Sin(t * 0.61f) * 6f;
            float pitch = Mathf.Sin(t * 0.17f) * 4f;
            if (_watcher != null)
            {
                var to = _watcher.position - _headPivot.position;
                if (to.sqrMagnitude < _lookRange * _lookRange)
                {
                    var local = transform.InverseTransformDirection(to);
                    float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                    if (Mathf.Abs(yaw) < 100f)
                    {
                        target = Mathf.Clamp(yaw, -60f, 60f);
                        pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -20f, 20f);
                    }
                }
            }
            _yaw = Mathf.MoveTowardsAngle(_yaw, target, 90f * Time.unscaledDeltaTime);
            _pitch = Mathf.MoveTowards(_pitch, pitch, 40f * Time.unscaledDeltaTime);
            _headPivot.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
    }
}
