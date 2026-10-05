using System.Collections.Generic;
using SpaceStation.Data;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// Phase 11-4~ 모듈별 내부 템플릿 만들기 (메뉴 SpaceStation/Interior/Build Templates):
    /// Blender `BlenderWork/Interior_Kit.blend`(텍스트 블록 `core_builder` 등) → `Art/Models/Interior/SM_Interior_X.fbx`의
    /// INT_X_* 오브젝트를 프리팹(`Prefabs/Interior/PF_Interior_X`)으로 조립하고 템플릿 에셋(`Data/Interior/IT_X`)에 문 자리·스폰을 적는다.
    /// 재질 슬롯 이름 → Hull·HullDark = 외부 공용, Accent = 그 모듈 강조색, Light = 천장 조명, Glass = 창 유리.
    /// "_Shell"·"_Glass" 오브젝트는 MeshCollider(걷는 면·창 막기), 나머지는 보기만.
    /// 다시 실행하면 덮어쓴다. 끝나면 Interior/Setup으로 InteriorMode에 연결.
    /// </summary>
    public static class InteriorTemplateBuilder
    {
        private const string ModelFolder = "Assets/_Project/Art/Models/Interior/";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Interior/";
        private const string DataFolder = "Assets/_Project/Data/Interior/";
        private const string ModuleFolder = "Assets/_Project/Data/Modules/";
        private const string StationMaterials = "Assets/_Project/Art/Materials/Station/";
        private const string InteriorMaterials = "Assets/_Project/Art/Materials/Interior/";
        private const string AccentFolder = "Assets/_Project/Art/Materials/";

        private const float F = -1.3f; // InteriorGeometry.FloorOffset
        private const float Socket = 3.2f;

        [MenuItem("SpaceStation/Interior/Build Templates")]
        public static void BuildAll()
        {
            // 플레이 중에 돌리면 SaveAssets가 런타임에 바뀐 품질 설정(SettingsApplier의 렌더 파이프라인·VSync)까지 저장해
            // ProjectSettings/QualitySettings.asset이 망가졌음 (2026-10-05)
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[InteriorTemplateBuilder] 플레이 모드를 끈 뒤 실행하세요.");
                return;
            }
            InteriorSetup.CreateMaterials(); // 유리 재질 등이 먼저 있어야 함
            BuildCore();
            BuildHabitat();
            BuildFarm();
            BuildOxygen();
            BuildWaterRecycler();
            BuildMedical();
            BuildRecreation();
            BuildRotatingRing();
            BuildStorage();
            BuildMiningDock();
            BuildMaintenanceBay();
            BuildRefinery();
            BuildResearchLab();
            BuildCargoTerminal();
            BuildFusionReactor();
            BuildSolar();
            BuildTurret();
            BuildShield();
            BuildBattery();
            BuildArmorBulkhead();
            BuildDamageControl();
            AssetDatabase.SaveAssets();
            InteriorSetup.Setup();
        }

        /// <summary>11-4 코어: 12각 고리 복도(반지름 4.4~7.0) + 바깥 연결 통로 8곳 + 안쪽 창 너머 허브·통신탑. 고리 중심 = 로컬 (4, 4).</summary>
        private static void BuildCore()
        {
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                for (int z = 0; z < 2; z++)
                {
                    var cell = new Vector3Int(x, 0, z);
                    sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                    sockets.Add(new InteriorSocket(cell, z == 0 ? new Vector3Int(0, 0, -1) : new Vector3Int(0, 0, 1), Socket));
                    // 매달린 모듈: 로비 바닥 해치. 칸 중심은 고리 안쪽 창틀에 붙으므로 바깥 모서리 쪽(로비 가운데 근처)으로 1.2씩
                    var outward = new Vector3(x == 0 ? -1.2f : 1.2f, 0f, z == 0 ? -1.2f : 1.2f);
                    sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, outward));
                }
            }
            // 다듬기 B: 복도는 따뜻한 빛, 창 너머 탑은 차가운 빛으로 대비
            var lights = new List<LightSpec>();
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f + Mathf.PI / 6f;
                lights.Add(LightSpec.Point(new Vector3(4f + Mathf.Cos(a) * 5.7f, F + 3.1f, 4f + Mathf.Sin(a) * 5.7f), 6.5f, 3.2f, new Color(1f, 0.88f, 0.74f)));
            }
            // 로비마다 따뜻한 빛 하나
            foreach (var (lx, lz) in new[] { (-0.8f, -0.8f), (8.8f, -0.8f), (-0.8f, 8.8f), (8.8f, 8.8f) })
                lights.Add(LightSpec.Point(new Vector3(lx, F + 3.0f, lz), 5.5f, 2.6f, new Color(1f, 0.88f, 0.74f)));
            // 허브 위에서 탑을 올려 비추는 차가운 스포트 3개 + 창가 쪽 차가운 보조광
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 2f / 3f;
                var p = new Vector3(4f + Mathf.Cos(a) * 2.6f, 0.9f, 4f + Mathf.Sin(a) * 2.6f);
                var aim = (new Vector3(4f, 9f, 4f) - p).normalized;
                lights.Add(LightSpec.Spot(p, aim, 14f, 9f, 38f, new Color(0.62f, 0.8f, 1f)));
            }
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI / 2f + Mathf.PI / 4f;
                lights.Add(LightSpec.Point(new Vector3(4f + Mathf.Cos(a) * 4.0f, F + 1.2f, 4f + Mathf.Sin(a) * 4.0f), 4f, 1.2f, new Color(0.6f, 0.78f, 1f)));
            }
            Build("Core", "MD_Core", sockets, new Vector3(4f, F, 4f - 5.7f), 0f, lights);
        }

        /// <summary>
        /// 11-5 거주 (2칸, 로컬 칸 (0,0,0)·(1,0,0)): x 방향 8각 복도(안쪽 아포템 2.3, x −2.4~10.4) + 칸마다 양옆·끝 문 자리 대기 공간,
        /// +z 쪽 2층 침실 캡슐 4칸, -z 쪽 창 2개·벤치. 위·아래 해치는 칸 중심 (천장 조명은 그 자리를 비움).
        /// </summary>
        private static void BuildHabitat()
        {
            const float ceiling = F + 3.6f;
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F));
                sockets.Add(new InteriorSocket(cell, Vector3Int.up, ceiling));
            }
            var warm = new Color(1f, 0.88f, 0.74f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(0f, F + 3.0f, 0f), 6f, 2.6f, warm),
                LightSpec.Point(new Vector3(4f, F + 3.0f, 0f), 6f, 2.8f, warm),
                LightSpec.Point(new Vector3(8f, F + 3.0f, 0f), 6f, 2.6f, warm),
                // 창가 차가운 보조광
                LightSpec.Point(new Vector3(4f, F + 1.6f, -1.6f), 4f, 1.0f, new Color(0.6f, 0.78f, 1f)),
            };
            // 침실 캡슐마다 은은한 읽기등
            foreach (float bx in new[] { 3.025f, 4.975f })
            {
                foreach (float by in new[] { F + 1.0f, F + 2.2f })
                    lights.Add(LightSpec.Point(new Vector3(bx, by, 3.1f), 2f, 0.55f, new Color(1f, 0.8f, 0.6f)));
            }
            Build("Habitat", "MD_Habitat", sockets, new Vector3(-1.2f, F, 0f), 90f, lights);
        }

        /// <summary>
        /// 11-5 수경 농장 (2칸, 로컬 칸 (0,0,0)·(1,0,0)): 재배 홀(x −3.2~11.2, z ±3.2) + 칸 중심 위 유리 돔(천장 구멍 반지름 2.6) + 돔 아래 화단·나무,
        /// 돔 사이 3단 재배 선반 2줄, 모서리 양액 탱크. 천장은 돔이 차지해 위 해치 없음, 아래 해치는 화단을 피해 z −2.0.
        /// </summary>
        private static void BuildFarm()
        {
            const float ceiling = F + 3.6f;
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, new Vector3(0f, 0f, -2.0f)));
            }
            var hall = new Color(1f, 0.95f, 0.86f);
            var daylight = new Color(0.9f, 1f, 0.92f);
            var growGlow = new Color(1f, 0.42f, 0.86f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-2.2f, F + 3.0f, 0f), 6.5f, 2.4f, hall),
                LightSpec.Point(new Vector3(4f, F + 3.0f, 0f), 6.5f, 2.4f, hall),
                LightSpec.Point(new Vector3(10.2f, F + 3.0f, 0f), 6.5f, 2.4f, hall),
                // 돔 꼭대기에서 화단으로 내리쬐는 빛
                LightSpec.Spot(new Vector3(0f, ceiling + 2.6f, 0f), Vector3.down, 8f, 7f, 80f, daylight),
                LightSpec.Spot(new Vector3(8f, ceiling + 2.6f, 0f), Vector3.down, 8f, 7f, 80f, daylight),
                // 돔 안쪽 살대를 비추는 빛 (없으면 돔이 검은 원판처럼 보임)
                LightSpec.Point(new Vector3(0f, ceiling + 0.6f, 0f), 4.5f, 1.6f, daylight),
                LightSpec.Point(new Vector3(8f, ceiling + 0.6f, 0f), 4.5f, 1.6f, daylight),
                // 선반 생장등 분홍 번짐
                LightSpec.Point(new Vector3(4f, F + 1.1f, 1.7f), 3f, 1.2f, growGlow),
                LightSpec.Point(new Vector3(4f, F + 1.1f, -1.7f), 3f, 1.2f, growGlow),
            };
            Build("Farm", "MD_Farm", sockets, new Vector3(-2.0f, F, 0f), 90f, lights);
        }

        /// <summary>
        /// 11-5 산소 생성기 (1칸): 모서리 깎은 6.4m 기계실 + 가운데 단 위 전기분해 탱크 2개(x ±1.2, 천장 가운데를 터서 위로 3.6까지),
        /// 청록 액체 관찰창·배관·콘솔·사물함. 해치는 탱크를 피해 위 = z +2.0, 아래 = z −2.0.
        /// </summary>
        private static void BuildOxygen()
        {
            const float ceiling = F + 3.6f;
            var cell = Vector3Int.zero;
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(cell, Vector3Int.left, Socket),
                new InteriorSocket(cell, Vector3Int.right, Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(cell, Vector3Int.up, ceiling, new Vector3(0f, 0f, 2.0f)),
                new InteriorSocket(cell, Vector3Int.down, -F, new Vector3(0f, 0f, -2.0f)),
            };
            var room = new Color(0.92f, 0.96f, 1f);
            var cyan = new Color(0.35f, 0.9f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-2.2f, F + 3.0f, 2.2f), 4.5f, 1.6f, room),
                LightSpec.Point(new Vector3(2.2f, F + 3.0f, 2.2f), 4.5f, 1.6f, room),
                LightSpec.Point(new Vector3(-2.2f, F + 3.0f, -2.2f), 4.5f, 1.6f, room),
                LightSpec.Point(new Vector3(2.2f, F + 3.0f, -2.2f), 4.5f, 1.6f, room),
                // 탱크 관찰창의 청록 번짐: 탱크 중심에 두어 사방으로 고르게 (바깥 한쪽에 두면 그쪽만 과하게 밝았음)
                LightSpec.Point(new Vector3(-1.2f, F + 1.65f, 0f), 4f, 1.6f, cyan),
                LightSpec.Point(new Vector3(1.2f, F + 1.65f, 0f), 4f, 1.6f, cyan),
                LightSpec.Point(new Vector3(0f, 3.1f, 0f), 4f, 1.6f, cyan),
            };
            Build("Oxygen", "MD_Oxygen", sockets, new Vector3(0f, F, -2.6f), 0f, lights);
        }

        /// <summary>
        /// 11-5 물 재활용기 (1칸): 6.4m 방 + 모서리 기둥 + 천장 가운데 창(2m) 아래 원통 유리 물탱크(반지름 0.85),
        /// 분배기에서 네 모서리로 가는 배관 → 여과 카트리지 8. 해치는 탱크·천장 창을 피해 위 = z +2.0, 아래 = z −2.0.
        /// </summary>
        private static void BuildWaterRecycler()
        {
            const float ceiling = F + 3.6f;
            var cell = Vector3Int.zero;
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(cell, Vector3Int.left, Socket),
                new InteriorSocket(cell, Vector3Int.right, Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(cell, Vector3Int.up, ceiling, new Vector3(0f, 0f, 2.0f)),
                new InteriorSocket(cell, Vector3Int.down, -F, new Vector3(0f, 0f, -2.0f)),
            };
            var room = new Color(0.95f, 0.96f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-2.0f, F + 3.0f, 2.0f), 4.5f, 1.5f, room),
                LightSpec.Point(new Vector3(2.0f, F + 3.0f, 2.0f), 4.5f, 1.5f, room),
                LightSpec.Point(new Vector3(-2.0f, F + 3.0f, -2.0f), 4.5f, 1.5f, room),
                LightSpec.Point(new Vector3(2.0f, F + 3.0f, -2.0f), 4.5f, 1.5f, room),
                // 천장 창에서 탱크로 내려오는 빛 + 탱크 속 물빛 (탱크 중심 — MODELING.md 6-1)
                LightSpec.Spot(new Vector3(0f, ceiling + 0.1f, 0f), Vector3.down, 6f, 5f, 70f, new Color(0.85f, 0.92f, 1f)),
                LightSpec.Point(new Vector3(0f, F + 0.9f, 0f), 4f, 1.4f, new Color(0.4f, 0.7f, 1f)),
            };
            Build("WaterRecycler", "MD_WaterRecycler", sockets, new Vector3(0f, F, -2.6f), 0f, lights);
        }

        /// <summary>
        /// 11-5 의료실 (1칸): 6.4m 진료실, 문 4곳을 잇는 십자 통로는 비우고 네 구역에 스캐너 아치 침대 / 침대·수액 거치대·생체 모니터 /
        /// 약품 보관함 / 진료 책상·의자, 남쪽 벽 둥근 장비 포트 6. 해치는 통로 교차점(칸 중심).
        /// </summary>
        private static void BuildMedical()
        {
            const float ceiling = F + 3.6f;
            var cell = Vector3Int.zero;
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(cell, Vector3Int.left, Socket),
                new InteriorSocket(cell, Vector3Int.right, Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(cell, Vector3Int.up, ceiling),
                new InteriorSocket(cell, Vector3Int.down, -F),
            };
            var clinic = new Color(0.98f, 0.99f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-2.0f, F + 3.0f, 2.0f), 4.5f, 1.7f, clinic),
                LightSpec.Point(new Vector3(2.0f, F + 3.0f, 2.0f), 4.5f, 1.7f, clinic),
                LightSpec.Point(new Vector3(-2.0f, F + 3.0f, -2.0f), 4.5f, 1.7f, clinic),
                LightSpec.Point(new Vector3(2.0f, F + 3.0f, -2.0f), 4.5f, 1.7f, clinic),
                // 스캐너 아치 안쪽 빛 (아치 중심)
                LightSpec.Point(new Vector3(2.0f, F + 0.9f, 2.22f), 2.5f, 0.8f, new Color(0.45f, 0.85f, 1f)),
            };
            Build("Medical", "MD_Medical", sockets, new Vector3(0f, F, -2.6f), 0f, lights);
        }

        /// <summary>
        /// 11-5 휴게실 (2칸, 로컬 칸 (0,0,0)·(1,0,0)): 8각 라운지(안쪽 아포템 2.8 = 천장 2.8, x −2.4~10.4) + −z 파노라마 창(x 2.1~5.9, 세로벽 + 위 경사면),
        /// 창을 바라보는 소파·러그·탁자·화분, +z 간식 카운터·스툴. 문 자리·해치는 거주 모듈과 같음 (위 해치 = 천장 2.8).
        /// </summary>
        private static void BuildRecreation()
        {
            const float ceiling = 2.8f;
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F));
                sockets.Add(new InteriorSocket(cell, Vector3Int.up, ceiling));
            }
            var warm = new Color(1f, 0.85f, 0.7f);
            var mood = new Color(0.7f, 0.45f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(0f, 2.1f, 0f), 6f, 2.2f, warm),
                LightSpec.Point(new Vector3(4f, 2.1f, 0.4f), 6f, 2.4f, warm),
                LightSpec.Point(new Vector3(8f, 2.1f, 0f), 6f, 2.2f, warm),
                // 창가 보랏빛 무드 + 카운터 위 따뜻한 빛
                LightSpec.Point(new Vector3(2.8f, 0.6f, -2.1f), 3.5f, 1.2f, mood),
                LightSpec.Point(new Vector3(5.2f, 0.6f, -2.1f), 3.5f, 1.2f, mood),
                LightSpec.Point(new Vector3(4f, F + 2.2f, 2.0f), 3.5f, 1.0f, warm),
            };
            // 스폰 = 서쪽에서 소파 너머 파노라마 창을 비스듬히 바라봄
            Build("Recreation", "MD_Recreation", sockets, new Vector3(1.0f, F, 0.3f), 115f, lights);
        }

        /// <summary>
        /// 11-5 회전 링 (3×3칸, 로컬 칸 x,z ∈ {-1,0,1}): 수평 고리(바깥 모델과 같은 방향) — 원형 허브(반지름 3.2, 천장 높이 5.0, 빛나는 회전축 기둥·둥근 벤치)
        /// + 살 통로 4 + 고리 복도(반지름 5.3~8.3, 바깥벽 창 8·벤치, 안쪽벽 화분 8) + 변 가운데 통로 4 + 모서리 로비 4. 바닥은 평평(중력 그대로).
        /// 바깥 문 자리 12 (둘레 칸의 바깥 면 전부), 해치 = 9칸 모두 칸 중심 (허브만 기둥을 피해 +x 2.1, 천장 높이 다름).
        /// </summary>
        private static void BuildRotatingRing()
        {
            const float ceiling = F + 3.6f;
            const float hubCeiling = F + 5.0f;
            var hubOffset = new Vector3(2.1f, 0f, 0f);
            var sockets = new List<InteriorSocket>();
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    var cell = new Vector3Int(x, 0, z);
                    if (x == -1) sockets.Add(new InteriorSocket(cell, Vector3Int.left, Socket));
                    if (x == 1) sockets.Add(new InteriorSocket(cell, Vector3Int.right, Socket));
                    if (z == -1) sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                    if (z == 1) sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket));
                    bool hub = x == 0 && z == 0;
                    sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, hub ? hubOffset : Vector3.zero));
                    sockets.Add(new InteriorSocket(cell, Vector3Int.up, hub ? hubCeiling : ceiling, hub ? hubOffset : Vector3.zero));
                }
            }
            var warm = new Color(1f, 0.88f, 0.74f);
            var lights = new List<LightSpec>
            {
                // 허브: 회전축 기둥 빛 + 둘레
                LightSpec.Point(new Vector3(0f, F + 2.4f, 0f), 5f, 2.2f, new Color(0.85f, 0.92f, 1f)),
            };
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                lights.Add(LightSpec.Point(new Vector3(Mathf.Cos(a) * 6.8f, F + 3.0f, Mathf.Sin(a) * 6.8f), 5.5f, 2.0f, warm));
            }
            foreach (var (lx, lz) in new[] { (-8f, -8f), (8f, -8f), (-8f, 8f), (8f, 8f) })
                lights.Add(LightSpec.Point(new Vector3(lx, F + 3.0f, lz), 5f, 1.6f, warm));
            // 스폰 = 고리 남쪽에서 동쪽으로 휘어 가는 복도를 바라봄
            Build("RotatingRing", "MD_RotatingRing", sockets, new Vector3(0f, F, -6.8f), 90f, lights);
        }

        /// <summary>
        /// 11-6 창고 (1칸): 천장이 높은(F + 4.8) 6.4m 창고, 문 4곳을 잇는 십자 통로(바닥 유도선)는 비우고 네 모서리에 L자 5단 선반 랙,
        /// 구역 안쪽에 상자 더미 / 큰 컨테이너 / 화물 카트 / 드럼통·재고 단말. 해치는 통로 교차점(칸 중심), 위 해치 깊이 = 높은 천장.
        /// </summary>
        private static void BuildStorage()
        {
            const float ceiling = F + 4.8f;
            var cell = Vector3Int.zero;
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(cell, Vector3Int.left, Socket),
                new InteriorSocket(cell, Vector3Int.right, Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(cell, Vector3Int.up, ceiling),
                new InteriorSocket(cell, Vector3Int.down, -F),
            };
            var cool = new Color(0.9f, 0.95f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(1.95f, F + 4.1f, 0f), 5.5f, 2.0f, cool),
                LightSpec.Point(new Vector3(-1.95f, F + 4.1f, 0f), 5.5f, 2.0f, cool),
                LightSpec.Point(new Vector3(0f, F + 4.1f, 1.95f), 5.5f, 2.0f, cool),
                LightSpec.Point(new Vector3(0f, F + 4.1f, -1.95f), 5.5f, 2.0f, cool),
                // 랙 사이 구석이 어둡지 않게 낮은 보조광
                LightSpec.Point(new Vector3(0f, F + 1.8f, 0f), 4.5f, 0.8f, new Color(1f, 0.9f, 0.78f)),
            };
            Build("Storage", "MD_Storage", sockets, new Vector3(0f, F, -2.6f), 0f, lights);
        }

        /// <summary>
        /// 11-6 채굴 도킹 (1칸, 말단): 앞면(+z) = 채굴선 접근로라 문 자리 없음 — 앞벽에 닫힌 에어록 문(경고 줄무늬)·관제 창(아래 콘솔)·광석 투입구.
        /// 투입구 → 기운 컨베이어 → 호퍼 → 광석 통, 광석 통 2, 우주복 사물함·벤치. 문 자리 = 뒤·양옆 + 위·아래 해치(칸 중심).
        /// </summary>
        private static void BuildMiningDock()
        {
            const float ceiling = F + 3.6f;
            var cell = Vector3Int.zero;
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(cell, Vector3Int.left, Socket),
                new InteriorSocket(cell, Vector3Int.right, Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(cell, Vector3Int.up, ceiling),
                new InteriorSocket(cell, Vector3Int.down, -F),
            };
            var warm = new Color(1f, 0.88f, 0.74f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-2.0f, F + 3.0f, 2.0f), 4.5f, 1.5f, new Color(0.8f, 0.9f, 1f)),
                LightSpec.Point(new Vector3(2.0f, F + 3.0f, 2.0f), 4.5f, 1.8f, warm),
                LightSpec.Point(new Vector3(-2.0f, F + 3.0f, -2.0f), 4.5f, 1.7f, warm),
                LightSpec.Point(new Vector3(2.0f, F + 3.0f, -2.0f), 4.5f, 1.7f, warm),
                // 에어록 앞 경고 주황빛
                LightSpec.Point(new Vector3(0f, F + 3.0f, 1.6f), 3.2f, 0.6f, new Color(1f, 0.7f, 0.3f)),
            };
            // 스폰 = 뒤(정거장 쪽 문)에서 에어록·관제 창을 바라봄
            Build("MiningDock", "MD_MiningDock", sockets, new Vector3(0f, F, -2.6f), 0f, lights);
        }

        /// <summary>
        /// 11-6 정비 베이 (2칸, 로컬 칸 (0,0,0)·(1,0,0)): 천장이 높은(F + 4.8) 작업 홀 x −3.2~11.2. 천장 레일 로봇 팔 → 작업 단 위 수리 중인 드론,
        /// +z 벽 작업대·공구 벽, −z 벽 부품 선반, 모서리 드론 충전 거치대 2·공구 수레·가스 실린더. 문 자리 = 양 끝 + 칸마다 앞뒤 + 위·아래 해치(칸 중심).
        /// </summary>
        private static void BuildMaintenanceBay()
        {
            const float ceiling = F + 4.8f;
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F));
                sockets.Add(new InteriorSocket(cell, Vector3Int.up, ceiling));
            }
            var warm = new Color(1f, 0.88f, 0.74f);
            var cool = new Color(0.88f, 0.94f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-0.5f, F + 4.0f, 0f), 6f, 2.0f, warm),
                LightSpec.Point(new Vector3(8.5f, F + 4.0f, 0f), 6f, 2.0f, warm),
                // 작업 단을 내리비추는 차가운 작업등
                LightSpec.Spot(new Vector3(4f, F + 4.4f, -1.2f), new Vector3(0f, -1f, 0.35f), 7f, 3.0f, 60f, cool),
                LightSpec.Point(new Vector3(4f, F + 2.2f, 2.2f), 3.5f, 1.0f, warm),   // 작업대
                LightSpec.Point(new Vector3(4f, F + 2.2f, -2.2f), 3.5f, 0.9f, warm),  // 부품 선반
            };
            // 스폰 = 서쪽 끝에서 로봇 팔·작업 단을 바라봄
            Build("MaintenanceBay", "MD_MaintenanceBay", sockets, new Vector3(-2.0f, F, 0.6f), 90f, lights);
        }

        /// <summary>
        /// 11-6 제련소 (2칸, 로컬 칸 (0,0,0)·(1,0,0)): 천장이 높은(F + 4.8) 제련 홀. 가운데 전기로(주황 관찰창·전극 3·배기 후드와 덕트)
        /// → 출탕 홈통 → 래들, +z 벽 주괴 롤러 컨베이어, −z 벽 제어 콘솔·광석 통, 모서리 냉각수 탱크·주괴 더미·슬래그 통·집게 걸이.
        /// 문 자리 = 양 끝 + 칸마다 앞뒤 + 위·아래 해치(칸 중심).
        /// </summary>
        private static void BuildRefinery()
        {
            const float ceiling = F + 4.8f;
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F));
                sockets.Add(new InteriorSocket(cell, Vector3Int.up, ceiling));
            }
            var neutral = new Color(0.95f, 0.92f, 0.88f);
            var molten = new Color(1f, 0.5f, 0.15f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-0.5f, F + 4.0f, 0f), 6f, 1.6f, neutral),
                LightSpec.Point(new Vector3(8.5f, F + 4.0f, 0f), 6f, 1.6f, neutral),
                // 쇳물빛: 관찰창 양쪽 + 래들 위 (전기로 몸통 바깥, 중심에 두면 몸통에 가려짐)
                LightSpec.Point(new Vector3(4f, F + 1.1f, -1.6f), 3.5f, 1.4f, molten),
                LightSpec.Point(new Vector3(4f, F + 1.2f, 2.0f), 4f, 2.0f, molten),
                LightSpec.Point(new Vector3(4f, F + 2.0f, -2.4f), 3.5f, 0.8f, neutral), // 콘솔
            };
            // 스폰 = 서쪽 끝에서 전기로를 바라봄
            Build("Refinery", "MD_Refinery", sockets, new Vector3(-2.0f, F, 0.6f), 90f, lights);
        }

        /// <summary>
        /// 11-6 연구소 (1칸): 가운데 홀로그램 테이블 위 행성 홀로그램(궤도 고리 3·위성 2), (+x,+z) L자 실험대(현미경·시약병·원심분리기·분석기),
        /// (−x,+z) 유리문 샘플 냉장고·표본 탱크, (+x,−z) 대형 벽 스크린·책상, (−x,−z) 서버 랙 3. 해치는 테이블을 피해 z ±2.0 (산소 생성기와 같음).
        /// </summary>
        private static void BuildResearchLab()
        {
            const float ceiling = F + 3.6f;
            var cell = Vector3Int.zero;
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(cell, Vector3Int.left, Socket),
                new InteriorSocket(cell, Vector3Int.right, Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(cell, Vector3Int.up, ceiling, new Vector3(0f, 0f, 2.0f)),
                new InteriorSocket(cell, Vector3Int.down, -F, new Vector3(0f, 0f, -2.0f)),
            };
            var white = new Color(0.92f, 0.96f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-2.0f, F + 3.0f, 2.0f), 4.5f, 1.4f, white),
                LightSpec.Point(new Vector3(2.0f, F + 3.0f, 2.0f), 4.5f, 1.5f, white),
                LightSpec.Point(new Vector3(-2.0f, F + 3.0f, -2.0f), 4.5f, 1.3f, white),
                LightSpec.Point(new Vector3(2.0f, F + 3.0f, -2.0f), 4.5f, 1.4f, white),
                // 홀로그램 청록빛 (테이블 위) + 표본 탱크 앞 푸른빛 (표본을 비춤)
                LightSpec.Point(new Vector3(0f, F + 1.5f, 0f), 3.5f, 1.4f, new Color(0.4f, 0.9f, 1f)),
                LightSpec.Point(new Vector3(-1.6f, F + 0.9f, 2.1f), 2.2f, 0.6f, new Color(0.45f, 0.75f, 1f)),
            };
            // 홀로그램: 위성 3이 각자 궤도 고리(research_builder MOON_ORBITS와 같은 축)를 따라 돌고, 핵은 천천히 자전
            var holo = new Vector3(0f, F + 1.45f, 0f);
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_Research_Moon1", holo, new Vector3(0f, 1f, 0f), 40f),
                new OrbitSpec("INT_Research_Moon2", holo, new Vector3(0.5f, 1f, 0.2f), -28f),
                new OrbitSpec("INT_Research_Moon3", holo, new Vector3(-0.6f, 1f, 0.4f), 18f),
                new OrbitSpec("INT_Research_HoloCore", holo, new Vector3(0.15f, 1f, 0f), 12f),
            };
            // 스폰 = 남쪽 문 앞에서 홀로그램 테이블을 바라봄
            Build("ResearchLab", "MD_ResearchLab", sockets, new Vector3(0f, F, -2.6f), 0f, lights, orbits);
        }

        /// <summary>
        /// 11-6 화물 터미널 (2칸 도킹, 로컬 칸 (0,0,0)·(1,0,0)): 앞면(+z) = 화물선 접근로(연결 불가) → 앞벽에 대형 화물문(경고 줄무늬 틀·경광등)과
        /// 관제창 2·관제 콘솔. 천장이 높은(F + 4.8) 홀에 갠트리 크레인이 컨테이너를 매달고, 바닥 유도선, 뒤 벽·구석 컨테이너 더미·화물 팔레트.
        /// 문 자리 = 양 끝 + 칸마다 뒤(−z) + 위·아래 해치(칸 중심).
        /// </summary>
        private static void BuildCargoTerminal()
        {
            const float ceiling = F + 4.8f;
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F));
                sockets.Add(new InteriorSocket(cell, Vector3Int.up, ceiling));
            }
            var warm = new Color(1f, 0.88f, 0.74f);
            var cool = new Color(0.85f, 0.92f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-0.5f, F + 4.0f, 0f), 6f, 1.8f, warm),
                LightSpec.Point(new Vector3(8.5f, F + 4.0f, 0f), 6f, 1.8f, warm),
                // 화물문·하역 구역을 비추는 차가운 스포트 + 관제 콘솔 + 경광등 주황빛
                LightSpec.Spot(new Vector3(4f, F + 4.4f, -0.5f), new Vector3(0f, -0.8f, 0.6f), 8f, 2.6f, 70f, cool),
                LightSpec.Point(new Vector3(9.5f, F + 2.0f, 2.2f), 3.5f, 0.9f, cool),
                LightSpec.Point(new Vector3(4f, F + 3.6f, 2.6f), 4.5f, 0.8f, new Color(1f, 0.55f, 0.2f)),
            };
            // 스폰 = 0번 칸 뒤 문(정거장 쪽) 앞에서 화물문·크레인을 비스듬히 바라봄
            Build("CargoTerminal", "MD_CargoTerminal", sockets, new Vector3(0.4f, F, -2.4f), 40f, lights);
        }

        /// <summary>
        /// 11-6 핵융합로 (2칸, 로컬 칸 (0,0,0)·(1,0,0)): 천장이 높은(F + 4.8) 원자로 홀. 가운데(x 4) 둥근 구덩이(반지름 2.0, 깊이 0.9) 속 토카막
        /// (도넛 몸통·구리 토로이달 코일 12·중앙 솔레노이드 기둥·분홍 플라스마 관찰창 8·진단 포트 4)을 난간 관찰 통로가 한 바퀴 두름.
        /// 앞뒤 벽·난간 방사선 표지, 모서리 제어 콘솔·극저온 탱크·축전기 뱅크·방사성 폐기물 드럼. 문 자리 = 양 끝 + 칸마다 앞뒤 + 위·아래 해치(칸 중심).
        /// </summary>
        private static void BuildFusionReactor()
        {
            const float ceiling = F + 4.8f;
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F));
                sockets.Add(new InteriorSocket(cell, Vector3Int.up, ceiling));
            }
            var neutral = new Color(0.9f, 0.93f, 1f);
            var plasma = new Color(0.85f, 0.45f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-0.5f, F + 4.0f, 0f), 6f, 1.7f, neutral),
                LightSpec.Point(new Vector3(8.5f, F + 4.0f, 0f), 6f, 1.7f, neutral),
                // 관찰 통로 앞뒤 (구덩이 양옆)
                LightSpec.Point(new Vector3(4f, F + 3.4f, 2.6f), 4.5f, 1.2f, neutral),
                LightSpec.Point(new Vector3(4f, F + 3.4f, -2.6f), 4.5f, 1.2f, neutral),
                LightSpec.Point(new Vector3(-2.5f, F + 2.0f, 2.2f), 3f, 0.7f, new Color(0.6f, 0.8f, 1f)), // 콘솔
            };
            // 플라스마 관찰창 빛: 구덩이 속 몸통 바깥 4곳 (관찰창 근처, 몸통에 가리지 않게)
            for (int k = 0; k < 4; k++)
            {
                float a = (15f + 90f * k) * Mathf.Deg2Rad;
                lights.Add(LightSpec.Point(new Vector3(4f + Mathf.Cos(a) * 1.9f, F + 0.2f, Mathf.Sin(a) * 1.9f), 3f, 1.2f, plasma));
            }
            // 스폰 = 서쪽 끝에서 토카막을 바라봄
            Build("FusionReactor", "MD_FusionReactor", sockets, new Vector3(-2.0f, F, 0.6f), 90f, lights);
        }

        /// <summary>
        /// 11-7 태양광 정비 통로 (1칸): 네 모서리를 장비 덩어리(인버터·배전반·냉각 펌프·점검 화면)가 채운 폭 3.2 십자 통로,
        /// 가운데 유리 천장(3.4m) 너머 지붕 위 패널 3×3이 바깥 패널처럼 태양 쪽으로 기움(<see cref="InteriorSunTilt"/>).
        /// 문 자리 = 네 옆면 (바깥 통로는 패널 회전축과 나란한 두 옆면에만 생겨 그 둘만 열림) + 아래 해치(칸 중심). 위 면은 연결 불가.
        /// </summary>
        private static void BuildSolar()
        {
            var cell = Vector3Int.zero;
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(cell, Vector3Int.left, Socket),
                new InteriorSocket(cell, Vector3Int.right, Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(cell, Vector3Int.down, -F),
            };
            var white = new Color(0.92f, 0.95f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(2.25f, F + 3.0f, 0f), 4f, 1.3f, white),
                LightSpec.Point(new Vector3(-2.25f, F + 3.0f, 0f), 4f, 1.3f, white),
                LightSpec.Point(new Vector3(0f, F + 3.0f, 2.25f), 4f, 1.3f, white),
                LightSpec.Point(new Vector3(0f, F + 3.0f, -2.25f), 4f, 1.3f, white),
                // 유리 천장으로 들어오는 햇빛 (통로 교차점) + 패널 뒷면을 비추는 빛 (없으면 검은 판으로 보임)
                LightSpec.Spot(new Vector3(0f, F + 4.4f, 0f), Vector3.down, 6f, 2.2f, 75f, new Color(1f, 0.97f, 0.88f)),
                LightSpec.Point(new Vector3(0f, F + 4.15f, 0f), 4.5f, 1.4f, new Color(0.85f, 0.9f, 1f)),
            };
            // 패널 3×3: solar_builder와 같은 순서 (x −2.2 → 2.2, 그 안에서 z −2.2 → 2.2), 피벗 = 패널 아랫면 중심
            var tilts = new Dictionary<string, Vector3>();
            int i = 0;
            for (int a = -1; a <= 1; a++)
            {
                for (int b = -1; b <= 1; b++)
                    tilts["INT_Solar_Panel" + (++i)] = new Vector3(a * 2.2f, 3.3f, b * 2.2f);
            }
            // 스폰 = 남쪽 통로에서 유리 천장 쪽을 바라봄
            Build("Solar", "MD_Solar", sockets, new Vector3(0f, F, -2.4f), 0f, lights, null, tilts);
        }

        /// <summary>
        /// 11-7 포탑 정비 통로 (1칸): 가운데 회전 받침 — 고정 받침대 위 톱니 48 회전판(포신 기둥·탄약 공급 슈트·평형추, 천장 막힌 홈 속으로)과
        /// 맞물린 모터 피니언 8톱니가 돈다(<see cref="InteriorOrbit"/>, 6배 빠르게 반대로). 둘레 경고 난간(반지름 1.8) 바깥 = 정비 통로.
        /// 모서리: 탄약 랙 · 사격 통제 콘솔·레이더 화면 · 예비 포신·공구함 · 서보 제어함. 문 자리 = 네 옆면, 위·아래 해치는 기둥을 피해 (−2.25, −2.25).
        /// </summary>
        private static void BuildTurret()
        {
            const float ceiling = F + 3.6f;
            var cell = Vector3Int.zero;
            var hatch = new Vector3(-2.25f, 0f, -2.25f);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(cell, Vector3Int.left, Socket),
                new InteriorSocket(cell, Vector3Int.right, Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(cell, Vector3Int.up, ceiling, hatch),
                new InteriorSocket(cell, Vector3Int.down, -F, hatch),
            };
            var white = new Color(0.92f, 0.95f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(2.15f, F + 3.0f, 0f), 4f, 1.3f, white),
                LightSpec.Point(new Vector3(-2.15f, F + 3.0f, 0f), 4f, 1.3f, white),
                LightSpec.Point(new Vector3(0f, F + 3.0f, 2.15f), 4f, 1.3f, white),
                LightSpec.Point(new Vector3(0f, F + 3.0f, -2.15f), 4f, 1.3f, white),
                // 회전 받침을 비추는 작업등 + 콘솔·레이더 빛
                LightSpec.Spot(new Vector3(1.2f, F + 3.4f, -1.2f), new Vector3(-0.45f, -1f, 0.45f), 5f, 2.2f, 60f, new Color(1f, 0.95f, 0.85f)),
                LightSpec.Point(new Vector3(-2.3f, F + 1.6f, 2.3f), 2.5f, 0.6f, new Color(0.5f, 0.85f, 1f)),
                // 탄약 랙 앞 (모서리라 통로 조명이 닿지 않아 어두웠음)
                LightSpec.Point(new Vector3(2.25f, F + 2.3f, 1.9f), 2.6f, 0.9f, new Color(1f, 0.93f, 0.82f)),
            };
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_Turret_Rotor", Vector3.zero, Vector3.up, 6f),
                // 피니언 8톱니 : 회전판 48톱니 → 6배 빠르게 반대로 (turret_builder PIN_C = 1.3 + 1.3·8/48)
                new OrbitSpec("INT_Turret_Pinion", new Vector3(1.3f + 1.3f * 8f / 48f, 0f, 0f), Vector3.up, -36f),
            };
            // 스폰 = 남쪽 문 앞에서 회전 받침을 바라봄
            Build("Turret", "MD_Turret", sockets, new Vector3(0f, F, -2.6f), 0f, lights, orbits);
        }

        /// <summary>
        /// 11-7 실드 정비 통로 (1칸): 가운데 유리 봉쇄실(반지름 1.5) 안에 떠 있는 방출기 코어 + 위아래 투사기·에너지 빔 +
        /// 서로 다른 축으로 도는 고리 2(<see cref="InteriorOrbit"/>). 둘레 = 정비 통로. 모서리: 축전기 뱅크 · 제어 콘솔·실드 돔 화면 ·
        /// 전력 변환기 · 비상 차단 패널. 문 자리 = 네 옆면, 위·아래 해치는 봉쇄실을 피해 (−2.25, −2.25).
        /// </summary>
        private static void BuildShield()
        {
            const float ceiling = F + 3.6f;
            var cell = Vector3Int.zero;
            var hatch = new Vector3(-2.25f, 0f, -2.25f);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(cell, Vector3Int.left, Socket),
                new InteriorSocket(cell, Vector3Int.right, Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(cell, Vector3Int.up, ceiling, hatch),
                new InteriorSocket(cell, Vector3Int.down, -F, hatch),
            };
            var white = new Color(0.9f, 0.94f, 1f);
            var shield = new Color(0.4f, 0.75f, 1f);
            var core = new Vector3(0f, F + 1.8f, 0f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(2.15f, F + 3.0f, 0f), 4f, 1.1f, white),
                LightSpec.Point(new Vector3(-2.15f, F + 3.0f, 0f), 4f, 1.1f, white),
                LightSpec.Point(new Vector3(0f, F + 3.0f, 2.15f), 4f, 1.1f, white),
                LightSpec.Point(new Vector3(0f, F + 3.0f, -2.15f), 4f, 1.1f, white),
                // 코어 빛: 봉쇄실 안(고리·기둥을 비춤) + 유리 밖으로 번지는 푸른빛
                LightSpec.Point(core, 4.5f, 2.0f, shield),
                LightSpec.Point(new Vector3(2.3f, F + 2.3f, 1.9f), 2.6f, 0.8f, new Color(1f, 0.93f, 0.82f)), // 축전기 뱅크
            };
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_Shield_Ring1", core, Vector3.up, 30f),
                new OrbitSpec("INT_Shield_Ring2", core, new Vector3(0.35f, 1f, -0.25f), -20f),
            };
            // 스폰 = 남쪽 문 앞에서 봉쇄실을 바라봄
            Build("Shield", "MD_Shield", sockets, new Vector3(0f, F, -2.6f), 0f, lights, orbits);
        }

        /// <summary>
        /// 11-7 배터리 정비 통로 (1칸): x 방향 일자 통로(폭 3.7) 양옆 네 사분면에 배터리 랙 뱅크(셀 모듈 5단 × 2열, 구리 버스바, 케이블 다발),
        /// 가운데는 ±z 문으로 가는 교차 통로. 랙의 가운데 쪽 끝면마다 큰 저장량 게이지(세로 10칸 — 실제 저장량 연동은 11-10).
        /// 문 자리 = 네 옆면, 위·아래 해치 = 칸 중심.
        /// </summary>
        private static void BuildBattery()
        {
            const float ceiling = F + 3.6f;
            var cell = Vector3Int.zero;
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(cell, Vector3Int.left, Socket),
                new InteriorSocket(cell, Vector3Int.right, Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(cell, Vector3Int.up, ceiling),
                new InteriorSocket(cell, Vector3Int.down, -F),
            };
            var white = new Color(0.92f, 0.95f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(1.85f, F + 3.0f, 0f), 4f, 1.3f, white),
                LightSpec.Point(new Vector3(-1.85f, F + 3.0f, 0f), 4f, 1.3f, white),
                LightSpec.Point(new Vector3(0f, F + 3.0f, 1.85f), 4f, 1.1f, white),
                LightSpec.Point(new Vector3(0f, F + 3.0f, -1.85f), 4f, 1.1f, white),
                // 교차부: 게이지 4개가 마주 보는 곳의 은은한 청록빛
                LightSpec.Point(new Vector3(0f, F + 1.3f, 0f), 3f, 0.6f, new Color(0.45f, 0.85f, 1f)),
            };
            // 스폰 = 서쪽 문 앞에서 일자 통로를 바라봄
            Build("Battery", "MD_Battery", sockets, new Vector3(-2.4f, F, 0f), 90f, lights);
        }

        /// <summary>
        /// 11-7 장갑 격벽 (1칸): 두꺼운 통로 방 — 모서리 I빔 기둥, 문 사이 벽 겹 장갑판(볼트), 천장 사각 거더 틀(해치 둘레)·대각 거더,
        /// 문마다 압력 격벽 문틀(경고 줄무늬 기둥·경광등). 모서리: 비상 산소함 · 소화기함·소화기·경보 버튼 · 예비 장갑판·두께 화면 · 압력 감시 패널·밀폐 핸들.
        /// 문 자리 = 네 옆면, 위·아래 해치 = 칸 중심.
        /// </summary>
        private static void BuildArmorBulkhead()
        {
            const float ceiling = F + 3.6f;
            var cell = Vector3Int.zero;
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(cell, Vector3Int.left, Socket),
                new InteriorSocket(cell, Vector3Int.right, Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(cell, Vector3Int.up, ceiling),
                new InteriorSocket(cell, Vector3Int.down, -F),
            };
            var white = new Color(0.95f, 0.95f, 0.92f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(2.05f, F + 3.0f, 0f), 4f, 1.2f, white),
                LightSpec.Point(new Vector3(-2.05f, F + 3.0f, 0f), 4f, 1.2f, white),
                LightSpec.Point(new Vector3(0f, F + 3.0f, 2.05f), 4f, 1.2f, white),
                LightSpec.Point(new Vector3(0f, F + 3.0f, -2.05f), 4f, 1.2f, white),
                LightSpec.Point(new Vector3(0f, F + 2.6f, 0f), 3.5f, 0.8f, white), // 거더 틀 안 (해치 둘레)
            };
            // 스폰 = 남쪽 문 앞에서 맞은편 문을 바라봄
            Build("ArmorBulkhead", "MD_ArmorBulkhead", sockets, new Vector3(0f, F, -2.4f), 0f, lights);
        }

        /// <summary>
        /// 11-7 손상 통제실 (1칸, 수리반 대기실): 가운데 상황판 테이블(정거장 손상 지도)·의자 2, 위에 매단 회전 경광등(<see cref="InteriorOrbit"/>).
        /// 모서리: 사물함·벤치 · 공구 걸이판·용접기 수레 · 보수판 더미·밀봉제 통 · 경보 패널. 문 자리 = 네 옆면, 위·아래 해치는 테이블을 피해 (−2.25, −2.25).
        /// </summary>
        private static void BuildDamageControl()
        {
            const float ceiling = F + 3.6f;
            var cell = Vector3Int.zero;
            var hatch = new Vector3(-2.25f, 0f, -2.25f);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(cell, Vector3Int.left, Socket),
                new InteriorSocket(cell, Vector3Int.right, Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(cell, Vector3Int.up, ceiling, hatch),
                new InteriorSocket(cell, Vector3Int.down, -F, hatch),
            };
            var white = new Color(0.95f, 0.95f, 0.92f);
            var beacon = new Vector3(0f, ceiling - 0.42f, 0f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(2.05f, F + 3.0f, 0f), 4f, 1.2f, white),
                LightSpec.Point(new Vector3(-2.05f, F + 3.0f, 0f), 4f, 1.2f, white),
                LightSpec.Point(new Vector3(0f, F + 3.0f, 2.05f), 4f, 1.2f, white),
                LightSpec.Point(new Vector3(0f, F + 3.0f, -2.05f), 4f, 1.2f, white),
                // 경광등 주황빛 (테이블 위로 번짐)
                LightSpec.Point(beacon - new Vector3(0f, 0.25f, 0f), 3.5f, 0.9f, new Color(1f, 0.55f, 0.15f)),
            };
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_Damage_Beacon", beacon, Vector3.up, 150f),
            };
            // 스폰 = 남쪽 문 앞에서 상황판 테이블을 바라봄
            Build("DamageControl", "MD_DamageControl", sockets, new Vector3(0.6f, F, -2.4f), -15f, lights, orbits);
        }

        /// <summary>템플릿 조명 하나 (점광원 또는 스포트, 그림자 없음).</summary>
        private struct LightSpec
        {
            public Vector3 Position;
            public Vector3 Direction;
            public float Range;
            public float Intensity;
            public float SpotAngle;
            public Color Color;
            public bool IsSpot;

            public static LightSpec Point(Vector3 p, float range, float intensity, Color color) =>
                new LightSpec { Position = p, Range = range, Intensity = intensity, Color = color };

            public static LightSpec Spot(Vector3 p, Vector3 dir, float range, float intensity, float angle, Color color) =>
                new LightSpec { Position = p, Direction = dir, Range = range, Intensity = intensity, SpotAngle = angle, Color = color, IsSpot = true };
        }

        /// <summary>장식 회전 하나: FBX 조각 이름 + 템플릿 로컬 회전 중심·축·속도 (<see cref="InteriorOrbit"/>).</summary>
        private struct OrbitSpec
        {
            public string Part;
            public Vector3 Center;
            public Vector3 Axis;
            public float DegreesPerSecond;

            public OrbitSpec(string part, Vector3 center, Vector3 axis, float degreesPerSecond)
            {
                Part = part; Center = center; Axis = axis; DegreesPerSecond = degreesPerSecond;
            }
        }

        private static void Build(string name, string moduleName, List<InteriorSocket> sockets, Vector3 spawn, float spawnYaw,
            List<LightSpec> lights, List<OrbitSpec> orbits = null, Dictionary<string, Vector3> sunTilts = null)
        {
            string modelPath = ModelFolder + "SM_Interior_" + name + ".fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError($"[InteriorTemplateBuilder] 모델 없음: {modelPath}");
                return;
            }
            var module = AssetDatabase.LoadAssetAtPath<ModuleData>(ModuleFolder + moduleName + ".asset");
            var accent = AssetDatabase.LoadAssetAtPath<Material>(AccentFolder + "M_Accent_" + moduleName.Replace("MD_", "") + ".mat");
            var materials = new Dictionary<string, Material>
            {
                { "Hull", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.InteriorHullPath) },
                { "HullDark", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.InteriorHullDarkPath) },
                { "Accent", accent },
                { "Light", AssetDatabase.LoadAssetAtPath<Material>(InteriorMaterials + "M_InteriorLight.mat") },
                { "Glass", AssetDatabase.LoadAssetAtPath<Material>(InteriorMaterials + "M_InteriorGlass.mat") },
                { "Fabric", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.FabricPath) },
                { "Blanket", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.BlanketPath) },
                { "Plant", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.PlantPath) },
                { "PlantLight", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.PlantLightPath) },
                { "Soil", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.SoilPath) },
                { "Grow", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.GrowPath) },
                { "O2Liquid", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.LiquidPath) },
                { "Screen", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ScreenPath) },
                { "Metal", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.MetalPath) },
                { "Water", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.WaterPath) },
                { "Device", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.DevicePath) },
                { "Locker", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.LockerPath) },
                { "Clinic", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ClinicPath) },
                { "Sofa", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.SofaPath) },
                { "Rug", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.RugPath) },
                { "Crate", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.CratePath) },
                { "Hazard", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.HazardPath) },
                { "Rock", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.RockPath) },
                { "Molten", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.MoltenPath) },
                { "ScreenDark", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ScreenDarkPath) },
                { "ScreenGrid", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ScreenGridPath) },
                { "Plasma", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.PlasmaPath) },
                { "SolarCell", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.SolarCellPath) },
                { "Shield", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ShieldPath) },
                { "Red", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.RedPath) },
            };

            var root = new GameObject("PF_Interior_" + name);
            // 오브젝트가 하나뿐인 FBX는 Unity가 그 메시를 루트에 바로 붙여 가져옴 (11-6 창고) → 루트를 조각 하나로, 이름은 메시 이름
            var partList = new List<Transform>();
            if (model.GetComponent<MeshFilter>() != null)
                partList.Add(model.transform);
            foreach (Transform child in model.transform)
                partList.Add(child);
            foreach (var part in partList)
            {
                var filter = part.GetComponent<MeshFilter>();
                if (filter == null)
                    continue;
                bool isRoot = part == model.transform;
                string partName = isRoot ? filter.sharedMesh.name : part.name;
                var go = new GameObject(partName);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = isRoot ? Vector3.zero : part.localPosition;
                go.transform.localRotation = isRoot ? Quaternion.identity : part.localRotation;
                go.transform.localScale = isRoot ? Vector3.one : part.localScale;
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var slots = part.GetComponent<MeshRenderer>().sharedMaterials;
                var mats = new Material[slots.Length];
                for (int i = 0; i < slots.Length; i++)
                {
                    string slot = slots[i] != null ? slots[i].name : "";
                    if (!materials.TryGetValue(slot, out mats[i]) || mats[i] == null)
                    {
                        Debug.LogWarning($"[InteriorTemplateBuilder] {partName}: 재질 슬롯 '{slot}' → Hull");
                        mats[i] = materials["Hull"];
                    }
                }
                r.sharedMaterials = mats;
                if (partName.EndsWith("_Shell") || partName.EndsWith("_Glass"))
                    go.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                if (orbits != null)
                {
                    foreach (var orbit in orbits)
                    {
                        if (orbit.Part == partName)
                            go.AddComponent<InteriorOrbit>().Configure(orbit.Center, orbit.Axis, orbit.DegreesPerSecond);
                    }
                }
                // 태양 쪽으로 기우는 패널: 패널 중심에 피벗을 두고 그 아래로 옮김 (FBX 조각 원점은 템플릿 원점)
                if (sunTilts != null && sunTilts.TryGetValue(partName, out var pivotPosition))
                {
                    var pivot = new GameObject(partName + "_Pivot");
                    pivot.transform.SetParent(root.transform, false);
                    pivot.transform.localPosition = pivotPosition;
                    go.transform.SetParent(pivot.transform, true);
                    pivot.AddComponent<InteriorSunTilt>();
                }
            }
            var lightRoot = new GameObject("Lights");
            lightRoot.transform.SetParent(root.transform, false);
            foreach (var spec in lights)
            {
                var go = new GameObject(spec.IsSpot ? "Spot" : "Light");
                go.transform.SetParent(lightRoot.transform, false);
                go.transform.localPosition = spec.Position;
                if (spec.IsSpot)
                    go.transform.localRotation = Quaternion.LookRotation(spec.Direction);
                var light = go.AddComponent<Light>();
                light.type = spec.IsSpot ? LightType.Spot : LightType.Point;
                light.shadows = LightShadows.None;
                light.range = spec.Range;
                light.intensity = spec.Intensity;
                light.color = spec.Color;
                if (spec.IsSpot)
                {
                    light.spotAngle = spec.SpotAngle;
                    light.innerSpotAngle = spec.SpotAngle * 0.6f;
                }
            }

            System.IO.Directory.CreateDirectory(PrefabFolder);
            string prefabPath = PrefabFolder + "PF_Interior_" + name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);

            string templatePath = DataFolder + "IT_" + name + ".asset";
            var template = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(templatePath);
            if (template == null)
            {
                template = ScriptableObject.CreateInstance<InteriorTemplate>();
                AssetDatabase.CreateAsset(template, templatePath);
            }
            template.EditorSet(module, prefab, sockets, spawn, spawnYaw);
            EditorUtility.SetDirty(template);
            Debug.Log($"[InteriorTemplateBuilder] {name}: 문 자리 {sockets.Count}곳, 조명 {lights.Count}개");
        }
    }
}
