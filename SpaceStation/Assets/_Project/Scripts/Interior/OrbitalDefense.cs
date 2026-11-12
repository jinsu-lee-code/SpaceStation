using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 3단계 궤도 방어망 "등대 곶 해안" 내부 값 (Blender od3_builder와 같은 값, 원점 = 0번 칸 중심 · 칸 8 m, 바닥 5 × 2칸 + 왼쪽 위 2 × 2):
    /// 왼쪽 층층 바위 곶 (윗면 = 진주 등대 · 관리인 오두막 · 망원경 · 개울 → 뒤 오른쪽 폭포, 앞면 = 절벽에 판 진주 계단, 앞 오른쪽 = 큰 바다 동굴) ·
    /// 앞 모래 해변 (진주 잔교 · 선베드) · 바다 (수정 부표 셋 · 가운데 = 포대 자리) · 뒤 방파제 (걸어 다니는 둑, 끝 등불대 = 오로라 레버) · 오른쪽 끝 조수 웅덩이.
    /// 위 문 둘 (곶 뒤 칸 왼쪽 · 오른쪽, 바닥 6.7) = 곶 뒤 하늘 다리, 등대 바깥 나선 계단이 다리 · 꼭대기 발코니로 이어짐.
    /// 각은 x-z 평면 수학 각 (0° = +x, 90° = +z, 270° = −z = 앞).
    /// </summary>
    public static class OrbitalDefense
    {
        public const float Floor = -1.3f;
        /// <summary>바다 수면 (해변 바닥보다 10 cm 아래) · 곶 윗면 개울 · 조수 웅덩이 (od3_builder WATER_LEVELS — Water · Water1 · Water2).</summary>
        public static readonly float[] WaterLevels = { Floor - 0.1f, 3.7f, -1.23f };
        public const float SeaLevel = Floor - 0.1f;

        // ---------------- 곶 · 등대 ----------------
        public static readonly Vector3 CapeCenter = new Vector3(3.68f, Floor, 4.16f);
        /// <summary>곶 윗면 풀밭 높이.</summary>
        public const float CapeTop = 3.76f;
        public static readonly Vector3 Lighthouse = new Vector3(4.32f, CapeTop, 5.6f);
        /// <summary>꼭대기 발코니 · 등실 바닥 · 렌즈 가운데 (도는 조각 Lens 원점) · 등실 유리 반지름.</summary>
        public const float GalleryY = 8.6f, LampRadius = 1.32f;
        public static readonly Vector3 LensCenter = new Vector3(4.32f, 9.65f, 5.6f);
        /// <summary>하늘 다리 바닥 (= 위 칸 바닥, 위 문 둘).</summary>
        public const float SkywalkY = Floor + 8f;
        /// <summary>망원경 머리 (ScopeHead 원점) · 쉬는 방향 (Unity yaw, 앞 오른쪽 바다).</summary>
        public static readonly Vector3 ScopeHead = new Vector3(7.55f, CapeTop + 1.22f, 3.3f);
        public const float ScopeYaw = 121.4f;
        /// <summary>큰 바다 동굴 길 (입구 바깥 → 안쪽 끝, 바닥 = Floor 모래톱).</summary>
        public static readonly Vector2[] CavePath =
        {
            new Vector2(10.42f, 0.86f), new Vector2(9.17f, 1.48f), new Vector2(7.82f, 2.36f), new Vector2(6.96f, 3.34f), new Vector2(6.37f, 4.41f), new Vector2(5.96f, 5.39f),
        };
        /// <summary>곶 윗면 개울이 뒤 오른쪽 절벽으로 떨어지는 폭포 (물줄기 판 Falls — 지층 턱마다 흘러 넘음, 2026-11-13) — 바다 웅덩이에 닿는 자리 · 폭.</summary>
        public static readonly Vector3 FallsLanding = new Vector3(7.76f, SeaLevel, 9.62f);
        public const float FallsWidth = 1.1f;

        // ---------------- 해변 · 바다 ----------------
        /// <summary>진주 잔교 (x · 끝 z) · 가운데 수정 부표 포대 (미니게임 "별똥별 막기") · 장식 부표 둘.</summary>
        public const float PierX = 19.05f, PierEndZ = 3.5f;
        public static readonly Vector3 Turret = new Vector3(22.45f, Floor + 0.12f, 5.05f);
        public const float TurretRadius = 1.75f;
        public static readonly Vector3[] Buoys = { new Vector3(14.8f, SeaLevel, 3.2f), new Vector3(28.96f, SeaLevel, 2.72f) };
        /// <summary>방파제 끝 등불대 참 (가운데 · 반지름, 윗면 = 길과 같은 Floor + 0.12 — 2026-11-13, 턱이 참 둘레를 한 줄로 감음) — 오로라 레버 받침이 앞 왼쪽 가장자리.</summary>
        public static readonly Vector3 MoleEnd = new Vector3(32.5f, Floor + 0.12f, 5.7f);
        public const float MoleEndRadius = 2.3f;
        public const float MoleTop = Floor + 0.12f;
        /// <summary>선베드 셋 (오로라 관람, 바다 = +z를 봄).</summary>
        public static readonly float[] Loungers = { 25.4f, 27.4f, 29.4f };
        public const float LoungerZ = -2.4f;
        /// <summary>조수 웅덩이 셋 (가운데 · 반지름 x · z).</summary>
        public static readonly Vector4[] TidePools = { new Vector4(33.9f, -0.5f, 0.95f, 0.7f), new Vector4(35.0f, 1.5f, 0.75f, 0.55f), new Vector4(34.8f, -2.7f, 0.7f, 0.55f) };

        // ---------------- 연결점 ----------------
        /// <summary>스폰 = 해변 (잔교 옆, 바다 · 곶 · 등대를 봄).</summary>
        public static readonly Vector3 Spawn = new Vector3(21.6f, Floor, -1.8f);
        public const float SpawnYaw = 320f;
        /// <summary>옆 문 깊이 (칸 중심 → 문 패널 바깥면, 하늘 면 바로 안).</summary>
        public const float DoorDepth = 3.9f;
        /// <summary>
        /// 옆 문 여덟 = 외형 연결점: 곶 둘레 아래 여섯 (왼쪽 둘 · 앞 둘 · 뒤 둘 — 자갈 해안 · 해변) + 위 둘 (곶 뒤 칸 왼쪽 · 오른쪽 면, 바닥 6.7 = 하늘 다리 양 끝).
        /// </summary>
        public static readonly (Vector3Int Cell, Vector3Int Direction)[] Doors =
        {
            (new Vector3Int(0, 0, 0), new Vector3Int(-1, 0, 0)), (new Vector3Int(0, 0, 0), new Vector3Int(0, 0, -1)), (new Vector3Int(1, 0, 0), new Vector3Int(0, 0, -1)),
            (new Vector3Int(0, 0, 1), new Vector3Int(-1, 0, 0)), (new Vector3Int(0, 0, 1), new Vector3Int(0, 0, 1)), (new Vector3Int(1, 0, 1), new Vector3Int(0, 0, 1)),
            (new Vector3Int(0, 1, 1), new Vector3Int(-1, 0, 0)), (new Vector3Int(1, 1, 1), new Vector3Int(1, 0, 0)),
        };
        /// <summary>
        /// 아래 해치 열 (바닥 칸 모두 — 외형 아래 연결점, od3_builder HATCHES · od_export에서 1.6 m 평평 · 위 2.3 m 빔 확인): 칸 · 가운데에서 자리 · 바닥 높이
        /// (곶 둘레 자갈 해안 · 앞 모래 후미 · 해변 · 방파제 윗면 · 끝 등불대 참).
        /// </summary>
        public static readonly (Vector3Int Cell, Vector2 Offset, float FloorY)[] Hatches =
        {
            (new Vector3Int(0, 0, 0), new Vector2(-1.2f, -2.4f), Floor), (new Vector3Int(0, 0, 1), new Vector2(-0.6f, 2.4f), Floor),
            (new Vector3Int(1, 0, 0), new Vector2(0.6f, -1.4f), Floor), (new Vector3Int(1, 0, 1), new Vector2(2.2f, 2.4f), MoleTop),
            (new Vector3Int(2, 0, 0), new Vector2(0.0f, -2.4f), Floor), (new Vector3Int(2, 0, 1), new Vector2(0.0f, 2.4f), MoleTop),
            (new Vector3Int(3, 0, 0), new Vector2(-0.8f, -2.6f), Floor), (new Vector3Int(3, 0, 1), new Vector2(0.8f, 1.8f), MoleTop),
            (new Vector3Int(4, 0, 0), new Vector2(0.0f, -2.2f), Floor), (new Vector3Int(4, 0, 1), new Vector2(0.5f, -2.3f), MoleTop),
        };
        /// <summary>위 해치 셋 (외형 위 연결점): 곶 뒤 칸 둘 천장 = 하늘 다리 위, 칸 (2, 0, 1) 천장 = 방파제 위.</summary>
        public static readonly (Vector3Int Cell, Vector2 Offset)[] UpHatches =
        {
            (new Vector3Int(0, 1, 1), new Vector2(0.6f, 1.08f)), (new Vector3Int(1, 1, 1), new Vector2(0.68f, 0.55f)), (new Vector3Int(2, 0, 1), new Vector2(0.1f, 2.68f)),
        };
        public const float UpHatchDepth = 3.9f;
    }
}
