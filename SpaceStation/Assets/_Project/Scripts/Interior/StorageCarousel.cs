using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 저장 단지 내부 "별빛 회전목마 창고"의 회전목마 값 (Blender sc_builder와 같은 값, 템플릿 좌표 = 0번 칸 중심 원점).
    /// 캡슐 22 = 바깥 줄 12 (0 ~ 11, 각 30 × 번호) + 안 줄 10 (12 ~ 21, 각 36 × (번호 + 0.5)). 각은 x → z 쪽 (위에서 보면 반시계)으로 잼, 판도 그쪽으로 돎.
    /// 캡슐 가운데 높이 (판 위) = 0.75 / 0.95 번갈아 + 오르내림 (한 바퀴에 세 번, 이웃끼리 반대로). 판이 120°의 배수에 서면 오르내림 = 0 (쉬는 높이).
    /// 캡슐 자세: 로컬 +x = 바깥, +z = 나아가는 쪽 (안장은 기둥 뒤 −z).
    /// </summary>
    public static class StorageCarousel
    {
        public const float Floor = -1.3f;
        public const float DiskTop = Floor + 0.40f;          // 도는 판 윗면 (받침 F + 0.24 + 판 0.16)
        public static readonly Vector3 Center = new Vector3(12f, DiskTop, 4f);
        public const float DiskRadius = 4.8f;
        public const float BaseRadius = 5.28f, FenceRadius = 5.6f;
        public const float BaseTop = Floor + 0.24f;
        public const float CeilingY = DiskTop + 2.66f;       // 지붕 밑 판 아랫면 (기둥 위 + 0.02)
        public const float OuterRadius = 4.0f, InnerRadius = 2.48f;
        public const int OuterCount = 12, InnerCount = 10, Count = OuterCount + InnerCount;
        public const float LowHeight = 0.75f, HighHeight = 0.95f;
        public const float Bob = 0.12f;
        public const int BobPerTurn = 3;
        /// <summary>캡슐 가운데 → 안장 윗면 · 안장 가운데 (뒤로).</summary>
        public const float SaddleUp = 0.33f, SaddleBack = -0.28f;
        /// <summary>트임 셋 (서 · 동 · 남, 각 = x cos · z sin) · 반폭.</summary>
        public static readonly float[] Gaps = { 180f, 0f, 270f };
        public const float GapHalf = 20f;

        public static bool Outer(int i) => i < OuterCount;

        public static float Radius(int i) => Outer(i) ? OuterRadius : InnerRadius;

        /// <summary>처음 자세 (판 0°)의 각 (도).</summary>
        public static float Angle(int i) => Outer(i) ? 30f * i : 36f * (i - OuterCount + 0.5f);

        /// <summary>쉬는 높이 (판 위) — 줄 · 번호 짝으로 번갈아.</summary>
        public static float RestHeight(int i) => Parity(i) == 0 ? LowHeight : HighHeight;

        private static int Parity(int i) => Outer(i) ? i % 2 : (i - OuterCount + 1) % 2;

        /// <summary>판이 turn (도) 돌았을 때 캡슐 가운데 높이 (판 위). 낮은 캡슐이 오르면 높은 캡슐은 내려감.</summary>
        public static float Height(int i, float turn)
        {
            float s = Mathf.Sin(BobPerTurn * turn * Mathf.Deg2Rad) * Bob;
            return RestHeight(i) + (Parity(i) == 0 ? s : -s);
        }

        /// <summary>판 기준 (판 가운데 · 판 0°) 캡슐 자세 — 판이 돈 각은 판 회전이 더함.</summary>
        public static Vector3 LocalPosition(int i, float turn)
        {
            float a = Angle(i) * Mathf.Deg2Rad;
            return new Vector3(Radius(i) * Mathf.Cos(a), Height(i, turn), Radius(i) * Mathf.Sin(a));
        }

        public static Quaternion LocalRotation(int i) => Quaternion.Euler(0f, -Angle(i), 0f);

        /// <summary>판이 turn 돌았을 때 판 회전 (위에서 보면 반시계 = Unity y각 음수).</summary>
        public static Quaternion DiskRotation(float turn) => Quaternion.Euler(0f, -turn, 0f);

        /// <summary>템플릿 좌표 캡슐 자세 (가운데 · 회전).</summary>
        public static void Pose(int i, float turn, out Vector3 position, out Quaternion rotation)
        {
            var disk = DiskRotation(turn);
            position = Center + disk * LocalPosition(i, turn);
            rotation = disk * LocalRotation(i);
        }

        /// <summary>판 0°에서 안장 자리 (앉은 엉덩이, 템플릿 좌표) · 그때 바라보는 y각.</summary>
        public static Vector3 RestSaddle(int i, out float yaw)
        {
            Pose(i, 0f, out var p, out var r);
            yaw = r.eulerAngles.y;
            return p + r * new Vector3(0f, SaddleUp, SaddleBack - 0.02f);
        }
    }
}
