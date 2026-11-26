using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>발소리 바닥 7 (SOUND_LIST_NEW 4-1 · D-6).</summary>
    public enum StepFloor
    {
        Metal,
        Grate,
        Glass,
        Grass,
        Wood,
        Stone,
        Water,
    }

    /// <summary>
    /// ⑤ 바닥별 발소리 (사운드 개편 2026-11-25): 걸음마다 발밑을 광선 하나로 읽어 맞은 삼각형의 재질 이름으로 바닥을 고른다
    /// (내부 템플릿의 걷는 면 = _Shell 메시 충돌체, 재질 슬롯 = 부분 메시 — 템플릿 84개를 미리보기 장면에서 0.4 m 격자로 훑어 표를 정함).
    /// 얕은 물 = 방의 물 면 (KWS 대신 수면 · 연못 재질, 높이 폭 1.2 m 안) 경계 안에서 발이 수면 (꼭대기)보다 2 ~ 45 cm 낮고 밟은 곳이 흙 · 돌 · 모래일 때 (물 면엔 충돌체가 없음).
    /// 템플릿 84개에서 물 메시에 임시 충돌체를 붙여 잰 정밀 판정과 견줌: 맞음 1623 · 잘못 물 99 (궤도 방어망 물가 모래 83) · 놓침 300 (궤도 도시 분수 화단 295) 점 (0.4 m 격자).
    /// 모르는 재질 = 금속 판, 광선이 안 맞으면 직전 바닥 그대로.
    /// </summary>
    public static class FootstepSurface
    {
        /// <summary>방 안 평평한 물 면 하나 (세계 좌표 xz 경계 · 수면 높이).</summary>
        public readonly struct WaterLevel
        {
            public readonly Rect Area;
            public readonly float Y;

            public WaterLevel(Rect area, float y)
            {
                Area = area;
                Y = y;
            }
        }

        /// <summary>발이 수면 아래 이만큼 (m) 안이면 얕은 물 (더 깊은 곳은 LakeHit 벽으로 못 들어감).</summary>
        public const float MinDepth = 0.02f;
        public const float MaxDepth = 0.45f;

        // 재질 이름 조각 → 바닥 (앞에서부터 먼저 맞는 것). 넓이 순 (2026-11-25 조사): 금속 = 기본 Hull · 1단계 EvoWhite · EvoFloor · 2단계 Evo2Deck · Evo2Wall · Evo2Trim (모르는 재질도 금속),
        // 잔디 = Evo2Lawn 3718 m2 · 돌 = Evo3Pearl 1843 · Evo2Path 1672 · 유리 = Evo2ClearGlass 940 · 모래 해변 539.
        private static readonly (string Part, StepFloor Floor)[] Table =
        {
            ("Glass", StepFloor.Glass), ("Window", StepFloor.Glass), ("Ice", StepFloor.Glass), ("Lens", StepFloor.Glass), ("Mirror", StepFloor.Glass), ("StarSea", StepFloor.Glass),
            ("Lawn", StepFloor.Grass), ("Grass", StepFloor.Grass), ("Hedge", StepFloor.Grass), ("Plant", StepFloor.Grass), ("Moss", StepFloor.Grass), ("Field", StepFloor.Grass),
            ("Flower", StepFloor.Grass), ("Leaf", StepFloor.Grass), ("Wheat", StepFloor.Grass), ("Lavender", StepFloor.Grass), ("Tulip", StepFloor.Grass), ("Pollen", StepFloor.Grass),
            ("Blossom", StepFloor.Grass), ("Wisteria", StepFloor.Grass), ("Soil", StepFloor.Grass), ("Evo2Bed", StepFloor.Grass), ("BeachSand", StepFloor.Grass), ("Evo3Sand", StepFloor.Grass),
            ("Wood", StepFloor.Wood), ("Bark", StepFloor.Wood), ("Plank", StepFloor.Wood), ("Wax", StepFloor.Wood), ("Honey", StepFloor.Wood),
            ("Rug", StepFloor.Wood), ("Blanket", StepFloor.Wood), ("Fabric", StepFloor.Wood), ("Sofa", StepFloor.Wood), ("Curtain", StepFloor.Wood),
            ("Rock", StepFloor.Stone), ("Stone", StepFloor.Stone), ("Concrete", StepFloor.Stone), ("Path", StepFloor.Stone), ("Pearl", StepFloor.Stone), ("Nacre", StepFloor.Stone),
            ("Coral", StepFloor.Stone), ("Cave", StepFloor.Stone), ("Roof", StepFloor.Stone), ("Mount", StepFloor.Stone), ("Evo2Sand", StepFloor.Stone), ("SeaBed", StepFloor.Stone),
            ("Marble", StepFloor.Stone), ("Tile", StepFloor.Stone), ("Brick", StepFloor.Stone),
            ("HullDark", StepFloor.Grate), ("Grate", StepFloor.Grate), ("Grill", StepFloor.Grate),
        };

        // 물 면으로 볼 재질 (폭포 · 은하수 강 · 산소 탱크 액체처럼 세운 면 · 빛은 빼고, 평평한지는 따로 봄)
        private static readonly string[] WaterParts = { "M_InteriorWater", "Pond", "M_InteriorEvo3Water", "M_InteriorEvo4River" };

        private static readonly Dictionary<int, StepFloor> _cache = new Dictionary<int, StepFloor>();

        /// <summary>마지막으로 읽은 바닥 · 재질 이름 (확인용).</summary>
        public static StepFloor LastFloor { get; private set; }
        public static string LastMaterial { get; private set; }

        /// <summary>재질 이름 → 바닥 (모르면 금속 판).</summary>
        public static StepFloor Classify(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
                return StepFloor.Metal;
            foreach (var (part, floor) in Table)
            {
                if (materialName.IndexOf(part, System.StringComparison.Ordinal) >= 0)
                    return floor;
            }
            return StepFloor.Metal;
        }

        /// <summary>
        /// 방 단계별 발소리 키움 (dB, 2026-11-26 정한 것 S-6): 2단계 이상 방은 음악 · 소리 풍경이 커서 발소리가 묻힘 (3단계 약 0 dB, 기본 방 +4 ~ 5)
        /// → 2단계 +2 · 3단계 이상 +4. 기본 · 1단계는 그대로.
        /// </summary>
        public static float StageGainDb(int stage) => stage >= 3 ? 4f : stage == 2 ? 2f : 0f;

        /// <summary><see cref="StageGainDb"/>의 음량 배율.</summary>
        public static float StageGain(int stage) => Mathf.Pow(10f, StageGainDb(stage) / 20f);

        /// <summary>물이 차는 바닥인지 (흙 · 모래 · 돌 · 해저 — 갑판 · 유리 · 나무 다리는 물 위로 걸음).</summary>
        public static bool IsWadeable(StepFloor floor) => floor == StepFloor.Grass || floor == StepFloor.Stone;

        /// <summary>발 자리가 얕은 물 안인지 (물 면 경계 안 · 수면 아래 MinDepth ~ MaxDepth).</summary>
        public static bool InWater(Vector3 feet, IReadOnlyList<WaterLevel> water)
        {
            if (water == null)
                return false;
            for (int i = 0; i < water.Count; i++)
            {
                var w = water[i];
                float depth = w.Y - feet.y;
                if (depth > MinDepth && depth < MaxDepth && w.Area.Contains(new Vector2(feet.x, feet.z)))
                    return true;
            }
            return false;
        }

        /// <summary>발밑 바닥 (걸음마다 한 번 — 광선 하나). 안 맞으면 fallback.</summary>
        public static StepFloor At(Vector3 feet, IReadOnlyList<WaterLevel> water, StepFloor fallback)
        {
            var floor = fallback;
            if (Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out var hit, 0.9f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var mat = MaterialAt(hit);
                if (mat != null)
                {
                    int id = mat.GetInstanceID();
                    if (!_cache.TryGetValue(id, out floor))
                        _cache[id] = floor = Classify(mat.name);
                    LastMaterial = mat.name;
                }
                else
                {
                    floor = StepFloor.Metal;
                    LastMaterial = hit.collider.name;
                }
            }
            if (IsWadeable(floor) && InWater(feet, water))
                floor = StepFloor.Water;
            LastFloor = floor;
            return floor;
        }

        /// <summary>광선이 맞은 삼각형의 재질 (메시 충돌체 + 같은 물체의 렌더러 부분 메시). 상자 충돌체 등은 그 물체 렌더러의 첫 재질, 없으면 null.</summary>
        private static Material MaterialAt(RaycastHit hit)
        {
            var renderer = hit.collider.GetComponent<Renderer>();
            if (renderer == null)
                return null;
            var mats = renderer.sharedMaterials;
            if (mats.Length == 0)
                return null;
            if (hit.collider is MeshCollider mc && mc.sharedMesh != null && hit.triangleIndex >= 0)
            {
                var mesh = mc.sharedMesh;
                int index = hit.triangleIndex * 3;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    var sub = mesh.GetSubMesh(s);
                    if (index >= sub.indexStart && index < sub.indexStart + sub.indexCount)
                        return mats[s];
                }
            }
            return mats[0];
        }

        /// <summary>방 (템플릿 인스턴스) 안 평평한 물 면을 모음 — 방이 바뀔 때 한 번 (꺼진 물체 포함, 메시 경계를 세계 좌표로).</summary>
        public static void CollectWater(Transform room, List<WaterLevel> into)
        {
            into.Clear();
            if (room == null)
                return;
            foreach (var filter in room.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<Renderer>();
                if (renderer == null || filter.sharedMesh == null || !IsWaterMaterial(renderer.sharedMaterials))
                    continue;
                var b = filter.sharedMesh.bounds;
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                for (int k = 0; k < 8; k++)
                {
                    var corner = filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)));
                    min = Vector3.Min(min, corner);
                    max = Vector3.Max(max, corner);
                }
                if (max.y - min.y > 1.2f)
                    continue; // 세운 면 · 높은 폭포 · 탱크 (껍질 메시에 물 재질이 섞인 것)
                // 수면 = 메시 꼭대기 (궤도 항만 바다 · 수리 사령부 개울은 물결 · 비탈로 높이 폭 0.8 ~ 1 m — 가운데 높이로 보면 해변 얕은 물 70 m2를 모두 놓쳤음)
                into.Add(new WaterLevel(new Rect(min.x, min.z, max.x - min.x, max.z - min.z), max.y));
            }
        }

        private static bool IsWaterMaterial(Material[] mats)
        {
            foreach (var m in mats)
            {
                if (m == null)
                    continue;
                foreach (var part in WaterParts)
                {
                    if (m.name.IndexOf(part, System.StringComparison.Ordinal) >= 0)
                        return true;
                }
            }
            return false;
        }
    }
}
