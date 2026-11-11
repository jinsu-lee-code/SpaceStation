using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 미니게임 "벌 몰래 꿀 먹기"의 곰 (플레이어 = 곰 주민, 그루터기에 앉은 <see cref="ResidentFigure"/> 위에 붙임): 무릎에 꿀단지를 안고 —
    /// 먹기 = 오른 앞발로 꿀단지 속 → 입 (숟갈마다 한 번, 고개를 꿀단지로 숙였다 듦) · 시치미 = 두 앞발로 꿀단지를 안고 딴 데 봄 · 쏘임 = 두 앞발로 코를 감싸고 고개를 젖힘.
    /// 팔은 두 마디 IK (어깨 → 팔꿈치 → 앞발, 팔꿈치는 바깥 아래로). <see cref="ResidentMotion"/> (숨 · 귀 · 꼬리 · 깜빡임) 뒤에 팔 · 고개 · 가슴만 덮어씀.
    /// </summary>
    [DefaultExecutionOrder(200)] // ResidentMotion (기본 0) 뒤
    public sealed class InteriorHoneyBear : MonoBehaviour
    {
        /// <summary>먹는 자세 0 ~ 1.</summary>
        public float Eat { get; set; }
        /// <summary>숟갈 진행 0 ~ 1 (0 · 1 = 꿀단지 속, 0.5 = 입).</summary>
        public float Scoop { get; set; }
        /// <summary>시치미 0 ~ 1 (딴 데 봄).</summary>
        public float Innocent { get; set; }
        /// <summary>쏘임 0 ~ 1 (코를 감쌈).</summary>
        public float Ouch { get; set; }
        /// <summary>팔 · 고개를 덮어쓰는 정도 (끝나고 기뻐할 때 0 — 주민 손 흔들기가 보이게).</summary>
        public float Weight { get; set; } = 1f;

        private Transform _pot;
        private Vector3 _potMouth, _potCenter;   // 인물 기준
        private float _scale = 1f;
        private Transform _rUpper, _rLower, _rHand, _lUpper, _lLower, _lHand, _head, _chest, _lEye, _rEye;

        /// <summary>코 끝 (월드) — 꿀벌이 콕 쏘는 곳.</summary>
        public Vector3 Nose
        {
            get
            {
                var t = transform;
                if (_lEye != null && _rEye != null)
                    return (_lEye.position + _rEye.position) * 0.5f - t.up * (0.05f * _scale) + t.forward * (0.07f * _scale);
                return _head != null ? _head.position + t.forward * (0.15f * _scale) : t.position + t.up * _scale;
            }
        }

        /// <summary>입 (월드) — 앞발이 꿀을 가져가는 곳.</summary>
        public Vector3 Mouth => Nose - transform.up * (0.05f * _scale) - transform.forward * (0.02f * _scale);

        /// <summary>머리 위 (말풍선 자리).</summary>
        public Vector3 Top => (_head != null ? _head.position : transform.position) + transform.up * (0.42f * _scale);

        /// <summary>안긴 꿀단지 크기 (그루터기 위 그대로면 2등신 곰 무릎 (깊이 15 cm)보다 커서 배에 묻혔음).</summary>
        public const float HeldScale = 0.8f;

        /// <summary>자세를 잡은 뒤 (<see cref="ResidentFigure.Create"/>) 부름: 본을 찾고, 꿀단지를 무릎 (두 무릎 위 · 조금 앞)에 안김 (크기 <see cref="HeldScale"/>배).</summary>
        public void Init(Transform pot, float scale)
        {
            _scale = scale;
            var bones = ResidentPoser.ByName(transform);
            _rUpper = bones(HumanBodyBones.RightUpperArm);
            _rLower = bones(HumanBodyBones.RightLowerArm);
            _rHand = bones(HumanBodyBones.RightHand);
            _lUpper = bones(HumanBodyBones.LeftUpperArm);
            _lLower = bones(HumanBodyBones.LeftLowerArm);
            _lHand = bones(HumanBodyBones.LeftHand);
            _head = bones(HumanBodyBones.Head);
            _chest = bones(HumanBodyBones.Chest);
            foreach (var c in GetComponentsInChildren<Transform>(true))
            {
                if (c.name == "LeftEye") _lEye = c;
                else if (c.name == "RightEye") _rEye = c;
            }
            _pot = pot;
            if (pot == null)
                return;
            var lt = bones(HumanBodyBones.LeftUpperLeg);
            var ls = bones(HumanBodyBones.LeftLowerLeg);
            var rt = bones(HumanBodyBones.RightUpperLeg);
            var rs = bones(HumanBodyBones.RightLowerLeg);
            var t = transform;
            Vector3 lap = t.position + t.forward * (0.16f * scale) + t.up * 0.45f;
            if (lt != null && ls != null && rt != null && rs != null)
            {
                lap = (ls.position + rs.position) * 0.5f + t.forward * (0.07f * scale);
                lap.y = Mathf.Max((lt.position.y + ls.position.y) * 0.5f, (rt.position.y + rs.position.y) * 0.5f) + 0.045f * scale;
            }
            // 꿀단지 원점 = 밑면 가운데 → 무릎 위, 방향은 그대로 (흘러내린 꿀 = 앞 오른쪽, 옆 카메라 쪽)
            pot.SetParent(t, true);
            pot.localScale *= HeldScale;
            pot.position = lap;
            var r = pot.GetComponent<Renderer>();
            var b = r != null ? r.bounds : new Bounds(lap + Vector3.up * 0.08f, new Vector3(0.2f, 0.16f, 0.2f));
            _potCenter = t.InverseTransformPoint(new Vector3(b.center.x, lap.y + 0.08f, b.center.z));
            _potMouth = t.InverseTransformPoint(new Vector3(b.center.x, b.max.y - 0.03f, b.center.z));
        }

        private void LateUpdate()
        {
            if (_pot == null || Weight <= 0f)
                return;
            var t = transform;
            float w = Mathf.Clamp01(Weight);
            float eat = Mathf.Clamp01(Eat) * (1f - Ouch);
            float s = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * Scoop);   // 0 = 꿀단지, 1 = 입
            if (_chest != null)
                _chest.rotation = Quaternion.AngleAxis(w * (eat * 9f * (1f - 0.4f * s) - Ouch * 6f), t.right) * _chest.rotation;
            var center = t.TransformPoint(_potCenter);
            var mouth = t.TransformPoint(_potMouth);
            var right = t.right;
            var up = t.up;
            var fwd = t.forward;
            var hugR = center + right * (0.1f * _scale) + up * (0.02f * _scale);
            var hugL = center - right * (0.1f * _scale) + up * (0.02f * _scale);
            var dip = mouth - up * (0.03f * _scale);
            var toMouth = Mouth + right * (0.02f * _scale) - fwd * (0.01f * _scale);
            var noseR = Nose + right * (0.05f * _scale) - fwd * (0.03f * _scale);
            var noseL = Nose - right * (0.05f * _scale) - fwd * (0.03f * _scale);
            var rTarget = Vector3.Lerp(hugR, Vector3.Lerp(dip, toMouth, s), eat);
            rTarget = Vector3.Lerp(rTarget, noseR, Ouch);
            var lTarget = Vector3.Lerp(hugL, noseL, Ouch);
            Reach(_rUpper, _rLower, _rHand, rTarget, right - up * 0.4f, w);
            Reach(_lUpper, _lLower, _lHand, lTarget, -right - up * 0.4f, w);
            if (_head != null)
            {
                float now = Time.unscaledTime;
                float pitch = eat * Mathf.Lerp(20f, -6f, s) - Innocent * 14f - Ouch * 18f;       // + = 숙임
                float yaw = -Innocent * 38f + Ouch * 10f * Mathf.Sin(now * 38f);                   // − = 왼쪽 (꿀벌 반대쪽 · 카메라 반대쪽)
                float roll = Innocent * 8f * Mathf.Sin(now * 2.2f);                                // 흥얼거리며 까딱
                _head.rotation = Quaternion.AngleAxis(w * yaw, up) * Quaternion.AngleAxis(w * pitch, right) * Quaternion.AngleAxis(w * roll, fwd) * _head.rotation;
            }
        }

        /// <summary>두 마디 IK: 어깨 · 팔꿈치 · 앞발 — 팔꿈치는 pole 쪽으로 굽힘. 닿지 않으면 그쪽으로 뻗음.</summary>
        private static void Reach(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole, float weight)
        {
            if (upper == null || lower == null || hand == null || weight <= 0f)
                return;
            Vector3 s = upper.position, e = lower.position, h = hand.position;
            float a = (e - s).magnitude, b = (h - e).magnitude;
            if (a < 1e-4f || b < 1e-4f)
                return;
            var to = target - s;
            if (to.sqrMagnitude < 1e-8f)
                return;
            var dir = to.normalized;
            float d = Mathf.Clamp(to.magnitude, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
            float x = (a * a - b * b + d * d) / (2f * d);
            float y = Mathf.Sqrt(Mathf.Max(0f, a * a - x * x));
            var bend = Vector3.ProjectOnPlane(pole, dir);
            if (bend.sqrMagnitude < 1e-6f)
                bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            var elbow = s + dir * x + bend.normalized * y;
            upper.rotation = Quaternion.Slerp(upper.rotation, Quaternion.FromToRotation(e - s, elbow - s) * upper.rotation, weight);
            e = lower.position;
            h = hand.position;
            var goal = s + dir * d;
            lower.rotation = Quaternion.Slerp(lower.rotation, Quaternion.FromToRotation(h - e, goal - e) * lower.rotation, weight);
        }
    }
}
