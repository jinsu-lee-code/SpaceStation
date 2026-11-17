using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 3단계 궤도 항만 "진주 조개 항구 · 인어 왕국의 항구" 내부 값 (Blender op3_builder와 같은 값, 원점 = 0번 칸 중심 · 칸 8 m, 바닥 7 × 2칸 한 층):
    /// 왼쪽 = 산호 섬 위 진주 조개 궁전 · 소라 첨탑 · 섬 전망 첨탑 · 앞 인어 꼬리 분수 마당, 가운데 = 석호 (해마 가리비 배 넷이 도는 배 길) · 해마 회전목마 · 진주 모래 해변 선착장 ·
    /// 뒤 산호 방파제 (소라 문), 오른쪽 = 진주 잔교 · 산호 기중기 운전대 · 큰 진주 조개 셋 + 잡기 단 · 앵무조개 창고 · 등대 전망대.
    /// 사용자 결정 (2026-11-16): 체험 = 해마 가리비 배 뱃놀이, 미니게임 = 진주 조개 잡기 + 인어 분수 진주 던지기. 각은 x-z 평면 수학 각 (0° = +x, 90° = +z).
    /// </summary>
    public static partial class OrbitalPort
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
        /// <summary>
        /// 조개 k 뚜껑 경첩 축 (op3_builder clam_half side = 입 방향 × 위 — 이 축으로 + 돌리면 입 쪽이 들림). 닫기 = −<see cref="ClamLidOpen"/>°.
        /// </summary>
        public static Vector3 ClamHingeAxis(int k)
        {
            float a = Clams[k].z * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        // ---------------- 미니게임 (사용자 2026-11-16 "미니게임 진행해줘") ----------------
        /// <summary>
        /// 진주 조개 잡기 시작 상자 (보이지 않음): 잡기 단 난간 바로 밖 물 위에 세운 얇은 판 — 단에서 조개 쪽을 보면 시선이 먼저 닿아 "F 진주 조개 잡기"
        /// (난간 안 마루는 막지 않음, 판 중에는 꺼서 조개를 겨눔) · 안내판 (단 뒤 머리 위 — 조개를 볼 때 등 뒤) · 단 위로 치는 반지름 (단 가운데에서 수평).
        /// </summary>
        public static readonly Vector3 ClamGameStart = new Vector3(42f, Floor + 0.95f, 4.83f);
        public static readonly Vector3 ClamGameStartSize = new Vector3(5f, 1.7f, 0.14f);
        public static readonly Vector3 ClamGameSign = new Vector3(42f, Floor + 2.55f, 3.2f);
        public const float ClamStageReach = 1.9f;
        /// <summary>
        /// 인어 분수 진주 던지기 시작 상자 (보이지 않음, 분수 물 위 — 테 앞에서 물을 보면 "F 진주 던지기", 판 중에는 꺼서 물 위 고리를 겨눔) ·
        /// 안내판 (꼬리 끝 위) · 판을 이어 가는 거리 (분수 가운데에서 수평 — 더 멀어지면 끝).
        /// </summary>
        public static readonly Vector3 TossStart = new Vector3(9.8f, Floor + 0.68f, -0.5f);
        public static readonly Vector3 TossStartSize = new Vector3(2.5f, 0.6f, 2.5f);
        public static readonly Vector3 TossSign = new Vector3(9.7f, 1.3f, -0.5f);
        public const float TossLeave = 8f;
        /// <summary>분수 물 반지름 (op3_builder OP_FountWater = FOUNT_R − 0.04) — 이 안에 떨어진 진주만 셈.</summary>
        public const float FountainWaterRadius = 1.86f;
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

        // ---------------- 배 길 (op3_builder course_curve와 같은 점 — 닫힌 Catmull-Rom, 이음점 사이를 0.25 m 남짓으로 나눔) ----------------
        private static Vector2[] _course;
        private static float[] _courseS;
        private static int[] _courseControlIndex;

        private static void InitCourse()
        {
            if (_course != null)
                return;
            int n = CourseControl.Length;
            var pts = new System.Collections.Generic.List<Vector2>();
            var ctrl = new int[n];
            for (int i = 0; i < n; i++)
            {
                ctrl[i] = pts.Count;
                Vector2 p0 = CourseControl[(i - 1 + n) % n], p1 = CourseControl[i], p2 = CourseControl[(i + 1) % n], p3 = CourseControl[(i + 2) % n];
                int m = Mathf.Max(2, (int)((p2 - p1).magnitude / 0.25f));
                for (int k = 0; k < m; k++)
                {
                    float t = (float)k / m;
                    pts.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t) + (-p0 + 3f * p1 - 3f * p2 + p3) * (t * t * t)));
                }
            }
            var s = new float[pts.Count + 1];
            for (int i = 0; i < pts.Count; i++)
                s[i + 1] = s[i] + (pts[(i + 1) % pts.Count] - pts[i]).magnitude;
            _courseS = s;
            _courseControlIndex = ctrl;
            _course = pts.ToArray();
        }

        /// <summary>배 길 한 바퀴 길이 (m, s = 선착장 앞에서 잰 길이 — 배가 가는 쪽으로 늘어남).</summary>
        public static float CourseLoop { get { InitCourse(); return _courseS[_course.Length]; } }

        /// <summary>s를 한 바퀴 안으로 (0 ~ <see cref="CourseLoop"/>).</summary>
        public static float CourseWrap(float s) => Mathf.Repeat(s, CourseLoop);

        /// <summary>배 길 위 s 자리 (x, z).</summary>
        public static Vector2 CoursePoint(float s)
        {
            InitCourse();
            s = CourseWrap(s);
            int lo = 0, hi = _course.Length;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_courseS[mid] <= s)
                    lo = mid;
                else
                    hi = mid;
            }
            float len = _courseS[lo + 1] - _courseS[lo];
            return Vector2.Lerp(_course[lo], _course[(lo + 1) % _course.Length], len > 1e-6f ? (s - _courseS[lo]) / len : 0f);
        }

        /// <summary>배 길 s 자리 · 가는 쪽 (앞뒤 0.3 m로 잰 매끈한 방향, 단위 벡터 x, z).</summary>
        public static void CoursePose(float s, out Vector2 at, out Vector2 dir)
        {
            at = CoursePoint(s);
            dir = (CoursePoint(s + 0.3f) - CoursePoint(s - 0.3f)).normalized;
        }

        /// <summary>배 k의 처음 자리 (op3_builder boats — 배 길 점을 넷으로 나눈 점 번호 n·k/4, <see cref="Boats"/>와 같은 자리).</summary>
        public static float BoatStartS(int k)
        {
            InitCourse();
            return _courseS[_course.Length * k / 4];
        }

        /// <summary>배 길 (CourseControl 닫힌 Catmull-Rom — op3_builder course_curve와 같은 0.25 m 이음)까지 수평 거리.</summary>
        public static float CourseDistance(Vector2 p)
        {
            InitCourse();
            float best = float.MaxValue;
            for (int i = 0; i < _course.Length; i++)
            {
                Vector2 a = _course[i], seg = _course[(i + 1) % _course.Length] - a;
                float u = Mathf.Clamp01(Vector2.Dot(p - a, seg) / Mathf.Max(1e-6f, seg.sqrMagnitude));
                best = Mathf.Min(best, (a + seg * u - p).magnitude);
            }
            return best;
        }

        /// <summary>뱃놀이 안내 줄 — 이음점 사이마다 지나는 곳 (이음점 0 = 선착장 앞에서 궁전 쪽으로 출발).</summary>
        private static readonly string[] CourseSections =
        {
            "선착장 출발 · 인어 꼬리 분수", "산호 섬 · 진주 조개 궁전", "진주 계단 · 삼지창 문", "섬 전망 첨탑", "산호 방파제 · 왕관 바다 문",
            "산호 방파제 · 해마 회전목마", "진주 잔교 · 산호 기중기", "큰 진주 조개 부두", "모래톱 · 물범 쉼터", "선착장으로",
        };

        /// <summary>배 길 s 자리를 지나는 곳 이름 (뱃놀이 안내 줄).</summary>
        public static string CourseSection(float s)
        {
            InitCourse();
            s = CourseWrap(s);
            int i = 0;
            for (int k = 0; k < _courseControlIndex.Length; k++)
            {
                if (_courseS[_courseControlIndex[k]] <= s)
                    i = k;
            }
            return CourseSections[i];
        }

        // ---------------- 해마 가리비 배 뱃놀이 (체험 — InteriorScallopRide) ----------------
        /// <summary>배 피벗 기준 앉음판 왼쪽 · 오른쪽 자리 (원점 = 배 가운데 수면, +z = 앞 — op3_builder boat: 앉음판 = 가운데 − 앞 0.2, 윗면 수면 + 0.30, 폭 0.9).</summary>
        public static Vector3 BoatSeat(int side) => new Vector3(side == 0 ? -0.22f : 0.22f, 0.32f, -0.2f);
        /// <summary>앉은 발 자리 (앉음판 앞 바닥판 윗면 = 수면 + 0.06) · 앉은 눈높이 (발 위).</summary>
        public static Vector3 BoatFeet(int side) => new Vector3(side == 0 ? -0.22f : 0.22f, 0.06f, 0.12f);
        public const float BoatSeatedEye = 0.98f;
        /// <summary>뱃놀이 빠르기 (m/초 — 한 바퀴 약 53초) · 선착장에 서 있는 시간 (앉아 있으면 짧게) · 앞 배와 간격 (배 길이 3.3 + 0.3).</summary>
        public const float BoatCruise = 0.85f, BoatDwell = 8f, BoatDwellSeated = 3f, BoatGap = 3.6f;
        /// <summary>내리는 자리 (선착장 마루 가운데 · 끝에서 0.45 m, 윗면 = Stop.y) · 선착장 안내판 (마루 들머리 위).</summary>
        public static readonly Vector3 BoatExit = new Vector3(24f, Floor + 0.06f, -1.6f);
        public static readonly Vector3 BoatSign = new Vector3(24f, Floor + 2.15f, -2.9f);

        // ---------------- 해마 (critter_builder 귀여운 해마 Critter_Seahorse{모델} — 사용자 2026-11-17 "해마 유니티로 옮겨줘") ----------------
        // 옛 해마 (op3_builder — 관 · 공을 이어 붙인 얼굴 없는 기둥, 사용자 "해마가 좀 못생긴 것 같아서")는 짓지 않고 내부 동물 모델을 놓음 (얼굴 = 동물 아틀라스).
        // 자리 = 모델 원점 = 말린 꼬리 밑, op3_builder SH_BOAT · SH_CAR · SH_PAL · SEAHORSES와 같은 값.
        /// <summary>배 해마 한 쌍 (모델, 옆 — 배 가운데에서 왼쪽이 + : op3_builder side = (−앞 z, 0, 앞 x) × 옆): 민트 = 오른쪽 · 분홍 = 왼쪽.</summary>
        public static readonly (string Model, float Side)[] BoatSeahorses = { ("SeahorseMint", -1f), ("SeahorsePink", 1f) };
        /// <summary>배 해마: 배 가운데에서 앞 · 옆 · 꼬리 밑 높이 (수면 기준 — 말린 꼬리만 물에) · 크기 (1.5배 = 키 0.93 m). 고삐 끝 = 꼬리 밑 + 0.66 (머리 속).</summary>
        public const float BoatSeahorseAhead = 1.85f, BoatSeahorseSide = 0.32f, BoatSeahorseDrop = -0.15f, BoatSeahorseScale = 1.5f;

        /// <summary>배 k 처음 자리의 앞쪽 (op3_builder boats — 배 길 점 n·k/4에서 다음 점으로, 배 메시를 구운 방향).</summary>
        public static Vector2 BoatStartForward(int k)
        {
            float s = BoatStartS(k);
            return (CoursePoint(s + 0.01f) - CoursePoint(s)).normalized;
        }

        /// <summary>배 k의 해마 (옆 side) 처음 자리 (꼬리 밑) · 바라보는 쪽 수학 각 — 배 피벗 아래로 옮겨 배와 함께 돎.</summary>
        public static void BoatSeahorse(int k, float side, out Vector3 at, out float facing)
        {
            var c = CoursePoint(BoatStartS(k));
            var d = BoatStartForward(k);
            var p = c + d * BoatSeahorseAhead + new Vector2(-d.y, d.x) * (side * BoatSeahorseSide);
            at = new Vector3(p.x, LagoonLevel + BoatSeahorseDrop, p.y);
            facing = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 회전목마 해마 넷 (회전목마 조각의 자식 — 함께 돎): 금빛 기둥 (반지름 1.8, 0 · 90 · 180 · 270°)이 몸 가운데를 지나 (모델 몸 가운데 = 원점 앞 1 cm × 크기),
        /// 꼬리 밑 = 받침 윗면 + 0.55, 도는 쪽을 봄 · 1.75배 (키 1.09 m).
        /// </summary>
        public static readonly (string Model, Vector3 At, float Facing)[] CarouselSeahorses =
        {
            ("SeahorseMint", new Vector3(25.8f, Floor + 0.95f, 3.4825f), 90f), ("SeahorsePink", new Vector3(24.0175f, Floor + 0.95f, 5.3f), 180f),
            ("SeahorseGold", new Vector3(22.2f, Floor + 0.95f, 3.5175f), 270f), ("SeahorsePearl", new Vector3(23.9825f, Floor + 0.95f, 1.7f), 0f),
        };
        public const float CarouselSeahorseScale = 1.75f;
        /// <summary>궁전 왕좌 양옆 금빛 해마 상 (진주 받침 윗면 = 궁전 단 + 0.32, 문 (+x)을 봄) · 1.6배 (키 1 m).</summary>
        public static readonly (string Model, Vector3 At, float Facing)[] PalaceSeahorses =
        {
            ("SeahorseGold", new Vector3(3.2845f, Floor + 0.92f, 5.835f), 0f), ("SeahorseGold", new Vector3(3.2845f, Floor + 0.92f, 4.085f), 0f),
        };
        public const float PalaceSeahorseScale = 1.6f;

        // ---------------- 바다 동물 (critter_builder 바다 동물 — 궤도 방어망과 같은 모델, 사용자 2026-11-16 "이제 동물 배치 진행해줘") ----------------
        // (모델, 자리 (모델 원점), 바라보는 쪽 수학 각). 땅 위 자리 = 몸 밑 닿는 점 (물범 = 원점 위 3.5 cm 안 배 · 지느러미 · 엉덩이 밑, 게 = 다리 끝)이 97 % 모래 1.5 cm 안 ·
        //  가장 큰 틈 3 cm 미만이 되게 템플릿 면을 광선으로 재서 찾은 높이 — OrbitalPortTests.Critters_OnTheirSpots가 지킴.
        /// <summary>
        /// 물범 · 하얀 아기 물범 = 진주 조개 잡기 단 서쪽 모래톱 물가 (나란히 석호 쪽을 봄 — 앞 1 m가 물가 턱): 평평한 모래 위 (물가 턱 비탈에 두면 1 m 몸 밑이 8 cm 묻혔음),
        /// 어른 물범 몸 밑 1.4 %만 1.5 cm 넘게 뜨고 가장 깊이 3.3 cm 묻힘 · 아기 물범 뜬 점 없음.
        /// </summary>
        public static readonly (string Kind, Vector3 At, float Facing)[] Seals =
        {
            ("Seal", new Vector3(40.5f, -2.109f, 2.6f), 100f), ("SealPup", new Vector3(39.6f, -2.101f, 2.3f), 75f),
        };
        /// <summary>
        /// 게 넷 = 물가선에서 0.55 m 안쪽 평평한 모래 (해변 둘 · 방파제 만 · 모래톱 동쪽 — 주민 자리 · 등에서 1.2 m 넘게): 물을 보고 물가를 따라 옆걸음.
        /// 옆걸음 띠 (물가 따라 ± 0.55 · 앞뒤 ± 0.2)에 해변 조개 · 불가사리 장식이 없게 (16.5에서는 다리가 불가사리 위에 걸쳐 보였음).
        /// </summary>
        public static readonly (string Kind, Vector3 At, float Facing)[] Crabs =
        {
            ("Crab", new Vector3(15.4f, -2.073f, -2.01f), 88f), ("Crab", new Vector3(30.5f, -2.073f, -1.95f), 98f),
            ("Crab", new Vector3(16.2f, -2.073f, 10.47f), 268f), ("Crab", new Vector3(47.2f, -2.073f, 3.95f), 92f),
        };
        /// <summary>
        /// 바다거북 둘 = 석호 수면 (원점 = 등딱지 가장자리 = 수면): 배 길 안쪽 산호초 곁 · 동쪽 조개 앞 물 — 배는 배 길을 따라 돌므로 길 전체 (배 반폭 0.85 + 0.3)와
        /// 물 위 물체 (회전목마 받침 · 산호초 · 조개 · 잔교 · 물가)를 비키는 고리로 천천히 헤엄.
        /// </summary>
        public static readonly (string Kind, Vector3 At, float Facing)[] Turtles =
        {
            ("Turtle", new Vector3(17.8f, LagoonLevel, 2.6f), 30f), ("Turtle", new Vector3(35.8f, LagoonLevel, 6.9f), 200f),
        };
        /// <summary>거북 헤엄 고리가 배 길에서 떨어질 거리 (배 반폭 0.85 + 여유 0.3).</summary>
        public const float TurtleCourseClear = 1.15f;
        /// <summary>
        /// 날치 셋 (critter_builder Critter_FlyingFish — 사용자 2026-11-16 모델 시트 "좋아 유니티로 옮겨줘"): 처음 자리 = 활공 한가운데 (수면 + 0.15), 바라보는 쪽으로
        /// 물에서 튀어나와 날개를 편 채 낮게 미끄러져 다시 물에 듦 (CritterFx Glide). 배 길 안쪽 오른편 · 왼편 산호초 곁 · 섬 뒤 북서쪽 물 —
        /// 활공 길 아래 물 깊이 0.5 m 넘게 · 배 길에서 1.3 m · 거북 자리에서 1.6 m · 길 둘레 0.45 m 안에 물 위 물체 없음 · 방 벽에서 0.6 m.
        /// </summary>
        public static readonly (string Kind, Vector3 At, float Facing)[] FlyingFish =
        {
            ("FlyingFish", new Vector3(29.0f, LagoonLevel + 0.15f, 5.5f), 180f), ("FlyingFish", new Vector3(20.5f, LagoonLevel + 0.15f, 4.5f), 60f),
            ("FlyingFish", new Vector3(-1.9f, LagoonLevel + 0.15f, 11.1f), 0f),
        };
        /// <summary>날치 묶음 배율 (CritterFx Glide 움직임 크기 — 활공 길이 0.18 × 15 = 2.7 m · 높이 0.025 × 15 = 0.375 m · 물속 0.04 × 15 = 0.6 m).</summary>
        public const float FlyingFishScale = 15f;
        public const float FlyingFishGlide = 0.18f * FlyingFishScale;

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
