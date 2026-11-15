using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 3단계 궤도 항만 "진주 조개 항구 · 인어 왕국의 항구" 내부 값 (Blender op3_builder와 같은 값, 원점 = 0번 칸 중심 · 칸 8 m, 바닥 7 × 2칸 한 층):
    /// 왼쪽 = 산호 섬 위 진주 조개 궁전 · 소라 첨탑 · 섬 전망 첨탑 · 앞 인어 꼬리 분수 마당, 가운데 = 석호 (해마 가리비 배 넷이 도는 배 길) · 해마 회전목마 · 진주 모래 해변 선착장 ·
    /// 뒤 산호 방파제 (소라 문), 오른쪽 = 진주 잔교 · 산호 기중기 운전대 · 큰 진주 조개 셋 + 잡기 단 · 앵무조개 창고 · 등대 전망대.
    /// 사용자 결정 (2026-11-16): 체험 = 해마 가리비 배 뱃놀이, 미니게임 = 진주 조개 잡기 + 인어 분수 진주 던지기. 각은 x-z 평면 수학 각 (0° = +x, 90° = +z).
    /// </summary>
    public static class OrbitalPort
    {
        public const float Floor = -2.08f;
        /// <summary>석호 수면 · 바닥, 섬 풀밭 윗면 (땅 위 1.8 cm), 궁전 길 · 분수 마당 돌판 윗면, 잔교 마루.</summary>
        public const float LagoonLevel = Floor - 0.1f, SeaBed = Floor - 1.15f, LawnTop = Floor + 0.018f, PaveTop = Floor + 0.03f, PierY = Floor + 0.06f;
        /// <summary>칸 천장 (하늘 면) — 한 층이라 모두 3.95.</summary>
        public const float CeilingY = 3.95f;

        // ---------------- 산호 섬 · 궁전 · 분수 ----------------
        /// <summary>진주 조개 궁전 가운데 (단 윗면) · 둥근 벽 반지름 · 문 = +x (큰 계단 · 삼지창 문 쪽).</summary>
        public static readonly Vector3 Palace = new Vector3(4.8f, Floor + 0.6f, 4.96f);
        public const float PalaceRadius = 2.6f;
        /// <summary>인어 꼬리 분수 (마당 가운데 · 물 반지름 · 수면) · 꼬리 끝 (물줄기가 솟는 자리 — 꼬리지느러미 사이).</summary>
        public static readonly Vector3 Fountain = new Vector3(9.8f, Floor + 0.38f, -0.5f);
        public const float FountainRadius = 1.9f;
        public static readonly Vector3 FountainTip = new Vector3(9.624f, 0.42f, -0.5f);

        // ---------------- 석호 · 회전목마 · 배 ----------------
        /// <summary>해마 회전목마 (따로 조각 Carousel, 원점 = 받침 윗면 가운데 — 이 축으로 돎).</summary>
        public static readonly Vector3 Carousel = new Vector3(24f, Floor + 0.4f, 3.5f);
        /// <summary>해마 가리비 배 넷 (따로 조각 Boat{k}, 원점 = 배 가운데 수면) — 배 길 (op3_builder COURSE_CTRL, 닫힌 Catmull-Rom 44.9 m)을 넷으로 나눈 자리.</summary>
        public static readonly Vector3[] Boats =
        {
            new Vector3(24f, LagoonLevel, -0.3f), new Vector3(14.85f, LagoonLevel, 4.25f), new Vector3(24f, LagoonLevel, 7.38f), new Vector3(33.317f, LagoonLevel, 4.373f),
        };
        /// <summary>배 길 이음점 (닫힌 Catmull-Rom — 선착장 앞에서 시작해 궁전 앞 → 방파제 앞 → 동쪽으로 돎).</summary>
        public static readonly Vector2[] CourseControl =
        {
            new Vector2(24f, -0.3f), new Vector2(18f, 0.45f), new Vector2(15.2f, 2.9f), new Vector2(15.2f, 6.3f), new Vector2(18.4f, 7.25f),
            new Vector2(24f, 7.38f), new Vector2(29.8f, 7.35f), new Vector2(33.2f, 5.8f), new Vector2(32.6f, 2.6f), new Vector2(29.8f, 0.45f),
        };
        /// <summary>선착장 (해변 → 배 길 앞 끝 진주 마루, 가운데 · 마루 윗면).</summary>
        public static readonly Vector3 Stop = new Vector3(24f, Floor + 0.06f, -1.6f);

        // ---------------- 큰 진주 조개 셋 · 기중기 (미니게임 진주 조개 잡기) ----------------
        /// <summary>조개 셋 (가운데 x, z · 입이 보는 쪽 수학 각) · 잡기 단 (반원 마루 가운데, 윗면 Floor + 0.04).</summary>
        public static readonly Vector3[] Clams = { new Vector3(38.6f, 6.55f, 210f), new Vector3(42f, 6.9f, 270f), new Vector3(45.4f, 6.55f, 330f) };
        public static readonly Vector3 ClamStage = new Vector3(42f, Floor + 0.04f, 3.75f);
        /// <summary>
        /// 조개 경첩 · 입술 높이 (수면 위 0.53 — 아래 껍데기 안 바닥이 수면 위 4 cm, 수면에 닿은 바위 받침 위: 사용자 2026-11-16 "조개가 물속에 파묻혀 있음") ·
        /// 조개 뚜껑 (따로 조각 ClamLid{k}, 원점 = 경첩 — 52° 열린 채 내보냄) · 조개 속 진주 (따로 조각 ClamPearl{k}, 원점 = 진주 가운데 — 그릇 안 바닥에 얹힘, 꼭대기가 입술 위 10 cm).
        /// </summary>
        public const float ClamY = LagoonLevel + 0.53f;
        public static readonly Vector3[] ClamHinges = { new Vector3(39.379f, ClamY, 7.0f), new Vector3(42f, ClamY, 7.8f), new Vector3(44.621f, ClamY, 7.0f) };
        public static readonly Vector3[] ClamPearls = { new Vector3(38.6f, ClamY - 0.2f, 6.55f), new Vector3(42f, ClamY - 0.2f, 6.9f), new Vector3(45.4f, ClamY - 0.2f, 6.55f) };
        public const float ClamLidOpen = 52f;
        /// <summary>산호 기중기 운전대 마루 (x0, x1, z0, z1 · 윗면 — 잔교 위를 가로질러 밑으로 지나감).</summary>
        public static readonly Vector4 CraneDeck = new Vector4(37f, 40.2f, 8.25f, 11.85f);
        public const float CraneDeckY = Floor + 2.36f;

        // ---------------- 전망대 둘 (위 해치 자리) ----------------
        /// <summary>섬 전망 첨탑 · 등대 전망대 가운데 (x, z) · 원판 윗면 · 원판 안 (첨탑) · 바깥 반지름.</summary>
        public static readonly Vector2 IsleLookout = new Vector2(9.4f, 8.2f), LightLookout = new Vector2(48.4f, 1.6f);
        public const float LookoutDeckY = Floor + 2.05f, IsleDeckRadius = 1.75f, LightDeckRadius = 1.6f;

        // ---------------- 물 ----------------
        /// <summary>석호 수면 · 분수 수면 (op3_builder Water · Water1).</summary>
        public static readonly float[] WaterLevels = { LagoonLevel, Floor + 0.38f };

        // ---------------- 연결점 ----------------
        /// <summary>스폰 = 선착장 뒤 진주 모래 해변 (선착장 · 배 · 회전목마 · 방파제 소라 문을 봄).</summary>
        public static readonly Vector3 Spawn = new Vector3(24f, Floor, -3.3f);
        public const float SpawnYaw = 0f;
        /// <summary>옆 문 깊이 (칸 중심 → 문 패널 바깥면, 하늘 면 바로 안).</summary>
        public const float DoorDepth = 3.9f;
        /// <summary>옆 문 넷 = 외형 연결점: 오른쪽 끝 칸 (6, 0, 0) 오른쪽 · 앞, (6, 0, 1) 오른쪽 (잔교 끝), 칸 (1, 0, 1) 뒤 (방파제 섬 쪽).</summary>
        public static readonly (Vector3Int Cell, Vector3Int Direction)[] Doors =
        {
            (new Vector3Int(6, 0, 0), new Vector3Int(1, 0, 0)), (new Vector3Int(6, 0, 0), new Vector3Int(0, 0, -1)),
            (new Vector3Int(6, 0, 1), new Vector3Int(1, 0, 0)), (new Vector3Int(1, 0, 1), new Vector3Int(0, 0, 1)),
        };
        /// <summary>
        /// 아래 해치 열넷 (바닥 칸 모두, op3_builder HATCHES — op_export에서 1.6 m 틀 9 × 9 광선이 한 판 (4 mm, 없으면 2 cm) · 위 2.3 m 빔 · 몸 높이 0.55 m 안 빔):
        /// 칸 · 가운데에서 자리 · 바닥 높이 (섬 풀밭 · 해변 · 방파제 · 잔교).
        /// </summary>
        public static readonly (Vector3Int Cell, Vector2 Offset, float FloorY)[] Hatches =
        {
            (new Vector3Int(0, 0, 0), new Vector2(-0.4f, 2.8f), LawnTop), (new Vector3Int(0, 0, 1), new Vector2(0f, -0.2f), Floor),
            (new Vector3Int(1, 0, 0), new Vector2(-2.4f, 0.6f), LawnTop), (new Vector3Int(1, 0, 1), new Vector2(-1.0f, 2.0f), Floor),
            (new Vector3Int(2, 0, 0), new Vector2(-1.4f, -2.4f), Floor), (new Vector3Int(2, 0, 1), new Vector2(0.4f, 2.8f), Floor),
            (new Vector3Int(3, 0, 0), new Vector2(-2.2f, -2.8f), Floor), (new Vector3Int(3, 0, 1), new Vector2(0f, 2.4f), Floor),
            (new Vector3Int(4, 0, 0), new Vector2(0.8f, -1.4f), Floor), (new Vector3Int(4, 0, 1), new Vector2(0f, 2.4f), Floor),
            (new Vector3Int(5, 0, 0), new Vector2(-0.2f, 0f), Floor), (new Vector3Int(5, 0, 1), new Vector2(3.0f, 1.2f), PierY),
            (new Vector3Int(6, 0, 0), new Vector2(0.8f, -2.2f), Floor), (new Vector3Int(6, 0, 1), new Vector2(-0.4f, 1.2f), PierY),
        };
        /// <summary>위 해치 셋 (외형 위 연결점): 섬 전망 첨탑 원판 (1, 0, 1) · 기중기 운전대 마루 (5, 0, 1) · 등대 전망대 원판 (6, 0, 0) — 마루 위 ± 0.5 m가 한 판, 난간 · 기둥과 0.4 m.</summary>
        public static readonly (Vector3Int Cell, Vector2 Offset)[] UpHatches =
        {
            (new Vector3Int(1, 0, 1), new Vector2(0.3f, 0f)), (new Vector3Int(5, 0, 1), new Vector2(-0.55f, 0.85f)), (new Vector3Int(6, 0, 0), new Vector2(0.25f, 0.65f)),
        };
        public const float UpHatchDepth = 3.9f;

        /// <summary>위 해치 k 밑 마루 높이 (전망대 원판 · 운전대 마루).</summary>
        public static float UpHatchFloor(int k) => UpHatches[k].Cell == new Vector3Int(5, 0, 1) ? CraneDeckY : LookoutDeckY;
    }
}
