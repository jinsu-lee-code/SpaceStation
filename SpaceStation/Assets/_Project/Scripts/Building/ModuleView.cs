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
    /// 모듈 오브젝트 하나의 표시 상태를 합쳐 MaterialPropertyBlock으로 적용한다 (5-4 "불빛으로 표현").
    /// - 비활성(코어와 분리): 어둡게 + 창문·띠 발광 꺼짐
    /// - 파손: 붉은 틴트 + 발광이 불규칙하게 깜빡임
    /// - 수리 중: 하늘색 틴트가 천천히 맥동
    /// - 노후: 갈색 틴트 + 발광 약하게
    /// - 선택: 약한 노란 틴트 + 가장자리 빛(SelectionRim 복제 렌더러)
    /// 기본 상태(활성 + 정상 + 비선택)에서는 블록을 비워 SRP Batcher 호환을 유지한다.
    /// 깜빡임·맥동이 필요한 상태에서만 매 프레임 갱신한다.
    /// </summary>
    public sealed class ModuleView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField, Range(0f, 1f)] private float _inactiveBrightness = 0.25f;
        [SerializeField] private Color _highlightColor = new Color(1f, 0.9f, 0.3f, 1f);
        [Tooltip("선택 시 틴트 비율 (가장자리 빛과 함께 쓰므로 약하게)")]
        [SerializeField, Range(0f, 1f)] private float _highlightTintAmount = 0.18f;
        [Tooltip("파손(경고 톤) 틴트 색과 섞는 비율")]
        [SerializeField] private Color _damagedTint = new Color(1f, 0.3f, 0.15f, 1f);
        [SerializeField, Range(0f, 1f)] private float _damagedTintAmount = 0.5f;
        [SerializeField] private Color _repairingTint = new Color(0.3f, 0.8f, 1f, 1f);
        [SerializeField, Range(0f, 1f)] private float _repairingTintAmount = 0.45f;
        [Tooltip("노후(효율 저하) 틴트")]
        [SerializeField] private Color _wornTint = new Color(0.45f, 0.35f, 0.25f, 1f);
        [SerializeField, Range(0f, 1f)] private float _wornTintAmount = 0.5f;
        [SerializeField, Range(0f, 1f)] private float _wornEmission = 0.35f;

        private Renderer[] _renderers;
        private Renderer[] _slotRenderers; // 재질 슬롯마다 한 칸 (렌더러, 재질 인덱스)
        private int[] _slotIndices;
        private Color[] _baseColors;
        private Color[] _baseEmission;
        private MaterialPropertyBlock _propertyBlock;
        private bool _operational = true;
        private bool _highlighted;
        private ModuleDamageVisual _damage;
        private bool _worn;
        private Material _rimMaterial;
        private System.Collections.Generic.List<GameObject> _rimCopies;
        private float _seed;

        public ModuleInstance Module { get; private set; }
        /// <summary>코어와 연결되어 동작 중인지 (모듈 전용 연출이 읽음).</summary>
        public bool Operational => _operational;
        public ModuleDamageVisual DamageVisual => _damage;
        /// <summary>모듈 전용 순간 연출 (예: 실드가 운석을 빗겨냄).</summary>
        public event System.Action Impulse;

        public void PlayImpulse() => Impulse?.Invoke();

        private bool Animating => _damage != ModuleDamageVisual.None;

        public void Initialize(ModuleInstance module, Material rimMaterial = null)
        {
            Module = module;
            _rimMaterial = rimMaterial;
            _seed = Random.value * 100f;
            _propertyBlock = new MaterialPropertyBlock();
            _renderers = GetComponentsInChildren<Renderer>();
            // 재질 슬롯 단위로 색을 기억한다 (모델 한 메시에 Hull/Accent 등 여러 재질이 있을 때 슬롯마다 다른 색 유지)
            var slotRenderers = new System.Collections.Generic.List<Renderer>();
            var slotIndices = new System.Collections.Generic.List<int>();
            var baseColors = new System.Collections.Generic.List<Color>();
            var baseEmission = new System.Collections.Generic.List<Color>();
            foreach (var r in _renderers)
            {
                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    slotRenderers.Add(r);
                    slotIndices.Add(m);
                    baseColors.Add(mat != null && mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId) : Color.white);
                    baseEmission.Add(mat != null && mat.HasProperty(EmissionColorId) && mat.IsKeywordEnabled("_EMISSION")
                        ? mat.GetColor(EmissionColorId) : Color.black);
                }
            }
            _slotRenderers = slotRenderers.ToArray();
            _slotIndices = slotIndices.ToArray();
            _baseColors = baseColors.ToArray();
            _baseEmission = baseEmission.ToArray();
            Apply(0f);
        }

        /// <summary>코어와 연결되어 동작 중인지. false면 어둡게 + 불이 꺼진다.</summary>
        public void SetOperational(bool operational)
        {
            if (_operational == operational)
                return;
            _operational = operational;
            Apply(Time.time);
        }

        public void SetHighlighted(bool highlighted)
        {
            if (_highlighted == highlighted)
                return;
            _highlighted = highlighted;
            SetRimVisible(highlighted);
            Apply(Time.time);
        }

        public void SetDamageVisual(ModuleDamageVisual damage)
        {
            if (_damage == damage)
                return;
            _damage = damage;
            Apply(Time.time);
        }

        /// <summary>내구도가 낮아 효율이 떨어진 상태 (4-3).</summary>
        public void SetWorn(bool worn)
        {
            if (_worn == worn)
                return;
            _worn = worn;
            Apply(Time.time);
        }

        private void Update()
        {
            if (Animating)
                Apply(Time.time);
        }

        private void Apply(float time)
        {
            if (_slotRenderers == null)
                return;

            bool normal = _operational && !_highlighted && _damage == ModuleDamageVisual.None && !_worn;
            float emissionScale = EmissionScale(time);
            for (int i = 0; i < _slotRenderers.Length; i++)
            {
                if (normal)
                {
                    _slotRenderers[i].SetPropertyBlock(null, _slotIndices[i]);
                    continue;
                }

                Color color = _baseColors[i];
                if (_damage == ModuleDamageVisual.Damaged)
                    color = Color.Lerp(color, _damagedTint, _damagedTintAmount);
                else if (_damage == ModuleDamageVisual.Repairing)
                    color = Color.Lerp(color, _repairingTint, _repairingTintAmount * (0.6f + 0.4f * Mathf.Sin(time * 3f)));
                else if (_worn)
                    color = Color.Lerp(color, _wornTint, _wornTintAmount);
                if (_highlighted)
                    color = Color.Lerp(color, _highlightColor, _highlightTintAmount);
                color.a = _baseColors[i].a; // 반투명 재질(온실 유리)은 상태 색이 바뀌어도 투명도 유지
                if (!_operational)
                    color = new Color(color.r * _inactiveBrightness, color.g * _inactiveBrightness, color.b * _inactiveBrightness, color.a);

                _propertyBlock.Clear();
                _propertyBlock.SetColor(BaseColorId, color);
                if (_baseEmission[i].maxColorComponent > 0f)
                    _propertyBlock.SetColor(EmissionColorId, _baseEmission[i] * emissionScale);
                _slotRenderers[i].SetPropertyBlock(_propertyBlock, _slotIndices[i]);
            }
        }

        /// <summary>창문·띠 발광 배율. 비활성 0, 파손은 불규칙 깜빡임, 노후는 약하게.</summary>
        private float EmissionScale(float time)
        {
            if (!_operational)
                return 0f;
            if (_damage == ModuleDamageVisual.Damaged)
            {
                // 짧게 꺼졌다 켜지는 불규칙 깜빡임
                float n = Mathf.PerlinNoise(time * 7f, _seed);
                return n > 0.55f ? 0.08f : 0.6f + 0.4f * n;
            }
            if (_damage == ModuleDamageVisual.Repairing)
                return 0.5f;
            return _worn ? _wornEmission : 1f;
        }

        /// <summary>
        /// 선택 가장자리 빛: 각 메시를 SelectionRim 재질로 한 번 더 그리는 복제본을 처음 선택될 때 만든다.
        /// 복제본은 원본 렌더러의 자식이라 회전하는 부품(태양광 패널 등)도 따라간다. 상태 틴트 대상(_renderers)에는 포함되지 않는다.
        /// </summary>
        private void SetRimVisible(bool visible)
        {
            if (_rimMaterial == null || _renderers == null)
                return;
            if (visible && _rimCopies == null)
            {
                _rimCopies = new System.Collections.Generic.List<GameObject>();
                foreach (var r in _renderers)
                {
                    var filter = r.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                        continue;
                    var copy = new GameObject(r.name + "_Rim");
                    copy.transform.SetParent(r.transform, false); // 원본의 위치·회전·스케일을 그대로 따름
                    copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    var rr = copy.AddComponent<MeshRenderer>();
                    var mats = new Material[filter.sharedMesh.subMeshCount];
                    for (int m = 0; m < mats.Length; m++)
                        mats[m] = _rimMaterial;
                    rr.sharedMaterials = mats;
                    rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    rr.receiveShadows = false;
                    _rimCopies.Add(copy);
                }
            }
            if (_rimCopies == null)
                return;
            foreach (var copy in _rimCopies)
            {
                if (copy != null)
                    copy.SetActive(visible);
            }
        }
    }
}
