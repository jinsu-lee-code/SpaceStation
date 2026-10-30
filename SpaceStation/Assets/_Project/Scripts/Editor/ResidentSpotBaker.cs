using System.Collections.Generic;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 11-11 템플릿마다 주민 자리를 정해 IT_*.asset에 저장한다 (메뉴 SpaceStation/Interior/Bake Resident Spots).
    /// 템플릿 프리팹을 멀리 임시로 놓고 모든 메시에 MeshCollider를 붙여 실제 모델 모양으로 찾는다:
    /// - 앉기: 바닥 위 0.35~0.65m의 위를 향한 면(침대 · 소파 · 벤치 · 의자 · 상자) + 머리 위 · 다리 앞 공간이 빈 곳. 등받이(벽 · 등판)가 있는 쪽을 등지게
    /// - 작업: 빈 바닥에서 손 높이(1.1m) 0.3~0.75m 앞에 화면 · 장비(Screen · Device 재질)가 있는 곳, 그쪽을 바라봄
    /// - 서기: 빈 바닥 (머리까지 빈 곳), 방 가운데를 바라봄
    /// 문 자리 · 해치 · 들어오는 곳 근처는 비운다. 서로 0.9m 이상 떨어지게 고르고(가장 먼 점 우선), 앉기를 먼저 채운다.
    /// 일터 특성 · 여가 방 표는 아래 <see cref="WorkTraits"/> · <see cref="LeisureRooms"/>.
    /// 템플릿 모양이 바뀌면(Build Templates) 이 메뉴도 다시 실행한다.
    /// </summary>
    public static class ResidentSpotBaker
    {
        private const string DataFolder = "Assets/_Project/Data/Interior/";
        private static readonly Vector3 Far = new Vector3(20000f, 0f, 20000f);
        private const float Step = 0.4f;
        private const float MinSpacing = 0.9f;
        private const float Floor = Interior.InteriorGeometry.FloorOffset;

        /// <summary>낮에 이 방에서 일하는 특성 (템플릿 이름 → 특성).</summary>
        private static readonly Dictionary<string, ResidentTrait[]> WorkTraits = new Dictionary<string, ResidentTrait[]>
        {
            { "MaintenanceBay", new[] { ResidentTrait.Technician, ResidentTrait.Mechanic } },
            { "DamageControl", new[] { ResidentTrait.Technician } },
            { "ArmorBulkhead", new[] { ResidentTrait.Technician } },
            { "Battery", new[] { ResidentTrait.Technician } },
            { "Solar", new[] { ResidentTrait.Technician } },
            { "FuelCell", new[] { ResidentTrait.Technician } },
            { "Turret", new[] { ResidentTrait.Technician } },
            { "Refinery", new[] { ResidentTrait.Mechanic } },
            { "MiningDock", new[] { ResidentTrait.Mechanic } },
            { "CargoTerminal", new[] { ResidentTrait.Mechanic } },
            { "Storage", new[] { ResidentTrait.Mechanic } },
            { "ResearchLab", new[] { ResidentTrait.Scientist } },
            { "FusionReactor", new[] { ResidentTrait.Scientist } },
            { "Shield", new[] { ResidentTrait.Scientist } },
            { "Medical", new[] { ResidentTrait.Scientist } },
            { "Farm", new[] { ResidentTrait.Gardener } },
            { "Oxygen", new[] { ResidentTrait.Gardener } },
            { "WaterRecycler", new[] { ResidentTrait.Gardener } },
            // 12-5 새 기본 모듈 (기존 비슷한 방과 같은 특성)
            { "Sensor", new[] { ResidentTrait.Technician } },
            { "DataCenter", new[] { ResidentTrait.Scientist } },
            { "Quarantine", new[] { ResidentTrait.Scientist } },
            { "School", new[] { ResidentTrait.Scientist } },
            { "Protein", new[] { ResidentTrait.Gardener } },
            { "WaterEx", new[] { ResidentTrait.Gardener } },
            { "Radiator", new[] { ResidentTrait.Technician } },   // 사용자 결정 2026-10-22: 배터리 · 태양광 · 연료전지와 같은 기술자
            { "CommRelay", new[] { ResidentTrait.Scientist } },   // 사용자 결정 2026-10-22: 신호 해독 = 연구원 (연구소 · 데이터 센터와 같음)
            // 진화 모듈 (재료 모듈과 같은 특성)
            { "EvoResearch", new[] { ResidentTrait.Scientist } }, // 연구 단지 = 연구소 ×2
            { "EvoEco", new[] { ResidentTrait.Gardener } },       // 생태 모듈 = 농장 · 산소 · 물 재활용 (모두 정원사)
            { "EvoPowerBank", new[] { ResidentTrait.Technician } }, // 축전 뱅크 = 배터리 ×3 (기술자)
            { "EvoWelfare", new[] { ResidentTrait.Scientist } },    // 복지 센터 = 의무실 (연구원) + 휴게실 (쉬는 방 — LeisureRooms)
            { "EvoCulture", new[] { ResidentTrait.Scientist } },    // 문화 센터 = 학교 (연구원 = 선생님) + 휴게실 (쉬는 방)
            { "EvoSolar", new[] { ResidentTrait.Technician } },     // 태양광 어레이 = 태양광 ×4 (기술자)
            { "EvoFusion", new[] { ResidentTrait.Scientist } },     // 융합 발전소 = 핵융합로 (연구원) + 물 재활용기
            { "EvoHydrogen", new[] { ResidentTrait.Technician, ResidentTrait.Gardener } },   // 수소 플랜트 = 연료전지 (기술자) + 물 재활용기 · 산소 (정원사)
            { "EvoFarm", new[] { ResidentTrait.Gardener } },        // 수직 농장 = 수경 농장 ×2 + 단백질 배양조 (모두 정원사)
            { "EvoMineral", new[] { ResidentTrait.Mechanic } },     // 광물 처리 단지 = 채굴 도킹 + 제련소 (모두 정비공)
            { "EvoShield", new[] { ResidentTrait.Scientist } },     // 실드 매트릭스 = 실드 발생기 ×3 (연구원)
            { "EvoDefense", new[] { ResidentTrait.Technician } },   // 방어 포대 = 포탑 ×2 + 장갑 격벽 (모두 기술자)
        };

        /// <summary>낮에 일터가 없는 주민이 쉬러 오는 방 (12-5 행정 관제소 = 민원인 · 여객 도킹 포트 = 대합실에서 창밖 구경).</summary>
        private static readonly HashSet<string> LeisureRooms = new HashSet<string> { "Recreation", "RotatingRing", "Core", "Commerce", "Admin", "Passenger", "EvoWelfare", "EvoCulture", "EvoGravity" };

        /// <summary>12-5 일하는 자리를 일터가 없는 주민도 쓰는 방 (사용자 결정 2026-10-20: 행정 관제소 창구 직원 · 2026-10-21: 방사선 차폐실 비축 물자 관리 ·
        /// 여객 도킹 포트 안내 직원).</summary>
        private static readonly HashSet<string> OpenWorkRooms = new HashSet<string> { "Admin", "Shelter", "Passenger" };

        /// <summary>12-5 쉬러 온 사람 상태 글.</summary>
        private static readonly Dictionary<string, string> VisitStatus = new Dictionary<string, string> { { "Admin", "민원 보는 중" }, { "Passenger", "우주 구경 중" } };

        /// <summary>12-5 일하는 사람 상태 글 (없으면 "일하는 중").</summary>
        private static readonly Dictionary<string, string> WorkStatus = new Dictionary<string, string> { { "Shelter", "물자 관리 중" }, { "Passenger", "여객 안내 중" } };

        /// <summary>
        /// 12-5 자리를 직접 정한 방 (찾아낸 자리로는 안 되는 방). 행정 관제소: 주민이 정해진 길로 발권기 · 창구를 오가므로
        /// (<see cref="Interior.InteriorQueueBoard"/>) 일하는 자리 = 창구 1 · 2 · 3 직원 의자 → 관제 콘솔 4, 쉬는 자리 = 대기 의자 8 (admin_builder ANCHORS).
        /// </summary>
        private static readonly Dictionary<string, System.Func<List<ResidentSpot>>> FixedSpots = new Dictionary<string, System.Func<List<ResidentSpot>>>
        {
            { "Admin", AdminSpots },
            { "Shelter", ShelterSpots },
            { "Passenger", PassengerSpots },
            { "Radiator", RadiatorSpots },
            { "CommRelay", CommRelaySpots },
            { "EvoResearch", EvoResearchSpots },
            { "EvoTower", EvoTowerSpots },
            { "EvoEco", EvoEcoSpots },
            { "EvoPowerBank", EvoPowerBankSpots },
            { "EvoWelfare", EvoWelfareSpots },
            { "EvoCulture", EvoCultureSpots },
            { "EvoSolar", EvoSolarSpots },
            { "EvoFusion", EvoFusionSpots },
            { "EvoHydrogen", EvoHydrogenSpots },
            { "EvoGravity", EvoGravitySpots },
            { "EvoFarm", EvoFarmSpots },
            { "EvoMineral", EvoMineralSpots },
            { "EvoShield", EvoShieldSpots },
            { "EvoDefense", EvoDefenseSpots },
        };

        /// <summary>
        /// 진화 1단계 방어 포대 (db_builder SEATS): 기술자 — 포탑실 바깥 모서리 사수 콘솔 넷 (콘솔을 봄, 문 자리 가로 ±1.75 밖) ·
        /// 레이더 상황판 양 끝 둘 (상황판을 봄) · 벙커 틈 창 아래 책상 넷 (책상을 봄). 상황판 옆길 (z ±0.65 ~ 1.67) 1.3m 이상.
        /// </summary>
        private static List<ResidentSpot> EvoDefenseSpots()
        {
            var list = new List<ResidentSpot>
            {
                new ResidentSpot(new Vector3(-2.057f, Floor, -2.057f), -135f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(-2.057f, Floor, 2.057f), -45f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(18.057f, Floor, -2.057f), 135f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(18.057f, Floor, 2.057f), 45f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(6.55f, Floor, 0f), 90f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(9.45f, Floor, 0f), -90f, ResidentPose.Work, work: true),
            };
            foreach (float x in new[] { 5.55f, 10.45f })
            {
                list.Add(new ResidentSpot(new Vector3(x, Floor, -1.97f), 180f, ResidentPose.Work, work: true));
                list.Add(new ResidentSpot(new Vector3(x, Floor, 1.97f), 0f, ResidentPose.Work, work: true));
            }
            return list;
        }

        /// <summary>
        /// 진화 1단계 실드 매트릭스 (sm_builder SEATS): 연구원 — 두 팔 벽쪽 관제 콘솔 넷 (콘솔을 봄, 사이 통로 3.4m) · 팔 끝 발생 노드 관리 둘 (노드 바깥쪽, 끝 문 자리 앞 밖).
        /// </summary>
        private static List<ResidentSpot> EvoShieldSpots()
        {
            return new List<ResidentSpot>
            {
                new ResidentSpot(new Vector3(5.2f, Floor, -2.03f), 180f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(5.2f, Floor, 2.03f), 0f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(-2.03f, Floor, 5.2f), -90f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(2.03f, Floor, 5.2f), 90f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(9.15f, Floor, 0f), -90f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(0f, Floor, 9.15f), 180f, ResidentPose.Work, work: true),
            };
        }

        /// <summary>
        /// 진화 1단계 광물 처리 단지 (mc_builder SEATS): 정비공 — 용광로 틈 창 앞 둘 (용광로를 봄) · 분쇄기 양옆 둘 (분쇄기를 봄) ·
        /// 하역장 관제 책상 둘 (−z 서쪽 · +z 동쪽, 문 자리 밖). 모두 가운데 줄 · 책상 앞이라 양옆 길 1.7m 이상 남김.
        /// </summary>
        private static List<ResidentSpot> EvoMineralSpots()
        {
            return new List<ResidentSpot>
            {
                new ResidentSpot(new Vector3(1.85f, Floor, 0f), -90f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(14.15f, Floor, 0f), 90f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(6.35f, Floor, 0f), 90f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(9.65f, Floor, 0f), -90f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(5.0f, Floor, -1.97f), 180f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(11.0f, Floor, 1.97f), 0f, ResidentPose.Work, work: true),
            };
        }

        /// <summary>
        /// 진화 1단계 수직 농장 (vf_builder SEATS): 정원사 — 1층 3단 선반 양쪽 여섯 (선반을 봄, 창가 길 안쪽 가장자리) ·
        /// 2층 창가 모종 선반 앞 넷 (기둥 받침과 사이 1.3m 남김) · 꼭대기 관제 책상 의자 (앉아서 일함).
        /// </summary>
        private static List<ResidentSpot> EvoFarmSpots()
        {
            var list = new List<ResidentSpot>();
            foreach (float x in new[] { 3.8f, 6.15f, 8.5f })
            {
                list.Add(new ResidentSpot(new Vector3(x, Floor, -1.25f), 0f, ResidentPose.Work, work: true));
                list.Add(new ResidentSpot(new Vector3(x, Floor, 1.25f), 180f, ResidentPose.Work, work: true));
            }
            foreach (float x in new[] { 4.2f, 7.4f })
            {
                list.Add(new ResidentSpot(new Vector3(x, 8f + Floor, -2.1f), 180f, ResidentPose.Work, work: true));
                list.Add(new ResidentSpot(new Vector3(x, 8f + Floor, 2.1f), 0f, ResidentPose.Work, work: true));
            }
            list.Add(new ResidentSpot(new Vector3(-1.95f, 16f + Floor + 0.5f, -1.95f), -135f, ResidentPose.Sit, work: true));
            return list;
        }

        /// <summary>
        /// 진화 1단계 중력 거주구 (gh_builder SEATS): 사는 사람 · 쉬러 온 사람 (회전 링 = 쉬는 방) — 탑 둘레 벤치 일곱 (바깥을 봄) ·
        /// 원형 통로 창가 벤치 열여섯 (창을 봄) · 골목 집 앞 이웃 넷 (집을 봄) · 둥근 탁자 의자 넷 (탁자를 봄) · 전망실 창가 둘 (바깥을 봄 — 탑 둘레 길 1.5m 남김).
        /// 곡면 벤치 · 둥근 탁자는 찾아낸 자리로 잘 안 잡혀 고정 자리로.
        /// </summary>
        private static List<ResidentSpot> EvoGravitySpots()
        {
            var c = new Vector3(8f, 0f, 8f);
            var list = new List<ResidentSpot>();
            void Ring(float r, float deg, float y, ResidentPose pose)
            {
                float a = deg * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                list.Add(new ResidentSpot(c + d * r + Vector3.up * y, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pose));
            }
            foreach (var (a0, a1) in new[] { (-40f, 26f), (34f, 106f), (114f, 220f) })
            {
                int n = a1 - a0 < 80f ? 2 : 3;
                for (int j = 0; j < n; j++)
                    Ring(1.8f, a0 + (a1 - a0) * (j + 0.5f) / n, Floor + 0.45f, ResidentPose.Sit);
            }
            for (int q = 0; q < 4; q++)
            {
                foreach (var (b0, b1) in new[] { (16f, 37f), (53f, 74f) })
                {
                    foreach (float t in new[] { 0.3f, 0.7f })
                        Ring(6.93f, q * 90f + b0 + (b1 - b0) * t, Floor + 0.45f, ResidentPose.Sit);
                }
            }
            foreach (float z in new[] { 26.95f, 29.05f })
            {
                list.Add(new ResidentSpot(new Vector3(6.75f, Floor, z), -90f, ResidentPose.Stand));
                list.Add(new ResidentSpot(new Vector3(9.25f, Floor, z), 90f, ResidentPose.Stand));
            }
            var table = new Vector3(8f, 0f, 21f);
            foreach (float deg in new[] { 45f, 135f, 225f, 315f })
            {
                float a = deg * Mathf.Deg2Rad;
                var p = new Vector3(table.x + Mathf.Cos(a) * 0.95f, Floor + 0.45f, table.z + Mathf.Sin(a) * 0.95f);
                list.Add(new ResidentSpot(p, Mathf.Atan2(table.x - p.x, table.z - p.z) * Mathf.Rad2Deg, ResidentPose.Sit));
            }
            foreach (float deg in new[] { 200f, 340f })
                Ring(3.0f, deg, Floor + 2.9f, ResidentPose.Stand);   // 전망실 (엘리베이터 문 270° · 망원경 90°를 비킴)
            return list;
        }

        /// <summary>
        /// 진화 1단계 수소 플랜트 (hp_builder): 기술자 · 정원사 — 전기분해조 대각선 둘 (통을 봄) · 연료전지 더미 양옆 둘 (빛 고리 자리 x 8.15를 비킴) ·
        /// 출력 장 앞 · 흐름 제어대 앞 · 물 구체 뒤 · 산소 구체 압력계 앞. 걸어 다닐 길 (1.6m 이상) 밖.
        /// </summary>
        private static List<ResidentSpot> EvoHydrogenSpots()
        {
            return new List<ResidentSpot>
            {
                new ResidentSpot(new Vector3(1.3f, Floor, 1.3f), -135f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(-1.3f, Floor, -1.3f), 45f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(7.2f, Floor, 1.3f), 180f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(9.2f, Floor, -1.3f), 0f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(5.2f, Floor, -2.05f), 180f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(5.4f, Floor, 1.85f), 0f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(-1.75f, Floor, 6.55f), 180f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(1.75f, Floor, 3.95f), 0f, ResidentPose.Work, work: true),
            };
        }

        /// <summary>
        /// 진화 1단계 융합 발전소 (fs_builder): 연구원 — 제어대 의자 셋 (창 쪽 화면을 봄 — 앉아서 일함) · 냉각수 펌프 앞 둘 · 자석 전원 장 앞 둘 ·
        /// 남는 물 탱크 앞 · 구체와 융합로 사이 가운데 둘 (융합로를 봄). 사용자 2026-10-29 "길을 지나다닐 수가 없네": 주민은 충돌체가 있어
        /// 융합로 옆 길 (폭 1.3m) · 구체 옆 길에는 자리를 두지 않음. 모서리 정수 필터는 문 자리 사이라 자리 없음.
        /// </summary>
        private static List<ResidentSpot> EvoFusionSpots()
        {
            var list = new List<ResidentSpot>();
            foreach (float x in new[] { 4.05f, 4.9f, 5.75f })
                list.Add(new ResidentSpot(new Vector3(x, Floor + 0.45f, -1.85f), 180f, ResidentPose.Sit, work: true));
            list.Add(new ResidentSpot(new Vector3(4.25f, Floor, 1.75f), 0f, ResidentPose.Work, work: true));
            list.Add(new ResidentSpot(new Vector3(5.6f, Floor, 1.75f), 0f, ResidentPose.Work, work: true));
            list.Add(new ResidentSpot(new Vector3(10.45f, Floor, -2.05f), 180f, ResidentPose.Work, work: true));
            list.Add(new ResidentSpot(new Vector3(11.8f, Floor, -2.05f), 180f, ResidentPose.Work, work: true));
            list.Add(new ResidentSpot(new Vector3(10.4f, Floor, 1.5f), 0f, ResidentPose.Work, work: true));
            list.Add(new ResidentSpot(new Vector3(5.15f, Floor, 0f), 90f, ResidentPose.Work, work: true));
            list.Add(new ResidentSpot(new Vector3(10.85f, Floor, -0.45f), Mathf.Atan2(8f - 10.85f, 0.45f) * Mathf.Rad2Deg, ResidentPose.Work, work: true));   // 물 탱크 앞 사람과 1.4m 띄움
            return list;
        }

        /// <summary>
        /// 진화 1단계 태양광 어레이 (sa_builder): 기술자 — 관제석 의자 셋 (곡면 책상 바깥, 추적 가대를 봄 — 앉아서 일함) ·
        /// 변환 장치 장 앞 둘 (창 쪽을 봄) · 패널 정비대 앞 (예비 패널을 봄) · 공구 수레 앞 · 해 추적 홀로 탁자 양쪽 둘.
        /// 곡면 책상 · 둥근 탁자는 찾아낸 자리로 잘 안 잡혀 고정 자리로.
        /// </summary>
        private static List<ResidentSpot> EvoSolarSpots()
        {
            var c = new Vector3(4f, 0f, 4f);
            var list = new List<ResidentSpot>();
            foreach (float deg in new[] { 260f, 270f, 280f })
            {
                float a = deg * Mathf.Deg2Rad;
                var p = new Vector3(c.x + Mathf.Cos(a) * 5.6f, Floor + 0.45f, c.z + Mathf.Sin(a) * 5.6f);
                list.Add(new ResidentSpot(p, Mathf.Atan2(c.x - p.x, c.z - p.z) * Mathf.Rad2Deg, ResidentPose.Sit, work: true));
            }
            list.Add(new ResidentSpot(new Vector3(10.15f, Floor, 3.05f), 90f, ResidentPose.Work, work: true));
            list.Add(new ResidentSpot(new Vector3(10.15f, Floor, 4.95f), 90f, ResidentPose.Work, work: true));
            list.Add(new ResidentSpot(new Vector3(3.6f, Floor, 8.3f), 0f, ResidentPose.Work, work: true));
            list.Add(new ResidentSpot(new Vector3(2.3f, Floor, 9.0f), 0f, ResidentPose.Work, work: true));
            list.Add(new ResidentSpot(new Vector3(-0.6f, Floor, 3.15f), 0f, ResidentPose.Work, work: true));
            list.Add(new ResidentSpot(new Vector3(-0.6f, Floor, 4.85f), 180f, ResidentPose.Work, work: true));
            return list;
        }

        /// <summary>
        /// 진화 1단계 문화 센터 (cc_builder): 일하는 자리 = 연구원 (선생님) — 칠판 앞 (칠판을 봄) · 투영기 옆 (투영기를 봄).
        /// 쉬는 자리 (쉬는 방) = 투영실 곡면 벤치 여덟 (안 · 바깥 겹 네 쪽, 투영기를 봄) · 책상 의자 여섯 (칠판을 봄) · 보드게임 의자 여섯 (탁자를 봄) ·
        /// 안락의자 둘 · 쉼터 벤치 둘 (창 · 투영실을 봄) + 서기 둘. 곡면 벤치 · 둥근 탁자 의자는 찾아낸 자리로 잘 안 잡혀 고정 자리로.
        /// </summary>
        private static List<ResidentSpot> EvoCultureSpots()
        {
            var c = new Vector3(4f, 0f, 4f);
            var list = new List<ResidentSpot>
            {
                new ResidentSpot(new Vector3(4f, Floor, -2.75f), 180f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(4f, Floor, 3.15f), 0f, ResidentPose.Work, work: true),
            };
            foreach (float r in new[] { 1.65f, 2.55f })
            {
                foreach (float deg in new[] { 45f, 135f, 225f, 315f })
                {
                    float a = deg * Mathf.Deg2Rad;
                    var p = new Vector3(c.x + Mathf.Cos(a) * r, Floor + 0.45f, c.z + Mathf.Sin(a) * r);
                    list.Add(new ResidentSpot(p, Mathf.Atan2(c.x - p.x, c.z - p.z) * Mathf.Rad2Deg, ResidentPose.Sit));
                }
            }
            foreach (float z in new[] { -2.1f, -0.85f })
            {
                foreach (float x in new[] { 2.3f, 4.0f, 5.7f })
                    list.Add(new ResidentSpot(new Vector3(x, Floor + 0.45f, z + 0.62f), 180f, ResidentPose.Sit));
            }
            foreach (var (gx, gz) in new[] { (2.4f, 9.6f), (5.6f, 9.6f) })
            {
                foreach (float deg in new[] { 90f, 210f, 330f })
                {
                    float a = deg * Mathf.Deg2Rad;
                    var p = new Vector3(gx + Mathf.Cos(a) * 0.82f, Floor + 0.42f, gz + Mathf.Sin(a) * 0.82f);
                    list.Add(new ResidentSpot(p, Mathf.Atan2(gx - p.x, gz - p.z) * Mathf.Rad2Deg, ResidentPose.Sit));
                }
            }
            list.Add(new ResidentSpot(new Vector3(9.55f, Floor + 0.42f, 3f), 90f, ResidentPose.Sit));
            list.Add(new ResidentSpot(new Vector3(9.55f, Floor + 0.42f, 5f), 90f, ResidentPose.Sit));
            list.Add(new ResidentSpot(new Vector3(-2.9f, Floor + 0.45f, 3.3f), 90f, ResidentPose.Sit));
            list.Add(new ResidentSpot(new Vector3(-2.9f, Floor + 0.45f, 4.7f), 90f, ResidentPose.Sit));
            list.Add(new ResidentSpot(new Vector3(4f, Floor, 9.6f), 0f, ResidentPose.Stand));
            list.Add(new ResidentSpot(new Vector3(10.1f, Floor, 6.6f), 90f, ResidentPose.Stand));
            return list;
        }

        /// <summary>
        /// 진화 1단계 복지 센터 (wc_builder): 일하는 자리 = 연구원 — 스캔 침대 둘 통로 쪽 둘씩 (침대를 봄).
        /// 쉬는 자리 (쉬는 방) = 정자 원형 벤치 다섯 (방석 F + 0.45, 바깥 창을 봄) · 여가 날개 소파 넷 (창을 봄) · 끝 모서리 라운지 의자 둘 ·
        /// 홀로 탁자 둘레 서기 셋. 원형 벤치 · 곡면 의자는 찾아낸 자리로 잘 안 잡혀 고정 자리로.
        /// </summary>
        private static List<ResidentSpot> EvoWelfareSpots()
        {
            var list = new List<ResidentSpot>();
            foreach (float s in new[] { -1f, 1f })
            {
                foreach (float x in new[] { 4.5f, 5.75f })
                    list.Add(new ResidentSpot(new Vector3(x, Floor, s * 0.95f), s > 0f ? 0f : 180f, ResidentPose.Work, work: true));
            }
            foreach (float deg in new[] { 150f, 195f, 240f, 285f, 330f })
            {
                float a = deg * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                list.Add(new ResidentSpot(dir * 1.3f + Vector3.up * (Floor + 0.45f), Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, ResidentPose.Sit));
            }
            foreach (float s in new[] { -1f, 1f })
            {
                foreach (float z in new[] { 5.05f, 5.85f })
                    list.Add(new ResidentSpot(new Vector3(s * 2.05f, Floor + 0.44f, z), s * 90f, ResidentPose.Sit));
            }
            list.Add(new ResidentSpot(new Vector3(-1.9f, Floor + 0.42f, 10f), -45f, ResidentPose.Sit));
            list.Add(new ResidentSpot(new Vector3(1.9f, Floor + 0.42f, 10f), 45f, ResidentPose.Sit));
            list.Add(new ResidentSpot(new Vector3(0f, Floor, 4.35f), 0f, ResidentPose.Stand));
            list.Add(new ResidentSpot(new Vector3(0f, Floor, 6.05f), 180f, ResidentPose.Stand));
            list.Add(new ResidentSpot(new Vector3(-0.85f, Floor, 5.2f), 90f, ResidentPose.Stand));
            return list;
        }

        /// <summary>
        /// 진화 1단계 축전 뱅크 (pb_builder): 기술자 — 충 · 방전 제어대 둘 (x 4 · 12, 반지름 0.86) 둘레 셋씩 (제어대를 봄, 반지름 1.25) +
        /// 창 아래 예비 셀 선반 앞 넷 (선반을 봄, 엇갈린 벽). 둥근 제어대는 찾아낸 자리로 잘 안 잡혀 고정 자리로.
        /// </summary>
        private static List<ResidentSpot> EvoPowerBankSpots()
        {
            var list = new List<ResidentSpot>();
            foreach (var (x, side) in new[] { (4f, 1f), (12f, -1f) })
            {
                list.Add(new ResidentSpot(new Vector3(x - 1.25f, Floor, 0f), 90f, ResidentPose.Work, work: true));
                list.Add(new ResidentSpot(new Vector3(x + 1.25f, Floor, 0f), -90f, ResidentPose.Work, work: true));
                list.Add(new ResidentSpot(new Vector3(x, Floor, side * 1.25f), side > 0f ? 180f : 0f, ResidentPose.Work, work: true));
            }
            foreach (var (x, side) in new[] { (2.95f, 1f), (5.05f, -1f), (10.95f, -1f), (13.05f, 1f) })
                list.Add(new ResidentSpot(new Vector3(x, Floor, side * 2.25f), side > 0f ? 0f : 180f, ResidentPose.Work, work: true));
            return list;
        }

        /// <summary>
        /// 진화 1단계 생태 모듈 (eco_builder): 정원사 — 온실 가운데 통로에서 양쪽 재배대를 봄 (바닥 해치 x 8 · 16 비킴) 넷 + 순환 코어 물 구체 앞 하나.
        /// </summary>
        private static List<ResidentSpot> EvoEcoSpots()
        {
            var list = new List<ResidentSpot>();
            foreach (float x in new[] { 5.6f, 12f })
            {
                list.Add(new ResidentSpot(new Vector3(x, Floor, 0.55f), 0f, ResidentPose.Work, work: true));
                list.Add(new ResidentSpot(new Vector3(x, Floor, -0.55f), 180f, ResidentPose.Work, work: true));
            }
            list.Add(new ResidentSpot(new Vector3(1.3f, Floor, 6.9f), Mathf.Atan2(-1.3f, 1.9f) * Mathf.Rad2Deg, ResidentPose.Work, work: true));
            return list;
        }

        /// <summary>
        /// 진화 1단계 거주 타워 (ht_builder): 사는 사람 쉬는 자리 — 1층 둥근 식탁 둘 의자 8 (식탁을 봄) · 3층 돔 라운지 곡면 소파 4 (아트리움을 봄).
        /// 곡면 소파 · 둥근 식탁은 찾아낸 자리로 잘 안 잡혀 고정 자리로.
        /// </summary>
        private static List<ResidentSpot> EvoTowerSpots()
        {
            var list = new List<ResidentSpot>();
            var center = new Vector3(4f, 0f, 4f);
            foreach (float tx in new[] { -0.4f, 8.4f })
            {
                for (int k = 0; k < 4; k++)
                {
                    float a = (45f + 90f * k) * Mathf.Deg2Rad;
                    var p = new Vector3(tx + Mathf.Cos(a) * 0.95f, Floor + 0.49f, 4f + Mathf.Sin(a) * 0.95f);
                    list.Add(new ResidentSpot(p, Mathf.Atan2(tx - p.x, 4f - p.z) * Mathf.Rad2Deg, ResidentPose.Sit));
                }
            }
            foreach (float deg in new[] { 322.5f, 45f, 135f, 222.5f })
            {
                float a = deg * Mathf.Deg2Rad;
                var p = new Vector3(center.x + Mathf.Cos(a) * 4.0f, 8f + Floor + 0.42f, center.z + Mathf.Sin(a) * 4.0f);
                list.Add(new ResidentSpot(p, Mathf.Atan2(center.x - p.x, center.z - p.z) * Mathf.Rad2Deg, ResidentPose.Sit));
            }
            return list;
        }

        /// <summary>
        /// 진화 1단계 연구 단지 (rc_builder): 연구원 — 네 모서리 곡면 책상 의자 (모서리를 봄, 앉음) · 관측 단 콘솔 앞 (단 위 F + 0.1, 기둥을 봄) ·
        /// 창 아래 계산 설비 장 앞 둘 (장을 봄). 찾아낸 자리는 곡면 책상 · 둥근 단을 잘 못 잡아 고정 자리로.
        /// </summary>
        private static List<ResidentSpot> EvoResearchSpots()
        {
            return new List<ResidentSpot>
            {
                new ResidentSpot(new Vector3(10.04f, Floor + 0.49f, 2.04f), 45f, ResidentPose.Sit, work: true),
                new ResidentSpot(new Vector3(-2.04f, Floor + 0.49f, 2.04f), -45f, ResidentPose.Sit, work: true),
                new ResidentSpot(new Vector3(-2.04f, Floor + 0.49f, -2.04f), -135f, ResidentPose.Sit, work: true),
                new ResidentSpot(new Vector3(10.04f, Floor + 0.49f, -2.04f), 135f, ResidentPose.Sit, work: true),
                new ResidentSpot(new Vector3(2.5f, Floor + 0.1f, 0f), 90f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(3.4f, Floor, 2.2f), 0f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(4.6f, Floor, -2.2f), 180f, ResidentPose.Work, work: true),
            };
        }

        /// <summary>
        /// 12-5 방열판 (radiator_builder ANCHORS["work"]): 기술자가 서서 일함 — 열교환기 조작반 (남쪽을 봄) · 열 감시판 (동쪽 벽을 봄) ·
        /// 동북 펌프 (펌프를 봄). 찾아낸 자리는 관 다발 사이 · 펌프 뒤로 잡혀 고정 자리로.
        /// </summary>
        private static List<ResidentSpot> RadiatorSpots()
        {
            return new List<ResidentSpot>
            {
                new ResidentSpot(new Vector3(0.6f, Floor, 1.28f), 180f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(2.35f, Floor, -2.45f), 90f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(1.55f, Floor, 1.6f), 47f, ResidentPose.Work, work: true),
            };
        }

        /// <summary>
        /// 12-5 통신 중계소 (comm_builder ANCHORS): 연구원 — 북동 해독 책상 (구석 화면 2를 봄, 앉음) · 북서 기록 책상 (기록계를 봄, 앉음) ·
        /// 남동 송수신기 함 앞 (서서 일함).
        /// </summary>
        private static List<ResidentSpot> CommRelaySpots()
        {
            return new List<ResidentSpot>
            {
                new ResidentSpot(new Vector3(2.25f, Floor + 0.49f, 2.25f), 45f, ResidentPose.Sit, work: true),
                new ResidentSpot(new Vector3(-2.15f, Floor + 0.49f, 2.25f), -90f, ResidentPose.Sit, work: true),
                new ResidentSpot(new Vector3(2.1f, Floor, -2.5f), 90f, ResidentPose.Work, work: true),
            };
        }

        /// <summary>
        /// 12-5 방사선 차폐실 (shelter_builder ANCHORS): 평소 = 일터 없는 주민이 비축 물자 관리 — 선량계 게이트 콘솔(앉음) · 비상 물자 선반 ·
        /// 공기 정화기 제어판 (서서 일함). 샤워 부스는 일하는 자리가 아님 (사용자 결정). 침상 · 벤치는 대피 때 자리라 평소엔 비움.
        /// </summary>
        private static List<ResidentSpot> ShelterSpots()
        {
            return new List<ResidentSpot>
            {
                new ResidentSpot(new Vector3(1.3f, Floor + 0.49f, -2.38f), 0f, ResidentPose.Sit, work: true),
                new ResidentSpot(new Vector3(10.5f, Floor, 2.05f), 0f, ResidentPose.Work, work: true),
                new ResidentSpot(new Vector3(10.25f, Floor, -1.95f), 180f, ResidentPose.Work, work: true),
            };
        }

        /// <summary>
        /// 12-5 여객 도킹 포트 (passenger_builder ANCHORS): 안내 직원 = 일터 없는 주민 — 입국 심사 부스 2 (등을 맞대고 양옆 줄을 봄) ·
        /// 안내 데스크 (창 쪽을 봄). 쉬는 자리 = 대합실 공항 의자 14 (창밖 +z를 봄, 앞줄부터).
        /// </summary>
        private static List<ResidentSpot> PassengerSpots()
        {
            var list = new List<ResidentSpot>
            {
                new ResidentSpot(new Vector3(-0.3f, Floor + 0.49f, 1.0f), -90f, ResidentPose.Sit, work: true),
                new ResidentSpot(new Vector3(0.3f, Floor + 0.49f, 1.0f), 90f, ResidentPose.Sit, work: true),
                new ResidentSpot(new Vector3(5.8f, Floor + 0.49f, -2.55f), 0f, ResidentPose.Sit, work: true),
            };
            foreach (float z in new[] { 1.55f, 0.35f })
            {
                foreach (float x in new[] { 5.05f, 5.65f, 6.25f, 6.85f, 9.15f, 9.75f, 10.35f })
                    list.Add(new ResidentSpot(new Vector3(x, Floor + 0.43f, z), 0f, ResidentPose.Sit));
            }
            return list;
        }

        private static List<ResidentSpot> AdminSpots()
        {
            var list = new List<ResidentSpot>();
            foreach (float z in new[] { 2.125f, 0.55f, -1.05f })   // 창구 1 · 2 · 3 (손님 쪽 −x를 봄)
                list.Add(new ResidentSpot(new Vector3(3.5f, Floor + 0.49f, z), -90f, ResidentPose.Sit, work: true));
            list.Add(new ResidentSpot(new Vector3(6.7172f, Floor + 0.49f, -1.4767f), -51.71f, ResidentPose.Sit, work: true));   // 관제 콘솔 (책상을 봄)
            list.Add(new ResidentSpot(new Vector3(7.15f, Floor + 0.49f, 0.55f), -90f, ResidentPose.Sit, work: true));
            list.Add(new ResidentSpot(new Vector3(6.4677f, Floor + 0.49f, 2.376f), -128.23f, ResidentPose.Sit, work: true));
            list.Add(new ResidentSpot(new Vector3(9.9f, Floor + 0.49f, 0.55f), -90f, ResidentPose.Sit, work: true));
            // 대기 의자 (+x 창구 쪽을 봄): 앞줄 통로 쪽부터
            foreach (var (x, z) in new[] { (0.5f, 0.75f), (0.5f, -0.75f), (0.5f, 1.15f), (0.5f, -1.15f), (-0.7f, 0.75f), (-0.7f, -0.75f), (0.5f, 1.55f), (0.5f, -1.55f) })
                list.Add(new ResidentSpot(new Vector3(x, Floor + 0.43f, z), 90f, ResidentPose.Sit));
            return list;
        }

        private struct Candidate
        {
            public Vector3 Local;
            public float Yaw;
            public ResidentPose Pose;
        }

        [MenuItem("SpaceStation/Interior/Bake Resident Spots")]
        public static void BakeAll()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[ResidentSpotBaker] 플레이 모드에서는 실행하지 않음");
                return;
            }
            var log = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate", new[] { DataFolder }))
            {
                var template = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (template == null || template.Prefab == null || template.Module == null)
                    continue;
                log.Add(Bake(template));
                EditorUtility.SetDirty(template);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ResidentSpotBaker] " + string.Join(" · ", log));
        }

        /// <summary>
        /// 이름이 prefix로 시작하는 템플릿만 굽기 (진화 모듈 내부 = "IT_Evo" — Build Evolved Templates가 부름).
        /// 전체 굽기는 찾아낸 자리가 실행마다 조금씩 달라지는 방(휴게실 등)까지 다시 써서 관계없는 변경이 생김.
        /// </summary>
        public static void BakeTemplates(string prefix)
        {
            var log = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate", new[] { DataFolder }))
            {
                var template = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (template == null || template.Prefab == null || template.Module == null || !template.name.StartsWith(prefix))
                    continue;
                log.Add(Bake(template));
                EditorUtility.SetDirty(template);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ResidentSpotBaker] " + string.Join(" · ", log));
        }

        private static string Bake(InteriorTemplate template)
        {
            string name = template.name.Replace("IT_", "");
            var module = template.Module;
            VisitStatus.TryGetValue(name, out var visitStatus);
            WorkStatus.TryGetValue(name, out var workStatus);
            template.EditorSetVisit(OpenWorkRooms.Contains(name), visitStatus, workStatus);
            if (FixedSpots.TryGetValue(name, out var fixedSpots))
            {
                var spots = fixedSpots();
                // 12-5 방열판: 고정 자리여도 일터 특성(기술자)은 그대로 — 일터 없는 주민 방(OpenWork)은 특성 없음
                var fixedTraits = WorkTraits.TryGetValue(name, out var ft) ? new List<ResidentTrait>(ft) : new List<ResidentTrait>();
                template.EditorSetResidents(spots, fixedTraits, LeisureRooms.Contains(name));
                return $"{name} 고정 자리 {spots.Count}";
            }
            var instance = (GameObject)Object.Instantiate(template.Prefab, Far, Quaternion.identity);
            var added = new List<Component>();
            try
            {
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null)
                        continue;
                    added.Add(filter.gameObject.AddComponent<MeshCollider>());
                    ((MeshCollider)added[added.Count - 1]).sharedMesh = filter.sharedMesh;
                }
                Physics.SyncTransforms();

                var avoid = AvoidPoints(template);
                var sits = new List<Candidate>();
                var works = new List<Candidate>();
                var stands = new List<Candidate>();
                foreach (var cell in module.CellOffsets)
                    Scan(cell, avoid, sits, works, stands);

                WorkTraits.TryGetValue(name, out var traits);
                bool leisure = LeisureRooms.Contains(name);
                int cells = module.CellOffsets.Count;
                int housing = module.HousingCapacity;
                int restCount = housing > 0 || leisure ? Mathf.Min(8, Mathf.Max(housing, 3 * cells)) : 0;
                int workCount = traits != null ? Mathf.Min(4, 2 * cells) : 0;

                var chosen = new List<Vector3>();
                var spots = new List<ResidentSpot>();
                int workFound = 0;
                foreach (var c in Pick(works, workCount, chosen))
                {
                    spots.Add(new ResidentSpot(c.Local, c.Yaw, ResidentPose.Work, work: true));
                    workFound++;
                }
                // 작업대를 못 찾은 만큼은 서서 일하는 자리 (방 가운데를 봄) — 손상 통제실처럼 낮은 상황판만 있는 방
                foreach (var c in Pick(stands, workCount - workFound, chosen))
                    spots.Add(new ResidentSpot(c.Local, c.Yaw, ResidentPose.Stand, work: true));
                int sitCount = 0;
                foreach (var c in Pick(sits, restCount, chosen))
                {
                    spots.Add(new ResidentSpot(c.Local, c.Yaw, ResidentPose.Sit));
                    sitCount++;
                }
                foreach (var c in Pick(stands, restCount - sitCount, chosen))
                    spots.Add(new ResidentSpot(c.Local, c.Yaw, ResidentPose.Stand));

                template.EditorSetResidents(spots, traits != null ? new List<ResidentTrait>(traits) : new List<ResidentTrait>(), leisure);
                int w = 0, s = 0, st = 0;
                foreach (var sp in spots)
                {
                    if (sp.Work) w++;
                    else if (sp.Pose == ResidentPose.Sit) s++;
                    else st++;
                }
                return $"{name} 작업 {w}/{workCount} 앉기 {s} 서기 {st}/{restCount} (후보 {works.Count}/{sits.Count}/{stands.Count})";
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>문 자리 · 해치 · 들어오는 곳 (템플릿 로컬, 바닥 높이 무시).</summary>
        private static List<(Vector3 Point, float Radius)> AvoidPoints(InteriorTemplate template)
        {
            // 처음 값(문 반경 2.0, 들어오는 곳 1.6)은 6.4m 방 바닥을 거의 다 가렸음 → 문 앞 통로 폭만큼
            var list = new List<(Vector3, float)> { (template.Spawn, 1.0f) };
            foreach (var s in template.Sockets)
            {
                var c = (Vector3)s.Cell * Interior.InteriorGeometry.CellSize;
                if (s.Direction.y == 0)
                {
                    var dir = (Vector3)s.Direction;
                    list.Add((c + dir * (s.Depth - 0.4f), 1.1f));
                    list.Add((c + dir * (s.Depth - 1.4f), 0.9f));
                }
                else
                    list.Add((c + s.Offset, 1.0f));
            }
            return list;
        }

        private static bool NearAvoid(Vector3 local, List<(Vector3 Point, float Radius)> avoid)
        {
            foreach (var (p, r) in avoid)
            {
                var d = local - p;
                d.y = 0f;
                if (d.sqrMagnitude < r * r)
                    return true;
            }
            return false;
        }

        private static void Scan(Vector3Int cell, List<(Vector3, float)> avoid, List<Candidate> sits, List<Candidate> works, List<Candidate> stands)
        {
            float size = Interior.InteriorGeometry.CellSize;
            var center = (Vector3)cell * size;
            float floor = center.y + Floor;
            for (float x = -3.4f; x <= 3.401f; x += Step)
            {
                for (float z = -3.4f; z <= 3.401f; z += Step)
                {
                    var local = center + new Vector3(x, 0f, z);
                    var top = Far + new Vector3(local.x, floor + 2.3f, local.z);
                    if (!Physics.Raycast(top, Vector3.down, out var hit, 3.2f, ~0, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.9f)
                        continue;
                    float h = hit.point.y - (Far.y + floor);
                    var groundLocal = hit.point - Far;
                    if (NearAvoid(groundLocal, avoid))
                        continue;
                    var yawToCenter = YawTo(groundLocal, center);

                    if (Mathf.Abs(h) < 0.25f)
                    {
                        // 빈 바닥: 머리까지 비었는지
                        if (Physics.CheckCapsule(hit.point + Vector3.up * 0.35f, hit.point + Vector3.up * 1.75f, 0.28f, ~0, QueryTriggerInteraction.Ignore))
                            continue;
                        stands.Add(new Candidate { Local = groundLocal, Yaw = OpenYaw(hit.point, yawToCenter), Pose = ResidentPose.Stand });
                        if (TryWork(hit.point, out float workYaw))
                            works.Add(new Candidate { Local = groundLocal, Yaw = workYaw, Pose = ResidentPose.Work });
                    }
                    else if (h > 0.35f && h < 0.65f)
                    {
                        if (TrySit(hit.point, Far.y + floor, out float sitYaw))
                            sits.Add(new Candidate { Local = groundLocal, Yaw = sitYaw, Pose = ResidentPose.Sit });
                    }
                }
            }
        }

        /// <summary>앉을 자리: 위가 비었고, 다리를 내릴 방향이 있는지. 등받이가 있는 반대쪽을 바라봄.</summary>
        private static bool TrySit(Vector3 seat, float floorY, out float yaw)
        {
            yaw = 0f;
            if (Physics.CheckCapsule(seat + Vector3.up * 0.3f, seat + Vector3.up * 1.1f, 0.2f, ~0, QueryTriggerInteraction.Ignore))
                return false;
            float best = -1f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                // 무릎 · 발 자리 (앉은 자세: 무릎 0.44 앞, 발 0.48 앞, 바닥까지)
                var knee = seat + dir * 0.45f + Vector3.up * 0.05f;
                if (Physics.CheckSphere(knee, 0.1f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                var foot = new Vector3(knee.x, floorY + 0.3f, knee.z);
                if (Physics.CheckSphere(foot, 0.12f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                if (!Physics.Raycast(foot, Vector3.down, out var fh, 0.6f) || Mathf.Abs(fh.point.y - floorY) > 0.25f)
                    continue;
                // 등받이 (뒤쪽 0.7 안에 무언가) 점수
                float score = Physics.Raycast(seat + Vector3.up * 0.5f, -dir, 0.7f, ~0, QueryTriggerInteraction.Ignore) ? 1f : 0.5f;
                if (score > best)
                {
                    best = score;
                    yaw = a;
                }
            }
            return best > 0f;
        }

        /// <summary>작업 자리: 손 높이 앞 0.3~0.75m에 화면 · 장비가 있는 방향.</summary>
        private static bool TryWork(Vector3 ground, out float yaw)
        {
            yaw = 0f;
            float bestDist = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                foreach (float height in WorkHeights)
                {
                    if (!Physics.Raycast(ground + Vector3.up * height, dir, out var hit, 0.75f, ~0, QueryTriggerInteraction.Ignore) || hit.distance < 0.3f)
                        continue;
                    if (!IsWorkSurface(MaterialName(hit)))
                        continue;
                    if (hit.distance < bestDist)
                    {
                        bestDist = hit.distance;
                        yaw = a;
                    }
                }
            }
            return bestDist < float.MaxValue;
        }

        /// <summary>손 높이 (작업대 · 화면 · 높은 콘솔).</summary>
        private static readonly float[] WorkHeights = { 0.85f, 1.1f, 1.35f }; // 0.85: 상황판 테이블처럼 낮은 작업대 (0.95만 쓰면 손상 통제실이 0개)

        /// <summary>일하는 대상 재질: 화면 · 장비 · 의료 장비.</summary>
        private static bool IsWorkSurface(string material)
        {
            return material != null && (material.Contains("Screen") || material.Contains("Device") || material.Contains("Clinic"));
        }

        /// <summary>맞은 삼각형의 재질 이름 (MeshCollider만).</summary>
        private static string MaterialName(RaycastHit hit)
        {
            if (!(hit.collider is MeshCollider mc) || mc.sharedMesh == null || hit.triangleIndex < 0)
                return null;
            var mesh = mc.sharedMesh;
            var renderer = mc.GetComponent<Renderer>();
            if (renderer == null)
                return null;
            int index = hit.triangleIndex * 3;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var sub = mesh.GetSubMesh(s);
                if (index >= sub.indexStart && index < sub.indexStart + sub.indexCount)
                {
                    var mats = renderer.sharedMaterials;
                    return s < mats.Length && mats[s] != null ? mats[s].name : null;
                }
            }
            return null;
        }

        /// <summary>
        /// 서기 자리가 바라볼 방향: 눈높이에서 가장 멀리 트인 방향 (같으면 방 가운데 쪽).
        /// 방 가운데를 바라보게 했더니 코어에서는 가운데 유리 탑 벽을 코앞에서 보고 서 있었음.
        /// </summary>
        private static float OpenYaw(Vector3 ground, float towardCenter)
        {
            float bestYaw = towardCenter, best = -1f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                float d = Physics.Raycast(ground + Vector3.up * 1.5f, dir, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 6f;
                d += Mathf.Cos(Mathf.DeltaAngle(a, towardCenter) * Mathf.Deg2Rad) * 0.5f; // 가운데 쪽을 조금 더
                if (d > best)
                {
                    best = d;
                    bestYaw = a;
                }
            }
            return bestYaw;
        }

        private static float YawTo(Vector3 from, Vector3 to)
        {
            var d = to - from;
            d.y = 0f;
            return d.sqrMagnitude < 0.01f ? 0f : Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        /// <summary>이미 고른 자리에서 가장 먼 후보부터 (간격 <see cref="MinSpacing"/> 이상).</summary>
        private static List<Candidate> Pick(List<Candidate> pool, int count, List<Vector3> chosen)
        {
            var result = new List<Candidate>();
            var left = new List<Candidate>(pool);
            while (result.Count < count && left.Count > 0)
            {
                int bestIndex = -1;
                float bestDist = float.MinValue;
                for (int i = 0; i < left.Count; i++)
                {
                    // 처음 하나는 칸 원점에 가장 가까운 후보, 그 뒤로는 고른 자리에서 가장 먼 후보
                    float d = chosen.Count == 0 ? -left[i].Local.sqrMagnitude : float.MaxValue;
                    foreach (var c in chosen)
                    {
                        var v = left[i].Local - c;
                        v.y = 0f;
                        d = Mathf.Min(d, v.sqrMagnitude);
                    }
                    if (chosen.Count > 0 && d < MinSpacing * MinSpacing)
                        continue;
                    if (d > bestDist)
                    {
                        bestDist = d;
                        bestIndex = i;
                    }
                }
                if (bestIndex < 0)
                    break;
                result.Add(left[bestIndex]);
                chosen.Add(left[bestIndex].Local);
                left.RemoveAt(bestIndex);
            }
            return result;
        }
    }
}
