using System.Collections.Generic;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>주민 한 명의 색 (InteriorResidents가 튜닝 색표에서 고름).</summary>
    public struct ResidentColors
    {
        public Color Skin, Eye, Hair, Top, Bottom, Shoes, Under;
    }

    /// <summary>
    /// 11-11 주민 인물 하나. 자체 제작 모델(11-11b, <see cref="ResidentModelSet"/> — 사람형 본)을 놓고 외형(<see cref="ResidentLook"/>)대로
    /// 몸 구역 · 옷 · 머리를 고르고, 본을 돌려 자세(서기 · 앉기 · 작업)를 만든다.
    /// 걷지 않는다: 숨쉬기(가슴이 살짝 부풂) + 머리를 천천히 두리번 + 가까이 온 플레이어를 바라봄.
    /// 텍스처(얼굴 합성 · 옷색 · 머리색)는 공유 재질 하나에 MaterialPropertyBlock으로.
    /// </summary>
    public sealed class ResidentFigure : MonoBehaviour
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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
        public static ResidentFigure Create(ResidentModelSet set, ResidentLook look, ResidentColors colors, Transform parent,
            Vector3 position, Quaternion rotation, ResidentPose pose, float scale, float sitDrop, Material material)
        {
            var root = new GameObject("Resident");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, rotation);
            var figure = root.AddComponent<ResidentFigure>();
            var instance = Instantiate(look.Woman ? set.Woman : set.Man, root.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * scale;
            figure._model = instance.transform;

            Dress(set, look, colors, instance, pose, material);
            var animator = instance.GetComponentInChildren<Animator>();
            var head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            figure._renderer = AttachHair(set, look, colors, instance.transform, head, material);

            if (animator != null)
            {
                ResidentPoser.Apply(animator, root.transform, pose);
                figure._head = head;
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

        public void Configure(string label, float seed, Transform watcher, float lookRange)
        {
            Label = label;
            _seed = seed;
            _watcher = watcher;
            _lookRange = lookRange;
        }

        /// <summary>
        /// 모델 속 메시 고르기: 몸 구역 `R2?_B_구역`은 입은 옷이 가리는 구역만 지우고, 옷 `R2?_C_이름`은 입은 것만 남김.
        /// 얼굴(합성) · 옷(색) 텍스처를 입힘. 미니스커트는 앉기 자세에서 블렌드셰이프로 허벅지 위에 내려앉음.
        /// </summary>
        private static void Dress(ResidentModelSet set, ResidentLook look, ResidentColors colors, GameObject instance, ResidentPose pose, Material material)
        {
            var hidden = new HashSet<string>();
            foreach (var name in look.Garments)
            {
                var g = set.Garment(name);
                if (g != null)
                    foreach (var r in g.Hides) hidden.Add(r);
            }
            var face = ResidentTextures.Face(set.FacePart, look.Eye, look.Brow, look.Mouth, look.Mark, colors.Skin, colors.Eye, colors.Hair);
            foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string n = smr.gameObject.name;
                int b = n.IndexOf("_B_", System.StringComparison.Ordinal);
                int c = n.IndexOf("_C_", System.StringComparison.Ordinal);
                Texture tex = null;
                if (b >= 0)
                {
                    if (hidden.Contains(n.Substring(b + 3)))
                    {
                        DestroyNow(smr.gameObject);
                        continue;
                    }
                    tex = face;
                }
                else if (c >= 0)
                {
                    string garment = n.Substring(c + 3);
                    if (!look.Garments.Contains(garment))
                    {
                        DestroyNow(smr.gameObject);
                        continue;
                    }
                    var def = set.Garment(garment);
                    tex = ResidentTextures.Cloth(set.ClothTexture(garment, look.Woman), GarmentColor(def, colors));
                    if (def != null && pose == ResidentPose.Sit && !string.IsNullOrEmpty(def.SitShape) && smr.sharedMesh != null)
                    {
                        int k = smr.sharedMesh.GetBlendShapeIndex(def.SitShape);
                        if (k >= 0)
                            smr.SetBlendShapeWeight(k, 100f);
                    }
                }
                smr.updateWhenOffscreen = false;
                if (material != null)
                    smr.sharedMaterial = material;
                Apply(smr, tex, Color.white);
            }
        }

        private static Color GarmentColor(GarmentDef def, ResidentColors c)
        {
            if (def == null)
                return Color.white;
            switch (def.Slot)
            {
                case GarmentSlot.Top:
                case GarmentSlot.Suit: return c.Top;
                case GarmentSlot.Bottom: return c.Bottom;
                case GarmentSlot.Shoes: return c.Shoes;
                default: return c.Under;
            }
        }

        /// <summary>머리 조합 메시를 머리 모델(`R2H_앞_옆_뒤_x`)에서 찾아 쉬는 자세 그대로 놓고 Head 본에 붙임 (자세 · 고개 돌림을 따라감).</summary>
        private static Renderer AttachHair(ResidentModelSet set, ResidentLook look, ResidentColors colors, Transform model, Transform head, Material material)
        {
            var source = set.Hair != null ? set.Hair.transform.Find("R2H_" + look.Hair + "_x") : null;
            var mf = source != null ? source.GetComponent<MeshFilter>() : null;
            if (mf == null)
                return model.GetComponentInChildren<SkinnedMeshRenderer>();
            var go = new GameObject("Hair");
            go.transform.SetParent(model, false);
            go.transform.localPosition = source.localPosition;
            go.transform.localRotation = source.localRotation;
            go.transform.localScale = source.localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (material != null)
                mr.sharedMaterial = material;
            Apply(mr, set.HairTexture, colors.Hair);
            if (head != null)
                go.transform.SetParent(head, true);
            return mr;
        }

        private static void Apply(Renderer r, Texture tex, Color color)
        {
            var block = new MaterialPropertyBlock();
            if (tex != null)
            {
                block.SetTexture(MainTexId, tex);
                block.SetTexture(BaseMapId, tex);
            }
            block.SetColor(BaseColorId, color);
            r.SetPropertyBlock(block);
        }

        private static void DestroyNow(GameObject go)
        {
            go.SetActive(false);
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go); // 에디터 미리보기
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
