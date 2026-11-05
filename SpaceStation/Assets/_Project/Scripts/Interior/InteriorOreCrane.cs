using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 정련 콤비나트 격납고 집게 크레인: 들보가 레일을 따라 채굴선 짐칸 위로 가서 집게를 벌린 채 내려 광석을 쥐고, 들어 올려 광석 통 위로 나른 뒤 놓으면 광석이 더미로 떨어져 묻힘.
    /// 광석은 원형(INT_EvoRefineryCombine_OreRock)의 복제 — 짐칸에서 집기 전에 나타나고 더미에 떨어지면 사라짐. 모듈이 멈추면(비활성 · 전력 부족) 그 자리에 멈춤.
    /// 좌표는 모두 템플릿 기준, 들보 피벗 아래 줄 · 집게 · 손가락 피벗 (<c>InteriorTemplateBuilder.BuildEvoRefineryCombine</c>이 만듦).
    /// </summary>
    public sealed class InteriorOreCrane : InteriorRoomDisplay
    {
        private const float GantrySpeed = 0.9f;
        private const float HoistSpeed = 0.45f;
        private const float FingerSpeed = 60f;
        private const float StepPause = 0.3f;
        private const float OpenDegrees = 34f;
        private const float GripDegrees = 12f;
        private const float GripAbove = 0.24f;     // 쥘 때 경첩 ~ 광석 가운데
        private const float Gravity = 9.8f;
        private const float GrowTime = 0.4f;
        private const float SinkTime = 0.7f;

        [SerializeField] private Transform _gantry;
        [SerializeField] private Transform _cable;
        [SerializeField] private Transform _claw;
        [SerializeField] private Transform[] _fingers;
        [SerializeField] private Vector3[] _fingerAxes;   // 손가락 피벗 로컬, + 각 = 벌림
        [SerializeField] private Transform _rock;         // 광석 원형 (원점 = 가운데)
        [SerializeField] private float _restZ;            // 모델 자세의 들보 z
        [SerializeField] private Vector3 _pick;           // 집는 광석 가운데
        [SerializeField] private Vector3 _drop;           // 놓는 광석 가운데 (들보 z · 경첩 높이의 기준)
        [SerializeField] private float _pileY;            // 더미 윗면 (광석이 묻히는 높이)
        [SerializeField] private float _hingeModel;       // 모델 자세의 경첩 높이 (= 든 높이)
        [SerializeField] private float _cableTop;         // 줄 위 끝 (줄 모델 = 1m)
        [SerializeField] private float _startDelay;

        private enum Step { Wait, ToPick, LowerPick, Grip, RaisePick, ToDrop, LowerDrop, Release, RaiseDrop }

        private Step _step;
        private Transform _held;
        private Transform _loose;
        private float _looseSpeed;
        private float _sink = -1f;
        private Transform _spawned;
        private float _grow;
        private float _z, _hinge, _finger;
        private float _fromZ, _fromHinge, _fromFinger, _toZ, _toHinge, _toFinger;
        private float _t, _duration, _pause;
        private Vector3 _gantryRest, _clawRest;
        private Transform _pool;

        public void Configure(Transform gantry, Transform cable, Transform claw, Transform[] fingers, Vector3[] fingerAxes, Transform rock,
            float restZ, Vector3 pick, Vector3 drop, float pileY, float hingeModel, float cableTop, float startDelay)
        {
            _gantry = gantry;
            _cable = cable;
            _claw = claw;
            _fingers = fingers;
            _fingerAxes = fingerAxes;
            _rock = rock;
            _restZ = restZ;
            _pick = pick;
            _drop = drop;
            _pileY = pileY;
            _hingeModel = hingeModel;
            _cableTop = cableTop;
            _startDelay = startDelay;
        }

        private void Awake()
        {
            if (_gantry == null)
                return;
            _gantryRest = _gantry.localPosition;
            _clawRest = _claw.localPosition;
            _z = _restZ;
            _hinge = _hingeModel;
            _pause = _startDelay;
            _step = Step.Wait;
            Apply();
        }

        protected override void Refresh()
        {
        }

        private void Update()
        {
            if (_gantry == null || (Polled && !Active))
                return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            UpdateRocks(dt);
            UpdateCrane(dt);
            Apply();
        }

        private void UpdateRocks(float dt)
        {
            if (_spawned != null && _grow < 1f)
            {
                _grow = Mathf.Min(1f, _grow + dt / GrowTime);
                _spawned.localScale = Vector3.one * Smooth(_grow);
            }
            if (_loose == null)
                return;
            var p = _loose.localPosition;
            if (_sink < 0f)
            {
                _looseSpeed += Gravity * dt;
                p.y -= _looseSpeed * dt;
                if (p.y <= _pileY)
                {
                    p.y = _pileY;
                    _sink = 0f;
                }
                _loose.localPosition = p;
                return;
            }
            _sink += dt / SinkTime;
            _loose.localScale = Vector3.one * Mathf.Lerp(1f, 0f, Smooth(_sink));
            if (_sink >= 1f)
            {
                Destroy(_loose.gameObject);
                _loose = null;
                _sink = -1f;
            }
        }

        private void UpdateCrane(float dt)
        {
            if (_pause > 0f)
            {
                _pause -= dt;
                return;
            }
            if (_step == Step.Wait)
            {
                Begin(Step.ToPick);
                return;
            }
            _t = Mathf.Min(1f, _t + dt / _duration);
            float u = Smooth(_t);
            _z = Mathf.Lerp(_fromZ, _toZ, u);
            _hinge = Mathf.Lerp(_fromHinge, _toHinge, u);
            _finger = Mathf.Lerp(_fromFinger, _toFinger, u);
            if (_step == Step.Release && _held != null && _t >= 0.3f)
            {
                _held.SetParent(transform, true);
                _loose = _held;
                _looseSpeed = 0f;
                _sink = -1f;
                _held = null;
            }
            if (_t < 1f)
                return;
            switch (_step)
            {
                case Step.ToPick:
                    Spawn();
                    Begin(Step.LowerPick);
                    break;
                case Step.LowerPick:
                    Begin(Step.Grip);
                    break;
                case Step.Grip:
                    _held = _spawned;
                    _spawned = null;
                    if (_held != null)
                    {
                        _held.localScale = Vector3.one;
                        _held.SetParent(_claw, true);
                    }
                    Begin(Step.RaisePick);
                    break;
                case Step.RaisePick:
                    Begin(Step.ToDrop);
                    break;
                case Step.ToDrop:
                    Begin(Step.LowerDrop);
                    break;
                case Step.LowerDrop:
                    Begin(Step.Release);
                    break;
                case Step.Release:
                    Begin(Step.RaiseDrop);
                    break;
                default:
                    _step = Step.Wait;
                    _pause = StepPause * 2f;
                    break;
            }
        }

        private void Spawn()
        {
            var go = Instantiate(_rock.gameObject, transform);
            go.name = "OreRockLive";
            go.SetActive(true);
            _spawned = go.transform;
            _spawned.localPosition = _pick;
            _spawned.localRotation = Quaternion.identity;
            _spawned.localScale = Vector3.zero;
            _grow = 0f;
        }

        private void Begin(Step step)
        {
            _step = step;
            _fromZ = _toZ = _z;
            _fromHinge = _toHinge = _hinge;
            _fromFinger = _toFinger = _finger;
            switch (step)
            {
                case Step.ToPick: _toZ = _pick.z; _toHinge = _hingeModel; _toFinger = OpenDegrees; break;
                case Step.LowerPick: _toHinge = _pick.y + GripAbove; break;
                case Step.Grip: _toFinger = GripDegrees; break;
                case Step.RaisePick: _toHinge = _hingeModel; break;
                case Step.ToDrop: _toZ = _drop.z; break;
                case Step.LowerDrop: _toHinge = _drop.y + GripAbove; break;
                case Step.Release: _toFinger = OpenDegrees; break;
                case Step.RaiseDrop: _toHinge = _hingeModel; _toFinger = 0f; break;
            }
            _duration = Mathf.Max(0.25f,
                Mathf.Abs(_toZ - _fromZ) / GantrySpeed,
                Mathf.Abs(_toHinge - _fromHinge) / HoistSpeed,
                Mathf.Abs(_toFinger - _fromFinger) / FingerSpeed);
            _t = 0f;
            _pause = StepPause;
        }

        private void Apply()
        {
            _gantry.localPosition = _gantryRest + Vector3.forward * (_z - _restZ);
            _claw.localPosition = _clawRest + Vector3.up * (_hinge - _hingeModel);
            var s = _cable.localScale;
            s.y = Mathf.Max(0.01f, _cableTop - _hinge - 0.14f);   // 줄 끝이 집게 머리 윗면 (경첩 + 0.15) 속 1cm
            _cable.localScale = s;
            for (int i = 0; i < _fingers.Length; i++)
                _fingers[i].localRotation = Quaternion.AngleAxis(_finger, _fingerAxes[i]);
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
