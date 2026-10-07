using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-11 주민 인물 하나. LuceedStudio "Little Guys" 모델(사람형 본)을 놓고 본을 돌려 자세(서기 · 앉기 · 작업)를 만든다.
    /// 걷지 않는다: 숨쉬기(가슴이 살짝 부풂) + 머리를 천천히 두리번 + 가까이 온 플레이어를 바라봄.
    /// 옷 · 머리 색은 <see cref="ResidentOutfits"/>가 만든 텍스처를 MaterialPropertyBlock(_MainTex)으로 입힌다.
    /// 처음 만든 Blender 로우폴리 인물은 "살벌해 보인다"는 피드백으로 교체 (2026-10-07).
    /// </summary>
    public sealed class ResidentFigure : MonoBehaviour
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        private Transform _model;
        private Renderer _renderer;
        private Transform _head;
        private Transform _chest;
        private Quaternion _headBase;   // 자세를 잡은 뒤 머리 회전 (인물 기준)
        private Vector3 _chestScale;
        private Transform _watcher;
        private float _seed;
        private float _lookRange;
        private float _yaw;
        private float _pitch;

        /// <summary>이름표에 쓰는 글 (이름 · 특성 · 지금 하는 일).</summary>
        public string Label { get; private set; }
        /// <summary>이름표를 띄울 곳 (머리 위).</summary>
        public Vector3 TagPoint => _renderer != null
            ? new Vector3(_head != null ? _head.position.x : transform.position.x, _renderer.bounds.max.y + 0.12f, _head != null ? _head.position.z : transform.position.z)
            : transform.position + Vector3.up * 1.8f;

        /// <summary>
        /// 인물을 만든다. 원점 = 발 사이 바닥(앉기는 앉는 면 − <paramref name="sitDrop"/>), 앞 = +z.
        /// </summary>
        public static ResidentFigure Create(GameObject model, Transform parent, Vector3 position, Quaternion rotation, ResidentPose pose, float scale, float sitDrop, Material material = null)
        {
            var root = new GameObject("Resident");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, rotation);
            var figure = root.AddComponent<ResidentFigure>();
            var instance = Instantiate(model, root.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * scale;
            figure._model = instance.transform;
            figure._renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            if (figure._renderer is SkinnedMeshRenderer smr)
                smr.updateWhenOffscreen = false;
            // 원본 툰 셰이더는 주광(태양)만 받아 내부(태양 꺼짐 · 점광원만)에서 새까맸음 → URP Lit 재질로 바꿔 끼움
            if (figure._renderer != null && material != null)
                figure._renderer.sharedMaterial = material;

            var animator = instance.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                ResidentPoser.Apply(animator, root.transform, pose);
                figure._head = animator.GetBoneTransform(HumanBodyBones.Head);
                figure._chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                animator.enabled = false; // 컨트롤러 없음 — 잡은 자세 그대로
                if (pose == ResidentPose.Sit)
                    ResidentPoser.SeatOn(animator, root.transform, instance.transform, sitDrop, scale);
            }
            if (figure._head != null)
                figure._headBase = Quaternion.Inverse(root.transform.rotation) * figure._head.rotation;
            if (figure._chest != null)
                figure._chestScale = figure._chest.localScale;

            // 지나갈 수 없게 + 바라보기 판정
            var capsule = root.AddComponent<CapsuleCollider>();
            float height = pose == ResidentPose.Sit ? 1.1f * scale : 1.32f * scale;
            capsule.radius = 0.22f * scale;
            capsule.height = height;
            capsule.center = new Vector3(0f, pose == ResidentPose.Sit ? sitDrop + height * 0.4f : height * 0.5f, pose == ResidentPose.Sit ? 0.1f : 0f);
            return figure;
        }

        public void Configure(string label, Texture outfit, float seed, Transform watcher, float lookRange)
        {
            Label = label;
            _seed = seed;
            _watcher = watcher;
            _lookRange = lookRange;
            if (_renderer != null && outfit != null)
            {
                var block = new MaterialPropertyBlock();
                _renderer.GetPropertyBlock(block);
                block.SetTexture(MainTexId, outfit);
                block.SetTexture(BaseMapId, outfit);
                _renderer.SetPropertyBlock(block);
            }
        }

        private void Update()
        {
            float t = Time.unscaledTime + _seed;
            // 숨쉬기: 약 4초 주기로 가슴이 0.6% 부풂
            if (_chest != null)
                _chest.localScale = _chestScale * (1f + Mathf.Sin(t * 1.6f) * 0.006f);
            if (_head == null)
                return;
            // 머리: 평소엔 천천히 두리번, 플레이어가 가까우면 그쪽을 봄 (좌우 60° 안)
            float target = Mathf.Sin(t * 0.23f) * 16f + Mathf.Sin(t * 0.61f) * 5f;
            float pitch = Mathf.Sin(t * 0.17f) * 3f;
            if (_watcher != null)
            {
                var to = _watcher.position - _head.position;
                if (to.sqrMagnitude < _lookRange * _lookRange)
                {
                    var local = transform.InverseTransformDirection(to);
                    float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                    if (Mathf.Abs(yaw) < 100f)
                    {
                        target = Mathf.Clamp(yaw, -60f, 60f);
                        pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -20f, 20f);
                    }
                }
            }
            _yaw = Mathf.MoveTowardsAngle(_yaw, target, 90f * Time.unscaledDeltaTime);
            _pitch = Mathf.MoveTowards(_pitch, pitch, 40f * Time.unscaledDeltaTime);
            _head.rotation = transform.rotation * Quaternion.Euler(_pitch, _yaw, 0f) * _headBase;
        }
    }

    /// <summary>
    /// 11-11 정지 자세 (사람형 본 방향으로 지정 — 모델 본 축과 무관). 방향은 인물 기준(+x 오른쪽, +y 위, +z 앞), 왼쪽 값을 오른쪽에 좌우 대칭.
    /// </summary>
    public static class ResidentPoser
    {
        private struct Aim
        {
            public HumanBodyBones Bone, Child;
            public Vector3 LeftDir; // 왼쪽 팔다리 기준 (오른쪽은 x 반전)
            public Aim(HumanBodyBones bone, HumanBodyBones child, Vector3 leftDir) { Bone = bone; Child = child; LeftDir = leftDir; }
        }

        private static readonly Aim[] Stand =
        {
            new Aim(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, new Vector3(-0.22f, -1f, 0.02f)),
            new Aim(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, new Vector3(-0.1f, -1f, 0.18f)),
        };

        private static readonly Aim[] Work =
        {
            new Aim(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, new Vector3(-0.25f, -0.6f, 0.75f)),
            new Aim(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, new Vector3(0.04f, -0.15f, 1f)), // 안쪽으로 많이 모으면 팔짱처럼 보였음
        };

        private static readonly Aim[] Sit =
        {
            new Aim(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, new Vector3(-0.1f, -0.05f, 1f)),
            new Aim(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, new Vector3(-0.02f, -1f, 0.2f)),
            new Aim(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, new Vector3(0f, -0.3f, 1f)),
            new Aim(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, new Vector3(-0.15f, -1f, 0.45f)),
            new Aim(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, new Vector3(0.1f, -0.35f, 1f)),
        };

        public static void Apply(Animator animator, Transform root, ResidentPose pose)
        {
            var aims = pose == ResidentPose.Sit ? Sit : pose == ResidentPose.Work ? Work : Stand;
            foreach (var a in aims)
            {
                AimBone(animator, root, a.Bone, a.Child, a.LeftDir);
                AimBone(animator, root, Mirror(a.Bone), Mirror(a.Child), new Vector3(-a.LeftDir.x, a.LeftDir.y, a.LeftDir.z));
            }
        }

        /// <summary>앉기: 허벅지 아래가 앉는 면(원점 위 sitDrop)에 닿도록 모델을 올리고 내린다.</summary>
        public static void SeatOn(Animator animator, Transform root, Transform model, float sitDrop, float scale)
        {
            var hip = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            if (hip == null)
                return;
            float hipY = root.InverseTransformPoint(hip.position).y;
            float target = sitDrop + 0.06f * scale; // 허벅지 반지름만큼 위
            model.localPosition += Vector3.up * (target - hipY);
        }

        private static void AimBone(Animator animator, Transform root, HumanBodyBones bone, HumanBodyBones child, Vector3 dir)
        {
            var b = animator.GetBoneTransform(bone);
            var c = animator.GetBoneTransform(child);
            if (b == null || c == null)
                return;
            var now = c.position - b.position;
            var want = root.TransformDirection(dir.normalized);
            if (now.sqrMagnitude < 1e-8f)
                return;
            b.rotation = Quaternion.FromToRotation(now.normalized, want) * b.rotation;
        }

        private static HumanBodyBones Mirror(HumanBodyBones b)
        {
            switch (b)
            {
                case HumanBodyBones.LeftUpperArm: return HumanBodyBones.RightUpperArm;
                case HumanBodyBones.LeftLowerArm: return HumanBodyBones.RightLowerArm;
                case HumanBodyBones.LeftHand: return HumanBodyBones.RightHand;
                case HumanBodyBones.LeftUpperLeg: return HumanBodyBones.RightUpperLeg;
                case HumanBodyBones.LeftLowerLeg: return HumanBodyBones.RightLowerLeg;
                case HumanBodyBones.LeftFoot: return HumanBodyBones.RightFoot;
                case HumanBodyBones.LeftToes: return HumanBodyBones.RightToes;
                default: return b;
            }
        }
    }
}
