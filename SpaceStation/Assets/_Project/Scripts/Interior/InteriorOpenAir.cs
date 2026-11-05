using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 이상 개방형 방의 우주복 (사용자 2026-10-26 "압력에서 자유로운 우주복으로 바깥 정원을 걷는다", 2026-11-02 그리드 허브 시안 1):
    /// 실내 구역(<see cref="_indoor"/>) 밖에 있는 주민 = 둥근 유리 헬멧 · 금빛 목 테 · 등 팩을 켬, 실내로 들어오면 끔 (걸어서 압력 막을 지나면 그때 바뀜).
    /// 얼굴이 그대로 보이게 몸 모델은 건드리지 않음. 헬멧은 머리 본 자식 (고개를 돌리면 따라감), 크기는 동물마다 메시를 구워 머리 · 귀를 감싸게 잼 (종류별 한 번).
    /// 귀가 긴 동물(토끼, 사용자 2026-11-02 "귀가 밖으로 빠져나와 있는 게 더 귀여울 것")은 머리만 감싸고 귀는 유리 밖으로 — 귀가 지나는 유리 면에 작은 금빛 고리 (귀를 까딱이면 면을 따라 미끄러짐).
    /// 재는 값은 모두 쉬는 자세(바인드 포즈)의 본 기준이라, 첫 주민이 앉았든 걷든, 주민 크기가 다르든 그대로 맞음.
    /// 실행 순서: 귀 본을 움직이는 ResidentMotion.LateUpdate 뒤에 고리를 옮김.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class InteriorOpenAir : InteriorRoomDisplay
    {
        private const float HelmetMargin = 1.22f;   // 머리 · 귀 상자 반 크기 → 타원체 반지름
        private const float EarWiggleRoom = 1.08f;  // 귀가 까딱여도 유리 안에 (가장 먼 꼭짓점 기준 배율)
        private const float PackDepth = 0.05f;      // 팩 가운데 ~ 뒤 판 (모델 크기)
        private const float PackSink = 0.012f;      // 팩 뒤 판이 등에 묻는 깊이 (모델 크기 — 뜬 것처럼 보이지 않게)
        private const float EarsOutAbove = 0.55f;   // 머리 본 위로 이보다 높이 솟으면 귀가 긴 동물 (모델 크기, 머리 꼭대기 약 0.37)
        private const float HeadTop = 0.4f;         // 귀가 긴 동물의 헬멧이 감싸는 높이 (머리 본 위, 모델 크기)
        private const float EarRingGap = 1.15f;     // 귀 단면 → 고리 반지름
        private const float EarRingRound = 0.6f;    // 고리의 짧은 쪽 ÷ 긴 쪽 최소 (납작한 귀에도 고리 모양이 남게)

        [Tooltip("실내 구역 (프리팹 로컬 x, z, 반지름) — 이 밖의 주민 = 우주복")]
        [SerializeField] private Vector3[] _indoor = System.Array.Empty<Vector3>();
        [Tooltip("실내 상자 (프리팹 로컬 x, z 직사각형) — 높이 _indoorBoxTop 아래만 (건물 옥상은 바깥)")]
        [SerializeField] private Rect _indoorBox;
        [SerializeField] private float _indoorBoxTop;
        [SerializeField] private Mesh _helmet;
        [SerializeField] private Mesh _rim;
        [SerializeField] private Mesh _pack;
        [SerializeField] private Mesh _packLight;
        [SerializeField] private Material _glass;
        [SerializeField] private Material _gold;
        [SerializeField] private Material _trim;
        [SerializeField] private Material _light;

        /// <summary>쉬는 자세로 잰 맞춤 값 (위치 · 회전은 각 본 기준, 길이는 모델 크기).</summary>
        private struct Fit
        {
            public Vector3 HelmetPosition;     // 머리 본 기준
            public Quaternion HelmetRotation;
            public Vector3 Radii;              // 헬멧 타원체 반지름 (감싸는 꼭짓점이 모두 안에)
            public float RimDrop;              // 헬멧 가운데 → 목 테 (아래로)
            public Vector3 PackPosition;       // 가슴 본 기준
            public Quaternion PackRotation;
            public EarRing[] Ears;             // 귀가 유리를 지나는 자리 (귀가 짧으면 없음)
        }

        private struct EarRing
        {
            public string Bone;
            public Vector3 Point;              // 귀 본 기준 — 쉬는 자세에서 귀가 유리를 지나는 자리
            public Vector3 Tangent;            // 헬멧 기준 고리 짧은 축
            public Vector3 Scale;
        }

        /// <summary>헬멧 면에 붙은 고리 — 귀가 움직이면 면을 따라 미끄러짐.</summary>
        private sealed class LiveRing
        {
            public Transform Ring, Ear, Helmet;
            public Vector3 Point, Tangent, Radii;
        }

        private struct BoneRest
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        private sealed class Suit
        {
            public readonly List<GameObject> Parts = new List<GameObject>();
            public bool On;

            public void Set(bool on)
            {
                if (On == on)
                    return;
                On = on;
                foreach (var p in Parts)
                {
                    if (p != null)
                        p.SetActive(on);
                }
            }
        }

        private static readonly Dictionary<Mesh, Fit> Fits = new Dictionary<Mesh, Fit>();
        private static readonly List<Vector3> Scratch = new List<Vector3>();
        private static Mesh _baked;

        private readonly Dictionary<ResidentFigure, Suit> _suits = new Dictionary<ResidentFigure, Suit>();
        private readonly List<ResidentFigure> _figures = new List<ResidentFigure>();
        private readonly List<LiveRing> _rings = new List<LiveRing>();

        public void Configure(Vector3[] indoor, Mesh helmet, Mesh rim, Mesh pack, Mesh packLight, Material glass, Material gold, Material trim, Material light)
        {
            _indoor = indoor;
            _helmet = helmet;
            _rim = rim;
            _pack = pack;
            _packLight = packLight;
            _glass = glass;
            _gold = gold;
            _trim = trim;
            _light = light;
        }

        /// <summary>옥상을 걷는 건물 (드론 정비창): 건물 평면 + 지붕 높이.</summary>
        public void SetIndoorBox(Rect xz, float top)
        {
            _indoorBox = xz;
            _indoorBoxTop = top;
        }

        /// <summary>이 프리팹 로컬 자리가 실내 구역 안인지.</summary>
        private bool Indoors(Vector3 local)
        {
            if (_indoorBox.width > 0f && local.y < _indoorBoxTop && _indoorBox.Contains(new Vector2(local.x, local.z)))
                return true;
            foreach (var c in _indoor)
            {
                float dx = local.x - c.x, dz = local.z - c.y;
                if (dx * dx + dz * dz < c.z * c.z)
                    return true;
            }
            return false;
        }

        protected override void Refresh()
        {
            var room = transform.parent != null ? transform.parent : transform;
            _figures.Clear();
            room.GetComponentsInChildren(_figures);
            foreach (var f in _figures)
            {
                if (f == null)
                    continue;
                bool outdoors = !Indoors(transform.InverseTransformPoint(f.transform.position));
                if (!_suits.TryGetValue(f, out var suit))
                {
                    if (!outdoors)
                        continue;   // 실내에만 머무는 주민은 만들지 않음
                    suit = Dress(f);
                    _suits[f] = suit;
                }
                suit.Set(outdoors);
            }
        }

        private void LateUpdate()
        {
            if (!Active)
                return;
            foreach (var r in _rings)
            {
                if (r.Ring != null && r.Ring.gameObject.activeInHierarchy)
                    Slide(r);
            }
        }

        /// <summary>귀가 유리를 지나는 자리를 타원체 면으로 옮기고 고리를 면에 눕힘.</summary>
        private static void Slide(LiveRing r)
        {
            var p = r.Helmet.InverseTransformPoint(r.Ear.TransformPoint(r.Point));
            var s = new Vector3(p.x / r.Radii.x, p.y / r.Radii.y, p.z / r.Radii.z).normalized;
            var normal = new Vector3(s.x / r.Radii.x, s.y / r.Radii.y, s.z / r.Radii.z).normalized;
            r.Ring.localPosition = Vector3.Scale(s, r.Radii);
            r.Ring.localRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(r.Tangent, normal), normal);
        }

        private Suit Dress(ResidentFigure figure)
        {
            var suit = new Suit { On = true };
            var root = figure.transform;
            var model = root.childCount > 0 ? root.GetChild(0) : root;
            var head = FindBone(root, "Head");
            var chest = FindBone(root, "Chest") ?? FindBone(root, "Spine");
            var smr = root.GetComponentInChildren<SkinnedMeshRenderer>();
            if (head == null || smr == null || smr.sharedMesh == null)
                return suit;
            if (!Fits.TryGetValue(smr.sharedMesh, out var fit))
            {
                fit = Measure(model, smr, head, chest);
                Fits[smr.sharedMesh] = fit;
            }
            int layer = head.gameObject.layer;
            // 헬멧: 머리 본 자식
            var helmet = new GameObject("SuitHelmet").transform;
            helmet.SetParent(head, false);
            helmet.localPosition = fit.HelmetPosition;
            helmet.localRotation = fit.HelmetRotation;
            Part("Glass", helmet, _helmet, _glass, Vector3.zero, fit.Radii);
            float t = fit.RimDrop / fit.Radii.y;
            float rimR = Mathf.Sqrt(Mathf.Max(0.05f, 1f - t * t));
            float rimX = fit.Radii.x * rimR, rimZ = fit.Radii.z * rimR;
            Part("Rim", helmet, _rim, _gold, Vector3.down * fit.RimDrop, new Vector3(rimX, (rimX + rimZ) * 0.5f, rimZ));
            // 귀 고리: 헬멧 자식 (유리 면 위) — 귀 본을 따라 면을 미끄러짐
            if (fit.Ears != null)
            {
                foreach (var ear in fit.Ears)
                {
                    var bone = FindBone(root, ear.Bone);
                    var ring = bone != null ? Part("SuitEarRing", helmet, _rim, _gold, Vector3.zero, ear.Scale) : null;
                    if (ring == null)
                        continue;
                    var live = new LiveRing { Ring = ring, Ear = bone, Helmet = helmet, Point = ear.Point, Tangent = ear.Tangent, Radii = fit.Radii };
                    Slide(live);
                    _rings.Add(live);
                }
            }
            Keep(suit, helmet, layer);
            // 등 팩: 가슴 본 자식
            if (chest != null)
            {
                var back = new GameObject("SuitPack").transform;
                back.SetParent(chest, false);
                back.localPosition = fit.PackPosition;
                back.localRotation = fit.PackRotation;
                Part("Pack", back, _pack, _trim, Vector3.zero, Vector3.one);
                Part("PackLight", back, _packLight, _light, Vector3.zero, Vector3.one);
                Keep(suit, back, layer);
            }
            return suit;
        }

        private static void Keep(Suit suit, Transform part, int layer)
        {
            foreach (var t in part.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
            suit.Parts.Add(part.gameObject);
        }

        private static Transform Part(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            if (mesh == null)
                return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        /// <summary>
        /// 본을 쉬는 자세로 잠깐 돌려 메시를 구워 머리 본 위 (머리 · 귀) 상자와 가슴 높이 등 위치를 잼 (같은 프레임에 되돌림 — 화면에는 안 보임).
        /// 등: 가슴 위쪽 띠 · 가운데 좁은 폭 안의 가장 뒤, 몸 앞면 거리로 상한 (몸은 앞뒤가 비슷, 꼬리는 뒤에만 있음 — 사용자 2026-11-02 팩이 등에서 뜸).
        /// </summary>
        private static Fit Measure(Transform model, SkinnedMeshRenderer smr, Transform head, Transform chest)
        {
            var bones = smr.bones;
            var saved = ToBindPose(smr, bones);
            if (_baked == null)
                _baked = new Mesh();
            smr.BakeMesh(_baked, true);
            _baked.GetVertices(Scratch);
            var toModel = model.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            for (int i = 0; i < Scratch.Count; i++)
                Scratch[i] = toModel.MultiplyPoint3x4(Scratch[i]);
            var headRest = model.InverseTransformPoint(head.position);
            var chestRest = chest != null ? model.InverseTransformPoint(chest.position) : headRest;
            float top = float.NegativeInfinity;
            foreach (var p in Scratch)
                top = Mathf.Max(top, p.y);
            bool longEars = top - headRest.y > EarsOutAbove;
            float limit = longEars ? headRest.y + HeadTop : float.PositiveInfinity;
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            float back = 0f, front = 0f;
            float band = (headRest.y - chestRest.y) * 0.6f;
            const float width = 0.06f;
            foreach (var p in Scratch)
            {
                if (p.y > headRest.y && p.y <= limit)
                {
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
                if (p.y > chestRest.y && p.y < chestRest.y + band && Mathf.Abs(p.x - chestRest.x) < width)
                {
                    back = Mathf.Min(back, p.z - chestRest.z);
                    front = Mathf.Max(front, p.z - chestRest.z);
                }
            }
            if (front > 0f)
                back = Mathf.Max(back, -front * 1.15f);
            var center = headRest + Vector3.up * 0.17f;
            var half = Vector3.one * 0.2f;
            if (min.x <= max.x)   // 머리 위 꼭짓점이 없으면 기본 크기
            {
                center = (min + max) * 0.5f;
                half = (max - min) * 0.5f;
            }
            // 상자 모서리 쪽 귀(코알라처럼 위로 넓게 벌어진 귀)도 유리 안에: 실제 꼭짓점이 모두 들어가는 배율 (사용자 2026-11-02 "코알라 귀가 헬멧을 뚫고 나옴")
            float worst = 0f;
            foreach (var p in Scratch)
            {
                if (p.y > headRest.y && p.y <= limit)
                    worst = Mathf.Max(worst, Level(p, center, half));
            }
            var radii = half * Mathf.Max(HelmetMargin, worst * EarWiggleRoom);
            var fit = new Fit
            {
                HelmetPosition = head.InverseTransformPoint(model.TransformPoint(center)),
                HelmetRotation = Quaternion.Inverse(head.rotation) * model.rotation,
                Radii = radii,
                RimDrop = half.y,
            };
            if (chest != null)
            {
                var pack = new Vector3(chestRest.x, chestRest.y + 0.02f, chestRest.z + back - (PackDepth - PackSink));
                fit.PackPosition = chest.InverseTransformPoint(model.TransformPoint(pack));
                fit.PackRotation = Quaternion.Inverse(chest.rotation) * model.rotation;
            }
            if (longEars)
                fit.Ears = MeasureEars(model, center, radii);
            FromBindPose(bones, saved);
            return fit;
        }

        /// <summary>본을 메시의 쉬는 자세로 (본 월드 = 렌더러 월드 × 바인드 포즈 역행렬). 돌아올 값을 돌려줌.</summary>
        private static BoneRest[] ToBindPose(SkinnedMeshRenderer smr, Transform[] bones)
        {
            var binds = smr.sharedMesh.bindposes;
            var world = smr.transform.localToWorldMatrix;
            var rest = new Dictionary<Transform, Matrix4x4>();
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                    rest[bones[i]] = world * binds[i].inverse;
            }
            var saved = new BoneRest[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var b = bones[i];
                if (b == null)
                    continue;
                saved[i] = new BoneRest { Position = b.localPosition, Rotation = b.localRotation, Scale = b.localScale };
                var parent = rest.TryGetValue(b.parent, out var m) ? m : b.parent.localToWorldMatrix;
                var local = parent.inverse * rest[b];
                b.localPosition = local.MultiplyPoint3x4(Vector3.zero);
                b.localRotation = local.rotation;
                b.localScale = local.lossyScale;
            }
            return saved;
        }

        private static void FromBindPose(Transform[] bones, BoneRest[] saved)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null)
                    continue;
                bones[i].localPosition = saved[i].Position;
                bones[i].localRotation = saved[i].Rotation;
                bones[i].localScale = saved[i].Scale;
            }
        }

        /// <summary>타원체 배율 (1 = 면 위, 1보다 크면 밖).</summary>
        private static float Level(Vector3 p, Vector3 center, Vector3 radii)
        {
            var d = p - center;
            return Mathf.Sqrt(d.x * d.x / (radii.x * radii.x) + d.y * d.y / (radii.y * radii.y) + d.z * d.z / (radii.z * radii.z));
        }

        /// <summary>
        /// 귀가 유리 면을 지나는 단면 = 메시 변이 면을 넘는 점들 (꼭짓점 평균은 고리 줄무늬 쪽으로 쏠려 사용자 2026-11-02 "고리가 어긋남").
        /// 고리는 유리 면에 눕는 받침 (사용자 2026-11-02 "귀에 건 고리 말고 헬멧 표면에 맞춰서"): 면의 접평면에서 단면의 긴 축 · 짧은 축으로 가운데 · 크기 · 방향을 잼.
        /// </summary>
        private static EarRing[] MeasureEars(Transform model, Vector3 center, Vector3 radii)
        {
            var rings = new List<EarRing>();
            var tris = _baked.triangles;
            var cut = new List<Vector3>();
            foreach (string name in new[] { "LeftEar", "RightEar" })
            {
                var bone = FindBone(model, name);
                if (bone == null)
                    continue;
                float side = Mathf.Sign(model.InverseTransformPoint(bone.position).x);
                Crossings(tris, center, radii, side, 1f, cut);
                if (cut.Count < 3)
                    continue;
                var c = Mean(cut);
                var o = c - center;
                var normal = new Vector3(o.x / (radii.x * radii.x), o.y / (radii.y * radii.y), o.z / (radii.z * radii.z)).normalized;
                var u0 = Vector3.Cross(normal, Vector3.forward).normalized;
                var v0 = Vector3.Cross(normal, u0);
                float cuu = 0f, cvv = 0f, cuv = 0f;
                foreach (var p in cut)
                {
                    float a = Vector3.Dot(p - c, u0), b = Vector3.Dot(p - c, v0);
                    cuu += a * a;
                    cvv += b * b;
                    cuv += a * b;
                }
                float angle = 0.5f * Mathf.Atan2(2f * cuv, cuu - cvv);
                var u = Mathf.Cos(angle) * u0 + Mathf.Sin(angle) * v0;
                var v = -Mathf.Sin(angle) * u0 + Mathf.Cos(angle) * v0;
                float minU = float.PositiveInfinity, maxU = float.NegativeInfinity, minV = float.PositiveInfinity, maxV = float.NegativeInfinity;
                foreach (var p in cut)
                {
                    float a = Vector3.Dot(p - c, u), b = Vector3.Dot(p - c, v);
                    minU = Mathf.Min(minU, a);
                    maxU = Mathf.Max(maxU, a);
                    minV = Mathf.Min(minV, b);
                    maxV = Mathf.Max(maxV, b);
                }
                var ringCenter = c + u * ((minU + maxU) * 0.5f) + v * ((minV + maxV) * 0.5f);
                float ru = (maxU - minU) * 0.5f * EarRingGap;
                float rv = Mathf.Max((maxV - minV) * 0.5f * EarRingGap, ru * EarRingRound);
                rings.Add(new EarRing
                {
                    Bone = name,
                    Point = bone.InverseTransformPoint(model.TransformPoint(ringCenter)),
                    Tangent = v,
                    Scale = new Vector3(ru, (ru + rv) * 0.5f, rv),
                });
            }
            return rings.ToArray();
        }

        /// <summary>구운 메시의 변이 타원체 배율 level 면을 넘는 점 (가운데 위쪽, side 쪽만 — 그 높이에는 귀만 면을 넘음).</summary>
        private static void Crossings(int[] tris, Vector3 center, Vector3 radii, float side, float level, List<Vector3> into)
        {
            into.Clear();
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    var a = Scratch[tris[i + k]];
                    var b = Scratch[tris[i + (k + 1) % 3]];
                    float ea = Level(a, center, radii), eb = Level(b, center, radii);
                    if ((ea - level) * (eb - level) >= 0f)
                        continue;
                    var p = Vector3.Lerp(a, b, (level - ea) / (eb - ea));
                    var d = p - center;
                    if (d.y > 0f && d.x * side > 0f)
                        into.Add(p);
                }
            }
        }

        private static Vector3 Mean(List<Vector3> points)
        {
            var sum = Vector3.zero;
            foreach (var p in points)
                sum += p;
            return sum / points.Count;
        }

        private static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>())
            {
                if (t.name == name)
                    return t;
            }
            return null;
        }
    }
}
