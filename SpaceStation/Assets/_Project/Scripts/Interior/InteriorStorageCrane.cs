using System;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 대형 창고 다리 크레인 (사용자 2026-10-31 대형 창고 시안 1 "이대로 유니티로 옮겨줘"):
    /// 고창 천장 레일 위 다리(Bridge, x)와 트롤리(Trolley, z)가 선반 윗단의 상자 하나로 가서 집게(Grip)를 내려 집고,
    /// 다른 빈 윗단 칸으로 옮겨 내려놓는다 (방금 내려놓은 상자를 바로 다시 집지 않음). 줄(Cable)은 트롤리에서 집게 틀 위까지 늘고 준다.
    /// 모듈이 멈추면(비활성 · 전력 부족) 그 자리에 멈춤. 좌표는 모두 템플릿 루트 로컬 (모델 자세 그대로 = 0).
    /// 정차 자리 일 (화물 환적장 크레인 B, 2026-11-10): <see cref="RequestDock"/> = 정차한 보트 고물에 상자를 내려놓거나(빈 보트) 실린 상자를 집어 옴(실린 보트) —
    /// 하던 한 번을 마친 뒤 그 자리로 가서 내리고 올림. 보트는 <see cref="DockBusy"/>가 풀릴 때까지 기다림 (<see cref="InteriorFlumeRide"/>).
    /// </summary>
    public sealed class InteriorStorageCrane : InteriorRoomDisplay
    {
        private const float BridgeSpeed = 0.6f;   // m/s
        private const float TrolleySpeed = 0.5f;  // m/s
        private const float LiftSpeed = 0.3f;     // m/s
        private const float GrabPause = 0.4f;
        private const float CyclePause = 1.5f;

        [Serializable]
        public struct Rig
        {
            public Transform Bridge;     // 레일을 따라 x (모델 자세 x = RestX)
            public Transform Trolley;    // Bridge 자식 — 다리를 따라 z (모델 = RestZ)
            public Transform Cable;      // Trolley 자식 — 피벗 = 줄 위 끝 (CableTopY), 모델 길이 CableLength
            public Transform Grip;       // Trolley 자식 — 피벗 = 집게 틀 아랫면 (모델 = GripRestY, 상자를 든 높이)
            public float RestX, RestZ;
            public float MinX, MaxX;     // 다리 이동 범위 (대차가 모서리 리브 받침에 닿지 않게)
            public float GripRestY;
            public float GripFrameHeight; // 집게 틀 두께 (줄 아래 끝 = 집게 틀 윗면)
            public float CableTopY;
            public float CableLength;
            public float CrateHeight;    // 집게 틀 아랫면이 닿는 상자 윗면 = 칸 바닥 + 이 값
            public float LiftSpeed;      // 집게 오르내림 m/s (0 = 기본 0.3)
        }

        private enum Phase { Wait, Travel, Lower, Raise }
        private enum Dock { None, Requested, Going, Working }

        [SerializeField] private Rig _rig;
        [SerializeField] private Transform[] _crates = Array.Empty<Transform>();   // 옮기는 상자 (피벗 = 바닥 가운데). 처음 Crates[i]는 Slots[i]
        [SerializeField] private Vector3[] _slots = Array.Empty<Vector3>();        // 윗단 칸 (상자 바닥 가운데)

        private Vector3 _bridgeRest, _trolleyRest, _gripRest, _cableScale;
        private float _x, _z, _y, _fromX, _fromZ, _fromY, _toX, _toZ, _toY, _t, _duration, _pause;
        private Phase _phase;
        private int[] _slotCrate;
        private int _held = -1;
        private int _target = -1;
        private int _lastFilled = -1;      // 방금 놓은 칸 (바로 다시 집지 않음)
        private int _lastEmptied = -1;     // 방금 집은 칸 (제자리에 도로 놓지 않음)
        private bool _ready;
        [SerializeField] private int _placed = -1;   // 처음 칸에 놓인 상자 수 (나머지 상자 = 보트 고물 등 칸 밖, -1 = 모두)
        private Dock _dock;
        private Transform _dockAnchor;   // 보트 고물 상자 자리 (바닥 가운데, 보트 피벗 자식)
        private Transform _dockCargo;    // 보트에 실린 상자 (없으면 빈 보트) — 일이 끝나면 결과

        /// <summary>정차 자리 일을 맡았거나 하는 중 (보트는 이게 풀릴 때까지 기다림).</summary>
        public bool DockBusy => _dock != Dock.None;
        /// <summary>정차 자리 위에서 내리는 중 (이때는 보트를 보내면 안 됨 — 취소 불가).</summary>
        public bool DockCommitted => _dock == Dock.Working;
        /// <summary>모듈이 돌고 있음 (멈추면 정차 일은 건너뜀).</summary>
        public bool Running => Active;
        /// <summary>정차 일 뒤 보트 고물에 실린 상자 (없으면 null).</summary>
        public Transform DockResult => _dockCargo;

        public void Configure(Rig rig, Transform[] crates, Vector3[] slots, int placed = -1)
        {
            _rig = rig;
            _crates = crates ?? Array.Empty<Transform>();
            _slots = slots ?? Array.Empty<Vector3>();
            _placed = placed;
        }

        /// <summary>정차 자리 일 요청: anchor = 보트 고물 상자 자리, cargo = 지금 실린 상자 (null = 빈 보트 → 화물선 상자를 실어 줌).</summary>
        public void RequestDock(Transform anchor, Transform cargo)
        {
            if (anchor == null || (cargo != null && Array.IndexOf(_crates, cargo) < 0))
                return;
            _dockAnchor = anchor;
            _dockCargo = cargo;
            _dock = Dock.Requested;
        }

        /// <summary>정차 일 취소 (보트가 떠나야 함). 정차 자리 위에서 내리는 중이면 무시.</summary>
        public void CancelDock()
        {
            if (_dock == Dock.Working)
                return;
            _dock = Dock.None;
        }

        private void Setup()
        {
            if (_ready)
                return;
            _ready = true;
            _bridgeRest = _rig.Bridge != null ? _rig.Bridge.localPosition : Vector3.zero;
            _trolleyRest = _rig.Trolley != null ? _rig.Trolley.localPosition : Vector3.zero;
            _gripRest = _rig.Grip != null ? _rig.Grip.localPosition : Vector3.zero;
            _cableScale = _rig.Cable != null ? _rig.Cable.localScale : Vector3.one;
            _x = _rig.RestX;
            _z = _rig.RestZ;
            _y = _rig.GripRestY;
            _slotCrate = new int[_slots.Length];
            int placed = _placed < 0 ? _crates.Length : Mathf.Min(_placed, _crates.Length);
            for (int k = 0; k < _slotCrate.Length; k++)
                _slotCrate[k] = k < placed && _crates[k] != null ? k : -1;
            _phase = Phase.Wait;
            _pause = 1f;
            Apply();
        }

        protected override void Refresh()
        {
            Setup();
        }

        private void Update()
        {
            if (!_ready || !Active || _rig.Bridge == null || _rig.Trolley == null || _rig.Grip == null || _slots.Length == 0)
                return;   // 모듈이 멈추면 그 자리에 멈춤
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            switch (_phase)
            {
                case Phase.Wait:
                    _pause -= dt;
                    if (_pause <= 0f)
                        Next();
                    break;
                case Phase.Travel:
                    if (Step(dt))
                    {
                        if (_target < 0)
                        {
                            if (_dock == Dock.Going && _dockAnchor != null)
                            {
                                // 정차 자리 위 → 보트 고물 상자 윗면까지 내림
                                _dock = Dock.Working;
                                BeginLift(transform.InverseTransformPoint(_dockAnchor.position).y + _rig.CrateHeight, Phase.Lower);
                            }
                            else
                            {
                                _phase = Phase.Wait;   // 가는 사이 취소됨
                                _pause = 0.3f;
                            }
                            break;
                        }
                        // 칸 위에 섬 → 상자 윗면까지 내림
                        BeginLift(_slots[_target].y + _rig.CrateHeight, Phase.Lower);
                    }
                    break;
                case Phase.Lower:
                    if (Step(dt))
                    {
                        if (_target < 0)
                            DockGrab();
                        else
                            Grab();
                        _pause = GrabPause;
                        BeginLift(_rig.GripRestY, Phase.Raise);
                    }
                    break;
                case Phase.Raise:
                    if (_pause > 0f)
                    {
                        _pause -= dt;   // 집거나 놓은 뒤 잠깐 멈췄다 올림
                        break;
                    }
                    if (Step(dt))
                    {
                        _phase = Phase.Wait;
                        _pause = _held >= 0 ? 0.2f : CyclePause;
                        if (_dock == Dock.Working)
                        {
                            _dock = Dock.None;   // 정차 일 끝
                            _pause = 0.2f;
                        }
                        else if (_dock != Dock.None)
                            _pause = 0f;
                    }
                    break;
            }
            Apply();
        }

        /// <summary>
        /// 정차 일 차례: 빈 보트 + 빈 손 → 평소처럼 화물선 상자를 집으러 감 (다음 차례에 정차 자리로), 실린 보트 + 든 손 → 평소처럼 빈 칸에 먼저 내려놓음,
        /// 그 밖 = 정차 자리로 감.
        /// </summary>
        private bool NextDock()
        {
            if (_dock == Dock.None || _dockAnchor == null)
                return false;
            bool unload = _dockCargo != null;
            if (unload == (_held >= 0))
                return false;
            var to = transform.InverseTransformPoint(_dockAnchor.position);
            _target = -1;
            _dock = Dock.Going;
            _fromX = _x;
            _fromZ = _z;
            _toX = Mathf.Clamp(to.x, _rig.MinX, _rig.MaxX);
            _toZ = to.z;
            _fromY = _toY = _y;
            _duration = Mathf.Max(0.4f, Mathf.Abs(_toX - _fromX) / BridgeSpeed, Mathf.Abs(_toZ - _fromZ) / TrolleySpeed);
            _t = 0f;
            _phase = Phase.Travel;
            return true;
        }

        /// <summary>보트 고물에서: 들었으면 고물 상자 자리에 내려놓고, 비었으면 실린 상자를 집음.</summary>
        private void DockGrab()
        {
            if (_held >= 0)
            {
                var crate = _crates[_held];
                crate.SetParent(_dockAnchor, true);
                crate.localPosition = Vector3.zero;
                crate.localRotation = Quaternion.identity;
                _held = -1;
                _dockCargo = crate;
            }
            else if (_dockCargo != null)
            {
                _dockCargo.SetParent(_rig.Grip, true);
                _held = Array.IndexOf(_crates, _dockCargo);
                _dockCargo = null;
            }
        }

        /// <summary>다음 칸: 들었으면 방금 집은 칸이 아닌 빈 칸, 비었으면 방금 놓은 칸이 아닌 찬 칸.</summary>
        private void Next()
        {
            if (NextDock())
                return;
            bool wantEmpty = _held >= 0;
            int avoid = wantEmpty ? _lastEmptied : _lastFilled;
            int candidates = wantEmpty ? _slotCrate.Length - Occupied() : Occupied();
            int count = 0, pick = -1;
            for (int k = 0; k < _slotCrate.Length; k++)
            {
                bool empty = _slotCrate[k] < 0;
                if (empty != wantEmpty || (k == avoid && candidates > 1))
                    continue;
                count++;
                if (UnityEngine.Random.Range(0, count) == 0)
                    pick = k;
            }
            if (pick < 0)
            {
                _pause = 1f;
                return;
            }
            _target = pick;
            var to = _slots[pick];
            _fromX = _x;
            _fromZ = _z;
            _toX = Mathf.Clamp(to.x, _rig.MinX, _rig.MaxX);
            _toZ = to.z;
            _fromY = _toY = _y;
            _duration = Mathf.Max(0.4f, Mathf.Abs(_toX - _fromX) / BridgeSpeed, Mathf.Abs(_toZ - _fromZ) / TrolleySpeed);
            _t = 0f;
            _phase = Phase.Travel;
        }

        private void BeginLift(float toY, Phase phase)
        {
            _fromX = _toX = _x;
            _fromZ = _toZ = _z;
            _fromY = _y;
            _toY = toY;
            _duration = Mathf.Max(0.3f, Mathf.Abs(_toY - _fromY) / (_rig.LiftSpeed > 0f ? _rig.LiftSpeed : LiftSpeed));
            _t = 0f;
            _phase = phase;
        }

        private bool Step(float dt)
        {
            _t = Mathf.Min(1f, _t + dt / _duration);
            float u = Smooth(_t);
            _x = Mathf.Lerp(_fromX, _toX, u);
            _z = Mathf.Lerp(_fromZ, _toZ, u);
            _y = Mathf.Lerp(_fromY, _toY, u);
            return _t >= 1f;
        }

        /// <summary>집게가 상자 윗면에 닿음: 비었으면 상자를 집고, 들었으면 칸에 내려놓음.</summary>
        private void Grab()
        {
            if (_held < 0)
            {
                int c = _slotCrate[_target];
                if (c >= 0 && _crates[c] != null)
                {
                    _crates[c].SetParent(_rig.Grip, true);
                    _held = c;
                    _slotCrate[_target] = -1;
                    _lastEmptied = _target;
                }
            }
            else
            {
                var crate = _crates[_held];
                crate.SetParent(transform, true);
                crate.localPosition = _slots[_target];   // 칸 바닥에 딱 맞게 (움직이며 생긴 오차 없앰)
                crate.localRotation = Quaternion.identity;
                _slotCrate[_target] = _held;
                _lastFilled = _target;
                _held = -1;
            }
        }

        private void Apply()
        {
            if (_rig.Bridge != null)
                _rig.Bridge.localPosition = _bridgeRest + Vector3.right * (_x - _rig.RestX);
            if (_rig.Trolley != null)
                _rig.Trolley.localPosition = _trolleyRest + Vector3.forward * (_z - _rig.RestZ);
            if (_rig.Grip != null)
                _rig.Grip.localPosition = _gripRest + Vector3.up * (_y - _rig.GripRestY);
            if (_rig.Cable != null)
            {
                float len = _rig.CableTopY - (_y + _rig.GripFrameHeight);
                var s = _cableScale;
                s.y = _cableScale.y * Mathf.Max(0.01f, len) / Mathf.Max(0.01f, _rig.CableLength);
                _rig.Cable.localScale = s;
            }
        }

        private int Occupied()
        {
            int n = 0;
            foreach (int c in _slotCrate)
            {
                if (c >= 0)
                    n++;
            }
            return n;
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
