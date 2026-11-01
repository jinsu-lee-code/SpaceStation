using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 진화 궤도 미러 빛기둥 (사용자 2026-10-27 "중앙탑 빛줄기가 그냥 선처럼 보임 → 광원이 비추는 듯한 진짜 빛줄기"):
    /// 해바라기 거울마다 집광 수신기까지 카메라를 향한 넓은 빛띠(LineRenderer + SpaceStation/LightShaft — 가운데만 밝고 가장자리로
    /// 부드럽게 사라짐 · 길이로 빛 알갱이가 흐름)를 그린다. 거울 쪽이 넓고 옅고, 수신기로 갈수록 모여 밝아진다.
    /// 끝점은 StationArtBuilder가 모델 빛줄기 메시에서 찾아 넣음 (모델 기준). 비활성이면 서서히 옅어짐, 파손이면 깜빡임.
    /// </summary>
    public sealed class LightBeamFx : MonoBehaviour
    {
        [SerializeField] private Material _material;
        [Tooltip("거울 쪽 끝 (모델 기준)")]
        [SerializeField] private Vector3[] _from;
        [Tooltip("수신기 쪽 끝 (모델 기준)")]
        [SerializeField] private Vector3[] _to;
        [SerializeField] private float _width = 0.075f;
        [Tooltip("수신기 쪽 폭 비율 (빛이 모임)")]
        [SerializeField] private float _focus = 0.4f;
        [SerializeField] private Color _mirrorColor = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] private Color _receiverColor = new Color(1f, 1f, 1f, 0.95f);

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
            var width = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, _focus));
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("LightShaft" + i);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = 2;
                lr.SetPosition(0, _from[i]);
                lr.SetPosition(1, _to[i]);
                lr.widthMultiplier = _width;
                lr.widthCurve = width;
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
