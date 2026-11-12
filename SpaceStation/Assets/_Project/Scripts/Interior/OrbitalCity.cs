using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 3단계 궤도 도시 "알이 깨어난 정원 섬 도시" 내부 값 (Blender oc_builder와 같은 값, 원점 = 0번 칸 중심 · 칸 8 m):
    /// 섬 가운데 허브 (16, 16) · 높인 궤도열차 고리 (반지름 16.8) · 앞 오른쪽 역 (−45°) · 프리즘 나무 기둥 엘리베이터 (언덕 꼭대기 광장 → 옥상 전망대 직행) ·
    /// 해치 21 · 옥상 문 자리 3 · 앞 계단 폭포 셋 · 수면 넷 (땅 높이 강 · 연못 + 단마다 웅덩이 — KWS 물 하나씩).
    /// 각은 x-z 평면 수학 각 (0° = +x, 90° = +z, 270° = −z = 앞).
    /// </summary>
    public static class OrbitalCity
    {
        public const float Floor = -1.3f;
        public static readonly Vector3 Hub = new Vector3(16f, 0f, 16f);

        /// <summary>고리 강 안 · 바깥 둑 반지름 (다리 넷 = 45° + 90°).</summary>
        public const float RiverInner = 12f, RiverOuter = 13.6f;

        // ---------------- 궤도열차 ----------------
        public const float TrackRadius = 16.8f;
        /// <summary>레일 윗면 (밑 = F + 2.4, 걸어 지나감).</summary>
        public const float TrackTop = Floor + 2.7f;
        /// <summary>객차 바닥 윗면 (차 바닥 판 = 레일 위 0.12 ~ 0.36).</summary>
        public const float CarFloor = TrackTop + 0.36f;
        public const float PlatformTop = TrackTop + 0.3f;
        public const float PlatformInner = TrackRadius + 0.95f;
        public const float PlatformOuter = TrackRadius + 3.2f;
        public const float PlatformHalf = 17f;
        public const float StationAngle = -45f;
        public const float CarPitch = 9.5f, CarLength = 2.6f, CarWidth = 1.7f;
        /// <summary>쉬는 자세 앞 차 가운데 각 (가운데 차 = 역 가운데).</summary>
        public const float FrontAngle = StationAngle + CarPitch;
        /// <summary>앞 차 코 = 정지선 각.</summary>
        public static float NoseAngle => FrontAngle + CarLength * 0.5f / TrackRadius * Mathf.Rad2Deg;
        /// <summary>한 바퀴 (m).</summary>
        public static float Lap => 2f * Mathf.PI * TrackRadius;

        // ---------------- 수정 나무 기둥 ----------------
        /// <summary>엘리베이터 층 바닥: 언덕 꼭대기 광장 (2층 바닥) → 옥상 전망대 직행 (사용자 2026-11-11 "나머지 층 전부 없애줘 — 프리즘 나무 외관을 해침").</summary>
        public static readonly float[] LiftStops = { Floor + 8f, 38.7f };
        public const float ShaftRadius = 1.1f, CarRadius = 0.98f;
        /// <summary>기둥 문 자리 (칸 중심에서 문 패널 바깥면까지) — 테라스 다리 끝 3.93 바로 안.</summary>
        public const float TowerDoorDepth = 3.9f;

        /// <summary>옥상 칸 (2, 5, 2) 문 자리 = 외형 옆 연결점 (−z 없음). 가운데 기둥 칸 (2, 2 ~ 4, 2)은 승강장이 없어 문 없는 면.</summary>
        public static readonly (Vector3Int Cell, Vector3Int Direction)[] TowerDoors =
        {
            (new Vector3Int(2, 5, 2), Vector3Int.right), (new Vector3Int(2, 5, 2), new Vector3Int(0, 0, 1)), (new Vector3Int(2, 5, 2), Vector3Int.left),
        };

        // ---------------- 물 · 폭포 ----------------
        /// <summary>수면 높이 (KWS 물 하나씩, 기준점 = 수면): 땅 높이 (강 · 연못 · 바닥 웅덩이 · 강으로 가는 물길) · 아래 단 · 위 단 · 꼭대기 물길. Blender 물체 Water · Water1 · 2 · 3.</summary>
        public static readonly float[] WaterLevels = { Floor - 0.15f, 1.41f, 3.81f, 6.61f };

        /// <summary>계단 폭포 셋 (위 → 아래): 턱 (물이 넘어가는 곳) · 떨어지는 자리 (아래 웅덩이 수면) · 물줄기 폭 — 물안개 · 물방울 · 웅덩이 물결 자리.</summary>
        public static readonly (Vector3 Lip, Vector3 Landing, float Width)[] Falls =
        {
            (new Vector3(16f, 6.61f, 11.05f), new Vector3(16f, 3.81f, 10.12f), 0.86f),
            (new Vector3(16f, 3.81f, 8.65f), new Vector3(16f, 1.41f, 7.72f), 0.86f),
            (new Vector3(16f, 1.41f, 6.25f), new Vector3(16f, -1.45f, 5.42f), 0.86f),
        };

        /// <summary>
        /// 아래 해치 21 (바닥 칸, 네 귀퉁이 빼고 — 외형 아래 연결점): oc_export가 찾은 칸 가운데에서 가장 가까운 빈 바닥 (1.9 m 틀이 모두 바닥 높이 · 위가 빔).
        /// 안쪽 9칸 = 언덕 속 고리 길 · 뿌리 홀, 바깥 12칸 = 고리 길 · 잔디 (못 찾으면 레일 밑).
        /// </summary>
        public static readonly (Vector3Int Cell, Vector2 Offset)[] Hatches =
        {
            (new Vector3Int(0, 0, 1), new Vector2(0f, -2f)), (new Vector3Int(0, 0, 2), new Vector2(0.8f, 0f)), (new Vector3Int(0, 0, 3), new Vector2(0f, 2f)),
            (new Vector3Int(1, 0, 0), new Vector2(2.8f, -2f)), (new Vector3Int(1, 0, 1), new Vector2(2f, 2.2f)), (new Vector3Int(1, 0, 2), Vector2.zero),
            (new Vector3Int(1, 0, 3), new Vector2(2f, -2.2f)), (new Vector3Int(1, 0, 4), new Vector2(-2f, 0f)), (new Vector3Int(2, 0, 0), new Vector2(0f, 0.8f)),
            (new Vector3Int(2, 0, 1), new Vector2(0f, 1.6f)), (new Vector3Int(2, 0, 2), Vector2.zero), (new Vector3Int(2, 0, 3), Vector2.zero),
            (new Vector3Int(2, 0, 4), new Vector2(0f, -0.8f)), (new Vector3Int(3, 0, 0), new Vector2(-0.6f, -1.2f)), (new Vector3Int(3, 0, 1), new Vector2(-2f, 1.8f)),
            (new Vector3Int(3, 0, 2), Vector2.zero), (new Vector3Int(3, 0, 3), new Vector2(-2.2f, -2f)), (new Vector3Int(3, 0, 4), new Vector2(2f, 0f)),
            (new Vector3Int(4, 0, 1), new Vector2(-1.4f, 0f)), (new Vector3Int(4, 0, 2), new Vector2(-0.8f, 0f)), (new Vector3Int(4, 0, 3), new Vector2(0f, 2f)),
        };

        /// <summary>허브 둘레 각 deg · 반지름 r · 높이 y의 템플릿 좌표.</summary>
        public static Vector3 Polar(float r, float deg, float y)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Hub.x + r * Mathf.Cos(a), y, Hub.z + r * Mathf.Sin(a));
        }

        /// <summary>각 deg에서 반지름이 늘어나는 쪽을 볼 때의 yaw (모듈 로컬 +z 기준 시계 방향).</summary>
        public static float OutwardYaw(float deg) => Mathf.Atan2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * Mathf.Rad2Deg;

        /// <summary>각이 늘어나는 쪽 (열차가 달리는 쪽)을 볼 때의 yaw.</summary>
        public static float ForwardYaw(float deg) => Mathf.Atan2(-Mathf.Sin(deg * Mathf.Deg2Rad), Mathf.Cos(deg * Mathf.Deg2Rad)) * Mathf.Rad2Deg;

        /// <summary>정지 판정 (색이 아니라 글로 — 색약): 정지선과 앞 차 코 사이 거리 (m).</summary>
        public static string Grade(float distance)
        {
            float d = Mathf.Abs(distance);
            if (d <= 0.3f)
                return "딱 맞췄어요!";
            if (d <= 1f)
                return "아주 좋아요";
            if (d <= 3f)
                return "좋아요";
            return "조금 아쉬워요";
        }
    }
}
