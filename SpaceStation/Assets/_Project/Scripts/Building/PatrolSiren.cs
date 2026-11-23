using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 순찰차 경광등 (바깥 <see cref="PatrolCarFx"/> · 치안 본부 내부 <see cref="Interior.InteriorPatrolCar"/> 공통, 사용자 2026-11-03 "원형으로 불이 들어와서 어색, 확실하게 빛나게, 두 색이 비슷해 보임"):
    /// 둥근 입자 대신 경광등 막대 모양 그대로 상자 둘이 번쩍임 — 파랑 두 번 → 빨강 두 번. 색이 비슷해 보이지 않게 흰빛 없는 진한 파랑 · 빨강, 색약이어도 자리(좌우) · 순서로 구분됨.
    /// 재질 = 추진 불꽃 재질을 복사해 무늬(둥근 얼룩)만 뺀 것 (가산이라 블룸으로 번짐).
    /// </summary>
    public sealed class PatrolSiren
    {
        private const float FlashTime = 0.07f, FlashGap = 0.06f, SideGap = 0.08f;
        private static readonly Color BlueLight = new Color(0f, 0.12f, 1f);
        private static readonly Color RedLight = new Color(1f, 0f, 0f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        private readonly MeshRenderer _blue;
        private readonly MeshRenderer _red;
        private readonly Light _light;
        private readonly float _lightPower;
        private float _clock;
        private bool _on;

        /// <param name="car">차 (자식으로 붙어 함께 움직임)</param>
        /// <param name="source">추진 불꽃 재질 (복사해 무늬를 뺌)</param>
        /// <param name="blueAt">파란 등 가운데 (차 기준)</param>
        /// <param name="redAt">빨간 등 가운데 (차 기준)</param>
        /// <param name="size">등 하나 크기 (차 기준)</param>
        /// <param name="brightness">밝기 배수 (블룸 번짐)</param>
        /// <param name="lightRange">둘레를 물들이는 점광 반지름 (0 = 없음 — 바깥 모듈은 점광 없음)</param>
        /// <param name="lightPower">점광 세기</param>
        public PatrolSiren(Transform car, Material source, Vector3 blueAt, Vector3 redAt, Vector3 size, float brightness, float lightRange = 0f, float lightPower = 0f)
        {
            var material = new Material(source);
            material.SetTexture(BaseMap, null);
            var mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            _blue = Lamp("SirenBlue", car, mesh, material, blueAt, size, BlueLight * brightness);
            _red = Lamp("SirenRed", car, mesh, material, redAt, size, RedLight * brightness * 0.85f);   // 톤매핑이 밝은 빨강을 주황으로 밀어서 파랑보다 낮게 (1.9면 주황으로 보임)
            if (lightRange > 0f)
            {
                var go = new GameObject("SirenLight");
                go.transform.SetParent(car, false);
                go.transform.localPosition = (blueAt + redAt) * 0.5f + Vector3.up * 0.25f;
                _light = go.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.shadows = LightShadows.None;
                _light.range = lightRange;
                _lightPower = lightPower;
                _light.enabled = false;
            }
        }

        private static MeshRenderer Lamp(string name, Transform car, Mesh mesh, Material material, Vector3 at, Vector3 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(car, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColor, color);
            r.SetPropertyBlock(block);
            r.enabled = false;
            return r;
        }

        public bool On => _on;

        /// <summary>지금 켜진 쪽: 0 = 없음, 1 = 파랑, 2 = 빨강 (확인용).</summary>
        public int Lit => _blue.enabled ? 1 : _red.enabled ? 2 : 0;

        public void Start()
        {
            _on = true;
            _clock = 0f;
        }

        public void Stop()
        {
            _on = false;
            _blue.enabled = false;
            _red.enabled = false;
            if (_light != null)
                _light.enabled = false;
        }

        /// <summary>켜져 있으면 번쩍임을 진행 (매 프레임, 부르는 쪽 시간).</summary>
        public void Tick(float dt)
        {
            if (!_on)
                return;
            float side = FlashTime * 2f + FlashGap + SideGap;
            _clock = (_clock + dt) % (side * 2f);
            bool blueSide = _clock < side;
            float t = blueSide ? _clock : _clock - side;
            bool flash = t < FlashTime || (t >= FlashTime + FlashGap && t < FlashTime * 2f + FlashGap);
            _blue.enabled = flash && blueSide;
            _red.enabled = flash && !blueSide;
            if (_light != null)
            {
                _light.enabled = flash;
                _light.color = blueSide ? BlueLight : RedLight;
                _light.intensity = _lightPower;
            }
        }
    }
}
