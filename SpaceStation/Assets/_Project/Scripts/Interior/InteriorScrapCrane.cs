using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 자재 재생 플랜트 집게 크레인 (사용자 2026-10-31 "기계팔이 진짜로 옆쪽에서 물건을 집어와서 용광로에 넣는 동작"):
    /// 남 컨베이어가 압축 폐자재 덩이(INT_EvoReclaim_Bale을 본뜬 복제 — 벽 쪽 덮개 속에서 나옴)를 돔 가장자리 끝까지 실어 오면,
    /// 돛대의 붐이 그쪽으로 돌고 트롤리가 붐 끝으로 나가 집게를 벌린 채 내려 덩이를 쥔다. 들어 올려 안으로 당긴 뒤 돌아 쇳물 위에서 놓으면
    /// 덩이는 쇳물에 떨어져 불꽃(<see cref="InteriorFx.Spark"/>)을 튀기고 가라앉는다. 놓는 방위는 매번 바뀜 (붐이 도는 모습이 보이게).
    /// 모듈이 멈추면(비활성 · 전력 부족) 그 자리에 멈춤. 좌표는 모두 템플릿 루트 로컬 — 붐 = 돛대 축 피벗 (모델 자세 = 붐이 +x),
    /// 트롤리 · 줄 · 집게 · 손가락은 그 아래 피벗 (<c>InteriorTemplateBuilder.BuildEvoReclaim</c>이 만듦).
    /// </summary>
    public sealed class InteriorScrapCrane : InteriorRoomDisplay
    {
        private const float SwingSpeed = 32f;      // 붐 °/s
        private const float TrolleySpeed = 0.8f;   // m/s
        private const float HoistSpeed = 0.35f;    // 집게 오르내림 m/s
        private const float FingerSpeed = 50f;     // 손가락 °/s
        private const float StepPause = 0.3f;      // 동작 사이 멈춤
        private const float BeltSpeed = 0.22f;     // m/s
        private const float BaleGap = 0.55f;       // 벨트 위 덩이 가운데 간격
        private const int BeltMax = 3;
        private const float GrowTime = 0.3f;       // 덮개 속에서 나타남
        private const float Gravity = 9.8f;
        private const float SinkTime = 1.4f;
        private const float SinkDepth = 0.25f;
        private const float DropHeight = 0.85f;    // 놓을 때 덩이 밑 ~ 쇳물 윗면
        private const float GripAbove = 0.24f;     // 쥘 때 경첩 ~ 덩이 윗면 (손가락 끝이 덩이 옆면 가운데)
        private const float OpenDegrees = 35f;
        private const float GripDegrees = 16f;
        private static readonly float[] DropTurns = { 90f, 180f, 270f };

        [SerializeField] private Transform _boom;
        [SerializeField] private Transform _trolley;
        [SerializeField] private Transform _cable;
        [SerializeField] private Transform _claw;
        [SerializeField] private Transform[] _fingers;
        [SerializeField] private Vector3[] _fingerAxes;      // 손가락 피벗 로컬, +각 = 벌림
        [SerializeField] private Transform _bale;            // 덩이 본 (피벗 = 덩이 가운데)
        [SerializeField] private Vector3 _beltFrom;          // 덩이 가운데 — 덮개 속 시작 · 벨트 끝 (크레인이 집음)
        [SerializeField] private Vector3 _beltTo;
        [SerializeField] private Vector3 _baleHalf = new Vector3(0.2f, 0.11f, 0.16f);
        [SerializeField] private Vector3 _melt;              // 쇳물 윗면 가운데
        [SerializeField] private float _restRadius = 1f;     // 모델 자세의 트롤리 반지름 (= 나르는 반지름)
        [SerializeField] private float _dropRadius = 0.7f;
        [SerializeField] private float _hingeModel;          // 모델 자세의 집게 경첩 높이
        [SerializeField] private float _hingeUp;             // 든 높이
        [SerializeField] private float _cableTop;            // 줄 위 끝 (줄 모델 = 1m)
        [SerializeField] private Material _spark;

        private enum Step { Wait, SwingPick, OutPick, LowerPick, Grip, RaisePick, InPick, SwingDrop, OutDrop, LowerDrop, Release, RaiseDrop, InDrop }

        private sealed class Bale
        {
            public Transform T;
            public float S;        // 벨트 위 거리 (0 = 시작)
            public float Grow;
            public float Fall = -1f;
            public float Speed;
            public float Sink = -1f;
        }

        private readonly List<Bale> _belt = new List<Bale>();
        private readonly List<Bale> _loose = new List<Bale>();   // 떨어지는 중 · 가라앉는 중
        private readonly Stack<Transform> _pool = new Stack<Transform>();
        private Bale _held;
        private Vector3 _hub, _trolleyRest, _clawRest;
        private float _yaw, _radius, _hinge, _finger;
        private float _fromYaw, _fromRadius, _fromHinge, _fromFinger;
        private float _toYaw, _toRadius, _toHinge, _toFinger;
        private float _pickYaw, _pickRadius, _pickHinge, _dropHinge;
        private Step _step;
        private float _t, _duration, _pause;
        private int _turn;
        private bool _ready;

        public void Configure(Transform boom, Transform trolley, Transform cable, Transform claw, Transform[] fingers, Vector3[] fingerAxes, Transform bale,
            Vector3 beltFrom, Vector3 beltTo, Vector3 baleHalf, Vector3 melt, float restRadius, float dropRadius, float hingeModel, float hingeUp, float cableTop, Material spark)
        {
            _boom = boom;
            _trolley = trolley;
            _cable = cable;
            _claw = claw;
            _fingers = fingers;
            _fingerAxes = fingerAxes;
            _bale = bale;
            _beltFrom = beltFrom;
            _beltTo = beltTo;
            _baleHalf = baleHalf;
            _melt = melt;
            _restRadius = restRadius;
            _dropRadius = dropRadius;
            _hingeModel = hingeModel;
            _hingeUp = hingeUp;
            _cableTop = cableTop;
            _spark = spark;
        }

        private void Setup()
        {
            if (_ready || _boom == null || _trolley == null || _claw == null || _bale == null)
                return;
            _ready = true;
            _hub = _boom.localPosition;
            _trolleyRest = _trolley.localPosition;
            _clawRest = _claw.localPosition;
            _bale.gameObject.SetActive(false);   // 본뜨기 전용
            var d = _beltTo - _hub;
            _pickYaw = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
            _pickRadius = new Vector2(d.x, d.z).magnitude;
            _pickHinge = _beltTo.y + _baleHalf.y + GripAbove;
            _dropHinge = _melt.y + DropHeight + _baleHalf.y * 2f + GripAbove;
            _yaw = _pickYaw;
            _radius = _restRadius;
            _hinge = _hingeUp;
            _finger = 0f;
            Apply();
            // 처음부터 벨트에 덩이 셋 (끝에 하나)
            float length = BeltLength;
            for (int i = 0; i < BeltMax; i++)
            {
                var b = new Bale { T = Take(), S = length - i * (BaleGap + 0.05f), Grow = 1f };
                _belt.Add(b);
                PoseOnBelt(b);
            }
            _step = Step.Wait;
        }

        protected override void Refresh()
        {
            Setup();
        }

        private float BeltLength => Vector3.Distance(_beltFrom, _beltTo);

        private void Update()
        {
            if (!_ready || !Active)
                return;   // 모듈이 멈추면 크레인 · 벨트도 그 자리에 멈춤
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            UpdateBelt(dt);
            UpdateLoose(dt);
            UpdateCrane(dt);
            Apply();
        }

        // ---------------- 벨트 ----------------

        private void UpdateBelt(float dt)
        {
            float length = BeltLength;
            for (int i = 0; i < _belt.Count; i++)
            {
                var b = _belt[i];
                float limit = i == 0 ? length : _belt[i - 1].S - BaleGap;
                b.S = Mathf.Min(b.S + BeltSpeed * dt, Mathf.Max(b.S, limit));
                b.Grow = Mathf.Min(1f, b.Grow + dt / GrowTime);
                PoseOnBelt(b);
            }
            if (_belt.Count < BeltMax && (_belt.Count == 0 || _belt[_belt.Count - 1].S >= BaleGap))
            {
                var b = new Bale { T = Take(), S = 0f, Grow = 0f };
                _belt.Add(b);
                PoseOnBelt(b);
            }
        }

        private void PoseOnBelt(Bale b)
        {
            float length = BeltLength;
            b.T.localPosition = Vector3.Lerp(_beltFrom, _beltTo, length > 0f ? b.S / length : 1f);
            b.T.localRotation = _bale.localRotation;
            b.T.localScale = _bale.localScale * Smooth(b.Grow);
        }

        private bool BaleReady => _belt.Count > 0 && _belt[0].S >= BeltLength - 0.001f;

        // ---------------- 떨어짐 · 가라앉음 ----------------

        private void UpdateLoose(float dt)
        {
            for (int i = _loose.Count - 1; i >= 0; i--)
            {
                var b = _loose[i];
                var p = b.T.localPosition;
                if (b.Sink < 0f)
                {
                    b.Speed += Gravity * dt;
                    p.y -= b.Speed * dt;
                    if (p.y - _baleHalf.y <= _melt.y)
                    {
                        p.y = _melt.y + _baleHalf.y;
                        b.Sink = 0f;
                        var parent = b.T.parent;
                        InteriorFx.Spark(parent, parent.TransformPoint(new Vector3(p.x, _melt.y + 0.02f, p.z)), parent.TransformDirection(Vector3.up), _spark, 10, 18, 2f);
                    }
                    b.T.localPosition = p;
                    continue;
                }
                b.Sink += dt / SinkTime;
                p.y = _melt.y + _baleHalf.y - SinkDepth * Smooth(b.Sink);
                b.T.localPosition = p;
                b.T.localScale = _bale.localScale * Mathf.Lerp(1f, 0.1f, Smooth(b.Sink));
                if (b.Sink >= 1f)
                {
                    b.T.gameObject.SetActive(false);
                    _pool.Push(b.T);
                    _loose.RemoveAt(i);
                }
            }
        }

        // ---------------- 크레인 ----------------

        private void UpdateCrane(float dt)
        {
            if (_pause > 0f)
            {
                _pause -= dt;
                return;
            }
            if (_step == Step.Wait)
            {
                if (BaleReady)
                    Begin(Step.SwingPick);
                return;
            }
            _t = Mathf.Min(1f, _t + dt / _duration);
            float u = Smooth(_t);
            _yaw = _fromYaw + Mathf.DeltaAngle(_fromYaw, _toYaw) * u;
            _radius = Mathf.Lerp(_fromRadius, _toRadius, u);
            _hinge = Mathf.Lerp(_fromHinge, _toHinge, u);
            _finger = Mathf.Lerp(_fromFinger, _toFinger, u);
            if (_step == Step.Release && _held != null && _t >= 0.3f)
            {
                _held.T.SetParent(_bale.parent, true);   // 손가락이 조금 벌어지면 쇳물로 떨어짐
                _held.Speed = 0f;
                _held.Sink = -1f;
                _loose.Add(_held);
                _held = null;
            }
            if (_t < 1f)
                return;
            switch (_step)
            {
                case Step.SwingPick: Begin(Step.OutPick); break;
                case Step.OutPick: Begin(Step.LowerPick); break;
                case Step.LowerPick: Begin(Step.Grip); break;
                case Step.Grip:
                    if (_belt.Count > 0)
                    {
                        _held = _belt[0];
                        _belt.RemoveAt(0);
                        _held.T.SetParent(_claw, true);
                    }
                    Begin(Step.RaisePick);
                    break;
                case Step.RaisePick: Begin(Step.InPick); break;
                case Step.InPick: Begin(Step.SwingDrop); break;
                case Step.SwingDrop: Begin(Step.OutDrop); break;
                case Step.OutDrop: Begin(Step.LowerDrop); break;
                case Step.LowerDrop: Begin(Step.Release); break;
                case Step.Release: Begin(Step.RaiseDrop); break;
                case Step.RaiseDrop: Begin(Step.InDrop); break;
                default:
                    _step = Step.Wait;
                    _turn = (_turn + 1) % DropTurns.Length;
                    break;
            }
        }

        private void Begin(Step step)
        {
            _step = step;
            _fromYaw = _toYaw = _yaw;
            _fromRadius = _toRadius = _radius;
            _fromHinge = _toHinge = _hinge;
            _fromFinger = _toFinger = _finger;
            switch (step)
            {
                case Step.SwingPick: _toYaw = _pickYaw; break;
                case Step.OutPick: _toRadius = _pickRadius; _toFinger = OpenDegrees; break;
                case Step.LowerPick: _toHinge = _pickHinge; break;
                case Step.Grip: _toFinger = GripDegrees; break;
                case Step.RaisePick: _toHinge = _hingeUp; break;
                case Step.InPick: _toRadius = _restRadius; break;
                case Step.SwingDrop: _toYaw = _pickYaw + DropTurns[_turn]; break;
                case Step.OutDrop: _toRadius = _dropRadius; break;
                case Step.LowerDrop: _toHinge = _dropHinge; break;
                case Step.Release: _toFinger = OpenDegrees; break;
                case Step.RaiseDrop: _toHinge = _hingeUp; _toFinger = 0f; break;
                case Step.InDrop: _toRadius = _restRadius; break;
            }
            _duration = Mathf.Max(0.25f,
                Mathf.Abs(Mathf.DeltaAngle(_fromYaw, _toYaw)) / SwingSpeed,
                Mathf.Abs(_toRadius - _fromRadius) / TrolleySpeed,
                Mathf.Abs(_toHinge - _fromHinge) / HoistSpeed,
                Mathf.Abs(_toFinger - _fromFinger) / FingerSpeed);
            _t = 0f;
            _pause = StepPause;
        }

        private void Apply()
        {
            _boom.localRotation = Quaternion.AngleAxis(_yaw, Vector3.up);
            _trolley.localPosition = _trolleyRest + Vector3.right * (_radius - _restRadius);
            _claw.localPosition = _clawRest + Vector3.up * (_hinge - _hingeModel);
            if (_cable != null)
            {
                var s = _cable.localScale;
                s.y = Mathf.Max(0.01f, _cableTop - _hinge - 0.14f);   // 줄 끝이 집게 머리 윗면 (경첩 + 0.15) 속 1cm
                _cable.localScale = s;
            }
            if (_fingers == null)
                return;
            for (int i = 0; i < _fingers.Length; i++)
            {
                if (_fingers[i] != null)
                    _fingers[i].localRotation = Quaternion.AngleAxis(_finger, _fingerAxes[i]);
            }
        }

        private Transform Take()
        {
            Transform t;
            if (_pool.Count > 0)
                t = _pool.Pop();
            else
            {
                t = Instantiate(_bale.gameObject, _bale.parent).transform;
                t.name = "ScrapBale";
            }
            t.SetParent(_bale.parent, false);
            t.gameObject.SetActive(true);
            return t;
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
