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

        // ---------------- 미니게임 "별똥별 막기" (InteriorStarfall · StarfallGame, 사용자 2026-11-13 "포대에 앉아 조준 · 발사") ----------------
        /// <summary>포수 의자 앉는 면 (od3_builder turret() — 포대 가운데에서 북동 1.1 m) · 바라보는 쪽 (yaw, 바다 오른쪽 뒤) · 눈 높이 · 처음 시선 (음수 = 위).</summary>
        public static readonly Vector3 GunSeat = new Vector3(23.16f, Floor + 0.52f, 5.89f);
        /// <summary>
        /// 처음 시선 = 대각선 위 30° (사용자 2026-11-13 "너무 아래쪽에서 운석이 시작해 — 좀 더 위쪽을 보면서 포격으로 막는 느낌, 대각선으로 막는 정도의 시야각").
        /// 눈 0.72는 포대 테 난간 손잡이와 같은 높이라 화면 가운데를 가로막았음.
        /// </summary>
        public const float GunSeatYaw = 40f, GunEye = 1.0f, GunPitch = -30f;
        /// <summary>
        /// 앉은 처음 화면 안 (시야각 60° · 4:3 화면에서 여유 4°): 시선 가운데에서 좌우 ± ViewHalfYaw · 위아래 ± ViewHalfPitch.
        /// 별똥별 출발과 길 앞쪽은 이 안에서만 (사용자 2026-11-13 "앉은 시야 밖으로 떨어지는 별똥별도 있어서 놓치기 쉬움").
        /// </summary>
        public const float ViewHalfYaw = 32f, ViewHalfPitch = 26f;
        /// <summary>포 끝 = 포대 수정 무리 가운데 큰 수정 꼭짓점 (Turret 원점 받침 윗면 + 1.9).</summary>
        public static readonly Vector3 TurretMuzzle = new Vector3(22.45f, Floor + 2.28f, 5.05f);
        /// <summary>
        /// 별똥별 출발 = 바다 쪽 하늘 (아래 칸 천장 하늘 면) 바로 밑, 포수 시선에서 좌우 ± StarfallStartYaw · 올려본 각 StarfallStartElevMin ~ Max (대각선 위);
        /// 떨어질 자리 = 포수에게서 좌우 ± StarfallLandYaw · StarfallLandNear ~ Far m 바다 (벽에서 2 m 안) — 앞쪽 바다로 비스듬히 떨어짐 (위에서 맞혀 막음).
        /// </summary>
        public const float StarfallStartYaw = 26f, StarfallStartElevMin = 30f, StarfallStartElevMax = 48f;
        public const float StarfallLandYaw = 30f, StarfallLandNear = 7f, StarfallLandFar = 12f;

        /// <summary>포수 눈 (앉은 자리 + 눈 높이).</summary>
        public static Vector3 GunEyePoint => GunSeat + Vector3.up * GunEye;

        /// <summary>포수 시선에서 yawOffset (도) · 올려본 각 elevation (도) 쪽의 바다 쪽 하늘 면 바로 밑 출발점.</summary>
        public static Vector3 StarfallStart(float yawOffset, float elevation)
        {
            var eye = GunEyePoint;
            float y = LowCeilingY - 0.1f;
            float h = (y - eye.y) / Mathf.Tan(Mathf.Max(5f, elevation) * Mathf.Deg2Rad);
            float a = (GunSeatYaw + yawOffset) * Mathf.Deg2Rad;
            return new Vector3(eye.x + Mathf.Sin(a) * h, y, eye.z + Mathf.Cos(a) * h);
        }

        /// <summary>포수에게서 yawOffset (도) · dist (m) 떨어진 수면 자리 (벽에서 2 m 안으로 당김).</summary>
        public static Vector3 StarfallLanding(float yawOffset, float dist)
        {
            float a = (GunSeatYaw + yawOffset) * Mathf.Deg2Rad;
            float x = Mathf.Clamp(GunSeat.x + Mathf.Sin(a) * dist, LowRoomFromX + 2f, RoomMaxX - 2f);
            float z = Mathf.Clamp(GunSeat.z + Mathf.Cos(a) * dist, RoomMinZ + 2f, RoomMaxZ - 2f);
            return new Vector3(x, SeaLevel, z);
        }

        /// <summary>점이 바다 쪽 아래 칸 안 (벽에서 0.3 m 안)인지.</summary>
        public static bool InsideLowRoom(Vector3 p) =>
            p.x > LowRoomFromX + 0.3f && p.x < RoomMaxX - 0.3f && p.z > RoomMinZ + 0.3f && p.z < RoomMaxZ - 0.3f && p.y < LowCeilingY;

        /// <summary>점이 앉은 처음 화면 안인지 (위아래는 margin만큼 더 안쪽).</summary>
        public static bool StarfallVisible(Vector3 point, float margin = 0f)
        {
            var d = point - GunEyePoint;
            float yaw = Mathf.DeltaAngle(GunSeatYaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            float el = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            return Mathf.Abs(yaw) < ViewHalfYaw && Mathf.Abs(el + GunPitch) < ViewHalfPitch - margin;
        }

        /// <summary>
        /// 별똥별 길이 앉은 처음 화면 (대각선 위)에서 보이는지: 출발은 방 안 · 화면 안쪽 (위아래 여유 8°),
        /// 길 앞 60%는 화면 좌우 안 · 위 끝 아래 (그 뒤는 화면 아래로 빠져도 됨 — 위에서 맞혀 막음).
        /// </summary>
        public static bool StarfallPathVisible(Vector3 from, Vector3 to)
        {
            if (!InsideLowRoom(from) || !StarfallVisible(from, 8f))
                return false;
            var eye = GunEyePoint;
            for (int k = 1; k <= 4; k++)
            {
                var d = Vector3.Lerp(from, to, 0.15f * k) - eye;
                float yaw = Mathf.DeltaAngle(GunSeatYaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
                float el = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                if (Mathf.Abs(yaw) >= ViewHalfYaw || el + GunPitch >= ViewHalfPitch)
                    return false;
            }
            return true;
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

        // ---------------- 바다 동물 (critter_builder 바다 동물, 사용자 2026-11-13 "좋아 다음으로 넘어가자" — 물범 · 돌고래 · 바닷새 · 거북 · 게) ----------------
        // (모델, 자리 (모델 원점), 바라보는 쪽 수학 각). 땅 · 바위 위 자리 = 몸 밑 닿는 점 (물범 = 배 · 지느러미 · 엉덩이 밑, 새 = 발바닥, 게 = 다리 끝)이 모두 면에서 1.5 cm 안이 되게
        //  자리 · 높이를 찾은 값 (원점 한 점만 면에 맞췄더니 둥근 바위 위 물범 배 · 꼬리가 최대 11 cm 떠 있었음, 사용자 2026-11-13) — OrbitalDefenseTests.Critters_OnTheirSpots가 지킴.
        /// <summary>
        /// 물범 바위 둘 (od3_builder 물범 바위 — 바다 가장자리): 큰 바위 = 어른 물범 (해변 · 잔교 쪽을 봄), 작은 바위 = 하얀 아기 물범.
        /// 둥근 바위에 둥근 배라 한 점에서만 닿음 → 어른 물범은 꼭대기에서 10 cm 옆으로 · 배 가운데가 바위 속 6.6 cm (배 둘레 · 앞지느러미가 바위에 닿아 퍼져 누운 모양).
        /// </summary>
        public static readonly (string Kind, Vector3 At, float Facing)[] Seals =
        {
            ("Seal", new Vector3(32.55f, -0.917f, 1.5f), 200f), ("SealPup", new Vector3(35.05f, -0.983f, 3.4f), 215f),
        };
        /// <summary>게 넷 = 물가 모래 (주민 자리 · 선베드 · 잔교에서 1.2 m 넘게): 해변 쪽을 보고 물가를 따라 (x) 옆걸음 — 그 길 위 모래 높이 차 1 cm 안.</summary>
        public static readonly (string Kind, Vector3 At, float Facing)[] Crabs =
        {
            ("Crab", new Vector3(31.2f, -1.31f, -1.6f), 268f), ("Crab", new Vector3(24.2f, -1.345f, -1.4f), 274f),
            ("Crab", new Vector3(20.6f, -1.383f, -1.0f), 275f), ("Crab", new Vector3(14.3f, -1.377f, -1.1f), 265f),
        };
        /// <summary>돌고래 둘 = 깊은 바다 (2.1 m 넘게): 처음 자리 = 뛰어오른 한가운데 (수면 + 0.2), 바라보는 쪽으로 2.4 m를 0.7 m 높이로 뜀 (CritterFx Leap × 34).</summary>
        public static readonly (string Kind, Vector3 At, float Facing)[] Dolphins =
        {
            ("Dolphin", new Vector3(16.6f, SeaLevel + 0.2f, 6.3f), 0f), ("Dolphin", new Vector3(26.2f, SeaLevel + 0.2f, 4.6f), -17f),
        };
        /// <summary>바다거북 둘 = 얕은 바다 수면 (원점 = 등딱지 가장자리 = 수면): 잔교 · 배 · 부표 · 물가를 비키는 고리로 천천히 헤엄.</summary>
        public static readonly (string Kind, Vector3 At, float Facing)[] Turtles =
        {
            ("Turtle", new Vector3(16.4f, SeaLevel, 1.8f), 30f), ("Turtle", new Vector3(25.6f, SeaLevel, 1.6f), 160f),
        };
        /// <summary>퍼핀 넷 = 방파제 앞 바다 바위 셋 · 조수 웅덩이 바위 하나 (둘레가 트여 휙 돌아봄).</summary>
        public static readonly (string Kind, Vector3 At, float Facing)[] Puffins =
        {
            ("Puffin", new Vector3(21.38f, -1.212f, 8.6f), 250f), ("Puffin", new Vector3(23.7f, -1.167f, 8.35f), 280f),
            ("Puffin", new Vector3(24.4f, -1.163f, 8.2f), 230f), ("Puffin", new Vector3(34.6f, -1.05f, 2.2f), 200f),
        };
        /// <summary>
        /// 곶 동쪽 지층 풀 턱의 퍼핀 둘 (바닷새 둥지 자리, 2.83 · 1.99 m): 턱 폭이 0.3 m (벽 0.11 ~ 0.13 · 끝 0.15 ~ 0.18)라 바깥에서 40° 비스듬히 앉아
        /// 숨 쉬기 · 고개 들기만 (돌아보면 부리 · 꽁지가 바위에 묻힘 — CritterFx Bask).
        /// </summary>
        public static readonly (string Kind, Vector3 At, float Facing)[] LedgePuffins =
        {
            ("Puffin", new Vector3(9.06f, 2.822f, 5.304f), -28f), ("Puffin", new Vector3(9.215f, 1.979f, 5.958f), -22f),
        };

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
