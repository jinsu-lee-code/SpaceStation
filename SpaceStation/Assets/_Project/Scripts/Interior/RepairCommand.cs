using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 3단계 수리 사령부 "반딧불 고목 치유소" 내부 값 (Blender rc3_builder와 같은 값, 원점 = 0번 칸 중심 · 칸 8 m, 바닥 3 × 3칸 + 뒤 두 줄 위 칸 3 × 2):
    /// 뒤 두 줄 = 이끼 언덕 위 진주 고목 (속 빈 줄기 + 나선 계단 → 나무집 층계참 → 꼭대기 관측대) · 가지 위 나무집 관제실 (지붕 종각 금빛 경종) · 도르래 구조 바구니 ·
    /// 나무 구멍 공방 둘 · 샘 구멍 → 물길 → 연못. 앞 한 줄 = 이끼 숲 빈터 (치유의 샘 연못 · 돌봄 오두막 · 그루터기 앞 기억 등불 다섯 · 수정 수레 · 요정 버섯 고리).
    /// 각은 x-z 평면 수학 각 (0° = +x, 90° = +z, 270° = −z = 뒤).
    /// </summary>
    public static class RepairCommand
    {
        public const float Floor = -2.08f;
        /// <summary>이끼 언덕 윗면 (줄기 속 바닥 · 공방 바닥) · 위 칸 바닥 (= 뒤 위 옆문 · 나무집 마루 · 줄기 속 나무집 층계참) · 꼭대기 관측대.</summary>
        public const float HillTop = Floor + 0.45f, SkyFloor = Floor + 8f, DeckY = 9.2f;
        /// <summary>뒤 두 줄 (z &lt; 11.95)만 위 칸 — 앞 줄 천장 3.95, 뒤 천장 11.95.</summary>
        public const float BackRowMaxZ = 11.95f, LowCeilingY = 3.95f, HighCeilingY = 11.95f;

        // ---------------- 고목 ----------------
        /// <summary>고목 축 (바닥 높이) · 줄기 속 (나선 계단 홀) 반지름 · 관측대 고리 (계단 구멍 ~ 바깥).</summary>
        public static readonly Vector3 TreeAxis = new Vector3(8f, Floor, 5.12f);
        public const float TrunkInnerRadius = 2.0f, DeckInnerRadius = 2.02f, DeckOuterRadius = 4.4f;
        /// <summary>줄기 문 (빈터 쪽, 수학 각) · 나무집 쪽 문 · 공방 둘 · 샘 구멍.</summary>
        public const float EntryAngle = 120f, TreehouseDoorAngle = 270f, SpringAngle = 45f;
        public static readonly float[] WorkshopAngles = { 205f, 300f };

        // ---------------- 나무집 관제실 · 경종 · 구조 바구니 ----------------
        /// <summary>나무집 가운데 (마루 높이) · 마루 반지름 · 방 반지름.</summary>
        public static readonly Vector3 Treehouse = new Vector3(8f, SkyFloor, -1.3f);
        public const float TreehouseDeckRadius = 2.6f, TreehouseRoomRadius = 1.4f;
        /// <summary>
        /// 금빛 경종 (따로 조각 Bell, 원점 = 매달린 꼭대기) · 마루 위 종 줄 손잡이 고리 (따로 조각 BellRope, 원점 = 고리 — 충돌 없음, 체험에서 당김).
        /// 줄은 나무집 둘레 122° (둥근 문 · 창 사이) 벽 가까이 (r 1.55) — 마루 고리길 한가운데 매달면 지나갈 수 없었음 (2026-11-13 플레이).
        /// </summary>
        public static readonly Vector3 Bell = new Vector3(8f, 10.27f, -1.3f);
        public static readonly Vector3 BellHandle = new Vector3(7.179f, 7.04f, 0.014f);
        /// <summary>구조 바구니 (따로 조각 Basket, 원점 = 바닥 윗면 가운데 — 땅 정류장) · 도르래 바퀴 (BasketRope 원점) · 바구니 반지름.</summary>
        public static readonly Vector3 Basket = new Vector3(4.66f, -1.537f, -1.3f);
        public static readonly Vector3 Pulley = new Vector3(4.66f, 11.08f, -1.3f);
        public const float BasketRadius = 0.66f;

        // ---------------- 체험 ① 구조 바구니 (InteriorRescueBasket · RescueBasketRide) ----------------
        /// <summary>바구니가 오르는 높이 (땅 정류장 바닥 → 나무집 마루).</summary>
        public static float BasketTop => SkyFloor - Basket.y;
        /// <summary>탄 사람 발이 들어가는 반지름 (엮은 벽 안쪽) · 바닥 판 윗면 (원점 위) · 걸이 줄 고리 높이 (바닥 위) · 엮은 벽 높이 · 마루 쪽 트임 (반각, +x).</summary>
        public const float BasketInnerRadius = 0.5f, BasketFloorTop = 0.06f, BasketHook = 2.08f, BasketWallHeight = 1.02f, BasketOpening = 38f;
        /// <summary>나무집 마루 난간의 바구니 문 (나무집 둘레 180°, 난간 반지름 2.48 · 너비 ±15° = 1.28 m) — 바구니가 마루에 서 있지 않으면 막음.</summary>
        public static Vector3 BasketGate => Treehouse + new Vector3(-(TreehouseDeckRadius - 0.12f), 0f, 0f);
        public const float BasketGateWidth = 1.4f;
        /// <summary>빈 바구니를 부르는 거리 (땅 정류장 가운데 · 마루 문에서, 수평 m).</summary>
        public const float BasketCallReach = 1.9f;

        // ---------------- 체험 ② 경종 → 반딧불 드론 등불 쇼 (InteriorFireflyShow · FireflyLanternShow) ----------------
        /// <summary>
        /// 드론 떼가 도는 반지름 · 높이 (고목 축에서): 잎 구름 밑 · 나무집 받침 가지 (r 4.9에서 4.6 ~ 5.2, 255° · 285°) 아래, 줄기 겉 (r ≤ 3.3)과 등불 (r 3.2 ~ 5.6) 사이 —
        /// 2026-11-15 r 4.9 둘레를 훑음: 높이 3.45 ~ 4.35는 한 바퀴 내내 빔 (4.3이면 받침 가지에 닿았음).
        /// </summary>
        public const float SwarmRadius = 4.9f, SwarmY = 3.9f, SwarmWobble = 0.3f;
        /// <summary>
        /// 반짝임 = 등불을 다 켠 자리 (떼 고리)에서 바깥으로 살짝 퍼짐 (m) — 관측대 위로 올라 흩어지면 빈터 · 나무집 마루에서 관측대 바닥 · 잎 구름에 가려 안 보였음 (2026-11-15 플레이),
        /// 고리 둘레 위쪽은 나무집 받침 가지 (255° · 285°, 높이 4.55부터)가 있어 퍼짐은 수평으로만.
        /// </summary>
        public const float FinaleOut = 0.35f;
        /// <summary>공방 안 드론 자리 (축에서 r, 언덕 위 높이) · 공방 문 안 · 문 앞 (문 가운데 줄로만 드나듦 — 비스듬히 나가면 문틀 · 벽에 닿음) · 문 앞에서 곧게 고리 높이로 (고리 높이는 한 바퀴 내내 빔).</summary>
        public const float DroneHomeRadius = 4.0f, DroneHomeLift = 1.2f, DroneDoorInRadius = 4.3f, DroneDoorRadius = 5.3f, DroneDoorLift = 1.4f;
        /// <summary>등불을 켤 때 드론이 머무는 자리 (등불 구슬에서 바깥 · 위).</summary>
        public const float LanternHoverOut = 0.45f, LanternHoverUp = 0.12f;
        /// <summary>등불을 켜지 않고 떼를 이루는 드론 수 (등불 드론 열셋과 함께).</summary>
        public const int EscortDrones = 11;

        /// <summary>등불 k를 켤 때 드론이 머무는 자리.</summary>
        public static Vector3 LanternHover(int k)
        {
            var l = Lanterns[k];
            var o = new Vector3(l.x - TreeAxis.x, 0f, l.z - TreeAxis.z).normalized;
            return l + o * LanternHoverOut + Vector3.up * LanternHoverUp;
        }

        /// <summary>고목 축 둘레 수학 각 (도, 0 ~ 360).</summary>
        public static float AngleAroundTree(Vector3 p)
        {
            float a = Mathf.Atan2(p.z - TreeAxis.z, p.x - TreeAxis.x) * Mathf.Rad2Deg;
            return a < 0f ? a + 360f : a;
        }

        /// <summary>고목 축에서 r · 각 deg (도) · 높이 y인 자리.</summary>
        public static Vector3 AroundTree(float r, float deg, float y)
        {
            return new Vector3(TreeAxis.x + r * Mathf.Cos(deg * Mathf.Deg2Rad), y, TreeAxis.z + r * Mathf.Sin(deg * Mathf.Deg2Rad));
        }

        // ---------------- 가지 등불 (경종 → 반딧불 드론 떼가 켬) · 기억 등불 (미니게임) ----------------
        /// <summary>가지에 매단 등불 빛 구슬 (따로 조각 Lantern{k}, 원점 = 구슬 가운데).</summary>
        public static readonly Vector3[] Lanterns =
        {
            new Vector3(12.01f, 7.439f, 7.108f), new Vector3(10.96f, 6.912f, 6.413f), new Vector3(8.697f, 5.289f, 8.39f), new Vector3(4.373f, 7.33f, 7.555f),
            new Vector3(3.832f, 7.485f, 3.489f), new Vector3(4.939f, 7.033f, 4.09f), new Vector3(11.658f, 7.533f, 2.231f), new Vector3(12.461f, 7.391f, 4.448f),
            new Vector3(4.058f, 7.857f, 1.416f), new Vector3(2.626f, 6.238f, 6.532f), new Vector3(11.743f, 6.0f, 8.843f), new Vector3(10.901f, 4.738f, 3.63f),
            new Vector3(6.585f, 4.361f, 8.407f),
        };
        /// <summary>고목 앞 기억 등불 다섯 빛 구슬 (따로 조각 MemLamp{k}) — 별 · 달 · 하트 · 잎 · 물방울 모양 표 (색이 아니라 자리 · 모양으로 구분).</summary>
        public static readonly Vector3[] MemoryLamps =
        {
            new Vector3(12.807f, 0.77f, 8.876f), new Vector3(10.318f, 1.37f, 10.327f), new Vector3(6.115f, 1.652f, 10.921f), new Vector3(4.767f, 1.363f, 10.293f), new Vector3(3.064f, 0.77f, 7.97f),
        };
        public static readonly string[] MemoryShapes = { "별", "달", "하트", "잎", "물방울" };
        /// <summary>그루터기 (서서 등불을 보는 자리).</summary>
        public static readonly Vector3 MemoryStump = new Vector3(8f, Floor, 14.6f);

        // ---------------- 물 ----------------
        /// <summary>연못 수면 · 샘 수면 (rc3_builder Water · Water1).</summary>
        public static readonly float[] WaterLevels = { Floor - 0.08f, HillTop + 0.12f };
        /// <summary>연못 가운데 · 반지름 (x, z) · 샘 가운데.</summary>
        public static readonly Vector3 Pond = new Vector3(12.64f, Floor - 0.08f, 16f);
        public static readonly Vector2 PondRadii = new Vector2(2.96f, 2.16f);
        public static readonly Vector3 Spring = new Vector3(10.652f, HillTop + 0.12f, 7.772f);
        /// <summary>물길이 연못에 닿는 곳 (작은 물결).</summary>
        public static readonly Vector3 StreamMouth = new Vector3(12.64f, Floor - 0.08f, 14.3f);

        // ---------------- 연결점 ----------------
        /// <summary>스폰 = 앞 빈터 (그루터기 뒤, 기억 등불 다섯 · 고목 · 관측대를 봄).</summary>
        public static readonly Vector3 Spawn = new Vector3(8.6f, Floor, 17.2f);
        public const float SpawnYaw = 180f;
        /// <summary>옆 문 깊이 (칸 중심 → 문 패널 바깥면, 하늘 면 바로 안).</summary>
        public const float DoorDepth = 3.9f;
        /// <summary>옆 문 하나 = 외형 연결점: 위 칸 (1, 1, 0) 뒤 면 (바닥 5.92 = 나무집 마루 · 옆문 앞 참).</summary>
        public static readonly (Vector3Int Cell, Vector3Int Direction)[] Doors =
        {
            (new Vector3Int(1, 1, 0), new Vector3Int(0, 0, -1)),
        };
        /// <summary>
        /// 아래 해치 아홉 (바닥 칸 모두 — 외형 아래 연결점, rc3_builder HATCHES · rc_export에서 1.6 m 평평 (2 cm) · 위 2.3 m 빔 확인): 칸 · 가운데에서 자리 · 바닥 높이
        /// (빈터 · 언덕 둘레 낮은 땅 · 언덕 윗면 (줄기 문 오른쪽 앞)).
        /// </summary>
        public static readonly (Vector3Int Cell, Vector2 Offset, float FloorY)[] Hatches =
        {
            (new Vector3Int(0, 0, 0), new Vector2(0.0f, 0.0f), -2.079f), (new Vector3Int(0, 0, 1), new Vector2(-1.3f, 0.0f), Floor), (new Vector3Int(0, 0, 2), new Vector2(-1.2f, 0.0f), Floor),
            (new Vector3Int(1, 0, 0), new Vector2(2.9f, -2.9f), -2.069f), (new Vector3Int(1, 0, 1), new Vector2(0.9f, 1.6f), HillTop), (new Vector3Int(1, 0, 2), new Vector2(-1.9f, 1.3f), Floor),
            (new Vector3Int(2, 0, 0), new Vector2(0.0f, 0.0f), Floor), (new Vector3Int(2, 0, 1), new Vector2(2.1f, -0.5f), Floor), (new Vector3Int(2, 0, 2), new Vector2(1.0f, 0.0f), Floor),
        };
        /// <summary>위 해치 둘 (외형 위 연결점): 칸 (1, 1, 0) · (1, 1, 1) 천장 = 꼭대기 관측대 위 (계단 구멍 난간과 바깥 난간 사이, 축에서 3.2 m).</summary>
        public static readonly (Vector3Int Cell, Vector2 Offset)[] UpHatches =
        {
            (new Vector3Int(1, 1, 0), new Vector2(0f, 1.92f)), (new Vector3Int(1, 1, 1), new Vector2(0f, 0.32f)),
        };
        public const float UpHatchDepth = 3.9f;
    }
}
