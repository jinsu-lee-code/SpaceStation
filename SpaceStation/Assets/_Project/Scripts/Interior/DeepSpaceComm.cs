using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 4단계 심우주 통신기지 "은하수 천문 언덕" 내부 값 (Blender ds4_builder와 같은 값, 원점 = 0번 칸 중심 · 칸 8 m, 바닥 1 × 3칸 한 층 — 외형 평면 × 8):
    /// S자 은하수 강 (양 끝 = 별 샘) · 은빛 아치 다리 둘 (x 0 · 16), 천문대 돔 셋 (가운데 = 들어가는 관측실 · 망원경 · 신호 조종대, 양옆 = 창 빛나는 중계소),
    /// 뒤 전파 접시 넷 (조작반 손바퀴 · 빛 케이블 → 가까운 옆 돔), 앞 별똥별 아치, 왼쪽 앞 별자리 광장 (북두칠성 빛 줄 · 천체 모형), 오른쪽 앞 초승달 벤치 · 삼각대 망원경.
    /// 사용자 결정 (2026-11-17): 체험 = 전파 접시 신호 모으기 (<see cref="InteriorDishSignal"/>) · 별똥별 아치 소원 (<see cref="InteriorWishArch"/>),
    /// 미니게임 = 신호 맞추기 (<see cref="InteriorSignalTune"/>) · 별똥별 잡기 (<see cref="InteriorStarCatch"/>), 4단계 키트 = 성운 키트 (SM_InteriorKit_Evo4),
    /// 위 해치 없음 (하늘이 트인 방의 천장 해치는 단계별 연결통로 정리 때 — 외형 위 연결점 셋은 비워 둠).
    /// 각은 x-z 평면 수학 각 (0° = +x, 90° = +z), yaw는 Unity (0° = +z, 90° = +x).
    /// </summary>
    public static class DeepSpaceComm
    {
        public const float Floor = -2.08f;
        /// <summary>판석 길 윗면 · 별자리 광장 윗면 · 은하수 강 수면.</summary>
        public const float PathTop = Floor + 0.025f, PlazaTop = Floor + 0.07f, WaterY = Floor - 0.07f;
        /// <summary>칸 천장 (하늘 면) — 한 층이라 모두 3.95.</summary>
        public const float CeilingY = 3.95f;

        // ---------------- 돔 셋 ----------------
        /// <summary>가운데 관측실 (들어감): 가운데 · 돔 반지름 · 북 높이 (돔 껍데기는 바닥 + 북에서 시작).</summary>
        public static readonly Vector2 CenterDome = new Vector2(8f, -0.3f);
        public const float CenterDomeRadius = 1.7f, CenterDrum = 1.75f;
        public static Vector3 CenterDomeTop => new Vector3(CenterDome.x, Floor + CenterDrum + CenterDomeRadius, CenterDome.y);
        /// <summary>옆 중계소 둘 (안 들어감 — 닫힌 문 · 창 빛): 가운데 · 돔 반지름 · 돔 시작 높이 (받침 0.3 + 북 1.45).</summary>
        public static readonly Vector2[] SideDomes = { new Vector2(0f, 1.15f), new Vector2(16f, 1.15f) };
        public const float SideDomeRadius = 1.25f, SideDomeBase = Floor + 0.3f + 1.45f;
        public static Vector3 SideDomeTop(int k) => new Vector3(SideDomes[k].x, SideDomeBase + SideDomeRadius, SideDomes[k].y);

        // ---------------- 전파 접시 넷 (체험 — 신호 모으기) ----------------
        /// <summary>접시 받침 가운데 (뒤 능선, 왼쪽부터) · 빛 케이블이 가는 옆 돔.</summary>
        public static readonly Vector2[] Dishes = { new Vector2(-2.5f, 2.8f), new Vector2(2.9f, 2.95f), new Vector2(13.1f, 2.95f), new Vector2(18.5f, 2.8f) };
        public static readonly int[] DishDome = { 0, 0, 1, 1 };
        /// <summary>도는 받침 DishYaw{k} 원점 (기둥 위 — 세로축으로 돎).</summary>
        public static readonly Vector3[] DishYawPivots =
        {
            new Vector3(-2.5f, -0.628f, 2.8f), new Vector3(2.9f, -0.633f, 2.95f), new Vector3(13.1f, -0.63f, 2.95f), new Vector3(18.5f, -0.628f, 2.8f),
        };
        /// <summary>접시 DishTilt{k} 원점 (굴대 — 받침 팔 사이, 높낮이로 돎).</summary>
        public static readonly Vector3[] DishTiltPivots =
        {
            new Vector3(-2.5f, -0.158f, 2.8f), new Vector3(2.9f, -0.163f, 2.95f), new Vector3(13.1f, -0.16f, 2.95f), new Vector3(18.5f, -0.158f, 2.8f),
        };
        /// <summary>조작반 손바퀴 DishWheel{k} 원점 (바퀴 가운데 — 조작반 앞에서 접시 쪽 축으로 돎).</summary>
        public static readonly Vector3[] DishWheels =
        {
            new Vector3(-1.788f, -1.399f, 1.59f), new Vector3(3.612f, -1.362f, 1.74f), new Vector3(12.388f, -1.365f, 1.74f), new Vector3(17.788f, -1.396f, 1.59f),
        };
        /// <summary>
        /// 조작반 앞 서는 자리 (접시를 봄): 가운데 둘 = 조작반 가운데에서 0.75 m 앞, 양 끝 둘 = 옆 돔 반대쪽 비스듬히 (조작반 앞은 옆 돔 받침에서 9 cm라 몸이 밀려났음).
        /// 0.62 m (Blender 체험 자리)는 손바퀴 충돌 구 · 옆 돔 받침에 몸이 밀려 시선이 손바퀴 옆으로 빗나갔음 (2026-11-17 플레이 확인 — 양 끝 접시 둘에서 F가 안 먹힘).
        /// </summary>
        public static readonly (Vector3 At, float Yaw)[] DishStands =
        {
            (new Vector3(-2.2f, Floor, 1.0f), 350f), (new Vector3(3.88f, Floor + 0.02f, 1.284f), 329.5f),
            (new Vector3(12.12f, Floor + 0.005f, 1.284f), 30.5f), (new Vector3(18.2f, Floor, 1.0f), 10f),
        };
        /// <summary>손바퀴 충돌 구 반지름 (보고 F — 몸 0.3 + 이만큼이 서는 자리 거리 0.53 m보다 작게).</summary>
        public const float WheelUseRadius = 0.15f;
        /// <summary>
        /// 모델 그대로의 겨눔 = 신호 별 쪽 (넷이 나란히 — 외형 AIM 205°를 조금 세움, 위로 67.5°). 평소엔 저마다 흐트러져 있다가 (<see cref="DishRestYaw"/> · <see cref="DishRestTilt"/>)
        /// 손바퀴를 돌리면 이 겨눔으로 돌아옴.
        /// </summary>
        public static readonly Vector3 SignalAim = new Vector3(-0.346f, 0.924f, -0.161f);
        public static readonly float[] DishRestYaw = { -55f, 40f, -40f, 60f };
        public static readonly float[] DishRestTilt = { 16f, -14f, 12f, -18f };
        /// <summary>신호 별 (접시 넷이 겨누는 쪽 하늘 — 하늘 면 35 cm 아래, 맞출수록 밝아짐).</summary>
        public static readonly Vector3 SignalStar = new Vector3(6.59f, 3.6f, 2.22f);

        /// <summary>손바퀴 축 (조작반 → 접시, 수평).</summary>
        public static Vector3 WheelAxis(int k)
        {
            var d = new Vector3(Dishes[k].x - DishWheels[k].x, 0f, Dishes[k].y - DishWheels[k].z);
            return d.normalized;
        }

        /// <summary>접시 굴대 축 (모델 겨눔의 수평 방향에 수직 — Blender side).</summary>
        public static Vector3 TiltAxis
        {
            get
            {
                var h = new Vector3(SignalAim.x, 0f, SignalAim.z).normalized;
                return new Vector3(-h.z, 0f, h.x);
            }
        }

        /// <summary>빛 케이블 k 수평 끝점 (접시 받침 → 옆 돔 받침, 땅을 따라 — 홈 판 안 빛 줄 Cable{k}).</summary>
        public static Vector2 CableStart(int k)
        {
            var t = (SideDomes[DishDome[k]] - Dishes[k]).normalized;
            return Dishes[k] + t * 0.6f;
        }

        public static Vector2 CableEnd(int k)
        {
            var t = (SideDomes[DishDome[k]] - Dishes[k]).normalized;
            return SideDomes[DishDome[k]] - t * (SideDomeRadius + 0.38f);
        }

        // ---------------- 가운데 관측실 신호 조종대 (미니게임 — 신호 맞추기) ----------------
        /// <summary>조종대 앞 서는 자리 (돔 안 왼쪽 뒤 — 가운데 화면을 봄) · 보는 쪽 · 처음 시선 (아래로 — 책상 화면).</summary>
        public static readonly Vector3 ConsoleStand = new Vector3(7.49f, Floor + 0.035f, 0.21f);
        public const float ConsoleYaw = 315f, ConsolePitch = 36f;
        /// <summary>휜 책상 화면 셋 (돔 가운데에서 117 · 135 · 153°, 1.38 m · 바닥 + 1.08, 위로 14° 기움) — 가운데 (1) = 파형.</summary>
        public static readonly float[] ScreenAngles = { 117f, 135f, 153f };
        public const float ScreenWidth = 0.44f, ScreenHeight = 0.27f;

        /// <summary>화면 k 앞면 가운데.</summary>
        public static Vector3 ScreenCenter(int k)
        {
            var d = Out(ScreenAngles[k]);
            return new Vector3(CenterDome.x, Floor + 1.08f, CenterDome.y) + d * 1.38f + ScreenNormal(k) * 0.01f;
        }

        /// <summary>화면 k 법선 (돔 가운데 쪽 · 14° 위로).</summary>
        public static Vector3 ScreenNormal(int k)
        {
            float tilt = 14f * Mathf.Deg2Rad;
            return (-Out(ScreenAngles[k]) * Mathf.Cos(tilt) + Vector3.up * Mathf.Sin(tilt)).normalized;
        }

        /// <summary>화면 k 오른쪽 (서서 볼 때) · 위쪽 (화면 면 안).</summary>
        public static Vector3 ScreenRight(int k) => Vector3.Cross(Vector3.up, Out(ScreenAngles[k])).normalized;
        public static Vector3 ScreenUp(int k) => Vector3.Cross(ScreenNormal(k), ScreenRight(k)).normalized * -1f;

        /// <summary>조종대 시작 상자 (보이지 않음 — 휜 책상 가운데 위, 보고 F).</summary>
        public static Vector3 ConsoleUse => new Vector3(CenterDome.x, Floor + 0.95f, CenterDome.y) + Out(135f) * 1.3f;

        // ---------------- 별똥별 아치 (체험 — 소원) ----------------
        /// <summary>아치 (가운데 돔 길 · 기둥 둘 = x ± 1) · 반원 반지름 · 기둥 높이 (반원 시작) · 꼭대기.</summary>
        public static readonly Vector3 Arch = new Vector3(8f, Floor, -2.8f);
        public const float ArchRadius = 1.0f, ArchLeg = 1.7f;
        public static Vector3 ArchTop => new Vector3(Arch.x, Floor + ArchLeg + ArchRadius, Arch.z);
        /// <summary>
        /// 아치 앞 서는 자리 (판석 길) · 보는 쪽 (아치 너머 가운데 돔 · 강) · 처음 시선 (위로 38° — 아치 꼭대기와 돔 지붕 사이로 하늘이 보임).
        /// 아치 밑 (z −2.75)은 가운데 돔 발코니 턱이 눈 위 0.45 m 앞이라 하늘이 가려졌음 (2026-11-17 플레이 확인).
        /// </summary>
        public static readonly Vector3 WishStand = new Vector3(8f, PathTop, -3.4f);
        public const float WishYaw = 0f, WishPitch = -38f;
        /// <summary>소원 별똥별 길 (하늘 면 아래 — 아치 너머 왼쪽 위에서 가운데 돔 지붕 위를 지나 돔 오른쪽 뒤 강 굽이로) · 빠르기.</summary>
        public static readonly Vector3 WishStarFrom = new Vector3(5.4f, 3.6f, 0.1f), WishStarTo = new Vector3(11f, 1.3f, 0.6f);
        public const float WishStarSpeed = 3.6f;
        /// <summary>별빛이 번지기 시작하는 강 자리 (별똥별이 닿은 쪽 — 오른쪽 굽이).</summary>
        public static readonly Vector2 WishRiverPoint = new Vector2(11.1f, 0.55f);

        // ---------------- 별자리 광장 (미니게임 — 별똥별 잡기) ----------------
        /// <summary>광장 가운데 · 반지름 · 천체 모형 Orrery (원점 = 축 위 해 — 천천히 돎).</summary>
        public static readonly Vector2 Plaza = new Vector2(4.55f, -2.45f);
        public const float PlazaRadius = 1.15f;
        public static readonly Vector3 Orrery = new Vector3(4.55f, -1.08f, -2.45f);
        /// <summary>광장 앞 서는 자리 · 보는 쪽 (광장 너머 강 · 하늘) · 처음 시선 (위로).</summary>
        public static readonly Vector3 CatchStand = new Vector3(4.55f, -2.01f, -3.31f);
        public const float CatchYaw = 0f, CatchPitch = -16f;
        /// <summary>별똥별이 나타나는 하늘 상자 (x · z 범위 · 높이 — 선 자리 처음 화면 안, 가운데 돔 · 접시를 비킴) · 떨어지는 광장 (가운데 · 반지름 · 높이).</summary>
        public static readonly Vector2 CatchSpawnX = new Vector2(2.5f, 6f), CatchSpawnZ = new Vector2(-0.2f, 1.4f), CatchSpawnY = new Vector2(2.6f, 3.3f);
        public const float CatchLandRadius = 1f, CatchLandY = PlazaTop + 0.12f;

        // ---------------- 강 위 별 셋 · 동물 ----------------
        /// <summary>강 위 떠 있는 별 StarFloat{k} (원점 = 수면 — 천천히 흔들리며 돎).</summary>
        public static readonly Vector3[] FloatingStars = { new Vector3(3.526f, WaterY, -0.673f), new Vector3(8.002f, WaterY, 2.17f), new Vector3(12.555f, WaterY, -0.732f) };
        /// <summary>
        /// (모델, 자리 (모델 원점), 바라보는 쪽 수학 각) — Blender ds_critters.py가 고름: 발 넷 (모델 발 자리를 바라보는 쪽으로 돌림) 땅 높이 차 ≤ 1.2 cm,
        /// 원점 = 가장 높은 발 땅 + 모델 발바닥 (발바닥 가장 낮은 점이 면 속 1 cm 안), 해치 틀 · 주민 자리 · 체험 자리 · 길 · 물가에서 떨어져, 풀 · 꽃 · 자갈은 비움 (빌더 CRITTERS).
        /// 외형 사슴은 몸 반경 0.45 m가 들어갈 자리가 없어 (접시 · 돔 · 강 · 주민 자리) 아기 사슴 하나 — 부엉이 · 고슴도치는 아래 (2026-11-17 내부 모델).
        /// </summary>
        public static readonly (string Kind, Vector3 At, float Facing)[] Fawns =
        {
            ("Fawn", new Vector3(2.5f, Floor - 0.0082f, 1.4f), 28.6f),
        };
        /// <summary>토끼 넷 = 뒤 강가 풀밭 · 가운데 돔 오른쪽 · 오른쪽 다리 건너 · 왼쪽 다리 건너.</summary>
        public static readonly (string Kind, Vector3 At, float Facing)[] Bunnies =
        {
            ("Bunny", new Vector3(5.241f, Floor + 0.0278f, 2.694f), 345.9f), ("Bunny", new Vector3(11.133f, Floor + 0.0176f, -1.053f), 134f),
            ("Bunny", new Vector3(14.541f, Floor + 0.004f, -0.106f), 215.4f), ("Bunny", new Vector3(1.253f, Floor + 0.0082f, -0.209f), 332.8f),
        };
        /// <summary>
        /// 부엉이 둘 (critter_builder Critter_Owl — 사용자 2026-11-17 모델 시트 2 "좋아 이대로 통신기지 내부에 넣어줘") = 가운데 돔 발코니 턱 (외형 돔 둘레 발코니의 부엉이),
        /// 문 양옆 앞쪽 난간 기둥 사이 (기둥 = −60° + 12.5° 간격)에서 바깥을 봄. 돔 가운데에서 1.845 m · 크기 0.9배 = 꽁지 끝 · 등이 돔 벽 (1.7 m)에,
        /// 가슴이 위 난간 (1.97 m, 턱 위 0.215 m)에 닿지 않음 (Blender 시험 배치 crit_place.py — 1.865 m · 1배는 가슴이 난간에 1.5 cm 묻혔음). 원점 = 발 = 턱 윗면.
        /// </summary>
        public static readonly (string Kind, Vector3 At, float Facing)[] Owls =
        {
            ("Owl", new Vector3(6.6129f, Floor + 1.795f, -1.5165f), 221.25f), ("Owl", new Vector3(9.3871f, Floor + 1.795f, -1.5165f), 318.75f),
        };
        /// <summary>부엉이 크기 (모델 키 0.45 m → 0.4 m — 발코니 턱 폭에 맞춤).</summary>
        public const float OwlScale = 0.9f;
        /// <summary>
        /// 고슴도치 둘 (critter_builder Critter_Hedgehog) = 오른쪽 옆 돔 너머 풀밭 (강 쪽을 봄) · 왼쪽 별자리 광장 왼쪽 앞 풀밭 (광장 앞을 봄 — 사용자 "고슴도치 하나는 왼쪽 풀밭으로",
        /// 오른쪽 접시 뒤에서 옮김). Blender crit_place.py가 고름 (발 넷 땅 높이 차 ≤ 1.2 cm): 지금 풀 · 꽃 · 자갈에서 떨어진 자리 (오른쪽 0.57 m · 왼쪽 0.43 m —
        /// 왼쪽은 여기뿐)라 빌더 CRITTERS에 막음 (0.45 · 0.3 m)을 더해도 풀꽃이 다시 섞이지 않음 (빌드 지문 같음 — 내부 FBX 그대로), 풀잎이 몸에 8 cm 안으로 오지 않음.
        /// </summary>
        public static readonly (string Kind, Vector3 At, float Facing)[] Hedgehogs =
        {
            ("Hedgehog", new Vector3(18.65f, Floor + 0.0006f, 1.45f), 229.8f), ("Hedgehog", new Vector3(3.15f, Floor - 0.0021f, -2.65f), 332.8f),
        };

        // ---------------- 연결점 ----------------
        /// <summary>스폰 = 가운데 앞문 안 판석 길 (별똥별 아치 너머 가운데 관측실 · 강 · 뒤 접시를 봄).</summary>
        public static readonly Vector3 Spawn = new Vector3(8f, PathTop, -3.35f);
        public const float SpawnYaw = 0f;
        /// <summary>옆 문 깊이 (칸 중심 → 문 패널 바깥면, 하늘 면 바로 안).</summary>
        public const float DoorDepth = 3.9f;
        /// <summary>옆 문 여덟 = 외형 연결점: 칸마다 앞 · 뒤 + 양 끝.</summary>
        public static readonly (Vector3Int Cell, Vector3Int Direction)[] Doors =
        {
            (new Vector3Int(0, 0, 0), new Vector3Int(0, 0, -1)), (new Vector3Int(1, 0, 0), new Vector3Int(0, 0, -1)), (new Vector3Int(2, 0, 0), new Vector3Int(0, 0, -1)),
            (new Vector3Int(0, 0, 0), new Vector3Int(0, 0, 1)), (new Vector3Int(1, 0, 0), new Vector3Int(0, 0, 1)), (new Vector3Int(2, 0, 0), new Vector3Int(0, 0, 1)),
            (new Vector3Int(0, 0, 0), new Vector3Int(-1, 0, 0)), (new Vector3Int(2, 0, 0), new Vector3Int(1, 0, 0)),
        };
        /// <summary>
        /// 아래 해치 셋 (칸마다, ds4_builder HATCHES — ds_export에서 1.6 m 틀 9 × 9 광선이 한 판 · 위 2.3 m 빔 · 몸 높이 0.55 m 안 빔):
        /// 양 끝 = 끝 문 안쪽 풀밭 (옆 돔 · 별 샘 사이 — 강이 비스듬히 지나 다른 데는 1.9 m 틀 자리가 없음), 가운데 = 앞문 길 오른쪽.
        /// </summary>
        public static readonly (Vector3Int Cell, Vector2 Offset, float FloorY)[] Hatches =
        {
            (new Vector3Int(0, 0, 0), new Vector2(-2.45f, -0.05f), Floor), (new Vector3Int(1, 0, 0), new Vector2(2.65f, -2.45f), Floor),
            (new Vector3Int(2, 0, 0), new Vector2(2.45f, -0.05f), Floor),
        };

        private static Vector3 Out(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }
    }
}
