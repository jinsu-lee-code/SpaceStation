using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 궤도 미러 빛기둥 (사용자 2026-10-27 "중앙탑 빛줄기가 그냥 선처럼 보임 → 광원이 비추는 듯한 진짜 빛줄기",
    /// 2026-11-02 "그냥 선같아보여서 반사광 느낌이 안나"): 해바라기 거울마다 집광 수신기까지 카메라를 향한 넓은 빛띠(LineRenderer + SpaceStation/LightShaft)를 그린다.
    /// 빛띠 폭 = 그 거울 지름만큼 넓게 시작해 수신기 크기로 모임 (거울 지름보다 가늘면 거울에서 나온 빛으로 안 보임), 거울 쪽이 더 밝다.
    /// 끝점 · 폭은 모델 기준 — 바깥 모델은 StationArtBuilder가 모델 빛줄기 메시에서 찾아 넣고, 내부 템플릿은 InteriorTemplateBuilder가 넣는다. 비활성이면 서서히 옅어짐, 파손이면 깜빡임.
    /// </summary>
    public sealed class LightBeamFx : MonoBehaviour
    {
        private const int Segments = 8;         // 폭이 변하는 띠를 두 삼각형으로 그리면 대각선 자국이 남음
        private const float SoftEdge = 1.35f;   // 빛띠 폭 ÷ 거울 지름 (가장자리는 셰이더가 흐리게 지움)

        [SerializeField] private Material _material;
        [Tooltip("거울 쪽 끝 (모델 기준)")]
        [SerializeField] private Vector3[] _from;
        [Tooltip("수신기 쪽 끝 (모델 기준)")]
        [SerializeField] private Vector3[] _to;
        [Tooltip("거울 지름 (모델 기준, 빛줄기마다)")]
        [SerializeField] private float[] _mirrorDiameters;
        [Tooltip("수신기 쪽 지름 (빛이 모이는 곳)")]
        [SerializeField] private float _receiverDiameter = 0.08f;
        [SerializeField] private Color _mirrorColor = new Color(1f, 1f, 1f, 0.4f);
        [SerializeField] private Color _receiverColor = new Color(1f, 1f, 1f, 0.26f);

        private LineRenderer[] _lines;
        private ModuleView _view;
        private bool _live;
        private float _level = 1f;
        private float _shown = -1f;
        private float _seed;

        public int BeamCount => _lines != null ? _lines.Length : 0;

        private void Start()
        {
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            _seed = Random.value * 100f;
            if (_material == null || _from == null || _to == null)
                return;
            int n = Mathf.Min(_from.Length, _to.Length);
            _lines = new LineRenderer[n];
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("LightShaft" + i);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = Segments + 1;
                for (int k = 0; k <= Segments; k++)
                    lr.SetPosition(k, Vector3.Lerp(_from[i], _to[i], k / (float)Segments));
                float mirror = _mirrorDiameters != null && i < _mirrorDiameters.Length ? _mirrorDiameters[i] : _receiverDiameter * 2f;
                lr.widthMultiplier = 1f;
                lr.widthCurve = AnimationCurve.Linear(0f, mirror * SoftEdge, 1f, _receiverDiameter * SoftEdge);
                lr.alignment = LineAlignment.View;
                lr.textureMode = LineTextureMode.Stretch;
                lr.numCapVertices = 0;
                lr.sharedMaterial = _material;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                _lines[i] = lr;
            }
            Apply(1f);
        }

        private void Update()
        {
            if (_lines == null || !_live)
                return;
            float target = _view.Operational ? 1f : 0.1f;
            if (_view.DamageVisual == ModuleDamageVisual.Damaged)
                target *= Mathf.PerlinNoise(Time.time * 6f, _seed) > 0.5f ? 1f : 0.2f;
            _level = Mathf.MoveTowards(_level, target, Time.deltaTime * 2f);
            if (Mathf.Abs(_level - _shown) > 0.01f)
                Apply(_level);
        }

        private void Apply(float level)
        {
            _shown = level;
            var a = _mirrorColor;
            var b = _receiverColor;
            a.a *= level;
            b.a *= level;
            foreach (var lr in _lines)
            {
                lr.startColor = a;
                lr.endColor = b;
            }
        }
    }
}
