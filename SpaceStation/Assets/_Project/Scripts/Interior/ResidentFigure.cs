using System;
using System.Collections.Generic;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-11 주민 인물 하나 — 11-11d 동물 주민(<see cref="AnimalModelSet"/>, 모든 종류가 같은 몸 · 리그).
    /// 본을 돌려 자세(서기 · 앉기 · 작업)를 만든다. 걷지 않는다: 숨쉬기(가슴이 살짝 부풂) + 머리를 천천히 두리번 + 가까이 온 플레이어를 바라봄.
    /// 재질은 슬롯마다 공유 재질, 색은 재질 번호별 MaterialPropertyBlock.
    /// </summary>
    public sealed class ResidentFigure : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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
        private float _tagTop;          // 자세를 잡은 뒤 실제 키 (그리기 범위는 넉넉한 상자라 그 위면 이름표가 너무 높이 뜸)

        /// <summary>이름표에 쓰는 글 (이름 · 직함 · 종류 · 특성 · 지금 하는 일).</summary>
        public string Label { get; private set; }
        /// <summary>이름표를 띄울 곳 (머리 위).</summary>
        public Vector3 TagPoint => new Vector3(_head != null ? _head.position.x : transform.position.x,
            transform.position.y + (_tagTop > 0f ? _tagTop : 1f) + 0.1f, _head != null ? _head.position.z : transform.position.z);

        /// <summary>
        /// 동물 주민을 만든다. 원점 = 발 사이 바닥(앉기는 앉는 면 − <paramref name="sitDrop"/>), 앞 = +z.
        /// 본은 이름으로 찾음(Humanoid 아바타 없음 — 본 이름 = HumanBodyBones 이름).
        /// 재질은 슬롯 이름(`M_Animal_<슬롯>`)으로 공유 재질로 바꿈 — Unity가 안 쓰는 슬롯을 빼서 순서가 종류마다 다름.
        /// 색: 털 = <paramref name="fur"/>, 나머지 = 종류 표, 눈 = 재질 색 그대로.
        /// </summary>
        public static ResidentFigure Create(AnimalModelSet set, AnimalSpecies species, Color fur, Transform parent,
            Vector3 position, Quaternion rotation, ResidentPose pose, float scale, float sitDrop)
        {
            var root = new GameObject("Resident");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, rotation);
            var figure = root.AddComponent<ResidentFigure>();
            var instance = Instantiate(species.Model, root.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * scale;
            foreach (var a in instance.GetComponentsInChildren<Animator>(true))
                a.enabled = false; // 컨트롤러 없음 — 잡은 자세 그대로

            foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = false;
                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string slot = AnimalModelSet.SlotOf(mats[i] != null ? mats[i].name : null);
                    var shared = slot != null ? set.Material(slot) : null;
                    if (shared != null)
                        mats[i] = shared;
                }
                smr.sharedMaterials = mats;
                for (int i = 0; i < mats.Length; i++)
                {
                    string slot = AnimalModelSet.SlotOf(mats[i] != null ? mats[i].name : null);
                    Color? c = slot == AnimalModelSet.Fur ? fur : slot == AnimalModelSet.FurLight ? species.Light
                        : slot == AnimalModelSet.Pink ? species.Accent : slot == AnimalModelSet.Dark ? species.Dark
                        : slot == AnimalModelSet.Beak ? species.Beak : (Color?)null;
                    if (c == null)
                        continue;
                    var block = new MaterialPropertyBlock();
                    block.SetColor(BaseColorId, c.Value);
                    smr.SetPropertyBlock(block, i);
                }
                figure._renderer = smr;
            }

            var bones = ResidentPoser.ByName(instance.transform);
            var restHead = bones(HumanBodyBones.Head);
            float restAboveHead = figure._renderer != null && restHead != null ? figure._renderer.bounds.max.y - restHead.position.y : 0.4f * scale;
            ResidentPoser.Apply(bones, root.transform, pose);
            if (pose == ResidentPose.Sit)
                ResidentPoser.SeatOn(bones, root.transform, instance.transform, sitDrop, scale);
            figure._head = bones(HumanBodyBones.Head);
            figure._chest = bones(HumanBodyBones.Chest);
            if (figure._head != null)   // 이름표 높이 = 자세 잡은 머리 본 + (쉬는 자세의 머리 본 → 메시 꼭대기, 귀 포함)
                figure._tagTop = root.transform.InverseTransformPoint(figure._head.position).y + restAboveHead;
            FitBounds(root.transform, instance, scale);
            if (figure._head != null)
                figure._headBase = Quaternion.Inverse(root.transform.rotation) * figure._head.rotation;
            if (figure._chest != null)
                figure._chestScale = figure._chest.localScale;

            // 지나갈 수 없게 + 바라보기 판정
            var capsule = root.AddComponent<CapsuleCollider>();
            float height = (pose == ResidentPose.Sit ? 0.75f : 0.95f) * scale;
            capsule.radius = 0.24f * scale;
            capsule.height = Mathf.Max(height, capsule.radius * 2f);
            capsule.center = new Vector3(0f, pose == ResidentPose.Sit ? sitDrop + height * 0.4f : height * 0.5f, pose == ResidentPose.Sit ? 0.05f : 0f);
            return figure;
        }

        public void Configure(string label, float seed, Transform watcher, float lookRange)
        {
            Label = label;
            _seed = seed;
            _watcher = watcher;
            _lookRange = lookRange;
        }

        /// <summary>
        /// 그리기 범위를 인물 전체 상자로 (자세를 잡으면 메시가 쉬는 자세 범위를 벗어남 — 앉은 다리가 화면 가장자리에서 사라질 수 있음).
        /// 원점(발 사이 바닥)부터 높이 1.5 · 폭 1.2 · 깊이 1.2 (모델 원래 크기 기준, 동물 키 약 0.9 · 귀 포함 1.15).
        /// </summary>
        private static void FitBounds(Transform root, GameObject instance, float scale)
        {
            var center = root.position + root.up * (0.75f * scale);
            var half = new Vector3(0.6f, 0.75f, 0.6f) * scale;
            foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var space = smr.rootBone != null ? smr.rootBone : smr.transform;
                var b = new Bounds(space.InverseTransformPoint(center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
                    b.Encapsulate(space.InverseTransformPoint(center + root.rotation * corner));
                }
                smr.localBounds = b;
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
    /// 본 찾기는 함수로 받음 (동물 리그 = <see cref="ByName"/>).
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

        public static void Apply(Func<HumanBodyBones, Transform> bones, Transform root, ResidentPose pose)
        {
            var aims = pose == ResidentPose.Sit ? Sit : pose == ResidentPose.Work ? Work : Stand;
            foreach (var a in aims)
            {
                AimBone(bones, root, a.Bone, a.Child, a.LeftDir);
                AimBone(bones, root, Mirror(a.Bone), Mirror(a.Child), new Vector3(-a.LeftDir.x, a.LeftDir.y, a.LeftDir.z));
            }
        }

        /// <summary>본 이름이 HumanBodyBones 이름과 같은 모델(11-11d 동물 리그)에서 이름으로 본을 찾음.</summary>
        public static Func<HumanBodyBones, Transform> ByName(Transform model)
        {
            var map = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (!map.ContainsKey(t.name))
                    map[t.name] = t;
            }
            return b => map.TryGetValue(b.ToString(), out var t) ? t : null;
        }

        /// <summary>앉기: 허벅지 아래가 앉는 면(원점 위 sitDrop)에 닿도록 모델을 올리고 내린다.</summary>
        public static void SeatOn(Func<HumanBodyBones, Transform> bones, Transform root, Transform model, float sitDrop, float scale)
        {
            var hip = bones(HumanBodyBones.LeftUpperLeg);
            if (hip == null)
                return;
            float hipY = root.InverseTransformPoint(hip.position).y;
            float target = sitDrop + 0.06f * scale; // 허벅지 반지름만큼 위
            model.localPosition += Vector3.up * (target - hipY);
        }

        private static void AimBone(Func<HumanBodyBones, Transform> bones, Transform root, HumanBodyBones bone, HumanBodyBones child, Vector3 dir)
        {
            var b = bones(bone);
            var c = bones(child);
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
