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
        // ---------------- 체험 "등대 빛 돌리기" (InteriorLighthouse · LighthouseBeam, 사용자 2026-11-13 "비추면 반응") ----------------
        /// <summary>렌즈 손잡이 쉴 때 방향 (Unity yaw) — od3_builder lighthouse() 손잡이 = 등실 문 맞은편 (수학 각 6.3°). 앞 빛줄기 = 손잡이 쪽.</summary>
        public const float LensHandleYaw = 83.7f;
        /// <summary>손잡이 끝 구슬 (렌즈 가운데에서 바깥 · 아래, m).</summary>
        public const float HandleReach = 0.9f, HandleDrop = 0.38f;
        /// <summary>
        /// 빛줄기가 아래로 기운 각 (도) · 시작 (렌즈 수정 고리 바깥, 렌즈 가운데에서 m) · 시작 지름 (렌즈 높이) · 끝 지름 · 가장 긴 길이 (빛줄기를 따라, m).
        /// 진짜 등대처럼 거의 수평 (2026-11-13 사용자: 45°였을 때 빛이 수정이 아니라 회랑 바닥 밑에서 나왔고, 손잡이를 잡으면 빛이 안 보였음):
        /// 렌즈에서 나와 등실 유리 (턱 위)와 회랑 끝 (<see cref="GalleryEdge"/>) 위로 지나 밤하늘에 뻗다 스러짐 — 바다에는 닿지 않고, 빛이 지나가는 쪽의 곳이 답함 (먼 바다의 배를 비추는 등대처럼).
        /// </summary>
        public const float BeamPitch = 10f, BeamStart = 0.46f, BeamStartDiameter = 0.6f, BeamEndDiameter = 1.8f, BeamLength = 9f;
        /// <summary>등실 유리 아래 진주 턱 위 · 회랑 바닥 바깥 끝 (렌즈 가운데에서, 실측).</summary>
        public const float LampSillTop = GalleryY + 0.25f, GalleryEdge = 2.4f;
        /// <summary>방 하늘 면 (칸 상자): 벽 x · z, 바다 쪽 아래 칸 천장 턱 (x <see cref="LowRoomFromX"/>부터 천장 <see cref="LowCeilingY"/>).</summary>
        public const float RoomMinX = -3.95f, RoomMaxX = 35.95f, RoomMinZ = -3.95f, RoomMaxZ = 11.95f, LowRoomFromX = 11.95f, LowCeilingY = 3.95f;
        /// <summary>빛줄기 끝 고리 바깥 ~ 하늘 면 (벽에 수직, m) — 하늘 면에 잘린 끝 대신 셰이더 끝 흐림으로 스러짐.</summary>
        public const float BeamWallGap = 0.2f;

        /// <summary>
        /// 빛줄기 길이 (렌즈 가운데에서 빛줄기를 따라, m): <see cref="BeamLength"/>, 하늘 면 (벽 · 아래 칸 천장 턱)이 먼저면 끝 고리가 그 <see cref="BeamWallGap"/> 앞에서 끝남.
        /// 뒤쪽 벽을 비출 때 빛기둥이 하늘 면에 칼로 자른 듯 끊겼음 (2026-11-13 플레이).
        /// </summary>
        public static float BeamReach(float yaw)
        {
            float tan = Mathf.Tan(BeamPitch * Mathf.Deg2Rad);
            float dx = Mathf.Sin(yaw * Mathf.Deg2Rad), dz = Mathf.Cos(yaw * Mathf.Deg2Rad);
            float m = BeamEndDiameter * 0.5f + BeamWallGap;   // 끝 고리가 비스듬히 닿아도 옆이 하늘 면에 걸리지 않게 (벽에 수직으로 잼)
            float wall = Mathf.Min(Cross(LensCenter.x, dx, RoomMinX + m, RoomMaxX - m), Cross(LensCenter.z, dz, RoomMinZ + m, RoomMaxZ - m));
            if (dx > 1e-4f && LensCenter.y - (LowRoomFromX - LensCenter.x) / dx * tan > LowCeilingY)
                wall = Mathf.Min(wall, (LowRoomFromX - m - LensCenter.x) / dx);
            float cos = Mathf.Cos(BeamPitch * Mathf.Deg2Rad);
            return Mathf.Clamp(wall / cos, BeamStart + 1f, BeamLength);
        }

        /// <summary>빛줄기 반지름 (빛줄기를 따라 s m, 길이 reach) — LightBeamFx와 같이 시작 → 끝 지름을 곧게 이음.</summary>
        public static float BeamRadius(float s, float reach) =>
            Mathf.Lerp(BeamStartDiameter, BeamEndDiameter, Mathf.Clamp01((s - BeamStart) / (reach - BeamStart))) * 0.5f;

        /// <summary>수평 빛이 한 축 [min, max] 밖으로 나가는 거리.</summary>
        private static float Cross(float from, float dir, float min, float max) =>
            dir > 1e-4f ? (max - from) / dir : dir < -1e-4f ? (min - from) / dir : float.MaxValue;
        /// <summary>
        /// 빛이 닿으면 답하는 곳 (템플릿 기준 — 방향 = 렌즈에서 본 yaw, 폭은 거리로): 먼 부표 수정 · 포대 수정 · 매인 배 · 방파제 끝 등불대 · 하늘 다리 꽃 둘.
        /// 모두 손잡이를 잡은 자리 (등실)에서 회랑 바닥 끝에 가리지 않고 보이는 곳 (가까운 부표 · 폭포 · 오두막은 회랑 밑이라 빠짐). 순서 = InteriorLighthouse가 붙이는 반응 순서.
        /// </summary>
        public static readonly (string Name, Vector3 At)[] BeamTargets =
        {
            ("부표 수정", new Vector3(28.96f, SeaLevel + 0.9f, 2.72f)),
            ("포대 수정", new Vector3(22.45f, Floor + 1.3f, 5.05f)),
            ("매인 배", new Vector3(20.6f, SeaLevel + 0.2f, 1.2f)),
            ("방파제 등불대", new Vector3(33.95f, Floor + 2.78f, 6.6f)),
            ("하늘 다리 동쪽 꽃", new Vector3(7.75f, SkywalkY + 1.0f, 9.2f)),
            ("하늘 다리 서쪽 꽃", new Vector3(0.8f, SkywalkY + 1.0f, 9.2f)),
        };
        /// <summary>곳 폭 (± 도) = 반지름 0.9 m가 보이는 각, 2.5 ~ 8°.</summary>
        public static float BeamTargetWidth(Vector3 at)
        {
            var d = new Vector2(at.x - LensCenter.x, at.z - LensCenter.z).magnitude;
            return Mathf.Clamp(Mathf.Atan2(0.9f, d) * Mathf.Rad2Deg, 2.5f, 8f);
        }
        public static float BeamTargetYaw(Vector3 at) => Mathf.Atan2(at.x - LensCenter.x, at.z - LensCenter.z) * Mathf.Rad2Deg;

        // ---------------- 체험 "오로라 보호막 펼치기" (InteriorAuroraShield · AuroraShieldShow, 사용자 2026-11-13 "레버 → 보호막 쇼 관람") ----------------
        /// <summary>방파제 끝 레버 굴대 (AuroraLever 원점 = od3_builder mole() piv) · 당긴 각 (템플릿 x축, 도 — 끝이 −z 쪽으로 눕음).</summary>
        public static readonly Vector3 AuroraLever = new Vector3(31.25f, Floor + 1.06f, 4.7f);
        public const float LeverPull = -50f;
        /// <summary>
        /// 보호막 = 바다 위 납작한 반구 (타원체 위 절반, 바깥 3단계 실드와 같은 오로라 구면 파문 + 수정 결정 막): 가운데 (수면) · 반지름 x · y · z.
        /// 꼭대기 2.2 m (바다 쪽 아래 칸 천장 3.95 밑 — 별똥별이 떨어질 틈), 동쪽 발 = 레버 바로 안쪽 (레버 자리에서 퍼져 나감), 선베드는 밖에서 올려다봄.
        /// </summary>
        public static readonly Vector3 ShieldCenter = new Vector3(21.4f, SeaLevel, 4.6f);
        public static readonly Vector3 ShieldRadii = new Vector3(9.4f, 3.6f, 5.4f);
        /// <summary>선베드에 누운 자리 (= 주민 누운 자리, 엉덩이) · 눈 높이 · 올려다보는 각 (음수 = 위).</summary>
        public const float LoungerSeatY = Floor + 0.365f, LoungerEye = 0.5f, LoungerPitch = -24f;

        /// <summary>단위 구 방향 → 보호막 면 위 점 (템플릿 기준).</summary>
        public static Vector3 ShieldPoint(Vector3 unit) => ShieldCenter + Vector3.Scale(unit, ShieldRadii);

        /// <summary>보호막 면 위 점의 바깥 법선.</summary>
        public static Vector3 ShieldNormal(Vector3 point)
        {
            var d = point - ShieldCenter;
            return new Vector3(d.x / (ShieldRadii.x * ShieldRadii.x), d.y / (ShieldRadii.y * ShieldRadii.y), d.z / (ShieldRadii.z * ShieldRadii.z)).normalized;
        }

        /// <summary>점이 보호막 안 (수면 위 반구 속)인지.</summary>
        public static bool InsideShield(Vector3 point)
        {
            var d = point - ShieldCenter;
            if (d.y < 0f)
                return false;
            float x = d.x / ShieldRadii.x, y = d.y / ShieldRadii.y, z = d.z / ShieldRadii.z;
            return x * x + y * y + z * z < 1f;
        }

        /// <summary>
        /// 별똥별 출발점: 막 위 닿는 자리 target에서 들어오는 반대 방향 incoming (위쪽 · 하늘 면 쪽)으로 가장 길게 maxLength,
        /// 바다 쪽 아래 칸 천장 · 벽 (하늘 면) 앞 0.1 m에서 멈춤 — 하늘 면에서 나와 막에 떨어지는 것처럼. 반환 = 출발점 (거리가 0.5 m 아래면 target).
        /// </summary>
        public static Vector3 MeteorStart(Vector3 target, Vector3 incoming, float maxLength)
        {
            var d = incoming.normalized;
            float len = maxLength;
            if (d.y > 1e-4f)
                len = Mathf.Min(len, (LowCeilingY - 0.1f - target.y) / d.y);
            len = Mathf.Min(len, Cross(target.x, d.x, LowRoomFromX + 0.1f, RoomMaxX - 0.1f), Cross(target.z, d.z, RoomMinZ + 0.1f, RoomMaxZ - 0.1f));
            return len < 0.5f ? target : target + d * len;
        }

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
