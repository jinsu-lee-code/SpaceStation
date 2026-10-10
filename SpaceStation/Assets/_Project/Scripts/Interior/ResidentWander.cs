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
    /// 다리 · 팔 흔들기는 <see cref="ResidentMotion.Walk"/>.
    /// </summary>
    public sealed class ResidentWander : MonoBehaviour
    {
        private enum State { Idle, Turn, Walk }

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

        public void Init(WanderRoom room, int home, float mood, Transform watcher, float scale, int id)
        {
            _room = room;
            _home = home;
            _at = home;
            room.Owner[home] = this;
            _mood = mood;
            _watcher = watcher;
            _scale = scale;
            _motion = GetComponent<ResidentMotion>();
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
                        Decide(now);
                    break;
                case State.Turn:
                    if (Busy())
                        break;
                    if (TurnToward(_goal - transform.position, dt) < 25f)
                        _state = State.Walk;
                    if (_motion != null)
                        _motion.Walk(0.35f, dt); // 돌면서 제자리걸음
                    break;
                case State.Walk:
                    UpdateWalk(now, dt);
                    break;
            }
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
            bool away = _at != _home;
            int target;
            Vector3 point;
            if (WanderRules.ShouldWander(_mood, _excursions, _rng.NextDouble()))
            {
                if (!PickTarget(out target, out point))
                {
                    LastDecision = "갈 곳 없음 (빈 자리 · 바닥이 모두 막힘)";
                    return;
                }
                _excursions++;
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
            LastDecision = target >= 0 ? $"자리 {target}로" : "바닥으로";
            Release(_at);
            _at = -1;
            _target = target;
            if (target >= 0)
                _room.Owner[target] = this;
            _goal = point;
            _goalYaw = target >= 0 ? _room.Yaws[target] : transform.eulerAngles.y + (float)(_rng.NextDouble() * 120.0 - 60.0);
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
                _at = _target;
                _target = -1;
                _state = State.Idle;
                _next = now + WanderRules.IdleSeconds(_mood, _rng.NextDouble());
                _arriveYaw = true;
                return;
            }
            TurnToward(to, dt);
            transform.position += to / dist * (speed * dt);
            if (_motion != null)
                _motion.Walk(speed / _scale, dt);
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
