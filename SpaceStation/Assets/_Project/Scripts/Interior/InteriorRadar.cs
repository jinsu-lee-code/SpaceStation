using System.Collections.Generic;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 센서 어레이(관측 관제실) 레이더 — 사용자 결정 2026-10-19: 실제 예보.
    /// 유성우 · 포격(<see cref="MeteorShowerSystem"/>) 경보면 오는 방향(모듈 로컬) 가장자리에 운석 점(남은 파 수만큼)이 나타나 남은 시간만큼 가운데로 오고,
    /// 도는 빛줄(INT_Sensor_Sweep, <see cref="InteriorOrbit"/>)이 지날 때 밝았다가 흐려진다. 위 · 아래에서 오면 가운데 점이 커졌다 작아짐.
    /// 예보 콘솔 글자 칸 = 이름 · 방향 · 남은 시간 · 파 수 / 예보 없음, 경광등(INT_Sensor_Beacon)은 경보 · 낙하 중에만 깜빡임 (색약: 밝기 · 깜빡임).
    /// 모듈이 꺼지면 점 · 글자 · 경광등 모두 끔.
    /// </summary>
    public sealed class InteriorRadar : InteriorRoomDisplay
    {
        private const int MaxBlips = 6;
        private const float ApproachSeconds = 60f;   // 이만큼 남았을 때 가장자리
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Vector3 _center;
        [SerializeField] private float _radius = 0.8f;
        [SerializeField] private InteriorOrbit _sweep;
        [SerializeField] private Renderer _beacon;
        [Header("예보 글자 칸")]
        [SerializeField] private Vector3 _textCenter;
        [SerializeField] private Vector3 _textNormal;
        [SerializeField] private Vector3 _textUp;
        [SerializeField] private Vector2 _textSize = new Vector2(0.48f, 0.3f);

        private TextMeshPro _text;
        private readonly List<Renderer> _blips = new List<Renderer>();
        private Renderer _centerBlip;
        private MaterialPropertyBlock _block;
        private Color _blipEmission;
        private Color _beaconEmission;
        private MeteorShowerSystem.ShowerPhase _phase;
        private Vector3Int _localDir;
        private float _timeLeft;
        private int _count;
        private bool _vertical;

        public void Configure(Vector3 center, float radius, InteriorOrbit sweep, Renderer beacon, Vector3 textCenter, Vector3 textNormal, Vector3 textUp,
            Vector2 textSize)
        {
            _center = center;
            _radius = radius;
            _sweep = sweep;
            _beacon = beacon;
            _textCenter = textCenter;
            _textNormal = textNormal;
            _textUp = textUp;
            _textSize = textSize;
        }

        private void EnsureParts()
        {
            if (_text != null)
                return;
            _block = new MaterialPropertyBlock();
            _text = CreateText("ForecastText", _textCenter, _textNormal, _textUp, _textSize.x, _textSize.y, 1.6f, TextAlignmentOptions.Center);
            var material = _beacon != null ? _beacon.sharedMaterial : null;   // 경광등 재질 = 발광 주황 (Molten)
            _blipEmission = material != null && material.HasProperty(EmissionColorId) ? material.GetColor(EmissionColorId) : new Color(1f, 0.3f, 0.05f);
            _beaconEmission = _blipEmission;
            for (int i = 0; i < MaxBlips; i++)
                _blips.Add(CreateBlip("Blip", material, new Vector3(0.05f, 0.012f, 0.05f)));
            _centerBlip = CreateBlip("BlipCenter", material, new Vector3(0.09f, 0.012f, 0.09f));
        }

        private Renderer CreateBlip(string name, Material material, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (material != null)
                r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.enabled = false;
            return r;
        }

        protected override void Refresh()
        {
            EnsureParts();
            var shower = Station != null && Station.Simulation != null ? Station.Simulation.MeteorShower : null;
            if (!Active || shower == null)
            {
                _phase = MeteorShowerSystem.ShowerPhase.Idle;
                _count = 0;
                SetText(_text, Active ? "예보 없음" : "센서 꺼짐", ScreenDim);
                return;
            }
            _phase = shower.Phase;
            _timeLeft = shower.TimeLeft;
            _localDir = Module != null ? WorldToLocalDir(Module, shower.Direction) : shower.Direction;
            _vertical = _localDir.y != 0;
            _count = _phase == MeteorShowerSystem.ShowerPhase.Idle ? 0 : Mathf.Clamp(shower.Waves - shower.WavesDone, 1, MaxBlips);
            string dir = MeteorShowerSystem.DirectionName(shower.Direction);
            switch (_phase)
            {
                case MeteorShowerSystem.ShowerPhase.Warning:
                    int s = Mathf.Max(0, Mathf.CeilToInt(_timeLeft));
                    SetText(_text, $"<b>{shower.Label} 예보</b>\n{dir}\n{s / 60}:{s % 60:00} · {shower.Waves}파", ScreenText);
                    break;
                case MeteorShowerSystem.ShowerPhase.Falling:
                    SetText(_text, $"<b>{shower.Label} 낙하 중</b>\n{dir}\n{shower.WavesDone}/{shower.Waves}파", ScreenText);
                    break;
                default:
                    SetText(_text, "예보 없음", ScreenDim);
                    break;
            }
        }

        private void Update()
        {
            if (!Polled || _text == null)
                return;
            float t = Time.unscaledTime;
            bool alert = Active && _phase != MeteorShowerSystem.ShowerPhase.Idle;
            // 경광등: 경보 = 1초에 한 번, 낙하 = 빠르게 (밝기 · 깜빡임으로 구분)
            if (_beacon != null)
            {
                float rate = _phase == MeteorShowerSystem.ShowerPhase.Falling ? 3f : 1f;
                bool on = alert && Mathf.Repeat(t * rate, 1f) < 0.5f;
                _block.Clear();
                _block.SetColor(EmissionColorId, on ? _beaconEmission * 1.6f : Color.black);
                if (!on)
                    _block.SetColor(BaseColorId, new Color(0.25f, 0.08f, 0.02f));
                _beacon.SetPropertyBlock(_block);
            }
            float sweepAngle = _sweep != null ? _sweep.Angle : t * 60f;
            for (int i = 0; i < _blips.Count; i++)
            {
                bool show = alert && !_vertical && i < _count;
                var r = _blips[i];
                if (r.enabled != show)
                    r.enabled = show;
                if (!show)
                    continue;
                // 방향 둘레로 퍼진 점들 (파마다 하나), 남은 시간만큼 가장자리 → 가운데
                float spread = (i - (_count - 1) * 0.5f) * 9f;
                float baseAngle = Mathf.Atan2(_localDir.z, _localDir.x) * Mathf.Rad2Deg + spread;
                float approach = _phase == MeteorShowerSystem.ShowerPhase.Falling ? 0.12f : Mathf.Clamp01(_timeLeft / ApproachSeconds);
                float r01 = Mathf.Lerp(0.15f, 0.92f, approach) + 0.03f * Mathf.Sin(i * 1.7f);
                float a = baseAngle * Mathf.Deg2Rad;
                r.transform.localPosition = _center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (_radius * r01) + Vector3.up * 0.006f;
                // 빛줄이 지난 뒤 흐려짐: 빛줄 앞 가장자리 = atan2 각 −sweepAngle (위 축 회전은 +x를 −z 쪽으로)
                float since = Mathf.Repeat(baseAngle + sweepAngle, 360f);
                SetGlow(r, Mathf.Lerp(1.6f, 0.25f, since / 360f));
            }
            bool center = alert && _vertical;
            if (_centerBlip.enabled != center)
                _centerBlip.enabled = center;
            if (center)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * (_phase == MeteorShowerSystem.ShowerPhase.Falling ? 9f : 4f));
                float size = Mathf.Lerp(0.07f, 0.16f, pulse);
                _centerBlip.transform.localPosition = _center + Vector3.up * 0.006f;
                _centerBlip.transform.localScale = new Vector3(size, 0.012f, size);
                SetGlow(_centerBlip, Mathf.Lerp(0.4f, 1.6f, pulse));
            }
        }

        private void SetGlow(Renderer r, float level)
        {
            _block.Clear();
            _block.SetColor(EmissionColorId, _blipEmission * level);
            r.SetPropertyBlock(_block);
        }
    }
}
