using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 연구원 내부 "별빛 데이터 미로"의 값 (Blender mz_builder와 같은 값, 템플릿 좌표 = 0번 칸 중심 원점, 2 × 2칸 가운데 = (4, 4)).
    /// 서버 랙 벽 세 겹 네모 고리 (반폭 <see cref="Rings"/>, 트임 = 바깥 앞 · 가운데 뒤 · 안 서쪽), 가운데 둥근 유리 연구실 (문 = 서쪽).
    /// </summary>
    public static class ResearchMaze
    {
        public const float Floor = -1.3f;
        public static readonly Vector3 Center = new Vector3(4f, Floor, 4f);
        public static readonly float[] Rings = { 6.56f, 4.8f, 3.2f };
        public const float Wall = 0.24f;
        public const float Gap = 0.72f;
        public const float WallTop = Floor + 2.6f;
        public const float LabRadius = 1.95f;
        /// <summary>갓 밑 양면 데이터 빛줄 가운데 높이.</summary>
        public const float FlowY = WallTop - 0.135f;
        /// <summary>입구 아치 (바깥 고리 앞면) z.</summary>
        public static float ArchZ => Center.z - Rings[0];

        /// <summary>길잡이 점 길 (x, z): 입구 밖 → 바깥 길 → 서 → 뒤 → 가운데 트임 → 동 → 앞 → 서 → 안 트임 → 연구실 문. 구간 i = 점 덩어리 Dots{i:00}.</summary>
        public static readonly Vector2[] Route =
        {
            new Vector2(4f, -3.1f), new Vector2(4f, -1.68f), new Vector2(-1.68f, -1.68f), new Vector2(-1.68f, 9.68f), new Vector2(4f, 9.68f),
            new Vector2(4f, 8f), new Vector2(8f, 8f), new Vector2(8f, 4f), new Vector2(8f, 0f), new Vector2(0f, 0f), new Vector2(0f, 4f), new Vector2(1.9f, 4f),
        };

        /// <summary>아래 해치 (칸 · 칸 중심에서 옮김 x, z) — 바깥 길 가운데 (길 폭 1.52, 뚜껑 1.4).</summary>
        public static readonly (Vector3Int Cell, Vector2 Offset)[] Hatches =
        {
            (new Vector3Int(0, 0, 0), new Vector2(1.0f, -1.68f)),
            (new Vector3Int(1, 0, 0), new Vector2(-0.4f, -1.68f)),
            (new Vector3Int(0, 0, 1), new Vector2(-1.68f, -1.0f)),
            (new Vector3Int(1, 0, 1), new Vector2(1.68f, -1.0f)),
        };

        /// <summary>막다른 길 화면 (화면 앞면 x, y, z · 바라보는 쪽 x, z).</summary>
        public static readonly (Vector3 Face, Vector2 Normal)[] DeadScreens =
        {
            (new Vector3(6.4f - 0.12f - 0.024f, Floor + 1.55f, 9.68f), new Vector2(-1f, 0f)),   // 뒤 바깥 길 가로막 서면 (짧은 막다른 길)
            (new Vector3(6.4f + 0.12f + 0.024f, Floor + 1.55f, 9.68f), new Vector2(1f, 0f)),    // 같은 가로막 동면 (동쪽 바깥 길 = 긴 막다른 길 끝)
            (new Vector3(3.2f - 0.12f - 0.024f, Floor + 1.55f, 8f), new Vector2(-1f, 0f)),      // 가운데 길 가로막 서면
        };

        /// <summary>입구 오른쪽 길잡이 화면 앞면 가운데 · 아치 위 간판 앞면 가운데.</summary>
        public static readonly Vector3 HintScreen = new Vector3(6f, Floor + 1.31f, ArchZ - 0.12f + 0.002f - 0.034f);
        public static readonly Vector3 SignFace = new Vector3(4f, Floor + 3.76f, ArchZ - 0.075f);

        /// <summary>길잡이 점 길 전체 길이.</summary>
        public static float RouteLength()
        {
            float L = 0f;
            for (int i = 1; i < Route.Length; i++)
                L += Vector2.Distance(Route[i - 1], Route[i]);
            return L;
        }

        /// <summary>고리 둘레 위치 s (0 ~ 8h, 앞 서쪽 모서리에서 앞 → 동 → 뒤 → 서) → 데이터 빛줄 위 자리 · 나아가는 쪽. 트임 안 (반 길이 pad 포함)이면 false.</summary>
        public static bool RingPoint(int ring, float s, float pad, out Vector3 p, out Vector3 dir)
        {
            float h = Rings[ring];
            s = Mathf.Repeat(s, 8f * h);
            int side = Mathf.Min(3, (int)(s / (2f * h)));
            float t = s - side * 2f * h;
            float cx = Center.x, cz = Center.z;
            switch (side)
            {
                case 0: p = new Vector3(cx - h + t, FlowY, cz - h); dir = Vector3.right; break;
                case 1: p = new Vector3(cx + h, FlowY, cz - h + t); dir = Vector3.forward; break;
                case 2: p = new Vector3(cx + h - t, FlowY, cz + h); dir = Vector3.left; break;
                default: p = new Vector3(cx - h, FlowY, cz + h - t); dir = Vector3.back; break;
            }
            float g = Gap + pad;
            bool inGap = ring switch
            {
                0 => side == 0 && Mathf.Abs(p.x - cx) < g,
                1 => side == 2 && Mathf.Abs(p.x - cx) < g,
                _ => side == 3 && Mathf.Abs(p.z - cz) < g,
            };
            return !inGap;
        }
    }
}
