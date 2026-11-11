using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 3단계 항성 발전망 "꿀벌 정원" 내부 값 (Blender sg3_builder와 같은 값, 원점 = 0번 칸 중심 · 칸 8 m, 바닥 7 × 2칸 + 위 2 × 2 (x 1 ~ 2) · 1 × 2 (x 5)):
    /// 왼쪽 꽃나무 숲 · 등나무 꽃 정자 (가운데 (12, 4)) · 해바라기 무리 · 꿀벌 물터 연못 · 전망대 (위 해치) · 꿀 샘 · 꿀 언덕 (가운데 (40, 4), 속 = 벌집 홀) · 라벤더 이랑.
    /// 각은 x-z 평면 수학 각 (0° = +x, 90° = +z, 270° = −z = 앞).
    /// </summary>
    public static class StellarGrid
    {
        public const float Floor = -1.3f;

        // ---------------- 등나무 꽃 정자 ----------------
        public static readonly Vector3 PavilionCenter = new Vector3(12f, Floor, 4f);
        public const float DomeRadius = 6.4f, DomeHeight = 10.4f;
        /// <summary>정자 입구 (앞 왼쪽 · 오른쪽 = 꿀 언덕 쪽).</summary>
        public static readonly float[] Entrances = { 240f, 0f };
        /// <summary>그네 굴대 (z 방향 — 자리는 x로 흔들림, 앞 = +x) · 줄 길이 · 수정 꽃송이 종 각 (앞뒤).</summary>
        public static readonly Vector3 SwingPivot = new Vector3(12f, Floor + 4.7f, 7f);
        public const float SwingLength = 4.1f;
        public static readonly float[] BellAngles = { 28f, 44f, 60f };
        /// <summary>정자 안 꿀빛 등불 여섯 (위 금빛 띠에 매단 빛 공 가운데).</summary>
        public static Vector3 Lantern(int k)
        {
            float a = (60f * k + 14.3f) * Mathf.Deg2Rad;
            const float t = 0.72f;
            float r = DomeRadius * Mathf.Cos(t * Mathf.PI / 2f), y = Floor + DomeHeight * Mathf.Sin(t * Mathf.PI / 2f) - 1.75f;
            return new Vector3(PavilionCenter.x + r * Mathf.Cos(a), y, PavilionCenter.z + r * Mathf.Sin(a));
        }

        // ---------------- 꿀 언덕 · 벌집 홀 ----------------
        public static readonly Vector3 HillCenter = new Vector3(40f, Floor, 4f);
        /// <summary>벌집 홀 가운데 (꿀 저장 기둥) · 정자 쪽 둥근 벌집 입구 (금빛 고리 가운데).</summary>
        public static readonly Vector3 HallCenter = new Vector3(40f, Floor, 4.6f);
        public static readonly Vector3 Portal = new Vector3(36.75f, Floor + 1.15f, 3.6f);
        /// <summary>꿀 언덕 꼭대기 여왕 수정 가운데 (sg3_builder PEAK).</summary>
        public static readonly Vector3 Queen = new Vector3(40f, 10.62f, 4.83f);

        // ---------------- 꿀 샘 (미니게임 "벌 몰래 꿀 먹기" 무대) ----------------
        public static readonly Vector3 HoneySpring = new Vector3(34.4f, Floor, 0.96f);
        public static readonly Vector3 Stump = new Vector3(33.05f, Floor + 0.42f, 1f);

        // ---------------- 사이 ----------------
        public static readonly Vector3 PondCenter = new Vector3(29f, Floor, 7.84f);
        public const float PondLevel = Floor - 0.12f;
        public static readonly Vector3 SunflowerCenter = new Vector3(23.6f, Floor, 0.16f);
        /// <summary>꿀벌이 앉아 쉬는 큰 꽃 넷 (꽃술 윗면).</summary>
        public static readonly Vector3[] RestFlowers =
        {
            new Vector3(4.5f, Floor + 1.13f, 4f), new Vector3(20.8f, Floor + 1.33f, 6f), new Vector3(27.2f, Floor + 1.13f, -1f), new Vector3(32.4f, Floor + 1.33f, 9f),
        };
        /// <summary>전망대 원판 가운데 (윗면) — 위 해치 바로 밑.</summary>
        public static readonly Vector3 Lookout = new Vector3(24.5f, Floor + 2.6f, 8f);

        // ---------------- 연결점 ----------------
        /// <summary>옆 문 깊이 (칸 중심 → 문 패널 바깥면, 하늘 면 바로 안).</summary>
        public const float DoorDepth = 3.9f;
        /// <summary>옆 문 둘 = 꿀 언덕 칸 앞 · 뒤 (외형 연결점 — 앞뒤 굴로 벌집 홀, 언덕 앞뒤 산책길).</summary>
        public static readonly (Vector3Int Cell, Vector3Int Direction)[] Doors =
        {
            (new Vector3Int(5, 0, 0), new Vector3Int(0, 0, -1)), (new Vector3Int(5, 0, 1), new Vector3Int(0, 0, 1)),
        };
        /// <summary>
        /// 아래 해치 14 (바닥 칸 모두 — 외형 아래 연결점, sg3_builder HATCHES · sg_export에서 1.6 m 평평 · 위 2.3 m 빔 확인):
        /// 꽃나무 숲 모서리 (앞뒤 산책길 왼쪽 끝 — 숲 가운데에 두면 해치 위 머리 공간 때문에 나무가 다섯 그루뿐이었음) · 정자 둘레 (돔 밖) · 앞뒤 산책길 ·
        /// 벌집 홀 (꿀 저장 기둥 앞뒤) · 라벤더 이랑 사이 넓은 길.
        /// </summary>
        public static readonly (Vector3Int Cell, Vector2 Offset)[] Hatches =
        {
            (new Vector3Int(0, 0, 0), new Vector2(3f, -2.75f)), (new Vector3Int(0, 0, 1), new Vector2(3f, 2.85f)),
            (new Vector3Int(1, 0, 0), new Vector2(-3f, -1f)), (new Vector3Int(1, 0, 1), new Vector2(-3f, 1f)),
            (new Vector3Int(2, 0, 0), new Vector2(3f, -1f)), (new Vector3Int(2, 0, 1), new Vector2(3f, 1f)),
            (new Vector3Int(3, 0, 0), new Vector2(0f, -2.6f)), (new Vector3Int(3, 0, 1), new Vector2(-2.5f, 2.6f)),
            (new Vector3Int(4, 0, 0), new Vector2(0f, -2.75f)), (new Vector3Int(4, 0, 1), new Vector2(0f, 2.85f)),
            (new Vector3Int(5, 0, 0), new Vector2(0f, 2.1f)), (new Vector3Int(5, 0, 1), new Vector2(0f, -0.9f)),
            (new Vector3Int(6, 0, 0), new Vector2(0f, 0.05f)), (new Vector3Int(6, 0, 1), new Vector2(0f, 0f)),
        };
        /// <summary>위 해치 (칸 (3, 0, 1) 천장 = 외형 위 연결점): 전망대 원판 위 (금빛 사다리 · 진주 하늘 고리).</summary>
        public static readonly Vector3Int UpHatchCell = new Vector3Int(3, 0, 1);
        public const float UpHatchDepth = 3.9f;
        public static readonly Vector3 UpHatchOffset = new Vector3(0.5f, 0f, 0f);

        // ---------------- 꿀벌 ----------------
        /// <summary>
        /// 꿀벌 고리 길 (닫힌 길, 부드럽게 이음): 벌집 입구 → 징검돌 위 → 해바라기 무리 뒤 → 정자 오른쪽 입구로 들어가 꿀 샘 · 떠 있는 수정을 한 바퀴 (그네 틀 앞) →
        /// 같은 입구로 나와 전망대 계단 앞 → 연못 위 (물 마시는 벌은 맴도는 벌로 — 길은 갈대 위로) → 벌집 입구. 정자 꽃송이 (입구 위 F + 2.75)보다 낮고 머리보다 높게.
        /// </summary>
        public static readonly Vector3[] BeePath =
        {
            new Vector3(35.3f, Floor + 1.8f, 3.6f), new Vector3(33.0f, Floor + 2.4f, 3.4f), new Vector3(29.0f, Floor + 2.6f, 2.6f), new Vector3(25.5f, Floor + 3.4f, 1.8f),
            new Vector3(21.6f, Floor + 2.6f, 2.7f), new Vector3(18.9f, Floor + 2.3f, 3.6f), new Vector3(15.2f, Floor + 2.5f, 3.0f), new Vector3(13.4f, Floor + 2.7f, 2.3f),
            new Vector3(11.4f, Floor + 2.8f, 2.2f), new Vector3(10.2f, Floor + 2.7f, 3.4f), new Vector3(10.6f, Floor + 2.6f, 4.5f), new Vector3(12.2f, Floor + 3.0f, 4.7f),
            new Vector3(14.0f, Floor + 2.6f, 4.45f), new Vector3(16.2f, Floor + 2.4f, 4.5f), new Vector3(18.9f, Floor + 2.3f, 4.4f), new Vector3(22.0f, Floor + 2.5f, 4.6f),
            new Vector3(26.0f, Floor + 2.7f, 4.5f), new Vector3(28.6f, Floor + 2.0f, 5.6f), new Vector3(29.6f, Floor + 1.9f, 6.9f), new Vector3(31.6f, Floor + 1.8f, 6.1f),
            new Vector3(34.0f, Floor + 2.0f, 4.7f),
        };
        /// <summary>제자리에서 맴도는 꿀벌 (연못가 · 벌집 입구 · 라벤더 위 · 정자 꽃 고리).</summary>
        public static readonly Vector3[] HoverBees =
        {
            new Vector3(29.2f, Floor + 0.95f, 7.9f), new Vector3(35.6f, Floor + 2.4f, 4.6f), new Vector3(47.5f, Floor + 1.2f, 2.3f), new Vector3(49.5f, Floor + 1.1f, 7.7f),
            new Vector3(7.4f, Floor + 1.2f, 4.2f),
        };
        /// <summary>미니게임 경비 꿀벌 (꿀 샘과 언덕 사이, 평소엔 벌집 쪽 +x를 봄 — 크기 1.7배).</summary>
        public static readonly Vector3 GuardBee = new Vector3(35.95f, Floor + 1.75f, 1.15f);
        public const float GuardScale = 1.7f;

        /// <summary>닫힌 길 위 점 (Catmull-Rom, u = 0 ~ 1 한 바퀴, 마디마다 같은 몫).</summary>
        public static Vector3 PathPoint(Vector3[] path, float u)
        {
            int n = path.Length;
            float f = Mathf.Repeat(u, 1f) * n;
            int i = Mathf.FloorToInt(f);
            float t = f - i;
            var p0 = path[(i - 1 + n) % n];
            var p1 = path[i % n];
            var p2 = path[(i + 1) % n];
            var p3 = path[(i + 2) % n];
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
        }

        /// <summary>닫힌 길 길이 (촘촘히 잼).</summary>
        public static float PathLength(Vector3[] path)
        {
            float len = 0f;
            var prev = PathPoint(path, 0f);
            for (int k = 1; k <= 400; k++)
            {
                var p = PathPoint(path, k / 400f);
                len += (p - prev).magnitude;
                prev = p;
            }
            return len;
        }
    }
}
