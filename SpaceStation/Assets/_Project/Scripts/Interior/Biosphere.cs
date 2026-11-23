using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 3단계 바이오스피어 "물의 순환 고리" 내부 값 (Blender bio3_builder와 같은 값, 원점 = 0번 칸 중심 · 칸 8 m, 3 × 3칸 2층):
    /// 가운데 허브 (8, 8) · 연꽃 호수 · 물가 길 · 계단 밭 다섯 단 · 능선 산책길 · 숲 비탈 · 진주 옹벽 · 옹벽 밖 고리 산책길 · 네 모서리,
    /// 물길 셋 (능선 샘 · 수문 → 단마다 작은 폭포 → 호수) · 진주 아치 셋이 받친 하늘 호수 (가운데 폭포 기둥) · 뒤 왼쪽 아치 기둥 속 승강기 (능선 ↔ 하늘 정원).
    /// 각은 x-z 평면 수학 각 (0° = +x, 90° = +z, 270° = −z = 앞).
    /// </summary>
    public static class Biosphere
    {
        public const float Floor = -1.3f;
        public static readonly Vector3 Hub = new Vector3(8f, 0f, 8f);

        public const float LakeRadius = 2.8f, ShoreRadius = 3.6f;
        /// <summary>능선 산책길 (안 · 바깥 반지름 · 높이).</summary>
        public const float RidgeInner = 7.6f, RidgeOuter = 8.6f, RidgeTop = Floor + 1.55f;
        public const float WallInner = 9.95f, WallOuter = 10.15f;
        /// <summary>계단 밭 다섯 단 (안 → 바깥): 안 반지름 = 3.6 + 0.8 k, 윗면 = F + 0.3 + 0.25 k.</summary>
        public static float TerraceInner(int k) => ShoreRadius + 0.8f * k;
        public static float TerraceTop(int k) => Floor + 0.3f + 0.25f * k;

        public static readonly float[] Streams = { 90f, 210f, 330f };
        public static readonly float[] Arches = { 30f, 150f, 270f };
        /// <summary>옆 문 넷 (+x · +z · −x · −z), 정문 = 270° (앞 아치 뿌리 두 갈래 사이 계단).</summary>
        public static readonly float[] DoorAngles = { 0f, 90f, 180f, 270f };
        public const float PierAngle = 180f;

        // ---------------- 하늘 호수 · 승강기 ----------------
        public const float SkyRim = 3.85f, SkyWater = 2.3f;
        /// <summary>하늘 정원 길 높이.</summary>
        public const float SkyTop = Floor + 10.46f;
        public const float LegRadius = 9.3f, LegThickness = 0.8f;
        /// <summary>승강기 아치 (뒤 왼쪽) — 문 = 허브 쪽.</summary>
        public const float LiftAngle = 150f;
        public static readonly float[] LiftStops = { RidgeTop, SkyTop };
        public const float ShaftRadius = LegThickness - 0.1f, CarRadius = LegThickness - 0.17f, LiftDoorHalf = 0.4f;
        public static Vector3 LiftCenter => Polar(LegRadius, LiftAngle, 0f);

        // ---------------- 연결점 ----------------
        /// <summary>옆 문 깊이 (칸 중심 → 문 패널 바깥면, 하늘 면 11.95 바로 안).</summary>
        public const float DoorDepth = 3.9f;
        public static readonly (Vector3Int Cell, Vector3Int Direction)[] Doors =
        {
            (new Vector3Int(2, 0, 1), Vector3Int.right), (new Vector3Int(1, 0, 2), new Vector3Int(0, 0, 1)),
            (new Vector3Int(0, 0, 1), Vector3Int.left), (new Vector3Int(1, 0, 0), new Vector3Int(0, 0, -1)),
        };

        /// <summary>
        /// 아래 해치 9 (바닥 칸 모두 — 외형 아래 연결점): bio_export가 찾은 칸 가운데에서 가장 가까운 평평한 바닥 (1.6 m 틀 · 위 2.3 m 빔).
        /// 옆 칸 = 옹벽 밖 띠 (문 계단 옆), 귀퉁이 = 모서리 정원, 가운데 = 호수 선착장 (180°).
        /// </summary>
        public static readonly (Vector3Int Cell, Vector2 Offset)[] Hatches =
        {
            (new Vector3Int(0, 0, 0), Vector2.zero), (new Vector3Int(0, 0, 1), new Vector2(-3f, -1.8f)), (new Vector3Int(0, 0, 2), Vector2.zero),
            (new Vector3Int(1, 0, 0), new Vector2(-1.8f, -3f)), (new Vector3Int(1, 0, 1), new Vector2(-2.4f, 0f)), (new Vector3Int(1, 0, 2), new Vector2(0f, 3f)),
            (new Vector3Int(2, 0, 0), Vector2.zero), (new Vector3Int(2, 0, 1), new Vector2(3f, -1.8f)), (new Vector3Int(2, 0, 2), new Vector2(0.4f, -0.4f)),
        };

        /// <summary>위 해치 (칸 (1, 1, 1) 천장 = 외형 위 연결점): 하늘 정원 뒤 길 바깥 가장자리 위 (금빛 사다리 · 진주 고리).</summary>
        public static readonly Vector3Int UpHatchCell = new Vector3Int(1, 1, 1);
        public const float UpHatchDepth = 3.9f;
        public static readonly Vector3 UpHatchOffset = new Vector3(0f, 0f, 3f);

        // ---------------- 물 · 폭포 ----------------
        /// <summary>KWS 물 높이 (기준점 = 수면): 가운데 호수 · 앞 오른쪽 연못 · 하늘 호수. Blender 물체 Water · Water1 · Water2. 물길 · 샘 = Channels (바깥 물결 재질).</summary>
        public static readonly float[] WaterLevels = { Floor - 0.15f, Floor - 0.12f, Floor + 10.3f };

        /// <summary>폭포 기둥 (그릇 밑 물꼭지 → 호수) · 수문 폭포 셋 (샘 → 능선 물길): 떨어지는 자리 · 폭 — 물안개 · 물방울 · 물결.</summary>
        public static readonly (Vector3 Lip, Vector3 Landing, float Width)[] Falls =
        {
            (new Vector3(8f, Floor + 8.85f, 8f), new Vector3(8f, Floor - 0.15f, 8f), 1.2f),
            (Polar(RidgeOuter + 0.12f, 90f, RidgeTop + 0.21f), Polar(RidgeOuter - 0.12f, 90f, RidgeTop - 0.15f), 0.6f),
            (Polar(RidgeOuter + 0.12f, 210f, RidgeTop + 0.21f), Polar(RidgeOuter - 0.12f, 210f, RidgeTop - 0.15f), 0.6f),
            (Polar(RidgeOuter + 0.12f, 330f, RidgeTop + 0.21f), Polar(RidgeOuter - 0.12f, 330f, RidgeTop - 0.15f), 0.6f),
        };

        /// <summary>
        /// 단마다 작은 폭포 (물길 셋 × (능선 → 다섯째 단 · 단 → 아래 단 넷 · 첫째 단 → 물가 물길)): 떨어지는 자리 (아래 물길 수면) — 물보라 · 물결 (폭 0.5).
        /// Blender bio3_builder terraces() · springs()와 같은 값.
        /// </summary>
        public static Vector3[] SmallFallLandings()
        {
            var list = new System.Collections.Generic.List<Vector3>();
            foreach (float a in Streams)
            {
                list.Add(Polar(RidgeInner - 0.16f, a, TerraceTop(4) - 0.1f));
                for (int k = 0; k < 5; k++)
                    list.Add(Polar(TerraceInner(k) - 0.16f, a, k > 0 ? TerraceTop(k - 1) - 0.1f : Floor - 0.13f));
            }
            return list.ToArray();
        }

        /// <summary>물레방아 셋 (물길마다 넷째 단 → 셋째 단 작은 폭포 바로 앞, 반지름 0.32, 굴대 = 물길 가로).</summary>
        public static Vector3 WheelCenter(int stream) => Polar(TerraceInner(3) - 0.3f, Streams[stream], TerraceTop(2) + 0.12f);
        public static Vector3 WheelAxis(int stream)
        {
            float a = Streams[stream] * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        // ---------------- 체험: 물꼬 맞추기 (SluicePuzzle · InteriorSluiceGates) ----------------
        /// <summary>단 번호 n (1 = 맨 위 · 능선 쪽 … 5 = 맨 아래 · 호수 쪽) → 계단 밭 k (안 → 바깥, <see cref="TerraceInner"/>).</summary>
        public static int TerraceOfLevel(int n) => SluicePuzzle.Levels - n;
        /// <summary>수문 (능선 바깥 가장자리, 금빛 문판 · 바퀴 — Blender springs()와 같은 값). 문판은 열리면 0.5 m 올라감.</summary>
        public const float GateRadius = RidgeOuter + 0.12f, GateLift = 0.5f;
        public static Vector3 GateWheelHub(int stream) => Polar(GateRadius, Streams[stream], RidgeTop + 1.36f);
        public static Vector3 GateWheelAxis(int stream)
        {
            float a = Streams[stream] * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        /// <summary>목마른 밭 글자 자리 (물길에서 24° 옆 밭 위 1.4 m — 능선에서 맨 위 단 해바라기 (1.2 m) 너머로 보이게).</summary>
        public static Vector3 FieldLabel(int stream, int level)
        {
            int k = TerraceOfLevel(level);
            return Polar(TerraceInner(k) + 0.4f, Streams[stream] + 24f, TerraceTop(k) + 1.4f);
        }

        /// <summary>
        /// 폭포 조각 Spill{물길}_{단}이 떨어지는 자리 = 물보라 번호 (빌더가 <see cref="Falls"/> 뒤에 <see cref="SmallFallLandings"/>를 이어 붙인 배열):
        /// 단 0 = 수문 폭포 + 능선 끝 폭포, 단 n = 그 단에서 아래 단 (5단은 물가 물길)으로.
        /// </summary>
        public static int[] SpillLandings(int stream, int level)
        {
            int block = Falls.Length + stream * 6;
            return level == 0 ? new[] { 1 + stream, block } : new[] { block + 1 + TerraceOfLevel(level) };
        }

        // ---------------- 미니게임: 반딧불이 잡기 (InteriorFireflyCatch) ----------------
        /// <summary>반딧불이 숲 모서리 (뒤 오른쪽 45°): 길가 유리병 선반 (시작) · 반딧불이가 나는 곳 (가운데 · 반지름).</summary>
        public const float FireflyAngle = 45f, FireflyZoneDistance = 13.2f, FireflyZoneRadius = 2.1f;
        public static Vector3 FireflyRack
        {
            get
            {
                float a = FireflyAngle * Mathf.Deg2Rad;
                var u = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var v = new Vector3(-u.z, 0f, u.x);
                return Hub + u * 12.55f + v * 0.9f + Vector3.up * Floor;
            }
        }
        public static Vector3 FireflyZone => Polar(FireflyZoneDistance, FireflyAngle, Floor);

        /// <summary>
        /// 동물 자리 (Blender bio3_builder CRITTERS와 같은 값 — 꽃 · 잎 · 나무가 이 자리를 비킴, 사용자 2026-11-11 "동물들이랑도 겹친다"):
        /// 종류 · 반지름 · 각 · 비탈 위 (아니면 바닥 F).
        /// </summary>
        public static readonly (string Kind, float Radius, float Angle, bool OnSlope)[] Critters =
        {
            ("Deer", 9.45f, 196f, true), ("Fawn", 9.6f, 203f, true), ("Deer", 13.6f, 140f, false),
            ("Bunny", 9.5f, 122f, true), ("Bunny", 9.6f, 300f, true), ("Bunny", 12.2f, 60f, false),
        };

        /// <summary>
        /// 오리 가족 (바깥 모듈의 흰 어미 · 노란 새끼): 호수 (WaterLevels 번호) · 어미 자리 반지름 · 각 · 새끼 수 · 보는 방향 (각, Polar와 같은 기준 — x = cos · z = sin).
        /// 새끼는 어미 바로 뒤로 줄지음 (어미 → 첫 새끼 DuckLead, 새끼끼리 DuckSpacing, 번갈아 옆으로 4 cm).
        /// 가운데 호수 = 어미 + 새끼 셋 — 연잎 (수면 높이) · 갈대 · 선착장 (140 ~ 220°)이 빽빽해 가장 크게 헤엄치는 자리 (고리 1.5배 = 왕복 1.8 m, 방향 ± 4° · 자리 5 cm가
        /// 흔들려도 그대로)를 찾음 = 정문 쪽 폭포 기둥 앞 (265°, 반지름 1.2),
        /// 하늘 호수 (비어 있음) = 어미 한 쌍 + 맞은편에 어미 + 새끼 둘 (바깥 모듈은 가운데 호수에 두 가족 — 내부 호수는 둘째 가족 고리가 0.8 m뿐이었음).
        /// </summary>
        public static readonly (int Lake, float Radius, float Angle, int Kids, float Heading)[] DuckFamilies =
        {
            (0, 1.2f, 265f, 3, 150f), (2, 1.45f, 105f, 0, 15f), (2, 1.45f, 135f, 0, 225f), (2, 1.4f, 290f, 2, 20f),
        };
        /// <summary>새끼 줄 간격 (m) — CritterFx 간격 밀기 (몸 반지름 합 + 4 cm: 어미 ↔ 새끼 0.47 · 새끼끼리 0.31)보다 조금 넓게.</summary>
        public const float DuckLead = 0.5f, DuckSpacing = 0.34f;
        /// <summary>가운데 호수 헤엄 금지 반지름 (폭포 기둥 밑동 0.62 + 물보라).</summary>
        public const float FallsKeepOut = 1.05f;

        /// <summary>오리 가족 자리: 이름 (Duck · Duckling) · 템플릿 좌표 (원점 = 물높이) · y 회전 (모델 앞 = −x → 보는 방향 각 h이면 180 − h).</summary>
        public static (string Kind, Vector3 At, float Yaw)[] DuckSpots()
        {
            var list = new System.Collections.Generic.List<(string, Vector3, float)>();
            foreach (var (lake, r, deg, kids, heading) in DuckFamilies)
            {
                var home = Polar(r, deg, WaterLevels[lake]);
                float h = heading * Mathf.Deg2Rad;
                var fwd = new Vector3(Mathf.Cos(h), 0f, Mathf.Sin(h));
                var side = new Vector3(-fwd.z, 0f, fwd.x);
                list.Add(("Duck", home, 180f - heading));
                for (int k = 0; k < kids; k++)
                    list.Add(("Duckling", home - fwd * (DuckLead + DuckSpacing * k) + side * (k % 2 == 0 ? 0.04f : -0.04f), 180f - heading));
            }
            return list.ToArray();
        }

        /// <summary>숲 비탈 높이 (능선 바깥 8.6 → 옹벽 9.95, 위가 둥근 언덕).</summary>
        public static float SlopeY(float r) => Floor + 0.55f + 1.0f * Mathf.Sin(Mathf.Clamp01((WallInner - r) / (WallInner - RidgeOuter)) * Mathf.PI * 0.5f);

        /// <summary>허브 둘레 각 deg · 반지름 r · 높이 y의 템플릿 좌표.</summary>
        public static Vector3 Polar(float r, float deg, float y)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Hub.x + r * Mathf.Cos(a), y, Hub.z + r * Mathf.Sin(a));
        }

        /// <summary>허브에서 바깥을 보는 몸 방향 (Unity y 회전, 0 = +z).</summary>
        public static float OutwardYaw(float deg) => 90f - deg;
    }
}
