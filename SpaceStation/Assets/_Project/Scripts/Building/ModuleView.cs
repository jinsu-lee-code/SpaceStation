using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Building
{
    public enum ModuleDamageVisual
    {
        None,
        Damaged,
        Repairing,
    }

    /// <summary>
    /// 모듈 오브젝트 하나의 표시 상태(활성/비활성, 파손/수리, 선택 강조)를 합쳐 MaterialPropertyBlock으로 적용한다.
    /// 우선순위: 선택 강조 &gt; 파손/수리 틴트 &gt; 기본색, 비활성이면 마지막에 어둡게.
    /// 기본 상태(활성 + 정상 + 비선택)에서는 블록을 비워 SRP Batcher 호환을 유지한다.
    /// </summary>
    public sealed class ModuleView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField, Range(0f, 1f)] private float _inactiveBrightness = 0.25f;
        [SerializeField] private Color _highlightColor = new Color(1f, 0.9f, 0.3f, 1f);
        [Tooltip("파손(경고 톤) 틴트 색과 섞는 비율")]
        [SerializeField] private Color _damagedTint = new Color(1f, 0.3f, 0.15f, 1f);
        [SerializeField, Range(0f, 1f)] private float _damagedTintAmount = 0.65f;
        [SerializeField] private Color _repairingTint = new Color(0.3f, 0.8f, 1f, 1f);
        [SerializeField, Range(0f, 1f)] private float _repairingTintAmount = 0.5f;

        private Renderer[] _renderers;
        private Color[] _baseColors;
        private MaterialPropertyBlock _propertyBlock;
        private bool _operational = true;
        private bool _highlighted;
        private ModuleDamageVisual _damage;

        public ModuleInstance Module { get; private set; }

        public void Initialize(ModuleInstance module)
        {
            Module = module;
            _propertyBlock = new MaterialPropertyBlock();
            _renderers = GetComponentsInChildren<Renderer>();
            _baseColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                var mat = _renderers[i].sharedMaterial;
                _baseColors[i] = mat != null && mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId) : Color.white;
            }
            Apply();
        }

        /// <summary>코어와 연결되어 동작 중인지. false면 어둡게 표시.</summary>
        public void SetOperational(bool operational)
        {
            if (_operational == operational)
                return;
            _operational = operational;
            Apply();
        }

        public void SetHighlighted(bool highlighted)
        {
            if (_highlighted == highlighted)
                return;
            _highlighted = highlighted;
            Apply();
        }

        public void SetDamageVisual(ModuleDamageVisual damage)
        {
            if (_damage == damage)
                return;
            _damage = damage;
            Apply();
        }

        private void Apply()
        {
            if (_renderers == null)
                return;

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_operational && !_highlighted && _damage == ModuleDamageVisual.None)
                {
                    _renderers[i].SetPropertyBlock(null);
                    continue;
                }

                Color color;
                if (_highlighted)
                    color = _highlightColor;
                else if (_damage == ModuleDamageVisual.Damaged)
                    color = Color.Lerp(_baseColors[i], _damagedTint, _damagedTintAmount);
                else if (_damage == ModuleDamageVisual.Repairing)
                    color = Color.Lerp(_baseColors[i], _repairingTint, _repairingTintAmount);
                else
                    color = _baseColors[i];

                if (!_operational)
                    color = new Color(color.r * _inactiveBrightness, color.g * _inactiveBrightness, color.b * _inactiveBrightness, color.a);

                _propertyBlock.SetColor(BaseColorId, color);
                _renderers[i].SetPropertyBlock(_propertyBlock);
            }
        }
    }
}
