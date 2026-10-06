using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-10 방 안 세로 칸 게이지 (배터리 정비 통로의 저장량 게이지).
    /// 칸 렌더러는 아래 → 위 순서. 켜진 칸은 재질 그대로(화면 발광), 꺼진 칸은 블록으로 어둡게, 저장량이 낮으면 켜진 칸이 붉어진다.
    /// 값은 <see cref="InteriorAtmosphere"/>가 정해 주고, 템플릿 빌더가 FBX 조각(INT_*_Gauge_NN)으로 칸을 채운다.
    /// </summary>
    public sealed class InteriorGauge : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Tooltip("칸 렌더러 (아래 → 위)")]
        [SerializeField] private Renderer[] _segments;
        [SerializeField] private Color _offBase = new Color(0.02f, 0.05f, 0.06f);
        [Tooltip("이 저장량 비율 아래면 켜진 칸이 경고색")]
        [SerializeField] private float _lowFill = 0.2f;
        [SerializeField] private Color _lowBase = new Color(0.25f, 0.06f, 0.04f);
        [SerializeField] private Color _lowEmission = new Color(1.6f, 0.3f, 0.15f);

        private MaterialPropertyBlock _off;
        private MaterialPropertyBlock _low;
        private MaterialPropertyBlock _empty;
        private int _lit = -1;
        private bool _isLow;

        public int SegmentCount => _segments != null ? _segments.Length : 0;

        public void Configure(Renderer[] segments)
        {
            _segments = segments;
        }

        public bool Owns(Renderer r)
        {
            if (_segments == null)
                return false;
            foreach (var s in _segments)
            {
                if (s == r)
                    return true;
            }
            return false;
        }

        /// <summary>저장량 비율(0~1) → 켤 칸 수. 칸 안에 저장량이 조금이라도 걸치면 켬 (조금이라도 있으면 최소 1칸).</summary>
        public static int LitCount(float fill, int count)
        {
            if (count <= 0 || fill <= 0f)
                return 0;
            return Mathf.Clamp(Mathf.CeilToInt(fill * count - 0.001f), 1, count);
        }

        /// <summary>저장량 비율을 보여 줌. powered = false면 (비활성 방) 전부 꺼짐.</summary>
        public void SetFill(float fill, bool powered)
        {
            int lit = powered ? LitCount(fill, SegmentCount) : 0;
            bool low = lit > 0 && fill < _lowFill;
            if (lit == _lit && low == _isLow)
                return;
            _lit = lit;
            _isLow = low;
            EnsureBlocks();
            for (int i = 0; i < SegmentCount; i++)
            {
                var r = _segments[i];
                if (r != null)
                    r.SetPropertyBlock(i >= lit ? _off : low ? _low : _empty);
            }
        }

        private void EnsureBlocks()
        {
            if (_off != null)
                return;
            _empty = new MaterialPropertyBlock();
            _off = new MaterialPropertyBlock();
            _off.SetColor(BaseColorId, _offBase);
            _off.SetColor(EmissionColorId, Color.black);
            _low = new MaterialPropertyBlock();
            _low.SetColor(BaseColorId, _lowBase);
            _low.SetColor(EmissionColorId, _lowEmission);
        }
    }
}
