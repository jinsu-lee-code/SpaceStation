using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 3단계 AI 중추 "수정 뇌 미로 정원" 내부 값 (Blender ai3_builder와 같은 값, 원점 = 0번 칸 중심 · 칸 8 m, 바닥 L자 일곱 칸 한 층):
    /// 가운데 = 뇌 두 반구 모양 생울타리 미로 (앞 = 보라 꽃 · 뒤 = 금빛 꽃) · 빈터 유리 돔 정자 (연꽃 받침 수정 뇌 · 책 받침 넷 · 시냅스 수정 여섯),
    /// 왼쪽 = 장미 아치 문 앞마당 (미로 출발 · 기록판), 오른쪽 = 드론 마당 앞마당 → 드론 마당 (드론 둥지 탑 · 유리 온실 · 꽃밭 · 화분 작업대 · 모듈 문).
    /// 사용자 결정 (2026-11-16): 체험 = 정원 드론 타고 뇌 위 날기 + 수정 뇌 깨우기 빛 쇼, 미니게임 = 뇌 미로 빨리 빠져나가기 (빛 구슬 셋 모으기) + 시냅스 따라 켜기.
    /// 각은 x-z 평면 수학 각 (0° = +x, 90° = +z), yaw는 Unity (0° = +z, 90° = +x).
    /// </summary>
    public static partial class AICore
    {
        public const float Floor = -2.08f;
        /// <summary>미로 진주 자갈 윗면 · 빈터 · 앞마당 판석 윗면 · 판석 길 윗면 · 정자 단 윗면 (ai3_builder GRAVEL_Y · PAVE_Y · POD_Y).</summary>
        public const float GravelTop = Floor + 0.012f, PaveTop = Floor + 0.035f, PathTop = Floor + 0.025f, PodTop = Floor + 0.3f;
        /// <summary>칸 천장 (하늘 면) — 한 층이라 모두 3.95.</summary>
        public const float CeilingY = 3.95f;

        // ---------------- 빈터 · 정자 ----------------
        /// <summary>정자 가운데 (단 윗면) · 단 반지름 · 기둥 고리 · 아래 계단 바깥 · 빈터 판석 반지름.</summary>
        public static readonly Vector3 Pavilion = new Vector3(8f, PodTop, 4f);
        public const float PavilionRadius = 2.0f, ColumnRadius = 1.8f, StepRadius = 2.3f, ClearingRadius = 4.4f;
        /// <summary>수정 뇌 (따로 조각 Brain, 원점 = 뇌 가운데 — 빛 쇼 때 빛남).</summary>
        public static readonly Vector3 Brain = new Vector3(8f, -0.46f, 4f);
        /// <summary>책 받침 넷 (대각선 45 · 135 · 225 · 315°, 정자 가운데에서 1.2 m) · 받침 판 기울기 (바깥 = 서는 사람 쪽을 봄).</summary>
        public static readonly float[] LecternAngles = { 45f, 135f, 225f, 315f };
        public const float LecternRadius = 1.2f, LecternTilt = 25f, LecternTop = PodTop + 0.94f;

        /// <summary>책 받침 k 바깥 방향 (x-z).</summary>
        public static Vector3 LecternOut(int k)
        {
            float a = LecternAngles[k] * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        /// <summary>책 받침 k 판 가운데 (윗면 위 — 보고 F 자리).</summary>
        public static Vector3 LecternPlate(int k) => new Vector3(Pavilion.x, LecternTop, Pavilion.z) + LecternOut(k) * LecternRadius;

        /// <summary>책 받침 k 판 윗면 법선 (바깥 위로 25°) · 책등 (판 비탈 아래쪽 = 오른쪽 책장이 이 축으로 접힘).</summary>
        public static Vector3 LecternNormal(int k) => (LecternOut(k) * Mathf.Sin(LecternTilt * Mathf.Deg2Rad) + Vector3.up * Mathf.Cos(LecternTilt * Mathf.Deg2Rad)).normalized;

        public static Vector3 BookSpineAxis(int k)
        {
            var side = Vector3.Cross(Vector3.up, LecternOut(k)).normalized;
            return Vector3.Cross(side, LecternNormal(k)).normalized;
        }

        /// <summary>책 k (BookL{k} · BookR{k} 원점 = 책등 — 판 윗면 + 3 cm).</summary>
        public static Vector3 BookSpine(int k) => LecternPlate(k) + LecternNormal(k) * 0.03f;

        /// <summary>시냅스 수정 여섯 (따로 조각 Synapse{k}, 원점 = 구슬 가운데 — 정자 앞 서는 자리에서 왼쪽부터) · 금빛 줄기 뿌리 (뇌 뒤).</summary>
        public static readonly Vector3[] Synapses =
        {
            new Vector3(6.922f, 0.06f, 4.62f), new Vector3(7.242f, 0.484f, 4.62f), new Vector3(7.735f, 0.73f, 4.62f),
            new Vector3(8.265f, 0.73f, 4.62f), new Vector3(8.758f, 0.484f, 4.62f), new Vector3(9.078f, 0.06f, 4.62f),
        };
        public static readonly Vector3 SynapseRoot = new Vector3(8f, PodTop + 0.72f, 4.42f);
        public const float SynapseRadius = 0.11f;
        /// <summary>시냅스 따라 켜기: 서는 자리 (정자 앞 계단 쪽 · 뇌를 봄) · 보는 쪽 · 처음 시선 (위로) · 겨눔 너그러움 (이웃 사이 12° 넘게의 절반 밑).</summary>
        public static readonly Vector3 SynapseStand = new Vector3(8f, PodTop, 2.35f);
        public const float SynapseYaw = 0f, SynapsePitch = -16f, SynapseAimDegrees = 5.5f;
        /// <summary>시냅스 따라 켜기 시작 상자 (보이지 않음 — 시냅스 부채 앞 · 보고 F, 판 중에는 꺼짐).</summary>
        public static readonly Vector3 SynapseStart = new Vector3(8f, 0.42f, 4.5f);
        public static readonly Vector3 SynapseStartSize = new Vector3(2.6f, 1.2f, 0.3f);

        // ---------------- 미로 · 장미 문 · 앞마당 ----------------
        /// <summary>장미 아치 문 (기둥 둘 = z ± 1.05) · 미로 기록판 (따로 조각 MazeBoard, 원점 = 판 앞면 가운데, +x를 봄 — 0.54 × 0.36 m).</summary>
        public static readonly Vector3 Gate = new Vector3(-3.35f, Floor, 4f);
        public static readonly Vector3 MazeBoard = new Vector3(-3.157f, -0.63f, 5.05f);
        public static readonly Vector2 MazeBoardSize = new Vector2(0.54f, 0.36f);
        /// <summary>앞마당 둘 (장미 문 앞 · 드론 마당 앞 — 둥근 판석 마당) · 반지름.</summary>
        public static readonly Vector2[] Lobbies = { new Vector2(-1.3f, 4f), new Vector2(17.3f, 4f) };
        public const float LobbyRadius = 2.6f;
        /// <summary>
        /// 미로 빛 구슬 자리 다섯 (ai3_builder ORBS — 막다른 칸 가운데, 입구 칸 빼고 장미 문에서 걸어 닿는 칸 중 장미 문에서 먼 칸부터 서로 먼 칸, 자갈 위 0.95 m):
        /// 판마다 셋 (앞 · 뒤 반구 하나씩 넘게). front = 앞 반구 (보라 꽃).
        /// </summary>
        public static readonly (bool Front, Vector3 At)[] Orbs =
        {
            (true, new Vector3(1.98f, GravelTop + 0.95f, 1.945f)), (true, new Vector3(0.26f, GravelTop + 0.95f, 0.225f)),
            (false, new Vector3(17.46f, GravelTop + 0.95f, 7.775f)), (false, new Vector3(3.7f, GravelTop + 0.95f, 7.775f)), (false, new Vector3(14.02f, GravelTop + 0.95f, 9.495f)),
        };
        /// <summary>빛 구슬을 줍는 거리 (발 가운데에서 수평) · 정자 도착 (정자 가운데에서 수평, 단 위).</summary>
        public const float OrbReach = 0.9f, MazeGoalRadius = PavilionRadius - 0.25f;
        /// <summary>미로 빠져나가기 판 수 · 판 시간 한도 (넘으면 끝).</summary>
        public const int MazeOrbCount = 3;
        public const float MazeTimeLimit = 300f;

        // ---------------- 드론 둥지 · 정원 드론 (체험 — InteriorDroneRide) ----------------
        /// <summary>정원 드론 (따로 조각 Drone, 원점 = 둥지 탑 옥상 착륙장 가운데 = 바구니 밑) · 탑 가운데 · 옥상 높이.</summary>
        public static readonly Vector3 DronePad = new Vector3(25.4f, Floor + 2.7f, -0.7f);
        /// <summary>드론 몸 꼭대기 (원점 위 — 수정 꼭지까지) · 날개 테 끝 반지름 · 바구니 반지름 · 바구니 테 높이 · 서쪽 트임 반각.</summary>
        public const float DroneTop = 2.09f, DroneSpan = 1.47f, BasketRadius = 0.95f, BasketRim = 0.84f, BasketOpening = 45f;
        /// <summary>드론 기준 앉음판 왼쪽 · 오른쪽 (가운데 앉음판 둘 — 서쪽 = 트임 · 나는 앞을 봄, 윗면 바구니 바닥 + 0.47).</summary>
        public static Vector3 DroneSeat(int side) => new Vector3(0.12f, 0.53f, side == 0 ? 0.32f : -0.32f);
        /// <summary>앉은 발 자리 (앉음판 앞 바구니 바닥 윗면) · 앉은 눈높이 (발 위 — 바구니 테 0.84 위로 내다봄).</summary>
        public static Vector3 DroneFeet(int side) => new Vector3(-0.2f, 0.06f, side == 0 ? 0.32f : -0.32f);
        public const float DroneSeatedEye = 0.98f;
        /// <summary>앉은 처음 시선 (아래로 — 미로 위에서 뇌 모양 · 빛 신경이 보이게, 수평 12°는 하늘 화면만 크게 보였음 · 2026-11-16 Unity 확인).</summary>
        public const float DroneSeatedPitch = 26f;
        /// <summary>내리는 자리 (옥상 착륙장 서쪽 — 드론 원점 기준, 둘레 난간 1.89 안).</summary>
        public static readonly Vector3 DroneExit = new Vector3(-1.3f, 0f, 0f);
        /// <summary>날기: 떠오르는 시간 · 내려앉는 시간 · 나는 빠르기 (m/초) · 가속 · 착륙장에 서 있는 시간 (앉으면 이만큼 뒤 뜸).</summary>
        public const float DroneLiftTime = 2.6f, DroneLandTime = 2.8f, DroneCruise = 2.4f, DroneAccel = 0.8f, DroneBoardDelay = 2.5f;
        /// <summary>
        /// 날 길 (바구니 밑 높이, 열린 Catmull-Rom — 착륙장 위 → 드론 마당 앞마당 위 → 미로 위 타원 한 바퀴 (뒤 반구 → 장미 문 쪽 → 앞 반구) → 착륙장 위):
        /// 착륙장 위 = 바닥 + 3.8 (둥지 난간 1.05 위 5 cm · 꼭대기가 하늘 면 14 cm 아래), 미로 위 = 바닥 + 3.6 (생울타리 2.1 위 1.5 m · 정자 돔에서 수평 2.3 m).
        /// </summary>
        public const float DroneTransitY = Floor + 3.8f, DroneLoopY = Floor + 3.6f;
        public static readonly Vector2 DroneLoopCenter = new Vector2(8f, 4f), DroneLoopRadii = new Vector2(8.6f, 3.8f);
        private static Vector3[] _route;
        private static float[] _routeS;

        private static Vector3[] RouteControl()
        {
            var list = new System.Collections.Generic.List<Vector3>
            {
                new Vector3(DronePad.x, DroneTransitY, DronePad.z), new Vector3(22.6f, DroneTransitY, 1.2f), new Vector3(18.8f, DroneLoopY + 0.12f, 2.6f),   // 드론 마당 쪽으로 돌아 들어감 (방 L자 모서리 x 19.95 · z 3.95를 날개 테가 넘지 않게)
            };
            for (int k = 0; k <= 8; k++)
            {
                float a = 45f * k * Mathf.Deg2Rad;                       // 동쪽 끝에서 뒤 반구 (z +) → 장미 문 쪽 → 앞 반구 → 동쪽 끝
                list.Add(new Vector3(DroneLoopCenter.x + DroneLoopRadii.x * Mathf.Cos(a), DroneLoopY, DroneLoopCenter.y + DroneLoopRadii.y * Mathf.Sin(a)));
            }
            list.Add(new Vector3(18.9f, DroneLoopY + 0.12f, 2.3f));
            list.Add(new Vector3(22.6f, DroneTransitY, 1.2f));
            list.Add(new Vector3(DronePad.x, DroneTransitY, DronePad.z));
            return list.ToArray();
        }

        private static void InitRoute()
        {
            if (_route != null)
                return;
            var c = RouteControl();
            int n = c.Length;
            var pts = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = c[Mathf.Max(0, i - 1)], p1 = c[i], p2 = c[i + 1], p3 = c[Mathf.Min(n - 1, i + 2)];
                int m = Mathf.Max(2, (int)((p2 - p1).magnitude / 0.2f));
                for (int k = 0; k < m; k++)
                {
                    float t = (float)k / m;
                    pts.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t) + (-p0 + 3f * p1 - 3f * p2 + p3) * (t * t * t)));
                }
            }
            pts.Add(c[n - 1]);
            _route = pts.ToArray();
            _routeS = new float[_route.Length];
            for (int i = 1; i < _route.Length; i++)
                _routeS[i] = _routeS[i - 1] + (_route[i] - _route[i - 1]).magnitude;
        }

        /// <summary>날 길 길이 (m, 착륙장 위에서 착륙장 위까지).</summary>
        public static float DroneRouteLength
        {
            get
            {
                InitRoute();
                return _routeS[_routeS.Length - 1];
            }
        }

        /// <summary>날 길 s 자리 · 나아가는 방향 (수평 — 드론이 이쪽을 봄).</summary>
        public static void DroneRoutePose(float s, out Vector3 at, out Vector3 dir)
        {
            InitRoute();
            s = Mathf.Clamp(s, 0f, _routeS[_routeS.Length - 1]);
            int lo = 0, hi = _routeS.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_routeS[mid] <= s)
                    lo = mid;
                else
                    hi = mid;
            }
            float seg = Mathf.Max(1e-5f, _routeS[hi] - _routeS[lo]);
            at = Vector3.Lerp(_route[lo], _route[hi], (s - _routeS[lo]) / seg);
            var d = _route[hi] - _route[lo];
            d.y = 0f;
            dir = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.left;
        }

        /// <summary>날 길 지나는 곳 이름 (안내 줄).</summary>
        public static string DroneSection(float s)
        {
            DroneRoutePose(s, out var p, out _);
            if (p.x > 18.4f)
                return "드론 둥지 · 드론 마당";
            float a = Mathf.Atan2((p.z - DroneLoopCenter.y) / DroneLoopRadii.y, (p.x - DroneLoopCenter.x) / DroneLoopRadii.x) * Mathf.Rad2Deg;
            if (a < 0f)
                a += 360f;
            if (a < 30f || a >= 330f)
                return "드론 마당 앞마당 · 정자 큰길";
            if (a < 150f)
                return "뒤 반구 (금빛 신경 · 금빛 꽃)";
            if (a < 210f)
                return "장미 아치 문 · 미로 출발";
            return "앞 반구 (보라 신경 · 보라 꽃)";
        }

        // ---------------- 연결점 ----------------
        /// <summary>스폰 = 드론 마당 앞마당 옆 판석 길 (큰길 너머 정자 · 미로 입구 기둥을 봄).</summary>
        public static readonly Vector3 Spawn = new Vector3(20.6f, PathTop, 3.05f);
        public const float SpawnYaw = 270f;
        /// <summary>옆 문 깊이 (칸 중심 → 문 패널 바깥면, 하늘 면 바로 안).</summary>
        public const float DoorDepth = 3.9f;
        /// <summary>옆 문 하나 = 외형 연결점: 드론 마당 칸 (3, 0, 0) 뒤 (+z).</summary>
        public static readonly (Vector3Int Cell, Vector3Int Direction)[] Doors =
        {
            (new Vector3Int(3, 0, 0), new Vector3Int(0, 0, 1)),
        };
        /// <summary>
        /// 아래 해치 일곱 (바닥 칸 모두, ai3_builder HATCHES — ai_export에서 1.6 m 틀 9 × 9 광선이 한 판 · 위 2.3 m 빔 · 몸 높이 0.55 m 안 빔):
        /// 앞마당 둘에 둘씩 (큰길 양옆 · 판석 위) · 빈터 둘레 길 앞뒤 · 드론 마당 풀밭 (문 앞 판석 길 틀 밖). 칸 · 가운데에서 자리 · 바닥 높이.
        /// </summary>
        public static readonly (Vector3Int Cell, Vector2 Offset, float FloorY)[] Hatches =
        {
            (new Vector3Int(0, 0, 0), new Vector2(-1.3f, 2.75f), PaveTop), (new Vector3Int(1, 0, 0), new Vector2(0f, 0.72f), PaveTop),
            (new Vector3Int(2, 0, 0), new Vector2(1.3f, 2.75f), PaveTop), (new Vector3Int(3, 0, 0), new Vector2(-2.9f, 1.45f), Floor),
            (new Vector3Int(0, 0, 1), new Vector2(-1.3f, -2.75f), PaveTop), (new Vector3Int(1, 0, 1), new Vector2(0f, -0.72f), PaveTop),
            (new Vector3Int(2, 0, 1), new Vector2(1.3f, -2.75f), PaveTop),
        };
    }
}
