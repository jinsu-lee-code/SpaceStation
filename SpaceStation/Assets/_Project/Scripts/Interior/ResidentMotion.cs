using System;
using System.Collections.Generic;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-16 동물 주민 생명감 (코드로 본을 돌리는 절차적 동작 — 8종이 한 리그를 공유하므로 한 번에 적용).
    /// 매 프레임 자세(<see cref="ResidentPoser"/>)를 잡은 본 회전에서 시작해 덧붙인다:
    /// - 평소: 숨쉬기 · 몸 흔들림 · (기분 좋으면) 들썩임 · 눈 깜빡임 · 귀 씰룩 · 꼬리 흔들기 · 두리번, 가끔 기지개 · 크게 둘러보기 · 폴짝
    /// - 기분(<see cref="ResidentMood"/>): 나쁨 = 고개 숙임 · 귀 처짐 · 꼬리 내림 · 느림 · 눈 반쯤 감김 / 좋음 = 귀 쫑긋 · 꼬리 크게 · 가볍게 들썩
    /// - 플레이어가 다가오면 바라보고 기분 따라 손 흔들기 · 폴짝 · 팔짱 끼고 고개 돌림 (같은 주민은 20초에 한 번)
    /// - 일하는 자리는 손을 조금씩 움직임 (작업 중), 앉은 주민은 폴짝 · 들썩임 없음
    /// 방향은 인물 기준(+x = 인물 오른쪽, +y 위, +z 앞). 게임 시간과 무관(unscaled).
    /// </summary>
    public sealed class ResidentMotion : MonoBehaviour
    {
        private enum Act { None, Stretch, LookAround, Hop, Wave, HopWave, TurnAway }

        private const float ReactCooldown = 20f;

        private Transform _model;
        private Transform _hips, _chest, _head;
        private Transform _lUpper, _lLower, _rUpper, _rLower;
        private Transform _lEar, _rEar, _tail;
        private readonly Transform[] _eyes = new Transform[2];
        private readonly int[] _eyeAxis = new int[2];
        private readonly Vector3[] _eyeScale = new Vector3[2];
        private float _lEarSide, _rEarSide;
        private readonly List<(Transform Bone, Quaternion Local)> _rest = new List<(Transform, Quaternion)>();
        private Vector3 _modelRest;
        private Vector3 _chestScale;
        private ResidentPose _pose;
        private float _scale = 1f;

        private int _id;
        private float _mood;
        private MoodStyle _style = ResidentMood.Neutral;
        private Transform _watcher;
        private float _lookRange = 3f;
        private System.Random _rng;

        private float _phase;
        private float _wag;
        private float _yaw, _pitch;
        private float _blinkAt, _blinkStart = -10f;
        private float _twitchAt, _twitchStart = -10f;
        private int _twitchEar;
        private float _idleAt;
        private Act _act;
        private float _actStart, _actLength;
        private float _turnSide = 1f;
        private bool _near;
        private int _meet;
        private float _reactReady;

        /// <summary>지금 하는 동작 (확인용).</summary>
        public string Current => _act.ToString();

        /// <summary>플레이어에게 반응하는 중 (걷기를 잠깐 멈춤).</summary>
        public bool Reacting => _act == Act.Wave || _act == Act.HopWave || _act == Act.TurnAway;

        private Transform _lThigh, _rThigh, _lShin, _rShin;
        private float _walk;        // 걷기 섞는 정도 0~1
        private float _walkPhase;

        /// <summary>
        /// 11-16 ③ 걷기 (<see cref="ResidentWander"/>가 매 프레임 부름): speed = 초당 m(모델 원래 크기 기준, 0이면 멈춤).
        /// 다리를 앞뒤로 · 무릎 굽힘 · 팔은 반대로 · 몸이 통통 튐. 보폭 약 0.16m.
        /// </summary>
        public void Walk(float speed, float dt)
        {
            _walk = Mathf.MoveTowards(_walk, speed > 0.01f ? 1f : 0f, dt * 5f);
            if (speed > 0.01f)
                _walkPhase += dt * speed / 0.16f * Mathf.PI;
        }

        private float _hold = -1f;

        /// <summary>
        /// 확인용: 동작(Stretch · LookAround · Hop · Wave · HopWave · TurnAway)을 진행 progress(0~1)에 멈춰 보여줌. act = null이면 풀기.
        /// 동작이 1~3초로 짧아 캡처 사이에 끝나버려서 만듦.
        /// </summary>
        public void Hold(string act, float progress)
        {
            if (string.IsNullOrEmpty(act) || !Enum.TryParse(act, out Act a))
            {
                _hold = -1f;
                _act = Act.None;
                return;
            }
            _act = a;
            _actStart = Time.unscaledTime;
            _actLength = 1f;
            _hold = Mathf.Clamp01(progress);
            _turnSide = 1f;
        }

        /// <summary>자세를 잡은 직후에 부른다 — 그때의 본 회전이 기준.</summary>
        public void Init(Func<HumanBodyBones, Transform> bones, Transform model, ResidentPose pose, float scale, int id)
        {
            _model = model;
            _pose = pose;
            _scale = scale;
            _id = id;
            _rng = new System.Random(id * 7919 + 17);
            _hips = bones(HumanBodyBones.Hips);
            _chest = bones(HumanBodyBones.Chest);
            _head = bones(HumanBodyBones.Head);
            _lUpper = bones(HumanBodyBones.LeftUpperArm);
            _lLower = bones(HumanBodyBones.LeftLowerArm);
            _rUpper = bones(HumanBodyBones.RightUpperArm);
            _rLower = bones(HumanBodyBones.RightLowerArm);
            _lThigh = bones(HumanBodyBones.LeftUpperLeg);
            _rThigh = bones(HumanBodyBones.RightUpperLeg);
            _lShin = bones(HumanBodyBones.LeftLowerLeg);
            _rShin = bones(HumanBodyBones.RightLowerLeg);
            var extra = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                extra[t.name] = t;
            extra.TryGetValue("LeftEar", out _lEar);
            extra.TryGetValue("RightEar", out _rEar);
            extra.TryGetValue("Tail", out _tail);
            extra.TryGetValue("LeftEye", out _eyes[0]);
            extra.TryGetValue("RightEye", out _eyes[1]);

            foreach (var b in new[] { _hips, _chest, _head, _lUpper, _lLower, _rUpper, _rLower, _lEar, _rEar, _tail, _lThigh, _rThigh, _lShin, _rShin,
                         bones(HumanBodyBones.Spine), bones(HumanBodyBones.Neck), bones(HumanBodyBones.LeftHand), bones(HumanBodyBones.RightHand) })
            {
                if (b != null)
                    _rest.Add((b, b.localRotation));
            }
            _modelRest = model.localPosition;
            if (_chest != null)
                _chestScale = _chest.localScale;
            // 귀가 인물의 어느 쪽에 있는지 (처짐 방향)
            _lEarSide = _lEar != null ? Mathf.Sign(transform.InverseTransformPoint(_lEar.position).x) : -1f;
            _rEarSide = _rEar != null ? Mathf.Sign(transform.InverseTransformPoint(_rEar.position).x) : 1f;
            // 눈: 인물 위쪽과 가장 나란한 본 축을 납작하게 해서 깜빡임
            for (int i = 0; i < 2; i++)
            {
                var e = _eyes[i];
                if (e == null)
                    continue;
                _eyeScale[i] = e.localScale;
                float best = -1f;
                for (int a = 0; a < 3; a++)
                {
                    var axis = a == 0 ? e.right : a == 1 ? e.up : e.forward;
                    float d = Mathf.Abs(Vector3.Dot(axis, transform.up));
                    if (d > best)
                    {
                        best = d;
                        _eyeAxis[i] = a;
                    }
                }
            }
            float now = Time.unscaledTime;
            _phase = (float)_rng.NextDouble() * 10f;
            _blinkAt = now + 0.5f + (float)_rng.NextDouble() * 3f;
            _twitchAt = now + 2f + (float)_rng.NextDouble() * 4f;
            _idleAt = now + 3f + (float)_rng.NextDouble() * 6f;
        }

        public void Configure(float mood, Transform watcher, float lookRange)
        {
            _mood = Mathf.Clamp(mood, -1f, 1f);
            _style = ResidentMood.Style(_mood);
            _watcher = watcher;
            _lookRange = lookRange;
        }

        private void LateUpdate()
        {
            if (_rng == null)
                return;
            float now = Time.unscaledTime;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _phase += dt * _style.Speed;
            _wag += dt * _style.TailHz * Mathf.PI * 2f;

            foreach (var (bone, local) in _rest)
                bone.localRotation = local;
            var root = transform;
            bool standing = _pose == ResidentPose.Stand;

            if (_hold < 0f)
            {
                UpdateReaction(now);
                if (_walk < 0.05f)
                    UpdateIdle(now, standing);   // 걷는 중에는 기지개 · 폴짝 안 함
            }
            float k = _hold >= 0f ? _hold : _act != Act.None ? Mathf.Clamp01((now - _actStart) / _actLength) : 0f;
            float env = Envelope(k);

            // 숨쉬기 · 몸 흔들림 · 들썩임 · 폴짝
            if (_chest != null)
                _chest.localScale = _chestScale * (1f + Mathf.Sin(_phase * 1.6f) * 0.015f);
            float lift = 0f;
            if (standing)
            {
                lift = _style.Bounce * Mathf.Abs(Mathf.Sin(_phase * 3.2f));
                if (_act == Act.Hop || _act == Act.HopWave)
                {
                    // 폴짝 두 번 (앞 60% 동안)
                    float hk = Mathf.Clamp01(k / 0.6f) * 2f;
                    lift += hk < 2f ? 0.07f * Mathf.Sin(Mathf.Repeat(hk, 1f) * Mathf.PI) : 0f;
                }
                Rotate(_hips, root.forward, Mathf.Sin(_phase * 0.7f) * 1.6f);
                if (_walk > 0f)
                {
                    // 걷기: 걸음마다 몸이 통통 + 좌우로 살짝 기우뚱
                    float s = Mathf.Sin(_walkPhase);
                    lift += _walk * 0.022f * Mathf.Abs(Mathf.Cos(_walkPhase));
                    Rotate(_hips, root.forward, _walk * s * 4f);
                }
            }
            if (_model != null)
                _model.localPosition = _modelRest + Vector3.up * (lift * _scale);
            if (_act == Act.Stretch)
                Rotate(_chest, root.right, -8f * env);   // 가슴을 뒤로 젖힘

            UpdateHead(root, now, k, env);
            UpdateArms(root, k, env);
            UpdateLegs(root);
            UpdateEars(root, now);
            UpdateTail(root);
            UpdateEyes(now);
        }

        // ---------------- 반응 · 작은 동작 ----------------

        private void UpdateReaction(float now)
        {
            if (_watcher == null || _head == null)
                return;
            var to = _watcher.position - _head.position;
            float dist = to.magnitude;
            if (!_near && dist < _lookRange && Mathf.Abs(YawTo(to)) < 100f)
            {
                _near = true;
                if (now >= _reactReady)
                {
                    _reactReady = now + ReactCooldown;
                    var r = ResidentMood.Reaction(_mood, _id, _meet++);
                    if (r == ResidentReaction.HopWave)
                        Start(_pose == ResidentPose.Stand ? Act.HopWave : Act.Wave, now, 1.8f);
                    else if (r == ResidentReaction.Wave)
                        Start(Act.Wave, now, 1.6f);
                    else if (r == ResidentReaction.TurnAway)
                    {
                        _turnSide = YawTo(to) >= 0f ? -1f : 1f; // 플레이어 반대쪽으로
                        Start(Act.TurnAway, now, 2.6f);
                    }
                }
            }
            else if (_near && dist > _lookRange + 0.8f)
                _near = false;
        }

        private void UpdateIdle(float now, bool standing)
        {
            if (_act != Act.None && now - _actStart >= _actLength)
                _act = Act.None;
            if (_act != Act.None || now < _idleAt)
                return;
            _idleAt = now + _style.IdleGap * (0.7f + (float)_rng.NextDouble() * 0.6f);
            if (_near)
                return; // 플레이어를 보는 중에는 딴짓 안 함
            double r = _rng.NextDouble();
            if (standing && _mood > 0.2f && r < 0.3)
                Start(Act.Hop, now, 0.9f);
            else if (r < 0.6 && _pose != ResidentPose.Work)
                Start(Act.Stretch, now, 1.5f);
            else
                Start(Act.LookAround, now, 2.6f);
        }

        private void Start(Act act, float now, float length)
        {
            _act = act;
            _actStart = now;
            _actLength = length / Mathf.Max(0.5f, _style.Speed);
        }

        /// <summary>0~1 진행에서 들어갔다 나오는 세기 (앞 20% 올라가고 뒤 25% 내려감).</summary>
        private static float Envelope(float k)
        {
            if (k <= 0f || k >= 1f)
                return 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Min(k / 0.2f, (1f - k) / 0.25f));
        }

        // ---------------- 머리 ----------------

        private void UpdateHead(Transform root, float now, float k, float env)
        {
            if (_head == null)
                return;
            float t = _phase;
            float yaw = Mathf.Sin(t * 0.23f) * 16f + Mathf.Sin(t * 0.61f) * 5f;
            float pitch = Mathf.Sin(t * 0.17f) * 3f + _style.HeadDown;
            float roll = Mathf.Sin(t * 0.31f) * 4f;
            if (_act == Act.LookAround)
                yaw = Mathf.Sin(k * Mathf.PI * 2f) * 45f;
            if (_watcher != null && _near)
            {
                var to = _watcher.position - _head.position;
                var local = root.InverseTransformDirection(to);
                float y = YawTo(to);
                if (Mathf.Abs(y) < 100f)
                {
                    yaw = Mathf.Clamp(y, -60f, 60f);
                    pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -20f, 20f) + _style.HeadDown * 0.5f;
                    roll = 8f * Mathf.Sin(t * 0.5f); // 갸웃
                }
            }
            if (_act == Act.TurnAway)
            {
                yaw = Mathf.Lerp(yaw, 55f * _turnSide, env);
                pitch = Mathf.Lerp(pitch, 10f, env);
                roll = Mathf.Lerp(roll, 0f, env);
            }
            if (_act == Act.Stretch)
                pitch = Mathf.Lerp(pitch, -18f, env);
            float speed = (_act == Act.TurnAway ? 200f : 90f) * _style.Speed;
            _yaw = Mathf.MoveTowardsAngle(_yaw, yaw, speed * Time.unscaledDeltaTime);
            _pitch = Mathf.MoveTowards(_pitch, pitch, speed * 0.6f * Time.unscaledDeltaTime);
            var q = root.rotation * Quaternion.Euler(_pitch, _yaw, roll) * Quaternion.Inverse(root.rotation);
            _head.rotation = q * _head.rotation;
        }

        private float YawTo(Vector3 worldDir)
        {
            var local = transform.InverseTransformDirection(worldDir);
            return Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        }

        // ---------------- 팔 ----------------

        private void UpdateArms(Transform root, float k, float env)
        {
            switch (_act)
            {
                case Act.Wave:
                case Act.HopWave:
                    // 오른팔을 들고 아래팔을 좌우로
                    Aim(root, _rUpper, _rLower, new Vector3(0.55f, 0.85f, 0.2f), env);
                    Aim(root, _rLower, ChildOf(_rLower), new Vector3(0.2f + 0.5f * Mathf.Sin(k * Mathf.PI * 7f), 1f, 0.15f), env);
                    break;
                case Act.TurnAway:
                    // 팔짱
                    Aim(root, _lUpper, _lLower, new Vector3(-0.3f, -0.5f, 0.75f), env);
                    Aim(root, _rUpper, _rLower, new Vector3(0.3f, -0.5f, 0.75f), env);
                    Aim(root, _lLower, ChildOf(_lLower), new Vector3(0.9f, 0.12f, 0.35f), env);
                    Aim(root, _rLower, ChildOf(_rLower), new Vector3(-0.9f, 0.18f, 0.3f), env);
                    break;
                case Act.Stretch:
                    Aim(root, _lUpper, _lLower, new Vector3(-0.3f, 1f, 0.05f), env);
                    Aim(root, _rUpper, _rLower, new Vector3(0.3f, 1f, 0.05f), env);
                    Aim(root, _lLower, ChildOf(_lLower), new Vector3(-0.1f, 1f, 0f), env);
                    Aim(root, _rLower, ChildOf(_rLower), new Vector3(0.1f, 1f, 0f), env);
                    break;
                default:
                    if (_pose == ResidentPose.Work)
                    {
                        // 작업 중: 아래팔을 조금씩 (화면 · 장비를 만지는 듯)
                        Rotate(_lLower, root.right, Mathf.Sin(_phase * 5.1f) * 5f);
                        Rotate(_rLower, root.right, Mathf.Sin(_phase * 4.3f + 1.3f) * 5f);
                    }
                    else
                    {
                        // 팔이 숨에 맞춰 살짝 흔들림
                        Rotate(_lUpper, root.forward, Mathf.Sin(_phase * 1.6f) * 1.5f);
                        Rotate(_rUpper, root.forward, -Mathf.Sin(_phase * 1.6f) * 1.5f);
                    }
                    break;
            }
        }

        /// <summary>걷기: 허벅지 앞뒤(왼 · 오른 반대), 뒤로 간 다리는 무릎을 굽혀 발을 듦, 팔은 다리와 반대로.</summary>
        private void UpdateLegs(Transform root)
        {
            if (_walk <= 0f)
                return;
            float s = Mathf.Sin(_walkPhase);
            // 인물 오른쪽 축으로 −면 다리가 앞으로
            Rotate(_lThigh, root.right, -26f * s * _walk);
            Rotate(_rThigh, root.right, 26f * s * _walk);
            Rotate(_lShin, root.right, 30f * Mathf.Max(0f, -s) * _walk);
            Rotate(_rShin, root.right, 30f * Mathf.Max(0f, s) * _walk);
            if (!Reacting && _act != Act.Stretch)
            {
                Rotate(_lUpper, root.right, 18f * s * _walk);
                Rotate(_rUpper, root.right, -18f * s * _walk);
            }
        }

        private static Transform ChildOf(Transform b) => b != null && b.childCount > 0 ? b.GetChild(0) : null;

        /// <summary>본이 자식 쪽으로 향하는 방향을 인물 기준 dir로 (w = 섞는 정도).</summary>
        private static void Aim(Transform root, Transform bone, Transform child, Vector3 dir, float w)
        {
            if (bone == null || child == null || w <= 0f)
                return;
            var now = child.position - bone.position;
            if (now.sqrMagnitude < 1e-8f)
                return;
            var want = root.TransformDirection(dir.normalized);
            var target = Quaternion.FromToRotation(now.normalized, want) * bone.rotation;
            bone.rotation = Quaternion.Slerp(bone.rotation, target, w);
        }

        private static void Rotate(Transform bone, Vector3 axis, float degrees)
        {
            if (bone != null)
                bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        // ---------------- 귀 · 꼬리 · 눈 ----------------

        private void UpdateEars(Transform root, float now)
        {
            if (now >= _twitchAt)
            {
                _twitchStart = now;
                _twitchEar = _rng.Next(2);
                _twitchAt = now + 2.5f + (float)_rng.NextDouble() * 4f;
            }
            float tk = (now - _twitchStart) / 0.22f;
            float twitch = tk >= 0f && tk < 1f ? Mathf.Sin(tk * Mathf.PI) * 14f : 0f;
            float droop = _style.EarDroop + (_act == Act.TurnAway ? 10f : 0f) + (_act == Act.HopWave ? -8f : 0f);
            // 인물 앞 축으로 돌려 귀 끝이 바깥 · 아래로 (왼쪽 귀(−x)는 +, 오른쪽 귀는 −)
            Rotate(_lEar, root.forward, -_lEarSide * (droop + (_twitchEar == 0 ? twitch : 0f)));
            Rotate(_rEar, root.forward, -_rEarSide * (droop + (_twitchEar == 1 ? twitch : 0f)));
            // 귀가 숨 · 걸음에 따라 살짝 흔들림
            float sway = Mathf.Sin(_phase * 2.1f) * 2f;
            Rotate(_lEar, root.right, sway);
            Rotate(_rEar, root.right, sway);
        }

        private void UpdateTail(Transform root)
        {
            if (_tail == null)
                return;
            float wag = _style.TailWag * (_act == Act.HopWave ? 1.6f : _act == Act.TurnAway ? 0.2f : 1f);
            // 꼬리 끝은 뒤(−z) → 인물 오른쪽 축으로 −면 아래로
            Rotate(_tail, root.right, -_style.TailDroop);
            Rotate(_tail, root.up, Mathf.Sin(_wag) * wag);
        }

        private void UpdateEyes(float now)
        {
            if (now >= _blinkAt)
            {
                _blinkStart = now;
                // 가끔 두 번 연달아
                _blinkAt = now + (_rng.NextDouble() < 0.2 ? 0.28f : 2.2f + (float)_rng.NextDouble() * 3.5f);
            }
            float bk = (now - _blinkStart) / 0.14f;
            float blink = bk >= 0f && bk < 1f ? Mathf.Sin(bk * Mathf.PI) : 0f;
            // 기분 나쁨 = 눈이 반쯤 감김
            float lid = Mathf.Lerp(1f, 0.62f, Mathf.Clamp01(-_mood));
            float open = Mathf.Lerp(lid, 0.1f, blink);
            for (int i = 0; i < 2; i++)
            {
                if (_eyes[i] == null)
                    continue;
                var s = _eyeScale[i];
                s[_eyeAxis[i]] *= open;
                _eyes[i].localScale = s;
            }
        }
    }
}
