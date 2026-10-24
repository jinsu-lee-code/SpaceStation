using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-16 ③ 한 방 안에서 주민들이 나눠 쓰는 걸어갈 자리 (템플릿의 서기 자리 — 바닥이 트인 곳). 한 자리에 한 명.
    /// </summary>
    public sealed class WanderRoom
    {
        public readonly List<Vector3> Points = new List<Vector3>();
        public readonly List<float> Yaws = new List<float>();
        /// <summary>자리마다 차지한 주민 (없으면 null).</summary>
        public readonly List<ResidentWander> Owner = new List<ResidentWander>();

        public int Add(Vector3 point, float yaw)
        {
            Points.Add(point);
            Yaws.Add(yaw);
            Owner.Add(null);
            return Points.Count - 1;
        }
    }

    /// <summary>
    /// 11-16 ③ 서 있는 쉬는 동물 주민이 같은 방 안에서 가끔 걸어서 자리를 옮김 (<see cref="WanderRules"/>).
    /// 걸어갈 곳 = 같은 방의 비어 있는 서기 자리, 없으면 원래 자리 주변 0.8~1.4m의 바닥(아래로 쏴서 같은 높이 바닥 + 그 자리에 다른 물체 없음).
    /// 가는 길이 막혔으면(캡슐로 쓸어 봄) 가지 않는다. 플레이어가 앞을 막거나 반응(손 흔들기 등) 중이면 멈춰 기다림.
    /// 걷는 사이 앞에 새 물건(보급 상자 등)이 놓이면 그 자리에 선다.
    /// 다리 · 팔 흔들기는 <see cref="ResidentMotion.Walk"/>.
    /// 11-16 ④ 앉은 주민(<see cref="InitSeated"/>)은 의자 앞 바닥이 비어 있으면 일어나(약 0.5초, 앞으로 밀려나오며) 같은 방을 걷다가,
    /// 나들이를 마치면 의자 앞으로 돌아와 의자 방향으로 돌아서 뒤로 물러나 앉는다.
    /// 12-5 방 표시가 심부름을 시킬 수 있다 (<see cref="StartErrand"/> — 행정 관제소 민원인: 발권기 · 창구): 일어나서 정해 준 길로 가서 잠깐 머문 뒤
    /// 같은 길로 돌아와 앉는다. 길은 방을 만들 때 비워 둔 통로라 막힘 검사를 하지 않는다 (플레이어가 앞을 막으면 기다림). <see cref="Hold"/>면 스스로 나들이를 가지 않음.
    /// </summary>
    public sealed class ResidentWander : MonoBehaviour
    {
        private enum State { Idle, Turn, Walk, StandUp, SeatTurn, SitDown }

        /// <summary>11-16 ④ 앉는 자리(의자 · 소파 · 벤치)를 가리키는 번호 — 방 자리 목록 밖.</summary>
        private const int Seat = -2;

        private const float TurnSpeed = 260f;     // 도/초
        private const float Radius = 0.24f;       // 몸 반지름 (모델 원래 크기 기준, ResidentFigure 캡슐과 같음)
        private const float Height = 0.95f;
        private const float BlockDistance = 0.9f; // 플레이어가 이 안에서 앞을 막으면 기다림

        private WanderRoom _room;
        private int _home;          // 원래 자리 번호
        private int _at;            // 지금 서 있는 자리 번호 (−1 = 자리 아닌 바닥)
        private int _target;        // 가는 자리 번호 (−1 = 자리 아닌 바닥)
        private Vector3 _goal;
        private float _goalYaw;
        private ResidentMotion _motion;
        private Collider _self;
        private Transform _watcher;
        private float _mood;
        private float _scale = 1f;
        private System.Random _rng;
        private State _state;
        private float _next;
        private int _excursions;

        public bool Walking => _state != State.Idle;

        // 12-5 심부름: 정해 준 길(자리 앞 → 통로 → 목적지)로 가서 머문 뒤 같은 길로 돌아와 자리로
        private List<Vector3> _route;
        private int _routeIndex;
        private float _routeYaw;
        private float _routeStay;
        private int _routePhase;      // 0 = 가는 중, 1 = 목적지에 머묾, 2 = 돌아오는 중, 3 = 자리로 가는 마지막 길
        private System.Action _onArrive, _onLeave, _onDone;

        /// <summary>12-5 스스로 나들이를 가지 않음 (자리에서 기다림 — 행정 관제소 민원인). 심부름은 됨.</summary>
        public bool Hold { get; set; }

        /// <summary>12-5 의자에 앉아 쉬는 중 (심부름을 시킬 수 있음).</summary>
        public bool Seated => _state == State.Idle && _at == Seat && _route == null;

        public bool OnErrand => _route != null;

        /// <summary>12-5 심부름 중 목적지에 머무는 중 (통로 밖).</summary>
        public bool ErrandStaying => _route != null && _routePhase == 1;

        /// <summary>12-5 심부름 중 일어서기 · 걷기 · 앉기 (통로를 쓰는 중).</summary>
        public bool ErrandMoving => _route != null && _routePhase != 1;

        /// <summary>12-5 머묾을 끝내고 돌아가기 시작 (머무는 시간을 무한으로 준 심부름 — 통로가 빌 때 방 표시가 부름).</summary>
        public void EndStay()
        {
            if (ErrandStaying)
                _next = Time.unscaledTime;
        }

        /// <summary>
        /// 12-5 심부름 시작: 앉아 있으면 일어나서 <paramref name="route"/>(월드 점, 마지막 = 목적지)를 차례로 걸어가 <paramref name="yaw"/>를 보고
        /// <paramref name="stay"/>초 머문 뒤 같은 길로 돌아와 원래 자리로. 도착 · 떠남 · 자리로 돌아옴 때 알림. 쉬는 중이 아니면 false.
        /// </summary>
        public bool StartErrand(IReadOnlyList<Vector3> route, float yaw, float stay, System.Action onArrive, System.Action onLeave, System.Action onDone)
        {
            if (_state != State.Idle || _route != null || route == null || route.Count == 0 || _room == null)
                return false;
            _route = new List<Vector3>(route);
            _routeIndex = 0;
            _routeYaw = yaw;
            _routeStay = stay;
            _routePhase = 0;
            _onArrive = onArrive;
            _onLeave = onLeave;
            _onDone = onDone;
            if (_at == Seat)
            {
                if (!TryStandUp())
                {
                    _route = null;
                    return false;
                }
                return true;   // 일어선 뒤(Idle) 첫 길
            }
            _next = Time.unscaledTime;
            return true;
        }

        private void ErrandStep(float now)
        {
            _justStood = false;
            if (_routePhase == 1)
            {
                // 머묾 끝: 목적지를 뺀 길을 거꾸로
                _routePhase = 2;
                _route.RemoveAt(_route.Count - 1);
                _route.Reverse();
                _routeIndex = 0;
                _onLeave?.Invoke();
            }
            if ((_routePhase == 0 || _routePhase == 2) && _routeIndex < _route.Count)
            {
                var p = _route[_routeIndex++];
                var d = p - transform.position;
                BeginWalk(p, -1, d.sqrMagnitude > 1e-4f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : transform.eulerAngles.y);
                return;
            }
            if (_routePhase == 0)
            {
                // 목적지 도착: 보는 쪽으로 돌고 머묾
                _routePhase = 1;
                _goalYaw = _routeYaw;
                _arriveYaw = true;
                _next = now + _routeStay;
                LastDecision = "심부름 도착";
                _onArrive?.Invoke();
                return;
            }
            // 자리로
            _routePhase = 3;
            if (_home == Seat)
                BeginWalk(_seatFront, Seat, _seatYaw);
            else if (_home >= 0 && (_room.Owner[_home] == null || _room.Owner[_home] == this))
                BeginWalk(_room.Points[_home], _home, _room.Yaws[_home]);
            else
                FinishErrand();
        }

        private void FinishErrand()
        {
            var done = _onDone;
            _route = null;
            _onArrive = _onLeave = _onDone = null;
            _next = Time.unscaledTime + WanderRules.IdleSeconds(_mood, _rng.NextDouble());
            done?.Invoke();
        }

        private void BeginWalk(Vector3 point, int target, float yaw)
        {
            Release(_at);
            _at = -1;
            _target = target;
            if (target >= 0)
                _room.Owner[target] = this;
            _goal = point;
            _goalYaw = yaw;
            _state = State.Turn;
        }

        // 11-16 ④ 앉는 자리에서 시작한 주민 (휴게실 · 회전 링 등): 일어나 → 걷고 → 돌아와 앉음
        private ResidentFigure _figure;
        private Vector3 _seatPos;
        private float _seatYaw;
        private Vector3 _seatFront;     // 일어서서 서는 곳 (의자 앞 바닥)

        public void Init(WanderRoom room, int home, float mood, Transform watcher, float scale, int id)
        {
            _room = room;
            _home = home;
            _at = home;
            room.Owner[home] = this;
            Setup(mood, watcher, scale, id);
        }

        private bool _justStood;

        private float FlatDistance(Vector3 p)
        {
            var d = p - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        /// <summary>앉은 주민: 의자 앞 바닥이 비어 있으면 가끔 일어나 같은 방 안을 걷다가 돌아와 다시 앉음.</summary>
        public void InitSeated(WanderRoom room, float mood, Transform watcher, float scale, int id)
        {
            _room = room;
            _home = Seat;
            _at = Seat;
            _seatPos = transform.position;
            _seatYaw = transform.eulerAngles.y;
            Setup(mood, watcher, scale, id);
        }

        private void Setup(float mood, Transform watcher, float scale, int id)
        {
            _mood = mood;
            _watcher = watcher;
            _scale = scale;
            _motion = GetComponent<ResidentMotion>();
            _figure = GetComponent<ResidentFigure>();
            _self = GetComponent<Collider>();
            _rng = new System.Random(id * 104729 + 3);
            _next = Time.unscaledTime + WanderRules.IdleSeconds(mood, _rng.NextDouble()) * 0.5f; // 처음엔 조금 일찍
        }

        private void OnDestroy()
        {
            Release(_at);
            Release(_target);
        }

        private void Release(int index)
        {
            if (_room != null && index >= 0 && index < _room.Owner.Count && _room.Owner[index] == this)
                _room.Owner[index] = null;
        }

        private void Update()
        {
            if (_room == null)
                return;
            float now = Time.unscaledTime;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            switch (_state)
            {
                case State.Idle:
                    if (_motion != null)
                        _motion.Walk(0f, dt);
                    if (now >= _next)
                    {
                        if (_route != null)
                            ErrandStep(now);
                        else
                            Decide(now);
                    }
                    break;
                case State.Turn:
                    if (Busy())
                    {
                        // 기다리는 동안 걷기 동작을 멈춤 — 안 멈추면 ResidentMotion이 반응(손 흔들기)을 끝내지 못해 Busy가 계속돼 영영 서 있었음
                        // (2026-10-20 행정 관제소: 일어나 돌던 민원인이 플레이어를 보고 손을 흔들다 멈춤)
                        _motion?.Walk(0f, dt);
                        break;
                    }
                    if (TurnToward(_goal - transform.position, dt) < 25f)
                        _state = State.Walk;
                    if (_motion != null)
                        _motion.Walk(0.35f, dt); // 돌면서 제자리걸음
                    break;
                case State.Walk:
                    UpdateWalk(now, dt);
                    break;
                case State.StandUp:
                {
                    // 의자에서 앞 바닥으로 밀려나오며 일어섬
                    float b = _motion.StandBlend;
                    transform.position = Vector3.Lerp(_seatPos, _seatFront, Mathf.SmoothStep(0f, 1f, b));
                    if (b >= 1f)
                    {
                        _figure?.SetColliderStanding(true);
                        _at = -1;
                        _state = State.Idle;
                        _next = now + 0.4f; // 일어나면 곧 걸음
                        _justStood = true;
                    }
                    break;
                }
                case State.SeatTurn:
                    _motion?.Walk(0f, dt);
                    if (TurnToward(Quaternion.Euler(0f, _seatYaw, 0f) * Vector3.forward, dt) < 2f)
                    {
                        _motion.StandUp(false);
                        _figure?.SetColliderStanding(false);
                        _state = State.SitDown;
                    }
                    break;
                case State.SitDown:
                {
                    // 뒤로 물러나 앉음
                    float b = _motion.StandBlend;
                    transform.position = Vector3.Lerp(_seatPos, _seatFront, Mathf.SmoothStep(0f, 1f, b));
                    if (b <= 0f)
                    {
                        transform.position = _seatPos;
                        _at = Seat;
                        _excursions = 0;
                        _state = State.Idle;
                        _next = now + WanderRules.IdleSeconds(_mood, _rng.NextDouble());
                        if (_route != null)
                            FinishErrand();   // 12-5 심부름을 마치고 다시 앉음
                    }
                    break;
                }
            }
        }

        // 일어설 곳을 찾는 방향 (의자 기준 도 — 앞 먼저, 다음 옆 · 비스듬히 뒤 · 뒤) · 거리
        private static readonly float[] StandAngles = { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 150f, -150f, 180f };
        private static readonly float[] StandDistances = { 0.5f, 0.7f, 0.9f };

        /// <summary>
        /// 앉아 있다가 일어설 수 있으면 일어서기 시작. 의자 주변(앞 먼저, 옆 · 뒤)에서 몸이 들어갈 빈 바닥을 찾는다
        /// — 회전 링 벤치는 창을 보고 있어 앞 0.6m 안이 유리라 앞으로는 일어설 수 없었음 (뒤 통로로 일어섬).
        /// </summary>
        private bool TryStandUp()
        {
            if (_motion == null || !_motion.CanStand)
                return false;
            foreach (float d in StandDistances)
            {
                foreach (float angle in StandAngles)
                {
                    var dir = Quaternion.Euler(0f, _seatYaw + angle, 0f) * Vector3.forward;
                    var p = _seatPos + dir * (d * _scale);
                    if (!Physics.Raycast(p + Vector3.up * 0.8f, Vector3.down, out var hit, 1.6f, ~0, QueryTriggerInteraction.Ignore))
                        continue;
                    p.y = hit.point.y;
                    if (Mathf.Abs(p.y - _seatPos.y) > 0.25f || !FloorFree(p, p.y))
                        continue;
                    _seatFront = p;
                    _motion.StandUp(true);
                    _state = State.StandUp;
                    LastDecision = $"일어남 ({angle:0}° · {d:0.0}m)";
                    return true;
                }
            }
            LastDecision = "의자 주변이 막혀 못 일어남";
            return false;
        }

        /// <summary>반응(손 흔들기 등) 중이거나 플레이어가 앞을 막음.</summary>
        private bool Busy()
        {
            if (_motion != null && _motion.Reacting)
                return true;
            if (_watcher == null)
                return false;
            var to = _watcher.position - transform.position;
            to.y = 0f;
            if (to.magnitude > BlockDistance * _scale + 0.4f)
                return false;
            var ahead = _goal - transform.position;
            ahead.y = 0f;
            return ahead.sqrMagnitude < 1e-4f || Vector3.Dot(to.normalized, ahead.normalized) > 0.2f;
        }

        private void Decide(float now)
        {
            _next = now + WanderRules.IdleSeconds(_mood, _rng.NextDouble());
            if (_at == Seat)
            {
                // 앉아 있음: 나들이 갈 기분이면 일어남 (걷기는 일어선 뒤 다음 결정에서). 12-5 Hold면 자리에서 기다림
                if (!Hold && WanderRules.ShouldWander(_mood, _excursions, _rng.NextDouble()))
                    TryStandUp();
                else
                    LastDecision = "앉아 쉼";
                return;
            }
            bool away = _at != _home;
            int target;
            Vector3 point;
            bool justStood = _justStood;
            _justStood = false;
            // 일어나자마자 다시 앉으면 어색하므로 일어선 직후엔 꼭 한 번 걸음 (12-5 Hold면 나들이 없이 자리로)
            if (!Hold && (justStood || WanderRules.ShouldWander(_mood, _excursions, _rng.NextDouble())))
            {
                if (!PickTarget(out target, out point))
                {
                    LastDecision = "갈 곳 없음 (빈 자리 · 바닥이 모두 막힘)";
                    return;
                }
                _excursions++;
            }
            else if (away && _home == Seat)
            {
                // 의자 앞으로 돌아가 앉음 (이미 의자 앞이면 바로 돌아서 앉음)
                if (FlatDistance(_seatFront) < 0.1f)
                {
                    LastDecision = "앉음";
                    _state = State.SeatTurn;
                    return;
                }
                if (!PathClear(_seatFront))
                {
                    LastDecision = "의자로 가는 길이 막힘";
                    return;
                }
                target = Seat;
                point = _seatFront;
            }
            else if (away)
            {
                // 원래 자리로 (누가 차지했거나 길이 막혔으면 다음에)
                if ((_room.Owner[_home] != null && _room.Owner[_home] != this) || !PathClear(_room.Points[_home]))
                {
                    LastDecision = "돌아갈 자리가 막힘";
                    return;
                }
                target = _home;
                point = _room.Points[_home];
                _excursions = 0;
            }
            else
            {
                LastDecision = "머묾";
                return;
            }
            LastDecision = target == Seat ? "의자로 돌아감" : target >= 0 ? $"자리 {target}로" : "바닥으로";
            Release(_at);
            _at = -1;
            _target = target;
            if (target >= 0)
                _room.Owner[target] = this;
            _goal = point;
            _goalYaw = target == Seat ? _seatYaw : target >= 0 ? _room.Yaws[target] : transform.eulerAngles.y + (float)(_rng.NextDouble() * 120.0 - 60.0);
            _state = State.Turn;
        }

        /// <summary>
        /// 비어 있고 가는 길이 트인 다른 자리(가까운 곳을 더 자주) → 없으면 지금 자리 주변 바닥.
        /// 코어처럼 가운데에 큰 기둥이 있으면 건너편 자리는 길이 막혀서, 막히지 않은 자리를 모두 살펴 고른다.
        /// </summary>
        private bool PickTarget(out int target, out Vector3 point)
        {
            var free = new List<int>();
            for (int i = 0; i < _room.Points.Count; i++)
            {
                float d2 = (_room.Points[i] - transform.position).sqrMagnitude;
                if (i != _at && _room.Owner[i] == null && d2 > 0.25f && d2 < 36f && PathClear(_room.Points[i]))
                    free.Add(i);
            }
            if (free.Count > 0)
            {
                // 가까운 두 곳 중 하나 (멀리 가로지르기보다 이웃 자리로 마실)
                free.Sort((a, b) => (_room.Points[a] - transform.position).sqrMagnitude.CompareTo((_room.Points[b] - transform.position).sqrMagnitude));
                target = free[_rng.Next(Mathf.Min(2, free.Count))];
                point = _room.Points[target];
                return true;
            }
            target = -1;
            var here = transform.position;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float a = (float)(_rng.NextDouble() * Mathf.PI * 2.0);
                float d = 0.8f + (float)_rng.NextDouble() * 0.6f;
                var p = here + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d * _scale;
                if (FloorFree(p, here.y) && PathClear(p))
                {
                    point = p;
                    return true;
                }
            }
            point = default;
            return false;
        }

        /// <summary>확인용: 마지막으로 정한 것.</summary>
        public string LastDecision { get; private set; } = "";

        /// <summary>같은 높이에 바닥이 있고, 그 자리에 몸이 들어갈 만큼 비었는지.</summary>
        private bool FloorFree(Vector3 p, float floorY)
        {
            if (!Physics.Raycast(p + Vector3.up * 1f, Vector3.down, out var hit, 1.4f, ~0, QueryTriggerInteraction.Ignore))
                return false;
            if (Mathf.Abs(hit.point.y - floorY) > 0.06f || hit.collider == _self)
                return false;
            float r = Radius * _scale;
            var a = p + Vector3.up * (r + 0.05f);
            var b = p + Vector3.up * (Height * _scale - r);
            foreach (var c in Physics.OverlapCapsule(a, b, r, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c != _self)
                    return false;
            }
            return true;
        }

        /// <summary>지금 자리 → 목표까지 몸(캡슐)으로 쓸어 봐서 걸리는 게 없는지 (자기 자신 · 바닥 제외).</summary>
        private bool PathClear(Vector3 point)
        {
            var from = transform.position;
            var dir = point - from;
            dir.y = 0f;
            float dist = dir.magnitude;
            if (dist < 0.05f)
                return false;
            float r = Radius * _scale;
            var a = from + Vector3.up * (r + 0.08f);
            var b = from + Vector3.up * (Height * _scale - r);
            foreach (var hit in Physics.CapsuleCastAll(a, b, r * 0.9f, dir / dist, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                // 처음부터 살짝 닿아 있는 것(거리 0 — 옆 벽 · 의자 등)은 빼고, 가는 길에 새로 걸리는 것만
                if (hit.collider != _self && hit.distance > 0f)
                    return false;
            }
            return true;
        }

        private void UpdateWalk(float now, float dt)
        {
            if (Busy())
            {
                if (_motion != null)
                    _motion.Walk(0f, dt);
                return;
            }
            var to = _goal - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            float speed = WanderRules.WalkSpeed(_mood) * _scale;
            if (dist <= speed * dt + 0.01f)
            {
                transform.position = new Vector3(_goal.x, transform.position.y, _goal.z);
                if (_target == Seat)
                {
                    // 의자 앞: 의자 방향으로 돌아선 뒤 앉음
                    _target = -1;
                    _state = State.SeatTurn;
                    return;
                }
                _at = _target;
                _target = -1;
                _state = State.Idle;
                _next = now + WanderRules.IdleSeconds(_mood, _rng.NextDouble());
                _arriveYaw = true;
                if (_route != null)
                {
                    // 12-5 심부름: 다음 길로 바로 (서 있는 집 자리로 돌아왔으면 끝)
                    _next = now;
                    if (_routePhase == 3)
                        FinishErrand();
                }
                return;
            }
            if (_route == null && now >= _propCheck)
            {
                _propCheck = now + 0.25f;
                if (PropAhead(to / dist, dist))
                {
                    // 출발한 뒤 가는 길에 놓인 물건(보급 상자 등) — 뚫고 지나가지 않고 그 자리에 섰다가 다음에 다시 정함
                    Release(_target);
                    _target = -1;
                    _at = -1;
                    _state = State.Idle;
                    _next = now + WanderRules.IdleSeconds(_mood, _rng.NextDouble()) * 0.5f;
                    LastDecision = "가는 길에 물건이 놓여 멈춤";
                    if (_motion != null)
                        _motion.Walk(0f, dt);
                    return;
                }
            }
            TurnToward(to, dt);
            transform.position += to / dist * (speed * dt);
            if (_motion != null)
                _motion.Walk(speed / _scale, dt);
        }

        private float _propCheck;

        /// <summary>
        /// 걷는 중 앞(최대 0.8m)에 상호작용 물건(보급 상자 · 부탁 물건 등)이 새로 생겼는지 — 길은 출발할 때 한 번만 보므로
        /// 걷는 사이 내부에 놓인 상자를 뚫고 지나갔음. 주민끼리 · 처음부터 닿아 있던 것(거리 0)은 보지 않음.
        /// </summary>
        private bool PropAhead(Vector3 dir, float dist)
        {
            float r = Radius * _scale;
            var from = transform.position;
            var a = from + Vector3.up * (r + 0.08f);
            var b = from + Vector3.up * (Height * _scale - r);
            foreach (var hit in Physics.CapsuleCastAll(a, b, r * 0.9f, dir, Mathf.Min(dist, 0.8f), ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != _self && hit.distance > 0f && hit.collider.GetComponentInParent<InteriorInteractable>() != null
                    && hit.collider.GetComponentInParent<ResidentFigure>() == null)
                    return true;
            }
            return false;
        }

        private bool _arriveYaw;

        private void LateUpdate()
        {
            // 도착하면 그 자리가 보는 쪽으로 천천히 돎
            if (!_arriveYaw || _state != State.Idle)
                return;
            float yaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, _goalYaw, TurnSpeed * 0.5f * Time.unscaledDeltaTime);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, _goalYaw)) < 0.5f)
                _arriveYaw = false;
        }

        /// <summary>가는 쪽으로 돌고 남은 각도를 돌려줌.</summary>
        private float TurnToward(Vector3 dir, float dt)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f)
                return 0f;
            float want = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float yaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, want, TurnSpeed * dt);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return Mathf.Abs(Mathf.DeltaAngle(yaw, want));
        }
    }
}
