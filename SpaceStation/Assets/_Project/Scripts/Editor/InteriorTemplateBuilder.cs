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
            InteriorPlantSpots.SetAll(); // 11-17 식물 자리
            BuildDamageControl();
            BuildFuelCell();
            BuildNewBasicTemplates();
            BuildEvolvedTemplates();
            InteriorPlantSpots.SetAll();
            AssetDatabase.SaveAssets();
            InteriorSetup.Setup();
        }

        /// <summary>
        /// 진화 모듈 내부만 다시 만들기 (메뉴 SpaceStation/Interior/Build Evolved Templates, 2026-10-29~).
        /// Blender `BlenderWork/Interior_Evo.blend`(텍스트 블록 `evo_kit` = 단계 키트 · `*_builder` = 모듈별) → `SM_Interior_Evo*.fbx`.
        /// </summary>
        [MenuItem("SpaceStation/Interior/Build Evolved Templates")]
        public static void BuildEvolvedOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[InteriorTemplateBuilder] 플레이 모드를 끈 뒤 실행하세요.");
                return;
            }
            InteriorSetup.CreateMaterials();
            BuildEvolvedTemplates();
            InteriorPlantSpots.SetAll();
            AssetDatabase.SaveAssets();
            InteriorSetup.Setup();
            ResidentSpotBaker.BakeTemplates("IT_Evo"); // 주민 자리 (진화 템플릿만)
        }

        private static void BuildEvolvedTemplates()
        {
            BuildEvoResearch();
            BuildEvoTower();
            BuildEvoEco();
            BuildEvoPowerBank();
            BuildEvoWelfare();
            BuildEvoCulture();
            BuildEvoSolar();
            BuildEvoFusion();
            BuildEvoHydrogen();
            BuildEvoGravity();
            BuildEvoFarm();
            BuildEvoMineral();
            BuildEvoShield();
            BuildEvoDefense();
            BuildEvoArmor();
            BuildEvoWorks();
            BuildEvoEmergency();
            BuildEvoReclaim();
            BuildEvoLogistics();
            BuildEvoStorage();
            BuildEvoShipyard();
            BuildEvoAutomation();
            BuildEvoSecurity();
            BuildEvoGridHub();
            BuildEvoOrbitalMirror();
            BuildEvoFusionPlant();
            BuildEvoArcology();
            BuildEvoGrandTorus();
            BuildEvoCivicHall();
            BuildEvoSecurityHQ();
            BuildEvoBiodome();
            BuildEvoFoodFactory();
            BuildEvoRefineryCombine();
            BuildEvoShieldDome();
            BuildEvoFortress();
            BuildEvoRadiationWall();
            BuildEvoDroneDepot();
            BuildEvoCargoTransfer();
            BuildEvoStorageComplex();
            BuildEvoResearchInstitute();
            BuildEvoOrbitalCity();
            BuildEvoBiosphere();
            BuildEvoStellarGrid();
        }

        /// <summary>
        /// 진화 2단계 드론 정비창 "드론 라이트쇼 격납고" (3 × 2칸 2층, dd_builder — 사용자 2026-11-05 시안 1 "좋아 이대로 유니티로 옮겨줘"): 직접 체험 = 옥상 드론 쇼 관람 (<see cref="InteriorDroneShow"/>).
        /// 외형 × 8: 앞면 통유리 수리 칸 3 × 2층 (받침 위 드론 · 천장 로봇 팔 둘 · 환한 작업 벽 · 공구 선반) · 옥상 이착륙장 셋 · 유리 관제탑 (못 들어감) · 관람석 세 단, 앞마당 유도선 · 수레.
        /// 동선: 갑판 (우주복) → 칸 사이 입구 아치 둘 · 뒤 입구 (압력 막) → 복도 → 수리 칸 안 / 2층 높이 뚫린 홀 (드론 모빌 SwarmA · B, 쇼 대형 그림) / 드론 비행장 (FlyA · B) → 유리 엘리베이터 (1층 · 2층 · 옥상, 문 = 남쪽) → 2층 전시 · 관제실 / 옥상 (우주복).
        /// 문 자리 10 = 1층 갑판 둘레 (깊이 3.95, 닫힘 = 난간), 아래 해치 = 바닥 칸 여섯, 위 해치 없음 (하늘). 벤치 · 의자 · 관람석에 앉음 (<see cref="InteriorBenchSeats"/>). 드론은 하늘 화면 (바깥 모델 <see cref="SpaceStation.Building.DroneShowFx"/>)에 뜸.
        /// </summary>
        private static void BuildEvoDroneDepot()
        {
            const float DeckEdge = 3.95f, L1 = F + 4.94f, Roof = F + 9.67f, LX = 12.9f, LZ = 7.6f;
            const float BX0 = -2.2f, BX1 = 18.2f, BZ0 = -1.7f, BZ1 = 9.7f, Wall = BZ1 - 0.25f;
            var back = new Vector3Int(0, 0, -1);
            var front = new Vector3Int(0, 0, 1);
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x <= 2; x++)
            {
                sockets.Add(new InteriorSocket(new Vector3Int(x, 0, 0), back, DeckEdge));
                sockets.Add(new InteriorSocket(new Vector3Int(x, 0, 1), front, DeckEdge));
            }
            for (int z = 0; z <= 1; z++)
            {
                sockets.Add(new InteriorSocket(new Vector3Int(0, 0, z), Vector3Int.left, DeckEdge));
                sockets.Add(new InteriorSocket(new Vector3Int(2, 0, z), Vector3Int.right, DeckEdge));
            }
            sockets.Add(new InteriorSocket(new Vector3Int(0, 0, 0), Vector3Int.down, -F, new Vector3(0f, 0f, -2.6f)));       // 앞 갑판 (틀 ±0.95가 열린 문 패널 · 걸레받이에 닿지 않게, 코드 검토 2026-11-06)
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.down, -F, new Vector3(0f, 0f, -2.6f)));
            sockets.Add(new InteriorSocket(new Vector3Int(2, 0, 0), Vector3Int.down, -F, new Vector3(0f, 0f, -2.6f)));
            sockets.Add(new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.down, -F, new Vector3(2f, 0f, -1f)));         // 1층 홀 (전시대와 원형 벤치 사이)
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 1), Vector3Int.down, -F, new Vector3(-0.8f, 0f, -1f)));      // 1층 홀 (원형 벤치와 전시대 사이)
            sockets.Add(new InteriorSocket(new Vector3Int(2, 0, 1), Vector3Int.down, -F, new Vector3(0f, 0f, 2.65f)));       // 뒤 갑판 (1층 동쪽은 비행장)
            var white = new Color(1f, 0.97f, 0.9f);
            var warm = new Color(1f, 0.86f, 0.62f);
            var cool = new Color(0.72f, 0.82f, 1f);
            var lights = new List<LightSpec>();
            foreach (float cx in new[] { 0.6f, 8f, 15.4f })
            {
                lights.Add(LightSpec.Point(new Vector3(cx, F + 2.7f, 0.9f), 8.5f, 1.5f, white));      // 수리 칸
                lights.Add(LightSpec.Point(new Vector3(cx, L1 + 2.7f, 0.9f), 8.5f, 1.5f, white));
            }
            foreach (float x in new[] { 4.03f, 11.97f })
            {
                lights.Add(LightSpec.Point(new Vector3(x, F + 2.1f, 0.9f), 5f, 0.7f, warm));          // 입구 통로
                lights.Add(LightSpec.Point(new Vector3(x, L1 + 2.4f, 0.9f), 5f, 0.7f, warm));         // 2층 벽감
            }
            lights.Add(LightSpec.Point(new Vector3(-0.4f, F + 3.2f, 6.3f), 9.5f, 1.6f, warm));        // 1층 복도 · 홀
            lights.Add(LightSpec.Point(new Vector3(9.5f, F + 3.2f, 6.2f), 9.5f, 1.6f, warm));
            lights.Add(LightSpec.Point(new Vector3(15.9f, F + 3.9f, 4.6f), 8.5f, 1.3f, warm));
            lights.Add(LightSpec.Point(new Vector3(-0.2f, L1 + 3.2f, 6.2f), 9.5f, 1.6f, warm));       // 2층 복도 · 전시
            lights.Add(LightSpec.Point(new Vector3(10.1f, L1 + 3.2f, 6.2f), 9.5f, 1.6f, warm));
            lights.Add(LightSpec.Point(new Vector3(16f, L1 + 3.2f, 6f), 8.5f, 1.3f, cool));           // 관제실
            lights.Add(LightSpec.Point(new Vector3(7.2f, L1 + 1.6f, 6.5f), 11f, 1.8f, warm));         // 뚫린 홀 (모빌 드론이 도는 길 밖 — 길 위면 지나갈 때마다 번쩍임)
            lights.Add(LightSpec.Point(new Vector3(15.9f, F + 2.9f, 7.35f), 5.5f, 0.9f, white));      // 비행장
            lights.Add(LightSpec.Point(new Vector3(4f, F + 2.2f, -2.8f), 7f, 0.55f, cool));           // 갑판
            lights.Add(LightSpec.Point(new Vector3(12f, F + 2.2f, -2.8f), 7f, 0.55f, cool));
            lights.Add(LightSpec.Point(new Vector3(8f, F + 2.2f, 10.8f), 8f, 0.55f, cool));
            lights.Add(LightSpec.Point(new Vector3(-3.1f, F + 2.2f, 4f), 7f, 0.5f, cool));
            lights.Add(LightSpec.Point(new Vector3(19.1f, F + 2.2f, 4f), 7f, 0.5f, cool));
            lights.Add(LightSpec.Point(new Vector3(1.2f, Roof + 2.4f, 2f), 7f, 0.6f, cool));          // 옥상 이착륙장
            lights.Add(LightSpec.Point(new Vector3(14.8f, Roof + 2.4f, 2f), 7f, 0.6f, cool));
            lights.Add(LightSpec.Point(new Vector3(2f, Roof + 3f, 5f), 8f, 0.7f, warm));              // 관람석
            lights.Add(LightSpec.Point(new Vector3(9f, Roof + 3f, 5f), 8f, 0.7f, warm));
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoDroneDepot_SwarmA", new Vector3(4.9f, 0f, 7.575f), Vector3.up, 14f),    // 드론 모빌 (dd_builder swarm)
                new OrbitSpec("INT_EvoDroneDepot_SwarmB", new Vector3(4.9f, 0f, 7.575f), Vector3.up, -24f),
                new OrbitSpec("INT_EvoDroneDepot_FlyA", new Vector3(15.9f, 0f, 7.35f), Vector3.up, 60f),      // 비행장: 고리 넷을 통과
                new OrbitSpec("INT_EvoDroneDepot_FlyB", new Vector3(15.9f, 0f, 7.35f), Vector3.up, -90f),
            };
            EvoAccent("DroneDepot", "#FFD09F");
            // 스폰 = 앞 갑판에서 입구 아치를 바라봄
            Build("EvoDroneDepot", "Evolved/MD_Evo_DroneDepot", sockets, new Vector3(4.03f, F, -2.6f), 0f, lights, orbits,
                configure: root =>
                {
                    AddOpenAir(root, System.Array.Empty<Vector3>());
                    root.GetComponent<InteriorOpenAir>().SetIndoorBox(new Rect(BX0, BZ0, BX1 - BX0, BZ1 - BZ0), Roof - 0.1f);
                    // 옥상 난간 위 보이지 않는 벽 (난간은 허리 높이, 칸 경계 벽은 건물 바깥 갑판 끝이라 옥상에서 떨어지는 건 못 막음)
                    var fence = new GameObject("RoofFence") { layer = 2 };   // Ignore Raycast
                    fence.transform.SetParent(root.transform, false);
                    const float FenceIn = 0.18f, FenceH = 4f;
                    void Fence(float x0, float x1, float z0, float z1)
                    {
                        var box = fence.AddComponent<BoxCollider>();
                        box.center = new Vector3((x0 + x1) * 0.5f, Roof + FenceH * 0.5f, (z0 + z1) * 0.5f);
                        box.size = new Vector3(Mathf.Max(x1 - x0, 0.04f), FenceH, Mathf.Max(z1 - z0, 0.04f));
                    }
                    Fence(BX0 + FenceIn, BX1 - FenceIn, BZ0 + FenceIn, BZ0 + FenceIn);
                    Fence(BX0 + FenceIn, BX1 - FenceIn, BZ1 - FenceIn, BZ1 - FenceIn);
                    Fence(BX0 + FenceIn, BX0 + FenceIn, BZ0 + FenceIn, BZ1 - FenceIn);
                    Fence(BX1 - FenceIn, BX1 - FenceIn, BZ0 + FenceIn, BZ1 - FenceIn);
                    // 유리 엘리베이터 (1층 · 2층 · 옥상, 문 = 남쪽, 문짝이 열리면 기둥 속으로)
                    var car = Child(root, "INT_EvoDroneDepot_LiftCar");
                    var carGlass = Child(root, "INT_EvoDroneDepot_LiftCarGlass");
                    carGlass.SetParent(car, true);
                    foreach (var t in new[] { car, carGlass })
                        t.gameObject.AddComponent<MeshCollider>().sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                    var lift = root.AddComponent<InteriorLift>();
                    var left = new Transform[3];
                    var right = new Transform[3];
                    for (int k = 0; k < 3; k++)
                    {
                        string levelName = k == 2 ? "옥상" : null;
                        left[k] = Child(root, $"INT_EvoDroneDepot_LiftDoor{k + 1}L");
                        right[k] = Child(root, $"INT_EvoDroneDepot_LiftDoor{k + 1}R");
                        BoxOf(left[k], 0f);
                        BoxOf(right[k], 0f);
                        BoxOf(Child(root, $"INT_EvoDroneDepot_LiftCall{k + 1}"), 0.06f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, true, levelName);
                        var button = Child(root, $"INT_EvoDroneDepot_LiftBtn{k + 1}");
                        button.SetParent(car, true);
                        BoxOf(button, 0.03f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, false, levelName);
                    }
                    lift.Configure(car, new[] { 0f, L1 - F, Roof - F }, left, right, new Vector3(LX, F + 0.085f, LZ), Vector3.right, 0.75f);
                    var liftSo = new SerializedObject(lift);
                    liftSo.FindProperty("_doorTravel").floatValue = 0.5f;
                    liftSo.ApplyModifiedPropertiesWithoutUndo();
                    // 벤치 · 의자 (dd_builder hall · front, 앉는 자리 = 쉬는 자리와 같음)
                    var seats = new List<Vector4>();
                    var boxes = new List<Bounds>();
                    void Seat(float x0, float x1, float z0, float z1, float y0, float top, float z, float yaw, params float[] xs)
                    {
                        boxes.Add(new Bounds(new Vector3((x0 + x1) * 0.5f, (y0 + top + 0.01f) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, top + 0.01f - y0, z1 - z0)));
                        foreach (var x in xs)
                            seats.Add(new Vector4(x, top, z, yaw));
                    }
                    Seat(-1.7f, 0.9f, 8.85f, 9.35f, F, F + 0.44f, 9.05f, 180f, -1.3f, -0.4f, 0.5f);
                    Seat(6.5f, 8.9f, 8.85f, 9.35f, F, F + 0.44f, 9.05f, 180f, 6.9f, 7.7f, 8.5f);
                    Seat(-1.8f, 1.1f, 8.9f, 9.4f, L1, L1 + 0.44f, 9.1f, 180f, -1.35f, -0.35f, 0.65f);
                    Seat(8.7f, 11.7f, 8.9f, 9.4f, L1, L1 + 0.44f, 9.1f, 180f, 9.2f, 10.2f, 11.2f);
                    Seat(2.8f, 5.2f, 11.1f, 11.6f, F, F + 0.44f, 11.3f, 180f, 3.2f, 4f, 4.8f);
                    Seat(11.1f, 13.5f, 11.1f, 11.6f, F, F + 0.44f, 11.3f, 180f, 11.5f, 12.3f, 13.1f);
                    foreach (float x in new[] { 15.6f, 16.6f })
                        Seat(x - 0.24f, x + 0.24f, 6.16f, 6.64f, L1, L1 + 0.44f, 6.4f, 0f, x);
                    for (int k = 0; k < 8; k++)
                    {
                        float a = Mathf.Deg2Rad * (45f + 45f * k);
                        float x = 4.9f + 0.8f * Mathf.Cos(a), z = 7.575f + 0.8f * Mathf.Sin(a);
                        Seat(x - 0.2f, x + 0.2f, z - 0.2f, z + 0.2f, F, F + 0.47f, z, Mathf.Rad2Deg * Mathf.Atan2(Mathf.Cos(a), Mathf.Sin(a)), x);
                    }
                    root.AddComponent<InteriorBenchSeats>().Configure(seats.ToArray(), boxes.ToArray(), -6f);
                    // 관람석 (dd_builder roof ROWS): 어느 자리든 앉으면 쇼 쪽 하늘을 올려다봄 (DroneShowFx 큰 쇼 가운데 x, z를 향해)
                    const float ShowX = 5.25f, ShowZ = -4f;
                    var standSeats = new List<Vector4>();
                    var standBoxes = new List<Bounds>();
                    foreach (var (z0, h) in new[] { (5.74f, 0.45f), (6.84f, 0.9f), (7.94f, 1.35f) })
                    {
                        foreach (var (a0, a1, xs) in new[] { (-1.2f, 4.6f, new[] { -0.7f, 0.25f, 1.2f, 2.15f, 3.1f, 4.05f }), (6f, 11.7f, new[] { 6.55f, 7.5f, 8.45f, 9.4f, 10.35f, 11.25f }) })
                        {
                            float top = Roof + h;
                            standBoxes.Add(new Bounds(new Vector3((a0 + a1) * 0.5f, (Roof + top + 0.01f) * 0.5f, z0 + 0.275f), new Vector3(a1 - a0, top + 0.01f - Roof, 0.59f)));
                            foreach (var x in xs)
                                standSeats.Add(new Vector4(x, top, z0 + 0.3f, Mathf.Rad2Deg * Mathf.Atan2(ShowX - x, ShowZ - (z0 + 0.3f))));
                        }
                    }
                    var stands = root.AddComponent<InteriorBenchSeats>();
                    stands.Configure(standSeats.ToArray(), standBoxes.ToArray(), -42f);
                    // 쇼: 버튼 · 로비 화면 · 관제실 화면 (dd_builder hall DD_Screen · DD_DeskScreen)
                    var showButton = BoxOf(Child(root, "INT_EvoDroneDepot_ShowBtn"), 0.06f).transform;
                    var boards = new[]
                    {
                        new InteriorDroneShow.Screen { Center = new Vector3(4.9f, F + 1.9f, Wall - 0.07f), Normal = new Vector3(0f, 0f, -1f), Width = 3.04f, Height = 1.54f },
                        new InteriorDroneShow.Screen { Center = new Vector3(16.1f, L1 + 1.9f, Wall - 0.07f), Normal = new Vector3(0f, 0f, -1f), Width = 3.04f, Height = 1.54f },
                    };
                    var desks = new List<InteriorDroneShow.Screen>();
                    foreach (float x in new[] { 15.2f, 16.1f, 17f })
                        desks.Add(new InteriorDroneShow.Screen { Center = new Vector3(x, L1 + 1.02f, 7.46f), Normal = new Vector3(0f, 0f, -1f), Width = 0.68f, Height = 0.4f });
                    root.AddComponent<InteriorDroneShow>().Configure(showButton, stands, boards, desks.ToArray());
                    // 글자: 쇼 버튼 표지 · 쇼 대형 그림 이름 (dd_builder show_boards)
                    InteriorSignLabels.Label Board(float x, string text) => new InteriorSignLabels.Label
                    {
                        Center = new Vector3(x, L1 + 0.6f, Wall - 0.047f), Normal = new Vector3(0f, 0f, -1f), Up = Vector3.up, Width = 0.9f, Height = 0.2f, Text = text,
                    };
                    root.AddComponent<InteriorSignLabels>().Configure(new[]
                    {
                        new InteriorSignLabels.Label { Center = new Vector3(6.9f, Roof + 0.7f, 4.9f), Normal = new Vector3(0f, 0f, -1f), Up = Vector3.up, Width = 0.46f, Height = 0.24f, Text = "<b>드론 쇼</b>\n<size=70%>위 버튼</size>" },
                        Board(2.3f, "별"), Board(3.6f, "왕관"), Board(4.9f, "하트"), Board(6.2f, "소용돌이"), Board(7.5f, "물결"),
                    });
                });
        }

        /// <summary>
        /// 진화 2단계 방사선 차폐벽 "별자리 병풍 정원" (ㄱ자 5칸, rw_builder — 사용자 2026-11-05 시안 1 "좋아 이대로 유니티로 옮겨줘"): 정원 · 전시관 구성 (직접 체험 모듈 아님).
        /// 외형 × 8: 앞 3칸 = 차폐판 여덟 장 지그재그 병풍 (밤의 일월오봉도, 별 Stars는 저마다 반짝임) + 병풍에 붙은 은하수 차폐 몸통 (윗면 River · 안 천장 RiverUnder = MilkyRiver 셰이더 내부 크기) + 양 끝 세 날개 표지 기둥, 뒤 왼쪽 2칸 = 달문 피난처.
        /// 동선: 갑판 (우주복) → 병풍 앞 산책길 · 벤치 / 마당 (디딤돌 · 석등 · 돌 정원) → 계단 → 강둑 회랑 → 네 단 → 강 위 전망 부두 / 뒷벽 아치 둘 (압력 막) → 강 아래 전시 회랑 (혼천의 Armillary · 천구의 Globe · 별자리 석판 · 차폐층 단면 · 방사선 경보판 <see cref="InteriorRadiationBoard"/>) / 달문 (압력 막) → 달맞이 찻집.
        /// 문 자리 10 = 갑판 둘레 (깊이 3.95, 닫힘 = 난간, 오목한 자리 포함), 아래 해치 = 칸마다, 위 해치 없음 (하늘). 벤치 · 찻집 자리에 앉음 (<see cref="InteriorBenchSeats"/>).
        /// </summary>
        private static void BuildEvoRadiationWall()
        {
            const float DeckEdge = 3.95f, WalkY = F + 2.45f, RoofLight = F + 2.4f;
            var back = new Vector3Int(0, 0, -1);
            var front = new Vector3Int(0, 0, 1);
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x <= 2; x++)
                sockets.Add(new InteriorSocket(new Vector3Int(x, 0, 0), back, DeckEdge));
            sockets.Add(new InteriorSocket(new Vector3Int(0, 0, 1), front, DeckEdge));
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 1), front, DeckEdge));
            sockets.Add(new InteriorSocket(new Vector3Int(2, 0, 0), front, DeckEdge));         // 오목한 자리 (동쪽 뒷길)
            sockets.Add(new InteriorSocket(new Vector3Int(0, 0, 0), Vector3Int.left, DeckEdge));
            sockets.Add(new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.left, DeckEdge));
            sockets.Add(new InteriorSocket(new Vector3Int(2, 0, 0), Vector3Int.right, DeckEdge));
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 1), Vector3Int.right, DeckEdge));
            sockets.Add(new InteriorSocket(new Vector3Int(0, 0, 0), Vector3Int.down, -F, new Vector3(-2.4f, 0f, -2.3f)));    // 병풍 앞 서쪽 (뚜껑 · 틀이 난간 · 벤치와 겹치지 않게)
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.down, -F, new Vector3(0f, 0f, -2.2f)));       // 병풍 앞 가운데
            sockets.Add(new InteriorSocket(new Vector3Int(2, 0, 0), Vector3Int.down, -F, new Vector3(2.4f, 0f, -2.3f)));     // 병풍 앞 동쪽
            sockets.Add(new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.down, -F, new Vector3(-1.3f, 0f, -3.2f)));    // 마당 서쪽
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 1), Vector3Int.down, -F, new Vector3(-0.8f, 0f, -0.1f)));    // 찻집 동쪽
            var warm = new Color(1f, 0.86f, 0.6f);
            var white = new Color(1f, 0.97f, 0.9f);
            var cool = new Color(0.72f, 0.82f, 1f);
            var lemon = new Color(1f, 0.88f, 0.5f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(0f, F + 1.5f, -1.7f), 6.5f, 1.1f, white),            // 병풍 앞
                LightSpec.Point(new Vector3(5f, F + 1.5f, -1.7f), 6.5f, 1.1f, white),
                LightSpec.Point(new Vector3(11f, F + 1.5f, -1.7f), 6.5f, 1.1f, white),
                LightSpec.Point(new Vector3(16f, F + 1.5f, -1.7f), 6.5f, 1.1f, white),
                LightSpec.Point(new Vector3(0.62f, F + 1.5f, 4.75f), 5f, 0.9f, lemon),           // 석등
                LightSpec.Point(new Vector3(7.38f, F + 2.25f, 5.1f), 3.5f, 0.6f, lemon),
                LightSpec.Point(new Vector3(4f, F + 1.9f, 4.4f), 5f, 0.7f, warm),                // 달문 앞
                LightSpec.Point(new Vector3(2.8f, F + 1.8f, 2.8f), 4.5f, 0.55f, cool),             // 회랑 아래
                LightSpec.Point(new Vector3(8f, F + 1.8f, 2.75f), 4.5f, 0.55f, cool),
                LightSpec.Point(new Vector3(13.4f, F + 1.8f, 2.8f), 4.5f, 0.55f, cool),
                LightSpec.Point(new Vector3(3f, WalkY + 2.1f, 2.7f), 7f, 0.65f, cool),           // 강둑 회랑
                LightSpec.Point(new Vector3(13f, WalkY + 2.1f, 2.7f), 7f, 0.65f, cool),
                LightSpec.Point(new Vector3(1.1f, RoofLight, 0.45f), 4f, 0.8f, white),           // 강 아래 전시 벽감
                LightSpec.Point(new Vector3(5.7f, RoofLight, 0.45f), 4f, 0.8f, white),
                LightSpec.Point(new Vector3(10.3f, RoofLight, 0.45f), 4f, 0.8f, white),
                LightSpec.Point(new Vector3(14.9f, RoofLight, 0.45f), 4f, 0.8f, white),
                LightSpec.Point(new Vector3(4f, F + 1.75f, 8f), 7.5f, 1.3f, warm),               // 찻집: 달 등 아래 (등에 너무 가까우면 아랫면이 번쩍임)
                LightSpec.Point(new Vector3(-0.4f, F + 2.4f, 8f), 5.5f, 1f, warm),
                LightSpec.Point(new Vector3(8.4f, F + 2.4f, 8f), 5.5f, 1f, warm),
                LightSpec.Point(new Vector3(-3.1f, F + 2.2f, 8f), 7f, 0.5f, cool),               // 갑판
                LightSpec.Point(new Vector3(19f, F + 2.2f, 1.8f), 7f, 0.55f, cool),
                LightSpec.Point(new Vector3(4f, F + 2.2f, 11f), 7f, 0.5f, cool),
                LightSpec.Point(new Vector3(11f, F + 2.2f, 6.5f), 6f, 0.5f, cool),
            };
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoRadiationWall_Armillary", new Vector3(1.1f, 0f, 0f), Vector3.up, 20f),     // 혼천의 안쪽 고리
                new OrbitSpec("INT_EvoRadiationWall_Globe", new Vector3(5.7f, 0f, 0f), Vector3.up, 12f),         // 천구의
            };
            EvoAccent("RadiationWall", "#C8D0A0");
            // 스폰 = 병풍 앞 산책길에서 병풍을 바라봄
            Build("EvoRadiationWall", "Evolved/MD_Evo_RadiationWall", sockets, new Vector3(4f, F, -2.2f), 0f, lights, orbits,
                configure: root =>
                {
                    AddOpenAir(root, new[]
                    {
                        // 찻집 (rw_builder SROOM) · 강 아래 회랑 (몸통 안)
                        new Vector3(-0.1f, 8f, 2.05f), new Vector3(2.7f, 8f, 2.05f), new Vector3(5.3f, 8f, 2.05f), new Vector3(8.1f, 8f, 2.05f),
                        new Vector3(0f, 0.65f, 1.3f), new Vector3(1.6f, 0.65f, 1.3f), new Vector3(3.2f, 0.65f, 1.3f), new Vector3(4.8f, 0.65f, 1.3f), new Vector3(6.4f, 0.65f, 1.3f), new Vector3(8f, 0.65f, 1.3f),
                        new Vector3(9.6f, 0.65f, 1.3f), new Vector3(11.2f, 0.65f, 1.3f), new Vector3(12.8f, 0.65f, 1.3f), new Vector3(14.4f, 0.65f, 1.3f), new Vector3(16f, 0.65f, 1.3f),
                    });
                    // 방사선 경보판 (rw_builder RW_DoseScreen): 수용 = 주민 자리 수
                    root.AddComponent<InteriorRadiationBoard>().Configure(new[]
                    {
                        new InteriorRadiationBoard.Panel { Center = new Vector3(16f, F + 1.45f, 1.73f), Normal = new Vector3(0f, 0f, -1f), Up = Vector3.up, Width = 1.28f, Height = 0.78f },
                    }, 25);
                    // 글자: 벽 설명판 (rw_builder INFO, 병풍과 같은 별자리) · 전시 안내판 · 달문 표지판
                    InteriorSignLabels.Label Info(float x, float z, float nx, string text) => new InteriorSignLabels.Label
                    {
                        Center = new Vector3(x, -0.04f, z), Normal = new Vector3(nx, 0f, 0.921f), Up = Vector3.up, Width = 1.3f, Height = 0.15f, Text = text,
                    };
                    InteriorSignLabels.Label Plaque(float x, float z, string text) => new InteriorSignLabels.Label
                    {
                        Center = new Vector3(x, F + 0.957f, z + 0.0222f), Normal = new Vector3(0f, 0.5f, 0.866f), Up = new Vector3(0f, 0.866f, -0.5f), Width = 0.48f, Height = 0.22f, Text = text,
                    };
                    InteriorSignLabels.Label Sign(float x, string text) => new InteriorSignLabels.Label
                    {
                        Center = new Vector3(x, F + 1.2f, 5.74f), Normal = new Vector3(0f, 0f, -1f), Up = Vector3.up, Width = 0.72f, Height = 0.32f, Text = text,
                    };
                    root.AddComponent<InteriorSignLabels>().Configure(new[]
                    {
                        Info(0.082f, -0.087f, 0.389f, "카시오페이아"), Info(2.238f, -0.037f, -0.389f, "직녀"), Info(4.562f, -0.037f, 0.389f, "오리온"), Info(6.838f, -0.037f, -0.389f, "북두칠성"),
                        Info(9.162f, -0.037f, 0.389f, "백조"), Info(11.438f, -0.037f, -0.389f, "사자"), Info(13.762f, -0.037f, 0.389f, "견우"), Info(15.918f, -0.087f, -0.389f, "전갈"),
                        Plaque(1.72f, 0.12f, "<b>혼천의</b>\n<size=70%>하늘의 움직임을 재는 기구</size>"),
                        Plaque(6.32f, 0.12f, "<b>천구의</b>\n<size=70%>별자리를 새긴 하늘 공</size>"),
                        Plaque(11.28f, 0.12f, "<b>별자리 석판</b>\n<size=70%>돌에 새긴 별 지도</size>"),
                        Plaque(13.98f, 0.14f, "<b>차폐층 단면</b>\n<size=70%>금속 · 흡수층 · 물 · 바깥판</size>"),
                        Sign(1.5f, "<b>방사선\n피난처</b>"),
                        Sign(6.5f, "<b>달맞이\n찻집</b>"),
                    });
                    // 벤치 (rw_builder front · yard · shelter, 앉는 자리 = 쉬는 자리와 같음)
                    var seats = new List<Vector4>();
                    var boxes = new List<Bounds>();
                    void Bench(float x0, float x1, float z0, float z1, float top, float z, float yaw, params float[] xs)
                    {
                        boxes.Add(new Bounds(new Vector3((x0 + x1) * 0.5f, F + (top + 0.01f) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, top + 0.01f, z1 - z0)));
                        foreach (var x in xs)
                            seats.Add(new Vector4(x, F + top, z, yaw));
                    }
                    Bench(2.1f, 5.9f, -3.42f, -2.92f, 0.44f, -3.1f, 0f, 2.575f, 3.525f, 4.475f, 5.425f);
                    Bench(10.1f, 13.9f, -3.42f, -2.92f, 0.44f, -3.1f, 0f, 10.575f, 11.525f, 12.475f, 13.425f);
                    Bench(-0.75f, 2.55f, 2.07f, 2.52f, 0.44f, 2.3f, 0f, -0.3f, 0.9f, 2.1f);
                    Bench(0.05f, 2.35f, 9.3f, 9.78f, 0.44f, 9.5f, 180f, 0.625f, 1.775f);
                    Bench(5.65f, 7.95f, 9.3f, 9.78f, 0.44f, 9.5f, 180f, 6.225f, 7.375f);
                    foreach (var (x, z, yaw) in new[] { (-1.278f, 7.04f, 67.5f), (-0.91f, 6.672f, 22.5f), (8.91f, 6.672f, -22.5f), (9.278f, 7.04f, -67.5f), (9.278f, 8.96f, -112.5f), (8.91f, 9.328f, -157.5f), (-0.91f, 9.328f, 157.5f), (-1.278f, 8.96f, 112.5f) })
                        Bench(x - 0.26f, x + 0.26f, z - 0.26f, z + 0.26f, 0.43f, z, yaw, x);
                    root.AddComponent<InteriorBenchSeats>().Configure(seats.ToArray(), boxes.ToArray(), -15f);   // 앉으면 병풍이 눈앞에 높이 서므로 조금 올려다봄
                });
        }

        /// <summary>
        /// 진화 2단계 요새 포대 "별 요새 불꽃놀이" (3 × 2칸, fo_builder — 사용자 2026-11-05 시안 1 "좋아 이대로 유니티로 옮겨줘"): 직접 체험 = 폭죽놀이 직관 (<see cref="InteriorFireworkShow"/>).
        /// 외형 × 8 그대로: 별 모양 능보 성곽 (계단 비탈 · 산호 띠, 위 = 걷는 성벽 길) · 앞 반월보 · 연병장 + 십자 길 · 육각 본성 (망루 셋 · 뿔지붕 · 깃발 · 도는 거리 측정기 Range) · 앞 능보 쌍열 포탑 (실제 운석 방향, <see cref="InteriorTurretAim"/>) · 뒤 능보 박격포대 · 깃발 줄.
        /// 동선: 갑판 → 서쪽 성벽 계단 → 성벽 위 길 → 북쪽 계단 둘 → 연병장 → 남쪽 관람석 · 쇼 레버 / 본성 (압력 막, 실제 운석 예보 <see cref="InteriorRadar"/> · 불꽃 별자리표).
        /// 문 자리 10 = 갑판 둘레 (깊이 3.95, 닫힘 = 난간), 아래 해치 = 칸마다, 위 해치 없음 (하늘). 불꽃은 하늘 화면 (바깥 모델 <see cref="SpaceStation.Building.FireworkFx"/>)에 터짐.
        /// </summary>
        private static void BuildEvoFortress()
        {
            const float DeckEdge = 3.95f, Ramp = F + 1.8f, GunY = Ramp + 1.95f, KX = 8f, KZ = 4.2f;
            var back = new Vector3Int(0, 0, -1);
            var front = new Vector3Int(0, 0, 1);
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x <= 2; x++)
            {
                sockets.Add(new InteriorSocket(new Vector3Int(x, 0, 0), back, DeckEdge));
                sockets.Add(new InteriorSocket(new Vector3Int(x, 0, 1), front, DeckEdge));
            }
            for (int z = 0; z <= 1; z++)
            {
                sockets.Add(new InteriorSocket(new Vector3Int(0, 0, z), Vector3Int.left, DeckEdge));
                sockets.Add(new InteriorSocket(new Vector3Int(2, 0, z), Vector3Int.right, DeckEdge));
            }
            sockets.Add(new InteriorSocket(new Vector3Int(0, 0, 0), Vector3Int.down, -F, new Vector3(-3.1f, 0f, -1.5f)));   // 서쪽 갑판
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.down, -F, new Vector3(-3f, 0f, 2.9f)));      // 연병장 남쪽
            sockets.Add(new InteriorSocket(new Vector3Int(2, 0, 0), Vector3Int.down, -F, new Vector3(3.1f, 0f, -1.5f)));    // 동쪽 갑판
            sockets.Add(new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.down, -F, new Vector3(-3.1f, 0f, -1.6f)));
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 1), Vector3Int.down, -F, new Vector3(-2.8f, 0f, -2.8f)));   // 연병장 북쪽
            sockets.Add(new InteriorSocket(new Vector3Int(2, 0, 1), Vector3Int.down, -F, new Vector3(3.1f, 0f, -1.6f)));
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.86f, 0.55f);
            var cool = new Color(0.72f, 0.82f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(KX, F + 2.6f, KZ), 5f, 1.4f, white),               // 본성 안
                LightSpec.Point(new Vector3(KX, F + 3.2f, 1f), 7f, 0.75f, warm),               // 본성 앞 · 관람석
                LightSpec.Point(new Vector3(KX, F + 3.2f, 7f), 6f, 0.5f, warm),
                LightSpec.Point(new Vector3(-0.3f, Ramp + 2.2f, 8.4f), 6f, 0.8f, warm),        // 뒤 능보
                LightSpec.Point(new Vector3(16.3f, Ramp + 2.2f, 8.4f), 6f, 0.8f, warm),
                LightSpec.Point(new Vector3(-0.25f, Ramp + 2.6f, -0.2f), 6f, 0.8f, cool),      // 앞 능보
                LightSpec.Point(new Vector3(16.25f, Ramp + 2.6f, -0.2f), 6f, 0.8f, cool),
                LightSpec.Point(new Vector3(-3.1f, F + 2.2f, 4f), 7f, 0.55f, cool),            // 갑판
                LightSpec.Point(new Vector3(19.1f, F + 2.2f, 4f), 7f, 0.55f, cool),
                LightSpec.Point(new Vector3(4f, F + 2.2f, -3.2f), 7f, 0.55f, cool),
                LightSpec.Point(new Vector3(12f, F + 2.2f, -3.2f), 7f, 0.55f, cool),
                LightSpec.Point(new Vector3(4f, F + 2.2f, 10.8f), 7f, 0.55f, cool),
                LightSpec.Point(new Vector3(12f, F + 2.2f, 10.8f), 7f, 0.55f, cool),
            };
            foreach (var (x, z) in new[] { (3.5f, 5.2f), (12.5f, 5.2f), (3.5f, 2.5f), (12.5f, 2.5f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 2.3f, z), 6f, 0.85f, warm));     // 연병장 등
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoFortress_Range", new Vector3(KX, 0f, KZ), Vector3.up, 60f),         // 꼭대기 거리 측정기
                new OrbitSpec("INT_EvoFortress_Sweep", new Vector3(8f, 0f, 4.85f), Vector3.up, 60f),      // 상황판 쓸기 빛
            };
            EvoAccent("Fortress", "#FF9F9F");
            // 스폰 = 서쪽 성벽 위에서 연병장 · 본성을 바라봄
            Build("EvoFortress", "Evolved/MD_Evo_Fortress", sockets, new Vector3(-0.3f, Ramp, 4f), 90f, lights, orbits,
                configure: root =>
                {
                    const string N = "INT_EvoFortress_";
                    AddOpenAir(root, new[] { new Vector3(KX, KZ, 1.85f), new Vector3(KX, 2.3f, 0.7f) });   // 본성 · 정문 굴
                    var r = root.transform;
                    Transform Pivot(string pivotName, Transform parent, Vector3 at, string part)
                    {
                        var p = new GameObject(pivotName).transform;
                        p.SetParent(parent, false);
                        p.position = r.TransformPoint(at);
                        p.rotation = r.rotation;
                        var mesh = Child(root, part);
                        if (mesh != null)
                            mesh.SetParent(p, true);
                        return p;
                    }
                    // 앞 능보 쌍열 포탑 (fo_builder GUN · 포신 x ±0.17): 받침 = 위 축, 포신 = 받침 자식 (−z로 밀림), 실제 운석 방향
                    void Gun(string tag, float cx, float cz)
                    {
                        var turret = Pivot("TurretPivot" + tag, r, new Vector3(cx, GunY, cz), N + "Turret" + tag);
                        var left = Pivot("BarrelPivot" + tag + "L", turret, new Vector3(cx - 0.17f, GunY, cz), N + "Barrel" + tag + "L");
                        var right = Pivot("BarrelPivot" + tag + "R", turret, new Vector3(cx + 0.17f, GunY, cz), N + "Barrel" + tag + "R");
                        root.AddComponent<InteriorTurretAim>().Configure(turret, new[] { left, right }, 0.14f);
                    }
                    Gun("A", -0.25f, -0.2f);
                    Gun("B", 16.25f, -0.2f);
                    // 본성 레이더 상황판 (fo_builder RADAR) = 실제 운석 예보, 예보 글 = 북서 면 화면 (중심에서 1.548m)
                    root.AddComponent<InteriorRadar>().Configure(new Vector3(8f, F + 0.958f, 4.85f), 0.48f,
                        Child(root, N + "Sweep").GetComponent<InteriorOrbit>(),
                        Child(root, N + "Beacon").GetComponent<Renderer>(),
                        new Vector3(7.226f, F + 1.6f, 5.541f), new Vector3(0.5f, 0f, -0.866f), Vector3.up, new Vector2(1.2f, 0.7f));
                    // 불꽃놀이: 관람석 (fo_builder SEAT_ROWS · SEAT_BLOCKS, 줄마다 4자리, 뒤 능보 쪽을 봄) · 쇼 레버 · 박격포대 대롱 끝 (fo_builder mortar_pad)
                    var rows = new[] { (1.25f, 1.7f, 0.42f), (0.87f, 1.25f, 0.82f), (0.5f, 0.87f, 1.22f) };
                    var blocks = new[] { (2.85f, 5.7f, -25f), (10.3f, 13.15f, 25f) };
                    var seats = new List<Vector4>();
                    var boxes = new List<Bounds>();
                    foreach (var (x0, x1, yaw) in blocks)
                    {
                        foreach (var (z0, z1, h) in rows)
                        {
                            boxes.Add(new Bounds(new Vector3((x0 + x1) * 0.5f, F + h * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, h + 0.01f, z1 - z0)));
                            for (int k = 0; k < 4; k++)
                                seats.Add(new Vector4(x0 + (x1 - x0) * (k + 0.5f) / 4f, F + h, (z0 + z1) * 0.5f - 0.02f, yaw));
                        }
                    }
                    var muzzles = new List<Vector3>();
                    foreach (var (cx, cz) in new[] { (-0.35f, 8.4f), (16.35f, 8.4f) })
                    {
                        for (int k = 0; k < 4; k++)
                        {
                            float a = (45f + 90f * k) * Mathf.Deg2Rad;
                            muzzles.Add(new Vector3(cx + 0.42f * Mathf.Cos(a), Ramp + 1.16f, cz + 0.42f * Mathf.Sin(a)));
                        }
                    }
                    BoxOf(Child(root, N + "Lever"), 0.08f);
                    var lever = Pivot("LeverPivot", r, new Vector3(6.25f, F + 1.02f, 1f), N + "Lever");
                    root.AddComponent<InteriorFireworkShow>().Configure(seats.ToArray(), boxes.ToArray(), muzzles.ToArray(), 4, lever, new Vector3(KX, 8f, KZ),
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.SparkPath));
                });
        }

        /// <summary>
        /// 진화 2단계 실드 돔 "오로라 등대" (2 × 2칸, 2칸 높이, sd_builder — 사용자 2026-11-05 시안 1 "좋아 이대로 유니티로 옮겨줘"): 정원 · 전시관 구성 (직접 체험 모듈 아님).
        /// 외형 그대로: 별빛 유리 바다 (걸어 다니는 바닥, StarSea 셰이더) · 바위섬 · 나선 줄무늬 등대 · 유리 등불 방의 도는 렌즈 드럼 Lens + 등대 빛 둘 (LightBeamFx, 렌즈와 함께 돎) · 꼭대기 레이더 Radar · 둘레 오로라 커튼 셋 (AuroraFx, 바깥과 같은 호) · 앞 오른쪽 등대지기 집 ↔ 유리 통로 ↔ 등대.
        /// 동선: 갑판 (우주복) → 집 남쪽 압력 막 → 집 안 (실제 운석 예보 레이더 상황판 <see cref="InteriorRadar"/> · 탁자 · 긴 의자) → 유리 통로 → 등대 1층 → 나선 계단 (폭 1.3m, 층계참 끝 밑 머리 공간 때문에 한 바퀴 2.86m) → 꼭대기 층계참 → 압력 막 → 전망대 (우주복, 망원경 둘 <see cref="InteriorTelescope"/>).
        /// 문 자리 8 = 갑판 둘레 (깊이 3.95, 닫힘 = 난간), 아래 해치 = 칸마다 넷, 위 해치 없음 (하늘).
        /// </summary>
        private static void BuildEvoShieldDome()
        {
            const float DeckEdge = 3.95f, CX = 3.2f, CZ = 4.8f, Balcony = F + 7.4f, LensY = Balcony + 2.55f;
            var back = new Vector3Int(0, 0, -1);
            var front = new Vector3Int(0, 0, 1);
            var sockets = new List<InteriorSocket>();
            for (int k = 0; k <= 1; k++)
            {
                sockets.Add(new InteriorSocket(new Vector3Int(k, 0, 0), back, DeckEdge));
                sockets.Add(new InteriorSocket(new Vector3Int(k, 0, 1), front, DeckEdge));
                sockets.Add(new InteriorSocket(new Vector3Int(0, 0, k), Vector3Int.left, DeckEdge));
                sockets.Add(new InteriorSocket(new Vector3Int(1, 0, k), Vector3Int.right, DeckEdge));
            }
            sockets.Add(new InteriorSocket(new Vector3Int(0, 0, 0), Vector3Int.down, -F, new Vector3(0f, 0f, -1.5f)));      // 남쪽 갑판
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.down, -F, new Vector3(2.9f, 0f, 0f)));       // 집 동쪽
            sockets.Add(new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.down, -F, new Vector3(-1.6f, 0f, 1.6f)));    // 북서쪽 갑판
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 1), Vector3Int.down, -F));                                  // 북동쪽 갑판
            var warm = new Color(1f, 0.86f, 0.55f);
            var white = new Color(1f, 0.97f, 0.92f);
            var cool = new Color(0.72f, 0.82f, 1f);
            var beacon = new Color(0.62f, 0.92f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(CX, LensY, CZ), 8f, 2.2f, beacon),               // 렌즈 (등불 방 · 전망대)
                LightSpec.Point(new Vector3(CX, F + 1.7f, CZ), 4.5f, 1.2f, white),           // 등대 1층
                LightSpec.Point(new Vector3(CX, F + 4.4f, CZ), 4.5f, 1f, cool),              // 계단 가운데
                LightSpec.Point(new Vector3(CX, Balcony - 0.6f, CZ), 4f, 0.9f, cool),        // 꼭대기 층계참
                LightSpec.Point(new Vector3(7f, F + 2.45f, 0.2f), 5f, 1.1f, warm),           // 집 매단 등
                LightSpec.Point(new Vector3(9f, F + 2.45f, 0.2f), 5f, 1.1f, warm),
                LightSpec.Point(new Vector3(8.98f, F + 2.1f, -2.1f), 4.5f, 0.8f, warm),      // 집 문 위 등
                LightSpec.Point(new Vector3(5.8f, F + 1.75f, 2.2f), 4f, 0.7f, cool),         // 유리 통로
            };
            foreach (var (x, z) in new[] { (-2.4f, -2.4f), (10.8f, 10.8f), (-2.4f, 10.8f), (4.4f, -3.4f), (10.85f, -3f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 2.3f, z), 6f, 0.85f, warm));   // 갑판 가로등
            foreach (var az in new[] { 60f, 150f, 240f, 330f })
            {
                float a = az * Mathf.Deg2Rad;
                lights.Add(LightSpec.Point(new Vector3(CX + 4.8f * Mathf.Cos(a), F + 3.4f, CZ + 4.8f * Mathf.Sin(a)), 9f, 1f, cool));   // 밤하늘 찬 빛 (등대 몸통 · 바위)
            }
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoShieldDome_Lens", new Vector3(CX, 0f, CZ), Vector3.up, 24f),         // 렌즈 드럼 + 등대 빛 (자식)
                new OrbitSpec("INT_EvoShieldDome_Radar", new Vector3(CX, 0f, CZ), Vector3.up, 90f),        // 꼭대기 레이더
                new OrbitSpec("INT_EvoShieldDome_Sweep", new Vector3(8.95f, 0f, 1.05f), Vector3.up, 60f),  // 상황판 쓸기 빛
            };
            EvoAccent("ShieldDome", "#A0C8FF");
            // 스폰 = 남쪽 갑판에서 바위섬 · 등대를 바라봄
            Build("EvoShieldDome", "Evolved/MD_Evo_ShieldDome", sockets, new Vector3(4.5f, F, -2.6f), -10f, lights, orbits,
                configure: root =>
                {
                    const string N = "INT_EvoShieldDome_";
                    AddOpenAir(root, new[]
                    {
                        new Vector3(CX, CZ, 1.6f),                                                                                                               // 등대 (전망대 = 밖)
                        new Vector3(4.9f, 3.1f, 0.95f), new Vector3(5.46f, 2.54f, 0.95f), new Vector3(6.03f, 1.97f, 0.95f), new Vector3(6.45f, 1.55f, 0.95f),   // 유리 통로
                        new Vector3(7.1f, -0.75f, 1.45f), new Vector3(8.9f, -0.75f, 1.45f), new Vector3(8.9f, 1f, 1.45f), new Vector3(7.4f, 0.8f, 1.2f),         // 집
                    });
                    // 등대 빛 둘: 렌즈 원판 (sd_builder lens, 방위 45 · 225)에서 시작해 렌즈와 함께 돎, 하늘 벽 앞에서 스며듦. 등대 재질은 내부 빛기둥과 따로
                    var beamRoot = new GameObject("BeaconBeams");
                    beamRoot.transform.SetParent(root.transform, false);
                    beamRoot.transform.SetParent(Child(root, N + "Lens"), true);
                    var from = new Vector3[2];
                    var to = new Vector3[2];
                    var normals = new Vector3[2];
                    for (int k = 0; k < 2; k++)
                    {
                        float a = (45f + 180f * k) * Mathf.Deg2Rad;
                        normals[k] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        from[k] = new Vector3(CX, LensY, CZ) + normals[k] * 1f;
                        to[k] = new Vector3(CX, LensY, CZ) + normals[k] * 6.9f;
                    }
                    AddLightShafts(beamRoot, from, to, new[] { 1.16f, 1.16f }, normals, 3.2f, InteriorSetup.Evo2BeaconBeamPath);
                    // 오로라: 바깥 WireAurora와 같은 호를 등대 둘레에, 하늘 벽 안으로 (× 8이 아님). 아랫단이 전망대 눈높이 근처 — 더 낮으면 전망대에서 온 시야가 초록으로 덮임
                    SpaceStation.Building.AuroraRibbon Arc(float radius, float az0, float az1, float y, float height, float folds, float phase)
                    {
                        const int count = 48;
                        var path = new Vector3[count + 1];
                        for (int i = 0; i <= count; i++)
                        {
                            float s = i / (float)count;
                            float a = Mathf.Deg2Rad * Mathf.Lerp(az0, az1, s);
                            float rr = radius + 0.32f * Mathf.Sin(2f * Mathf.PI * folds * s + phase);
                            path[i] = new Vector3(CX + rr * Mathf.Cos(a), y + 0.4f * Mathf.Sin(2f * Mathf.PI * 1.5f * s + phase), CZ + rr * Mathf.Sin(a));
                        }
                        return new SpaceStation.Building.AuroraRibbon(path, height, 0.3f);
                    }
                    var aurora = new GameObject("Aurora");
                    aurora.transform.SetParent(root.transform, false);
                    aurora.AddComponent<SpaceStation.Building.AuroraFx>().Configure(AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2AuroraPath),
                        new[] { Arc(4.8f, 5f, 150f, 6.8f, 3.9f, 3f, 0.7f), Arc(5.5f, 35f, 172f, 7.4f, 3.3f, 4f, 2.4f), Arc(5.5f, 182f, 238f, 7f, 3.4f, 2f, 4.1f) }, 8f);   // 가장 멀리 움직여도 하늘 벽 (등대에서 7.15m) 안: 떠돎 + 주름 + 기울기 + 뒤 겹 = 1.65m
                    // 집 레이더 상황판 (sd_builder RADAR) = 실제 운석 예보, 예보 글 = 동쪽 벽 화면
                    root.AddComponent<InteriorRadar>().Configure(new Vector3(8.95f, F + 0.958f, 1.05f), 0.56f,
                        Child(root, N + "Sweep").GetComponent<InteriorOrbit>(),
                        Child(root, N + "Beacon").GetComponent<Renderer>(),
                        new Vector3(9.905f, F + 1.6f, 1.32f), Vector3.left, Vector3.up, new Vector2(0.8f, 0.6f));
                    // 전망대 망원경 둘 (sd_builder SCOPE_AZ): 아콜로지 · 치안 본부와 같은 들여다보기, 바깥쪽을 쉴 때 방향으로 좌우 ±100°
                    var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Maplestory SDF.asset");
                    var scopes = new[] { 150f, 340f };
                    for (int k = 0; k < scopes.Length; k++)
                    {
                        var head = Child(root, $"{N}Scope{k + 1}");
                        BoxOf(head, 0.06f);
                        float a = scopes[k] * Mathf.Deg2Rad;
                        root.AddComponent<InteriorTelescope>().Configure(head, new Vector3(CX + 2.75f * Mathf.Cos(a), Balcony + 1.3f, CZ + 2.75f * Mathf.Sin(a)), 18f, 0f, font, 90f - scopes[k], 100f, startPitch: -16f, startFov: 24f, minPitch: -60f);
                    }
                });
        }

        /// <summary>
        /// 진화 2단계 정련 콤비나트 "작은 해 용광로 · 견학 제철소" (3 × 2칸, rf_builder — 사용자 2026-11-05 시안 1 "좋아 이대로 유니티로 옮겨줘"): 정원 · 전시관 구성 (직접 체험 모듈 아님).
        /// 외형 그대로: 가운데 유리 돔 (높은 단, 남쪽 계단 둘) 속 떠 있는 작은 해 · 은빛 발톱 · 홍염 고리 · 도는 쇳물 국자 셋 (<see cref="InteriorSunForge"/>, 돔 안 갤러리에서 눈앞에 봄), 돔 둘레 산책로 (우주복),
        /// 양옆 유리 볼트 격납고 둘 (실내, 관람 통로 · 채굴선 · 광석 통 · 집게 크레인 <see cref="InteriorOreCrane"/> · 북쪽 끝 에너지 막), 앞쪽 견학 광장 (황동 주괴 기념탑 · 금빛 해살).
        /// 문 자리 10 = 갑판 둘레 (깊이 3.95, 닫힘 = 난간), 아래 해치 = 격납고 통로 넷 · 광장 · 북쪽 길, 위 해치 없음 (하늘).
        /// </summary>
        private static void BuildEvoRefineryCombine()
        {
            const float DeckEdge = 3.95f, L1 = F + 1f, CX = 8f, CZ = 4.5f;
            var back = new Vector3Int(0, 0, -1);
            var front = new Vector3Int(0, 0, 1);
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x <= 2; x++)
            {
                sockets.Add(new InteriorSocket(new Vector3Int(x, 0, 0), back, DeckEdge));
                sockets.Add(new InteriorSocket(new Vector3Int(x, 0, 1), front, DeckEdge));
            }
            for (int z = 0; z <= 1; z++)
            {
                sockets.Add(new InteriorSocket(new Vector3Int(0, 0, z), Vector3Int.left, DeckEdge));
                sockets.Add(new InteriorSocket(new Vector3Int(2, 0, z), Vector3Int.right, DeckEdge));
            }
            sockets.Add(new InteriorSocket(new Vector3Int(0, 0, 0), Vector3Int.down, -F, new Vector3(1.25f, 0f, 1.3f)));    // 서쪽 격납고 통로 (문 북쪽)
            sockets.Add(new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.down, -F, new Vector3(1.25f, 0f, 0f)));
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.down, -F, new Vector3(0f, 0f, -3.1f)));      // 견학 광장 (기념탑 앞)
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 1), Vector3Int.down, -F, new Vector3(0f, 0f, 3.05f)));      // 북쪽 길
            sockets.Add(new InteriorSocket(new Vector3Int(2, 0, 0), Vector3Int.down, -F, new Vector3(-1.25f, 0f, 1.3f)));   // 동쪽 격납고 통로
            sockets.Add(new InteriorSocket(new Vector3Int(2, 0, 1), Vector3Int.down, -F, new Vector3(-1.25f, 0f, 0f)));
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.86f, 0.55f);
            var cool = new Color(0.72f, 0.82f, 1f);
            var ember = new Color(1f, 0.58f, 0.24f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(CX, L1 + 1.85f, CZ), 9f, 2.6f, new Color(1f, 0.72f, 0.42f)),   // 작은 해
                LightSpec.Point(new Vector3(CX, L1 + 0.5f, CZ), 4f, 1f, ember),                            // 주형 고리
                LightSpec.Point(new Vector3(CX, F + 2.4f, -2.7f), 4.5f, 0.9f, warm),                       // 기념탑
                LightSpec.Point(new Vector3(6.15f, F + 2.3f, -3.3f), 6f, 0.85f, warm),                     // 광장 등불
                LightSpec.Point(new Vector3(9.85f, F + 2.3f, -3.3f), 6f, 0.85f, warm),
                LightSpec.Point(new Vector3(CX, F + 2.4f, 11f), 6f, 0.6f, cool),                           // 북쪽 길
                LightSpec.Point(new Vector3(3.6f, F + 2.2f, 9.3f), 5f, 0.6f, warm),                        // 북쪽 빈터
                LightSpec.Point(new Vector3(12.4f, F + 2.2f, 9.3f), 5f, 0.6f, warm),
                LightSpec.Point(new Vector3(-3.2f, F + 2f, 4f), 7f, 0.45f, cool),                          // 바깥 길
                LightSpec.Point(new Vector3(19.2f, F + 2f, 4f), 7f, 0.45f, cool),
                LightSpec.Point(new Vector3(CX, F + 0.5f, -1.7f), 3f, 0.6f, ember),                        // 단 통풍 홈
                LightSpec.Point(new Vector3(2.8f, F + 0.5f, 1.6f), 3f, 0.6f, ember),
                LightSpec.Point(new Vector3(13.2f, F + 0.5f, 1.6f), 3f, 0.6f, ember),
                LightSpec.Point(new Vector3(2.8f, F + 0.5f, 7.4f), 3f, 0.6f, ember),
                LightSpec.Point(new Vector3(13.2f, F + 0.5f, 7.4f), 3f, 0.6f, ember),
            };
            foreach (var az in new[] { 45f, 135f, 225f, 315f })
            {
                float a = az * Mathf.Deg2Rad;
                lights.Add(LightSpec.Point(new Vector3(CX + 5.7f * Mathf.Cos(a), L1 + 2.3f, CZ + 5.7f * Mathf.Sin(a)), 5.5f, 0.8f, warm));   // 산책로 등불
            }
            foreach (var hx in new[] { -0.2f, 16.2f })
            {
                foreach (var z in new[] { 0f, 4f, 8f })
                    lights.Add(LightSpec.Point(new Vector3(hx, F + 2.6f, z), 6f, 1.25f, white));   // 격납고
            }
            EvoAccent("RefineryCombine", "#D0B090");
            // 스폰 = 견학 광장 남쪽 길에서 기념탑 · 돔을 바라봄
            Build("EvoRefineryCombine", "Evolved/MD_Evo_RefineryCombine", sockets, new Vector3(10.8f, F, -3.1f), -20f, lights,
                configure: root =>
                {
                    const string N = "INT_EvoRefineryCombine_";
                    AddOpenAir(root, new[]
                    {
                        new Vector3(CX, CZ, 4.42f),                                                                                                             // 돔
                        new Vector3(-0.2f, -0.35f, 2.25f), new Vector3(-0.2f, 1.9f, 2.25f), new Vector3(-0.2f, 4.15f, 2.25f), new Vector3(-0.2f, 6.4f, 2.25f), new Vector3(-0.2f, 8.2f, 2.25f),   // 서쪽 격납고
                        new Vector3(16.2f, -0.35f, 2.25f), new Vector3(16.2f, 1.9f, 2.25f), new Vector3(16.2f, 4.15f, 2.25f), new Vector3(16.2f, 6.4f, 2.25f), new Vector3(16.2f, 8.2f, 2.25f),   // 동쪽 격납고
                    });
                    var r = root.transform;
                    Transform Pivot(string pivotName, Transform parent, Vector3 at, string part)
                    {
                        var p = new GameObject(pivotName).transform;
                        p.SetParent(parent, false);
                        p.position = r.TransformPoint(at);
                        p.rotation = r.rotation;
                        var mesh = Child(root, part);
                        if (mesh != null)
                            mesh.SetParent(p, true);
                        return p;
                    }
                    // 작은 해 용광로: 받침 팔 (돔 축) → 국자 (기울기 축) · 쇳물 줄기 (맨 위)
                    const float LadleR = 2f, LadleY = L1 + 0.86f, StreamR = 2.38f, StreamTop = LadleY - 0.17f;
                    var ladleAz = new[] { 270f, 30f, 150f };
                    var carousel = Pivot("CarouselPivot", r, new Vector3(CX, L1 + 0.2f, CZ), N + "Carousel");
                    var ladles = new Transform[3];
                    var streams = new Transform[3];
                    for (int k = 0; k < 3; k++)
                    {
                        float a = ladleAz[k] * Mathf.Deg2Rad;
                        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        ladles[k] = Pivot("LadlePivot" + (k + 1), carousel, new Vector3(CX, LadleY, CZ) + dir * LadleR, N + "Ladle" + (k + 1));
                        streams[k] = Pivot("StreamPivot" + (k + 1), carousel, new Vector3(CX, StreamTop, CZ) + dir * StreamR, N + "Stream" + (k + 1));
                    }
                    root.AddComponent<InteriorSunForge>().Configure(carousel, ladles, streams, ladleAz, new[] { 45f, 195f, 315f },
                        StreamR, L1 + 0.11f, new Vector3(CX, L1 + 1.85f, CZ), 1.35f, new Vector3(CX, 3.78f, CZ),
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.SparkPath), ModuleFxMaterials.Get("FireworkSpark"), ModuleFxMaterials.Get("FireworkLine"),
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2SolarFlarePath));
                    // 격납고 크레인: 들보 → 줄 위 끝 · 집게 경첩 → 손가락 경첩 넷 (광석 원형은 크레인 둘이 함께 씀)
                    const float RailY = F + 2.1f, Hinge = F + 1.85f, CableTop = RailY - 0.1f, RestZ = 2.4f;
                    var rock = Child(root, N + "OreRock");
                    rock.gameObject.SetActive(false);
                    void Crane(string tag, float gx, float startDelay)
                    {
                        var gantry = Pivot("GantryPivot" + tag, r, new Vector3(gx, 0f, RestZ), N + "Gantry" + tag);
                        var cable = Pivot("CablePivot" + tag, gantry, new Vector3(gx, CableTop, RestZ), N + "Cable" + tag);
                        var claw = Pivot("ClawPivot" + tag, gantry, new Vector3(gx, Hinge, RestZ), N + "Claw" + tag);
                        var fingers = new Transform[4];
                        var axes = new Vector3[4];
                        for (int k = 0; k < 4; k++)
                        {
                            float a = Mathf.PI / 4f + Mathf.PI / 2f * k;
                            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                            fingers[k] = Pivot("FingerPivot" + tag + k, claw, new Vector3(gx, Hinge, RestZ) + d * 0.1f, N + "Finger" + tag + k);
                            axes[k] = Vector3.Cross(d, Vector3.up);   // + = 손가락 끝이 바깥으로 (벌림)
                        }
                        root.AddComponent<InteriorOreCrane>().Configure(gantry, cable, claw, fingers, axes, rock, RestZ,
                            new Vector3(gx, F + 1.34f, 5.1f), new Vector3(gx, F + 1.31f, -0.78f), F + 0.86f, Hinge, CableTop, startDelay);
                    }
                    Crane("A", -0.95f, 0f);
                    Crane("B", 16.95f, 6f);
                });
        }

        // 식량 공장 바퀴 (ff_builder 값): 축 높이 · 곤돌라 고리 반지름 · 핀 → 곤돌라 바닥
        private const float FoodWheelY = F + 7.12f, FoodPodRadius = 2.85f, FoodPodHang = 2.25f;
        private static readonly int[] FoodRiderPods = { 1, 2, 4 };

        /// <summary>식량 공장 곤돌라 벤치의 탄 주민 자리 (쉬는 자세, 템플릿 기준) — <see cref="ResidentSpotBaker"/>와 <see cref="InteriorFerrisWheel"/>가 함께 씀.</summary>
        public static Vector3[] FoodRiderSeats(float wheelX)
        {
            var seats = new Vector3[FoodRiderPods.Length];
            for (int i = 0; i < seats.Length; i++)
            {
                float a = (270f + 72f * FoodRiderPods[i]) * Mathf.Deg2Rad;
                seats[i] = new Vector3(wheelX + FoodPodRadius * Mathf.Cos(a) + 0.36f, FoodWheelY + FoodPodRadius * Mathf.Sin(a) - FoodPodHang + 0.43f, 0.6f);   // 방석 앞 끝(0.53) 바로 뒤: 무릎 · 발이 방석 앞으로 나옴
            }
            return seats;
        }

        public const float FoodWheelA = 4.96f, FoodWheelB = 27.04f;

        /// <summary>
        /// 진화 2단계 식량 공장 "유리 바퀴 관람차 공장" (5 × 1칸 + 위층, ff_builder — 사용자 2026-11-05 시안 1 "유니티로 옮겨줘"): 직접 체험 = 관람차 탑승 (<see cref="InteriorFerrisWheel"/>).
        /// 외형 × 8 그대로: 왼쪽 프로틴 바 바퀴 · 가운데 2층 유리 공장 + 지붕 배양조 셋 · 오른쪽 수확 작물 바퀴. 바퀴 안에 수평을 유지하는 곤돌라 다섯 (탄 주민 있음), 앞쪽 양 끝 계단 → 층계참 → 승강장 문 · 다리.
        /// 공장 1층 = 가공 라인 (<see cref="InteriorFoodLine"/> 벨트 물건 · 로봇 팔, 가운데 통로 = 남북 압력 막), 2층 = 시식 카페 (유리 엘리베이터 <see cref="InteriorLift"/>).
        /// 문 자리 6 = 외형 면이 있는 칸 1 · 2 · 3의 남 · 북 (깊이 3.95, 닫힘 = 난간), 아래 해치 = 칸마다, 위 해치 = 공장 지붕 칸.
        /// </summary>
        private static void BuildEvoFoodFactory()
        {
            const float DeckEdge = 3.95f, L1 = F + 5.73f, Plat = FoodWheelY - FoodPodRadius - FoodPodHang, LX = 12.3f, LZ = 1.44f;
            var back = new Vector3Int(0, 0, -1);
            var front = new Vector3Int(0, 0, 1);
            var sockets = new List<InteriorSocket>();
            for (int x = 1; x <= 3; x++)
            {
                sockets.Add(new InteriorSocket(new Vector3Int(x, 0, 0), back, DeckEdge));
                sockets.Add(new InteriorSocket(new Vector3Int(x, 0, 0), front, DeckEdge));
            }
            sockets.Add(new InteriorSocket(new Vector3Int(0, 0, 0), Vector3Int.down, -F, new Vector3(-0.4f, 0f, 2.9f)));   // 뒤쪽 길 (바퀴 밑 비킴)
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.down, -F, new Vector3(1.9f, 0f, -1.7f)));   // 바퀴와 공장 사이
            sockets.Add(new InteriorSocket(new Vector3Int(2, 0, 0), Vector3Int.down, -F, Vector3.zero));                   // 공장 가운데 통로
            sockets.Add(new InteriorSocket(new Vector3Int(3, 0, 0), Vector3Int.down, -F, new Vector3(-1.9f, 0f, -1.7f)));
            sockets.Add(new InteriorSocket(new Vector3Int(4, 0, 0), Vector3Int.down, -F, new Vector3(0.4f, 0f, 2.9f)));
            sockets.Add(new InteriorSocket(new Vector3Int(2, 1, 0), Vector3Int.up, 1.45f, new Vector3(1.85f, 0f, 0f)));    // 2층 천장 (카운터와 배양액 관 사이 통로 위)
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.86f, 0.55f);
            var cool = new Color(0.72f, 0.82f, 1f);
            var fresh = new Color(0.78f, 0.92f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(FoodWheelA, FoodWheelY, 0f), 8.5f, 1.4f, warm),     // 바퀴 축 (배양액 · 신선 등)
                LightSpec.Point(new Vector3(FoodWheelB, FoodWheelY, 0f), 8.5f, 1.4f, fresh),
                LightSpec.Point(new Vector3(FoodWheelA, FoodWheelY - 3.6f, 0f), 6.5f, 1.1f, white),   // 아래쪽 칸 · 승강장
                LightSpec.Point(new Vector3(FoodWheelB, FoodWheelY - 3.6f, 0f), 6.5f, 1.1f, white),
                LightSpec.Point(new Vector3(FoodWheelA, FoodWheelY + 3.4f, 0f), 6.5f, 1f, white),     // 위쪽 칸
                LightSpec.Point(new Vector3(FoodWheelB, FoodWheelY + 3.4f, 0f), 6.5f, 1f, white),
                LightSpec.Point(new Vector3(FoodWheelA - 2.6f, F + 3.6f, -3.2f), 5.5f, 1f, warm),     // 계단
                LightSpec.Point(new Vector3(FoodWheelB + 2.6f, F + 3.6f, -3.2f), 5.5f, 1f, warm),
                LightSpec.Point(new Vector3(13f, F + 4.6f, -1.2f), 7f, 1.4f, white),                  // 공장 1층
                LightSpec.Point(new Vector3(19f, F + 4.6f, -1.2f), 7f, 1.4f, white),
                LightSpec.Point(new Vector3(13.6f, F + 4.6f, 1.3f), 7f, 1.2f, white),
                LightSpec.Point(new Vector3(18.6f, F + 4.6f, 1.3f), 7f, 1.2f, white),
                LightSpec.Point(new Vector3(16f, F + 2.8f, 0f), 5f, 0.9f, white),
                LightSpec.Point(new Vector3(13.4f, L1 + 3.6f, 0f), 7f, 1.3f, white),                  // 카페
                LightSpec.Point(new Vector3(18.6f, L1 + 3.6f, 0f), 7f, 1.3f, white),
                LightSpec.Point(new Vector3(16f, L1 + 2.4f, -1.4f), 4.5f, 0.8f, warm),
                LightSpec.Point(new Vector3(16f, L1 + 2.4f, 1.4f), 4.5f, 0.8f, warm),
                LightSpec.Point(new Vector3(-2.05f, F + 1.6f, 0f), 3.5f, 0.5f, new Color(0.8f, 0.5f, 1f)),   // 화단 (플라즈마 볼 꽃)
                LightSpec.Point(new Vector3(34.05f, F + 1.6f, 0f), 3.5f, 0.5f, new Color(0.8f, 0.5f, 1f)),
            };
            foreach (var (x, z) in new[] { (11f, -3.1f), (21f, -3.1f), (11f, 3.1f), (21f, 3.1f), (1f, 3.1f), (31f, 3.1f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 2.3f, z), 6f, 0.85f, warm));   // 갑판 가로등
            foreach (var x in new[] { -2.6f, 34.6f })
                lights.Add(LightSpec.Point(new Vector3(x, F + 3.2f, 0f), 7f, 0.45f, cool));  // 양 끝 마당 (밤하늘 찬 빛)
            EvoAccent("FoodFactory", "#A8E880");
            // 스폰 = 공장과 왼쪽 바퀴 사이 앞쪽 길에서 바퀴 · 계단을 바라봄
            Build("EvoFoodFactory", "Evolved/MD_Evo_FoodFactory", sockets, new Vector3(9.8f, F, -3.2f), -59f, lights,
                configure: root =>
                {
                    AddOpenAir(root, new[] { new Vector3(12.9f, 0f, 2.7f), new Vector3(16f, 0f, 2.75f), new Vector3(19.1f, 0f, 2.7f) });   // 공장
                    var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Maplestory SDF.asset");
                    void Wheel(string tag, float cx, float startDelay, string title)
                    {
                        var hub = new GameObject($"Wheel{tag}_Pivot").transform;
                        hub.SetParent(root.transform, false);
                        hub.localPosition = new Vector3(cx, FoodWheelY, 0f);
                        Child(root, $"INT_EvoFoodFactory_Wheel{tag}").SetParent(hub, true);
                        Child(root, $"INT_EvoFoodFactory_Wheel{tag}Glass").SetParent(hub, true);
                        var pods = new Transform[5];
                        var doors = new Transform[5];
                        var seats = new Transform[5];
                        for (int k = 0; k < 5; k++)
                        {
                            float a = (270f + 72f * k) * Mathf.Deg2Rad;
                            var pivot = new GameObject($"Pod{tag}{k + 1}_Pivot").transform;
                            pivot.SetParent(root.transform, false);
                            pivot.localPosition = new Vector3(cx + FoodPodRadius * Mathf.Cos(a), FoodWheelY + FoodPodRadius * Mathf.Sin(a), 0f);
                            pods[k] = pivot;
                            foreach (var suffix in new[] { "", "Glass" })
                            {
                                var part = Child(root, $"INT_EvoFoodFactory_Pod{tag}{k + 1}{suffix}");
                                part.SetParent(pivot, true);
                                part.gameObject.AddComponent<MeshCollider>().sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
                            }
                            doors[k] = Child(root, $"INT_EvoFoodFactory_Pod{tag}{k + 1}Door");
                            doors[k].SetParent(pivot, true);
                            BoxOf(doors[k], 0f);
                            var seat = new GameObject("Seat").transform;
                            seat.SetParent(pivot, false);
                            seat.localPosition = new Vector3(0f, -FoodPodHang + 0.24f, 0.69f);
                            seat.gameObject.AddComponent<BoxCollider>().size = new Vector3(1.44f, 0.5f, 0.34f);
                            seats[k] = seat;
                        }
                        // 다리 (윗면만 밟음) · 승강장 문 · 다리 옆 보이지 않는 벽 · 바퀴 밑 받침 위 보이지 않는 벽 (도는 테는 충돌체 없음)
                        var bridge = Child(root, $"INT_EvoFoodFactory_Bridge{tag}");
                        var plate = bridge.gameObject.AddComponent<BoxCollider>();
                        plate.center = new Vector3(cx, Plat - 0.0775f, -1.775f);
                        plate.size = new Vector3(1.2f, 0.065f, 1.55f);
                        var gateL = Child(root, $"INT_EvoFoodFactory_Gate{tag}L");
                        var gateR = Child(root, $"INT_EvoFoodFactory_Gate{tag}R");
                        BoxOf(gateL, 0f);
                        BoxOf(gateR, 0f);
                        var walls = new GameObject($"Wheel{tag}_Walls");
                        walls.transform.SetParent(root.transform, false);
                        foreach (var s in new[] { -1f, 1f })
                        {
                            var wall = walls.AddComponent<BoxCollider>();
                            wall.center = new Vector3(cx + s * 0.64f, Plat + 1f, -1.76f);
                            wall.size = new Vector3(0.04f, 2.2f, 1.52f);
                        }
                        var under = walls.AddComponent<BoxCollider>();
                        under.center = new Vector3(cx, F + 0.95f, 0f);   // 윗면이 다리 판보다 낮게 (곤돌라로 들어가는 발 높이가 뜨지 않게)
                        under.size = new Vector3(6.7f, 1.9f, 5.04f);
                        root.AddComponent<InteriorFerrisWheel>().Configure(hub, pods, doors, seats, bridge, gateL, gateR, font, title,
                            new Vector3(cx, Plat + 2.46f, -2.6f), FoodPodHang, startDelay, FoodRiderSeats(cx), FoodRiderPods);
                    }
                    Wheel("A", FoodWheelA, 0f, "프로틴 바 관람차");
                    Wheel("B", FoodWheelB, 6f, "수확 관람차");
                    // 가공 라인: 벨트 넷 (바깥 → 안 → 포장기) · 로봇 팔 (어깨 피벗)
                    var arms = new Transform[4];
                    var shoulders = new[] { new Vector2(11.95f, -0.78f), new Vector2(13.85f, -0.78f), new Vector2(18.15f, -0.78f), new Vector2(20.05f, -0.78f) };
                    for (int i = 0; i < arms.Length; i++)
                    {
                        var pivot = new GameObject($"Arm{i + 1}_Pivot").transform;
                        pivot.SetParent(root.transform, false);
                        pivot.localPosition = new Vector3(shoulders[i].x, F + 0.78f, shoulders[i].y);
                        Child(root, $"INT_EvoFoodFactory_Arm{i + 1}").SetParent(pivot, true);
                        arms[i] = pivot;
                    }
                    const float OutY = F + 1.09f, InY = F + 0.88f;
                    root.AddComponent<InteriorFoodLine>().Configure(
                        new[] { Child(root, "INT_EvoFoodFactory_ItemBar"), Child(root, "INT_EvoFoodFactory_ItemCrate") },
                        new[] { new Vector3(8.4f, OutY, 0f), new Vector3(11.3f, InY, 0f), new Vector3(23.6f, OutY, 0f), new Vector3(20.7f, InY, 0f) },
                        new[] { new Vector3(11.2f, OutY, 0f), new Vector3(14.25f, InY, 0f), new Vector3(20.8f, OutY, 0f), new Vector3(17.75f, InY, 0f) },
                        new[] { 0, 0, 1, 1 }, arms);
                    // 유리 엘리베이터 (1층 ↔ 2층 카페, 문 = 동쪽)
                    var car = Child(root, "INT_EvoFoodFactory_LiftCar");
                    var carGlass = Child(root, "INT_EvoFoodFactory_LiftCarGlass");
                    carGlass.SetParent(car, true);
                    foreach (var t in new[] { car, carGlass })
                        t.gameObject.AddComponent<MeshCollider>().sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                    var lift = root.AddComponent<InteriorLift>();
                    var left = new Transform[2];
                    var right = new Transform[2];
                    for (int k = 0; k < 2; k++)
                    {
                        left[k] = Child(root, $"INT_EvoFoodFactory_LiftDoor{k + 1}L");
                        right[k] = Child(root, $"INT_EvoFoodFactory_LiftDoor{k + 1}R");
                        BoxOf(left[k], 0f);
                        BoxOf(right[k], 0f);
                        BoxOf(Child(root, $"INT_EvoFoodFactory_LiftCall{k + 1}"), 0.06f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, true);
                        var button = Child(root, $"INT_EvoFoodFactory_LiftBtn{k + 1}");
                        button.SetParent(car, true);
                        BoxOf(button, 0.03f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, false);
                    }
                    lift.Configure(car, new[] { 0f, L1 - F }, left, right, new Vector3(LX, F + 0.085f, LZ), Vector3.forward, 0.75f);
                    var liftSo = new SerializedObject(lift);
                    liftSo.FindProperty("_doorTravel").floatValue = 0.44f;   // 문짝이 공장 벽 · 벨트에 닿지 않게 (ff_builder)
                    liftSo.ApplyModifiedPropertiesWithoutUndo();
                });
        }

        /// <summary>
        /// 진화 2단계 바이오돔 "비눗방울 돔 정원" (4 × 2칸, bd_builder — 사용자 2026-11-03 시안 1 "좋아 이대로 유니티로 옮겨줘"): 정원 · 전시관 구성 (직접 체험 모듈 아님).
        /// 외형 평면 그대로: 미드나잇 갑판 (우주복, 기술 식물만) 위에 따로 선 지오데식 유리 돔 셋 + 유리 연결 통로 둘 (돔 안 = 실내, 살아 있는 식물).
        /// 큰 돔 "구름 숲" = 바위 폭포 → 웅덩이 → 호수 · 디딤돌 · 나무 아홉 (<see cref="PlantKind.Tree"/>) · 도는 구름 고리 여섯 Clouds + 비 (<see cref="SpaceStation.Building.RainFx"/>, 바깥 모듈과 같은 효과),
        /// 왼쪽 돔 "바퀴 텃밭" = 분수 · 크림색 고리 + 십자 길 · 여덟 칸 작물 · 매단 재배등 고리, 오른쪽 돔 "산소 습지" = 연못 · 갈대 · 수련 · 잉어 Fish · 전망 데크 · 산소 기둥 셋 (거품 Bubbles1 ~ 3).
        /// 풀 = 잔디 무늬 텍스처 Lawn (충돌 없음, 발은 그 아래 갑판) + 풀잎 · 고사리 · 들꽃 한 장씩 Leaves. 호수 · 연못에 못 들어가는 벽 = LakeHit (렌더러 없음).
        /// 문 자리 12 = 갑판 둘레 (깊이 3.95, 닫힘 = 난간), 아래 해치 = 칸마다 갑판으로, 위 해치 없음 (하늘).
        /// </summary>
        private static void BuildEvoBiodome()
        {
            const float DeckEdge = 3.95f;
            var back = new Vector3Int(0, 0, -1);
            var front = new Vector3Int(0, 0, 1);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(new Vector3Int(0, 0, 0), Vector3Int.left, DeckEdge),
                new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.left, DeckEdge),
                new InteriorSocket(new Vector3Int(3, 0, 0), Vector3Int.right, DeckEdge),
                new InteriorSocket(new Vector3Int(3, 0, 1), Vector3Int.right, DeckEdge),
            };
            for (int x = 0; x < 4; x++)
            {
                sockets.Add(new InteriorSocket(new Vector3Int(x, 0, 0), back, DeckEdge));
                sockets.Add(new InteriorSocket(new Vector3Int(x, 0, 1), front, DeckEdge));
            }
            for (int x = 0; x < 4; x++)
            {
                // 돔을 비켜 남 · 북 갑판으로 (양 끝 칸은 가로등도 비킴)
                float side = x == 0 ? 2.2f : x == 1 ? -2.6f : x == 2 ? 2.6f : -2.2f;
                float depth = x == 0 || x == 3 ? 2f : 2.4f;
                sockets.Add(new InteriorSocket(new Vector3Int(x, 0, 0), Vector3Int.down, -F, new Vector3(side, 0f, -depth)));
                sockets.Add(new InteriorSocket(new Vector3Int(x, 0, 1), Vector3Int.down, -F, new Vector3(side, 0f, depth)));
            }
            var sun = new Color(1f, 0.98f, 0.92f);
            var warm = new Color(1f, 0.86f, 0.55f);
            var cool = new Color(0.72f, 0.82f, 1f);
            var water = new Color(0.6f, 0.9f, 1f);
            var plasma = new Color(0.8f, 0.5f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(12f, F + 4f, 4f), 10f, 2f, sun),             // 큰 돔 (구름 고리 가운데)
                LightSpec.Point(new Vector3(9.2f, F + 3.2f, 3f), 7f, 1.3f, sun),         // 호수
                LightSpec.Point(new Vector3(9.9f, F + 2.8f, 5.4f), 5f, 1f, sun),         // 폭포
                LightSpec.Point(new Vector3(14.6f, F + 3f, 2.2f), 7f, 1.4f, sun),        // 동쪽 숲
                LightSpec.Point(new Vector3(14.6f, F + 3f, 6f), 7f, 1.4f, sun),
                LightSpec.Point(new Vector3(12f, F + 2.6f, -0.2f), 5f, 0.8f, sun),       // 남 · 북 입구 안쪽
                LightSpec.Point(new Vector3(12f, F + 2.6f, 8.2f), 5f, 0.8f, sun),
                LightSpec.Point(new Vector3(1.5f, F + 2.3f, 4f), 6.5f, 1.3f, sun),       // 텃밭 재배등 고리 아래
                LightSpec.Point(new Vector3(-0.6f, F + 2.2f, 4f), 4.5f, 0.7f, warm),
                LightSpec.Point(new Vector3(3.6f, F + 2.2f, 4f), 4.5f, 0.7f, warm),
                LightSpec.Point(new Vector3(1.5f, F + 2.2f, 1.9f), 4.5f, 0.7f, warm),
                LightSpec.Point(new Vector3(1.5f, F + 2.2f, 6.1f), 4.5f, 0.7f, warm),
                LightSpec.Point(new Vector3(22.5f, F + 3f, 4f), 8f, 1.7f, sun),          // 습지
                LightSpec.Point(new Vector3(24.6f, F + 2.2f, 2.6f), 4.5f, 0.7f, sun),
                LightSpec.Point(new Vector3(24.6f, F + 2.2f, 5.6f), 4.5f, 0.7f, sun),
                LightSpec.Point(new Vector3(23.5f, F + 1.8f, 4.2f), 3.5f, 0.5f, water),  // 연못
                LightSpec.Point(new Vector3(20.3f, F + 1.2f, 5.95f), 3f, 0.5f, water),   // 산소 기둥
                LightSpec.Point(new Vector3(20.2f, F + 1.2f, 2.5f), 3f, 0.5f, water),
                LightSpec.Point(new Vector3(6f, F + 1.9f, 4f), 3.5f, 0.7f, sun),         // 연결 통로
                LightSpec.Point(new Vector3(18f, F + 1.9f, 4f), 3.5f, 0.7f, sun),
                LightSpec.Point(new Vector3(6.05f, F + 1.6f, 0f), 3.5f, 0.5f, plasma),   // 화단 (플라즈마 볼 꽃)
                LightSpec.Point(new Vector3(17.95f, F + 1.6f, 0f), 3.5f, 0.5f, plasma),
                LightSpec.Point(new Vector3(6.05f, F + 1.6f, 8f), 3.5f, 0.5f, plasma),
                LightSpec.Point(new Vector3(17.95f, F + 1.6f, 8f), 3.5f, 0.5f, plasma),
            };
            foreach (var (x, z) in new[] { (3.6f, -3.1f), (12f, -3.1f), (20.4f, -3.1f), (3.6f, 11.1f), (12f, 11.1f), (20.4f, 11.1f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 2.3f, z), 6.5f, 0.85f, warm));   // 갑판 가로등
            foreach (var (x, z) in new[] { (-2.6f, -1.2f), (26.6f, -1.2f), (-2.6f, 9.2f), (26.6f, 9.2f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 3.2f, z), 7f, 0.45f, cool));     // 빈 갑판 (밤하늘 찬 빛, 세면 윤나는 갑판에 하얗게 비침)
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoBiodome_Clouds", new Vector3(12f, 0f, 4f), Vector3.up, 6f),        // 구름 고리 (bd_builder clouds)
                new OrbitSpec("INT_EvoBiodome_Fish", new Vector3(22.85f, 0f, 4.2f), Vector3.up, -18f),   // 잉어 (연못 가운데 축, bd_builder fish — 머리 쪽으로 헤엄침)
                new OrbitSpec("INT_EvoBiodome_Bubbles1", new Vector3(20.95f, 0f, 6.55f), Vector3.up, 90f),   // 산소 기둥 거품 — 나선을 돌리면 오르는 것처럼 보임
                new OrbitSpec("INT_EvoBiodome_Bubbles2", new Vector3(19.85f, 0f, 5.45f), Vector3.up, 90f),
                new OrbitSpec("INT_EvoBiodome_Bubbles3", new Vector3(20.2f, 0f, 2.5f), Vector3.up, 90f),
            };
            EvoAccent("Biodome", "#5FE08A");
            // 스폰 = 큰 돔 남쪽 입구 안에서 호수 · 바위 폭포를 바라봄
            Build("EvoBiodome", "Evolved/MD_Evo_Biodome", sockets, new Vector3(12.1f, F, -0.2f), -20f, lights, orbits,
                configure: root =>
                {
                    AddOpenAir(root, new[]
                    {
                        new Vector3(1.5f, 4f, 3.88f), new Vector3(12f, 4f, 5.26f), new Vector3(22.5f, 4f, 3.88f),   // 돔 (bd_builder DL · DC · DR, 압력 막 반지름)
                        new Vector3(6f, 4f, 1.25f), new Vector3(18f, 4f, 1.25f),                                    // 연결 통로
                    });
                    // 물에 못 들어가는 벽: 모양만 쓰고 보이지 않음
                    var lakeHit = Child(root, "INT_EvoBiodome_LakeHit");
                    var lakeMesh = lakeHit.GetComponent<MeshFilter>().sharedMesh;
                    Object.DestroyImmediate(lakeHit.GetComponent<MeshRenderer>());
                    Object.DestroyImmediate(lakeHit.GetComponent<MeshFilter>());
                    lakeHit.gameObject.AddComponent<MeshCollider>().sharedMesh = lakeMesh;
                    // 나무 줄기 충돌체 (PlantSpot 나무 모델에는 충돌체가 없음, 잔디밭 한가운데라 화분 턱도 없음 — InteriorPlantSpots와 같은 자리)
                    foreach (var (x, z, s) in BiodomeTrees)
                    {
                        var trunk = new GameObject("TreeHit");
                        trunk.transform.SetParent(root.transform, false);
                        trunk.transform.localPosition = new Vector3(x, F + 1f, z);
                        var capsule = trunk.AddComponent<CapsuleCollider>();
                        capsule.radius = 0.1f * s;
                        capsule.height = 2f;
                    }
                    // 비: 바깥 모듈과 같은 RainFx를 미터 단위로 (구름 고리와 함께 돌고, 비는 곧게 떨어짐)
                    var clouds = Child(root, "INT_EvoBiodome_Clouds");
                    var rain = root.AddComponent<SpaceStation.Building.RainFx>();
                    var so = new SerializedObject(rain);
                    so.FindProperty("_clouds").objectReferenceValue = clouds;
                    so.FindProperty("_module").objectReferenceValue = root.transform;
                    var spots = so.FindProperty("_cloudSpots");
                    spots.arraySize = 6;
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * 60f * Mathf.Deg2Rad;
                        spots.GetArrayElementAtIndex(i).vector3Value = new Vector3(12f + 2.3f * Mathf.Cos(a), F + 3.17f, 4f + 2.3f * Mathf.Sin(a));
                    }
                    so.FindProperty("_ringCenter").vector3Value = new Vector3(12f, 0f, 4f);
                    so.FindProperty("_area").vector2Value = new Vector2(1.3f, 0.7f);
                    so.FindProperty("_dropMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/FX/M_RainDrop.mat");
                    so.FindProperty("_mistMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/FX/M_RainMist.mat");
                    so.FindProperty("_fallSpeed").floatValue = 5.5f;
                    so.FindProperty("_groundY").floatValue = F - 0.07f;
                    so.FindProperty("_rate").floatValue = 110f;
                    so.FindProperty("_shelterHalfWidth").floatValue = 0f;   // 아치 없음
                    so.FindProperty("_dropSize").vector2Value = new Vector2(0.005f, 0.008f);
                    so.FindProperty("_dropStretch").floatValue = 0.05f;
                    so.FindProperty("_sway").floatValue = 0.1f;
                    so.FindProperty("_mistSize").vector2Value = new Vector2(1.2f, 1.9f);
                    so.FindProperty("_mistFall").vector2Value = new Vector2(0.5f, 0.8f);
                    so.FindProperty("_alwaysOn").boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                });
        }

        /// <summary>바이오돔 큰 돔 나무 (x, z, 크기) — bd_builder PLANTS. <see cref="InteriorPlantSpots"/>와 줄기 충돌체가 함께 씀.</summary>
        public static readonly (float x, float z, float s)[] BiodomeTrees =
        {
            (14.05f, 6.75f, 1.5f), (15.1f, 5.45f, 1.45f), (13.95f, 5.25f, 1.3f), (15.25f, 2.75f, 1.5f), (14f, 2.45f, 1.6f),
            (14.15f, 1f, 1.35f), (9.3f, 1.3f, 1.25f), (8.3f, 2.15f, 1.1f), (11.05f, 7.7f, 1.2f),
        };

        /// <summary>
        /// 진화 2단계 치안 본부 "푸른 등 파출소 광장" (2 × 2칸, sh_builder — 사용자 2026-11-03 시안 1 "좋아 이대로 유니티로 옮겨줘"): 직접 체험 = 감시탑 전망실 (엘리베이터 + 망원경 둘, <see cref="InteriorLift"/> · <see cref="InteriorTelescope"/>) · 광장에서 출동하는 순찰차 (<see cref="InteriorPatrolCar"/>).
        /// 외형 평면 그대로: 왼쪽 아래 칸 = 천장 없는 광장 (우주복, 돌바닥 · 벤치 · 가로등 · 머리 위 순찰 드론 고리 Orbit1 · 떠 있는 순찰차 Car1 · 2 · 번호표 발권기), ㄱ자 청사 = 나머지 세 칸 (유리 벽 + 박공 현관, 깎은 안쪽 모서리에 금빛 아치 + 압력 막 Curtain).
        /// 민원 창구 셋은 광장 쪽 벽 (바깥 턱 · 번호판, 유리 너머 직원) — 번호표 받은 광장 벤치 주민이 창구 앞으로 (<see cref="InteriorQueueBoard"/>, 틀 0° · 민원인 구역 = 광장),
        /// 청사 안 = 동쪽 전시장 (도는 판 위 순찰차 Show · 진열장 셋) · 북쪽 둥근 유리 상황실 (실제 운석 예보 <see cref="InteriorRadar"/> Sweep · Beacon) · 로비 (감시탑 몸통 · 안내 데스크 + 실제 정거장 지도 <see cref="InteriorStationMap"/>) · 지붕 착륙장 천창 + 드론 PadDrone1 · 2.
        /// 문 자리 8 = 광장 둘 (깊이 3.95, 닫힘 = 난간) · 청사 여섯 (깊이 3.79, 닫힘 = 막힌 창 패널 <see cref="InteriorSocket.SealedWall"/>). 아래 해치 = 칸마다 넷, 위 해치 = (1, 0) · (0, 1) — 광장은 하늘, (1, 1)은 감시탑.
        /// </summary>
        private static void BuildEvoSecurityHQ()
        {
            const float Ceil = F + 3.85f, DeckEdge = 3.95f, Wall = 3.79f, TowerFloor = F + 2.95f, TowerCeil = 3.78f, TX = 8.6f, TZ = 8.6f;
            var back = new Vector3Int(0, 0, -1);
            var front = new Vector3Int(0, 0, 1);
            var c00 = new Vector3Int(0, 0, 0);
            var c10 = new Vector3Int(1, 0, 0);
            var c01 = new Vector3Int(0, 0, 1);
            var c11 = new Vector3Int(1, 0, 1);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(c00, Vector3Int.left, DeckEdge),
                new InteriorSocket(c00, back, DeckEdge),
                new InteriorSocket(c10, back, Wall, default, true),
                new InteriorSocket(c10, Vector3Int.right, Wall, default, true),
                new InteriorSocket(c01, Vector3Int.left, Wall, default, true),
                new InteriorSocket(c01, front, Wall, default, true),
                new InteriorSocket(c11, front, Wall, default, true),
                new InteriorSocket(c11, Vector3Int.right, Wall, default, true),
                new InteriorSocket(c00, Vector3Int.down, -F, new Vector3(-1.9f, 0f, 2.0f)),   // 광장 휘장 · 드론 고리 아래 비킴
                new InteriorSocket(c10, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(c01, Vector3Int.down, -F, new Vector3(-2.1f, 0f, 0f)),     // 상황실 서쪽
                new InteriorSocket(c11, Vector3Int.down, -F, new Vector3(-2.1f, 0f, 0.6f)),   // 감시탑 서쪽 로비
                new InteriorSocket(c10, Vector3Int.up, Ceil, new Vector3(0.5f, 0f, 2.6f)),    // 천창 비킴
                new InteriorSocket(c01, Vector3Int.up, Ceil, new Vector3(-2.1f, 0f, 0.6f)),
            };
            var hall = Color.white;   // 청사 = 맑은 흰빛 (누런 등이면 흰 벽이 갈색으로 보임)
            var cool = new Color(0.72f, 0.82f, 1f);
            var lights = new List<LightSpec>();
            foreach (var (x, z, range, power) in new[]
            {
                (7.6f, -1.8f, 7.5f, 1.7f), (10.2f, -1f, 7.5f, 1.6f), (7.8f, 2.9f, 7.5f, 1.6f), (10.3f, 3f, 7f, 1.5f),      // 동쪽 전시장
                (5.4f, -0.9f, 5.5f, 1.2f), (5.4f, 1.4f, 5.5f, 1.2f),                                                     // 창구 직원 칸
                (5.6f, 6.4f, 7f, 1.6f), (10.4f, 6.6f, 6f, 1.2f), (6.8f, 10f, 6f, 1.2f), (10.2f, 10.2f, 6f, 1.2f),         // 로비
                (-2.2f, 6.6f, 6f, 1.5f), (1f, 5.7f, 6f, 1.4f), (3.6f, 8f, 6f, 1.5f), (-2.2f, 10f, 6f, 1.3f), (0.8f, 10.6f, 6f, 1.3f), (3.8f, 10.4f, 6f, 1.2f),   // 북쪽 칸 · 안내 데스크
            })
                lights.Add(LightSpec.Point(new Vector3(x, Ceil - 0.5f, z), range, power, hall));
            lights.Add(LightSpec.Point(new Vector3(0.6f, F + 2.4f, 8.05f), 3f, 0.7f, cool));                   // 둥근 상황실
            lights.Add(LightSpec.Point(new Vector3(TX, TowerCeil - 0.5f, TZ), 5.5f, 1.5f, hall));                // 전망실
            foreach (var (x, z) in new[] { (-3.2f, 3.95f), (-3.45f, -1.75f), (4.2f, -3.35f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 2.35f, z), 5f, 0.85f, cool));                    // 광장 가로등
            lights.Add(LightSpec.Point(new Vector3(-1.2f, F + 3f, 1.6f), 8f, 0.9f, hall));                     // 광장
            lights.Add(LightSpec.Point(new Vector3(1.8f, F + 3f, -1.2f), 8f, 0.9f, hall));
            lights.Add(LightSpec.Point(new Vector3(2.6f, F + 2.2f, 2.85f), 4f, 0.8f, new Color(0.5f, 0.65f, 1f)));   // 현관 푸른 등 밑
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoSecurityHQ_Orbit1", new Vector3(0.4f, 0f, 0.6f), Vector3.up, 18f),       // 광장 순찰 드론 고리
                new OrbitSpec("INT_EvoSecurityHQ_PadDrone1", new Vector3(-0.4f, 0f, 8.6f), Vector3.up, 25f),   // 지붕 착륙장
                new OrbitSpec("INT_EvoSecurityHQ_PadDrone2", new Vector3(8.6f, 0f, -0.25f), Vector3.up, -20f),
                new OrbitSpec("INT_EvoSecurityHQ_Show", new Vector3(9.95f, 0f, -1.95f), Vector3.up, 12f),      // 전시대
                new OrbitSpec("INT_EvoSecurityHQ_Cam", new Vector3(TX, 0f, TZ + 2f), Vector3.up, 30f),              // 감시탑 카메라
                new OrbitSpec("INT_EvoSecurityHQ_Sweep", new Vector3(0.6f, 0f, 8.05f), Vector3.up, 60f),       // 상황판 쓸기 빛
            };
            EvoAccent("SecurityHQ", "#A0B8FF");
            // 스폰 = 서쪽 문 안 광장에서 현관을 바라봄
            Build("EvoSecurityHQ", "Evolved/MD_Evo_SecurityHQ", sockets, new Vector3(-2.6f, F, -0.1f), 56f, lights, orbits,
                configure: root =>
                {
                    AddOpenAir(root, new[]
                    {
                        new Vector3(7f, -1.8f, 2.35f), new Vector3(7f, 2f, 2.35f), new Vector3(9.8f, -1.5f, 2.6f), new Vector3(9.8f, 2.6f, 2.6f),   // 동쪽 칸
                        new Vector3(6f, 4.5f, 1.4f),
                        new Vector3(-1.6f, 7.4f, 2.5f), new Vector3(2f, 7.4f, 2.5f), new Vector3(-1.6f, 10f, 2.3f), new Vector3(2f, 10f, 2.3f),     // 북쪽 칸
                        new Vector3(4.2f, 6.4f, 1.6f),
                        new Vector3(6.9f, 7.1f, 2.2f), new Vector3(9.5f, 9.5f, 2.6f), new Vector3(6.5f, 10f, 2f), new Vector3(10f, 6.5f, 1.9f),     // 로비
                    });
                    // 전시대 유리 캐노피는 판과 함께 돎, 충돌체 = 판 전체 원기둥 (도는 차를 지나가지 못함)
                    var showHit = new GameObject("ShowHit");
                    showHit.transform.SetParent(root.transform, false);
                    showHit.transform.localPosition = new Vector3(9.95f, F + 1f, -1.95f);
                    var showCapsule = showHit.AddComponent<CapsuleCollider>();
                    showCapsule.radius = 1.1f;
                    showCapsule.height = 2f;
                    Child(root, "INT_EvoSecurityHQ_ShowGlass").SetParent(Child(root, "INT_EvoSecurityHQ_Show"), true);
                    // 떠 있는 순찰차 둘: 차 바닥 가운데 피벗 아래로 (앞 = +x), 평소 충돌체 = 차 경계 상자 (날 때는 꺼짐)
                    var plume = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Synthesis/M_Synth_Particle.mat");
                    var thrusters = new[] { new Vector3(0.88f, 0.27f, 0.9f), new Vector3(0.88f, 0.27f, -0.9f), new Vector3(-0.88f, 0.27f, 0.9f), new Vector3(-0.88f, 0.27f, -0.9f) };
                    foreach (var (part, at, heading, distance) in new[]
                    {
                        ("Car1", new Vector3(-1.85f, F + 0.36f, -2.25f), Vector3.left, 1.9f),
                        ("Car2", new Vector3(2.1f, F + 0.36f, -2.35f), Vector3.back, 1.5f),
                    })
                    {
                        var pivot = new GameObject(part + "Pivot").transform;
                        pivot.SetParent(root.transform, false);
                        pivot.localPosition = at;
                        var body = Child(root, $"INT_EvoSecurityHQ_{part}");
                        body.SetParent(pivot, true);
                        Child(root, $"INT_EvoSecurityHQ_{part}Glass").SetParent(pivot, true);
                        BoxOf(body, 0f);
                        root.AddComponent<InteriorPatrolCar>().Configure(pivot, thrusters, new Vector3(-0.05f, 1.18f, -0.18f), new Vector3(-0.05f, 1.18f, 0.18f), new Vector3(0.2f, 0.14f, 0.34f), plume, heading, distance);
                    }
                    // 민원 창구 셋 (광장 쪽 벽 x 4.6, sh_builder WINS · counters_outside): 번호판 = 창 위, 직원 = 유리 안, 민원인 = 바깥 턱 앞에서 동쪽을 봄
                    InteriorQueueBoard.Window Window(float z) => new InteriorQueueBoard.Window
                    {
                        Board = new InteriorQueueBoard.Panel { Center = new Vector3(4.579f, F + 3.14f, z), Normal = Vector3.left, Up = Vector3.up, Width = 0.7f, Height = 0.16f },
                        StaffSeat = new Vector3(5.94f, F + 0.49f, z),
                        Counter = new Vector4(3.85f, F, z, 90f),
                    };
                    var queue = root.AddComponent<InteriorQueueBoard>();
                    queue.Configure(new[] { Window(-0.9f), Window(0.6f), Window(2.1f) },
                        new InteriorQueueBoard.Panel { Center = new Vector3(1.1f, F + 1.19f, 4.281f), Normal = Vector3.back, Up = Vector3.up, Width = 0.32f, Height = 0.18f },
                        new Vector4(1.1f, F, 3.82f, 0f), 2.9f, 3.7f, 3.4f, new Vector3(1.1f, F, 3.4f));
                    queue.ConfigureLanes(new[]
                    {
                        new Vector4(-2.75f, 2.9f, 3.7f, 2.9f), new Vector4(-2.75f, 3.4f, 1.1f, 3.4f), new Vector4(1.1f, 3.4f, 1.1f, 3.82f),
                        new Vector4(3.7f, 2.9f, 3.7f, -0.9f), new Vector4(-2.7f, 2.1f, -2.7f, 3.4f),
                    }, 0.35f);
                    queue.ConfigureVisitorArea(new Rect(-3.95f, -3.95f, 8.55f, 8.8f));   // 광장 벤치 주민만 민원인 — 청사 안 주민은 그대로 돌아다님
                    // 둥근 상황실 레이더 (sh_builder RC) = 실제 운석 예보, 예보 글 = 뒤쪽 상황판 (앞면 z = RC + 1.405)
                    root.AddComponent<InteriorRadar>().Configure(new Vector3(0.6f, F + 0.958f, 8.05f), 0.78f,
                        Child(root, "INT_EvoSecurityHQ_Sweep").GetComponent<InteriorOrbit>(),
                        Child(root, "INT_EvoSecurityHQ_Beacon").GetComponent<Renderer>(),
                        new Vector3(0.6f, F + 1.55f, 9.454f), Vector3.back, Vector3.up, new Vector2(0.7f, 0.5f));
                    // 안내 데스크 뒤 벽 실제 정거장 지도
                    root.AddComponent<InteriorStationMap>().Configure(new Vector3(4f, F + 1.85f, 11.399f), Vector3.back, Vector3.up, new Vector2(2.24f, 1.14f),
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ScreenPath),
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ScreenGridPath));
                    // 감시탑 엘리베이터 (로비 ↔ 전망실)
                    var car = Child(root, "INT_EvoSecurityHQ_LiftCar");
                    var carGlass = Child(root, "INT_EvoSecurityHQ_LiftCarGlass");
                    carGlass.SetParent(car, true);
                    foreach (var t in new[] { car, carGlass })
                        t.gameObject.AddComponent<MeshCollider>().sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                    var lift = root.AddComponent<InteriorLift>();
                    var left = new Transform[2];
                    var right = new Transform[2];
                    for (int k = 0; k < 2; k++)
                    {
                        left[k] = Child(root, $"INT_EvoSecurityHQ_LiftDoor{k + 1}L");
                        right[k] = Child(root, $"INT_EvoSecurityHQ_LiftDoor{k + 1}R");
                        BoxOf(left[k], 0f);
                        BoxOf(right[k], 0f);
                        BoxOf(Child(root, $"INT_EvoSecurityHQ_LiftCall{k + 1}"), 0.06f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, true);
                        var button = Child(root, $"INT_EvoSecurityHQ_LiftBtn{k + 1}");
                        button.SetParent(car, true);
                        BoxOf(button, 0.03f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, false);
                    }
                    lift.Configure(car, new[] { 0f, TowerFloor - F }, left, right, new Vector3(TX, F + 0.085f, TZ), Vector3.right, 0.75f);
                    // 전망실 망원경 둘 (sh_builder scopes): 아콜로지 · 1단계와 같은 들여다보기, 바깥쪽을 쉴 때 방향으로 좌우 ±100°
                    var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Maplestory SDF.asset");
                    var scopes = new[] { 190f, 350f };
                    for (int k = 0; k < scopes.Length; k++)
                    {
                        var head = Child(root, $"INT_EvoSecurityHQ_Scope{k + 1}");
                        BoxOf(head, 0.06f);
                        float a = scopes[k] * Mathf.Deg2Rad;
                        root.AddComponent<InteriorTelescope>().Configure(head, new Vector3(TX + 2.25f * Mathf.Cos(a), TowerFloor + 1.3f, TZ + 2.25f * Mathf.Sin(a)), 18f, 0f, font, 90f - scopes[k], 100f, startPitch: -16f, startFov: 24f, minPitch: -60f);
                    }
                });
        }

        /// <summary>
        /// 진화 2단계 시민 회관 "스텔라돔" (7칸 = (0 ~ 3, 0) + (0 ~ 2, 1), civ_builder — 사용자 2026-11-03 시안 1 "좋아 이대로 유니티로 옮겨줘"): 직접 체험 = 객석에 앉아 보는 상영 (<see cref="InteriorStellaDome"/>).
        /// 외형 평면 그대로: 2 × 2칸 유리 돔 (바닥이 칸 아래로 내려가는 그릇 — 입구 통로 · 객석 두 열 56석 (상영 때 등받이가 눕는 Back00 ~ 55) · 천구의 우물, 궤도 셋이 우물 걷는 머리 위로 돎 Orbit1 ~ 3) · 드럼의 하루 띠 DayBand ·
        /// 돔 둘레 바깥 갑판 (서 · 남 · 북, 우주복, 압력 막 아치) · 의료동 (T자 지붕 유리 띠 아래 흰 아치 복도 · 돌봄 캡슐 셋 · 별빛 교실 · 복지 창구, 창 + 5칸마다 분홍 십자) · 둥근 유리 라운지 (C자 소파 · 떠 있는 구체 Orbs).
        /// 문 자리 12 = 갑판 넷 (깊이 3.95, 닫힘 = 난간) · 의료동 다섯 · 라운지 셋 (깊이 3.79, 닫힘 = 막힌 창 패널 <see cref="InteriorSocket.SealedWall"/>). 돔 칸 넷은 해치 없음 (사용자 시안 결정: 돔 높이를 위해 바닥을 파냄), 아래 해치 = (2, 0) · (2, 1) · 라운지, 위 해치 = (2, 1) · 라운지.
        /// </summary>
        private static void BuildEvoCivicHall()
        {
            const float CX = 4f, CZ = 4f, RW = 6.35f, T1Y = F - 0.35f, T2Y = F - 0.7f, YP = F - 1.05f, Ceil = F + 3.85f;
            const float DeckEdge = 3.95f, Wall = 3.79f;
            var back = new Vector3Int(0, 0, -1);
            var front = new Vector3Int(0, 0, 1);
            var c00 = new Vector3Int(0, 0, 0);
            var c01 = new Vector3Int(0, 0, 1);
            var c10 = new Vector3Int(1, 0, 0);
            var c11 = new Vector3Int(1, 0, 1);
            var c20 = new Vector3Int(2, 0, 0);
            var c21 = new Vector3Int(2, 0, 1);
            var c30 = new Vector3Int(3, 0, 0);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(c00, Vector3Int.left, DeckEdge),
                new InteriorSocket(c01, Vector3Int.left, DeckEdge),
                new InteriorSocket(c00, back, DeckEdge),
                new InteriorSocket(c01, front, DeckEdge),
                new InteriorSocket(c10, back, Wall, default, true),
                new InteriorSocket(c20, back, Wall, default, true),
                new InteriorSocket(c11, front, Wall, default, true),
                new InteriorSocket(c21, front, Wall, default, true),
                new InteriorSocket(c21, Vector3Int.right, Wall, default, true),
                new InteriorSocket(c30, back, Wall, default, true),
                new InteriorSocket(c30, front, Wall, default, true),
                new InteriorSocket(c30, Vector3Int.right, Wall, default, true),
                new InteriorSocket(c20, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(c21, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(c30, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(c21, Vector3Int.up, Ceil, new Vector3(0.3f, 0f, 0f)),   // 지붕 유리 띠를 비킴
                new InteriorSocket(c30, Vector3Int.up, Ceil, Vector3.zero),
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.86f, 0.55f);
            var cool = new Color(0.72f, 0.82f, 1f);
            Vector3 Polar(float r, float az, float y)
            {
                float a = az * Mathf.Deg2Rad;
                return new Vector3(CX + r * Mathf.Cos(a), y, CZ + r * Mathf.Sin(a));
            }
            var lights = new List<LightSpec>();
            for (int k = 0; k < 8; k++)
                lights.Add(LightSpec.Point(Polar(5.7f, 22.5f + 45f * k, F + 2.05f), 5.5f, 0.75f, white));          // 입구 통로
            for (int k = 0; k < 4; k++)
                lights.Add(LightSpec.Point(Polar(3.6f, 90f * k, F + 1.5f), 5f, 0.85f, warm));                      // 객석 (상영 땐 어두워짐 — InteriorStellaDome, 낮으면 팔걸이에 뜨거운 점)
            for (int k = 0; k < 3; k++)
                lights.Add(LightSpec.Point(Polar(1.7f, 60f + 120f * k, YP + 1.7f), 4f, 0.7f, white));              // 천구의 우물
            lights.Add(LightSpec.Point(new Vector3(CX, 0.9f, CZ), 6f, 0.9f, warm));                                // 태양
            foreach (float az in new[] { 211f, 239f, 121f, 149f })
                lights.Add(LightSpec.Point(Polar(6.95f, az, F + 2.35f), 5f, 0.85f, cool));                         // 갑판 등불
            lights.Add(LightSpec.Point(new Vector3(-2.6f, F + 2.2f, 4f), 4.5f, 0.45f, cool));
            lights.Add(LightSpec.Point(new Vector3(3f, F + 2.2f, -3f), 4.5f, 0.45f, cool));
            lights.Add(LightSpec.Point(new Vector3(3f, F + 2.2f, 11f), 4.5f, 0.45f, cool));
            var hall = Color.white;   // 의료동 · 라운지 = 맑은 흰빛 (누런 등이면 흰 벽이 갈색으로 보임)
            foreach (float x in new[] { 10.6f, 14.4f, 18.2f })
                lights.Add(LightSpec.Point(new Vector3(x, Ceil - 0.5f, 0f), 7.5f, 1.9f, hall));                    // 동서 복도
            foreach (float z in new[] { 4.2f, 8.2f })
                lights.Add(LightSpec.Point(new Vector3(14.45f, Ceil - 0.5f, z), 7f, 1.7f, hall));                  // 남북 복도
            foreach (float z in new[] { 3.5f, 10.4f })
                lights.Add(LightSpec.Point(new Vector3(17.9f, Ceil - 0.6f, z), 6f, 1.6f, hall));                   // 돌봄실
            lights.Add(LightSpec.Point(new Vector3(12f, Ceil - 0.6f, 5.3f), 6f, 1.7f, hall));                      // 교실
            lights.Add(LightSpec.Point(new Vector3(8f, Ceil - 0.6f, 10.5f), 5.5f, 1.4f, hall));                    // 북쪽 끝
            lights.Add(LightSpec.Point(new Vector3(8f, Ceil - 0.6f, -2.5f), 5.5f, 1.4f, hall));                    // 남쪽 끝
            lights.Add(LightSpec.Point(new Vector3(12f, Ceil - 0.6f, -2.5f), 5.5f, 1.4f, hall));                   // 남쪽 쉼터
            lights.Add(LightSpec.Point(new Vector3(17.9f, Ceil - 0.6f, -2.5f), 5.5f, 1.4f, hall));
            lights.Add(LightSpec.Point(new Vector3(11.8f, Ceil - 0.6f, 10.4f), 5.5f, 1.4f, hall));                 // 북쪽 화단
            for (int k = 0; k < 4; k++)
            {
                float a = (45f + 90f * k) * Mathf.Deg2Rad;
                lights.Add(LightSpec.Point(new Vector3(24f + 2.1f * Mathf.Cos(a), Ceil - 0.5f, 2.1f * Mathf.Sin(a)), 6f, 1.4f, hall));   // 라운지
            }
            lights.Add(LightSpec.Point(new Vector3(24f, Ceil - 0.5f, 0f), 3.5f, 0.5f, new Color(0.85f, 0.75f, 1f)));   // 떠 있는 구체 위
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoCivicHall_Orbit1", new Vector3(CX, 0f, CZ), Vector3.up, 24f),
                new OrbitSpec("INT_EvoCivicHall_Orbit2", new Vector3(CX, 0f, CZ), Vector3.up, 13f),
                new OrbitSpec("INT_EvoCivicHall_Orbit3", new Vector3(CX, 0f, CZ), Vector3.up, 7f),
            };
            EvoAccent("CivicHall", "#FFB4E0");
            // 스폰 = 서남쪽 아치 안 입구 통로에서 천구의를 바라봄
            float spawnA = 225f * Mathf.Deg2Rad;
            Build("EvoCivicHall", "Evolved/MD_Evo_CivicHall", sockets, new Vector3(CX + 5.7f * Mathf.Cos(spawnA), F, CZ + 5.7f * Mathf.Sin(spawnA)), 45f, lights, orbits,
                configure: root =>
                {
                    AddOpenAir(root, new[]
                    {
                        new Vector3(CX, CZ, 6.5f),       // 돔
                        new Vector3(10.6f, -1.2f, 4.5f), // 의료동 (갑판은 x 6.1 서쪽)
                        new Vector3(10.6f, 9.2f, 4.5f),
                        new Vector3(15.5f, 0f, 6.2f),
                        new Vector3(15.5f, 8f, 6.2f),
                        new Vector3(24f, 0f, 3.95f),     // 라운지
                    });
                    // 충돌체 = 세운 의자 전체 모양 (F로 앉기 · 주민 맞춤), 보이는 앉는 판 _Seats · 눕는 등받이 _Back00 ~ 55는 충돌체 없음
                    var seats = Child(root, "INT_EvoCivicHall_SeatHit");
                    seats.gameObject.AddComponent<MeshCollider>().sharedMesh = seats.GetComponent<MeshFilter>().sharedMesh;
                    Object.DestroyImmediate(seats.GetComponent<MeshRenderer>());
                    // 앉는 자리: 눈이 등받이 바로 앞에 오게 발을 자리 가운데에서 0.28m 뒤로, 경첩 = 자리 가운데에서 0.19m 뒤 · 0.44m 위 (civ_builder seating · HINGE와 같은 각 · 반지름)
                    var spots = new List<Vector4>();
                    var backs = new List<Transform>();
                    var hinges = new List<Vector3>();
                    foreach (float section in new[] { 0f, 90f, 180f, 270f })
                    {
                        const float span = 32f;
                        foreach (var (radius, y, count) in new[] { (4.27f, T1Y, 8), (3.12f, T2Y, 6) })
                        {
                            for (int k = 0; k < count; k++)
                            {
                                float az = section - span + 2f * span * (k + 0.5f) / count;
                                var p = Polar(radius + 0.28f, az, y);
                                spots.Add(new Vector4(p.x, p.y, p.z, Mathf.Atan2(CX - p.x, CZ - p.z) * Mathf.Rad2Deg));
                                hinges.Add(Polar(radius + 0.19f, az, y + 0.44f));
                                backs.Add(Child(root, $"INT_EvoCivicHall_Back{backs.Count:00}"));
                            }
                        }
                    }
                    root.AddComponent<InteriorStellaDome>().Configure(
                        Child(root, "INT_EvoCivicHall_StarDome").GetComponent<Renderer>(),
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2StarLinesPath),
                        new[] { Child(root, "INT_EvoCivicHall_DayBand").GetComponent<Renderer>(), Child(root, "INT_EvoCivicHall_DomeGlow").GetComponent<Renderer>() },
                        seats, spots.ToArray(), backs.ToArray(), hinges.ToArray(), Child(root, "INT_EvoCivicHall_Orbs"), new Vector3(CX, CZ, RW), new Vector3(1.2f, 3.78f, 2.3f));
                });
        }

        /// <summary>
        /// 진화 2단계 대형 토러스 "은하철도 링" (5 × 5칸 바퀴 = 고리 + 가운데 십자 + 허브 위 한 칸, gt_builder — 사용자 2026-11-02 시안 1 "이대로 유니티로 옮겨줘, 의자 앉기도", 2026-11-03 플레이 의견으로 다시 짬): 직접 체험 = 밤 열차 타고 역 넷을 돎 (<see cref="InteriorTrain"/>).
        /// 외형 그대로 원형 고리 + 바큇살 넷 + 시계탑 허브: 고가 선로 · 역 넷 (승강장 · 스크린 도어 · 고리를 따라 휜 계단 · 유리 홀) · 동네 고리 (산책로 · 흰 집 · 잔디 · 나무, 유리 지붕) · 고가 아래 아치 회랑 · 고가 아래 아치 집 여덟 · 유리 통로 넷 · 돔 허브 · 엘리베이터 (<see cref="InteriorLift"/>) · 시계탑 (둥근 사각 몸통 · 바깥에서 흰 시계판 · 안에서 내다보는 시계 방 · 종루 · 첨탑 · 도는 톱니 GearA).
        /// 전부 유리 · 지붕 안이라 우주복 없음. 하늘 껍질 = 바퀴 전체를 감싼 큰 상자 (안쪽 빈 칸 넷을 벽으로 두르면 허브 · 건너편이 가려짐).
        /// 문 자리 = 외형 면이 있는 바깥 칸 12 (가운데 칸은 고가 벽 바로 뒤 깊이 3.9, 양옆 칸은 고가가 비스듬히 지나 짧은 문간 깊이 2.9 — 표준 3.2면 문 패널이 고리 곡면에서 떠 보임, 연결 안 된 자리 = 막힌 아치 창 패널 <see cref="InteriorTemplate.SealedSockets"/>), 아래 해치 = 바닥 칸 17, 시계탑 칸 문 · 위 해치 없음 (네 면이 시계).
        /// </summary>
        private static void BuildEvoGrandTorus()
        {
            const float HX = 16f, HZ = 16f, YF = 1.5f, YR = 5.7f, YD0 = 1.12f, RTK = 18.18f;
            var back = new Vector3Int(0, 0, -1);
            var front = new Vector3Int(0, 0, 1);
            const float Rim = 3.9f, Side = 2.9f;
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(new Vector3Int(1, 0, 0), back, Side),
                new InteriorSocket(new Vector3Int(2, 0, 0), back, Rim),
                new InteriorSocket(new Vector3Int(3, 0, 0), back, Side),
                new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.left, Side),
                new InteriorSocket(new Vector3Int(0, 0, 2), Vector3Int.left, Rim),
                new InteriorSocket(new Vector3Int(0, 0, 3), Vector3Int.left, Side),
                new InteriorSocket(new Vector3Int(4, 0, 1), Vector3Int.right, Side),
                new InteriorSocket(new Vector3Int(4, 0, 2), Vector3Int.right, Rim),
                new InteriorSocket(new Vector3Int(4, 0, 3), Vector3Int.right, Side),
                new InteriorSocket(new Vector3Int(1, 0, 4), front, Side),
                new InteriorSocket(new Vector3Int(2, 0, 4), front, Rim),
                new InteriorSocket(new Vector3Int(3, 0, 4), front, Side),
                // 고리 칸 = 회랑 바닥 (칸 가운데가 안쪽 벽에 가까워 바깥으로 살짝), 역 칸 = 승강장 아래, 십자 칸 = 유리 통로, 허브 칸 = 엘리베이터를 비켜 동쪽
                new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.down, -F, new Vector3(-0.16f, 0f, -0.31f)),
                new InteriorSocket(new Vector3Int(2, 0, 0), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(3, 0, 0), Vector3Int.down, -F, new Vector3(0.16f, 0f, -0.31f)),
                new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.down, -F, new Vector3(-0.31f, 0f, -0.16f)),
                new InteriorSocket(new Vector3Int(0, 0, 2), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(0, 0, 3), Vector3Int.down, -F, new Vector3(-0.31f, 0f, 0.16f)),
                new InteriorSocket(new Vector3Int(4, 0, 1), Vector3Int.down, -F, new Vector3(0.31f, 0f, -0.16f)),
                new InteriorSocket(new Vector3Int(4, 0, 2), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(4, 0, 3), Vector3Int.down, -F, new Vector3(0.31f, 0f, 0.16f)),
                new InteriorSocket(new Vector3Int(1, 0, 4), Vector3Int.down, -F, new Vector3(-0.16f, 0f, 0.31f)),
                new InteriorSocket(new Vector3Int(2, 0, 4), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(3, 0, 4), Vector3Int.down, -F, new Vector3(0.16f, 0f, 0.31f)),
                new InteriorSocket(new Vector3Int(2, 0, 1), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(1, 0, 2), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(3, 0, 2), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(2, 0, 3), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(2, 0, 2), Vector3Int.down, -F, new Vector3(2.4f, 0f, 0f)),
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.86f, 0.55f);
            var cool = new Color(0.72f, 0.82f, 1f);
            Vector3 Polar(float r, float az, float y)
            {
                float a = az * Mathf.Deg2Rad;
                return new Vector3(HX + r * Mathf.Cos(a), y, HZ + r * Mathf.Sin(a));
            }
            var lights = new List<LightSpec>();
            for (int k = 0; k < 4; k++)
            {
                lights.Add(LightSpec.Point(Polar(2.4f, 45f + 90f * k, 1.7f), 6f, 0.85f, white));           // 허브
                lights.Add(LightSpec.Point(Polar(1.7f, 45f + 90f * k, YR + 3f), 5f, 0.8f, cool));          // 시계탑 방
                float s = 270f - 90f * k;   // 남 · 서 · 북 · 동 (gt_builder rot_y 순서)
                lights.Add(LightSpec.Point(Polar(14.4f, s, F + 2.3f), 6f, 0.85f, warm));                   // 통로 입구
                lights.Add(LightSpec.Point(Polar(6.5f, s, F + 2.2f), 5f, 0.7f, cool));                     // 유리 통로
                foreach (float off in new[] { -15f, 15f })
                    lights.Add(LightSpec.Point(Polar(14.4f, s + off, F + 3f), 5f, 0.8f, white));           // 휜 계단
                foreach (float off in new[] { -8f, 8f })
                    lights.Add(LightSpec.Point(Polar(16f, s + off, YD0 - 0.3f), 5f, 0.7f, warm));          // 승강장 아래 길
                foreach (float off in new[] { -9f, 9f })
                    lights.Add(LightSpec.Point(Polar(16f, s + off, YF + 1.9f), 6f, 0.9f, white));          // 승강장
                foreach (float off in new[] { -31f, 31f })
                    lights.Add(LightSpec.Point(Polar(14.2f, s + off, F + 2.2f), 7f, 1.2f, white));         // 동네 (흰 집이 누렇게 보이지 않게, 금빛 지붕에 빛이 뭉치지 않게 산책로 안쪽)
                lights.Add(LightSpec.Point(Polar(15.5f, s + 45f, F + 2.4f), 7f, 1.2f, white));             // 마당
                foreach (float off in new[] { -24.35f, 0f, 24.35f })
                    lights.Add(LightSpec.Point(Polar(18.4f, s + off, F + 2.2f), 3.5f, 0.7f, white));       // 문간 셋
                foreach (float off in new[] { 39.84f, 50.16f })
                    lights.Add(LightSpec.Point(Polar(18.2f, 90f * k + off, YD0 - 0.6f), 3.6f, 0.9f, warm)); // 아치 집
            }
            lights.Add(LightSpec.Point(new Vector3(HX, 9.95f, HZ), 3f, 0.8f, cool));                       // 종루
            for (int k = 0; k < 4; k++)
            {
                foreach (float off in new[] { -30f, -11.25f, 11.25f, 30f })
                    lights.Add(LightSpec.Point(Polar(18.2f, 270f - 90f * k + off, YD0 - 0.35f), 6f, 0.75f, warm)); // 고가 아래 회랑 (칸막이 바로 앞은 빛이 뭉쳐 비킴)
            }
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoGrandTorus_GearA", new Vector3(HX, 0f, HZ), Vector3.up, 14f),         // 시계탑 천장 밑 톱니
            };
            EvoAccent("GrandTorus", "#B0E0FF");
            // 스폰 = 남쪽 유리 통로 끝에서 역을 바라봄 (양옆이 휜 계단)
            Build("EvoGrandTorus", "Evolved/MD_Evo_GrandTorus", sockets, new Vector3(HX, F, HZ - 13f), 180f, lights, orbits,
                configure: root =>
                {
                    // 엘리베이터 (허브 ↔ 시계탑 방)
                    var car = Child(root, "INT_EvoGrandTorus_LiftCar");
                    var carGlass = Child(root, "INT_EvoGrandTorus_LiftCarGlass");
                    carGlass.SetParent(car, true);
                    foreach (var t in new[] { car, carGlass })
                        t.gameObject.AddComponent<MeshCollider>().sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                    var lift = root.AddComponent<InteriorLift>();
                    var left = new Transform[2];
                    var right = new Transform[2];
                    for (int k = 0; k < 2; k++)
                    {
                        left[k] = Child(root, $"INT_EvoGrandTorus_LiftDoor{k + 1}L");
                        right[k] = Child(root, $"INT_EvoGrandTorus_LiftDoor{k + 1}R");
                        BoxOf(left[k], 0f);
                        BoxOf(right[k], 0f);
                        BoxOf(Child(root, $"INT_EvoGrandTorus_LiftCall{k + 1}"), 0.06f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, true);
                        var button = Child(root, $"INT_EvoGrandTorus_LiftBtn{k + 1}");
                        button.SetParent(car, true);
                        BoxOf(button, 0.03f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, false);
                    }
                    lift.Configure(car, new[] { 0f, YR - F }, left, right, new Vector3(HX, F + 0.085f, HZ), Vector3.right, 0.62f);
                    // 열차: 허브 축 피벗 아래 (쉬는 자세 = 남쪽 역). 문 · 스크린 도어는 움직이는 상자 충돌체
                    var pivot = new GameObject("TrainPivot").transform;
                    pivot.SetParent(root.transform, false);
                    pivot.localPosition = new Vector3(HX, 0f, HZ);
                    var seats = new Transform[2];
                    foreach (var part in new[] { "Train", "TrainGlass", "TrainSeat1", "TrainSeat2" })
                    {
                        var t = Child(root, "INT_EvoGrandTorus_" + part);
                        t.gameObject.AddComponent<MeshCollider>().sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                        t.SetParent(pivot, true);
                    }
                    var doorsLeft = new Transform[2];
                    var doorsRight = new Transform[2];
                    for (int k = 0; k < 2; k++)
                    {
                        seats[k] = pivot.Find($"INT_EvoGrandTorus_TrainSeat{k + 1}");
                        doorsLeft[k] = Child(root, $"INT_EvoGrandTorus_TrainDoor{k + 1}L");
                        doorsRight[k] = Child(root, $"INT_EvoGrandTorus_TrainDoor{k + 1}R");
                        BoxOf(doorsLeft[k], 0f);
                        BoxOf(doorsRight[k], 0f);
                        doorsLeft[k].SetParent(pivot, true);
                        doorsRight[k].SetParent(pivot, true);
                    }
                    var gatesLeft = new Transform[8];
                    var gatesRight = new Transform[8];
                    for (int q = 0; q < 4; q++)
                    {
                        for (int k = 0; k < 2; k++)
                        {
                            gatesLeft[q * 2 + k] = Child(root, $"INT_EvoGrandTorus_Gate{q}{k + 1}L");
                            gatesRight[q * 2 + k] = Child(root, $"INT_EvoGrandTorus_Gate{q}{k + 1}R");
                            BoxOf(gatesLeft[q * 2 + k], 0f);
                            BoxOf(gatesRight[q * 2 + k], 0f);
                        }
                    }
                    // 객차 등 (열차와 함께 돎) · 굴뚝 별빛 가루 자리 (gt_builder loco: 기관차 앞에서 30%, 꼭대기)
                    const float doorAngle = 7.879f, carHalf = 7.406f;
                    foreach (float off in new[] { -doorAngle, doorAngle })
                    {
                        var lamp = new GameObject("CarLight").transform;
                        lamp.SetParent(pivot, false);
                        float a = (270f + off) * Mathf.Deg2Rad;
                        lamp.localPosition = new Vector3(18.5f * Mathf.Cos(a), YF + 1.8f, 18.5f * Mathf.Sin(a));   // 문 쪽 은빛 테가 하얗게 번지지 않게 바깥 벽 쪽으로
                        var l = lamp.gameObject.AddComponent<Light>();
                        l.type = LightType.Point;
                        l.shadows = LightShadows.None;
                        l.range = 4.5f;
                        l.intensity = 0.7f;
                        l.color = warm;
                    }
                    var stack = new GameObject("StackSmoke").transform;
                    stack.SetParent(pivot, false);
                    float sa = 241.86f * Mathf.Deg2Rad;
                    stack.localPosition = new Vector3(RTK * Mathf.Cos(sa), 3.84f, RTK * Mathf.Sin(sa));
                    stack.localScale = Vector3.one * 5f;
                    var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Maplestory SDF.asset");
                    root.AddComponent<InteriorTrain>().Configure(pivot, doorsLeft, doorsRight, gatesLeft, gatesRight, seats, stack, ModuleFxMaterials.Get("FireworkSpark"), font,
                        new[] { "새벽", "은하", "자정", "달빛" }, doorAngle, doorAngle + carHalf, carHalf - 0.9f, 17.12f, 19.24f, 18.95f, 18.3f, YF + 0.07f, new Vector2(16.865f, YF + 2.31f));
                });
        }

        /// <summary>
        /// 진화 2단계 아콜로지 "유리 알 속 하늘 섬" (2 × 2칸 × 4층, ar_builder — 사용자 2026-11-02 시안 1 "이대로 유니티로 옮겨줘"): 알 전체가 실내 (살아 있는 잔디 · 나무, 우주복 없음), 유리 엘리베이터 탑 (<see cref="InteriorLift"/>)으로 네 층:
        /// 전망대 (유리 바닥 고리 · 탑을 두른 벤치 · 난간 곁 망원경 둘 <see cref="InteriorTelescope"/> — 머리 Scope1 · 2, F로 들여다봄) · 섬 도시 (돌 광장 · 잔디 띠의 타워 다섯 · 작은 집 · 나무 · 디딤돌 · 둘레 산책로 · 유리 난간) ·
        /// 뿌리 층 (고리 복도 둘레 집 여섯: 금빛 아치 문 · 침대 · 안락의자 · 창 · 화분, 아래턱 매달린 정원) · 결정실 (유리 바닥 아래 중력 결정 Crystal · 머리 위로 떠도는 돌 Stones · 벽 벤치).
        /// 유리 알 밖 = 고정 적도 띠 + 도는 혼천의 고리 RingA · RingB · 받침 · 발톱 셋. 문 자리 · 해치 없음 (외형 모든 면 연결점 없음 — 바깥에서 바로 입장).
        /// </summary>
        private static void BuildEvoArcology()
        {
            const float L0 = 2.35f, L1 = 6.55f, YI = 10.9f, L3 = 18.7f;
            var sockets = new List<InteriorSocket>();
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.86f, 0.55f);
            var cool = new Color(0.72f, 0.82f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(6.2f, YI + 3f, 4f), 6f, 0.8f, white),       // 광장 (탑 둘레)
                LightSpec.Point(new Vector3(1.8f, YI + 3f, 4f), 6f, 0.8f, white),
                LightSpec.Point(new Vector3(4f, YI + 3f, 6.2f), 6f, 0.8f, white),
                LightSpec.Point(new Vector3(4f, YI + 3f, 1.8f), 6f, 0.8f, white),
                LightSpec.Point(new Vector3(4f, L3 + 2.2f, 1.6f), 5f, 0.7f, cool),      // 전망대
                LightSpec.Point(new Vector3(4f, L3 + 2.2f, 6.4f), 5f, 0.7f, cool),
                LightSpec.Point(new Vector3(6f, L0 + 2.7f, 4f), 5f, 1.05f, cool),       // 결정실
                LightSpec.Point(new Vector3(2f, L0 + 2.7f, 4f), 5f, 1.05f, cool),
                LightSpec.Point(new Vector3(5.5f, L0 - 0.9f, 4f), 3.2f, 0.9f, cool),    // 유리 바닥 아래 결정
                LightSpec.Point(new Vector3(2.5f, L0 - 0.9f, 4f), 3.2f, 0.9f, cool),
            };
            for (int k = 0; k < 5; k++)   // 둘레 산책로 · 잔디 띠 (타워 사이)
            {
                float a = (56f + 72f * k) * Mathf.Deg2Rad;
                lights.Add(LightSpec.Point(new Vector3(4f + 4.8f * Mathf.Cos(a), YI + 2.6f, 4f + 4.8f * Mathf.Sin(a)), 5.5f, 0.8f, warm));
            }
            for (int k = 0; k < 3; k++)   // 뿌리 층 복도
            {
                float a = (90f + 120f * k) * Mathf.Deg2Rad;
                lights.Add(LightSpec.Point(new Vector3(4f + 2f * Mathf.Cos(a), L1 + 2.9f, 4f + 2f * Mathf.Sin(a)), 4.5f, 0.8f, white));
            }
            for (int k = 0; k < 6; k++)   // 집 여섯
            {
                float a = 60f * k * Mathf.Deg2Rad;
                lights.Add(LightSpec.Point(new Vector3(4f + 3.85f * Mathf.Cos(a), L1 + 2.9f, 4f + 3.85f * Mathf.Sin(a)), 3.6f, 0.9f, warm));
            }
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoArcology_Crystal", new Vector3(4f, 0f, 4f), Vector3.up, 20f),                                 // 중력 결정 (유리 바닥 아래)
                new OrbitSpec("INT_EvoArcology_Stones", new Vector3(4f, 0f, 4f), Vector3.up, 14f),                                  // 머리 위로 떠도는 돌
                new OrbitSpec("INT_EvoArcology_RingA", new Vector3(4f, 15.6f, 4f), new Vector3(0f, 0.9272f, 0.3746f), 12f),         // 혼천의 고리 (ar_builder armillary_ring)
                new OrbitSpec("INT_EvoArcology_RingB", new Vector3(4f, 8.4f, 4f), new Vector3(0f, 0.866f, -0.5f), -16f),
            };
            EvoAccent("Arcology", "#8FD0FF");
            // 스폰 = 남쪽 산책로에서 유리 엘리베이터 탑을 바라봄 (디딤돌 길 끝)
            Build("EvoArcology", "Evolved/MD_Evo_Arcology", sockets, new Vector3(4.18f, YI + 0.02f, -1.1f), -2f, lights, orbits,
                configure: root =>
                {
                    var car = Child(root, "INT_EvoArcology_LiftCar");
                    var carGlass = Child(root, "INT_EvoArcology_LiftCarGlass");
                    carGlass.SetParent(car, true);
                    foreach (var t in new[] { car, carGlass })
                        t.gameObject.AddComponent<MeshCollider>().sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                    var lift = root.AddComponent<InteriorLift>();
                    var left = new Transform[4];
                    var right = new Transform[4];
                    for (int k = 0; k < 4; k++)
                    {
                        left[k] = Child(root, $"INT_EvoArcology_LiftDoor{k + 1}L");
                        right[k] = Child(root, $"INT_EvoArcology_LiftDoor{k + 1}R");
                        BoxOf(left[k], 0f);
                        BoxOf(right[k], 0f);
                        BoxOf(Child(root, $"INT_EvoArcology_LiftCall{k + 1}"), 0.06f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, true);
                        var button = Child(root, $"INT_EvoArcology_LiftBtn{k + 1}");
                        button.SetParent(car, true);
                        BoxOf(button, 0.03f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, false);
                    }
                    lift.Configure(car, new[] { 0f, L1 - L0, YI - L0, L3 - L0 }, left, right, new Vector3(4f, L0 + 0.085f, 4f), Vector3.right, 0.85f);
                    // 망원경 둘 (ar_builder SCOPE_AZ · SCOPE_R): 머리 경계 상자 (시선 광선) + 들여다보기, 바깥쪽을 쉴 때 방향으로 좌우 ±100° (둘이 거의 한 바퀴).
                    // 처음 시선 = 넓게 · 아래로 (정거장 판을 봄 — 수평이면 놓인 자리에 따라 빈 하늘만 보임), 아래로 더 볼 수 있게 (알 꼭대기 근처라 옆 모듈이 발밑)
                    var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Maplestory SDF.asset");
                    var scopes = new[] { 202.5f, 337.5f };
                    for (int k = 0; k < scopes.Length; k++)
                    {
                        var head = Child(root, $"INT_EvoArcology_Scope{k + 1}");
                        BoxOf(head, 0.06f);
                        float a = scopes[k] * Mathf.Deg2Rad;
                        root.AddComponent<InteriorTelescope>().Configure(head, new Vector3(4f + 2.7f * Mathf.Cos(a), L3 + 1.3f, 4f + 2.7f * Mathf.Sin(a)), 18f, 0f, font, 90f - scopes[k], 100f, startPitch: -16f, startFov: 24f, minPitch: -60f);
                    }
                });
        }

        /// <summary>
        /// 진화 2단계 융합 플랜트 "별을 키우는 온실" (3 × 2칸, fp_builder — 사용자 2026-11-02 시안 1 "이대로 유니티로 옮겨줘"): 2단계 개방형 — 레시피 "융합로의 열로 물을 갈라 수소 · 산소까지"를 순서대로 걸으며 봄:
        /// 유리 돔 (실내, 압력 막 아치 셋) 속 눈높이의 보라 코일 여덟 · 세 가닥으로 꼬인 플라즈마 (Plasma, 돎) · 받침 패널 넷 → 주황 열 관 둘 아래 전구 길 (퍼걸러 둘) →
        /// 물 캡슐 (전극 둘 둘레 거품 BubblesA · B — 수소 쪽이 두 배) · 거품 기둥 넷 (Gas0 ~ 3, 수소 흰 기둥이 산소 기둥의 두 배 높이) · 열 교환 콘솔 둘,
        /// 서쪽 모서리 물 달 둘 (유리 관이 돔으로) · 가운데 마당 둥근 화단 벤치 둘 (플라즈마 볼 꽃 = 기술 식물) · 별 관측경 · 수소 풍선 수레 (Balloons, 천천히 돎) · 귀퉁이 벤치.
        /// 문 자리 = 외형 면이 있는 동쪽 칸 네 곳 (동 둘 · 남 · 북 — 닫힌 자리 = 난간 조각, 열린 자리 = 둥근 문, 키트), 아래 해치 = 바닥 칸 여섯 (돔 칸 넷은 돔을 비켜 남 · 북 갑판으로), 위 해치 없음 (하늘).
        /// </summary>
        private static void BuildEvoFusionPlant()
        {
            var c20 = new Vector3Int(2, 0, 0);
            var c21 = new Vector3Int(2, 0, 1);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(c20, Vector3Int.right, Socket),
                new InteriorSocket(c20, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(c21, Vector3Int.right, Socket),
                new InteriorSocket(c21, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(new Vector3Int(0, 0, 0), Vector3Int.down, -F, new Vector3(0.2f, 0f, -2f)),   // 돔 칸 = 돔을 비켜 남 · 북 갑판으로
                new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.down, -F, new Vector3(-0.2f, 0f, -2f)),
                new InteriorSocket(c20, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.down, -F, new Vector3(0.2f, 0f, 2f)),
                new InteriorSocket(new Vector3Int(1, 0, 1), Vector3Int.down, -F, new Vector3(-0.2f, 0f, 2f)),
                new InteriorSocket(c21, Vector3Int.down, -F, Vector3.zero),
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.86f, 0.55f);
            var cool = new Color(0.72f, 0.82f, 1f);
            var plasma = new Color(0.8f, 0.5f, 1f);
            var water = new Color(0.6f, 0.9f, 1f);
            const float plasmaY = F + 1.57f;
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(4f, F + 3.1f, -0.4f), 6f, 0.75f, white),    // 돔 둘레 길
                LightSpec.Point(new Vector3(4f, F + 3.1f, 8.4f), 6f, 0.75f, white),
                LightSpec.Point(new Vector3(-0.4f, F + 3.1f, 4f), 6f, 0.75f, white),
                LightSpec.Point(new Vector3(8.4f, F + 3.1f, 4f), 6f, 0.75f, white),
                LightSpec.Point(new Vector3(1.65f, plasmaY, 4f), 4f, 0.8f, plasma),     // 플라즈마 빛 (그릇 속)
                LightSpec.Point(new Vector3(6.35f, plasmaY, 4f), 4f, 0.8f, plasma),
                LightSpec.Point(new Vector3(4f, plasmaY, 1.65f), 4f, 0.8f, plasma),
                LightSpec.Point(new Vector3(4f, plasmaY, 6.35f), 4f, 0.8f, plasma),
                LightSpec.Point(new Vector3(11.9f, F + 2.6f, 4f), 5.5f, 1.1f, warm),    // 전구 길
                LightSpec.Point(new Vector3(16f, F + 3.2f, 4f), 6f, 0.9f, water),       // 물 캡슐 위
                LightSpec.Point(new Vector3(14.2f, F + 2.2f, 1.25f), 3.5f, 0.7f, white),// 수소 기둥
                LightSpec.Point(new Vector3(14.2f, F + 2.2f, 6.75f), 3.5f, 0.7f, white),
                LightSpec.Point(new Vector3(14.2f, F + 1.3f, -0.55f), 3.5f, 0.6f, water),// 산소 기둥
                LightSpec.Point(new Vector3(14.2f, F + 1.3f, 8.55f), 3.5f, 0.6f, water),
                LightSpec.Point(new Vector3(-1.75f, F + 1.25f, -1.75f), 4f, 0.8f, water),   // 물 달
                LightSpec.Point(new Vector3(-1.75f, F + 1.25f, 9.75f), 4f, 0.8f, water),
                LightSpec.Point(new Vector3(10.9f, F + 2.3f, -0.4f), 4.5f, 0.7f, plasma),   // 화단 (플라즈마 볼 꽃)
                LightSpec.Point(new Vector3(10.9f, F + 2.3f, 8.4f), 4.5f, 0.7f, plasma),
                LightSpec.Point(new Vector3(4f, F + 2.8f, -2.4f), 6f, 0.6f, cool),      // 빈 갑판 (밤하늘 찬 빛)
                LightSpec.Point(new Vector3(4f, F + 2.8f, 10.4f), 6f, 0.6f, cool),
                LightSpec.Point(new Vector3(-2.4f, F + 2.8f, 4f), 6f, 0.6f, cool),
                LightSpec.Point(new Vector3(17.6f, F + 2.8f, -0.6f), 7f, 0.7f, cool),   // 동쪽 마당
                LightSpec.Point(new Vector3(17.6f, F + 2.8f, 8.6f), 7f, 0.7f, cool),
            };
            const float capsuleBubbles = 110f, gasBubbles = 90f;
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoFusionPlant_Plasma", new Vector3(4f, 0f, 4f), Vector3.up, 24f),          // 꼬인 플라즈마 (fp_builder plasma)
                new OrbitSpec("INT_EvoFusionPlant_BubblesA", new Vector3(16f, 0f, 3f), Vector3.up, capsuleBubbles),   // 전극 둘레 거품 — 나선을 돌리면 오르는 것처럼 보임 (수소)
                new OrbitSpec("INT_EvoFusionPlant_BubblesB", new Vector3(16f, 0f, 5f), Vector3.up, capsuleBubbles),   // 산소
                new OrbitSpec("INT_EvoFusionPlant_Gas0", new Vector3(14.2f, 0f, 1.25f), Vector3.up, gasBubbles),      // 거품 기둥 (fp_builder GAS)
                new OrbitSpec("INT_EvoFusionPlant_Gas1", new Vector3(14.2f, 0f, -0.55f), Vector3.up, gasBubbles),
                new OrbitSpec("INT_EvoFusionPlant_Gas2", new Vector3(14.2f, 0f, 6.75f), Vector3.up, gasBubbles),
                new OrbitSpec("INT_EvoFusionPlant_Gas3", new Vector3(14.2f, 0f, 8.55f), Vector3.up, gasBubbles),
                new OrbitSpec("INT_EvoFusionPlant_Balloons", new Vector3(17.85f, 0f, 9.95f), Vector3.up, 7f),         // 풍선 (끈이 수레 기둥 끝 = 축에 모임)
            };
            EvoAccent("FusionPlant", "#FFB070");
            // 스폰 = 전구 길에서 돔 아치 · 융합로를 바라봄
            Build("EvoFusionPlant", "Evolved/MD_Evo_FusionPlant", sockets, new Vector3(12.2f, F, 4f), -90f, lights, orbits,
                configure: root =>
                {
                    AddOpenAir(root, new[] { new Vector3(4f, 4f, 5.4f) });   // 돔 (fp_builder CX · CZ, 압력 막 반지름 CURTAIN_R)
                });
        }

        /// <summary>
        /// 진화 2단계 궤도 미러 "해바라기 정원" (4 × 2칸 + 가운데 위 2 × 2, om_builder — 사용자 2026-11-02 시안 1 "이대로 유니티로 옮겨줘"): 2단계 개방형 —
        /// 거울 해바라기 열여섯 (머리 Head00 ~ 15 = <see cref="InteriorHeliostat"/>가 실제 해를 따라 돌림) · 거울에서 집광탑 수신기로 모이는 빛기둥 (<see cref="SpaceStation.Building.LightBeamFx"/>, 바깥 모듈과 같은 효과) ·
        /// 화단을 가르는 오솔길 (머리 아래를 걸음) · 거울 데이지 · 돌 마당 · 서 · 동 · 남 길 (아치 · 축제 전구) · 유리 덮개 속 진짜 해바라기 · 별 관측경 · 귀퉁이 · 길가 벤치,
        /// 가운데 전시관 (압력 막 아치 셋, 천장 렌즈에서 내려온 빛기둥 · 해 우물 혼천의 작은 해 + 도는 고리 ArmA · ArmB · 해시계 바닥 · 북쪽 콘솔 둘 · 벤치), 위 집광탑 (금빛 해살 왕관 · 도는 거울 고리 Ring).
        /// 문 자리 = 외형 면이 있는 네 곳 (0번 칸 서 · 남, 3번 칸 동 · 남 — 열린 갑판이라 닫힌 자리 = 난간 조각, 열린 자리 = 둥근 문, 키트), 아래 해치 = 바닥 칸 여덟 (양 끝 칸은 화단을 비킴), 위 해치 없음 (하늘).
        /// </summary>
        private static void BuildEvoOrbitalMirror()
        {
            var c0 = new Vector3Int(0, 0, 0);
            var c3 = new Vector3Int(3, 0, 0);
            var back = new Vector3Int(0, 0, -1);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(c0, Vector3Int.left, Socket),
                new InteriorSocket(c0, back, Socket),
                new InteriorSocket(c3, Vector3Int.right, Socket),
                new InteriorSocket(c3, back, Socket),
                new InteriorSocket(c0, Vector3Int.down, -F, new Vector3(3.2f, 0f, -2f)),               // 양 끝 칸 = 화단을 비켜 바깥 갑판으로
                new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(2, 0, 0), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(c3, Vector3Int.down, -F, new Vector3(-3.2f, 0f, -2f)),
                new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.down, -F, new Vector3(3.2f, 0f, 2f)),
                new InteriorSocket(new Vector3Int(1, 0, 1), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(2, 0, 1), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(3, 0, 1), Vector3Int.down, -F, new Vector3(-3.2f, 0f, 2f)),
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.86f, 0.55f);
            var cool = new Color(0.72f, 0.82f, 1f);
            const float sunY = F + 1.42f;
            var receiver = new Vector3(12f, F + 8.96f, 4f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(9.9f, F + 2.7f, 4f), 6f, 0.9f, white),      // 전시관
                LightSpec.Point(new Vector3(14.1f, F + 2.7f, 4f), 6f, 0.9f, white),
                LightSpec.Point(new Vector3(12f, sunY, 4f), 4.5f, 0.9f, warm),          // 해 우물 작은 해
                LightSpec.Point(new Vector3(12f, F + 2.6f, -2f), 5.5f, 1.1f, warm),     // 남쪽 입구 전구
                LightSpec.Point(new Vector3(5.5f, F + 2.6f, 4f), 5.5f, 1f, warm),       // 서 · 동 길
                LightSpec.Point(new Vector3(18.5f, F + 2.6f, 4f), 5.5f, 1f, warm),
                LightSpec.Point(new Vector3(0f, F + 2.8f, -1.2f), 7f, 0.7f, cool),      // 빈 갑판 (밤하늘 찬 빛)
                LightSpec.Point(new Vector3(24f, F + 2.8f, -1.2f), 7f, 0.7f, cool),
                LightSpec.Point(new Vector3(0f, F + 2.8f, 9.2f), 7f, 0.7f, cool),
                LightSpec.Point(new Vector3(24f, F + 2.8f, 9.2f), 7f, 0.7f, cool),
                LightSpec.Point(new Vector3(12f, F + 2.8f, 10f), 6f, 0.6f, cool),       // 북쪽 갑판 (유리 덮개 · 관측경)
                LightSpec.Point(new Vector3(10.2f, receiver.y - 1f, 4f), 5.5f, 0.5f, warm),   // 탑 · 수신기 아래 (축 위에 두면 아랫면이 하얗게 번짐)
                LightSpec.Point(new Vector3(13.8f, receiver.y - 1f, 4f), 5.5f, 0.5f, warm),
                LightSpec.Point(new Vector3(2f, F + 2f, 0.85f), 5.5f, 0.7f, warm),      // 화단 (해바라기 머리 아래)
                LightSpec.Point(new Vector3(2f, F + 2f, 7.15f), 5.5f, 0.7f, warm),
                LightSpec.Point(new Vector3(22f, F + 2f, 0.85f), 5.5f, 0.7f, warm),
                LightSpec.Point(new Vector3(22f, F + 2f, 7.15f), 5.5f, 0.7f, warm),
            };
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoOrbitalMirror_ArmA", new Vector3(12f, sunY, 4f), new Vector3(0.4067f, 0.9135f, 0f), 28f),   // 혼천의 고리 (om_builder armillary)
                new OrbitSpec("INT_EvoOrbitalMirror_ArmB", new Vector3(12f, sunY, 4f), new Vector3(0f, 0.342f, 0.9397f), -40f),
                new OrbitSpec("INT_EvoOrbitalMirror_Ring", receiver, new Vector3(0.309f, 0.9511f, 0f), 30f),                     // 수신기 거울 고리 (tower_ring)
            };
            EvoAccent("OrbitalMirror", "#FFE89A");
            // 스폰 = 남쪽 갑판에서 전시관 · 탑 · 동쪽 해바라기를 바라봄 (해치 · 등불 비킴)
            Build("EvoOrbitalMirror", "Evolved/MD_Evo_OrbitalMirror", sockets, new Vector3(8.5f, F, -2.3f), 30f, lights, orbits,
                configure: root =>
                {
                    // 머리 가운데 · 지름 = om_builder HEADS · FLOWERS (지름 = 거울 반지름 머리 × 0.78 × 2)
                    Vector3[] centers =
                    {
                        new Vector3(0.057f, 1.59f, 0.403f), new Vector3(-0.072f, 1.29f, 1.618f), new Vector3(3.783f, 2.44f, 0.178f), new Vector3(4.472f, 1.89f, 1.378f),
                        new Vector3(0.08f, 1.64f, 6.88f), new Vector3(3.441f, 2.54f, 7.852f), new Vector3(4.473f, 1.94f, 6.448f), new Vector3(-1.895f, 1.09f, 4.061f),
                        new Vector3(23.968f, 1.59f, 0.673f), new Vector3(24.143f, 1.29f, 1.589f), new Vector3(20.425f, 2.44f, 0.079f), new Vector3(19.617f, 1.89f, 1.459f),
                        new Vector3(23.828f, 1.64f, 6.869f), new Vector3(20.373f, 2.54f, 7.838f), new Vector3(19.706f, 1.94f, 6.486f), new Vector3(25.594f, 1.09f, 4.13f),
                    };
                    float[] heads = { 0.62f, 0.56f, 0.8f, 0.64f, 0.66f, 0.82f, 0.64f, 0.86f, 0.62f, 0.56f, 0.8f, 0.64f, 0.66f, 0.82f, 0.64f, 0.86f };
                    const float mirrorLift = 0.032f;   // 머리 가운데 → 거울 면 (om_builder flower)
                    var parts = new Transform[centers.Length];
                    var from = new Vector3[centers.Length];
                    var to = new Vector3[centers.Length];
                    var diameters = new float[centers.Length];
                    var normals = new Vector3[centers.Length];
                    for (int i = 0; i < centers.Length; i++)
                    {
                        parts[i] = Child(root, $"INT_EvoOrbitalMirror_Head{i:00}");
                        normals[i] = ((receiver - centers[i]).normalized + Vector3.up).normalized;
                        from[i] = centers[i] + normals[i] * mirrorLift;
                        to[i] = receiver;
                        diameters[i] = heads[i] * 0.78f * 2f;
                    }
                    var beams = AddLightShafts(root, from, to, diameters, normals, 0.64f);
                    root.AddComponent<InteriorHeliostat>().Configure(parts, centers, receiver, mirrorLift, beams);
                    // 전시관: 천장 렌즈 → 해 우물 작은 해 (om_builder hall_inside)
                    var well = new GameObject("WellShaft");
                    well.transform.SetParent(root.transform, false);
                    AddLightShafts(well, new[] { new Vector3(12f, F + 3.4f - 0.07f, 4f) }, new[] { new Vector3(12f, sunY + 0.1f, 4f) }, new[] { 1.05f }, new[] { Vector3.down }, 0.4f);
                    AddOpenAir(root, new[] { new Vector3(12f, 4f, 3.85f) });   // 전시관 (om_builder CX · CZ, 벽 바깥 반지름)
                });
        }

        private static SpaceStation.Building.LightBeamFx AddLightShafts(GameObject on, Vector3[] from, Vector3[] to, float[] diameters, Vector3[] normals, float receiverDiameter, string materialPath = InteriorSetup.LightShaftPath)
        {
            var fx = on.AddComponent<SpaceStation.Building.LightBeamFx>();
            var so = new SerializedObject(fx);
            so.FindProperty("_material").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            void Vectors(string property, Vector3[] values)
            {
                var p = so.FindProperty(property);
                p.arraySize = values.Length;
                for (int i = 0; i < values.Length; i++)
                    p.GetArrayElementAtIndex(i).vector3Value = values[i];
            }
            Vectors("_from", from);
            Vectors("_to", to);
            Vectors("_mirrorNormals", normals);
            var d = so.FindProperty("_mirrorDiameters");
            d.arraySize = diameters.Length;
            for (int i = 0; i < diameters.Length; i++)
                d.GetArrayElementAtIndex(i).floatValue = diameters[i];
            so.FindProperty("_receiverDiameter").floatValue = receiverDiameter;
            so.ApplyModifiedPropertiesWithoutUndo();
            return fx;
        }

        /// <summary>우주복: 실내 원 (프리팹 로컬 x, z, 반지름) 밖 주민 — 2단계 개방형 방 공통.</summary>
        private static void AddOpenAir(GameObject root, Vector3[] indoor)
        {
            Mesh Suit(string part)
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(InteriorSetup.ResidentSuitModelPath))
                {
                    if (o is Mesh m && m.name == part)
                        return m;
                }
                Debug.LogError($"[InteriorTemplateBuilder] 우주복 조각 없음: {part}");
                return null;
            }
            root.AddComponent<InteriorOpenAir>().Configure(indoor,
                Suit("SUIT_Helmet"), Suit("SUIT_Rim"), Suit("SUIT_Pack"), Suit("SUIT_PackLight"),
                AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.SuitHelmetPath),
                AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2GoldPath),
                AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2TrimPath),
                AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2IcePath));
        }

        /// <summary>
        /// 진화 2단계 그리드 허브 "번개 정원" (3 × 2칸 + 가운데 위 1 × 2, gd_builder — 사용자 2026-11-02 시안 1 "이대로 유니티로 옮겨줘"): 2단계 첫 개방형 —
        /// 천장 없는 바깥 정원 (하늘 INT_EvoGridHub_Sky = 창과 같은 실제 바깥 화면) · 유리 난간 갑판 · 돌 마당 · 남북 돌길 (아치 · 축제 전구 · 송전 단자),
        /// 가운데 전시관 (문 없이 지나가는 압력 막 아치 Curtain · 탑으로 오르는 유리 기둥 속 빛 나선 Flow · 배전 콘솔 둘 · 벤치), 위 테슬라 탑 (코일 · 토로이드 · 에너지 구 · 도는 고리 Ring),
        /// 번개 나무 여섯 (축전지 유리 화분 · 코일 줄기 · 전구 꽃 · 코일 덤불), 번개 Bolt00 ~ 09 = <see cref="InteriorLightning"/> (번갈아 번쩍임),
        /// 귀퉁이 벤치 · 별 관측경 · 갑판 별자리. 바깥 주민 = 유리 헬멧 · 등 팩 (<see cref="InteriorOpenAir"/>).
        /// 문 자리 = 없음 (외형 옆면 연결점 없음), 아래 해치 = 바닥 칸 여섯 (가운데 줄은 전시관을 비켜 길 위로).
        /// </summary>
        private static void BuildEvoGridHub()
        {
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(new Vector3Int(0, 0, 0), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.down, -F, new Vector3(0f, 0f, -1.6f)),   // 남쪽 길 위
                new InteriorSocket(new Vector3Int(2, 0, 0), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(new Vector3Int(1, 0, 1), Vector3Int.down, -F, new Vector3(0f, 0f, 1.6f)),    // 북쪽 길 위
                new InteriorSocket(new Vector3Int(2, 0, 1), Vector3Int.down, -F, Vector3.zero),
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.86f, 0.55f);
            var cool = new Color(0.72f, 0.82f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(6.1f, F + 2.7f, 4f), 5.5f, 0.9f, white),   // 전시관 (유리 기둥 속 · 천장 가까이 두면 하얗게 번졌음, 플레이 확인 2026-11-02)
                LightSpec.Point(new Vector3(9.9f, F + 2.7f, 4f), 5.5f, 0.9f, white),
                LightSpec.Point(new Vector3(8f, F + 2.6f, -1.9f), 5.5f, 1.1f, warm),   // 남쪽 길 전구
                LightSpec.Point(new Vector3(8f, F + 2.6f, 9.9f), 5.5f, 1.1f, warm),    // 북쪽 길 전구
                LightSpec.Point(new Vector3(6.2f, F + 5.0f, 4f), 5.5f, 0.45f, warm),   // 코일 · 토로이드 아래 (축 위에 두면 아랫면이 하얗게 번졌음)
                LightSpec.Point(new Vector3(9.8f, F + 5.0f, 4f), 5.5f, 0.45f, warm),
                LightSpec.Point(new Vector3(0f, F + 2.8f, 0f), 7f, 0.7f, cool),        // 빈 갑판 (밤하늘 찬 빛)
                LightSpec.Point(new Vector3(16f, F + 2.8f, 0f), 7f, 0.7f, cool),
                LightSpec.Point(new Vector3(0f, F + 2.8f, 8f), 7f, 0.7f, cool),
                LightSpec.Point(new Vector3(16f, F + 2.8f, 8f), 7f, 0.7f, cool),
            };
            foreach (var (x, z, top) in new[] { (1.04f, 4f, 4.3f), (4.48f, -1.28f, 3.6f), (11.52f, -1.28f, 4.3f), (14.96f, 4f, 3.6f), (11.52f, 9.28f, 4.3f), (4.48f, 9.28f, 3.6f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + top - 1.3f, z), 4.5f, 0.7f, warm));   // 나무 코일 · 화단 (gd_builder TREES, 꼭대기 판 바로 밑은 하얗게 번졌음)
            const float sphereY = F + 7.75f;
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoGridHub_Flow", new Vector3(8f, 0f, 4f), Vector3.up, 120f),
                new OrbitSpec("INT_EvoGridHub_Ring", new Vector3(8f, sphereY, 4f), new Vector3(0.26f, 0.95f, 0.17f), 35f),   // 고리 자체 축 (gd_builder tower_ring)
            };
            EvoAccent("GridHub", "#FFE07A");
            // 스폰 = 북동 갑판에서 정원 전체 · 전시관 · 탑을 바라봄 (해치 · 벤치 비킴)
            Build("EvoGridHub", "Evolved/MD_Evo_GridHub", sockets, new Vector3(16.4f, F, 9.4f), -123f, lights, orbits,
                configure: root =>
                {
                    // 번개 = 바깥 모듈과 같은 LightningFx (가는 흰 심 + 옅은 푸른 빛, 갈래가 계속 바뀜 — 사용자 2026-11-02 "심이 얇아야 번개 느낌"). 탑 → 안쪽 나무 넷 · 나무 ↔ 나무 (옆 칸 나무로 가는 번개는 나무끼리만 — 위 칸 하늘 안에 머무름, gd_builder)
                    var fx = root.AddComponent<SpaceStation.Building.LightningFx>();
                    var so = new SerializedObject(fx);
                    so.FindProperty("_lineMaterial").objectReferenceValue = ModuleFxMaterials.Get("FireworkLine");
                    so.FindProperty("_sparkMaterial").objectReferenceValue = ModuleFxMaterials.Get("FireworkSpark");
                    so.FindProperty("_hub").vector3Value = new Vector3(8f, sphereY, 4f);
                    so.FindProperty("_hubRadius").floatValue = 0.72f;
                    so.FindProperty("_rimCenter").vector3Value = new Vector3(8f, F + 7.08f, 4f);   // 토로이드 테두리 (gd_builder GD_ToroidLine)
                    so.FindProperty("_rimRadius").floatValue = 1.62f;
                    so.FindProperty("_nodeRadius").floatValue = 0.25f;
                    Vector3[] nodes =
                    {
                        new Vector3(1.04f, F + 4.64f, 4f), new Vector3(4.48f, F + 3.94f, -1.28f), new Vector3(11.52f, F + 4.64f, -1.28f),
                        new Vector3(14.96f, F + 3.94f, 4f), new Vector3(11.52f, F + 4.64f, 9.28f), new Vector3(4.48f, F + 3.94f, 9.28f),
                    };
                    var pn = so.FindProperty("_nodes");
                    pn.arraySize = nodes.Length;
                    for (int i = 0; i < nodes.Length; i++)
                        pn.GetArrayElementAtIndex(i).vector3Value = nodes[i];
                    void Ints(string property, int[] values)
                    {
                        var p = so.FindProperty(property);
                        p.arraySize = values.Length;
                        for (int i = 0; i < values.Length; i++)
                            p.GetArrayElementAtIndex(i).intValue = values[i];
                    }
                    Ints("_hubTargets", new[] { 1, 2, 4, 5 });
                    Ints("_links", new[] { 0, 1, 0, 5, 3, 2, 3, 4, 1, 2, 5, 4 });
                    so.FindProperty("_coreWidth").floatValue = 0.022f;
                    so.FindProperty("_glowWidth").floatValue = 0.26f;
                    so.FindProperty("_fxScale").floatValue = 8f;
                    so.FindProperty("_roughness").floatValue = 0.14f;   // 양옆 칸 하늘 (y 3.95) 밖으로 크게 튀지 않게
                    so.ApplyModifiedPropertiesWithoutUndo();
                    var flash = new GameObject("BoltFlash").AddComponent<Light>();
                    flash.transform.SetParent(root.transform, false);
                    flash.transform.localPosition = new Vector3(8f, F + 5.2f, 4f);
                    flash.type = LightType.Point;
                    flash.shadows = LightShadows.None;
                    flash.range = 14f;
                    flash.intensity = 0f;
                    flash.color = new Color(0.75f, 0.88f, 1f);
                    root.AddComponent<InteriorLightning>().Configure(fx, flash);
                    AddOpenAir(root, new[] { new Vector3(8f, 4f, 3.45f) });   // 전시관 (gd_builder CX · CZ, 벽 바깥 반지름)
                });
        }

        /// <summary>
        /// 진화 1단계 보안청 (ㄱ자 3칸, se_builder — 사용자 2026-10-31 시안 1 "이대로 유니티로 옮겨줘"): 행정과 무력을 한 청사에 —
        /// 남 홀 = 민원 · 치안 창구 (남쪽 문 안 보안 검색 아치 · 로비 바닥 별 배지, 창구 셋 = 유리 가림막 · 번호판 · 직원 의자 — 행정 관제소와 같은
        /// <see cref="InteriorQueueBoard"/> (창구 줄이 x축과 나란 → 틀 90°), 창구를 보는 대기 의자 두 줄 · 가운데 통로 끝 발권기 · 남 벽 별 배지,
        /// 지붕 둥근 창 = 유리 돔 아래 도는 순찰 드론 Drone, 동쪽 둥근 귀퉁이 순찰 장비함 · 드론 충전대 (빈 패드 = 순찰 나간 드론)),
        /// 북 탑 = 감시 · 요격 관제 (레이더 상황판 = 실제 운석 예보 <see cref="InteriorRadar"/> · 쓸기 빛 Sweep · 뒤 휜 화면 벽 가운데 예보 화면 · 위 경광등 Beacon,
        /// 서 벽 실제 정거장 지도 <see cref="InteriorStationMap"/>, 천장 포탑 축 (바둑판 띠) 속 포탑 받침 Turret · 쌍포 약실 BreechL · R —
        /// <see cref="InteriorTurretAim"/>가 예보 방향으로 돌리고 낙하 중엔 쏨, 북쪽 둥근 귀퉁이 관제 책상 둘).
        /// 문 자리 = 외형 면이 있는 바깥 면 8, 위 · 아래 해치 = 칸마다 (0번 칸 = 남서 귀퉁이, 탑 칸 위 = 상황판 서쪽 천장 · 아래 = 문 길 가운데).
        /// </summary>
        private static void BuildEvoSecurity()
        {
            const float ceiling = F + 3.8f;
            var c00 = new Vector3Int(0, 0, 0);
            var c10 = new Vector3Int(1, 0, 0);
            var c01 = new Vector3Int(0, 0, 1);
            var back = new Vector3Int(0, 0, -1);
            var fwd = new Vector3Int(0, 0, 1);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(c00, Vector3Int.left, Socket),
                new InteriorSocket(c00, back, Socket),
                new InteriorSocket(c10, Vector3Int.right, Socket),
                new InteriorSocket(c10, back, Socket),
                new InteriorSocket(c10, fwd, Socket),
                new InteriorSocket(c01, Vector3Int.left, Socket),
                new InteriorSocket(c01, Vector3Int.right, Socket),
                new InteriorSocket(c01, fwd, Socket),
                new InteriorSocket(c00, Vector3Int.down, -F, new Vector3(-1.9f, 0f, -1.9f)),     // 남서 귀퉁이 (검색 아치 발판 · 바닥 별 배지 비킴 — −1.8이면 발판이 2cm 걸림)
                new InteriorSocket(c10, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(c01, Vector3Int.down, -F, Vector3.zero),                      // 탑 문 길 가운데 (포탑 축 아래)
                new InteriorSocket(c00, Vector3Int.up, ceiling, new Vector3(-1.9f, 0f, -1.9f)),
                new InteriorSocket(c10, Vector3Int.up, ceiling, Vector3.zero),
                new InteriorSocket(c01, Vector3Int.up, ceiling, new Vector3(-2.0f, 0f, -3.0f)), // 상황판 서쪽 (포탑 축 · 빛 띠 비킴)
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var cool = new Color(0.78f, 0.86f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-0.8f, F + 3.0f, 0.4f), 6f, 1.2f, white),    // 로비
                LightSpec.Point(new Vector3(4.0f, F + 3.0f, 2.3f), 6f, 1.2f, white),     // 창구 직원 쪽
                LightSpec.Point(new Vector3(4.0f, F + 3.0f, -1.9f), 6f, 1.2f, white),    // 대기 의자
                LightSpec.Point(new Vector3(4.0f, F + 3.4f, -0.3f), 3.5f, 0.6f, cool),   // 둥근 천창 · 순찰 드론
                LightSpec.Point(new Vector3(9.6f, F + 3.0f, 0f), 6f, 1.3f, white),       // 홀 동쪽
                LightSpec.Point(new Vector3(0f, F + 3.0f, 4.0f), 6f, 1.2f, white),       // 탑 남쪽
                LightSpec.Point(new Vector3(0f, F + 1.9f, 5.4f), 2.5f, 0.5f, cool),      // 레이더 상황판
                LightSpec.Point(new Vector3(-2.2f, F + 2.5f, 4.9f), 3f, 0.6f, cool),     // 정거장 지도
                LightSpec.Point(new Vector3(0f, F + 3.0f, 9.6f), 6f, 1.2f, white),       // 탑 북쪽
                LightSpec.Point(new Vector3(0f, F + 4.0f, 9.0f), 2.5f, 0.5f, white),     // 포탑 축 속
                LightSpec.Point(new Vector3(0f, F + 2.2f, 10.4f), 4f, 0.7f, cool),       // 귀퉁이 관제 책상
            };
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoSecurity_Drone", new Vector3(4.0f, 0f, -0.3f), Vector3.up, 18f),   // se_builder HC (+ = +x에서 −z 쪽 = 드론 앞)
                new OrbitSpec("INT_EvoSecurity_Sweep", new Vector3(0f, 0f, 5.2f), Vector3.up, 60f),     // RC, 방어 포대와 같은 쓸기 빛
            };
            EvoAccent("SecurityBureau", "#7FA8FF");
            // 스폰 = 남쪽 문 (x 0) 안, 검색 아치 너머 로비 별 배지 · 창구를 바라봄
            Build("EvoSecurity", "Evolved/MD_Evo_SecurityBureau", sockets, new Vector3(0f, F, -2.45f), 15f, lights, orbits,
                configure: root =>
                {
                    var r = root.transform;
                    Transform Pivot(string pivotName, Transform parent, Vector3 at, string part)
                    {
                        var p = new GameObject(pivotName).transform;
                        p.SetParent(parent, false);
                        p.position = r.TransformPoint(at);
                        p.rotation = r.rotation;
                        var mesh = Child(root, part);
                        if (mesh != null)
                            mesh.SetParent(p, true);
                        return p;
                    }
                    // 레이더 상황판 (se_builder RC (0, 5.2) · 화면 반지름 0.86) = 실제 운석 예보, 예보 글 = 휜 화면 벽 가운데 짙은 화면 (반지름 1.72, −z를 봄)
                    root.AddComponent<InteriorRadar>().Configure(new Vector3(0f, F + 0.958f, 5.2f), 0.78f,
                        Child(root, "INT_EvoSecurity_Sweep")?.GetComponent<InteriorOrbit>(),
                        Child(root, "INT_EvoSecurity_Beacon")?.GetComponent<Renderer>(),
                        new Vector3(0f, F + 1.55f, 6.917f), Vector3.back, Vector3.up, new Vector2(0.7f, 0.5f));
                    // 탑 서 벽 정거장 지도 (se_builder MAP_C · 흰 테 안쪽 2.28 × 1.18)
                    root.AddComponent<InteriorStationMap>().Configure(new Vector3(-3.149f, F + 1.75f, 4.9f), Vector3.right, Vector3.up, new Vector2(2.24f, 1.14f),
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ScreenPath),
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ScreenGridPath));
                    // 창구 셋 · 발권기 (se_builder WIN_X · STAFF_Z · STAND_Z · KIOSK): 창구 줄이 x축과 나란 → 틀 90° (틀 좌표 = 프리팹 로컬을 −90° 돌린 것:
                    // 틀 x = −z, 틀 z = x). 대기 의자 앞 → 줄 앞 (x로) → 가운데 통로 x 4 (z로) → 창구 앞 통로 z −0.05 (x로) → 창구 앞 z 0.55 / 통로 끝 발권기 앞 z −2.05
                    var frame = Quaternion.Euler(0f, -90f, 0f);
                    Vector3 Fr(float x, float y, float z) => frame * new Vector3(x, y, z);
                    Vector4 Seg(float x0, float z0, float x1, float z1) => new Vector4(-z0, x0, -z1, x1);
                    InteriorQueueBoard.Window Window(float x)
                    {
                        var stand = Fr(x, F, 0.55f);
                        return new InteriorQueueBoard.Window
                        {
                            Board = new InteriorQueueBoard.Panel { Center = new Vector3(x, F + 2.5f, 0.829f), Normal = Vector3.back, Up = Vector3.up, Width = 0.7f, Height = 0.16f },
                            StaffSeat = Fr(x, F + 0.49f, 1.95f),
                            Counter = new Vector4(stand.x, stand.y, stand.z, 0f - 90f),   // 북 (창구)을 봄
                        };
                    }
                    var ticketStand = Fr(4.0f, F, -2.05f);
                    var queue = root.AddComponent<InteriorQueueBoard>();
                    queue.ConfigureFrame(90f);
                    queue.Configure(new[] { Window(2.2f), Window(4.0f), Window(5.8f) },
                        new InteriorQueueBoard.Panel { Center = new Vector3(4.0f, F + 1.19f, -2.569f), Normal = Vector3.forward, Up = Vector3.up, Width = 0.32f, Height = 0.18f },
                        new Vector4(ticketStand.x, ticketStand.y, ticketStand.z, 180f - 90f), 4.0f, 0.05f, 4.0f, ticketStand);   // 발권기를 봄 (남)
                    // 민원인 통로 (보급 상자 · 부탁 물건을 두지 않음): 의자 줄 앞 둘 · 가운데 통로 (발권기까지) · 창구 앞 통로 · 창구 앞 셋
                    queue.ConfigureLanes(new[]
                    {
                        Seg(1.7f, -0.45f, 6.3f, -0.45f), Seg(1.7f, -1.65f, 6.3f, -1.65f), Seg(4.0f, -2.05f, 4.0f, -0.05f),
                        Seg(2.2f, -0.05f, 5.8f, -0.05f), Seg(2.2f, -0.05f, 2.2f, 0.55f), Seg(4.0f, -0.05f, 4.0f, 0.55f), Seg(5.8f, -0.05f, 5.8f, 0.55f),
                    }, 0.35f);
                    // 천장 포탑 축 (se_builder TC (0, 9) · 약실 x ±0.42, 높이 천장 + 0.22) — 받침 = 위 축, 약실 = 받침 자식 (−z로 밀림)
                    var turret = Pivot("TurretPivot", r, new Vector3(0f, ceiling, 9f), "INT_EvoSecurity_Turret");
                    var breechL = Pivot("BreechLPivot", turret, new Vector3(-0.42f, ceiling + 0.22f, 9f), "INT_EvoSecurity_BreechL");
                    var breechR = Pivot("BreechRPivot", turret, new Vector3(0.42f, ceiling + 0.22f, 9f), "INT_EvoSecurity_BreechR");
                    root.AddComponent<InteriorTurretAim>().Configure(turret, new[] { breechL, breechR }, 0.14f);
                });
        }

        /// <summary>
        /// 진화 1단계 자동화 센터 (ㄱ자 3칸, ac_builder — 사용자 2026-10-31 시안 1 "이대로 유니티로 옮겨줘"): 기록 → 학습 → 실습 —
        /// 모서리 = 학습 코어 (천장 구멍 유리 돔 · 매달린 코어 구 · 도는 궤도 고리 RingA · B · 코어 탁자 위 홀로 팔 HoloTurret · HoloFore · 연구 책상 둘),
        /// +x 칸 = 실습 정비 베이 (천창 · 둥근 패드 위 고장 판 · 로봇 팔 Turret · Fore가 금 두 줄을 번갈아 용접 — <see cref="InteriorMirrorArm"/>가 홀로 팔도 같이,
        /// 정비 콘솔 · 예비 판 선반 · 정비 로봇 셋 · 부품 수레), +z 칸 = 정비 기록 연구실 (기록 서버장 · 기록 투영대 Logs가 돎 · 귀퉁이 연구 책상 둘),
        /// 천장 = 기록이 흐르는 유리 관 둘 (속 나선 FlowA = 베이 → 코어, FlowB = 코어 → 서버).
        /// 문 자리 = 외형 면이 있는 바깥 면 8 (ㄱ자 안쪽 모서리 두 면 포함), 위 · 아래 해치 = 칸마다 (0번 칸은 코어 · 돔 비킴).
        /// </summary>
        private static void BuildEvoAutomation()
        {
            const float ceiling = F + 3.8f;
            var c00 = new Vector3Int(0, 0, 0);
            var c10 = new Vector3Int(1, 0, 0);
            var c01 = new Vector3Int(0, 0, 1);
            var back = new Vector3Int(0, 0, -1);
            var fwd = new Vector3Int(0, 0, 1);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(c00, Vector3Int.left, Socket),
                new InteriorSocket(c00, back, Socket),
                new InteriorSocket(c10, Vector3Int.right, Socket),
                new InteriorSocket(c10, back, Socket),
                new InteriorSocket(c10, fwd, Socket),
                new InteriorSocket(c01, Vector3Int.left, Socket),
                new InteriorSocket(c01, Vector3Int.right, Socket),
                new InteriorSocket(c01, fwd, Socket),
                new InteriorSocket(c00, Vector3Int.down, -F, new Vector3(2.2f, 0f, -2.2f)),   // 코어 · 바닥 고리 비킴
                new InteriorSocket(c10, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(c01, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(c00, Vector3Int.up, ceiling, new Vector3(2.4f, 0f, -2.4f)), // 돔 아래 테 (반지름 2.3) 밖
                new InteriorSocket(c10, Vector3Int.up, ceiling, Vector3.zero),
                new InteriorSocket(c01, Vector3Int.up, ceiling, Vector3.zero),
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.85f, 0.65f);
            var cool = new Color(0.75f, 0.88f, 1f);
            var lavender = new Color(0.86f, 0.8f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(0f, F + 3.6f, 0f), 5f, 1.0f, white),        // 돔 아래 (꼭대기 테에서 멀리 — 자재 재생 플랜트와 같음)
                LightSpec.Point(new Vector3(0f, F + 2.0f, 0f), 3f, 0.6f, lavender),     // 코어 빛
                LightSpec.Point(new Vector3(2.0f, F + 3.0f, 2.0f), 6f, 1.2f, white),    // 세 칸이 만나는 곳
                LightSpec.Point(new Vector3(5.3f, F + 2.6f, -1.8f), 4f, 1.0f, warm),    // 실습대
                LightSpec.Point(new Vector3(9.0f, F + 3.0f, 0f), 6f, 1.3f, white),      // 베이
                LightSpec.Point(new Vector3(5.5f, F + 2.4f, 2.4f), 3.5f, 0.9f, cool),   // 정비 콘솔
                LightSpec.Point(new Vector3(10.0f, F + 2.2f, -1.6f), 3f, 0.7f, cool),   // 정비 로봇
                LightSpec.Point(new Vector3(0f, F + 3.0f, 4.6f), 6f, 1.3f, white),      // 연구실
                LightSpec.Point(new Vector3(0f, F + 3.0f, 9.0f), 6f, 1.2f, white),
                LightSpec.Point(new Vector3(-1.9f, F + 2.4f, 4.9f), 3.5f, 0.8f, cool),  // 서버장
                LightSpec.Point(new Vector3(2.0f, F + 2.4f, 5.3f), 3.5f, 0.8f, cool),
            };
            const float coreY = F + 2.6f, flowY = ceiling - 0.32f;
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoAutomation_RingA", new Vector3(0f, coreY, 0f), new Vector3(0f, 0.8829f, 0.4695f), 40f),   // 고리 자체 축 (ac_builder core_rings)
                new OrbitSpec("INT_EvoAutomation_RingB", new Vector3(0f, coreY, 0f), new Vector3(0.6428f, 0.766f, 0f), -55f),
                new OrbitSpec("INT_EvoAutomation_FlowA", new Vector3(0f, flowY, -0.45f), Vector3.right, 120f),     // 베이 → 코어 (−x)
                new OrbitSpec("INT_EvoAutomation_FlowB", new Vector3(-2.2f, flowY, 0f), Vector3.forward, 120f),    // 코어 → 서버 (+z)
                new OrbitSpec("INT_EvoAutomation_Logs", new Vector3(0f, 0f, 5.3f), Vector3.up, 12f),               // ac_builder LOGS
            };
            EvoAccent("AutomationCenter", "#C8B0FF");
            // 스폰 = 모서리 칸 남쪽 문 안에서 학습 코어 · 유리 돔을 바라봄 (바닥 해치 비킴)
            Build("EvoAutomation", "Evolved/MD_Evo_AutomationCenter", sockets, new Vector3(-0.9f, F, -2.3f), 30f, lights, orbits,
                configure: root =>
                {
                    // ac_builder 값: 팔 기둥 ARM (4.1, −1.8) · 기둥 위 TURRET_Y · 위 팔 높이 ARM_Y · 팔꿈치 ELBOW_R · 토치 끝 TIP · 금 SEAM_YAW · 홀로 팔 HOLO_BASE · HOLO_S
                    var r = root.transform;
                    Transform Pivot(string pivotName, Transform parent, Vector3 at, string part)
                    {
                        var p = new GameObject(pivotName).transform;
                        p.SetParent(parent, false);
                        p.position = r.TransformPoint(at);
                        p.rotation = r.rotation;
                        var mesh = Child(root, part);
                        if (mesh != null)
                            mesh.SetParent(p, true);
                        return p;
                    }
                    const float turretY = F + 1.18f, armY = F + 1.36f, holoS = 0.38f;
                    var armBase = new Vector3(4.1f, turretY, -1.8f);
                    var elbow = new Vector3(4.1f + 1.3f, armY, -1.8f);
                    var tip = new Vector3(4.1f + 1.565f, F + 0.822f, -1.8f);
                    var holoBase = new Vector3(0f, F + 1.12f, 0f);
                    Vector3 ToHolo(Vector3 p) => holoBase + (p - armBase) * holoS;
                    var turret = Pivot("ArmTurretPivot", r, armBase, "INT_EvoAutomation_Turret");
                    var fore = Pivot("ArmForePivot", turret, elbow, "INT_EvoAutomation_Fore");
                    var holoTurret = Pivot("HoloTurretPivot", r, holoBase, "INT_EvoAutomation_HoloTurret");
                    var holoFore = Pivot("HoloForePivot", holoTurret, ToHolo(elbow), "INT_EvoAutomation_HoloFore");
                    root.AddComponent<InteriorMirrorArm>().Configure(turret, fore, holoTurret, holoFore,
                        fore.InverseTransformPoint(r.TransformPoint(tip)), fore.InverseTransformDirection(r.TransformDirection(Vector3.up)),
                        12f, 28f, AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.SparkPath));
                });
        }

        /// <summary>
        /// 진화 1단계 조선소 (3 × 2칸, sy_builder — 사용자 2026-10-31 시안 1 "이대로 유니티로 옮겨줘"): 한 홀 세 구역 —
        /// 가운데 = 건조 도크 (z 방향 유리 아치 천장 아래 짓는 화물선: 뒤 반 = 판 붙인 흰 선체 · 엔진 셋 · 날개 · 별 배지, 앞 반 = 갈비뼈 골조,
        /// 레일에 매달린 용접 갠트리 Gantry · 토치 Torch0 · 1이 판 이음선마다 옮겨 용접 — <see cref="InteriorShipyard"/>), 북 벽 = 출항 게이트
        /// (도킹 앞면 · 연결 없음) · 위 = 실제 화물선 시계 <see cref="InteriorCargoBoard"/>.
        /// 서 = 선체 판 공방 (용광로 → 롤러 길 → 3롤 굽힘기 Roll0 ~ 2가 돎 · 판 받침대 · 완성 판 수레 · 설계 홀로 탁자 Holo가 돎 ·
        /// 남 창 아래 판 조립대 = 로봇 팔 토치 Torch2가 이음선 용접 <see cref="InteriorWelder"/>).
        /// 동 = 출항 관제 콘솔 (상태 화면 둘) · 수리 드론 거치대 · 검사 아치 속 도는 드론 Drone · 엔진 점검 지그 (Engine이 제 축으로 돎) · 조종실 모듈 대차.
        /// 문 자리 = 서 · 동 · 남 바깥 면 7, 위 · 아래 해치 = 칸마다 (가운데 줄은 아치 · 배 비킴).
        /// </summary>
        private static void BuildEvoShipyard()
        {
            const float ceiling = F + 3.8f;
            var c00 = new Vector3Int(0, 0, 0);
            var c10 = new Vector3Int(1, 0, 0);
            var c20 = new Vector3Int(2, 0, 0);
            var c01 = new Vector3Int(0, 0, 1);
            var c11 = new Vector3Int(1, 0, 1);
            var c21 = new Vector3Int(2, 0, 1);
            var back = new Vector3Int(0, 0, -1);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(c00, Vector3Int.left, Socket),
                new InteriorSocket(c01, Vector3Int.left, Socket),
                new InteriorSocket(c20, Vector3Int.right, Socket),
                new InteriorSocket(c21, Vector3Int.right, Socket),
                new InteriorSocket(c00, back, Socket),
                new InteriorSocket(c10, back, Socket),
                new InteriorSocket(c20, back, Socket),
                new InteriorSocket(c00, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(c01, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(c10, Vector3Int.down, -F, new Vector3(-2.8f, 0f, 0f)),    // 도크 서쪽 통로 (5.2, 0) — 배 · 받침 비킴
                new InteriorSocket(c11, Vector3Int.down, -F, new Vector3(3.0f, 0f, 0f)),     // 도크 동쪽 통로 (11.0, 8)
                new InteriorSocket(c20, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(c21, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(c00, Vector3Int.up, ceiling, Vector3.zero),
                new InteriorSocket(c01, Vector3Int.up, ceiling, Vector3.zero),
                new InteriorSocket(c10, Vector3Int.up, ceiling, new Vector3(-3.5f, 0f, 0f)),   // 아치 가장자리 보 (x 5.36) 밖 (4.5, 0)
                new InteriorSocket(c11, Vector3Int.up, ceiling, new Vector3(3.5f, 0f, 0f)),    // (11.5, 8)
                new InteriorSocket(c20, Vector3Int.up, ceiling, Vector3.zero),
                new InteriorSocket(c21, Vector3Int.up, ceiling, Vector3.zero),
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.85f, 0.65f);
            var cool = new Color(0.75f, 0.88f, 1f);
            var lights = new List<LightSpec>();
            foreach (float z in new[] { 0.2f, 4.4f, 8.6f })
                lights.Add(LightSpec.Point(new Vector3(8f, F + 4.4f, z), 7f, 1.2f, white));      // 유리 아치 아래 도크
            foreach (var (x, z) in new[] { (0f, 1.4f), (0f, 7f), (16f, 1.4f), (16f, 7f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 3.0f, z), 6f, 1.35f, white));      // 공방 · 관제 쪽 평평한 천장
            lights.Add(LightSpec.Point(new Vector3(1.2f, F + 2.6f, 9.0f), 4f, 1.0f, warm));       // 용광로 · 굽힘기
            lights.Add(LightSpec.Point(new Vector3(2.9f, F + 2.4f, -1.6f), 3.5f, 0.9f, warm));    // 판 조립대
            lights.Add(LightSpec.Point(new Vector3(15f, F + 2.4f, 9.4f), 3.5f, 0.9f, cool));      // 관제 콘솔
            lights.Add(LightSpec.Point(new Vector3(13.4f, F + 2.4f, 3.0f), 4f, 0.9f, cool));      // 드론 검사 · 엔진 점검
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoShipyard_Roll0", new Vector3(2.8f, F + 1.035f, 0f), Vector3.forward, 40f),     // 위 롤러와 아래 롤러는 반대로 (판을 함께 밀어냄)
                new OrbitSpec("INT_EvoShipyard_Roll1", new Vector3(2.3f, F + 0.8181f, 0f), Vector3.forward, -48f),
                new OrbitSpec("INT_EvoShipyard_Roll2", new Vector3(3.3f, F + 0.8181f, 0f), Vector3.forward, -48f),
                new OrbitSpec("INT_EvoShipyard_Holo", new Vector3(1.5f, 0f, 4.0f), Vector3.up, 15f),    // sy_builder HOLO
                new OrbitSpec("INT_EvoShipyard_Drone", new Vector3(14.9f, 0f, 4.0f), Vector3.up, 20f),  // sy_builder STAND
                new OrbitSpec("INT_EvoShipyard_Engine", new Vector3(12.5f, F + 0.95f, 1.7f), Vector3.right, 12f),
            };
            EvoAccent("Shipyard", "#90C8B0");
            // 스폰 = 남서 문 (0번 칸) 안에서 판 조립대 · 공방 너머 도크를 바라봄 (바닥 해치 칸 중심 비킴)
            Build("EvoShipyard", "Evolved/MD_Evo_Shipyard", sockets, new Vector3(0.6f, F, -2.2f), 40f, lights, orbits,
                configure: root =>
                {
                    // sy_builder 값: 갠트리 모델 자세 z 2.6 · 판 이음선 SEAMS · 토치 끝 · 법선 TORCHES (선체 각 55°)
                    var r = root.transform;
                    Transform Pivot(string pivotName, Transform parent, Vector3 at, string part)
                    {
                        var p = new GameObject(pivotName).transform;
                        p.SetParent(parent, false);
                        p.position = r.TransformPoint(at);
                        p.rotation = r.rotation;
                        var mesh = Child(root, part);
                        if (mesh != null)
                            mesh.SetParent(p, true);
                        return p;
                    }
                    var spark = AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.SparkPath);
                    var gantry = Pivot("GantryPivot", r, new Vector3(8f, ceiling - 0.34f, 2.6f), "INT_EvoShipyard_Gantry");
                    var torchTips = new[] { new Vector3(6.9761f, F + 2.4523f, 2.6f), new Vector3(9.0239f, F + 2.4523f, 2.6f) };
                    var torchNormals = new[] { new Vector3(-0.7681f, 0.6403f, 0f), new Vector3(0.7681f, 0.6403f, 0f) };
                    var torches = new Transform[2];
                    var tips = new Vector3[2];
                    var normals = new Vector3[2];
                    for (int i = 0; i < 2; i++)
                    {
                        torches[i] = Child(root, "INT_EvoShipyard_Torch" + i);
                        if (torches[i] != null)
                            torches[i].SetParent(gantry, true);
                        tips[i] = torches[i] != null ? torches[i].InverseTransformPoint(r.TransformPoint(torchTips[i])) : torchTips[i];
                        normals[i] = torches[i] != null ? torches[i].InverseTransformDirection(r.TransformDirection(torchNormals[i])) : torchNormals[i];
                    }
                    root.AddComponent<InteriorShipyard>().Configure(gantry, 2.6f, new[] { 1.4f, 2.6f, 3.8f }, torches, tips, normals, spark);
                    // 판 조립대 로봇 팔 토치 (이음선 꼭대기, 위를 향한 법선)
                    var jigTorch = Child(root, "INT_EvoShipyard_Torch2");
                    var jigTip = new Vector3(2.9f, F + 1.02f, -2.1f);
                    root.AddComponent<InteriorWelder>().Configure(new[] { jigTorch },
                        new[] { jigTorch != null ? jigTorch.InverseTransformPoint(r.TransformPoint(jigTip)) : jigTip },
                        new[] { jigTorch != null ? jigTorch.InverseTransformDirection(r.TransformDirection(Vector3.up)) : Vector3.up },
                        spark);
                    // 출항 게이트 위 화물선 시계 (sy_builder Y_Board 앞면 z 11.105, 가운데 F + 4.35, 2.16 × 0.62)
                    root.AddComponent<InteriorCargoBoard>().Configure(new InteriorArrivalBoard.Panel
                    {
                        Center = new Vector3(8f, F + 4.35f, 11.1f), Normal = Vector3.back, Up = Vector3.up, Width = 2.1f, Height = 0.5f,
                    });
                });
        }

        /// <summary>
        /// 진화 1단계 대형 창고 (2 × 2칸, bs_builder — 사용자 2026-10-31 시안 1 "이대로 유니티로 옮겨줘"): 칸막이 없는 한 공간 —
        /// 가운데 = 양면 3단 높은 선반 세 줄 위 한 단 높인 고창 천장 (네 면 고창 = 창살 촘촘한 외형의 철창 · 가운데 천창 · 외형의 X자 아치 리브 둘),
        /// 고창 레일의 다리 크레인이 윗단 상자 셋을 빈 윗단 칸으로 옮김 (<see cref="InteriorStorageCrane"/>).
        /// 네 귀 = 외형 둥근 탑 자리의 유리 원통 회전 보관탑 (Carousel0 ~ 3), 남 = 크레인 조작 책상 · 북 = 재고 카운터 · 서 = 입고 작업대 · 동 = 출고 대기 팔레트.
        /// 문 자리 = 칸마다 바깥 두 면 (8), 위 · 아래 해치 = 칸 중심 (천장 F + 3.8, 고창 밖).
        /// </summary>
        private static void BuildEvoStorage()
        {
            const float ceiling = F + 3.8f;
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                for (int z = 0; z < 2; z++)
                {
                    var cell = new Vector3Int(x, 0, z);
                    sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                    sockets.Add(new InteriorSocket(cell, z == 0 ? new Vector3Int(0, 0, -1) : new Vector3Int(0, 0, 1), Socket));
                    sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, Vector3.zero));
                    sockets.Add(new InteriorSocket(cell, Vector3Int.up, ceiling, Vector3.zero));
                }
            }
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.85f, 0.65f);
            var cool = new Color(0.75f, 0.88f, 1f);
            var lights = new List<LightSpec>();
            foreach (var (x, z) in new[] { (0f, 0f), (8f, 0f), (0f, 8f), (8f, 8f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 3.0f, z), 6f, 1.35f, white));       // 칸마다 (고창 밖 평평한 천장 — F + 3.2 · 1.4는 천장에 하얀 번짐, 플레이 확인 2026-10-31)
            lights.Add(LightSpec.Point(new Vector3(4f, F + 3.6f, 4f), 5.5f, 1.0f, white));        // 고창 아래 (크레인 · 윗단 상자 — F + 3.9는 가운데 고리가 등처럼 하얗게 번짐)
            lights.Add(LightSpec.Point(new Vector3(4f, F + 2.5f, 2.8f), 4f, 0.9f, cool));         // 선반 사이 통로 둘
            lights.Add(LightSpec.Point(new Vector3(4f, F + 2.5f, 5.2f), 4f, 0.9f, cool));
            lights.Add(LightSpec.Point(new Vector3(4f, F + 2.4f, -2.2f), 3.5f, 0.9f, white));     // 크레인 조작 책상 (남)
            lights.Add(LightSpec.Point(new Vector3(4f, F + 2.4f, 10.2f), 3.5f, 0.9f, white));     // 재고 카운터 (북)
            lights.Add(LightSpec.Point(new Vector3(-2.2f, F + 2.4f, 4f), 3.5f, 0.9f, warm));      // 입고 작업대 (서)
            lights.Add(LightSpec.Point(new Vector3(10.2f, F + 2.4f, 4f), 3.5f, 0.9f, warm));      // 출고 팔레트 (동)
            var orbits = new List<OrbitSpec>();
            var towers = new[] { (-2.1f, -2.1f), (10.1f, -2.1f), (-2.1f, 10.1f), (10.1f, 10.1f) };   // bs_builder TOWERS
            for (int k = 0; k < towers.Length; k++)
                orbits.Add(new OrbitSpec($"INT_EvoStorage_Carousel{k}", new Vector3(towers[k].Item1, 0f, towers[k].Item2), Vector3.up, k % 2 == 0 ? 10f : -10f));
            EvoAccent("BigStorage", "#B0C0A0");
            // 스폰 = 남쪽 문 (0번 칸) 안에서 선반 · 고창을 바라봄 (바닥 해치 칸 중심 · 보관탑 비킴)
            Build("EvoStorage", "Evolved/MD_Evo_BigStorage", sockets, new Vector3(0.6f, F, -2.2f), 30f, lights, orbits,
                configure: root =>
                {
                    // bs_builder 값: 윗단 DECK · 칸 x TOP_X · 줄 z ROWS · 옮기는 상자 TOP_MOVE · 빈 칸 TOP_EMPTY · 크레인 RAIL_Y · GRIP_REST · CRANE_X_RANGE
                    const float deck = F + 3.0f, gripRest = F + 4.0f, cableTop = F + 4.08f;
                    var r = root.transform;
                    Transform Pivot(string pivotName, Transform parent, Vector3 at, string part)
                    {
                        var p = new GameObject(pivotName).transform;
                        p.SetParent(parent, false);
                        p.position = r.TransformPoint(at);
                        p.rotation = r.rotation;
                        var mesh = Child(root, part);
                        if (mesh != null)
                            mesh.SetParent(p, true);
                        return p;
                    }
                    float[] topX = { 1.8f, 2.7f, 3.6f, 4.5f, 5.4f, 6.3f };
                    float[] rowZ = { 1.6f, 4.0f, 6.4f };
                    var cells = new[] { (0, 1), (1, 4), (2, 2), (0, 3), (1, 0), (2, 5), (1, 2) };   // 앞 셋 = 상자가 든 칸 (TOP_MOVE), 나머지 = 빈 칸 (TOP_EMPTY)
                    var slots = new Vector3[cells.Length];
                    for (int s = 0; s < cells.Length; s++)
                        slots[s] = new Vector3(topX[cells[s].Item2], deck, rowZ[cells[s].Item1]);
                    var crates = new Transform[3];
                    for (int i = 0; i < 3; i++)
                        crates[i] = Pivot("TopCrate" + i + "Pivot", r, slots[i], "INT_EvoStorage_Top" + i);
                    var bridge = Pivot("CraneBridgePivot", r, new Vector3(4f, F + 4.45f, 4f), "INT_EvoStorage_Bridge");
                    var trolley = Pivot("CraneTrolleyPivot", bridge, new Vector3(4f, F + 4.1f, 4f), "INT_EvoStorage_Trolley");
                    var cable = Pivot("CraneCablePivot", trolley, new Vector3(4f, cableTop, 4f), "INT_EvoStorage_Cable");
                    var grip = Pivot("CraneGripPivot", trolley, new Vector3(4f, gripRest, 4f), "INT_EvoStorage_Grip");
                    root.AddComponent<InteriorStorageCrane>().Configure(new InteriorStorageCrane.Rig
                    {
                        Bridge = bridge, Trolley = trolley, Cable = cable, Grip = grip,
                        RestX = 4f, RestZ = 4f, MinX = 1.6f, MaxX = 6.4f,
                        GripRestY = gripRest, GripFrameHeight = 0.06f, CableTopY = cableTop, CableLength = 1f, CrateHeight = 0.45f,
                    }, crates, slots);
                });
        }

        /// <summary>
        /// 진화 1단계 물류 허브 (3칸 일자 도킹, lh_builder — 사용자 2026-10-31 시안 1 "이대로 유니티로 옮겨줘"): 북 벽 = 화물선이 붙는 도킹 면 (연결점 없음) —
        /// 파노라마 창 · 가운데 화물 하역문 (경고 줄무늬 틀, 위 = 실제 화물선 시계 <see cref="InteriorCargoBoard"/>) · 안쪽 스캔 아치 · 바닥 화살표 · 팔레트 상자.
        /// 남 벽 문 사이 = 자동 창고 칸 둘 (3단 선반, 스태커 크레인 StackerA · B가 나르는 상자 둘을 비워 둔 칸 셋 사이로 넣고 뺌 — <see cref="InteriorLogistics"/>),
        /// 머리 위 셔틀 레일 (셔틀 PodA · B가 하역문 쪽 덮개 속에서 상자를 받아 레일 끝 덮개 속에 내려놓음). 서쪽 끝 배차 콘솔 · 동쪽 끝 포장대 · 저울 · 카트.
        /// 문 자리 = 연결점 있는 면 5 (양 끝 + 칸마다 남), 위 · 아래 해치 = 칸 중심.
        /// </summary>
        private static void BuildEvoLogistics()
        {
            const float ceiling = F + 3.8f;
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 3; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                if (x == 0)
                    sockets.Add(new InteriorSocket(cell, Vector3Int.left, Socket));
                if (x == 2)
                    sockets.Add(new InteriorSocket(cell, Vector3Int.right, Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, Vector3.zero));
                sockets.Add(new InteriorSocket(cell, Vector3Int.up, ceiling, Vector3.zero));
            }
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.85f, 0.65f);
            var cool = new Color(0.75f, 0.88f, 1f);
            var lights = new List<LightSpec>();
            foreach (float x in new[] { -1.6f, 2.4f, 6.0f, 10.0f, 13.6f, 17.6f })
                lights.Add(LightSpec.Point(new Vector3(x, F + 3.0f, 0.2f), 6f, 1.35f, white));      // 홀 가운데 줄 (F + 3.2 · 1.6은 천장에 하얀 번짐)
            lights.Add(LightSpec.Point(new Vector3(4f, F + 2.6f, -1.7f), 4f, 1.0f, cool));          // 창고 칸
            lights.Add(LightSpec.Point(new Vector3(12f, F + 2.6f, -1.7f), 4f, 1.0f, cool));
            lights.Add(LightSpec.Point(new Vector3(8f, F + 2.4f, 2.4f), 4.5f, 1.0f, warm));         // 하역문 · 스캔 아치
            lights.Add(LightSpec.Point(new Vector3(-0.5f, F + 2.4f, 2.0f), 3.5f, 0.8f, cool));      // 배차 콘솔
            lights.Add(LightSpec.Point(new Vector3(16.8f, F + 2.4f, 2.0f), 3.5f, 0.8f, warm));      // 포장대
            EvoAccent("LogisticsHub", "#8FE0B0");   // RecipeSetup 물류 허브 색 (#7CD957은 기본 화물 터미널 색 — 처음에 잘못 씀)
            // 스폰 = 가운데 칸 남쪽 문 안에서 스캔 아치 · 하역문을 바라봄 (바닥 해치 칸 중심 비킴)
            Build("EvoLogistics", "Evolved/MD_Evo_LogisticsHub", sockets, new Vector3(8f, F, -2.3f), 0f, lights,
                configure: root =>
                {
                    // lh_builder 값: 선반 윗면 LEVELS · 넣고 빼는 칸 SWAP · 칸 x slot_x · 상자 z (짐판 위 CAR_Z · 칸 속 SLOT_Z) · 셔틀 덮개 HOUSINGS
                    var r = root.transform;
                    Transform Pivot(string pivotName, Transform parent, Vector3 at, string part)
                    {
                        var p = new GameObject(pivotName).transform;
                        p.SetParent(parent, false);
                        p.position = r.TransformPoint(at);
                        p.rotation = r.rotation;
                        var mesh = Child(root, part);
                        if (mesh != null)
                            mesh.SetParent(p, true);
                        return p;
                    }
                    float[] levels = { F + 0.12f, F + 1.17f, F + 2.22f };
                    float SlotX(int bay, int k)
                    {
                        float x0 = bay == 0 ? 1.9f : 9.9f;
                        float xa = k < 3 ? x0 + 0.08f : x0 + 2.1f + 0.04f;
                        return xa + 0.34f + (k % 3) * 0.645f;
                    }
                    var swap = new[] { new[] { (0, 1), (1, 4), (2, 2) }, new[] { (0, 4), (1, 1), (2, 5) } };   // (단, 칸)
                    const float slotZ = -2.70f, carZ = -2.10f;   // 짐판 위 상자는 기둥 앞 (레일 z −1.75 − 0.35 — 상자가 기둥을 뚫지 않게, 사용자 2026-10-31)
                    var rest = new[] { (3.1f, F + 1.45f), (12.9f, F + 2.55f) };   // 스태커 모델 자세 (x, 짐판 윗면)
                    var stackers = new InteriorLogistics.Stacker[2];
                    for (int b = 0; b < 2; b++)
                    {
                        string l = b == 0 ? "A" : "B";
                        var mast = Child(root, "INT_EvoLogistics_Stacker" + l);
                        var car = Child(root, "INT_EvoLogistics_Stacker" + l + "Car");
                        if (mast != null && car != null)
                            car.SetParent(mast, true);
                        var slots = new Vector3[3];
                        for (int s = 0; s < 3; s++)
                            slots[s] = new Vector3(SlotX(b, swap[b][s].Item2), levels[swap[b][s].Item1], slotZ);
                        var crates = new[]
                        {
                            Pivot("Bay" + l + "Crate0Pivot", r, new Vector3(rest[b].Item1, rest[b].Item2, carZ), "INT_EvoLogistics_Bay" + l + "Crate0"),
                            Pivot("Bay" + l + "Crate1Pivot", r, slots[1], "INT_EvoLogistics_Bay" + l + "Crate1"),
                        };
                        stackers[b] = new InteriorLogistics.Stacker
                        {
                            Mast = mast, Car = car, RestX = rest[b].Item1, RestCarY = rest[b].Item2, CarZ = carZ, Crates = crates, Slots = slots, FirstFull = 1,
                        };
                    }
                    var podX = new[] { (4.6f, 5.9f, 1.9f), (11.6f, 10.1f, 14.1f) };   // (모델 x, 받는 덮개, 내리는 덮개)
                    var pods = new InteriorLogistics.Pod[2];
                    for (int i = 0; i < 2; i++)
                    {
                        string l = i == 0 ? "A" : "B";
                        var body = Child(root, "INT_EvoLogistics_Pod" + l);
                        var crate = Child(root, "INT_EvoLogistics_Pod" + l + "Crate");
                        if (body != null && crate != null)
                            crate.SetParent(body, true);
                        pods[i] = new InteriorLogistics.Pod { Body = body, Crate = crate, RestX = podX[i].Item1, PickX = podX[i].Item2, DropX = podX[i].Item3 };
                    }
                    root.AddComponent<InteriorLogistics>().Configure(stackers, pods);
                    // 하역문 위 안내판 (lh_builder L_Board 앞면 z 3.1, 가운데 F + 3.47, 2.1 × 0.5 — 아치에 가리지 않게 천장 가까이)
                    root.AddComponent<InteriorCargoBoard>().Configure(new InteriorArrivalBoard.Panel
                    {
                        Center = new Vector3(8f, F + 3.47f, 3.1f), Normal = Vector3.back, Up = Vector3.up, Width = 2.1f, Height = 0.5f,
                    });
                });
        }

        /// <summary>
        /// 진화 1단계 자재 재생 플랜트 (2 × 2칸, mr_builder — 사용자 2026-10-31 시안 1 "이대로 유니티로 옮겨줘. 천장의 구멍에 유리돔 깔끔하게 씌우는거 잊지말구"):
        /// 가운데 = 천장 구멍을 덮는 큰 유리 돔 (Window 얇은 껍질 — 아래 테 · 꼭대기 테 속으로 가장자리가 묻힘, 끝 뚜껑 없음: 뚜껑이 있으면 천장 높이 평평한 창이
        /// 갈비살을 가림 — 플레이 확인 2026-10-31, 흰 갈비살 · 모듈 색 띠) 아래 둥근 단 위 재생로
        /// (쇳물 웅덩이 · 유리 난간 · 네 귀 폐자재 깔때기), 돔 꼭대기 돛대의 집게 크레인 (<see cref="InteriorScrapCrane"/> — 남 컨베이어가 실어 온
        /// 압축 폐자재 덩이를 집어 쇳물에 넣음), 머리 위 컨베이어 둘 (남 = 덩이 들어옴 F + 2.1, 벽 쪽 덮개에서 나옴 · 동 = 주괴 나감). 벽마다 작업대 = 남 선별대 · 동 주형 줄 · 북 분해 작업대 · 서 제어 콘솔 (상태 화면 둘).
        /// 문 자리 = 칸마다 바깥 두 면 (8), 위 · 아래 해치 = 칸 중심 (천장 F + 3.8, 돔 테 밖).
        /// </summary>
        private static void BuildEvoReclaim()
        {
            const float ceiling = F + 3.8f;
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                for (int z = 0; z < 2; z++)
                {
                    var cell = new Vector3Int(x, 0, z);
                    sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                    sockets.Add(new InteriorSocket(cell, z == 0 ? new Vector3Int(0, 0, -1) : new Vector3Int(0, 0, 1), Socket));
                    sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, Vector3.zero));
                    sockets.Add(new InteriorSocket(cell, Vector3Int.up, ceiling, Vector3.zero));
                }
            }
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.85f, 0.65f);
            var cool = new Color(0.75f, 0.88f, 1f);
            var lights = new List<LightSpec>();
            foreach (var (x, z) in new[] { (0f, 0f), (8f, 0f), (0f, 8f), (8f, 8f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 3.3f, z), 6f, 1.5f, white));        // 칸마다 (돔 밖 평평한 천장)
            lights.Add(LightSpec.Point(new Vector3(4f, F + 3.6f, 4f), 5f, 1.0f, white));          // 돔 아래 (갈비살 · 크레인 — 꼭대기 테에 가까우면 하얗게 번짐, 플레이 확인 2026-10-31)
            lights.Add(LightSpec.Point(new Vector3(4f, F + 1.7f, 4f), 4.5f, 1.2f, new Color(1f, 0.6f, 0.25f)));   // 쇳물 빛
            lights.Add(LightSpec.Point(new Vector3(4.8f, F + 2.4f, -2.2f), 3.5f, 0.9f, white));   // 선별대 (남)
            lights.Add(LightSpec.Point(new Vector3(10.2f, F + 2.4f, 4f), 3.5f, 0.9f, warm));      // 주형 줄 (동)
            lights.Add(LightSpec.Point(new Vector3(4f, F + 2.4f, 10.2f), 3.5f, 0.9f, white));     // 분해 작업대 (북)
            lights.Add(LightSpec.Point(new Vector3(-2.2f, F + 2.4f, 4f), 3.5f, 0.9f, cool));      // 제어 콘솔 (서)
            EvoAccent("MaterialReclaimer", "#D8B080");
            // 스폰 = 남쪽 문 (0번 칸) 안에서 돔 아래 재생로를 바라봄 (바닥 해치 칸 중심 비킴)
            Build("EvoReclaim", "Evolved/MD_Evo_MaterialReclaimer", sockets, new Vector3(0.6f, F, -2.2f), 30f, lights,
                configure: root =>
                {
                    // 집게 크레인 (사용자 2026-10-31 "기계팔이 진짜로 옆쪽에서 물건을 집어와서 용광로에 넣는 동작", mr_builder crane):
                    // 붐 = 돛대 축 피벗 → 트롤리 (모델 반지름 1.0) → 줄 위 끝 · 집게 경첩 → 손가락 경첩 넷. 덩이 본은 가운데 피벗
                    const float hinge = F + 2.47f, cableTop = F + 3.21f, beltY = F + 2.232f + 0.11f;
                    var r = root.transform;
                    Transform Pivot(string pivotName, Transform parent, Vector3 at, string part)
                    {
                        var p = new GameObject(pivotName).transform;
                        p.SetParent(parent, false);
                        p.position = r.TransformPoint(at);
                        p.rotation = r.rotation;
                        var mesh = Child(root, part);
                        if (mesh != null)
                            mesh.SetParent(p, true);
                        return p;
                    }
                    var boom = Pivot("CraneBoomPivot", r, new Vector3(4f, 0f, 4f), "INT_EvoReclaim_CraneBoom");
                    var trolley = Pivot("CraneTrolleyPivot", boom, new Vector3(5f, 0f, 4f), "INT_EvoReclaim_CraneTrolley");
                    var cable = Pivot("CraneCablePivot", trolley, new Vector3(5f, cableTop, 4f), "INT_EvoReclaim_CraneCable");
                    var claw = Pivot("CraneClawPivot", trolley, new Vector3(5f, hinge, 4f), "INT_EvoReclaim_CraneClaw");
                    var fingers = new Transform[4];
                    var axes = new Vector3[4];
                    for (int k = 0; k < 4; k++)
                    {
                        float a = Mathf.PI / 4f + Mathf.PI / 2f * k;
                        var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        fingers[k] = Pivot("CraneFingerPivot" + k, claw, new Vector3(5f, hinge, 4f) + d * 0.1f, "INT_EvoReclaim_CraneFinger" + k);
                        axes[k] = Vector3.Cross(d, Vector3.up);   // + = 손가락 끝이 바깥으로 (벌림)
                    }
                    var bale = Pivot("ScrapBalePivot", r, new Vector3(4f, beltY, -2.85f), "INT_EvoReclaim_Bale");
                    root.AddComponent<InteriorScrapCrane>().Configure(boom, trolley, cable, claw, fingers, axes, bale,
                        new Vector3(4f, beltY, -2.85f), new Vector3(4f, beltY, 0.05f), new Vector3(0.2f, 0.11f, 0.16f), new Vector3(4f, F + 0.9f, 4f),
                        1f, 0.7f, hinge, F + 3.0f, cableTop, AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.SparkPath));
                });
        }

        /// <summary>
        /// 진화 1단계 비상 대응 센터 (ㄱ자 3칸, ec_builder — 사용자 2026-10-30 시안 1 "이대로 유니티에서 한번 보여줄래? 휑한 느낌이 드는지 보게"):
        /// 지휘탑 (0번 칸, 원통 천장 F + 5.0 · 둘레 고창 띠 · 천창 아래 도는 경광등 INT_EvoEmergency_Beacon · 가운데 상황 테이블 = 실제 정거장 지도
        /// <see cref="InteriorStationMap"/>을 눕힘), 출동실 (+x 칸 — 유리 발사관 둘 속 천천히 도는 진화 드론 DroneA · B · 발사 콘솔 둘 · 소화약제 선반),
        /// 긴급 보수실 (+z 칸 — 주황 · 흰 아치 유리 볼트 · 휜 보수판 이음선을 용접하는 팔 (토치 Torch0, <see cref="InteriorWelder"/>) · 보수판 선반).
        /// 문 자리 = 연결점 있는 면 4, 바닥 해치 = 칸마다, 위 해치 = 지휘탑 천장 (경광등 · 천창 비킴).
        /// </summary>
        private static void BuildEvoEmergency()
        {
            const float tower = F + 5.0f;
            var c00 = new Vector3Int(0, 0, 0);
            var c10 = new Vector3Int(1, 0, 0);
            var c01 = new Vector3Int(0, 0, 1);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(c00, Vector3Int.left, Socket),
                new InteriorSocket(c00, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(c10, Vector3Int.right, Socket),
                new InteriorSocket(c01, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(c00, Vector3Int.down, -F, new Vector3(-1.9f, 0f, -2.0f)),    // 두 문 사이 바깥 모서리 (테이블 · 주민 비킴)
                new InteriorSocket(c10, Vector3Int.down, -F, new Vector3(-0.8f, 0f, -1.8f)),    // 창 쪽 길 (7.2, −1.8)
                new InteriorSocket(c01, Vector3Int.down, -F, new Vector3(2.3f, 0f, -3.7f)),     // 보수실 들머리 (2.3, 4.3)
                new InteriorSocket(c00, Vector3Int.up, tower, new Vector3(1.4f, 0f, -1.4f)),    // 지휘탑 천장 (천창 · 경광등 비킴)
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.85f, 0.65f);
            var cool = new Color(0.75f, 0.88f, 1f);
            var lights = new List<LightSpec>();
            foreach (var (x, z) in new[] { (-1.4f, -1.4f), (1.4f, -1.4f), (-1.4f, 1.4f), (1.4f, 1.4f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 3.3f, z), 5f, 1.3f, white));        // 지휘탑
            lights.Add(LightSpec.Point(new Vector3(0f, tower - 1.0f, 0f), 4f, 0.8f, new Color(1f, 0.6f, 0.25f)));   // 경광등 주황빛
            foreach (float x in new[] { 4.6f, 7.2f, 10.0f })
                lights.Add(LightSpec.Point(new Vector3(x, F + 3.3f, -1.6f), 5f, 1.3f, white));    // 출동실 창 쪽 길
            foreach (var (tx, tz) in new[] { (5.8f, 0.5f), (8.6f, 0.5f) })
                lights.Add(LightSpec.Point(new Vector3(tx, F + 2.7f, tz), 2.5f, 0.6f, cool));     // 발사관 속
            foreach (float z in new[] { 4.4f, 7.0f, 9.6f })
                lights.Add(LightSpec.Point(new Vector3(0f, F + 3.6f, z), 5.5f, 1.4f, white));     // 보수실 볼트 아래
            lights.Add(LightSpec.Point(new Vector3(0f, F + 2.4f, 7.0f), 3.5f, 0.8f, warm));      // 용접대
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoEmergency_Beacon", Vector3.zero, Vector3.up, 150f),
                new OrbitSpec("INT_EvoEmergency_DroneA", new Vector3(5.8f, 0f, 0.5f), Vector3.up, 25f),
                new OrbitSpec("INT_EvoEmergency_DroneB", new Vector3(8.6f, 0f, 0.5f), Vector3.up, -25f),
            };
            EvoAccent("EmergencyCenter", "#FFB07F");
            // 스폰 = 남쪽 문 안 서쪽에서 상황 테이블 · 출동실 쪽을 바라봄 (테이블 남동 주민이 바로 앞을 가리지 않게 — 플레이 확인 2026-10-30)
            Build("EvoEmergency", "Evolved/MD_Evo_EmergencyCenter", sockets, new Vector3(-0.7f, F, -2.4f), 25f, lights, orbits,
                configure: root =>
                {
                    // 상황 테이블 윗면 (F + 0.951, ec_builder E_TableScreen 1.76 × 1.2) = 실제 정거장 지도 (지도 위 = +z, 오른쪽 = +x — 남쪽에서 읽힘)
                    root.AddComponent<InteriorStationMap>().Configure(new Vector3(0f, F + 0.951f, 0f), Vector3.up, Vector3.forward, new Vector2(1.72f, 1.16f),
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ScreenPath),
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ScreenGridPath));
                    // 보수판 이음선 용접 (ec_builder TORCHES — 토치 끝 · 이음선 법선)
                    var torch = Child(root, "INT_EvoEmergency_Torch0");
                    var tip = new Vector3(0.3221f, F + 1.0115f, 7.0f);
                    var normal = new Vector3(0.2474f, 0.9689f, 0f);
                    root.AddComponent<InteriorWelder>().Configure(new[] { torch },
                        new[] { torch != null ? torch.InverseTransformPoint(root.transform.TransformPoint(tip)) : tip },
                        new[] { torch != null ? torch.InverseTransformDirection(root.transform.TransformDirection(normal)) : normal },
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.SparkPath));
                });
        }

        /// <summary>
        /// 진화 1단계 정비 공창 (3 × 2칸, mw_builder — 사용자 2026-10-30 시안 1 "이대로 옮겨줘 내부에서 체크할게"): 양 격납고 홀 (x 방향 유리 볼트 천장 ·
        /// 흰 갈비살, 홀마다 양 끝 수리 패드 둘 = 수리 자리 넷 — 도는 회전판 위 고칠 부품 INT_EvoWorks_Pad0 ~ 3 · 용접 팔 (토치 Torch0 ~ 3 끝 불꽃) · 진단 콘솔, 가운데 작업대 위 도는 로봇 팔 ArmA · B,
        /// 볼트 아래 다리 크레인), 가운데 띠 = 공용 부품 창고 (유리 원통 속 회전 선반 CarouselA · B · 두 홀을 잇는 건널목 · 노란 안전선).
        /// 문 자리 = 연결점 있는 면 6, 바닥 해치 = 칸마다, 위 해치 = 가운데 줄 두 칸 (창고 건널목 평평한 천장), 천장 F + 4.0 (볼트 꼭대기 F + 5.15).
        /// </summary>
        private static void BuildEvoWorks()
        {
            const float ceiling = F + 4.0f;
            var c00 = new Vector3Int(0, 0, 0);
            var c10 = new Vector3Int(1, 0, 0);
            var c20 = new Vector3Int(2, 0, 0);
            var c01 = new Vector3Int(0, 0, 1);
            var c11 = new Vector3Int(1, 0, 1);
            var c21 = new Vector3Int(2, 0, 1);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(c00, Vector3Int.left, Socket),
                new InteriorSocket(c01, Vector3Int.left, Socket),
                new InteriorSocket(c20, Vector3Int.right, Socket),
                new InteriorSocket(c21, Vector3Int.right, Socket),
                new InteriorSocket(c10, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(c11, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(c00, Vector3Int.down, -F, new Vector3(-2.25f, 0f, 2.6f)),     // 패드 · 부품 선반 비킴 (창고 띠 쪽 끝)
                new InteriorSocket(c10, Vector3Int.down, -F, new Vector3(-2.6f, 0f, 2.6f)),      // 건널목 (5.4, 2.6)
                new InteriorSocket(c20, Vector3Int.down, -F, new Vector3(2.25f, 0f, 2.6f)),
                new InteriorSocket(c01, Vector3Int.down, -F, new Vector3(-2.25f, 0f, -2.6f)),
                new InteriorSocket(c11, Vector3Int.down, -F, new Vector3(2.6f, 0f, -2.6f)),      // 건널목 (10.6, 5.4)
                new InteriorSocket(c21, Vector3Int.down, -F, new Vector3(2.25f, 0f, -2.6f)),
                new InteriorSocket(c10, Vector3Int.up, ceiling, new Vector3(-1.2f, 0f, 3.4f)),   // 창고 띠 평평한 천장 (6.8, 3.4)
                new InteriorSocket(c11, Vector3Int.up, ceiling, new Vector3(1.2f, 0f, -3.4f)),   // (9.2, 4.6)
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.85f, 0.65f);
            var cool = new Color(0.75f, 0.88f, 1f);
            var lights = new List<LightSpec>();
            foreach (float zc in new[] { -0.4f, 8.4f })
            {
                foreach (float x in new[] { -1.2f, 4.0f, 8.0f, 12.0f, 17.2f })
                    lights.Add(LightSpec.Point(new Vector3(x, F + 3.4f, zc), 6f, 1.5f, white));   // 볼트 아래 홀
            }
            foreach (var (x, z) in new[] { (0f, -0.4f), (16f, -0.4f), (0f, 8.4f), (16f, 8.4f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 2.8f, z), 3.5f, 0.8f, warm));      // 수리 패드
            lights.Add(LightSpec.Point(new Vector3(6f, F + 3.2f, 4f), 5f, 1.2f, white));         // 창고 띠 건널목
            lights.Add(LightSpec.Point(new Vector3(10f, F + 3.2f, 4f), 5f, 1.2f, white));
            lights.Add(LightSpec.Point(new Vector3(2f, F + 2.0f, 4f), 2.5f, 0.6f, cool));         // 부품 선반 원통 속
            lights.Add(LightSpec.Point(new Vector3(14f, F + 2.0f, 4f), 2.5f, 0.6f, cool));
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoWorks_Pad0", new Vector3(0f, 0f, -0.4f), Vector3.up, 10f),
                new OrbitSpec("INT_EvoWorks_Pad1", new Vector3(16f, 0f, -0.4f), Vector3.up, -10f),
                new OrbitSpec("INT_EvoWorks_Pad2", new Vector3(0f, 0f, 8.4f), Vector3.up, -10f),
                new OrbitSpec("INT_EvoWorks_Pad3", new Vector3(16f, 0f, 8.4f), Vector3.up, 10f),
                new OrbitSpec("INT_EvoWorks_ArmA", new Vector3(8f, 0f, -0.4f), Vector3.up, 15f),
                new OrbitSpec("INT_EvoWorks_ArmB", new Vector3(8f, 0f, 8.4f), Vector3.up, -15f),
                new OrbitSpec("INT_EvoWorks_CarouselA", new Vector3(2f, 0f, 4f), Vector3.up, 12f),
                new OrbitSpec("INT_EvoWorks_CarouselB", new Vector3(14f, 0f, 4f), Vector3.up, -12f),
            };
            EvoAccent("MaintenanceWorks", "#FFC07F");
            // 스폰 = 창고 띠 건널목에서 앞 홀 서쪽 패드 쪽을 바라봄
            Build("EvoWorks", "Evolved/MD_Evo_MaintenanceWorks", sockets, new Vector3(5.2f, F, 4.2f), -130f, lights, orbits,
                configure: root =>
                {
                    // 수리 패드 용접 팔 토치 (사용자 2026-10-30, mw_builder TORCHES): 토치를 패드 아래로 옮겨 부품 · 팔과 함께 돌게 하고, 끝에서 불꽃.
                    // 짝수 = 선체 통 점검 판 끝 이음선 · 홀수 = 추진 노즐 옆면. 모델의 발광 구는 뺌
                    var centers = new[] { new Vector2(0f, -0.4f), new Vector2(16f, -0.4f), new Vector2(0f, 8.4f), new Vector2(16f, 8.4f) };
                    var torches = new Transform[4];
                    var tips = new Vector3[4];
                    var normals = new Vector3[4];
                    for (int i = 0; i < 4; i++)
                    {
                        torches[i] = Child(root, "INT_EvoWorks_Torch" + i);
                        var pad = Child(root, "INT_EvoWorks_Pad" + i);
                        if (torches[i] != null && pad != null)
                            torches[i].SetParent(pad, true);
                        var c = centers[i];
                        var tip = i % 2 == 0 ? new Vector3(c.x + 0.1144f, F + 1.4145f, c.y + 0.248f) : new Vector3(c.x + 0.1102f, F + 1.0579f, c.y + 0.4108f);
                        var n = i % 2 == 0 ? new Vector3(0.1987f, 0.9801f, 0f) : new Vector3(0.2379f, 0.395f, 0.8873f);
                        tips[i] = torches[i] != null ? torches[i].InverseTransformPoint(root.transform.TransformPoint(tip)) : tip;
                        normals[i] = torches[i] != null ? torches[i].InverseTransformDirection(root.transform.TransformDirection(n)) : n;
                    }
                    root.AddComponent<InteriorWelder>().Configure(torches, tips, normals, AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.SparkPath));
                });
        }

        /// <summary>
        /// 진화 1단계 장갑 외벽 (3칸 일자, aw_builder — 사용자 2026-10-30 시안 1 다듬음 "이대로 옮겨줘"): 긴 방패 회랑 — 문 사이 벽 넷 = 안쪽으로 기운 장갑면
        /// (밝은 회색 틈 위 큰 흰 육각판 · 고정 띠 양옆 큰 육각 바깥 창 하나씩 — 환공포증 배려로 판을 크게 · 대비를 낮게 · 구멍 창을 흩지 않음),
        /// 칸 경계 = 짙은 고정 띠 갈비 (기운 면 · 천장을 넘음), 천장 미끼 신호등 셋 (유리 갓 속 도는 빛 날개 — INT_EvoArmor_Beacon0 · 1 · 2, 외형의 돔 셋),
        /// 가운데 칸 = 장갑판 수리장 (천장 회전대에 매달려 도는 로봇 팔 Arm · 수리대 위 육각판 · 예비 판 선반 넷 — 빠른 수리),
        /// 양 끝 칸 = 충격 감시실 (큰 칸 일곱 육각 상황판 · 바깥 모서리 감시 콘솔 둘). 문 자리 = 연결점 있는 면 8, 천장 F + 4.0.
        /// </summary>
        private static void BuildEvoArmor()
        {
            const float ceiling = F + 4.0f;
            var c0 = new Vector3Int(0, 0, 0);
            var c1 = new Vector3Int(1, 0, 0);
            var c2 = new Vector3Int(2, 0, 0);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(c0, Vector3Int.left, Socket),
                new InteriorSocket(c0, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(c0, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(c1, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(c1, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(c2, Vector3Int.right, Socket),
                new InteriorSocket(c2, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(c2, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(c0, Vector3Int.down, -F, new Vector3(0f, 0f, -2.2f)),      // 상황판 밖 −z 길
                new InteriorSocket(c1, Vector3Int.down, -F, new Vector3(-2.5f, 0f, 0f)),      // 고정 띠와 수리대 사이 (x 5.5)
                new InteriorSocket(c2, Vector3Int.down, -F, new Vector3(0f, 0f, 2.2f)),
                new InteriorSocket(c0, Vector3Int.up, ceiling, new Vector3(-1.9f, 0f, 0f)),   // 미끼 신호등 · 빛 띠 비킴
                new InteriorSocket(c1, Vector3Int.up, ceiling, new Vector3(-2.5f, 0f, 0f)),   // 고정 띠 · 로봇 팔 회전대 비킴
                new InteriorSocket(c2, Vector3Int.up, ceiling, new Vector3(1.9f, 0f, 0f)),
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var steel = new Color(0.8f, 0.86f, 0.95f);
            var lights = new List<LightSpec>();
            foreach (float cx in new[] { 0f, 8f, 16f })
            {
                foreach (var (dx, z) in new[] { (-1.6f, 1.6f), (-1.6f, -1.6f), (1.6f, 1.6f), (1.6f, -1.6f) })
                    lights.Add(LightSpec.Point(new Vector3(cx + dx, F + 3.3f, z), 5f, 1.3f, white));
            }
            foreach (float bx in new[] { 0f, 10.5f, 16f })
                lights.Add(LightSpec.Point(new Vector3(bx, F + 3.3f, 0f), 3f, 0.6f, steel));   // 미끼 신호등 빛
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoArmor_Arm", new Vector3(8f, 0f, 0f), Vector3.up, 12f),
                new OrbitSpec("INT_EvoArmor_Beacon0", new Vector3(0f, 0f, 0f), Vector3.up, 90f),
                new OrbitSpec("INT_EvoArmor_Beacon1", new Vector3(10.5f, 0f, 0f), Vector3.up, 90f),
                new OrbitSpec("INT_EvoArmor_Beacon2", new Vector3(16f, 0f, 0f), Vector3.up, 90f),
            };
            EvoAccent("ArmorWall", "#B0B8C0");
            // 스폰 = 서쪽 감시실에서 회랑 (+x)을 바라봄
            Build("EvoArmor", "Evolved/MD_Evo_ArmorWall", sockets, new Vector3(1.4f, F, -1.5f), 75f, lights, orbits);
        }

        /// <summary>
        /// 진화 1단계 방어 포대 (3칸 일자, db_builder — 사용자 2026-10-30 시안 1 "일단 유니티에 넣어줘 직접 들어가서 체크해볼게"): 포탑 둘을 장갑 뒤에 묶어 한 사격 통제로 —
        /// 양 끝 칸 = 포탑실 (천장 위로 솟은 흰 돔 · 관측 창 여덟 · 꼭대기 원 = 위 해치, 베어링 레일에 매달려 도는 포탑 고리 — 쌍포 약실 · 평형추 · 급탄 회전판
        /// (INT_EvoDefense_TurretA · B), 바닥 탄약고 드럼 + 유리 승강관 속 탄 나선 (HoistA · B — 돌면 탄이 솟아 보임), 바깥 모서리 사수 콘솔 둘),
        /// 가운데 칸 = 장갑 벙커 사격 통제실 (양쪽 장갑 격벽 문틀 · 탄 선반, 외형 같은 가는 틈 창 + 아래 책상 넷, 레이더 상황판 = 센서 어레이와 같은
        /// <see cref="InteriorRadar"/> (쓸기 빛 Sweep · 실제 유성우 · 포격 예보 운석 점 · 경광등 Beacon · −z 책상 예보 화면), 둥근 천창으로 내려온 도는 레이더 돛대 (Mast)).
        /// 문 자리 = 연결점 있는 면 8, 천장 F + 4.0.
        /// </summary>
        private static void BuildEvoDefense()
        {
            const float ceiling = F + 4.0f;
            const float domeTop = F + 5.15f;   // db_builder DOME_TOP (포탑실 돔 꼭대기 평평한 원)
            var c0 = new Vector3Int(0, 0, 0);
            var c1 = new Vector3Int(1, 0, 0);
            var c2 = new Vector3Int(2, 0, 0);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(c0, Vector3Int.left, Socket),
                new InteriorSocket(c0, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(c0, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(c1, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(c1, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(c2, Vector3Int.right, Socket),
                new InteriorSocket(c2, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(c2, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(c0, Vector3Int.down, -F, new Vector3(0f, 0f, -2.2f)),     // 탄약고 드럼 · 바닥 빛 고리 밖 −z 길
                new InteriorSocket(c1, Vector3Int.down, -F, new Vector3(-2.8f, 0f, 0f)),     // 격벽 문틀과 상황판 사이 (x 5.2)
                new InteriorSocket(c2, Vector3Int.down, -F, new Vector3(0f, 0f, 2.2f)),
                new InteriorSocket(c0, Vector3Int.up, domeTop, Vector3.zero),                // 돔 꼭대기 평평한 원
                new InteriorSocket(c1, Vector3Int.up, ceiling, new Vector3(2.7f, 0f, 0f)),   // 천창 · 신호 줄 · 문틀 비킴 (x 10.7)
                new InteriorSocket(c2, Vector3Int.up, domeTop, Vector3.zero),
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var coral = new Color(1f, 0.56f, 0.56f);
            var lights = new List<LightSpec>();
            foreach (float cx in new[] { 0f, 16f })
            {
                foreach (var (dx, z) in new[] { (-1.4f, 2.4f), (-1.4f, -2.4f), (1.4f, 2.4f), (1.4f, -2.4f) })
                    lights.Add(LightSpec.Point(new Vector3(cx + dx, F + 3.0f, z), 5f, 1.3f, white));   // 포탑 고리 레일 밖
                lights.Add(LightSpec.Point(new Vector3(cx, F + 4.4f, 0f), 4f, 1.0f, white));            // 돔 속
                lights.Add(LightSpec.Point(new Vector3(cx, F + 2.4f, 0f), 2.5f, 0.4f, coral));          // 승강관 (드럼 바로 위는 드럼이 분홍으로 물들었음)
            }
            foreach (var (x, z) in new[] { (5.7f, 1.7f), (5.7f, -1.7f), (10.3f, 1.7f), (10.3f, -1.7f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 3.3f, z), 5f, 1.4f, white));              // 벙커
            lights.Add(LightSpec.Point(new Vector3(8f, F + 1.5f, 0f), 2.5f, 0.8f, coral));              // 레이더 상황판 빛
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoDefense_TurretA", new Vector3(0f, 0f, 0f), Vector3.up, 9f),
                new OrbitSpec("INT_EvoDefense_TurretB", new Vector3(16f, 0f, 0f), Vector3.up, -7f),
                new OrbitSpec("INT_EvoDefense_HoistA", new Vector3(0f, 0f, 0f), Vector3.up, 90f),    // + = 탄 나선이 위로 오르는 것처럼 (광물 승강 기둥과 같은 나선)
                new OrbitSpec("INT_EvoDefense_HoistB", new Vector3(16f, 0f, 0f), Vector3.up, 90f),
                new OrbitSpec("INT_EvoDefense_Sweep", new Vector3(8f, 0f, 0f), Vector3.up, 60f),     // + = 밝은 앞 가장자리 (+x)가 앞장서고 부채꼴 (+z 쪽)이 뒤따름
                new OrbitSpec("INT_EvoDefense_Mast", new Vector3(8f, 0f, 0f), Vector3.up, 30f),
            };
            EvoAccent("DefenseBattery", "#FF8F8F");
            // 스폰 = 서쪽 포탑실 격벽 문틀 앞에서 탄약고 · 포탑 고리를 바라봄
            Build("EvoDefense", "Evolved/MD_Evo_DefenseBattery", sockets, new Vector3(2.4f, F, 1.6f), -125f, lights, orbits,
                // 레이더 = 센서 어레이와 같은 실제 예보 (사용자 2026-10-30 "레이더는 기존에 있던 걸"): 유성우 · 포격이 오는 방향에 운석 점 · 쓸기 빛이 지날 때 밝아짐,
                // 경광등 = 돛대 아래 미끼 표지 빛 고리, 예보 글 = 상황판 쪽 −z 책상의 어두운 화면 (db_builder)
                configure: root => root.AddComponent<InteriorRadar>().Configure(
                    new Vector3(8f, F + 0.958f, 0f), 0.9f,
                    Child(root, "INT_EvoDefense_Sweep")?.GetComponent<InteriorOrbit>(),
                    Child(root, "INT_EvoDefense_Beacon")?.GetComponent<Renderer>(),
                    new Vector3(5.55f, F + 1.14f, -2.825f), Vector3.forward, Vector3.up, new Vector2(0.58f, 0.32f)));
        }

        /// <summary>
        /// 진화 1단계 실드 매트릭스 (ㄱ자 3칸, sm_builder — 사용자 2026-10-30 시안 1 확정 "유니티에 넣어줘"): 모서리 칸 = 매트릭스 코어실 —
        /// 천장 위로 솟은 유리 돔 (바깥 창 · 갈비살 · 꼭대기 평평한 원 = 위 해치) 아래 유리 구 속 빛나는 코어 구슬 + 기운 고리 둘이 돎 (INT_EvoShield_RingA · RingB),
        /// 두 팔 = 장 증폭 통로 — 천장 밑 유리 도관 속 빛 구슬 나선이 노드 쪽으로 흐르는 것처럼 돎 (FlowX · FlowZ) · 팔 칸 가운데 발생 노드 · 벽쪽 관제 콘솔 둘씩.
        /// 문 자리 = 연결점 있는 면 8, 바닥 해치 = 칸마다 (코어 · 노드 비킴), 위 해치 = 모서리 돔 꼭대기 (F + 5.15) · 팔은 벽쪽, 천장 F + 4.0.
        /// </summary>
        private static void BuildEvoShield()
        {
            const float ceiling = F + 4.0f;
            const float domeTop = F + 5.15f;   // sm_builder DOME_TOP
            const float coreY = F + 1.55f, flowY = F + 3.45f;
            var corner = new Vector3Int(0, 0, 0);
            var armX = new Vector3Int(1, 0, 0);
            var armZ = new Vector3Int(0, 0, 1);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(corner, Vector3Int.left, Socket),
                new InteriorSocket(corner, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(armX, Vector3Int.right, Socket),
                new InteriorSocket(armX, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(armX, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(armZ, Vector3Int.left, Socket),
                new InteriorSocket(armZ, Vector3Int.right, Socket),
                new InteriorSocket(armZ, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(corner, Vector3Int.down, -F, new Vector3(-1.9f, 0f, -1.9f)),   // 코어 받침 밖 모서리 쪽
                new InteriorSocket(armX, Vector3Int.down, -F, new Vector3(0f, 0f, 2.2f)),
                new InteriorSocket(armZ, Vector3Int.down, -F, new Vector3(2.2f, 0f, 0f)),
                new InteriorSocket(corner, Vector3Int.up, domeTop, Vector3.zero),                   // 돔 꼭대기 평평한 원
                new InteriorSocket(armX, Vector3Int.up, ceiling, new Vector3(0f, 0f, -2.2f)),      // 도관 · 빛 띠 밖
                new InteriorSocket(armZ, Vector3Int.up, ceiling, new Vector3(-2.2f, 0f, 0f)),
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var blue = new Color(0.55f, 0.78f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(0f, coreY, 0f), 4f, 1.5f, blue),                // 코어 빛
                LightSpec.Point(new Vector3(-2.1f, F + 3.2f, -2.1f), 5f, 1.3f, white),      // 코어실
                LightSpec.Point(new Vector3(2.1f, F + 3.2f, -2.1f), 5f, 1.1f, white),
                LightSpec.Point(new Vector3(-2.1f, F + 3.2f, 2.1f), 5f, 1.1f, white),
                LightSpec.Point(new Vector3(5.2f, F + 3.3f, 1.9f), 5f, 1.4f, white),        // +x 팔
                LightSpec.Point(new Vector3(5.2f, F + 3.3f, -1.9f), 5f, 1.4f, white),
                LightSpec.Point(new Vector3(9.6f, F + 3.3f, 0f), 5f, 1.4f, white),
                LightSpec.Point(new Vector3(8f, F + 1.3f, 0f), 2.5f, 0.8f, blue),           // 노드 빛
                LightSpec.Point(new Vector3(1.9f, F + 3.3f, 5.2f), 5f, 1.4f, white),        // +z 팔
                LightSpec.Point(new Vector3(-1.9f, F + 3.3f, 5.2f), 5f, 1.4f, white),
                LightSpec.Point(new Vector3(0f, F + 3.3f, 9.6f), 5f, 1.4f, white),
                LightSpec.Point(new Vector3(0f, F + 1.3f, 8f), 2.5f, 0.8f, blue),
            };
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoShield_RingA", new Vector3(0f, coreY, 0f), new Vector3(0f, 0.8829f, 0.4695f), 40f),   // 고리 자체 축 (sm_builder RING AXES)
                new OrbitSpec("INT_EvoShield_RingB", new Vector3(0f, coreY, 0f), new Vector3(0.6428f, 0.766f, 0f), -55f),
                new OrbitSpec("INT_EvoShield_FlowX", new Vector3(0f, flowY, 0f), Vector3.right, -120f),     // − = +x (노드 쪽)으로 흐름
                new OrbitSpec("INT_EvoShield_FlowZ", new Vector3(0f, flowY, 0f), new Vector3(0f, 0f, 1f), 120f), // + = +z (노드 쪽)으로 흐름
            };
            EvoAccent("ShieldMatrix", "#8FB8FF");
            // 스폰 = 코어실 −z 문 안쪽 옆에서 코어와 +x 팔을 바라봄
            Build("EvoShield", "Evolved/MD_Evo_ShieldMatrix", sockets, new Vector3(-1.6f, F, -2.0f), 55f, lights, orbits);
        }

        /// <summary>
        /// 진화 1단계 광물 처리 단지 (3칸 일자, mc_builder — 사용자 2026-10-30 시안 1 확정 "이대로 옮겨보자"): 광석이 들어와 금속이 되는 한 줄 —
        /// 가운데 칸 = 하역장 (z 방향 아치 볼트 · 꼭대기 유리 천창 띠 · +z 도킹 문에서 오는 착륙 화살표 · 분쇄기 깔때기 → 광석 승강 기둥 (INT_EvoMineral_OreLift — 유리 관 속 나선이 돎)),
        /// 천장 밑 갈림 상자에서 양쪽 용광로로 유리 나사 이송관 둘 (AugerA · AugerB — 광석이 붙은 나선이 돌아 바깥쪽으로 실려 가는 것처럼 보임, 외형의 황동 관),
        /// 양 끝 칸 = 허리 높이 항아리 용광로 (유리 난간 너머 녹은 금속 웅덩이 · 빛나는 틈 창 · 배기 후드 · 광석 낙하 관) + 바깥쪽 모서리 주괴 더미.
        /// 문 자리 = 연결점 있는 면 8, 바닥 해치 = 칸마다 (용광로 · 분쇄기 비킴), 위 해치 = 칸마다 (가운데 칸은 볼트 밖 평평한 천장), 천장 F + 4.0 (하역장 볼트 꼭대기 F + 5.0).
        /// </summary>
        private static void BuildEvoMineral()
        {
            const float ceiling = F + 4.0f;
            var c0 = new Vector3Int(0, 0, 0);
            var c1 = new Vector3Int(1, 0, 0);
            var c2 = new Vector3Int(2, 0, 0);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(c0, Vector3Int.left, Socket),
                new InteriorSocket(c0, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(c0, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(c1, new Vector3Int(0, 0, 1), Socket),   // 도킹 쪽
                new InteriorSocket(c1, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(c2, Vector3Int.right, Socket),
                new InteriorSocket(c2, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(c2, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(c0, Vector3Int.down, -F, new Vector3(0f, 0f, -2.2f)),      // 용광로 옆 −z 길
                new InteriorSocket(c1, Vector3Int.down, -F, new Vector3(-2.9f, 0f, 0f)),      // 분쇄기 서쪽
                new InteriorSocket(c2, Vector3Int.down, -F, new Vector3(0f, 0f, -2.2f)),
                new InteriorSocket(c0, Vector3Int.up, ceiling, new Vector3(-2.2f, 0f, 0f)),   // 후드 · 천장 빛 띠 비킴
                new InteriorSocket(c1, Vector3Int.up, ceiling, new Vector3(-3.0f, 0f, 1.6f)), // 볼트 밖 평평한 천장 · 이송관 걸이 비킴
                new InteriorSocket(c2, Vector3Int.up, ceiling, new Vector3(2.2f, 0f, 0f)),
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var melt = new Color(1f, 0.55f, 0.2f);
            var lights = new List<LightSpec>();
            foreach (float cx in new[] { 0f, 16f })
            {
                foreach (var (dx, z) in new[] { (-1.6f, 1.9f), (-1.6f, -1.9f), (1.6f, 1.9f), (1.6f, -1.9f) })
                    lights.Add(LightSpec.Point(new Vector3(cx + dx, F + 3.3f, z), 5f, 1.4f, white));
                lights.Add(LightSpec.Point(new Vector3(cx, F + 1.6f, 0f), 3.5f, 1.6f, melt));   // 녹은 금속 빛
            }
            lights.Add(LightSpec.Point(new Vector3(8f, F + 3.6f, 1.9f), 6f, 1.6f, white));       // 하역장 (볼트 밑)
            lights.Add(LightSpec.Point(new Vector3(8f, F + 3.6f, -1.9f), 6f, 1.6f, white));
            lights.Add(LightSpec.Point(new Vector3(8f, F + 2.6f, 1.4f), 3f, 0.8f, white));       // 분쇄기 깔때기 위
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoMineral_OreLift", new Vector3(8f, 0f, 0f), Vector3.up, 90f),          // + = 나선이 위로 오르는 것처럼
                new OrbitSpec("INT_EvoMineral_AugerA", new Vector3(0f, F + 3.3f, 0f), Vector3.right, 120f),  // + = −x (서쪽 용광로)로 실려 감
                new OrbitSpec("INT_EvoMineral_AugerB", new Vector3(0f, F + 3.3f, 0f), Vector3.right, -120f), // − = +x (동쪽 용광로)로 실려 감
            };
            EvoAccent("MineralComplex", "#C0A080");
            // 스폰 = 서쪽 용광로 옆 +z 길에서 하역장 쪽을 바라봄
            Build("EvoMineral", "Evolved/MD_Evo_MineralComplex", sockets, new Vector3(2.6f, F, 2.1f), 100f, lights, orbits);
        }

        /// <summary>
        /// 진화 1단계 수직 농장 (2칸 × 2층 + 꼭대기 1칸, vf_builder — 사용자 2026-10-29 시안 1 확정 "이대로 넣어줘"): 세 층을 가운데 칸 유리 엘리베이터로 잇음
        /// (<see cref="InteriorLift"/>, 1층 F · 2층 8 + F · 꼭대기 16 + F, 문 = +x 홀 쪽 — 문짝은 z로 열림).
        /// 1층 = 등을 맞댄 3단 재배 선반 (상추 · 재배등) + 창가 길, 2층 = 상추가 나선으로 박힌 공중 재배 기둥 넷이 천천히 돎 (INT_EvoFarm_Tower1~4) + 창가 모종 선반,
        /// 꼭대기 = 빛나는 배양액 통 셋 (둘레로 거품이 돌며 오름 — INT_EvoFarm_Bubbles1~3) + 관제 책상 + 천창.
        /// 문 자리 = 연결점 있는 면 (11), 바닥 해치 = 1층 두 칸 (+z 창가 길), 위 해치 = 2층 +x 칸 (기둥 고리 조명 비킴) · 꼭대기 (+x 천창 자리), 천장 F + 4.0 (층마다).
        /// </summary>
        private static void BuildEvoFarm()
        {
            const float ceiling = F + 4.0f;   // 칸 중심 기준 (층마다 같음)
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(new Vector3Int(0, 0, 0), Vector3Int.left, Socket),
                new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.right, Socket),
                new InteriorSocket(new Vector3Int(0, 1, 0), Vector3Int.left, Socket),
                new InteriorSocket(new Vector3Int(0, 1, 0), new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(new Vector3Int(1, 1, 0), Vector3Int.right, Socket),
                new InteriorSocket(new Vector3Int(0, 2, 0), Vector3Int.left, Socket),
                new InteriorSocket(new Vector3Int(0, 2, 0), Vector3Int.right, Socket),
                new InteriorSocket(new Vector3Int(0, 2, 0), new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(new Vector3Int(0, 2, 0), new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(new Vector3Int(0, 0, 0), Vector3Int.down, -F, new Vector3(0f, 0f, 2.2f)),    // 엘리베이터 옆 창가 길
                new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.down, -F, new Vector3(0f, 0f, 2.3f)),    // 선반 옆 창가 길
                new InteriorSocket(new Vector3Int(1, 1, 0), Vector3Int.up, ceiling, new Vector3(1.6f, 0f, 0f)), // 기둥 고리 조명 밖
                new InteriorSocket(new Vector3Int(0, 2, 0), Vector3Int.up, ceiling, new Vector3(2.25f, 0f, 0f)),
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.82f, 0.55f);
            var lights = new List<LightSpec>();
            foreach (float floor in new[] { F, 8f + F })
            {
                foreach (float x in new[] { 2.6f, 6.2f, 9.8f })
                {
                    lights.Add(LightSpec.Point(new Vector3(x, floor + 3.3f, 1.95f), 5.5f, 1.6f, white));    // 창가 길 위
                    lights.Add(LightSpec.Point(new Vector3(x, floor + 3.3f, -1.95f), 5.5f, 1.6f, white));
                }
                lights.Add(LightSpec.Point(new Vector3(-1.9f, floor + 3.2f, 0f), 5f, 1.4f, white));         // 엘리베이터 뒤
            }
            lights.Add(LightSpec.Point(new Vector3(5.8f, 8f + F + 2.6f, 0f), 4f, 0.8f, new Color(0.8f, 1f, 0.75f)));   // 재배 기둥 사이 (초록빛)
            foreach (var (x, z) in new[] { (1.8f, 0f), (-1.8f, 0f), (0f, 1.8f), (0f, -1.8f) })
                lights.Add(LightSpec.Point(new Vector3(x, 16f + F + 3.1f, z), 4.5f, 1.2f, white));          // 배양실
            foreach (var (x, z) in new[] { (2.3f, 2.3f), (2.3f, -2.3f), (-2.3f, 2.3f) })
                lights.Add(LightSpec.Point(new Vector3(x, 16f + F + 1.7f, z), 2.6f, 0.7f, warm));           // 배양 통 빛
            var orbits = new List<OrbitSpec>();
            var towers = new[] { 3.4f, 5.0f, 6.6f, 8.2f };
            for (int k = 0; k < towers.Length; k++)
                orbits.Add(new OrbitSpec($"INT_EvoFarm_Tower{k + 1}", new Vector3(towers[k], 0f, 0f), Vector3.up, k % 2 == 0 ? 8f : -8f));   // 이웃끼리 반대로 천천히
            var vats = new[] { new Vector3(2.3f, 0f, 2.3f), new Vector3(2.3f, 0f, -2.3f), new Vector3(-2.3f, 0f, 2.3f) };
            for (int k = 0; k < vats.Length; k++)
                orbits.Add(new OrbitSpec($"INT_EvoFarm_Bubbles{k + 1}", vats[k], Vector3.up, 150f));   // + 방향 = 나선이 위로 오르는 것처럼 보임 (수소 플랜트와 같음)
            EvoAccent("VerticalFarm", "#8FE07A");
            // 스폰 = +x 끝 문 안쪽에서 +z 창가 길과 선반을 바라봄
            Build("EvoFarm", "Evolved/MD_Evo_VerticalFarm", sockets, new Vector3(10.3f, F, 1.6f), -110f, lights, orbits,
                configure: root =>
                {
                    // 유리 엘리베이터 (거주 타워 · 중력 거주구와 같은 틀, 세 층 · 문이 +x를 봄)
                    var car = Child(root, "INT_EvoFarm_LiftCar");
                    var carGlass = Child(root, "INT_EvoFarm_LiftCarGlass");
                    carGlass.SetParent(car, true);
                    foreach (var t in new[] { car, carGlass })
                        t.gameObject.AddComponent<MeshCollider>().sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                    var lift = root.AddComponent<InteriorLift>();
                    var left = new Transform[3];
                    var right = new Transform[3];
                    for (int k = 0; k < 3; k++)
                    {
                        left[k] = Child(root, $"INT_EvoFarm_LiftDoor{k + 1}L");
                        right[k] = Child(root, $"INT_EvoFarm_LiftDoor{k + 1}R");
                        BoxOf(left[k], 0f);
                        BoxOf(right[k], 0f);
                        BoxOf(Child(root, $"INT_EvoFarm_LiftCall{k + 1}"), 0.06f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, true);
                        var button = Child(root, $"INT_EvoFarm_LiftBtn{k + 1}");
                        button.SetParent(car, true);
                        BoxOf(button, 0.03f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, false);
                    }
                    lift.Configure(car, new[] { 0f, 8f, 16f }, left, right, new Vector3(0f, F + 0.085f, 0f), Vector3.forward, 0.8f);
                });
        }

        /// <summary>
        /// 진화 1단계 중력 거주구 (3 × 3 + 꼬리 2칸, gh_builder — 사용자 2026-10-29 시안 3 확정 "유니티로 옮겨볼래?"):
        /// 가운데 칸 = 유리 엘리베이터 탑 (1층 F ↔ 2층 전망실 F + 2.9, <see cref="InteriorLift"/>) · 탑 둘레 허브 (벤치 · 유리 벽 · 미니 링 + 바퀴살이 돎 — INT_EvoGravity_Spokes),
        /// 2층 전망실 = 천장을 뚫고 솟은 둥근 방, 낮은 턱 위 360° 바깥 창 (INT_EvoGravity_Lookout — <see cref="InteriorLookoutWindow"/>: 자기 링이 보이게 탑 꼭대기 위에서 그림) ·
        /// 망원경 (<see cref="InteriorTelescope"/> — 머리 INT_EvoGravity_Scope, F로 들여다봄), 허브 밖 원형 통로 (창가 벤치 · 나무 화단), 꼬리 2칸 = 주민 골목 (작은 집 넷 · 둥근 탁자).
        /// 문 자리 = 연결점 있는 면 (8), 바닥 해치 = 칸마다 (가운데 (3.2, 0) · 모서리는 안쪽 대각선), 위 해치 = 연결점 있는 칸 (가운데 = 전망실 지붕 (−2.55, 0)), 천장 F + 4.0.
        /// </summary>
        private static void BuildEvoGravity()
        {
            const float ceiling = F + 4.0f;
            const float roof = F + 5.15f;   // gh_builder ROOF (전망실 지붕 아랫면)
            const float deck = 2.9f;        // 2층 높이 (LV[1] − F)
            const float lookoutLift = 3.8f;
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(new Vector3Int(1, 0, 0), new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.left, Socket),
                new InteriorSocket(new Vector3Int(2, 0, 1), Vector3Int.right, Socket),
                new InteriorSocket(new Vector3Int(1, 0, 3), Vector3Int.left, Socket),
                new InteriorSocket(new Vector3Int(1, 0, 3), Vector3Int.right, Socket),
                new InteriorSocket(new Vector3Int(1, 0, 4), Vector3Int.left, Socket),
                new InteriorSocket(new Vector3Int(1, 0, 4), Vector3Int.right, Socket),
                new InteriorSocket(new Vector3Int(1, 0, 4), new Vector3Int(0, 0, 1), Socket),
            };
            // 바닥 해치: 칸마다 (모서리 칸 중심은 둥근 벽 가까이 → 안쪽 대각선으로), 위 해치: 바깥 모델 윗면이 있는 칸 (모서리 칸 없음)
            foreach (var (cell, offset) in new[]
                     {
                         (new Vector3Int(0, 0, 0), new Vector3(0.6f, 0f, 0.6f)), (new Vector3Int(2, 0, 0), new Vector3(-0.6f, 0f, 0.6f)),
                         (new Vector3Int(0, 0, 2), new Vector3(0.6f, 0f, -0.6f)), (new Vector3Int(2, 0, 2), new Vector3(-0.6f, 0f, -0.6f)),
                         (new Vector3Int(1, 0, 0), Vector3.zero), (new Vector3Int(0, 0, 1), Vector3.zero), (new Vector3Int(2, 0, 1), Vector3.zero),
                         (new Vector3Int(1, 0, 2), Vector3.zero), (new Vector3Int(1, 0, 3), Vector3.zero), (new Vector3Int(1, 0, 4), Vector3.zero),
                         (new Vector3Int(1, 0, 1), new Vector3(3.2f, 0f, 0f)),   // 허브 +x 길 (벤치 · 화분 사이)
                     })
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, offset));
            foreach (var cell in new[] { new Vector3Int(1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(2, 0, 1), new Vector3Int(1, 0, 2), new Vector3Int(1, 0, 3), new Vector3Int(1, 0, 4) })
                sockets.Add(new InteriorSocket(cell, Vector3Int.up, ceiling, Vector3.zero));
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 1), Vector3Int.up, roof, new Vector3(-2.55f, 0f, 0f)));   // 전망실 지붕 (탑 둘레 빛 고리 밖)

            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.93f, 0.82f);
            var lights = new List<LightSpec>();
            foreach (var (x, z) in new[] { (12f, 8f), (4f, 8f), (8f, 12f), (8f, 4f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 2.4f, z), 6f, 1.6f, white));      // 허브 (전망실 바닥 밑)
            for (int k = 0; k < 8; k++)
            {
                float a = (22.5f + 45f * k) * Mathf.Deg2Rad;
                lights.Add(LightSpec.Point(new Vector3(8f + Mathf.Cos(a) * 9f, F + 3.4f, 8f + Mathf.Sin(a) * 9f), 6.5f, 1.8f, white));   // 원형 통로
            }
            lights.Add(LightSpec.Point(new Vector3(8f, F + 3.3f, 23.5f), 7f, 2.0f, warm));     // 골목
            lights.Add(LightSpec.Point(new Vector3(8f, F + 3.3f, 31f), 7f, 2.0f, warm));
            for (int k = 0; k < 3; k++)
            {
                float a = (30f + 120f * k) * Mathf.Deg2Rad;
                lights.Add(LightSpec.Point(new Vector3(8f + Mathf.Cos(a) * 2.4f, F + 4.8f, 8f + Mathf.Sin(a) * 2.4f), 4f, 1.0f, white));   // 전망실
            }
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoGravity_Spokes", new Vector3(8f, 0f, 8f), Vector3.up, 12f),   // 허브 미니 링 + 바퀴살 (바깥 회전 링처럼 천천히)
            };
            EvoAccent("GravityHabitat", "#9FD8FF");
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Maplestory SDF.asset");
            // 스폰 = −z 문 안쪽에서 허브 · 탑을 바라봄 (바닥 해치 (8, 0) 앞)
            Build("EvoGravity", "Evolved/MD_Evo_GravityHabitat", sockets, new Vector3(8f, F, -1.8f), 0f, lights, orbits,
                configure: root =>
                {
                    // 유리 엘리베이터 (거주 타워와 같은 틀): 캡슐 · 층 문 · 호출 버튼 · 캡슐 안 층 버튼
                    var car = Child(root, "INT_EvoGravity_LiftCar");
                    var carGlass = Child(root, "INT_EvoGravity_LiftCarGlass");
                    carGlass.SetParent(car, true);
                    foreach (var t in new[] { car, carGlass })
                        t.gameObject.AddComponent<MeshCollider>().sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                    var lift = root.AddComponent<InteriorLift>();
                    var left = new Transform[2];
                    var right = new Transform[2];
                    for (int k = 0; k < 2; k++)
                    {
                        left[k] = Child(root, $"INT_EvoGravity_LiftDoor{k + 1}L");
                        right[k] = Child(root, $"INT_EvoGravity_LiftDoor{k + 1}R");
                        BoxOf(left[k], 0f);
                        BoxOf(right[k], 0f);
                        BoxOf(Child(root, $"INT_EvoGravity_LiftCall{k + 1}"), 0.06f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, true);
                        var button = Child(root, $"INT_EvoGravity_LiftBtn{k + 1}");
                        button.SetParent(car, true);
                        BoxOf(button, 0.03f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, false);
                    }
                    lift.Configure(car, new[] { 0f, deck }, left, right, new Vector3(8f, F + 0.085f, 8f), Vector3.right, 0.8f);
                    // 전망실 바깥 창: 탑 꼭대기 위에서 그림 (자기 링 보임)
                    var lookout = Child(root, "INT_EvoGravity_Lookout");
                    lookout.gameObject.AddComponent<MeshCollider>().sharedMesh = lookout.GetComponent<MeshFilter>().sharedMesh;
                    lookout.gameObject.AddComponent<InteriorLookoutWindow>().Lift = lookoutLift;
                    // 망원경: 머리 경계 상자 (시선 광선) + 들여다보기
                    var head = Child(root, "INT_EvoGravity_Scope");
                    BoxOf(head, 0.06f);
                    root.AddComponent<InteriorTelescope>().Configure(head, new Vector3(8f, F + deck + 1.3f, 11f), 8f, lookoutLift, font);
                });
        }

        /// <summary>
        /// 진화 1단계 수소 플랜트 (ㄱ자 3칸, hp_builder — 사용자 2026-10-29 시안 1 확정 "이대로 옮겨줘"): 모서리 칸 전기분해조 — 흰 기둥 두른 유리 통 · 빛나는 물 ·
        /// 전극 둘 위로 천장까지 쌍둥이 유리관, 전극 · 관을 따라 나선 거품이 돌아 솟는 것처럼 보임 (INT_EvoHydrogen_BubblesA 수소 · BubblesB 산소 — 움직이는 메인 부품),
        /// +x 칸 = 누운 연료전지 더미 + 따라 도는 빛 고리 (Pulse) · 출력 장 · 흐름 제어대, +z 칸 = 물 구체 · 산소 구체 · 화분 둘, 천장 밑 수소 · 산소 · 물 관.
        /// 문 자리 = 연결점 있는 면 모두 (8), 바닥 · 위 해치 = 칸마다 (모서리 칸 (2.75, ·, −2.1) · +x 칸 (5.2, ·, 0) — 장치를 비킴), 천장 F + 4.0.
        /// </summary>
        private static void BuildEvoHydrogen()
        {
            const float ceiling = F + 4.0f;
            var corner = new Vector3Int(0, 0, 0);
            var east = new Vector3Int(1, 0, 0);
            var north = new Vector3Int(0, 0, 1);
            var hatch0 = new Vector3(2.75f, 0f, -2.1f);
            var hatch1 = new Vector3(5.2f - 8f, 0f, 0f);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(corner, Vector3Int.left, Socket),
                new InteriorSocket(corner, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(corner, Vector3Int.down, -F, hatch0),
                new InteriorSocket(corner, Vector3Int.up, ceiling, hatch0),
                new InteriorSocket(east, Vector3Int.right, Socket),
                new InteriorSocket(east, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(east, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(east, Vector3Int.down, -F, hatch1),
                new InteriorSocket(east, Vector3Int.up, ceiling, hatch1),
                new InteriorSocket(north, Vector3Int.left, Socket),
                new InteriorSocket(north, Vector3Int.right, Socket),
                new InteriorSocket(north, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(north, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(north, Vector3Int.up, ceiling, Vector3.zero),
            };
            var white = new Color(1f, 0.97f, 0.92f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-1.9f, F + 2.85f, 0f), 6f, 2.0f, white),       // 전기분해조 −x
                LightSpec.Point(new Vector3(1.6f, F + 2.85f, 1.6f), 5f, 1.4f, white),       // 모서리 안쪽 (두 날개 이음)
                LightSpec.Point(new Vector3(0f, F + 0.9f, 1.5f), 2.5f, 0.6f, new Color(0.6f, 0.9f, 1f)),   // 물 빛
                LightSpec.Point(new Vector3(8.8f, F + 2.85f, -1.6f), 6f, 1.8f, white),      // 연료전지
                LightSpec.Point(new Vector3(8.8f, F + 2.85f, 1.9f), 6f, 1.8f, white),
                LightSpec.Point(new Vector3(8.15f, F + 2.4f, 0f), 3f, 0.6f, new Color(1f, 0.92f, 0.7f)),   // 더미 위
                LightSpec.Point(new Vector3(-1.9f, F + 2.85f, 8.4f), 6f, 1.8f, white),      // 저장 칸
                LightSpec.Point(new Vector3(1.9f, F + 2.85f, 8.4f), 6f, 1.8f, white),
            };
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoHydrogen_BubblesA", new Vector3(0.5f, 0f, 0f), Vector3.up, 150f),   // + 방향 = 나선이 위로 오르는 것처럼 보임
                new OrbitSpec("INT_EvoHydrogen_BubblesB", new Vector3(0f, 0f, 0.5f), Vector3.up, 150f),
                new OrbitSpec("INT_EvoHydrogen_Pulse", new Vector3(8.15f, F + 1.2f, 0f), Vector3.right, 60f),
            };
            EvoAccent("HydrogenPlant", "#7FE3FF");
            // 스폰 = 모서리 칸 −x 문 옆에서 전기분해조를 바라봄
            Build("EvoHydrogen", "Evolved/MD_Evo_HydrogenPlant", sockets, new Vector3(-1.9f, F, 2.4f), 140f, lights, orbits);
        }

        /// <summary>
        /// 진화 1단계 융합 발전소 (3칸 일자, fs_builder — 사용자 2026-10-29 시안 1 확정 "이대로 옮겨줘"): 가운데 융합로 — 유리 돔 아래 흰 받침 ·
        /// 유리 도넛 그릇 속에서 도는 플라스마 고리 (INT_EvoFusion_Plasma — 움직이는 메인 부품) · 파란 코일 열둘 · 솔레노이드 기둥 + 냉각수 머리 · 받침 테 위 유리 울타리,
        /// 냉각수 구체 둘 (x 2.4 · 13.6 — 관 둘씩이 천장 밑을 지나 융합로 머리로) + 구체와 융합로 사이 칸에 제어대 (−z) · 냉각수 펌프 (+z) · 자석 전원 (−z) ·
        /// 남는 물 탱크 (+z) · 양 끝 정수 필터 기둥 넷. 사용자 2026-10-29 "길을 지나다닐 수가 없네" → 홀을 따라 걷는 길 폭 1.3m 이상 (융합로 · 구체 옆은 비움).
        /// 문 자리 = 끝 칸 바깥 면 + 칸마다 ±z, 바닥 해치 = 칸 중심, 위 해치 = 가운데 칸만 (돔을 비켜 (4.7, ·, −2.2)), 천장 F + 4.0.
        /// </summary>
        private static void BuildEvoFusion()
        {
            const float ceiling = F + 4.0f;
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 3; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                if (x == 0)
                    sockets.Add(new InteriorSocket(cell, Vector3Int.left, Socket));
                if (x == 2)
                    sockets.Add(new InteriorSocket(cell, Vector3Int.right, Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, Vector3.zero));
            }
            sockets.Add(new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.up, ceiling, new Vector3(4.7f - 8f, 0f, -2.2f)));
            var white = new Color(1f, 0.97f, 0.92f);
            var plasma = new Color(0.85f, 0.5f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(0f, F + 2.85f, 0f), 6.5f, 2.2f, white),       // −x 끝 (정수 필터)
                LightSpec.Point(new Vector3(16f, F + 2.85f, 0f), 6.5f, 2.2f, white),      // +x 끝
                LightSpec.Point(new Vector3(4.9f, F + 2.85f, -1.9f), 5f, 1.6f, white),    // 제어대
                LightSpec.Point(new Vector3(4.9f, F + 2.85f, 1.9f), 5f, 1.4f, white),     // 펌프
                LightSpec.Point(new Vector3(11.1f, F + 2.85f, -1.9f), 5f, 1.4f, white),   // 자석 전원
                LightSpec.Point(new Vector3(11.1f, F + 2.85f, 1.9f), 5f, 1.4f, white),    // 물 탱크
                LightSpec.Point(new Vector3(8f, F + 1.6f, 1.25f), 3.5f, 0.8f, plasma),    // 플라스마 빛
                LightSpec.Point(new Vector3(8f, F + 1.6f, -1.25f), 3.5f, 0.8f, plasma),
                LightSpec.Point(new Vector3(8f, F + 2.9f, 1.6f), 4f, 1.0f, white),        // 돔 아래 융합로 위쪽
                LightSpec.Point(new Vector3(8f, F + 2.9f, -1.6f), 4f, 1.0f, white),
            };
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoFusion_Plasma", new Vector3(8f, 0f, 0f), Vector3.up, 45f),   // 도넛 그릇 속 플라스마 고리
            };
            EvoAccent("FusionStation", "#FF9F5A");
            // 스폰 = −x 끝 (바닥 해치 (0, 0)과 문 사이)에서 융합로 쪽을 바라봄
            Build("EvoFusion", "Evolved/MD_Evo_FusionStation", sockets, new Vector3(-1.8f, F, 0f), 90f, lights, orbits,
                configure: root =>
                {
                    // 고리의 밝은 덩이 여섯: 흰 공이 잡음 플라즈마를 가려 → 같은 잡음의 더 뜨거운 플라즈마 (사용자 2026-11-02 "제대로 된 플라즈마")
                    var plasma = Child(root, "INT_EvoFusion_Plasma").GetComponent<MeshRenderer>();
                    var hot = AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.PlasmaHotPath);
                    var mats = plasma.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] != null && mats[i].name == "M_InteriorLight")
                            mats[i] = hot;
                    }
                    plasma.sharedMaterials = mats;
                });
        }

        /// <summary>
        /// 진화 1단계 태양광 어레이 (2 × 2칸, sa_builder — 사용자 2026-10-29 시안 1 확정 "실제 방향에 맞춰주고 옮겨줘"): 가운데 추적 가대 —
        /// 바닥 구동부 (모터 넷이 문 금색 톱니 원판) 위 흰 기둥이 천장 우물 유리 고리를 지나 머리까지, 머리 가로축 둘에 패널 줄 (PanelA · PanelB).
        /// <see cref="InteriorSunTracker"/>가 바깥 패널처럼 실제 해 쪽으로 돌리고 기울임 (Mast = 해 방위로 돎, 패널 줄 = 축마다 최대 38°).
        /// 둘레 = 관제석 (−z: 기둥을 보는 곡면 책상 · 의자 셋) · 패널 정비대 (+z: 예비 패널 · 로봇 팔 · 공구 수레) · 변환 장치 (+x: 창 아래 장 둘) ·
        /// 해 추적 홀로 탁자 (−x: INT_EvoSolar_Holo가 돎) + 모서리 전력 기둥 넷 · 화분 여섯.
        /// 문 자리 = 칸마다 바깥 두 면, 바닥 해치 = 칸 중심, 위 연결점 없음 (패널 자리, 천장 F + 4.0).
        /// </summary>
        private static void BuildEvoSolar()
        {
            const float pivotY = F + 4.62f; // sa_builder PIVOT_Y
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                for (int z = 0; z < 2; z++)
                {
                    var cell = new Vector3Int(x, 0, z);
                    sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                    sockets.Add(new InteriorSocket(cell, z == 0 ? new Vector3Int(0, 0, -1) : new Vector3Int(0, 0, 1), Socket));
                    sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, Vector3.zero));
                }
            }
            var white = new Color(1f, 0.97f, 0.92f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(4f, F + 2.85f, -1.6f), 6.5f, 2.2f, white),    // 관제석
                LightSpec.Point(new Vector3(4f, F + 2.85f, 9.6f), 6.5f, 2.2f, white),     // 정비대
                LightSpec.Point(new Vector3(-1.6f, F + 2.85f, 4f), 6f, 2.0f, white),      // 홀로 탁자
                LightSpec.Point(new Vector3(9.6f, F + 2.85f, 4f), 6f, 2.0f, white),       // 변환 장치
                LightSpec.Point(new Vector3(4f, F + 2.4f, 2.5f), 4f, 1.0f, new Color(1f, 0.9f, 0.7f)),   // 구동부 · 기둥 (앞뒤)
                LightSpec.Point(new Vector3(4f, F + 2.4f, 5.5f), 4f, 1.0f, new Color(1f, 0.9f, 0.7f)),
                LightSpec.Point(new Vector3(4f, F + 4.3f, 4f), 2.6f, 0.6f, new Color(0.85f, 0.92f, 1f)), // 우물 속 패널 아랫면
                LightSpec.Point(new Vector3(-0.6f, F + 1.4f, 4f), 1.5f, 0.3f, new Color(0.6f, 0.85f, 1f)), // 홀로 빛
            };
            foreach (var (x, z) in new[] { (-1.6f, -1.6f), (9.6f, -1.6f), (-1.6f, 9.6f), (9.6f, 9.6f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 2.6f, z), 4f, 1.0f, white));            // 네 모서리 (전력 기둥)
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoSolar_Holo", new Vector3(-0.6f, 0f, 4f), Vector3.up, 20f),   // 해 구슬을 따라 도는 작은 어레이 (홀로 = 연출)
            };
            EvoAccent("SolarArray", "#FFD86B");
            // 스폰 = +x 변환 장치 앞에서 추적 가대를 바라봄 (해치 (8, 0) · (8, 8) 사이)
            Build("EvoSolar", "Evolved/MD_Evo_SolarArray", sockets, new Vector3(9.3f, F, 4f), -90f, lights, orbits, configure: root =>
            {
                var t = root.transform;
                root.AddComponent<InteriorSunTracker>().Configure(t.Find("INT_EvoSolar_Mast"),
                    new[] { t.Find("INT_EvoSolar_PanelA"), t.Find("INT_EvoSolar_PanelB") },
                    new Vector3(4f, 0f, 4f), new[] { new Vector3(4f, pivotY, 4.85f), new Vector3(4f, pivotY, 3.15f) });
            });
        }

        /// <summary>
        /// 진화 1단계 문화 센터 (2 × 2칸, cc_builder — 사용자 2026-10-29 시안 1 확정): 가운데 천체 투영실 (천장 구멍 위 짙은 돔 속에서 별 하늘 ·
        /// 행성 · 투영기 머리가 함께 도는 INT_EvoCulture_Sky — 움직이는 메인 부품, 투영기를 보는 곡면 벤치 두 겹) + 교실 (−z: 홀로 칠판 · 분침이 도는
        /// 벽시계 INT_EvoCulture_Clock · 책상 여섯) + 놀이 (+z: 보드게임 탁자 둘) · 독서 (+x: 책장 · 안락의자) · 쉼터 (−x: 긴 벤치 · 화분).
        /// 문 자리 = 칸마다 바깥 두 면, 해치 = 칸 중심 (천장 F + 4.0).
        /// </summary>
        private static void BuildEvoCulture()
        {
            const float ceiling = F + 4.0f;
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                for (int z = 0; z < 2; z++)
                {
                    var cell = new Vector3Int(x, 0, z);
                    sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                    sockets.Add(new InteriorSocket(cell, z == 0 ? new Vector3Int(0, 0, -1) : new Vector3Int(0, 0, 1), Socket));
                    sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, Vector3.zero));
                    sockets.Add(new InteriorSocket(cell, Vector3Int.up, ceiling, Vector3.zero));
                }
            }
            var white = new Color(1f, 0.97f, 0.92f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(4f, F + 2.85f, -1.6f), 6.5f, 2.3f, white),    // 교실
                LightSpec.Point(new Vector3(4f, F + 2.85f, 9.6f), 6.5f, 2.2f, white),     // 놀이
                LightSpec.Point(new Vector3(-1.6f, F + 2.85f, 4f), 6f, 2.0f, white),      // 쉼터
                LightSpec.Point(new Vector3(9.6f, F + 2.85f, 4f), 6f, 2.0f, white),       // 독서
                LightSpec.Point(new Vector3(4f, F + 2.2f, 4f), 4.5f, 0.8f, new Color(1f, 0.85f, 0.6f)),   // 투영실 (돔 아래 은은하게)
                LightSpec.Point(new Vector3(4f, F + 1.7f, -2.5f), 2.5f, 0.4f, new Color(0.6f, 0.85f, 1f)), // 칠판 빛
            };
            foreach (var (x, z) in new[] { (-1.6f, -1.6f), (9.6f, -1.6f), (-1.6f, 9.6f), (9.6f, 9.6f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 2.6f, z), 4f, 1.0f, white));            // 네 모서리
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoCulture_Sky", new Vector3(4f, 0f, 4f), Vector3.up, 4f),
                new OrbitSpec("INT_EvoCulture_Clock", new Vector3(4f, F + 3.02f, -3.2f), Vector3.forward, 6f),   // 1분에 한 바퀴 (앞에서 보면 시계 방향)
            };
            EvoAccent("CultureCenter", "#FFC86B");
            // 스폰 = −x 쉼터 벤치 앞에서 천체 투영실을 바라봄 (해치 (0, 0) · (0, 8) 사이)
            Build("EvoCulture", "Evolved/MD_Evo_CultureCenter", sockets, new Vector3(-1.8f, F, 4f), 90f, lights, orbits);
        }

        /// <summary>
        /// 진화 1단계 복지 센터 (ㄱ자 3칸, wc_builder — 사용자 2026-10-29 시안 1 확정): 마음 쉼터 (0번 칸 — 연결점 없는 바깥 두 면을 둥근 유리 벽으로,
        /// 나무 화단 · 원형 벤치 · 드럼 천창 아래 도는 빛 고리 모빌 INT_EvoWelfare_Halo) + 몸 돌봄 (+x 날개 — 침대를 감싼 스캔 고리가 도는 침대 둘
        /// GantryA · GantryB) + 여가 (+z 날개 — 창을 보는 소파 · 홀로 놀이 탁자 위 도는 빛 픽셀 Holo · 빛 픽셀 천장).
        /// 문 자리 = 연결점이 있는 면만 (0번 칸 −x · −z 없음), 해치 = 칸 중심 (0번 칸은 드럼을 비켜 (2.5, ·, 2.5)), 천장 F + 4.0.
        /// </summary>
        private static void BuildEvoWelfare()
        {
            const float ceiling = F + 4.0f;
            var rotunda = new Vector3Int(0, 0, 0);
            var medical = new Vector3Int(1, 0, 0);
            var leisure = new Vector3Int(0, 0, 1);
            var hatch0 = new Vector3(2.5f, 0f, 2.5f);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(rotunda, Vector3Int.down, -F, hatch0),
                new InteriorSocket(rotunda, Vector3Int.up, ceiling, hatch0),
                new InteriorSocket(medical, Vector3Int.right, Socket),
                new InteriorSocket(medical, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(medical, new Vector3Int(0, 0, -1), Socket),
                new InteriorSocket(medical, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(medical, Vector3Int.up, ceiling, Vector3.zero),
                new InteriorSocket(leisure, Vector3Int.right, Socket),
                new InteriorSocket(leisure, Vector3Int.left, Socket),
                new InteriorSocket(leisure, new Vector3Int(0, 0, 1), Socket),
                new InteriorSocket(leisure, Vector3Int.down, -F, Vector3.zero),
                new InteriorSocket(leisure, Vector3Int.up, ceiling, Vector3.zero),
            };
            var white = new Color(1f, 0.97f, 0.97f);
            var pink = new Color(1f, 0.72f, 0.88f);
            // 흰 조명은 천장(F + 4.0)에서 1.1 이상 내려 둠 — 0.8 아래에 두었더니 천장에 하얗게 번진 얼룩 (게임 안 확인 2026-10-29)
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(0f, F + 3.2f, 0f), 6f, 2.2f, white),          // 정자 (드럼 속)
                LightSpec.Point(new Vector3(-1.7f, F + 2.8f, -1.7f), 4f, 1.1f, white),    // 정자 둥근 유리 쪽
                LightSpec.Point(new Vector3(0f, F + 2.8f, 0f), 3.5f, 0.6f, pink),         // 빛 고리 모빌
                LightSpec.Point(new Vector3(4.8f, F + 2.85f, 0f), 6.5f, 2.3f, white),     // 의료 날개
                LightSpec.Point(new Vector3(9.6f, F + 2.85f, 0f), 5.5f, 2.0f, white),
                LightSpec.Point(new Vector3(0f, F + 2.85f, 5.0f), 6.5f, 2.2f, white),     // 여가 날개
                LightSpec.Point(new Vector3(0f, F + 2.85f, 9.8f), 5.5f, 1.9f, white),
                LightSpec.Point(new Vector3(0f, F + 1.5f, 5.2f), 2.5f, 0.5f, pink),       // 홀로 탁자
            };
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoWelfare_Halo", Vector3.zero, Vector3.up, 6f),
                new OrbitSpec("INT_EvoWelfare_GantryA", new Vector3(5.1f, F + 0.86f, -2f), Vector3.right, 45f),
                new OrbitSpec("INT_EvoWelfare_GantryB", new Vector3(5.1f, F + 0.86f, 2f), Vector3.right, -45f),
                new OrbitSpec("INT_EvoWelfare_Holo", new Vector3(0f, 0f, 5.2f), Vector3.up, 25f),
            };
            EvoAccent("WelfareCenter", "#FF9ED8");
            // 스폰 = 의료 날개 (바닥 해치 (8, 0) 앞)에서 스캔 침대 사이로 정자를 바라봄
            Build("EvoWelfare", "Evolved/MD_Evo_WelfareCenter", sockets, new Vector3(6.7f, F, 0f), -90f, lights, orbits);
        }

        /// <summary>
        /// 진화 1단계 축전 뱅크 (3칸 일자, pb_builder — 사용자 2026-10-29 시안 1 확정): 유리 볼트 홀 (외형 유리 캡슐) + 띠 아치 둘 (x 4 · 12, 벽 따라 바닥까지 띠 기둥).
        /// 머리 위 거대 셀 두 줄 (z ±1.7, 축 F + 3.95)의 셀 심 10칸 = 정거장 저장량 게이지 (INT_EvoPowerBank_Gauge_00 ~ 09 → <see cref="InteriorGauge"/>,
        /// −x 끝부터 켜짐) · 셀마다 도는 충전 고리 셋 (RingA · RingB — 움직이는 메인 부품) · 띠 아치 아래 모선 다리 + 빛 기둥 + 충 · 방전 제어대 둘 ·
        /// 창 아래 예비 셀 선반. 문 자리 = 양 끝 + 칸마다 앞뒤 + 칸마다 바닥 해치 (칸 중심) · 볼트 꼭대기 등줄기 해치 (F + 5.1).
        /// </summary>
        private static void BuildEvoPowerBank()
        {
            const float cellY = F + 3.95f;
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 3; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                if (x == 0)
                    sockets.Add(new InteriorSocket(cell, Vector3Int.left, Socket));
                if (x == 2)
                    sockets.Add(new InteriorSocket(cell, Vector3Int.right, Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, Vector3.zero));
                sockets.Add(new InteriorSocket(cell, Vector3Int.up, F + 5.1f, Vector3.zero));
            }
            var white = new Color(0.95f, 0.96f, 1f);
            var amber = new Color(1f, 0.85f, 0.5f);
            var lights = new List<LightSpec>();
            foreach (float x in new[] { -1.5f, 2f, 6f, 10f, 14f, 17.5f })
                lights.Add(LightSpec.Point(new Vector3(x, F + 3.2f, 0f), 6.5f, 2.8f, white));
            foreach (float x in new[] { 4f, 12f })
            {
                foreach (float z in new[] { -1.7f, 1.7f })
                    lights.Add(LightSpec.Point(new Vector3(x, F + 3.3f, z), 3.5f, 0.7f, amber));   // 셀 빛
                lights.Add(LightSpec.Point(new Vector3(x, F + 1.5f, 0f), 2.5f, 0.5f, amber));      // 제어대 · 빛 기둥
            }
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoPowerBank_RingA", new Vector3(0f, cellY, -1.7f), Vector3.right, 35f),
                new OrbitSpec("INT_EvoPowerBank_RingB", new Vector3(0f, cellY, 1.7f), Vector3.right, -35f),
            };
            EvoAccent("PowerBank", "#FFD24A");
            // 스폰 = 서쪽 끝 문 앞 (바닥 해치 자리 ±0.95 밖)에서 셀 두 줄 · 제어대가 늘어선 홀을 바라봄
            Build("EvoPowerBank", "Evolved/MD_Evo_PowerBank", sockets, new Vector3(-2.0f, F, 0f), 90f, lights, orbits);
        }

        /// <summary>
        /// 진화 1단계 생태 모듈 (ㄱ자 4칸, eco_builder — 사용자 2026-10-29 시안 1): 온실 세 칸 (낮은 유리 볼트 · 재배대 두 줄 — 통로 쪽 수경 농장 상추,
        /// 벽 쪽 화분 식물 자리) + 이음 칸 (나무 화분 둘 · 수확 수레) + 끝 칸 순환 코어 (유리 물 구체 둘레를 도는 고리 = INT_EvoEco_Ring · 산소 기둥 넷 ·
        /// 온실로 이어지는 순환관). 문 자리 = 칸마다 바깥 면 + 바닥 · 천장 해치 (온실 = 칸 중심 · 볼트 꼭대기 등줄기 F + 5.1,
        /// 끝 칸 = 물 구체를 비켜 (0, ·, −2.4) · 천장 F + 4.0).
        /// </summary>
        private static void BuildEvoEco()
        {
            var sockets = new List<InteriorSocket>();
            var hall = new[] { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(2, 0, 0) };
            var wing = new Vector3Int(0, 0, 1);
            var back = new Vector3Int(0, 0, -1);
            var fwd = new Vector3Int(0, 0, 1);
            sockets.Add(new InteriorSocket(hall[0], Vector3Int.left, Socket));
            foreach (var c in hall)
            {
                sockets.Add(new InteriorSocket(c, back, Socket));
                if (c.x > 0)
                    sockets.Add(new InteriorSocket(c, fwd, Socket));
                sockets.Add(new InteriorSocket(c, Vector3Int.down, -F, Vector3.zero));
                sockets.Add(new InteriorSocket(c, Vector3Int.up, F + 5.1f, Vector3.zero));
            }
            sockets.Add(new InteriorSocket(hall[2], Vector3Int.right, Socket));
            var wingHatch = new Vector3(0f, 0f, -2.4f);
            sockets.Add(new InteriorSocket(wing, Vector3Int.left, Socket));
            sockets.Add(new InteriorSocket(wing, Vector3Int.right, Socket));
            sockets.Add(new InteriorSocket(wing, fwd, Socket));
            sockets.Add(new InteriorSocket(wing, Vector3Int.down, -F, wingHatch));
            sockets.Add(new InteriorSocket(wing, Vector3Int.up, F + 4.0f, wingHatch));
            var white = new Color(0.96f, 1f, 0.96f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-0.5f, F + 3.4f, 0f), 7f, 2.4f, white),
                LightSpec.Point(new Vector3(5.5f, F + 3.6f, 0f), 7f, 2.4f, white),
                LightSpec.Point(new Vector3(12f, F + 3.6f, 0f), 7f, 2.4f, white),
                LightSpec.Point(new Vector3(17.5f, F + 3.6f, 0f), 7f, 2.4f, white),
                LightSpec.Point(new Vector3(0f, F + 3.4f, 6.0f), 6f, 2.0f, white),
                LightSpec.Point(new Vector3(0f, F + 3.4f, 10.6f), 5f, 1.6f, white),
                LightSpec.Point(new Vector3(0f, F + 2.0f, 8.8f), 4f, 1.2f, new Color(0.5f, 0.85f, 1f)),   // 물 구체 빛
            };
            foreach (float x in new[] { 5.6f, 12f, 17.7f })
                foreach (float z in new[] { -1.65f, 1.65f })
                    lights.Add(LightSpec.Point(new Vector3(x, F + 2.2f, z), 3f, 0.7f, new Color(0.7f, 1f, 0.75f)));   // 재배등 아래
            float s12 = Mathf.Sin(12f * Mathf.Deg2Rad);
            var orbits = new List<OrbitSpec> { new OrbitSpec("INT_EvoEco_Ring", new Vector3(0f, F + 2.0f, 8.8f), new Vector3(s12, Mathf.Cos(12f * Mathf.Deg2Rad), 0f), 14f) };
            EvoAccent("EcoModule", "#7CFF9A");
            // 스폰 = 이음 칸 (바닥 해치 칸 중심 옆)에서 온실 통로와 순환 코어가 같이 보이게
            Build("EvoEco", "Evolved/MD_Evo_EcoModule", sockets, new Vector3(-1.0f, F, -1.4f), 60f, lights, orbits);
        }

        /// <summary>
        /// 진화 1단계 거주 타워 (2 × 2 칸 × 2층, ht_builder — 사용자 2026-10-29 시안 1 확정): 가운데 트인 아트리움을 유리 엘리베이터가 오르내리는 3개 층 —
        /// 1층 (F) 공용 라운지 · 식당 / 2층 (F + 4.4) 네 모서리 집 / 3층 (8 + F) 옥상 돔 라운지. 층 이동 = 유리 엘리베이터 탑승
        /// (<see cref="InteriorLift"/> — 사용자 결정 2026-10-29: 캡슐이 실제로 오르내리고 플레이어도 함께 실림).
        /// 문 자리 = 칸마다 바깥 두 면 + 아래 칸 바닥 해치 · 위 칸 천장 해치 (칸 중심, 위 칸 천장 = 위 칸 기준 F + 4.4).
        /// </summary>
        private static void BuildEvoTower()
        {
            const float upper = 8f + F;
            var sockets = new List<InteriorSocket>();
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 2; x++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        var cell = new Vector3Int(x, y, z);
                        sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                        sockets.Add(new InteriorSocket(cell, z == 0 ? new Vector3Int(0, 0, -1) : new Vector3Int(0, 0, 1), Socket));
                        sockets.Add(y == 0 ? new InteriorSocket(cell, Vector3Int.down, -F, Vector3.zero) : new InteriorSocket(cell, Vector3Int.up, F + 4.4f, Vector3.zero));
                    }
                }
            }
            var warm = new Color(1f, 0.96f, 0.9f);
            var lights = new List<LightSpec>();
            foreach (var (floor, h) in new[] { (F, 3.5f), (F + 4.4f, 2.8f), (upper, 3.7f) })
            {
                foreach (var (x, z) in new[] { (-0.6f, 4f), (8.6f, 4f), (4f, -0.6f), (4f, 8.6f) })
                    lights.Add(LightSpec.Point(new Vector3(x, floor + h, z), 6.5f, 2.6f, warm));
            }
            lights.Add(LightSpec.Point(new Vector3(4f, F + 2.4f, 8.2f), 4f, 1.2f, new Color(1f, 0.85f, 0.65f)));   // 부엌 섬 매단 조명
            lights.Add(LightSpec.Point(new Vector3(4f, upper + 4.6f, 4f), 6f, 1.5f, new Color(0.8f, 0.9f, 1f)));   // 옥상 돔
            EvoAccent("HabitatTower", "#7FD8FF");
            // 스폰 = 1층 앞(−z) 가운데에서 엘리베이터를 바라봄 (바닥 해치 칸 중심 (0,0) · (8,0) 사이)
            Build("EvoTower", "Evolved/MD_Evo_HabitatTower", sockets, new Vector3(4f, F, 0.6f), 0f, lights,
                configure: root =>
                {
                    var car = Child(root, "INT_EvoTower_LiftCar");
                    var carGlass = Child(root, "INT_EvoTower_LiftCarGlass");
                    carGlass.SetParent(car, true);
                    foreach (var t in new[] { car, carGlass })
                        t.gameObject.AddComponent<MeshCollider>().sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                    var lift = root.AddComponent<InteriorLift>();
                    var left = new Transform[3];
                    var right = new Transform[3];
                    for (int k = 0; k < 3; k++)
                    {
                        left[k] = Child(root, $"INT_EvoTower_LiftDoor{k + 1}L");
                        right[k] = Child(root, $"INT_EvoTower_LiftDoor{k + 1}R");
                        BoxOf(left[k], 0f);
                        BoxOf(right[k], 0f);
                        BoxOf(Child(root, $"INT_EvoTower_LiftCall{k + 1}"), 0.06f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, true);
                        var button = Child(root, $"INT_EvoTower_LiftBtn{k + 1}");
                        button.SetParent(car, true);
                        BoxOf(button, 0.03f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, false);
                    }
                    lift.Configure(car, new[] { 0f, 4.4f, 8f }, left, right, new Vector3(4f, F + 0.085f, 4f), Vector3.right, 0.85f);
                });
        }

        /// <summary>
        /// 공용 상태 화면 (사용자 2026-10-30 "주민이 보는 화면에 아무것도 나오지 않아 아쉽다" → 공용 화면 + 다른 진화 모듈도,
        /// 같은 날 "다른 단계의 모듈들도 미리 세팅" · "기본 모듈도" → 모든 템플릿): 고정 조각의 화면 재질(<see cref="IsStatusScreenMaterial"/>) 면을
        /// 덩어리(같은 자리 꼭짓점으로 이어진 삼각형)마다 나눠, 가장 넓은 평면 한 쌍 중 보이는 앞면 (<see cref="Visible"/> — 면 앞 45cm 안에 뒤판 · 몸체 ·
        /// 다른 장비가 없고 아래를 보지 않는 쪽)을 화면 칸으로 <see cref="InteriorStatusScreens"/>에 넘긴다. 위 · 아래를 보는 면, 작은 면 (가로 0.18m · 세로 0.1m 미만),
        /// 사각형을 85% 못 채우는 면 (둥근 면 · 흩어진 작은 면), 전용 화면(허브 안내 · 상황판 · 창구판 등 다른 <see cref="InteriorRoomDisplay"/>)이 그리는 자리는 뺀다.
        /// 기본 모듈처럼 어두운 모니터 면(ScreenDark)에 빛 줄 · 조각을 붙여 화면 무늬를 낸 곳은 빛 조각이 붙은 면을 앞면으로 보고 덮는다 (<see cref="TryDarkPanel"/>).
        /// 좌표 = 템플릿 루트 기준. 이미 붙은 상태 화면은 새로 찾은 칸으로 바꾼다 (칸이 없으면 뗌). 바뀌었으면 true.
        /// </summary>
        private static bool AddStatusScreens(GameObject root, Material back)
        {
            var panels = FindStatusPanels(root);
            var old = root.GetComponent<InteriorStatusScreens>();
            if (old != null)
                Object.DestroyImmediate(old, true);
            if (panels.Count > 0)
            {
                root.AddComponent<InteriorStatusScreens>().Configure(panels.ToArray(), back);
                Debug.Log($"[InteriorTemplateBuilder] {root.name}: 상태 화면 {panels.Count}칸");
            }
            return old != null || panels.Count > 0;
        }

        /// <summary>
        /// 상태 화면을 붙일 빛나는 화면 재질 (어두운 모니터 면은 <see cref="TryDarkPanel"/>이 따로): M_InteriorScreen으로 시작하는 재질 중 어두운 판(ScreenDark) · 격자 무늬(ScreenGrid) 말고 전부.
        /// 단계 키트가 화면 재질을 새로 만들 때 (예: 2단계 M_InteriorScreenNight) 이 이름 규칙만 따르면 그대로 잡힌다 (MODELING 7-11).
        /// </summary>
        private static bool IsStatusScreenMaterial(Material m) =>
            m != null && m.name.StartsWith("M_InteriorScreen") && !m.name.StartsWith("M_InteriorScreenDark") && !m.name.StartsWith("M_InteriorScreenGrid");

        private static List<InteriorStatusScreens.Panel> FindStatusPanels(GameObject root)
        {
            var panels = new List<InteriorStatusScreens.Panel>();
            // 전용 화면이 쓰는 자리 (각 표시의 직렬화된 Center · _center · _kioskCenter 같은 이름의 점 — 표시마다 따로 적지 않게)
            var reserved = new List<Vector3>();
            foreach (var display in root.GetComponentsInChildren<InteriorRoomDisplay>(true))
            {
                if (display is InteriorStatusScreens)
                    continue;
                var it = new SerializedObject(display).GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType == SerializedPropertyType.Vector3 && it.name.IndexOf("center", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        reserved.Add(it.vector3Value);
                }
            }
            // 고정 조각 (도는 조각 · 해 따라 기우는 조각 · 칸 게이지는 뺌 — 칸 좌표가 템플릿 루트 기준으로 멈춰 있어야 함)
            var parts = new List<Transform>();
            foreach (Transform part in root.transform)
            {
                if (part.GetComponent<MeshFilter>() != null && part.GetComponent<MeshRenderer>() != null
                    && part.GetComponent<InteriorOrbit>() == null && !part.name.Contains("_Gauge_"))
                    parts.Add(part);
            }
            var original = root.transform.position;
            bool backfaces = Physics.queriesHitBackfaces;
            var temporary = new List<Collider>();
            root.transform.position = new Vector3(0f, -5000f, 0f);   // 씬의 다른 물체와 떨어뜨려 광선 검사
            Physics.queriesHitBackfaces = true;                       // 뒷면이 다른 몸체 속에 묻힌 화면도 "막힘"으로
            try
            {
                // 충돌체 없는 조각(소품 · 책상 모니터)도 앞뒤 판정에 들어가게 잠깐 충돌체
                foreach (var part in parts)
                {
                    if (part.GetComponent<Collider>() == null)
                    {
                        var c = part.gameObject.AddComponent<MeshCollider>();
                        c.sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
                        temporary.Add(c);
                    }
                }
                Physics.SyncTransforms();
                // ① 빛나는 화면 면 (진화 모듈 콘솔 · 책상 화면) + 그 꼭짓점 (②에서 어두운 화면 위 빛 줄 찾기)
                var glow = new List<(Vector3 Point, Material Material)>();
                var darkIslands = new List<(Transform Part, Vector3[] Verts, List<int> Island)>();
                foreach (var part in parts)
                {
                    var mesh = part.GetComponent<MeshFilter>().sharedMesh;
                    var mats = part.GetComponent<MeshRenderer>().sharedMaterials;
                    Vector3[] verts = null;
                    for (int sub = 0; sub < mats.Length && sub < mesh.subMeshCount; sub++)
                    {
                        bool dark = mats[sub] != null && mats[sub].name.StartsWith("M_InteriorScreenDark");
                        if (!dark && !IsStatusScreenMaterial(mats[sub]))
                            continue;
                        verts ??= mesh.vertices;
                        var tris = mesh.GetTriangles(sub);
                        if (dark)
                        {
                            foreach (var island in Islands(tris, verts))
                                darkIslands.Add((part, verts, island));
                            continue;
                        }
                        foreach (int index in tris)
                            glow.Add((root.transform.InverseTransformPoint(part.TransformPoint(verts[index])), mats[sub]));
                        foreach (var island in Islands(tris, verts))
                        {
                            if (!TryPanel(root.transform, part, verts, island, out var panel) || Reserved(panel, reserved))
                                continue;
                            panel.Bar = mats[sub];
                            panels.Add(panel);
                        }
                    }
                }
                // ② 어두운 모니터 면 위에 빛 줄 · 조각(가짜 화면 무늬)이 붙은 화면 (기본 모듈 콘솔 · 진화 축전 · 융합 콘솔 — 사용자 "기본 모듈도"):
                //    빛 조각이 3cm 안에 붙은 쪽이 앞면. 정보창은 빛 조각보다 앞으로 띄워 덮는다. 이미 빛나는 화면 칸이 올라간 모니터는 뺌
                int glowPanels = panels.Count;
                foreach (var (part, verts, island) in darkIslands)
                {
                    if (TryDarkPanel(root.transform, part, verts, island, glow, out var panel) && !Reserved(panel, reserved)
                        && !HasPanelOn(panel, panels, glowPanels))
                        panels.Add(panel);
                }
            }
            finally
            {
                foreach (var c in temporary)
                    Object.DestroyImmediate(c);
                root.transform.position = original;
                Physics.queriesHitBackfaces = backfaces;
                Physics.SyncTransforms();
            }
            return panels;
        }

        /// <summary>전용 화면 점이 이 칸 사각형 (가장자리 10cm 여유) 안 · 면에서 25cm 안에 있으면 그 화면은 이미 쓰는 중.</summary>
        private static bool Reserved(InteriorStatusScreens.Panel panel, List<Vector3> reserved)
        {
            var right = Vector3.Cross(panel.Normal, panel.Up).normalized;
            foreach (var point in reserved)
            {
                var d = point - panel.Center;
                if (Mathf.Abs(Vector3.Dot(d, panel.Normal)) < 0.25f && Mathf.Abs(Vector3.Dot(d, right)) < panel.Size.x * 0.5f + 0.1f
                    && Mathf.Abs(Vector3.Dot(d, panel.Up)) < panel.Size.y * 0.5f + 0.1f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 이미 만든 내부 프리팹 전부에 상태 화면만 다시 붙이기 (메뉴 SpaceStation/Interior/Add Status Screens, 2026-10-30 — 기본 모듈 템플릿은
        /// 전체 Build Templates가 모든 프리팹 ID를 새로 매기므로 다시 만들지 않고 화면만). 광선 검사는 씬에 잠깐 꺼낸 복사본으로 하고, 결과를 프리팹에 넣는다.
        /// </summary>
        [MenuItem("SpaceStation/Interior/Add Status Screens")]
        public static void AddStatusScreensToPrefabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[InteriorTemplateBuilder] 플레이 모드를 끈 뒤 실행하세요.");
                return;
            }
            var back = AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ScreenDarkPath);
            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("PF_Interior_ t:Prefab", new[] { PrefabFolder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || !prefab.name.StartsWith("PF_Interior_"))
                    continue;
                var copy = (GameObject)Object.Instantiate(prefab);
                copy.name = prefab.name;
                var panels = FindStatusPanels(copy);
                Object.DestroyImmediate(copy);
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var old = contents.GetComponent<InteriorStatusScreens>();
                    if (old == null && panels.Count == 0)
                        continue;
                    if (old != null)
                        Object.DestroyImmediate(old, true);
                    if (panels.Count > 0)
                        contents.AddComponent<InteriorStatusScreens>().Configure(panels.ToArray(), back);
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    changed++;
                    Debug.Log($"[InteriorTemplateBuilder] {prefab.name}: 상태 화면 {panels.Count}칸");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[InteriorTemplateBuilder] 상태 화면 다시 붙임: 프리팹 {changed}개");
        }

        /// <summary>삼각형 목록 → 같은 자리 꼭짓점으로 이어진 덩어리들 (평면 음영이라 면마다 꼭짓점이 갈라져 있어 자리로 잇는다).</summary>
        private static List<List<int>> Islands(int[] tris, Vector3[] verts)
        {
            int n = tris.Length / 3;
            var parent = new int[n];
            for (int i = 0; i < n; i++)
                parent[i] = i;
            int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
            var owner = new Dictionary<Vector3Int, int>();
            for (int t = 0; t < n; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    var v = verts[tris[t * 3 + k]];
                    var key = new Vector3Int(Mathf.RoundToInt(v.x * 1000f), Mathf.RoundToInt(v.y * 1000f), Mathf.RoundToInt(v.z * 1000f));
                    if (owner.TryGetValue(key, out int o))
                        parent[Find(t)] = Find(o);
                    else
                        owner[key] = t;
                }
            }
            var groups = new Dictionary<int, List<int>>();
            for (int t = 0; t < n; t++)
            {
                int r = Find(t);
                if (!groups.TryGetValue(r, out var list))
                    groups[r] = list = new List<int>();
                list.Add(tris[t * 3]); list.Add(tris[t * 3 + 1]); list.Add(tris[t * 3 + 2]);
            }
            return new List<List<int>>(groups.Values);
        }

        /// <summary>빛나는 화면 덩어리 하나 → 화면 칸 (후보 면 중 앞 4cm가 빈 쪽 = 앞면).</summary>
        private static bool TryPanel(Transform root, Transform part, Vector3[] verts, List<int> island, out InteriorStatusScreens.Panel panel)
        {
            foreach (var face in Faces(root, part, verts, island))
            {
                if (!Visible(root, face, 0f))
                    continue;   // 앞에 뒤판 · 테 · 몸체 → 뒷면, 또는 다른 장비에 가려짐
                panel = face;
                return true;
            }
            panel = default;
            return false;
        }

        /// <summary>
        /// 보이는 화면 면인지: 아래를 보는 면이 아니고 (기울어진 키오스크 밑면 — 창고), 면 앞 45cm가 트여 있음 (바로 앞 4cm = 뒤판 · 몸체 → 뒷면,
        /// 그 너머 = 다른 장비에 가려진 화면 — 정련소). lift = 어두운 화면 위 빛 조각 두께 (그만큼 앞에서부터 잼).
        /// </summary>
        private static bool Visible(Transform root, InteriorStatusScreens.Panel face, float lift)
        {
            if (face.Normal.y < -0.3f)
                return false;
            var worldN = root.TransformDirection(face.Normal);
            return !Physics.Raycast(root.TransformPoint(face.Center) + worldN * (lift + 0.003f), worldN, 0.45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 어두운 모니터 덩어리 하나 → 화면 칸: 후보 면 중 빛 조각 꼭짓점 4개 이상이 면 위 3cm 안 · 면 사각형 안에 있는 쪽 (가짜 화면 무늬가 붙은 쪽 = 앞면).
        /// 정보창은 가장 앞의 빛 조각보다 3mm 앞으로 띄움 (Lift), 막대는 그 빛 조각 재질. 세로 1.2m 넘거나 아주 길쭉한 판 (축전기 캐비닛 · 띠)은 뺌.
        /// </summary>
        private static bool TryDarkPanel(Transform root, Transform part, Vector3[] verts, List<int> island, List<(Vector3 Point, Material Material)> glow,
            out InteriorStatusScreens.Panel panel)
        {
            panel = default;
            foreach (var face in Faces(root, part, verts, island))
            {
                if (face.Size.y > 1.2f || face.Size.x < face.Size.y * 0.5f || face.Size.x > face.Size.y * 4.5f)
                    return false;
                var right = Vector3.Cross(face.Normal, face.Up).normalized;
                float lift = 0f;
                int count = 0;
                Material bar = null;
                foreach (var (point, material) in glow)
                {
                    var d = point - face.Center;
                    float dn = Vector3.Dot(d, face.Normal);
                    if (dn < -0.003f || dn > 0.03f || Mathf.Abs(Vector3.Dot(d, right)) > face.Size.x * 0.5f + 0.01f
                        || Mathf.Abs(Vector3.Dot(d, face.Up)) > face.Size.y * 0.5f + 0.01f)
                        continue;
                    count++;
                    lift = Mathf.Max(lift, dn);
                    bar = material;
                }
                if (count < 4 || !Visible(root, face, lift))
                    continue;
                panel = face;
                panel.Lift = lift + 0.003f;
                panel.Dark = true;
                panel.Bar = bar;
                return true;
            }
            return false;
        }

        /// <summary>앞의 칸들(빛나는 화면) 중 이 어두운 판 위에 이미 올라간 것이 있는지 — 진화 연구 단지 모니터처럼 빛 블록에 칸이 있으면 판은 그대로.</summary>
        private static bool HasPanelOn(InteriorStatusScreens.Panel dark, List<InteriorStatusScreens.Panel> panels, int count)
        {
            var right = Vector3.Cross(dark.Normal, dark.Up).normalized;
            for (int i = 0; i < count; i++)
            {
                var d = panels[i].Center - dark.Center;
                if (Mathf.Abs(Vector3.Dot(d, dark.Normal)) < 0.05f && Mathf.Abs(Vector3.Dot(d, right)) < dark.Size.x * 0.5f + 0.02f
                    && Mathf.Abs(Vector3.Dot(d, dark.Up)) < dark.Size.y * 0.5f + 0.02f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 덩어리 하나의 화면 후보 면: 가장 넓은 옆을 보는 평면 + 그 반대 면 (판의 앞 · 뒤). 작은 면 · 사각형을 85% 못 채우는 면이면 없음.
        /// </summary>
        private static List<InteriorStatusScreens.Panel> Faces(Transform root, Transform part, Vector3[] verts, List<int> island)
        {
            var faces = new List<InteriorStatusScreens.Panel>(2);
            var area = new Dictionary<Vector3Int, float>();
            var normals = new Dictionary<Vector3Int, Vector3>();
            var pts = new Dictionary<Vector3Int, List<Vector3>>();
            for (int i = 0; i < island.Count; i += 3)
            {
                var a = root.InverseTransformPoint(part.TransformPoint(verts[island[i]]));
                var b = root.InverseTransformPoint(part.TransformPoint(verts[island[i + 1]]));
                var c = root.InverseTransformPoint(part.TransformPoint(verts[island[i + 2]]));
                var cr = Vector3.Cross(b - a, c - a);
                float ar = cr.magnitude * 0.5f;
                if (ar < 1e-6f)
                    continue;
                var nrm = cr.normalized;
                var key = new Vector3Int(Mathf.RoundToInt(nrm.x * 50f), Mathf.RoundToInt(nrm.y * 50f), Mathf.RoundToInt(nrm.z * 50f));
                area[key] = (area.TryGetValue(key, out float s) ? s : 0f) + ar;
                normals[key] = nrm;
                if (!pts.TryGetValue(key, out var list))
                    pts[key] = list = new List<Vector3>();
                list.Add(a); list.Add(b); list.Add(c);
            }
            Vector3Int best = default;
            float bestArea = 0f;
            foreach (var kv in area)
            {
                if (Mathf.Abs(normals[kv.Key].y) > 0.7f || kv.Value <= bestArea)
                    continue;
                best = kv.Key;
                bestArea = kv.Value;
            }
            if (bestArea <= 0f)
                return faces;
            var candidates = new List<Vector3Int> { best };
            var opposite = -best;
            foreach (var kv in area)
            {
                if ((kv.Key - opposite).sqrMagnitude <= 2 && kv.Value > bestArea * 0.5f)
                {
                    candidates.Add(kv.Key);
                    break;
                }
            }
            foreach (var key in candidates)
            {
                var n = normals[key];
                var up = Vector3.ProjectOnPlane(Vector3.up, n).normalized;
                var right = Vector3.Cross(n, up).normalized;
                float r0 = float.MaxValue, r1 = float.MinValue, u0 = float.MaxValue, u1 = float.MinValue, d = 0f;
                foreach (var p in pts[key])
                {
                    float r = Vector3.Dot(p, right), u = Vector3.Dot(p, up);
                    r0 = Mathf.Min(r0, r); r1 = Mathf.Max(r1, r); u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u);
                    d += Vector3.Dot(p, n);
                }
                d /= pts[key].Count;
                var center = right * ((r0 + r1) * 0.5f) + up * ((u0 + u1) * 0.5f) + n * d;
                var size = new Vector2(r1 - r0, u1 - u0);
                if (size.x < 0.18f || size.y < 0.1f)   // 연구 단지 책상 화면 = 0.30 × 0.12 (경계에 걸려 반만 잡히던 것 → 0.1)
                    break;
                // 같은 방향 작은 면 여럿 (콘솔 줄 · 관 옆면)이나 둥근 면은 사각형을 다 채우지 못함 → 화면 아님 (태양광 · 수소에서 잘못 잡힘)
                if (area[key] < size.x * size.y * 0.85f)
                    break;
                faces.Add(new InteriorStatusScreens.Panel { Center = center, Normal = n, Up = up, Size = size });
            }
            return faces;
        }

        /// <summary>조각 메시 경계 상자 충돌체 (조각은 템플릿 원점에 놓인 채 꼭짓점이 제자리 — 경계 = 템플릿 좌표). pad = 가장자리 여유 (누르기 쉽게).</summary>
        private static BoxCollider BoxOf(Transform part, float pad)
        {
            var bounds = part.GetComponent<MeshFilter>().sharedMesh.bounds;
            var box = part.gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size + Vector3.one * pad;
            return box;
        }

        /// <summary>
        /// 진화 모듈 강조색 `M_Accent_Evo_{Id}.mat` (없으면 만듦) — 기본 모듈 강조 재질과 같은 꼴 (URP Lit · 바탕 = UI 색 · 발광 = 바탕 × 0.6).
        /// 내부 템플릿의 Accent 슬롯 · 키트 문 패널 띠(InteriorSetup가 M_Accent_*를 모듈마다 등록)에 쓰임.
        /// </summary>
        private static void EvoAccent(string id, string hex)
        {
            string path = AccentFolder + "M_Accent_Evo_" + id + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null || !ColorUtility.TryParseHtmlString(hex, out var color))
                return;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", 0.3f);
            m.SetFloat("_Smoothness", 0.6f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * 0.6f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // None이면 URP 재질 검사가 _EMISSION을 꺼 버림
            m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, path);
        }

        /// <summary>
        /// 진화 1단계 연구 단지 (2칸, rc_builder — 사용자 2026-10-29 시안 1 확정): 둥근 모서리 홀 (천장 F + 4.0) · 1단계 키트 파노라마 창 띠
        /// (네 모서리 곡면 창 + 긴 벽 가운데 창) · 양 끝 천창 · 가운데 천장 구멍 위 유리 관측 돔. 가운데 관측 단 기둥 위 망원경이 돔 안에서 천천히 돌고
        /// (INT_EvoResearch_Scope), 기둥을 감싼 기운 궤도 고리 둘이 전자를 달고 돎 (RingA · RingB — 고리 + 전자 한 덩어리를 고리 축으로).
        /// 문 자리 = 양 끝 + 칸마다 앞뒤 + 칸마다 위 · 아래 해치 (칸 중심, 천장 높이 F + 4.0).
        /// </summary>
        private static void BuildEvoResearch()
        {
            const float ceiling = F + 4.0f;
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, Vector3.zero));
                sockets.Add(new InteriorSocket(cell, Vector3Int.up, ceiling, Vector3.zero));
            }
            var white = new Color(0.94f, 0.96f, 1f);
            var lavender = new Color(0.72f, 0.58f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-1.4f, F + 3.4f, 0f), 7f, 3.0f, white),   // 흰 세라믹 홀이 회색으로 가라앉지 않게 기본 방보다 밝게 (플레이 확인 2026-10-29)
                LightSpec.Point(new Vector3(9.4f, F + 3.4f, 0f), 7f, 3.0f, white),
                LightSpec.Point(new Vector3(2.2f, F + 3.3f, 0f), 5.5f, 2.3f, white),
                LightSpec.Point(new Vector3(5.8f, F + 3.3f, 0f), 5.5f, 2.3f, white),
                LightSpec.Point(new Vector3(4f, ceiling + 1.2f, 0f), 4.5f, 1.6f, white),       // 돔 속 망원경
                LightSpec.Point(new Vector3(4f, F + 0.6f, 0f), 3f, 0.6f, lavender),            // 관측 단 빛
            };
            foreach (var (x, z) in new[] { (-2.2f, 2.2f), (-2.2f, -2.2f), (10.2f, 2.2f), (10.2f, -2.2f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 2.6f, z), 3.2f, 1.1f, white)); // 모서리 연구 책상
            float s18 = Mathf.Sin(18f * Mathf.Deg2Rad), s15 = Mathf.Sin(15f * Mathf.Deg2Rad);
            var ring = new Vector3(4f, F + 3.05f, 0f);
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_EvoResearch_Scope", new Vector3(4f, 0f, 0f), Vector3.up, 5f),
                new OrbitSpec("INT_EvoResearch_RingA", ring, new Vector3(s18, Mathf.Cos(18f * Mathf.Deg2Rad), 0f), 20f),
                new OrbitSpec("INT_EvoResearch_RingB", ring, new Vector3(-s15 * 0.45f, Mathf.Cos(15f * Mathf.Deg2Rad), s15 * 0.9f).normalized, -14f),
            };
            EvoAccent("ResearchComplex", "#9E80FF");
            // 스폰 = 서쪽 끝 문 앞 (바닥 해치 자리 ±0.95 밖)에서 가운데 망원경을 바라봄
            Build("EvoResearch", "Evolved/MD_Evo_ResearchComplex", sockets, new Vector3(-2.0f, F, 0f), 90f, lights, orbits);
        }

        /// <summary>
        /// 12-5 새 기본 모듈 내부만 다시 만들기 (메뉴 SpaceStation/Interior/Build New Basic Templates).
        /// Blender `BlenderWork/Interior_NewBasic.blend`(텍스트 블록 `school_builder` 등) → `SM_Interior_X.fbx`.
        /// 전체 Build Templates는 모든 프리팹 ID를 새로 매기므로, 새 템플릿만 바꿀 때는 이 메뉴를 쓴다.
        /// </summary>
        [MenuItem("SpaceStation/Interior/Build New Basic Templates")]
        public static void BuildNewBasicOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[InteriorTemplateBuilder] 플레이 모드를 끈 뒤 실행하세요.");
                return;
            }
            InteriorSetup.CreateMaterials();
            BuildNewBasicTemplates();
            InteriorPlantSpots.SetAll();
            AssetDatabase.SaveAssets();
            InteriorSetup.Setup();
        }

        private static void BuildNewBasicTemplates()
        {
            BuildSchool();
            BuildStructureFrame();
            BuildConnectorHub();
            BuildSensorArray();
            BuildQuarantine();
            BuildProteinVat();
            BuildWaterExtractor();
            BuildDataCenter();
            BuildCommerce();
            BuildAdministration();
            BuildRadiationShelter();
            BuildPassengerPort();
            BuildRadiator();
            BuildCommRelay();
        }

        /// <summary>1칸 방 문 자리: 열린 수평 면(+x · −x · +z · −z) + 위 · 아래 해치(칸 중심에서 hatch만큼).</summary>
        private static List<InteriorSocket> OneCellSockets(float depth, Vector3 hatch, bool px = true, bool nx = true, bool pz = true, bool nz = true)
        {
            var cell = Vector3Int.zero;
            var list = new List<InteriorSocket>();
            if (px) list.Add(new InteriorSocket(cell, Vector3Int.right, depth));
            if (nx) list.Add(new InteriorSocket(cell, Vector3Int.left, depth));
            if (pz) list.Add(new InteriorSocket(cell, new Vector3Int(0, 0, 1), depth));
            if (nz) list.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), depth));
            list.Add(new InteriorSocket(cell, Vector3Int.down, -F, hatch));
            list.Add(new InteriorSocket(cell, Vector3Int.up, F + 3.6f, hatch));
            return list;
        }

        /// <summary>2칸(x 방향) 홀 문 자리: 양 끝 + 칸마다 앞뒤 + 칸마다 위 · 아래 해치.</summary>
        private static List<InteriorSocket> TwoCellSockets(Vector3 hatch)
        {
            var list = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                list.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                list.Add(new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket));
                list.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                list.Add(new InteriorSocket(cell, Vector3Int.down, -F, hatch));
                list.Add(new InteriorSocket(cell, Vector3Int.up, F + 3.6f, hatch));
            }
            return list;
        }

        /// <summary>12-5 학교 (2칸): 가운데 교실(홀로그램 칠판 · 책상 6) + 0번 칸 독서 · 지구본 구석 + 1번 칸 미술 · 가방 구석, 천장 행성 모빌(천천히 회전).</summary>
        private static void BuildSchool()
        {
            var warm = new Color(1f, 0.93f, 0.82f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(0f, F + 3.0f, 0f), 6.5f, 2.4f, warm),
                LightSpec.Point(new Vector3(4f, F + 3.0f, 0f), 6.5f, 2.8f, warm),
                LightSpec.Point(new Vector3(8f, F + 3.0f, 0f), 6.5f, 2.4f, warm),
                LightSpec.Point(new Vector3(4f, F + 1.8f, 2.3f), 3f, 0.8f, new Color(0.6f, 0.82f, 1f)), // 칠판 빛
            };
            var orbits = new List<OrbitSpec> { new OrbitSpec("INT_School_Mobile", new Vector3(-1.9f, 0f, 0f), Vector3.up, 8f) };
            Build("School", "MD_School", TwoCellSockets(Vector3.zero), new Vector3(-2.0f, F, 0f), 90f, lights, orbits);
        }

        /// <summary>12-5 구조 프레임: 트러스 속 전망 점검 통로 (걷는 십자 길 말고 전부 바깥 창). 문 자리 깊이 3.6 = 키트 문 벽이 유리벽 바깥 0.4,
        /// 창 너머에 자기 트러스가 보이게 (템플릿 ShowOwnExterior).</summary>
        private static void BuildStructureFrame()
        {
            var cool = new Color(0.78f, 0.86f, 1f);
            var lights = new List<LightSpec> { LightSpec.Point(new Vector3(0f, F + 3.0f, 0f), 6f, 1.8f, cool) };
            foreach (var (x, z) in new[] { (1.9f, 1.9f), (-1.9f, 1.9f), (1.9f, -1.9f), (-1.9f, -1.9f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 2.6f, z), 3.5f, 1.0f, cool));
            Build("Frame", "MD_StructureFrame", OneCellSockets(3.6f, Vector3.zero), new Vector3(0f, F, -1.6f), 0f, lights, showOwnExterior: true);
        }

        /// <summary>12-5 연결 허브: 환승 로비 (팔각, 문마다 방향 표지판 · 바닥 안내선, 안내 키오스크 + 화분 2).</summary>
        private static void BuildConnectorHub()
        {
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(0f, F + 3.1f, 0f), 6.5f, 2.6f, new Color(0.95f, 0.96f, 1f)),
                // 키오스크 화면 빛: 화면 면 뒤(키오스크 머리 뒤 · 아래)에 둠 → 화면 앞면에는 빛이 닿지 않아 반사점이 생길 수 없음.
                // 화면 앞 낮게 둔 자리도 위에서 내려다보면 화면 아래쪽에 반사점이 보였음 (2026-10-19 플레이 검수)
                LightSpec.Point(new Vector3(-2.25f, F + 1.0f, 2.25f), 3f, 0.8f, new Color(0.6f, 0.85f, 1f)),
            };
            Build("Hub", "MD_ConnectorHub", OneCellSockets(Socket, Vector3.zero), new Vector3(0f, F, -1.6f), 0f, lights,
                configure: root =>
                {
                    // hub_builder ANCHORS (문 위 표지판 글자 칸 · 키오스크 화면 좌표)
                    var n = new Vector3Int(0, 0, 1);
                    var s = new Vector3Int(0, 0, -1);
                    InteriorWayfinding.Sign SignAt(string key, Vector3Int dir, Vector3 c, Vector3 normal) => new InteriorWayfinding.Sign
                    {
                        Direction = dir, Center = c, Normal = normal, Width = 1.0f, Height = 0.2f,
                        Arrow = new[] { Child(root, "INT_Hub_Arrow" + key)?.GetComponent<Renderer>() },
                    };
                    var signs = new[]
                    {
                        SignAt("N", n, new Vector3(0.36f, 1.675f, 2.449f), new Vector3(0f, 0f, -1f)),
                        SignAt("E", Vector3Int.right, new Vector3(2.449f, 1.675f, -0.36f), new Vector3(-1f, 0f, 0f)),
                        SignAt("S", s, new Vector3(-0.36f, 1.675f, -2.449f), new Vector3(0f, 0f, 1f)),
                        SignAt("W", Vector3Int.left, new Vector3(-2.449f, 1.675f, 0.36f), new Vector3(1f, 0f, 0f)),
                    };
                    root.AddComponent<InteriorWayfinding>().Configure(signs,
                        new Vector3(-2.0765f, -0.0618f, 2.0765f), new Vector3(0.6409f, 0.4226f, -0.6409f),
                        new Vector3(-0.2988f, 0.9063f, 0.2988f), new Vector3(0.7071f, 0f, 0.7071f),
                        new[] { n, Vector3Int.right, s, Vector3Int.left, Vector3Int.up, Vector3Int.down },
                        new[] { 0.11f, 0.04f, -0.03f, -0.10f, -0.17f, -0.225f });
                });
        }

        /// <summary>화물 환적장 보트 처음 자리 (s, <see cref="CargoFlumeCourse"/>): 0 = 정차 · 대기 둘 (앞 보트 뒤 3.1씩).</summary>
        public static readonly float[] CargoBoatStarts = { 0f, CargoFlumeCourse.Loop - 3.1f, CargoFlumeCourse.Loop - 6.2f };

        /// <summary>화물 환적장 탄 주민 자리 (보트마다 뒷줄 둘, 처음 자세 · 엉덩이 = 방석 앞 끝 − 0.07) · 보트 번호.</summary>
        public static Vector3[] CargoRiderSeats()
        {
            var seats = new Vector3[CargoBoatStarts.Length * 2];
            for (int b = 0; b < CargoBoatStarts.Length; b++)
            {
                CargoFlumeCourse.Pose(CargoBoatStarts[b], out var p, out var r);
                for (int k = 0; k < 2; k++)
                {
                    var local = new Vector3(k == 0 ? -0.25f : 0.25f, 0.46f, -0.33f);
                    seats[b * 2 + k] = p + r * local;
                }
            }
            return seats;
        }

        public static readonly int[] CargoRiderBoats = { 0, 0, 1, 1, 2, 2 };

        /// <summary>
        /// 진화 2단계 화물 환적장 "은하수 후룸 항구" (3 × 2칸, ct_builder — 사용자 2026-11-09 시안 1 "유니티로 옮겨줘"): 직접 체험 = 후룸 보트 탑승 (<see cref="InteriorFlumeRide"/>).
        /// 외형 평면 × 8 그대로: 뒤쪽 은하수 항구 못 (화물선 둘 · 뒷벽 레일 크레인 둘 — B가 정차 보트 고물에 컨테이너를 싣고 내림) · 동쪽 부두 정차장 · 오른쪽 물길 · 회전 못 (등대) ·
        /// 체인 리프트 2.7 m · 산 뒤 능선 · 정상 터널 · 42° 큰 낙하 · 물보라 못 (<see cref="InteriorFlumeSplash"/>) · 앞 · 왼쪽 물길. 산 = 봉우리 여섯 (눈 모자 · 별), 산 속 실내 홀 둘 (별 동굴 라운지 · 관제실).
        /// 높이만 낮춤 (탄 사람 눈 · 터널 머리 공간이 3.95 아래). 문 자리 = (2,0,0) 뒷면 하나 (외형 연결점이 있으면 열림, 닫힘 = 난간), 아래 해치 6, 위 해치 없음 (하늘).
        /// 벤치 · 관람석에 앉음 (<see cref="InteriorBenchSeats"/>). 바깥 주민 = 우주복 (<see cref="InteriorOpenAir"/>), 산 속 홀 = 실내 상자.
        /// </summary>
        private static void BuildEvoCargoTransfer()
        {
            const float DeckEdge = 3.95f;
            const string N = "INT_EvoCargoTransfer_";
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(new Vector3Int(2, 0, 0), new Vector3Int(0, 0, -1), DeckEdge),                         // 뒷면 문 (남쪽 길)
                new InteriorSocket(new Vector3Int(0, 0, 0), Vector3Int.down, -F, new Vector3(1.40f, 0f, 0.40f)),        // 서쪽 광장
                new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.down, -F, new Vector3(2.40f, 0f, 1.05f)),        // 동남 광장 서쪽
                new InteriorSocket(new Vector3Int(2, 0, 0), Vector3Int.down, -F, new Vector3(-2.35f, 0f, 1.10f)),       // 동남 광장 동쪽
                new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.down, -F, new Vector3(1.20f, 0f, -1.15f)),       // 부두 만
                new InteriorSocket(new Vector3Int(1, 0, 1), Vector3Int.down, -F, new Vector3(-3.42f, 0f, -3.42f)),      // 라운지
                new InteriorSocket(new Vector3Int(2, 0, 1), Vector3Int.down, -F, new Vector3(-1.98f, 0f, -1.42f)),      // 정차장 동쪽
            };
            var warm = new Color(1f, 0.86f, 0.55f);
            var white = new Color(1f, 0.97f, 0.92f);
            var cool = new Color(0.72f, 0.82f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(2.40f, 0.60f, 4.15f), 5f, 0.5f, warm),      // 라운지
                LightSpec.Point(new Vector3(5.30f, 0.60f, 4.75f), 5f, 0.5f, warm),
                LightSpec.Point(new Vector3(10.30f, 0.60f, 4.25f), 5f, 0.5f, white),    // 관제실
                LightSpec.Point(new Vector3(12.60f, 0.70f, 4.10f), 5f, 0.5f, cool),
                LightSpec.Point(new Vector3(4.00f, 0.55f, 2.70f), 3f, 0.35f, warm),     // 현관 굴
                LightSpec.Point(new Vector3(12.00f, 0.55f, 2.75f), 3f, 0.35f, warm),
                LightSpec.Point(new Vector3(0.40f, 0.75f, 1.60f), 7f, 0.5f, warm),      // 서쪽 광장
                LightSpec.Point(new Vector3(4.70f, 0.75f, -1.00f), 7f, 0.5f, warm),
                LightSpec.Point(new Vector3(7.40f, 2.00f, -1.30f), 7f, 0.6f, cool),     // 물보라 못
                LightSpec.Point(new Vector3(14.70f, 0.75f, 2.55f), 6f, 0.5f, warm),     // 동남 등불 · 관람석 빛 대롱 · 남쪽 길
                LightSpec.Point(new Vector3(14.85f, 0.70f, -1.20f), 7f, 0.5f, warm),
                LightSpec.Point(new Vector3(12.00f, 0.70f, -2.55f), 6f, 0.45f, warm),
                LightSpec.Point(new Vector3(11.20f, 0.55f, 5.62f), 6f, 0.5f, warm),     // 정차장 바위 벽 등
                LightSpec.Point(new Vector3(13.70f, 0.55f, 5.52f), 6f, 0.5f, warm),
                LightSpec.Point(new Vector3(8.80f, 0.40f, 7.05f), 4f, 0.35f, warm),     // 바위 회랑
                LightSpec.Point(new Vector3(2.20f, 0.75f, 7.80f), 6f, 0.45f, warm),     // 서쪽 부두
                LightSpec.Point(new Vector3(5.90f, 0.75f, 7.80f), 6f, 0.45f, warm),
                LightSpec.Point(new Vector3(17.20f, 0.30f, -0.10f), 4f, 0.4f, cool),    // 회전 못 등대
                LightSpec.Point(new Vector3(16.50f, 0.80f, 10.60f), 7f, 0.5f, cool),    // 야적장
                LightSpec.Point(new Vector3(6.90f, 1.50f, 10.45f), 7f, 0.5f, cool),     // 화물선 사이
            };
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec(N + "Beacon", new Vector3(17.20f, 0f, -0.10f), Vector3.up, 60f),   // 등대 렌즈
            };
            EvoAccent("CargoTransfer", "#A0E8C0");
            // 스폰 = 서쪽 광장에서 정상 문 · 협곡 · 물보라 못을 바라봄
            Build("EvoCargoTransfer", "Evolved/MD_Evo_CargoTransfer", sockets, new Vector3(1.70f, F, -0.55f), 62f, lights, orbits,
                configure: root =>
                {
                    AddOpenAir(root, System.Array.Empty<Vector3>());
                    root.GetComponent<InteriorOpenAir>().SetIndoorBox(new Rect(0.70f, 3.25f, 13.60f, 2.40f), 0.70f);   // 산 속 홀 둘 (낙하 · 터널을 지나는 주민 발은 그 위라 바깥)
                    var r = root.transform;
                    var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Maplestory SDF.asset");
                    Transform Pivot(string pivotName, Transform parent, Vector3 at, Quaternion rot, string part)
                    {
                        var p = new GameObject(pivotName).transform;
                        p.SetParent(parent, false);
                        p.position = r.TransformPoint(at);
                        p.rotation = r.rotation * rot;
                        if (part != null)
                        {
                            var mesh = Child(root, part);
                            if (mesh != null)
                                mesh.SetParent(p, true);
                        }
                        return p;
                    }
                    // 컨테이너: 모델 하나 (정차 보트 고물, 바닥 가운데) → 화물선 칸 · 보트 고물에 복제, 몸통 색만 바꿈
                    var containerMesh = Child(root, N + "Container");
                    var containerBottom = new Vector3(CargoFlumeCourse.StationX - 0.925f, CargoFlumeCourse.LowWater + 0.15f, CargoFlumeCourse.LaneZ);
                    var violet = AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2VioletPath);
                    var bodies = new[]
                    {
                        AssetDatabase.LoadAssetAtPath<Material>(AccentFolder + "M_Paint_Evo_CargoTransfer.mat"),
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.EvoWhitePath),
                        violet,
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2GoldPath),
                    };
                    int crateCount = 0;
                    Transform Crate(Vector3 bottom, int color)
                    {
                        var pivot = Pivot("Crate" + crateCount++, r, bottom, Quaternion.identity, null);
                        var mesh = Object.Instantiate(containerMesh.gameObject, pivot).transform;
                        mesh.name = "Container";
                        mesh.localPosition = -containerBottom;
                        mesh.localRotation = Quaternion.identity;
                        var rend = mesh.GetComponent<MeshRenderer>();
                        var mats = rend.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            if (mats[i] == violet && bodies[color % bodies.Length] != null)
                                mats[i] = bodies[color % bodies.Length];
                        }
                        rend.sharedMaterials = mats;
                        return pivot;
                    }
                    // 화물선 칸 8 (ct_builder freighter: x = 배 중심 + −0.6 · 0 · 0.6 · 1.2, z 9.99 · 10.89, 갑판 −0.87) — 앞 다섯 = 상자 든 칸
                    const float DeckY = -0.87f;
                    var order = new[] { (0, 0), (0, 1), (1, 1), (2, 1), (3, 0), (1, 0), (2, 0), (3, 1) };
                    var colors = new[] { 0, 1, 3, 1, 2 };
                    float[] dxs = { -0.6f, 0f, 0.6f, 1.2f };
                    float[] zs = { 9.99f, 10.89f };
                    Vector3[] ShipSlots(float cx)
                    {
                        var slots = new Vector3[order.Length];
                        for (int i = 0; i < order.Length; i++)
                            slots[i] = new Vector3(cx + dxs[order[i].Item1], DeckY, zs[order[i].Item2]);
                        return slots;
                    }
                    InteriorStorageCrane.Rig Crane(string tag, float x, float minX, float maxX, float lift)
                    {
                        const float zr = 11.575f, tz = 10.44f;
                        var bridge = Pivot("Crane" + tag + "BridgePivot", r, new Vector3(x, 1.7f, zr), Quaternion.identity, N + "Crane" + tag + "_Bridge");
                        var trolley = Pivot("Crane" + tag + "TrolleyPivot", bridge, new Vector3(x, 1.58f, tz), Quaternion.identity, N + "Crane" + tag + "_Trolley");
                        var cable = Pivot("Crane" + tag + "CablePivot", trolley, new Vector3(x, 1.53f, tz), Quaternion.identity, N + "Crane" + tag + "_Cable");
                        var grip = Pivot("Crane" + tag + "GripPivot", trolley, new Vector3(x, 0f, tz), Quaternion.identity, N + "Crane" + tag + "_Grip");
                        return new InteriorStorageCrane.Rig
                        {
                            Bridge = bridge, Trolley = trolley, Cable = cable, Grip = grip,
                            RestX = x, RestZ = tz, MinX = minX, MaxX = maxX,
                            GripRestY = 0f, GripFrameHeight = 0.06f, CableTopY = 1.53f, CableLength = 1f, CrateHeight = 0.40f, LiftSpeed = lift,
                        };
                    }
                    var slotsA = ShipSlots(3.56f);
                    var cratesA = new Transform[5];
                    for (int i = 0; i < 5; i++)
                        cratesA[i] = Crate(slotsA[i], colors[i]);
                    root.AddComponent<InteriorStorageCrane>().Configure(Crane("A", 3.56f, 2.9f, 4.8f, 0.3f), cratesA, slotsA);
                    // 보트 셋 (피벗 = 흘수선 가운데, +z = 앞): 0번 = 모델 (정차 자세), 1 · 2 = 복제. 객실 색 0 = Paint · 1 = Evo2Violet · 2 = EvoWhite, 고물 컨테이너 0 · 2 = 실음
                    CargoFlumeCourse.Pose(0f, out var dockPos, out var dockRot);
                    int n = CargoBoatStarts.Length;
                    var boats = new Transform[n];
                    var seats = new Transform[n * 2];
                    var barsF = new Transform[n];
                    var barsR = new Transform[n];
                    var anchors = new Transform[n];
                    var cargo = new Transform[n];
                    var boat0 = Pivot("Boat0_Pivot", r, dockPos, dockRot, N + "Boat");
                    Child(root, N + "BoatSoft").SetParent(boat0, true);
                    var hingeF = new GameObject("BarF").transform;
                    hingeF.SetParent(boat0, false);
                    hingeF.localPosition = new Vector3(0f, 0.62f, 0.62f);
                    Child(root, N + "BoatBarF").SetParent(hingeF, true);
                    var hingeR = new GameObject("BarR").transform;
                    hingeR.SetParent(boat0, false);
                    hingeR.localPosition = new Vector3(0f, 0.62f, -0.20f);
                    Child(root, N + "BoatBarR").SetParent(hingeR, true);
                    for (int k = 0; k < 2; k++)
                    {
                        var seat = new GameObject(k == 0 ? "SeatL" : "SeatR").transform;
                        seat.SetParent(boat0, false);
                        seat.localPosition = new Vector3(k == 0 ? -0.25f : 0.25f, 0.53f, 0.38f);
                        seat.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.46f, 0.15f, 0.36f);
                    }
                    var anchor0 = new GameObject("CargoAnchor").transform;
                    anchor0.SetParent(boat0, false);
                    anchor0.localPosition = new Vector3(0f, 0.15f, -0.925f);
                    anchor0.localRotation = Quaternion.Inverse(dockRot);   // 정차 자세에서 템플릿과 같은 방향 (컨테이너 긴 변 = 템플릿 z)
                    var cabinMats = new[] { null, violet, AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.EvoWhitePath) };
                    var paint = bodies[0];
                    for (int b = 0; b < n; b++)
                    {
                        var boat = b == 0 ? boat0 : Object.Instantiate(boat0.gameObject, r).transform;
                        boat.name = $"Boat{b}_Pivot";
                        if (b > 0 && cabinMats[b] != null)
                        {
                            var rend = boat.Find(N + "Boat").GetComponent<MeshRenderer>();
                            var mats = rend.sharedMaterials;
                            for (int i = 0; i < mats.Length; i++)
                            {
                                if (mats[i] == paint)
                                    mats[i] = cabinMats[b];
                            }
                            rend.sharedMaterials = mats;
                        }
                        boats[b] = boat;
                        barsF[b] = boat.Find("BarF");
                        barsR[b] = boat.Find("BarR");
                        seats[b * 2] = boat.Find("SeatL");
                        seats[b * 2 + 1] = boat.Find("SeatR");
                        anchors[b] = boat.Find("CargoAnchor");
                    }
                    for (int b = 0; b < n; b++)
                    {
                        if (b == 1)
                            continue;   // 컨테이너는 복제가 끝난 뒤 실음 (먼저 실으면 복제 보트에 크레인이 모르는 상자가 따라감)
                        var crate = Crate(containerBottom, b == 0 ? 3 : 0);
                        crate.SetParent(anchors[b], true);
                        cargo[b] = crate;
                    }
                    containerMesh.gameObject.SetActive(false);   // 원본은 복제용
                    // 크레인 B: 화물선 B 칸 다섯 + 보트 고물 상자 둘 (정차 일 — 빈 보트에 싣고 실린 보트에서 내림)
                    var slotsB = ShipSlots(10.20f);
                    var cratesB = new List<Transform>();
                    var colorsB = new[] { 2, 0, 1, 3, 0 };
                    for (int i = 0; i < 5; i++)
                        cratesB.Add(Crate(slotsB[i], colorsB[i]));
                    foreach (var c in cargo)
                    {
                        if (c != null)
                            cratesB.Add(c);
                    }
                    var craneB = root.AddComponent<InteriorStorageCrane>();
                    craneB.Configure(Crane("B", CargoFlumeCourse.StationX - 0.925f, 9.55f, 11.45f, 0.4f), cratesB.ToArray(), slotsB, 5);
                    // 문짝 (정차장 못 벽 위, −x로 주머니 속)
                    var gate = Child(root, N + "Gate");
                    BoxOf(gate, 0f);
                    // 체인 고리 17 (모델 = 리프트 시작 + 0.3 자세)
                    const int Lugs = 17;
                    var lugs = new Transform[Lugs];
                    float lugS = CargoFlumeCourse.LiftStart + 0.3f;
                    CargoFlumeCourse.Pose(lugS, out var lugPos, out var lugRot);
                    lugPos.y = CargoFlumeCourse.Water(lugS);
                    lugs[0] = Pivot("Lug0", r, lugPos, lugRot, N + "ChainLug");
                    for (int k = 1; k < Lugs; k++)
                    {
                        lugs[k] = Object.Instantiate(lugs[0].gameObject, r).transform;
                        lugs[k].name = "Lug" + k;
                    }
                    var splash = root.AddComponent<InteriorFlumeSplash>();
                    splash.Configure(ModuleFxMaterials.Get("FireworkSpark"), ModuleFxMaterials.Get("Ripple"));
                    var starts = (float[])CargoBoatStarts.Clone();
                    root.AddComponent<InteriorFlumeRide>().Configure(boats, seats, barsF, barsR, anchors, cargo, starts, gate, lugs, craneB, splash, font,
                        new Vector3(11.6f, F + 2.0f, 7.70f), CargoRiderSeats(), CargoRiderBoats);
                    // 벤치 · 관람석 (ct_builder bleachers · plazas · hall_props, 엉덩이 = 앞 끝 − 0.07)
                    var benchSeats = new List<Vector4>();
                    var boxes = new List<Bounds>();
                    void Seat(float x0, float x1, float z0, float z1, float y0, float top, float yaw, params Vector2[] at)
                    {
                        boxes.Add(new Bounds(new Vector3((x0 + x1) * 0.5f, (y0 + top + 0.01f) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, top + 0.01f - y0, z1 - z0)));
                        foreach (var p in at)
                            benchSeats.Add(new Vector4(p.x, top, p.y, yaw));
                    }
                    foreach (var (sa, top) in new[] { (11.70f, F + 0.42f), (12.80f, F + 0.66f), (13.90f, F + 0.90f) })
                        Seat(sa, sa + 0.40f, -2.30f, -0.85f, top - 0.3f, top, -90f, new Vector2(sa + 0.07f, -2.125f), new Vector2(sa + 0.07f, -1.575f), new Vector2(sa + 0.07f, -1.025f));
                    Seat(2.60f, 2.96f, -0.60f, 0.60f, F, F + 0.42f, 90f, new Vector2(2.89f, -0.30f), new Vector2(2.89f, 0.30f));
                    Seat(-1.22f, -0.80f, -1.02f, -0.60f, F, F + 0.43f, 62.5f, new Vector2(-1.003f, -0.814f));
                    Seat(-0.93f, -0.50f, -1.32f, -0.88f, F, F + 0.43f, 27.5f, new Vector2(-0.714f, -1.103f));
                    Seat(1.42f, 1.78f, 3.75f, 4.45f, F, F + 0.42f, 90f, new Vector2(1.71f, 3.92f), new Vector2(1.71f, 4.28f));
                    Seat(5.70f, 6.70f, 5.15f, 5.51f, F, F + 0.42f, 180f, new Vector2(5.95f, 5.22f), new Vector2(6.45f, 5.22f));
                    root.AddComponent<InteriorBenchSeats>().Configure(benchSeats.ToArray(), boxes.ToArray(), -6f);
                });
        }

        /// <summary>저장 단지 안 줄 캡슐에 탄 주민 (안 줄 0 · 3 · 6번) — <see cref="ResidentSpotBaker"/>와 <see cref="InteriorCarousel"/>가 함께 씀.</summary>
        public static readonly int[] StorageRiderCapsules = { StorageCarousel.OuterCount, StorageCarousel.OuterCount + 3, StorageCarousel.OuterCount + 6 };

        /// <summary>탄 주민 안장 자리 (판 0°, 템플릿 기준) · 바라보는 y각 (나아가는 쪽, 기둥이 앞).</summary>
        public static Vector3[] StorageRiderSeats(out float[] yaws)
        {
            var seats = new Vector3[StorageRiderCapsules.Length];
            yaws = new float[seats.Length];
            for (int i = 0; i < seats.Length; i++)
                seats[i] = StorageCarousel.RestSaddle(StorageRiderCapsules[i], out yaws[i]);
            return seats;
        }

        /// <summary>
        /// 진화 2단계 저장 단지 "별빛 회전목마 창고" (4 × 2칸, sc_builder — 사용자 2026-11-10 시안 1 "좋아 이대로 유니티로 옮겨줘"): 직접 체험 = 회전목마 탑승 (<see cref="InteriorCarousel"/>).
        /// 외형 평면 × 8 그대로: 가운데 회전목마 (받침 · 반짝 울타리 트임 서 · 동 · 남, 도는 판 · 거울 기둥 · 캡슐 바깥 12 · 안 10 · 줄무늬 원뿔 지붕),
        /// 양옆 실내 창고 둘 (앞 진열 유리창 · 회전목마 쪽 아치 출입구 셋 (압력 막) · 자동 선반 세 줄 · 캡슐 내리는 자리) + 창고 벽에서 뻗은 집게 팔 둘, 앞 매표소 · 가로등 광장.
        /// 캡슐 높이만 낮춤 (안장에 앉은 머리 위 지붕까지 약 0.4 m 남음). 문 자리 10 = 외형 연결점이 있는 면 (깊이 3.95, 닫힘 = 난간), 아래 해치 8, 위 해치 없음 (하늘).
        /// 벤치에 앉음 (<see cref="InteriorBenchSeats"/>). 바깥 주민 = 우주복 (<see cref="InteriorOpenAir"/>), 창고 둘 = 실내 상자.
        /// </summary>
        private static void BuildEvoStorageComplex()
        {
            const float DeckEdge = 3.95f;
            const string N = "INT_EvoStorageComplex_";
            var west = new Vector3Int(-1, 0, 0);
            var east = new Vector3Int(1, 0, 0);
            var south = new Vector3Int(0, 0, -1);
            var north = new Vector3Int(0, 0, 1);
            var sockets = new List<InteriorSocket>
            {
                new InteriorSocket(new Vector3Int(0, 0, 0), west, DeckEdge), new InteriorSocket(new Vector3Int(0, 0, 1), west, DeckEdge),
                new InteriorSocket(new Vector3Int(3, 0, 0), east, DeckEdge), new InteriorSocket(new Vector3Int(3, 0, 1), east, DeckEdge),
                new InteriorSocket(new Vector3Int(0, 0, 0), south, DeckEdge), new InteriorSocket(new Vector3Int(1, 0, 0), south, DeckEdge), new InteriorSocket(new Vector3Int(3, 0, 0), south, DeckEdge),
                new InteriorSocket(new Vector3Int(0, 0, 1), north, DeckEdge), new InteriorSocket(new Vector3Int(1, 0, 1), north, DeckEdge), new InteriorSocket(new Vector3Int(3, 0, 1), north, DeckEdge),
                new InteriorSocket(new Vector3Int(0, 0, 0), Vector3Int.down, -F, new Vector3(-0.6f, 0f, 0f)),      // 서쪽 창고 선반 앞 (화분 · 벤치 비킴)
                new InteriorSocket(new Vector3Int(0, 0, 1), Vector3Int.down, -F, new Vector3(-0.6f, 0f, 0.1f)),    // 서쪽 창고 선반 뒤
                new InteriorSocket(new Vector3Int(3, 0, 0), Vector3Int.down, -F, new Vector3(0.6f, 0f, 0f)),       // 동쪽 창고
                new InteriorSocket(new Vector3Int(3, 0, 1), Vector3Int.down, -F, new Vector3(0.6f, 0f, 0.1f)),
                new InteriorSocket(new Vector3Int(1, 0, 0), Vector3Int.down, -F, new Vector3(1.8f, 0f, -2.5f)),    // 앞 광장 (가로등 · 매표소 사이)
                new InteriorSocket(new Vector3Int(2, 0, 0), Vector3Int.down, -F, new Vector3(-1.8f, 0f, -2.5f)),
                new InteriorSocket(new Vector3Int(1, 0, 1), Vector3Int.down, -F, new Vector3(0.8f, 0f, 2.5f)),     // 뒤 갑판
                new InteriorSocket(new Vector3Int(2, 0, 1), Vector3Int.down, -F, new Vector3(-0.8f, 0f, 2.5f)),
            };
            var warm = new Color(1f, 0.86f, 0.55f);
            var white = new Color(1f, 0.97f, 0.92f);
            var cool = new Color(0.72f, 0.82f, 1f);
            float roof = StorageCarousel.DiskTop + 2.2f;
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(12f, roof, 4f), 6.5f, 0.9f, white),         // 회전목마 지붕 밑 (거울 기둥 둘레, 흰 빛 = 캡슐 색 그대로 · 따뜻함은 전구가)
                LightSpec.Point(new Vector3(12f, roof, 0.7f), 4.5f, 0.6f, white),
                LightSpec.Point(new Vector3(12f, roof, 7.3f), 4.5f, 0.6f, white),
                LightSpec.Point(new Vector3(8.7f, roof, 4f), 4.5f, 0.6f, white),
                LightSpec.Point(new Vector3(15.3f, roof, 4f), 4.5f, 0.6f, white),
                LightSpec.Point(new Vector3(7.6f, F + 2.3f, -2.4f), 6f, 0.6f, warm),    // 가로등
                LightSpec.Point(new Vector3(16.4f, F + 2.3f, -2.4f), 6f, 0.6f, warm),
                LightSpec.Point(new Vector3(12f, F + 1.2f, -3.1f), 3f, 0.4f, warm),     // 매표소
                LightSpec.Point(new Vector3(12f, F + 2.4f, 10.8f), 7f, 0.45f, cool),    // 뒤 갑판
                LightSpec.Point(new Vector3(6.9f, F + 2.1f, 4f), 4.5f, 0.45f, warm),    // 창고 출입구 등 · 집게 팔
                LightSpec.Point(new Vector3(17.1f, F + 2.1f, 4f), 4.5f, 0.45f, warm),
            };
            foreach (var xm in new[] { 1.76f, 22.24f })
            {
                foreach (var z in new[] { -0.3f, 4f, 8.2f })
                    lights.Add(LightSpec.Point(new Vector3(xm, F + 2.45f, z), 6f, 0.85f, white));   // 창고 천장 등
            }
            EvoAccent("StorageComplex", "#C0D0B0");
            // 스폰 = 서쪽 광장에서 회전목마 · 매표소를 바라봄
            Build("EvoStorageComplex", "Evolved/MD_Evo_StorageComplex", sockets, new Vector3(7.1f, F, -0.9f), 45f, lights,
                configure: root =>
                {
                    AddOpenAir(root, System.Array.Empty<Vector3>());
                    root.GetComponent<InteriorOpenAir>().SetIndoorBox(new Rect(-2.56f, -2.24f, 8.64f, 12.48f), F + 2.75f, new Rect(17.92f, -2.24f, 8.64f, 12.48f));   // 창고 둘
                    var r = root.transform;
                    var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Maplestory SDF.asset");
                    // 판 피벗 (판 윗면 가운데): 판 · 거울 기둥 · 기둥 · 지붕 한 조각 (도는 동안에도 밟고 부딪힘)
                    var disk = new GameObject("Carousel_Pivot").transform;
                    disk.SetParent(r, false);
                    disk.position = r.TransformPoint(StorageCarousel.Center);
                    var carousel = Child(root, N + "Carousel");
                    carousel.SetParent(disk, true);
                    carousel.gameObject.AddComponent<MeshCollider>().sharedMesh = carousel.GetComponent<MeshFilter>().sharedMesh;
                    // 캡슐 22: 모델 하나 (바깥 0번 자리) → 피벗마다 복제, 몸통 색 셋 번갈아 (도장 · 흰 · 보라), 바깥 줄은 안장 (보고 F)
                    var capsuleMesh = Child(root, N + "Capsule");
                    var paint = AssetDatabase.LoadAssetAtPath<Material>(AccentFolder + "M_Paint_Evo_StorageComplex.mat");
                    var bodies = new[] { paint, AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.EvoWhitePath), AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2VioletPath) };
                    var capsules = new Transform[StorageCarousel.Count];
                    var seats = new Transform[StorageCarousel.Count];
                    var meshOffset = Vector3.zero;
                    for (int i = 0; i < StorageCarousel.Count; i++)
                    {
                        var pivot = new GameObject($"Capsule{i}_Pivot").transform;
                        pivot.SetParent(disk, false);
                        pivot.localPosition = StorageCarousel.LocalPosition(i, 0f);
                        pivot.localRotation = StorageCarousel.LocalRotation(i);
                        capsules[i] = pivot;
                        Transform mesh;
                        if (i == 0)
                        {
                            capsuleMesh.SetParent(pivot, true);
                            meshOffset = capsuleMesh.localPosition;
                            mesh = capsuleMesh;
                        }
                        else
                        {
                            mesh = Object.Instantiate(capsuleMesh.gameObject, pivot).transform;
                            mesh.name = capsuleMesh.name;
                            mesh.localPosition = meshOffset;
                            mesh.localRotation = Quaternion.identity;
                        }
                        int ring = StorageCarousel.Outer(i) ? 0 : 1;
                        int q = ring == 0 ? i : i - StorageCarousel.OuterCount;
                        var body = bodies[(q + ring) % bodies.Length];
                        if (body != null && body != paint)
                        {
                            var rend = mesh.GetComponent<MeshRenderer>();
                            var mats = rend.sharedMaterials;
                            for (int m = 0; m < mats.Length; m++)
                            {
                                if (mats[m] == paint)
                                    mats[m] = body;
                            }
                            rend.sharedMaterials = mats;
                        }
                        pivot.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.46f, 0.5f, 1f);   // 몸통 (판 위를 걸을 때 부딪힘)
                        if (ring == 0)
                        {
                            var seat = new GameObject("Seat").transform;
                            seat.SetParent(pivot, false);
                            seat.localPosition = new Vector3(0f, StorageCarousel.SaddleUp - 0.04f, StorageCarousel.SaddleBack);
                            seat.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.42f, 0.14f, 0.42f);
                            seats[i] = seat;
                        }
                    }
                    // 집게 팔 둘 (창고 벽 받침 피벗, 서 = +x로 뻗음 → z각 음수가 숙임)
                    Transform Claw(string tag, Vector3 at)
                    {
                        var p = new GameObject("Claw" + tag + "_Pivot").transform;
                        p.SetParent(r, false);
                        p.position = r.TransformPoint(at);
                        Child(root, N + "Claw" + tag).SetParent(p, true);
                        return p;
                    }
                    var claws = new[] { Claw("W", new Vector3(6.13f, F + 2.35f, 4f)), Claw("E", new Vector3(17.87f, F + 2.35f, 4f)) };
                    // 트임 막이 셋 (울타리 트임 양 끝을 잇는 줄, 도는 동안만 켬)
                    var walls = new GameObject("CarouselGateWalls").transform;
                    walls.SetParent(r, false);
                    var gateWalls = new Collider[StorageCarousel.Gaps.Length];
                    float chord = StorageCarousel.FenceRadius * Mathf.Cos(StorageCarousel.GapHalf * Mathf.Deg2Rad);
                    float span = 2f * StorageCarousel.FenceRadius * Mathf.Sin(StorageCarousel.GapHalf * Mathf.Deg2Rad) + 0.3f;
                    for (int k = 0; k < gateWalls.Length; k++)
                    {
                        float a = StorageCarousel.Gaps[k];
                        var go = new GameObject("Gate" + k);
                        go.transform.SetParent(walls, false);
                        go.transform.localPosition = new Vector3(StorageCarousel.Center.x + chord * Mathf.Cos(a * Mathf.Deg2Rad), StorageCarousel.BaseTop + 0.7f,
                            StorageCarousel.Center.z + chord * Mathf.Sin(a * Mathf.Deg2Rad));
                        go.transform.localRotation = Quaternion.Euler(0f, -a, 0f);
                        var box = go.AddComponent<BoxCollider>();
                        box.size = new Vector3(0.1f, 1.4f, span);
                        gateWalls[k] = box;
                    }
                    root.AddComponent<InteriorCarousel>().Configure(disk, capsules, seats, claws, new[] { -1f, 1f }, gateWalls, font,
                        new Vector3(12f, F + 2.45f, -2.4f), StorageRiderSeats(out _), StorageRiderCapsules);
                    // 벤치 (sc_builder warehouse · plaza, 엉덩이 = 앞 끝 − 0.07): 창고 진열창 앞 (밖을 봄) · 광장 (회전목마를 봄)
                    var benchSeats = new List<Vector4>();
                    var boxes = new List<Bounds>();
                    void Seat(float x0, float x1, float z0, float z1, float top, float yaw, params Vector2[] at)
                    {
                        boxes.Add(new Bounds(new Vector3((x0 + x1) * 0.5f, (F + top + 0.01f) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, top + 0.01f - F, z1 - z0)));
                        foreach (var p in at)
                            benchSeats.Add(new Vector4(p.x, top, p.y, yaw));
                    }
                    foreach (var xm in new[] { 1.76f, 22.24f })
                        Seat(xm - 0.8f, xm + 0.8f, -1.34f, -0.94f, F + 0.42f, 180f, new Vector2(xm - 0.4f, -1.27f), new Vector2(xm + 0.4f, -1.27f));
                    foreach (var x in new[] { 9.2f, 14.8f })
                        Seat(x - 0.7f, x + 0.7f, -0.8f, -0.4f, F + 0.42f, 0f, new Vector2(x - 0.35f, -0.47f), new Vector2(x + 0.35f, -0.47f));
                    root.AddComponent<InteriorBenchSeats>().Configure(benchSeats.ToArray(), boxes.ToArray(), -6f);
                });
        }

        /// <summary>연구원 연구실 쉬는 벤치 (남서 225°, 코어를 봄) 두 자리 각 — 엉덩이 = 방석 앞 끝 (r 1.45) + 0.07.</summary>
        public static readonly float[] MazeBenchAngles = { 218f, 232f };

        public static Vector4 MazeBenchSeat(float angle)
        {
            float a = angle * Mathf.Deg2Rad;
            var c = ResearchMaze.Center;
            return new Vector4(c.x + 1.52f * Mathf.Cos(a), F + 0.44f, c.z + 1.52f * Mathf.Sin(a), Mathf.Atan2(-Mathf.Cos(a), -Mathf.Sin(a)) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// 진화 2단계 연구원 "별빛 데이터 미로" (2 × 2칸, mz_builder — 사용자 2026-11-10 시안 1 "좋아 이대로 유니티로 옮겨줘"): 직접 체험 = 미로 (<see cref="InteriorMaze"/>).
        /// 외형 평면 × 8 그대로: 서버 랙 벽 세 겹 네모 미로 (트임 = 바깥 앞 · 가운데 뒤 · 안 서쪽, 가로막 · 반쯤 막은 턱, 막다른 길 화면 셋), 네 모서리 냉각탑 (팬 돎),
        /// 앞 입구 아치 · 간판 (최고 기록) · 길잡이 화면, 가운데 둥근 유리 연구실 (데이터 코어 · 위 빛 고리 · 연구 책상 셋 · 쉬는 벤치 · 화분, 서쪽 압력 막 문).
        /// 벽 높이 2.6 (눈높이 위), 앞 바깥 길 가로막 = 턱 (동쪽 바깥 길 = 긴 막다른 길). 외형 옆면 연결점이 없어 문 자리 없음, 아래 해치 4 = 바깥 길 바닥.
        /// 미로 · 갑판 주민 = 우주복 (<see cref="InteriorOpenAir"/>), 연구실 = 실내 원.
        /// </summary>
        private static void BuildEvoResearchInstitute()
        {
            const string N = "INT_EvoResearchInstitute_";
            var sockets = new List<InteriorSocket>();
            foreach (var (cell, off) in ResearchMaze.Hatches)
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, new Vector3(off.x, 0f, off.y)));   // 바깥 길 가운데 (뚜껑 1.4 · 길 폭 1.52)
            var c = ResearchMaze.Center;
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.86f, 0.55f);
            var cool = new Color(0.8f, 0.86f, 1f);
            var lavender = new Color(0.78f, 0.66f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(c.x, F + 2.35f, c.z + 0.9f), 4.5f, 1.0f, white),     // 연구실 (코어 위 고리 비킴)
                LightSpec.Point(new Vector3(c.x - 0.9f, F + 1.2f, c.z), 3f, 0.6f, lavender),     // 코어 빛
                LightSpec.Point(new Vector3(c.x, F + 2.4f, c.z - 7.2f), 5f, 0.55f, warm),        // 입구 아치
                LightSpec.Point(new Vector3(c.x - 7.25f, F + 2.2f, c.z), 6f, 0.35f, cool),       // 둘레 갑판
                LightSpec.Point(new Vector3(c.x + 7.25f, F + 2.2f, c.z), 6f, 0.35f, cool),
                LightSpec.Point(new Vector3(c.x, F + 2.2f, c.z + 7.25f), 6f, 0.35f, cool),
                LightSpec.Point(new Vector3(c.x - 2.8f, F + 2.3f, c.z), 3f, 0.4f, cool),         // 안 고리 트임 · 연구실 문
            };
            foreach (var (dx, dz) in new[] { (-5.68f, -5.68f), (5.68f, -5.68f), (-5.68f, 5.68f), (5.68f, 5.68f), (0f, -5.68f), (-5.68f, 0f), (5.68f, 0f), (0f, 5.68f) })
                lights.Add(LightSpec.Point(new Vector3(c.x + dx, F + 2.3f, c.z + dz), 4.5f, 0.5f, cool));   // 바깥 길 모서리 · 가운데
            foreach (var (dx, dz) in new[] { (-4f, -4f), (4f, -4f), (-4f, 4f), (4f, 4f) })
                lights.Add(LightSpec.Point(new Vector3(c.x + dx, F + 2.3f, c.z + dz), 4f, 0.45f, cool));    // 가운데 길 모서리
            var orbits = new List<OrbitSpec>();
            float h = ResearchMaze.Rings[0];
            int fan = 0;
            foreach (var (sx, sz) in new[] { (-h, -h), (h, -h), (-h, h), (h, h) })
                orbits.Add(new OrbitSpec(N + "Fan" + fan++, new Vector3(c.x + sx, 0f, c.z + sz), Vector3.up, fan % 2 == 0 ? 120f : -120f));   // 냉각탑 팬
            EvoAccent("ResearchInstitute", "#B79CFF");
            // 스폰 = 입구 아치 앞 갑판에서 미로를 바라봄
            Build("EvoResearchInstitute", "Evolved/MD_Evo_ResearchInstitute", sockets, new Vector3(c.x, F, ResearchMaze.ArchZ - 0.95f), 0f, lights, orbits,
                configure: root =>
                {
                    AddOpenAir(root, new[] { new Vector3(c.x, c.z, ResearchMaze.LabRadius) });   // 연구실 안 = 실내
                    var r = root.transform;
                    var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Maplestory SDF.asset");
                    // 길잡이 점 구간 (길 순서) · 코어 위 빛 고리 피벗 · 길잡이 화면 누르는 자리
                    var dots = new List<Renderer>();
                    for (int i = 0; i < ResearchMaze.Route.Length - 1; i++)
                    {
                        var d = Child(root, $"{N}Dots{i:00}");
                        if (d != null)
                            dots.Add(d.GetComponent<Renderer>());
                    }
                    var ringPivot = new GameObject("CoreRing_Pivot").transform;
                    ringPivot.SetParent(r, false);
                    ringPivot.position = r.TransformPoint(new Vector3(c.x, F + 2.62f, c.z));
                    Child(root, N + "CoreRing").SetParent(ringPivot, true);
                    var hintGo = new GameObject("MazeHintUse");
                    hintGo.transform.SetParent(r, false);
                    hintGo.transform.localPosition = ResearchMaze.HintScreen + new Vector3(0f, 0f, -0.05f);
                    var hintBox = hintGo.AddComponent<BoxCollider>();
                    hintBox.size = new Vector3(0.8f, 0.62f, 0.1f);
                    var pulse = AssetDatabase.LoadAssetAtPath<Material>(InteriorMaterials + "M_InteriorLight.mat");
                    root.AddComponent<InteriorMaze>().Configure(dots.ToArray(), ringPivot, hintBox, pulse, font);
                    // 연구실 쉬는 벤치 두 자리 (mz_builder lab bench, 남서 225° ± 16°)
                    float ba = 225f * Mathf.Deg2Rad;
                    var bc = new Vector3(c.x + 1.635f * Mathf.Cos(ba), (F + F + 0.45f) * 0.5f, c.z + 1.635f * Mathf.Sin(ba));
                    var seats = new Vector4[MazeBenchAngles.Length];
                    for (int i = 0; i < seats.Length; i++)
                        seats[i] = MazeBenchSeat(MazeBenchAngles[i]);
                    root.AddComponent<InteriorBenchSeats>().Configure(seats, new[] { new Bounds(bc, new Vector3(0.62f, 0.45f, 0.62f)) }, -6f);
                });
        }

        /// <summary>
        /// 진화 3단계 궤도 도시 "알이 깨어난 정원 섬 도시" (38칸: 바닥 5×5 + 2층 3×3 + 가운데 기둥 4칸, oc_builder — 사용자 2026-11-10 시안 1 "좋아 이대로 유니티로 옮겨줘"):
        /// 3단계 = 맨몸 정원 (우주복 · 유리 덮개 없음 — <see cref="InteriorOpenAir"/>를 붙이지 않음). 외형 평면 × 8: 초타원 섬 · 고리 강 · 다리 넷 · 네 모서리
        /// (앞 왼쪽 연꽃 호수 · 정자, 앞 오른쪽 역, 뒤 왼쪽 숲, 뒤 오른쪽 꽃 들판) · 둘레 높인 궤도열차 고리 (밑으로 걸어 지나감) ·
        /// 가운데 언덕 마을 (평평한 단 셋 · 계단 · 반쯤 묻힌 하얀 집 · 폭포, 언덕 속 고리 길 · 뿌리 홀) · 수정 나무 기둥 (꼬인 진주 줄기 넷 · 유리 엘리베이터 다섯 층 ·
        /// 층마다 나무 위 테라스 · 난간 밖 화단) · 맨 위 프리즘 (도는 무지개 고리). 나무 = 식물 자리 (Tree · Pine · Blossom, <see cref="InteriorPlantSpots"/>).
        /// 직접 체험 = 궤도열차 운전 + 미니게임 "정지선에 맞춰 멈추기" (<see cref="InteriorOrbitTrain"/>). 동물 = 외형 손그림 사슴 · 토끼 · 백조 · 나비를 내부 크기로 (CritterFx).
        /// 문 자리 = 기둥 테라스 15 (외형 옆 연결점), 해치 21 = 바닥 칸 (언덕 속 길 · 고리 길, <see cref="OrbitalCity.Hatches"/>).
        /// </summary>
        private static void BuildEvoOrbitalCity()
        {
            const string N = "INT_EvoOrbitalCity_";
            var sockets = new List<InteriorSocket>();
            foreach (var (cell, dir) in OrbitalCity.TowerDoors)
                sockets.Add(new InteriorSocket(cell, dir, OrbitalCity.TowerDoorDepth));   // 옥상 전망대 문 다리 끝 (3.93 바로 안)
            foreach (var (cell, off) in OrbitalCity.Hatches)
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, new Vector3(off.x, 0f, off.y)));
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.88f, 0.66f);
            var cool = new Color(0.82f, 0.9f, 1f);
            Vector3 P(float r, float deg, float y) => OrbitalCity.Polar(r, deg, y);
            var lights = new List<LightSpec>();
            for (int k = 0; k < 12; k++)
                lights.Add(LightSpec.Point(P(14.6f, 15f + 30f * k, F + 2.2f), 8f, 1.0f, warm));         // 강 바깥 고리 길 (레일 밑)
            for (int k = 0; k < 8; k++)
                lights.Add(LightSpec.Point(P(19.2f, 22.5f + 45f * k, F + 2.0f), 8f, 0.9f, white));      // 레일 밖 둘레 (호수 · 역 · 숲 · 들판)
            for (int k = 0; k < 4; k++)
            {
                lights.Add(LightSpec.Point(P(9.3f, 90f * k, F + 4.8f), 8f, 0.9f, white));               // 아래 단 마을
                lights.Add(LightSpec.Point(P(7.5f, 45f + 90f * k, F + 7.2f), 7f, 0.8f, white));         // 위 단
                lights.Add(LightSpec.Point(P(6.4f, 45f + 90f * k, F + 2.1f), 4.5f, 0.7f, warm));        // 언덕 속 고리 길 모서리 (굴 반지름 5.2 ~ 7.6)
                lights.Add(LightSpec.Point(P(6.4f, 90f * k, F + 2.1f), 4.5f, 0.7f, warm));              // 고리 길 가운데 (바깥 통로 쪽)
            }
            lights.Add(LightSpec.Point(new Vector3(16f, F + 2.5f, 16f), 4.5f, 0.8f, warm));             // 뿌리 홀
            lights.Add(LightSpec.Point(new Vector3(16f, F + 10.6f, 12.4f), 6f, 0.9f, white));           // 꼭대기 광장 (엘리베이터 앞)
            lights.Add(LightSpec.Point(new Vector3(16f, F + 10.6f, 19.6f), 6f, 0.7f, white));
            foreach (float fl in OrbitalCity.LiftStops)
            {
                if (fl < 10f)
                    continue;
                lights.Add(LightSpec.Point(new Vector3(16f, fl + 2.6f, 13.1f), 5f, 0.8f, white));        // 옥상 전망대 (엘리베이터 앞 · 뒤)
                lights.Add(LightSpec.Point(new Vector3(16f, fl + 2.6f, 18.9f), 5f, 0.6f, white));
            }
            lights.Add(LightSpec.Point(new Vector3(16f, OrbitalCity.LiftStops[OrbitalCity.LiftStops.Length - 1] + 3.6f, 16f), 4f, 0.9f, cool));   // 프리즘 속 빛
            for (int k = -1; k <= 1; k++)
                lights.Add(LightSpec.Point(P(OrbitalCity.TrackRadius + 1.9f, OrbitalCity.StationAngle + 11f * k, OrbitalCity.PlatformTop + 2.2f), 6f, 0.9f, warm));   // 역 지붕 밑
            lights.Add(LightSpec.Point(P(19.55f, 237f, F + 2.0f), 4f, 0.8f, warm));                      // 호숫가 정자
            var orbits = new List<OrbitSpec> { new OrbitSpec(N + "Halo", OrbitalCity.Hub, Vector3.up, 10f) };   // 프리즘 무지개 고리 (기운 채 돎)
            EvoAccent("OrbitalCity", "#C8E8FF");
            // 스폰 = 앞 (−z) 강 바깥 고리 길에서 언덕 폭포 · 기둥을 바라봄
            Build("EvoOrbitalCity", "Evolved/MD_Evo_OrbitalCity", sockets, new Vector3(16f, F, 1.4f), 0f, lights, orbits,
                configure: root =>
                {
                    var r = root.transform;
                    var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Maplestory SDF.asset");
                    // 엘리베이터 (언덕 꼭대기 광장 ↔ 옥상 전망대 직행, 문 = −z, gt_builder lift 틀)
                    var stops = OrbitalCity.LiftStops;
                    var car = Child(root, N + "LiftCar");
                    var carGlass = Child(root, N + "LiftCarGlass");
                    carGlass.SetParent(car, true);
                    foreach (var t in new[] { car, carGlass })
                        t.gameObject.AddComponent<MeshCollider>().sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                    var lift = root.AddComponent<InteriorLift>();
                    var left = new Transform[stops.Length];
                    var right = new Transform[stops.Length];
                    var offsets = new float[stops.Length];
                    for (int k = 0; k < stops.Length; k++)
                    {
                        offsets[k] = stops[k] - stops[0];
                        left[k] = Child(root, $"{N}LiftDoor{k + 1}L");
                        right[k] = Child(root, $"{N}LiftDoor{k + 1}R");
                        BoxOf(left[k], 0f);
                        BoxOf(right[k], 0f);
                        BoxOf(Child(root, $"{N}LiftCall{k + 1}"), 0.06f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, true);
                        var button = Child(root, $"{N}LiftBtn{k + 1}");
                        button.SetParent(car, true);
                        BoxOf(button, 0.03f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, false);
                    }
                    lift.Configure(car, offsets, left, right, new Vector3(16f, stops[0] + 0.085f, 16f), Vector3.right, OrbitalCity.CarRadius - 0.1f);
                    // 옥상 망원경 (oc_builder ScopeHead — 관절 = 받침 위 1.14, 쉬는 기울기 18°, 사용자 2026-11-11 "정면 망원경에 상호작용 없음"):
                    // 다른 전망대처럼 들여다보기, 앞 (−z · 폭포 · 섬 앞)을 쉴 때 방향으로 좌우 ±100°
                    var scope = Child(root, N + "ScopeHead");
                    BoxOf(scope, 0.06f);
                    root.AddComponent<InteriorTelescope>().Configure(scope, P(3.1f, 270f, stops[stops.Length - 1] + 1.14f), 18f, 0f, font, 90f - 270f, 100f, startPitch: -16f, startFov: 24f, minPitch: -60f);
                    // 궤도열차: 허브 축 피벗 아래 (쉬는 자세 = 역), 운전석 = 앞 차 방석 (보고 F)
                    var pivot = new GameObject("TrainPivot").transform;
                    pivot.SetParent(r, false);
                    pivot.localPosition = OrbitalCity.Hub;
                    var train = Child(root, N + "Train");
                    train.gameObject.AddComponent<MeshCollider>().sharedMesh = train.GetComponent<MeshFilter>().sharedMesh;
                    train.SetParent(pivot, true);
                    var seatGo = new GameObject("DriverSeat");
                    seatGo.transform.SetParent(pivot, false);
                    seatGo.transform.localPosition = P(OrbitalCity.TrackRadius, OrbitalCity.FrontAngle, OrbitalCity.CarFloor + 0.4f) - OrbitalCity.Hub;
                    seatGo.transform.localRotation = Quaternion.Euler(0f, OrbitalCity.ForwardYaw(OrbitalCity.FrontAngle), 0f);
                    var seat = seatGo.AddComponent<BoxCollider>();
                    seat.size = new Vector3(1.4f, 0.9f, 1.1f);
                    root.AddComponent<InteriorOrbitTrain>().Configure(pivot, seat, font,
                        P(OrbitalCity.TrackRadius + 2.45f, OrbitalCity.NoseAngle, OrbitalCity.PlatformTop + 1.95f), OrbitalCity.OutwardYaw(OrbitalCity.NoseAngle));
                    // 승강장 보이지 않는 턱: 선로 쪽 (발목 높이 — 운전석을 보고 F는 됨) · 바깥 낭떠러지 · 끝 (계단 자리 빼고)
                    void Guard(Vector3 p0, Vector3 p1, float h)
                    {
                        var go = new GameObject("PlatformGuard");
                        go.transform.SetParent(r, false);
                        go.transform.localPosition = (p0 + p1) * 0.5f + Vector3.up * (h * 0.5f);
                        go.transform.localRotation = Quaternion.LookRotation(p1 - p0, Vector3.up);
                        go.AddComponent<BoxCollider>().size = new Vector3(0.1f, h, (p1 - p0).magnitude + 0.02f);
                    }
                    float a0 = OrbitalCity.StationAngle - OrbitalCity.PlatformHalf, a1 = OrbitalCity.StationAngle + OrbitalCity.PlatformHalf, yp = OrbitalCity.PlatformTop;
                    for (float a = a0; a < a1 - 0.01f; a += 2f)
                    {
                        float b = Mathf.Min(a + 2f, a1);
                        Guard(P(OrbitalCity.PlatformInner + 0.06f, a, yp), P(OrbitalCity.PlatformInner + 0.06f, b, yp), 0.55f);
                        Guard(P(OrbitalCity.PlatformOuter - 0.06f, a, yp), P(OrbitalCity.PlatformOuter - 0.06f, b, yp), 1.0f);
                    }
                    Guard(P(OrbitalCity.PlatformInner, a0 + 0.2f, yp), P(OrbitalCity.PlatformOuter, a0 + 0.2f, yp), 1.0f);
                    Guard(P(OrbitalCity.TrackRadius + 2.5f, a1 - 0.2f, yp), P(OrbitalCity.PlatformOuter, a1 - 0.2f, yp), 1.0f);   // 계단 (반지름 17.75 ~ 19.25) 옆
                    // 고리 강 둑 보이지 않는 턱 (다리 자리만 비움): 강바닥 (F − 0.6)에 내려가면 둑 (F + 0.16)으로 못 올라와 갇혔음
                    foreach (float rb in new[] { OrbitalCity.RiverInner - 0.18f, OrbitalCity.RiverOuter + 0.18f })
                    {
                        float gap = Mathf.Asin(1.35f / rb) * Mathf.Rad2Deg;
                        for (int q = 0; q < 4; q++)
                        {
                            float s0 = 45f + 90f * q + gap, s1 = 135f + 90f * q - gap;
                            for (float a = s0; a < s1 - 0.01f; a += 5f)
                                Guard(P(rb, a, F), P(rb, Mathf.Min(a + 5f, s1), F), 1.0f);
                        }
                    }
                    // 동물: 사슴 · 아기 사슴 · 토끼 · 백조 = 내부 전용 모델 (SM_InteriorCritters, 미터 — critter_builder.py 메타볼 몸, 사용자 2026-11-10 · 11 사진 · 애니메이션 참고),
                    // 나비 = 외형 손그림 동물 (SM_Evo_OrbitalCity Critter_*)을 키워서. 움직임 = 외형과 같은 CritterFx (제자리) — CritterFx 흔들림 폭이 외형 크기 기준이라
                    // 묶음은 예전 배율 그대로 키우고 내부 모델은 그만큼 줄여 넣음 (실제 크기 = 미터)
                    var critterSrc = new Dictionary<string, GameObject>();
                    foreach (var path in new[] { "Assets/_Project/Art/Models/Modules/SM_Evo_OrbitalCity.fbx", "Assets/_Project/Art/Models/Interior/SM_InteriorCritters.fbx" })
                    {
                        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                        {
                            if (o is GameObject g && g.name.StartsWith("Critter_") && g.GetComponent<MeshFilter>() != null)
                                critterSrc[g.name] = g;
                        }
                    }
                    var critterMat = ModuleFxMaterials.Get("Critters");
                    void Critters(string holderName, float scale, (string src, Vector3 at, float yaw)[] list)
                    {
                        var holder = new GameObject(holderName).transform;
                        holder.SetParent(r, false);
                        holder.localScale = Vector3.one * scale;
                        var parts = new List<Transform>();
                        var kinds = new List<SpaceStation.Building.CritterFx.Kind>();
                        foreach (var (src, at, yaw) in list)
                        {
                            bool interior = critterSrc.TryGetValue("Critter_" + src, out var g);   // 내부 모델 (미터)
                            if (!interior && !critterSrc.TryGetValue("Critter_OrbitalCity_" + src, out g))
                            {
                                Debug.LogWarning("[InteriorTemplateBuilder] 궤도 도시 동물 없음: " + src);
                                continue;
                            }
                            var t = new GameObject("Critter_" + src).transform;
                            t.SetParent(holder, false);
                            t.localPosition = at / scale;
                            t.localRotation = Quaternion.Euler(0f, yaw, 0f) * g.transform.localRotation;
                            t.localScale = interior ? g.transform.localScale / scale : g.transform.localScale;
                            t.gameObject.AddComponent<MeshFilter>().sharedMesh = g.GetComponent<MeshFilter>().sharedMesh;
                            var mr = t.gameObject.AddComponent<MeshRenderer>();
                            mr.sharedMaterial = critterMat;
                            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                            parts.Add(t);
                            kinds.Add(StationArtBuilder.CritterKinds.TryGetValue(src.Substring(src.LastIndexOf('_') + 1), out var kind) ? kind : SpaceStation.Building.CritterFx.Kind.Swim);
                        }
                        var fx = holder.gameObject.AddComponent<SpaceStation.Building.CritterFx>();
                        var so = new SerializedObject(fx);
                        var arr = so.FindProperty("_critters");
                        arr.arraySize = parts.Count;
                        for (int i = 0; i < parts.Count; i++)
                        {
                            StationArtBuilder.CritterShape(parts[i], null, kinds[i], out var heading, out float radius);
                            var e = arr.GetArrayElementAtIndex(i);
                            e.FindPropertyRelative("Part").objectReferenceValue = parts[i];
                            e.FindPropertyRelative("Kind").enumValueIndex = (int)kinds[i];
                            e.FindPropertyRelative("Heading").vector3Value = heading;
                            e.FindPropertyRelative("Radius").floatValue = radius;
                            e.FindPropertyRelative("Leader").intValue = -1;
                            e.FindPropertyRelative("Side").floatValue = 0f;
                        }
                        so.FindProperty("_autoPlay").boolValue = true;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                    Critters("Critters_Deer", 13f, new[]
                    {
                        ("Deer", P(19.3f, 211.5f, F), 40f), ("Fawn", P(20.4f, 216.5f, F), -60f),   // 연꽃 호숫가 (어미 · 아기)
                        ("Deer", P(18.3f, 166f, F), 150f), ("Deer", P(18.4f, 80f, F), -20f),        // 숲 · 들판 가장자리
                    });
                    Critters("Critters_Bunny", 6f, new[]
                    {
                        ("Bunny", P(15.8f, 105f, F), 30f), ("Bunny", P(15.8f, 165f, F), 200f), ("Bunny", P(15.8f, 285f, F), 120f),   // 고리 길 밖 잔디 (레일 기둥 사이)
                    });
                    float water = OrbitalCity.WaterLevels[0];   // 백조 원점 = 물높이
                    Critters("Critters_Swan", 7f, new[]
                    {
                        ("Swan", P(12.8f, 200f, water), 90f), ("Swan", P(12.8f, 203.5f, water), 95f),    // 고리 강 (다리 비킴)
                        ("Swan", P(12.8f, 338f, water), -80f), ("Swan", P(12.8f, 341.5f, water), -85f),
                        ("Swan", P(19.0f, 228f, water), 20f),                                             // 연꽃 호수
                    });
                    // 물: 수면 높이마다 KWS 물 (꺼 둠 → InteriorKwsWater가 켬) + 패키지 없는 PC용 Blender 수면, 폭포 물보라 · 웅덩이 물결
                    var kws = new List<GameObject>();
                    var surfaces = new List<Renderer>();
                    for (int k = 0; k < OrbitalCity.WaterLevels.Length; k++)
                    {
                        var surface = Child(root, N + "Water" + (k == 0 ? "" : k.ToString()));
                        if (surface == null)
                            continue;
                        surfaces.Add(surface.GetComponent<Renderer>());
                        kws.Add(KwsWaterBuilder.Build(r, surface.GetComponent<MeshFilter>(), OrbitalCity.WaterLevels[k], k == 0 ? 0.6f : 0.35f, "OrbitalCity" + k, true));
                    }
                    var landings = new Vector3[OrbitalCity.Falls.Length];
                    var widths = new float[OrbitalCity.Falls.Length];
                    for (int i = 0; i < OrbitalCity.Falls.Length; i++)
                    {
                        landings[i] = OrbitalCity.Falls[i].Landing;
                        widths[i] = OrbitalCity.Falls[i].Width;
                        int lv = 0;   // 떨어지는 웅덩이 = 가장 가까운 수면
                        for (int k = 1; k < OrbitalCity.WaterLevels.Length; k++)
                        {
                            if (Mathf.Abs(OrbitalCity.WaterLevels[k] - landings[i].y) < Mathf.Abs(OrbitalCity.WaterLevels[lv] - landings[i].y))
                                lv = k;
                        }
                        if (lv < kws.Count && kws[lv] != null)
                            KwsWaterBuilder.Ripple(kws[lv], landings[i], 0.45f, 0.35f);
                    }
                    kws.RemoveAll(x => x == null);
                    root.AddComponent<InteriorKwsWater>().Configure(kws.ToArray(), surfaces.ToArray(), Child(root, N + "Sky").GetComponent<Renderer>(), new Vector3(16f, 4f, 16f));
                    root.AddComponent<InteriorWaterfallFx>().Configure(landings, widths, AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.FallsMistPath));
                    Critters("Critters_Butterfly", 4f, new[]
                    {
                        ("22_Butterfly", P(18.6f, 38f, F + 1.1f), 0f), ("23_Butterfly", P(19.2f, 47f, F + 0.8f), 60f), ("24_Butterfly", P(18.2f, 55f, F + 1.3f), 120f),
                        ("25_Butterfly", P(19.4f, 30f, F + 0.9f), 200f), ("26_Butterfly", P(20.4f, 233f, F + 1.2f), 0f), ("27_Butterfly", P(18.2f, 229f, F + 0.9f), 90f),
                        ("28_Butterfly", P(4.6f, 300f, F + 9.2f), 0f), ("29_Butterfly", P(4.4f, 120f, F + 9.4f), 180f),
                    });
                });
        }

        /// <summary>
        /// 진화 3단계 바이오스피어 "물의 순환 고리" (3 × 3칸 2층, Blender bio3_builder — 사용자 2026-11-11 시안 1 "유니티로 옮겨줘, 내부에서 직접 돌아다녀볼게"). 맨몸 정원 (우주복 없음).
        /// 외형 × 8: 가운데 연꽃 호수 · 물가 길 · 선착장 → 계단 밭 다섯 단 (밀 · 채소 · 라벤더 · 해바라기 · 튤립 조각보, 아치 방향 계단 셋) → 능선 산책길 → 숲 비탈 → 진주 옹벽 → 고리 산책길 · 네 모서리
        /// (정자 · 연못 · 과수원 · 반딧불이 숲). 물길 셋 = 비탈 위 샘 → 수문 (금빛 바퀴) → 능선 길 밑 → 단마다 작은 폭포 · 물레방아 → 호수.
        /// 진주 아치 셋이 하늘 호수 그릇을 받치고 (정문 아치 = 뿌리 두 갈래 사이 계단), 그릇 밑 폭포 기둥 · 무지개, 위 프리즘, 다리 아래 도는 구름 고리.
        /// 문 자리 = 옆 넷 (깊이 3.9), 아래 해치 = 바닥 칸 아홉, 위 해치 = 하늘 정원 뒤 천장 (사다리). 승강기 = 뒤 왼쪽 아치 기둥 속 (능선 ↔ 하늘 정원).
        /// 체험 (수문 · 반딧불이 잡기)은 다음 단계 — 이번엔 걸어 다니는 공간 · 물 · 승강기.
        /// </summary>
        private static void BuildEvoBiosphere()
        {
            const string N = "INT_EvoBiosphere_";
            var sockets = new List<InteriorSocket>();
            foreach (var (cell, dir) in Biosphere.Doors)
                sockets.Add(new InteriorSocket(cell, dir, Biosphere.DoorDepth));
            foreach (var (cell, off) in Biosphere.Hatches)
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, new Vector3(off.x, 0f, off.y)));
            sockets.Add(new InteriorSocket(Biosphere.UpHatchCell, Vector3Int.up, Biosphere.UpHatchDepth, Biosphere.UpHatchOffset));
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.88f, 0.66f);
            var cool = new Color(0.82f, 0.9f, 1f);
            Vector3 P(float r, float deg, float y) => Biosphere.Polar(r, deg, y);
            var lights = new List<LightSpec>();
            for (int k = 0; k < 8; k++)
                lights.Add(LightSpec.Point(P(8.1f, 22.5f + 45f * k, Biosphere.RidgeTop + 2.4f), 7f, 1.0f, white));        // 능선 산책길
            for (int k = 0; k < 6; k++)
                lights.Add(LightSpec.Point(P(5.6f, 60f * k, F + 3.0f), 6f, 0.8f, warm));                              // 계단 밭
            for (int k = 0; k < 4; k++)
            {
                lights.Add(LightSpec.Point(P(14.0f, 45f + 90f * k, F + 2.6f), 7f, 0.9f, white));                       // 네 모서리
                lights.Add(LightSpec.Point(P(10.9f, 90f * k, F + 2.2f), 5f, 0.7f, warm));                             // 옆 문 계단 · 고리 산책길
            }
            lights.Add(LightSpec.Point(P(2.4f, 270f, F + 2.0f), 5f, 0.8f, cool));                                     // 호수 · 폭포 기둥
            lights.Add(LightSpec.Point(P(2.4f, 90f, F + 2.0f), 5f, 0.6f, cool));
            for (int k = 0; k < 3; k++)
                lights.Add(LightSpec.Point(P(3.0f, Biosphere.Arches[k] + 60f, Biosphere.SkyTop + 2.2f), 5f, 0.8f, white));   // 하늘 정원
            lights.Add(LightSpec.Point(new Vector3(8f, F + 12.05f, 8f), 4f, 0.9f, cool));                              // 프리즘 속 빛
            var orbits = new List<OrbitSpec> { new OrbitSpec(N + "Halo", Biosphere.Hub, Vector3.up, 5f) };            // 구름 고리 · 새 (다리 아래)
            for (int i = 0; i < 3; i++)
                orbits.Add(new OrbitSpec(N + "Wheel" + i, Biosphere.WheelCenter(i), Biosphere.WheelAxis(i), -40f));   // 물레방아 (물이 떨어지는 쪽으로)
            EvoAccent("Biosphere", "#7CD67C");
            // 스폰 = 정문 (−z) 안: 아치 뿌리 두 갈래 사이 계단 · 무지개 · 폭포 기둥 · 하늘 호수를 바라봄
            Build("EvoBiosphere", "Evolved/MD_Evo_Biosphere", sockets, new Vector3(8f, F, -3.0f), 0f, lights, orbits,
                configure: root =>
                {
                    var r = root.transform;
                    // 승강기 (능선 ↔ 하늘 정원, 아치 기둥 속 — Blender에서 −z 문으로 지은 뒤 허브 쪽 (330°)으로 돌림 → 문 미닫이 축 = 60°)
                    var stops = Biosphere.LiftStops;
                    var car = Child(root, N + "LiftCar");
                    var carGlass = Child(root, N + "LiftCarGlass");
                    carGlass.SetParent(car, true);
                    foreach (var t in new[] { car, carGlass })
                        t.gameObject.AddComponent<MeshCollider>().sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                    var lift = root.AddComponent<InteriorLift>();
                    var left = new Transform[stops.Length];
                    var right = new Transform[stops.Length];
                    var offsets = new float[stops.Length];
                    for (int k = 0; k < stops.Length; k++)
                    {
                        offsets[k] = stops[k] - stops[0];
                        left[k] = Child(root, $"{N}LiftDoor{k + 1}L");
                        right[k] = Child(root, $"{N}LiftDoor{k + 1}R");
                        BoxOf(left[k], 0f);
                        BoxOf(right[k], 0f);
                        BoxOf(Child(root, $"{N}LiftCall{k + 1}"), 0.06f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, true);
                        var button = Child(root, $"{N}LiftBtn{k + 1}");
                        button.SetParent(car, true);
                        BoxOf(button, 0.03f).gameObject.AddComponent<InteriorLiftButton>().Configure(lift, k, false);
                    }
                    float slide = (Biosphere.LiftAngle + 180f - 270f) * Mathf.Deg2Rad;   // 문 미닫이 축 = +x를 (문 각 − 270°)만큼
                    var center = Biosphere.LiftCenter;
                    center.y = stops[0] + 0.085f;
                    lift.Configure(car, offsets, left, right, center, new Vector3(Mathf.Cos(slide), 0f, Mathf.Sin(slide)), Biosphere.CarRadius - 0.1f);
                    // 물 · 나무 줄기에 못 들어가는 벽 (bio3_builder LakeHit — 호수 · 하늘 호수 · 연못 + 나무 줄기): 모양만 쓰고 보이지 않음
                    //  (사용자 "중앙 호수 들어가면 나오지도 못함", 체크리스트 31 = 바이오돔과 같은 방식)
                    var lakeHit = Child(root, N + "LakeHit");
                    var lakeMesh = lakeHit.GetComponent<MeshFilter>().sharedMesh;
                    Object.DestroyImmediate(lakeHit.GetComponent<MeshRenderer>());
                    Object.DestroyImmediate(lakeHit.GetComponent<MeshFilter>());
                    lakeHit.gameObject.AddComponent<MeshCollider>().sharedMesh = lakeMesh;
                    // 비: 바깥 모듈과 같은 RainFx를 미터 단위로 (구름 고리와 함께 돌고, 비는 곧게 떨어짐 — 구운 빗줄 막대는 바깥 크기 값이라 점선처럼 멈춰 보였음)
                    var rain = root.AddComponent<SpaceStation.Building.RainFx>();
                    var rso = new SerializedObject(rain);
                    rso.FindProperty("_clouds").objectReferenceValue = Child(root, N + "Halo");
                    rso.FindProperty("_module").objectReferenceValue = r;
                    var spots = rso.FindProperty("_cloudSpots");
                    spots.arraySize = 5;
                    for (int q = 0; q < 5; q++)
                        spots.GetArrayElementAtIndex(q).vector3Value = P(5.9f, 72f * q + 18f, F + 8.1f);   // bio3_builder clouds() 구름 밑면 가운데
                    rso.FindProperty("_ringCenter").vector3Value = Biosphere.Hub;
                    rso.FindProperty("_area").vector2Value = new Vector2(1.5f, 0.6f);
                    rso.FindProperty("_dropMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/FX/M_RainDrop.mat");
                    rso.FindProperty("_mistMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/FX/M_RainMist.mat");
                    rso.FindProperty("_fallSpeed").floatValue = 5.5f;
                    rso.FindProperty("_groundY").floatValue = F - 0.07f;
                    rso.FindProperty("_rate").floatValue = 90f;
                    rso.FindProperty("_shelterHalfWidth").floatValue = 0f;   // 구름 아래엔 아치가 없음 (아치 다리는 구름 위)
                    rso.FindProperty("_dropSize").vector2Value = new Vector2(0.005f, 0.008f);
                    rso.FindProperty("_dropStretch").floatValue = 0.05f;
                    rso.FindProperty("_sway").floatValue = 0.1f;
                    rso.FindProperty("_mistSize").vector2Value = new Vector2(1.2f, 1.9f);
                    rso.FindProperty("_mistFall").vector2Value = new Vector2(0.5f, 0.8f);
                    rso.FindProperty("_alwaysOn").boolValue = true;
                    rso.ApplyModifiedPropertiesWithoutUndo();
                    // 동물: 사슴 · 아기 사슴 · 토끼 (Biosphere.Critters — 꽃 · 잎이 비킨 자리) · 오리 가족 (Biosphere.DuckSpots) = SM_InteriorCritters (미터)
                    var critterSrc = new Dictionary<string, GameObject>();
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Models/Interior/SM_InteriorCritters.fbx"))
                    {
                        if (o is GameObject g && g.name.StartsWith("Critter_") && g.GetComponent<MeshFilter>() != null)
                            critterSrc[g.name] = g;
                    }
                    float[] yaws = { 40f, -60f, 80f, 30f, 200f, -40f };
                    var deer = new List<(string, Vector3, float)>();
                    var bunnies = new List<(string, Vector3, float)>();
                    for (int k = 0; k < Biosphere.Critters.Length; k++)
                    {
                        var (kind, cr, ca, slope) = Biosphere.Critters[k];
                        var at = P(cr, ca, slope ? Biosphere.SlopeY(cr) : F);
                        (kind == "Bunny" ? bunnies : deer).Add((kind, at, yaws[k]));
                    }
                    CritterGroup(r, critterSrc, "Critters_Deer", 13f, deer.ToArray());
                    CritterGroup(r, critterSrc, "Critters_Bunny", 6f, bunnies.ToArray());
                    // 오리 가족 (바깥 모듈과 같은 흰 어미 · 노란 새끼): 새끼는 어미가 지나간 자리를 줄지어 따라감, 헤엄 고리 = 연잎 · 갈대 · 바위 · 둑 · 선착장 · 폭포 기둥을 비키는 가장 큰 것
                    CritterGroup(r, critterSrc, "Critters_Duck", 7f, Biosphere.DuckSpots(),
                        SwimBlocker(root, Biosphere.Hub, Biosphere.ShoreRadius, (Biosphere.WaterLevels[0], Biosphere.FallsKeepOut), (Biosphere.WaterLevels[2], 0f)));
                    // 물: 호수 · 연못 · 하늘 호수 = KWS 물 (꺼 둠 → InteriorKwsWater가 켬) + 패키지 없는 PC용 Blender 수면, 물길 · 샘 = 바깥 물결 재질 (늘 보임)
                    var kws = new List<GameObject>();
                    var surfaces = new List<Renderer>();
                    float[] depth = { 0.65f, 0.4f, 0.4f };
                    for (int k = 0; k < Biosphere.WaterLevels.Length; k++)
                    {
                        var surface = Child(root, N + "Water" + (k == 0 ? "" : k.ToString()));
                        if (surface == null)
                            continue;
                        surfaces.Add(surface.GetComponent<Renderer>());
                        kws.Add(KwsWaterBuilder.Build(r, surface.GetComponent<MeshFilter>(), Biosphere.WaterLevels[k], depth[k], "Biosphere" + k, k != 1));
                    }
                    var small = Biosphere.SmallFallLandings();   // 단마다 작은 폭포 떨어지는 자리도 물보라 · 물결 (물이 흐르는 게 보이게)
                    var landings = new Vector3[Biosphere.Falls.Length + small.Length];
                    var widths = new float[landings.Length];
                    for (int i = 0; i < Biosphere.Falls.Length; i++)
                    {
                        landings[i] = Biosphere.Falls[i].Landing;
                        widths[i] = Biosphere.Falls[i].Width;
                    }
                    for (int i = 0; i < small.Length; i++)
                    {
                        landings[Biosphere.Falls.Length + i] = small[i];
                        widths[Biosphere.Falls.Length + i] = 0.4f;
                    }
                    if (kws.Count > 0 && kws[0] != null)
                        KwsWaterBuilder.Ripple(kws[0], landings[0], 1.0f, 0.5f);   // 폭포 기둥이 떨어지는 호수 가운데
                    kws.RemoveAll(x => x == null);
                    root.AddComponent<InteriorKwsWater>().Configure(kws.ToArray(), surfaces.ToArray(), Child(root, N + "Sky").GetComponent<Renderer>(), new Vector3(8f, 4f, 8f));
                    var fallsFx = root.AddComponent<InteriorWaterfallFx>();
                    fallsFx.Configure(landings, widths, AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.FallsMistPath));
                    var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Maplestory SDF.asset");
                    // 체험 "물꼬 맞추기" (InteriorSluiceGates, 사용자 2026-11-11): 수문 바퀴 = 바퀴 가운데 피벗 (시선 공) · 문판 · 물길 물 / 폭포 조각 (물길 × 단) ·
                    //  밭 (물길 × 단, 피벗 높이 = 단 윗면 — 세로로 줄이면 시듦) · 물레방아 (물이 지나갈 때만 돎)
                    int streams = SluicePuzzle.StreamCount, levels = SluicePuzzle.Levels;
                    var gateWheels = new Transform[streams];
                    var plates = new Transform[streams];
                    var chans = new Renderer[streams * (levels + 1)];
                    var spills = new Renderer[streams * (levels + 1)];
                    var crops = new Transform[streams * levels];
                    var waterWheels = new InteriorOrbit[streams];
                    for (int s = 0; s < streams; s++)
                    {
                        plates[s] = Child(root, N + "GatePlate" + s);
                        var wheel = Child(root, N + "GateWheel" + s);
                        var pivot = new GameObject("GateWheelPivot" + s).transform;
                        pivot.SetParent(r, false);
                        pivot.localPosition = Biosphere.GateWheelHub(s);
                        if (wheel != null)
                            wheel.SetParent(pivot, true);
                        pivot.gameObject.AddComponent<SphereCollider>().radius = 0.32f;
                        gateWheels[s] = pivot;
                        var ww = Child(root, N + "Wheel" + s);
                        waterWheels[s] = ww != null ? ww.GetComponent<InteriorOrbit>() : null;
                        for (int n = 0; n <= levels; n++)
                        {
                            var c = Child(root, $"{N}Chan{s}_{n}");
                            var f = Child(root, $"{N}Spill{s}_{n}");
                            chans[s * (levels + 1) + n] = c != null ? c.GetComponent<Renderer>() : null;
                            spills[s * (levels + 1) + n] = f != null ? f.GetComponent<Renderer>() : null;
                        }
                        for (int n = 1; n <= levels; n++)
                        {
                            var crop = Child(root, $"{N}Crop{s}_{n}");
                            if (crop == null)
                                continue;
                            var cp = new GameObject($"CropPivot{s}_{n}").transform;
                            cp.SetParent(r, false);
                            cp.localPosition = new Vector3(0f, Biosphere.TerraceTop(Biosphere.TerraceOfLevel(n)), 0f);
                            crop.SetParent(cp, true);
                            crops[s * levels + n - 1] = cp;
                        }
                    }
                    root.AddComponent<InteriorSluiceGates>().Configure(gateWheels, plates, chans, spills, crops, waterWheels, fallsFx, font);
                    // 미니게임 "반딧불이 잡기" (InteriorFireflyCatch): 길가 유리병 선반 (보고 F) · 숲 모서리 · 하늘 호수로 풀어 줌 · 선반 위 안내판
                    var rack = new GameObject("FireflyRack").transform;
                    rack.SetParent(r, false);
                    float rackYaw = Biosphere.OutwardYaw(Biosphere.FireflyAngle);
                    rack.localPosition = Biosphere.FireflyRack + Vector3.up * 0.65f;
                    rack.localRotation = Quaternion.Euler(0f, rackYaw, 0f);
                    var rackBox = rack.gameObject.AddComponent<BoxCollider>();
                    rackBox.size = new Vector3(1.2f, 1.3f, 0.4f);
                    root.AddComponent<InteriorFireflyCatch>().Configure(rackBox, ModuleFxMaterials.Get("FireworkSpark"), font, Child(root, N + "Sky").GetComponent<Renderer>(),
                        Biosphere.FireflyZone, Biosphere.FireflyZoneRadius, F, new Vector3(Biosphere.Hub.x, Biosphere.SkyTop + 0.6f, Biosphere.Hub.z),
                        Biosphere.FireflyRack + Vector3.up * 1.75f, rackYaw, Child(root, N + "Fx").GetComponent<Renderer>());
                });
        }

        /// <summary>
        /// 진화 3단계 항성 발전망 "꿀벌 정원" (바닥 7 × 2칸 + 위 2 × 2 (x 1 ~ 2) · 1 × 2 (x 5), Blender sg3_builder — 사용자 2026-11-11 시안 1 "좋아 이대로 유니티로 옮겨줘"). 맨몸 정원.
        /// 외형 × 8: 왼쪽 꽃나무 숲 → 등나무 꽃 정자 (진주 갈비대 여섯 · 금빛 띠 둘 · 진주 · 금 시렁 위 덩굴 · 잎 지붕 · 꽃송이, 금빛 처마 고리의 꽃송이 커튼, 안 = 꽃 고리 화단 · 진주 벤치 · 꿀 샘 · 떠 있는 수정 · 그네 틀)
        /// → 징검돌 길 · 해바라기 무리 · 들꽃 초원 · 꿀벌 물터 연못 · 전망대 (위 해치) → 꿀 샘 (미니게임 무대) → 꿀 언덕 (육각 기둥, 속 = 벌집 홀 · 꿀 저장 기둥) → 라벤더 이랑.
        /// 문 자리 = 꿀 언덕 칸 앞뒤 둘 (굴로 벌집 홀 · 언덕 앞뒤 산책길), 아래 해치 = 바닥 칸 열넷, 위 해치 = 전망대 위. 꿀벌 = 내부 전용 모델 (고리 길 · 맴돎 · 꽃 위 쉼).
        /// 체험 (정자 꽃 그네) · 미니게임 (곰 몰래 꿀 먹기)은 다음 단계 — 이번엔 걸어 다니는 공간 · 물 · 꿀벌.
        /// </summary>
        private static void BuildEvoStellarGrid()
        {
            const string N = "INT_EvoStellarGrid_";
            var sockets = new List<InteriorSocket>();
            foreach (var (cell, dir) in StellarGrid.Doors)
                sockets.Add(new InteriorSocket(cell, dir, StellarGrid.DoorDepth));
            foreach (var (cell, off) in StellarGrid.Hatches)
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, new Vector3(off.x, 0f, off.y)));
            sockets.Add(new InteriorSocket(StellarGrid.UpHatchCell, Vector3Int.up, StellarGrid.UpHatchDepth, StellarGrid.UpHatchOffset));
            var white = new Color(1f, 0.97f, 0.92f);
            var warm = new Color(1f, 0.86f, 0.62f);
            var honey = new Color(1f, 0.74f, 0.4f);
            var cool = new Color(0.82f, 0.9f, 1f);
            var lights = new List<LightSpec>();
            var pc = StellarGrid.PavilionCenter;
            lights.Add(LightSpec.Point(new Vector3(pc.x, F + 3.4f, pc.z), 8f, 1.0f, warm));                                    // 정자 가운데
            for (int k = 0; k < 6; k++)
                lights.Add(LightSpec.Point(StellarGrid.Lantern(k) + Vector3.down * 0.5f, 4.5f, 0.55f, honey));               // 꿀빛 등불 여섯
            foreach (float x in new[] { 4f, 20f, 28f, 35f })
                lights.Add(LightSpec.Point(new Vector3(x, F + 2.8f, -2.6f), 7f, 0.8f, white));                               // 앞 산책길
            foreach (float x in new[] { 4f, 20f, 28f, 36f, 44f })
                lights.Add(LightSpec.Point(new Vector3(x, F + 2.8f, 10.6f), 7f, 0.8f, white));                               // 뒤 산책길
            lights.Add(LightSpec.Point(new Vector3(0f, F + 3.0f, 4f), 7f, 0.8f, white));                                     // 꽃나무 숲
            lights.Add(LightSpec.Point(new Vector3(24f, F + 3.2f, 3.5f), 8f, 0.9f, white));                                  // 초원 · 해바라기
            lights.Add(LightSpec.Point(new Vector3(29f, F + 2.0f, 7.8f), 5f, 0.6f, cool));                                   // 연못
            lights.Add(LightSpec.Point(StellarGrid.Lookout + Vector3.up * 1.9f, 5f, 0.7f, white));                           // 전망대
            lights.Add(LightSpec.Point(StellarGrid.HoneySpring + new Vector3(0f, 2.4f, -0.6f), 5f, 0.8f, warm));             // 꿀 샘 (미니게임 무대)
            var hall = StellarGrid.HallCenter;
            lights.Add(LightSpec.Point(new Vector3(hall.x, F + 3.0f, hall.z), 6f, 1.1f, honey));                             // 벌집 홀 (꿀 저장 기둥)
            lights.Add(LightSpec.Point(new Vector3(hall.x, F + 2.1f, -0.6f), 4f, 0.8f, honey));                              // 앞 굴
            lights.Add(LightSpec.Point(new Vector3(hall.x, F + 2.1f, 9.6f), 4f, 0.8f, honey));                               // 뒤 굴
            lights.Add(LightSpec.Point(new Vector3(37.8f, F + 2.0f, StellarGrid.Portal.z), 4f, 0.7f, honey));                // 벌집 입구 굴
            lights.Add(LightSpec.Point(new Vector3(48f, F + 2.8f, 4f), 8f, 0.85f, white));                                   // 라벤더 이랑
            foreach (float x in new[] { 8f, 16f, 24f, 31f })
                lights.Add(LightSpec.Point(new Vector3(x, F + 4.2f, x < 20f ? -1.8f : 4.4f), 9f, 0.7f, white));                  // 초원 · 정자 앞 (어두운 우주 하늘 아래 잔디가 묻히지 않게)
            lights.Add(LightSpec.Point(new Vector3(34.6f, F + 4.4f, 4.6f), 9f, 0.9f, white));                                // 꿀 언덕 정자 쪽 면
            lights.Add(LightSpec.Point(new Vector3(45.4f, F + 4.4f, 4.4f), 8f, 0.8f, white));                                // 꿀 언덕 라벤더 쪽 면
            EvoAccent("StellarGrid", "#FFF0A0");
            // 스폰 = 앞 문 안 (꿀 언덕 앞 산책길): 앞 산책길 · 꿀 샘 · 해바라기 너머 등나무 꽃 정자를 바라봄.
            // 자기 바깥 모델을 하늘 화면에 보임: 위 칸 (정자 · 꿀 언덕)이 좁아, 초원에서 올려다보면 바닥 칸 천장 (3.95)이 정자 돔 · 언덕 꼭대기를 가렸음 —
            //  천장 너머 바깥 모델의 돔 · 언덕 윗부분이 같은 자리 (1/8 축척 화면)에 이어 보임
            Build("EvoStellarGrid", "Evolved/MD_Evo_StellarGrid", sockets, new Vector3(38.4f, F, -2.8f), 270f, lights, showOwnExterior: true,
                configure: root =>
                {
                    var r = root.transform;
                    // 연못 · 해바라기 · 쉬는 큰 꽃 줄기 · 나무 줄기에 못 들어가는 벽 (sg3_builder LakeHit): 모양만 쓰고 보이지 않음
                    var lakeHit = Child(root, N + "LakeHit");
                    var lakeMesh = lakeHit.GetComponent<MeshFilter>().sharedMesh;
                    Object.DestroyImmediate(lakeHit.GetComponent<MeshRenderer>());
                    Object.DestroyImmediate(lakeHit.GetComponent<MeshFilter>());
                    lakeHit.gameObject.AddComponent<MeshCollider>().sharedMesh = lakeMesh;
                    // 물: 꿀벌 물터 연못 = KWS 물 (꺼 둠 → InteriorKwsWater가 켬) + 패키지 없는 PC용 Blender 수면
                    var surface = Child(root, N + "Water");
                    var kws = KwsWaterBuilder.Build(r, surface.GetComponent<MeshFilter>(), StellarGrid.PondLevel, 0.35f, "StellarGrid0", false);
                    root.AddComponent<InteriorKwsWater>().Configure(kws != null ? new[] { kws } : new GameObject[0], new[] { surface.GetComponent<Renderer>() },
                        Child(root, N + "Sky").GetComponent<Renderer>(), new Vector3(24f, 2f, 4f));
                    // 꿀벌 (내부 전용 모델 SM_InteriorCritters — Critter_Bee + 날개 Critter_BeeWings, 미터): 고리 길 열둘 · 맴돎 다섯 + 경비 꿀벌 · 꽃 위 쉼 넷
                    GameObject body = null, wings = null;
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Models/Interior/SM_InteriorCritters.fbx"))
                    {
                        if (o is GameObject g && g.GetComponent<MeshFilter>() != null)
                        {
                            if (g.name == "Critter_Bee") body = g;
                            else if (g.name == "Critter_BeeWings") wings = g;
                        }
                    }
                    if (body == null || wings == null)
                    {
                        Debug.LogWarning("[InteriorTemplateBuilder] 내부 꿀벌 모델 없음 (SM_InteriorCritters Critter_Bee · Critter_BeeWings)");
                        return;
                    }
                    var holder = new GameObject("Bees").transform;
                    holder.SetParent(r, false);
                    var face = Quaternion.Euler(0f, 90f, 0f);   // 모델 앞 (로컬 −x) → +z
                    Transform Bee(string name, Vector3 at, Vector3 dir, float scale)
                    {
                        var t = new GameObject(name).transform;
                        t.SetParent(holder, false);
                        t.localPosition = at;
                        t.localRotation = Quaternion.LookRotation(dir, Vector3.up) * face * body.transform.localRotation;
                        t.localScale = body.transform.localScale * scale;
                        t.gameObject.AddComponent<MeshFilter>().sharedMesh = body.GetComponent<MeshFilter>().sharedMesh;
                        var mr = t.gameObject.AddComponent<MeshRenderer>();
                        mr.sharedMaterial = ModuleFxMaterials.Get("Critters");
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        var pivot = new GameObject("WingPivot").transform;   // 날개 뿌리 (등 위) — 여기서 폭 · 높이를 번갈아 바꿔 파닥임
                        pivot.SetParent(t, false);
                        pivot.localPosition = new Vector3(0f, 0.115f, 0f);
                        var w = new GameObject("Wings").transform;
                        w.SetParent(pivot, false);
                        w.localPosition = new Vector3(0f, -0.115f, 0f);
                        w.localRotation = Quaternion.Inverse(body.transform.localRotation) * wings.transform.localRotation;
                        w.localScale = new Vector3(wings.transform.localScale.x / body.transform.localScale.x, wings.transform.localScale.y / body.transform.localScale.y,
                            wings.transform.localScale.z / body.transform.localScale.z);
                        w.gameObject.AddComponent<MeshFilter>().sharedMesh = wings.GetComponent<MeshFilter>().sharedMesh;
                        var wr = w.gameObject.AddComponent<MeshRenderer>();
                        wr.sharedMaterial = ModuleFxMaterials.Get("Prism");
                        wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        return t;
                    }
                    var path = StellarGrid.BeePath;
                    var flyers = new Transform[12];
                    for (int i = 0; i < flyers.Length; i++)
                    {
                        float u = (float)i / flyers.Length;
                        var p = StellarGrid.PathPoint(path, u);
                        flyers[i] = Bee("Bee_Fly" + i, p, StellarGrid.PathPoint(path, u + 0.004f) - p, 1f);
                    }
                    var hovers = new Transform[StellarGrid.HoverBees.Length + 1];
                    for (int i = 0; i < StellarGrid.HoverBees.Length; i++)
                        hovers[i] = Bee("Bee_Hover" + i, StellarGrid.HoverBees[i], new Vector3(Mathf.Sin(i * 2.1f), 0f, Mathf.Cos(i * 2.1f)), 1f);
                    hovers[hovers.Length - 1] = Bee("Bee_Guard", StellarGrid.GuardBee, Vector3.right, StellarGrid.GuardScale);   // 미니게임 경비 꿀벌 (평소 = 벌집 쪽)
                    var resters = new Transform[StellarGrid.RestFlowers.Length];
                    for (int i = 0; i < resters.Length; i++)
                        resters[i] = Bee("Bee_Rest" + i, StellarGrid.RestFlowers[i] + Vector3.up * 0.1f, new Vector3(Mathf.Cos(i * 1.9f), 0f, Mathf.Sin(i * 1.9f)), 0.9f);
                    holder.gameObject.AddComponent<InteriorBeeFlight>().Configure(path, flyers, hovers, resters, 1.1f, body.transform.localRotation);
                });
        }

        /// <summary>내부 헤엄 고리 크기 (CritterFx.SwimSize 배율) — 큰 것부터 막히지 않는 것 (바깥 모듈 1 · 0.7 · 0.45보다 넓은 호수).</summary>
        private static readonly float[] InteriorSwimScales = { 2.5f, 2f, 1.5f, 1.2f, 1f, 0.7f, 0.45f };
        /// <summary>헤엄 막힘 몸 여유 = 간격 반지름 × 이만큼 (물높이 몸통 둘레 — 오리 가슴 끝 0.19 m · 간격 반지름 0.29 m).</summary>
        private const float SwimBody = 0.75f;

        /// <summary>
        /// 내부 동물 묶음 (궤도 도시와 같은 틀): 묶음 = CritterFx 흔들림 폭에 맞춘 배율, 동물 = 내부 전용 모델 (미터)을 그만큼 줄여 넣음 (실제 크기 = 미터).
        /// 새끼 오리 = 바로 앞 어미 (Swim)를 따라감 (StationArtBuilder.WireCritters와 같음). swimBlocked (템플릿 좌표 · 미터 반지름)가 있으면
        /// 헤엄 동물마다 고리 크기 · 도는 쪽을 골라 막히지 않게 (새끼 줄 길이 포함), 없으면 제자리에서 두리번.
        /// </summary>
        private static void CritterGroup(Transform parent, Dictionary<string, GameObject> critterSrc, string holderName, float scale, (string src, Vector3 at, float yaw)[] list,
            System.Func<Vector3, float, bool> swimBlocked = null)
        {
            var holder = new GameObject(holderName).transform;
            holder.SetParent(parent, false);
            holder.localScale = Vector3.one * scale;
            var critterMat = ModuleFxMaterials.Get("Critters");
            var parts = new List<Transform>();
            var kinds = new List<SpaceStation.Building.CritterFx.Kind>();
            foreach (var (src, at, yaw) in list)
            {
                if (!critterSrc.TryGetValue("Critter_" + src, out var g))
                {
                    Debug.LogWarning("[InteriorTemplateBuilder] 내부 동물 없음: " + src);
                    continue;
                }
                var t = new GameObject("Critter_" + src).transform;
                t.SetParent(holder, false);
                t.localPosition = at / scale;
                t.localRotation = Quaternion.Euler(0f, yaw, 0f) * g.transform.localRotation;
                t.localScale = g.transform.localScale / scale;
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = g.GetComponent<MeshFilter>().sharedMesh;
                var mr = t.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = critterMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                parts.Add(t);
                kinds.Add(StationArtBuilder.CritterKinds.TryGetValue(src, out var kind) ? kind : SpaceStation.Building.CritterFx.Kind.Swim);
            }
            var fx = holder.gameObject.AddComponent<SpaceStation.Building.CritterFx>();
            var so = new SerializedObject(fx);
            var arr = so.FindProperty("_critters");
            arr.arraySize = parts.Count;
            var leaders = new int[parts.Count];
            var headings = new Vector3[parts.Count];
            var radii = new float[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                int leader = -1;
                if (kinds[i] == SpaceStation.Building.CritterFx.Kind.Follow)
                {
                    for (int j = i - 1; j >= 0 && leader < 0; j--)
                    {
                        if (kinds[j] == SpaceStation.Building.CritterFx.Kind.Swim)
                            leader = j;
                        else if (kinds[j] != SpaceStation.Building.CritterFx.Kind.Follow)
                            break;
                    }
                    if (leader < 0)
                        kinds[i] = SpaceStation.Building.CritterFx.Kind.Swim;   // 따라갈 어미가 없으면 혼자 헤엄
                }
                leaders[i] = leader;
                StationArtBuilder.CritterShape(parts[i], null, kinds[i], out headings[i], out radii[i]);
                var e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Part").objectReferenceValue = parts[i];
                e.FindPropertyRelative("Kind").enumValueIndex = (int)kinds[i];
                e.FindPropertyRelative("Heading").vector3Value = headings[i];
                e.FindPropertyRelative("Radius").floatValue = radii[i];
                e.FindPropertyRelative("Leader").intValue = leader;
                e.FindPropertyRelative("Side").floatValue = 0f;
            }
            for (int i = 0; i < parts.Count && swimBlocked != null; i++)
            {
                if (kinds[i] != SpaceStation.Building.CritterFx.Kind.Swim)
                    continue;
                // 새끼 줄 길이 (CritterFx.Play의 어미 길 자국과 같은 계산) · 새끼 처음 자리도 막히면 안 됨
                var home = parts[i].localPosition;
                float chain = 0f;
                var prev = home;
                for (int j = i + 1; j < parts.Count; j++)
                {
                    if (leaders[j] != i)
                        continue;
                    var p = parts[j].localPosition;
                    chain += new Vector2(p.x - prev.x, p.z - prev.z).magnitude;
                    prev = p;
                    if (swimBlocked(p * scale, radii[j] * scale * SwimBody))
                        Debug.LogWarning($"[InteriorTemplateBuilder] {holderName}: 새끼 {j} 처음 자리가 물 위 물체에 닿음 {p * scale}");
                }
                if (swimBlocked(home * scale, radii[i] * scale * SwimBody))
                    Debug.LogWarning($"[InteriorTemplateBuilder] {holderName}: {parts[i].name} {i} 처음 자리가 물 위 물체에 닿음 {home * scale}");
                float side = 0f;
                foreach (float size in InteriorSwimScales)
                {
                    foreach (float s in new[] { 1f, -1f })
                    {
                        SpaceStation.Building.CritterFx.SwimSize(false, radii[i], chain, size, out float ra, out float rb);
                        bool clear = true;
                        for (int k = 1; k < 96 && clear; k++)
                            clear = !swimBlocked((home + SpaceStation.Building.CritterFx.SwimOffset(headings[i], s, ra, rb, 2f * Mathf.PI * k / 96f)) * scale, radii[i] * scale * SwimBody);
                        if (clear)
                        {
                            side = s * size;
                            break;
                        }
                    }
                    if (side != 0f)
                        break;
                }
                if (side == 0f)
                    Debug.LogWarning($"[InteriorTemplateBuilder] {holderName}: {parts[i].name} {i} 헤엄 고리가 다 막힘 → 제자리");
                arr.GetArrayElementAtIndex(i).FindPropertyRelative("Side").floatValue = side;
            }
            so.FindProperty("_autoPlay").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 물 위 동물 헤엄 막힘 (CritterGroup): 수면 − 2 cm ~ + 1.5 m 몸체 면 (연잎 · 갈대 · 바위 · 둑 · 선착장 · 물 위 빛 구슬)이 가로 반지름 안에 있거나
        /// 금지 원 안 (폭포 기둥 + 물보라)이면 막힘. 면은 10 cm 간격 점으로 — 꼭짓점만 보면 큰 상자 (선착장 마루 = 모서리 넷)를 못 잡아 마루 밑으로 헤엄칠 뻔했음.
        /// 연잎은 수면 − 1 ~ + 0.5 cm라 수면 위만 보면 빠져 오리가 연잎 위에 앉았음 → 수면 물체 (`Water*`)만 빼고 수면에 걸친 것도 봄. 무지개 띠 (옅은 빛)만 지나가도 됨. 좌표 = 템플릿 (미터), lakes = (수면 높이, 가운데에서 금지 반지름).
        /// </summary>
        private static System.Func<Vector3, float, bool> SwimBlocker(GameObject root, Vector3 center, float reach, params (float water, float keepOut)[] lakes)
        {
            const float Cell = 0.25f;
            var grid = new Dictionary<(int, int), List<Vector3>>();
            var toLocal = root.transform.worldToLocalMatrix;
            void Add(Vector3 p)
            {
                if (new Vector2(p.x - center.x, p.z - center.z).sqrMagnitude > reach * reach)
                    return;
                foreach (var (water, _) in lakes)
                {
                    if (p.y > water - 0.02f && p.y < water + 1.5f)
                    {
                        var key = (Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
                        if (!grid.TryGetValue(key, out var cell))
                            grid[key] = cell = new List<Vector3>();
                        cell.Add(p);
                        return;
                    }
                }
            }
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.name.StartsWith("Critter_") || mf.name.EndsWith("Rainbow") || mf.name.Contains("_Water"))
                    continue;
                var m = toLocal * mf.transform.localToWorldMatrix;
                var vs = mf.sharedMesh.vertices;
                for (int i = 0; i < vs.Length; i++)
                    vs[i] = m.MultiplyPoint3x4(vs[i]);
                var tris = mf.sharedMesh.triangles;
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 a = vs[tris[t]], b = vs[tris[t + 1]], c = vs[tris[t + 2]];
                    float lo = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), hi = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                    bool band = false;
                    foreach (var (water, _) in lakes)
                        band |= hi > water - 0.02f && lo < water + 1.5f;
                    if (!band || Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < center.x - reach || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > center.x + reach
                        || Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < center.z - reach || Mathf.Min(a.z, Mathf.Min(b.z, c.z)) > center.z + reach)
                        continue;
                    int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max((a - b).magnitude, Mathf.Max((b - c).magnitude, (c - a).magnitude)) / 0.1f), 1, 200);
                    for (int u = 0; u <= n; u++)
                        for (int v = 0; u + v <= n; v++)
                            Add(a + (b - a) * ((float)u / n) + (c - a) * ((float)v / n));
                }
            }
            return (p, radius) =>
            {
                foreach (var (water, keepOut) in lakes)
                    if (Mathf.Abs(p.y - water) < 0.05f && new Vector2(p.x - center.x, p.z - center.z).sqrMagnitude < keepOut * keepOut)
                        return true;
                for (int x = Mathf.FloorToInt((p.x - radius) / Cell); x <= Mathf.FloorToInt((p.x + radius) / Cell); x++)
                    for (int z = Mathf.FloorToInt((p.z - radius) / Cell); z <= Mathf.FloorToInt((p.z + radius) / Cell); z++)
                    {
                        if (!grid.TryGetValue((x, z), out var cell))
                            continue;
                        foreach (var q in cell)
                            if (q.y > p.y - 0.02f && q.y < p.y + 1.5f && (q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z) < radius * radius)
                                return true;
                    }
                return false;
            };
        }

        private static Transform Child(GameObject root, string name) => root.transform.Find(name);

        /// <summary>12-5 모듈 강조색 도장 (발광 없음) `M_Paint_{모듈}.mat` — 강조 재질 바탕색을 따름. 강조 재질(발광)을 큰 소품에 쓰면 평평하게 빛났음.</summary>
        private static Material PaintFor(Material accent, string moduleName)
        {
            if (accent == null)
                return null;
            string path = AccentFolder + "M_Paint_" + moduleName.Replace("MD_", "") + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", accent.GetColor("_BaseColor"));
            m.SetFloat("_Metallic", 0.25f);
            m.SetFloat("_Smoothness", 0.5f);
            m.DisableKeyword("_EMISSION");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>12-5 센서 어레이: 관측 관제실 (가운데 레이더 표시대 · 도는 빛줄, 구석 콘솔 3, 둥근 천창 너머 자기 접시). 해치 = (−2.25, −2.25).</summary>
        private static void BuildSensorArray()
        {
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(0f, F + 3.0f, 0f), 6f, 1.8f, new Color(0.72f, 0.82f, 1f)),
                LightSpec.Point(new Vector3(0f, F + 0.75f, 0f), 3f, 0.9f, new Color(0.4f, 0.9f, 1f)),  // 레이더 화면 빛 (표시대 윗면 아래 — 화면 위 반사점 방지)
                LightSpec.Point(new Vector3(-2.25f, F + 2.8f, -2.25f), 3f, 0.8f, new Color(0.9f, 0.92f, 1f)), // 해치 구석
            };
            var orbits = new List<OrbitSpec> { new OrbitSpec("INT_Sensor_Sweep", Vector3.zero, Vector3.up, 60f) };
            Build("Sensor", "MD_SensorArray", OneCellSockets(Socket, new Vector3(-2.25f, 0f, -2.25f)), new Vector3(0f, F, -1.9f), 0f, lights, orbits,
                showOwnExterior: true,
                configure: root => root.AddComponent<InteriorRadar>().Configure(
                    new Vector3(0f, -0.3685f, 0f), 0.8f,                         // sensor_builder ANCHORS 레이더 표시대 윗면
                    Child(root, "INT_Sensor_Sweep")?.GetComponent<InteriorOrbit>(),
                    Child(root, "INT_Sensor_Beacon")?.GetComponent<Renderer>(),
                    new Vector3(-2.4228f, -0.0425f, 2.7905f), new Vector3(0.6917f, 0.2079f, -0.6917f), new Vector3(-0.147f, 0.9781f, 0.147f),
                    new Vector2(0.48f, 0.3f)));
        }

        /// <summary>12-5 검역소: 격리 병동 (가운데 팔각 유리 격리실 + 소독 에어록, 구석 방호복 사물함 · 검체 분석대 · 폐기물). 해치 = (−2.25, −2.25).</summary>
        private static void BuildQuarantine()
        {
            var white = new Color(0.95f, 0.98f, 1f);
            var lights = new List<LightSpec> { LightSpec.Point(new Vector3(0f, F + 2.5f, 0f), 3f, 1.2f, new Color(0.88f, 1f, 0.92f)) }; // 격리실 안
            foreach (var (x, z) in new[] { (2.2f, 2.2f), (-2.2f, 2.2f), (2.2f, -2.2f), (-2.2f, -2.2f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 3.0f, z), 4.5f, 1.6f, white));
            // 격리실 출입문 = 따로 움직이는 조각 INT_Quarantine_Door (바깥 레일, quarantine_builder ANCHORS): 다가가면 +x로 0.95 열림
            Build("Quarantine", "MD_Quarantine", OneCellSockets(Socket, new Vector3(-2.25f, 0f, -2.25f)), new Vector3(-2.0f, F, 0f), 90f, lights,
                configure: root => root.AddComponent<InteriorSlidingDoor>().Configure(Child(root, "INT_Quarantine_Door"), new Vector3(0.95f, 0f, 0f),
                    new Vector3(0f, F, -1.5f), 1.7f));
        }

        /// <summary>12-5 단백질 배양실: 배양 탱크 2 → 압출기 → 컨베이어 → 포장 상자. 뒤(+z) = 생산 라인 벽(문 자리 없음, 모듈 닫힌 면). 해치 = (−2.25, −2.25).</summary>
        private static void BuildProteinVat()
        {
            var warm = new Color(1f, 0.9f, 0.76f);
            var lights = new List<LightSpec>();
            foreach (var (x, z) in new[] { (1.6f, 1.6f), (-1.6f, 1.6f), (1.6f, -1.6f), (-1.6f, -1.6f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 3.0f, z), 5f, 1.8f, warm));
            // 배양액 빛(주황 점광원 2)은 뺌 — 탱크 유리에 강한 주황 반사점 · 붉은 반사광 (2026-10-19 피드백). 배양액은 발광 재질 + 블룸으로 충분
            Build("Protein", "MD_ProteinVat", OneCellSockets(Socket, new Vector3(-2.25f, 0f, -2.25f), pz: false), new Vector3(0f, F, -2.0f), 0f, lights,
                configure: root => root.AddComponent<InteriorConveyor>().Configure(   // protein_builder ANCHORS
                    Child(root, "INT_Protein_Bar"), Child(root, "INT_Protein_Press"), Child(root, "INT_Protein_CrateLid"),
                    new Vector3(0f, -0.45f, 2.35f), new Vector3(0f, -0.45f, 1.0f), new Vector3(0f, -1.24f, 0.67f), 2.18f, 0.03f,
                    new[]
                    {
                        new Vector4(-0.211f, -1.235f, 0.536f, 10.6f), new Vector4(-0.026f, -1.235f, 0.551f, -9.4f), new Vector4(0.173f, -1.235f, 0.55f, -32.4f),
                        new Vector4(-0.204f, -1.235f, 0.733f, -28.7f), new Vector4(-0.005f, -1.235f, 0.763f, -26.3f), new Vector4(-0.117f, -1.195f, 0.605f, 31.3f),
                        new Vector4(0.105f, -1.195f, 0.596f, 33.3f), new Vector4(-0.127f, -1.195f, 0.784f, -14.7f), new Vector4(0.079f, -1.195f, 0.755f, -13.4f),
                    },
                    new Vector3(-0.345f, -0.98f, 0.675f), new Vector3(0f, 0f, 1f), -100f));
        }

        /// <summary>12-5 물 추출기: 얼음 → 물 공정 (깔때기 · 녹이는 드럼 → 응축탑 → 정수 필터 → 물탱크, 천장 둘레 배관) + 동물 주민용 정수기.</summary>
        private static void BuildWaterExtractor()
        {
            var cool = new Color(0.86f, 0.93f, 1f);
            var lights = new List<LightSpec>();
            foreach (var (x, z) in new[] { (1.6f, 1.6f), (-1.6f, 1.6f), (1.6f, -1.6f), (-1.6f, -1.6f) })
                lights.Add(LightSpec.Point(new Vector3(x, F + 3.0f, z), 5f, 1.8f, cool));
            // 드럼 가열 띠 빛(주황 점광원)은 뺌 — 바닥 · 벽에 붉은 반사광이 생겼음 (2026-10-19 피드백). 띠는 발광 재질 + 블룸으로 충분
            Build("WaterEx", "MD_WaterExtractor", OneCellSockets(Socket, Vector3.zero), new Vector3(0f, F, -1.8f), 0f, lights,
                configure: root =>
                {
                    // 낙하관 속 얼음 조각 INT_WaterEx_Ice0.. (waterex_builder ANCHORS: 깔때기 속 F + 2.95 → 드럼 속 F + 1.3)
                    var ice = new List<Transform>();
                    foreach (Transform t in root.transform)
                    {
                        if (t.name.StartsWith("INT_WaterEx_Ice"))
                            ice.Add(t);
                    }
                    root.AddComponent<InteriorFalling>().Configure(ice.ToArray(), F + 2.95f, F + 1.3f, 0.35f, 40f);
                });
        }

        /// <summary>12-5 데이터 센터 (2칸): 서버 홀 (랙 두 줄 · 차가운 통로 · 케이블 트레이), 천장 배기 팬 2 = 외형 지붕 팬 자리(칸 중심, z +0.96) 회전.
        /// 팬이 칸 가운데 천장을 차지해 해치 = 칸 중심에서 z −0.8.</summary>
        private static void BuildDataCenter()
        {
            var cool = new Color(0.72f, 0.82f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(2.5f, F + 2.9f, 0f), 5f, 1.6f, cool),
                LightSpec.Point(new Vector3(5.5f, F + 2.9f, 0f), 5f, 1.6f, cool),
                LightSpec.Point(new Vector3(0f, F + 3.0f, 0f), 5f, 1.4f, cool),
                LightSpec.Point(new Vector3(8f, F + 3.0f, 0f), 5f, 1.4f, cool),
                LightSpec.Point(new Vector3(-2.2f, F + 3.0f, 0f), 4f, 1.2f, cool),
                LightSpec.Point(new Vector3(10.2f, F + 3.0f, 0f), 4f, 1.2f, cool),
                LightSpec.Point(new Vector3(4f, F + 1.0f, 0f), 3.5f, 0.6f, new Color(0.35f, 0.85f, 1f)), // 표시등 번짐
            };
            float fanY = F + 3.6f - 0.11f;
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_DataCenter_FanA", new Vector3(0f, fanY, 0.96f), Vector3.up, 300f),
                new OrbitSpec("INT_DataCenter_FanB", new Vector3(8f, fanY, 0.96f), Vector3.up, 300f),
            };
            Build("DataCenter", "MD_DataCenter", TwoCellSockets(new Vector3(0f, 0f, -0.8f)), new Vector3(-2.2f, F, 0f), 90f, lights, orbits,
                configure: root => root.AddComponent<InteriorServerLights>().Configure(   // dc_builder ANCHORS (벽 상황판 · 오른쪽 모니터)
                    new[]
                    {
                        Child(root, "INT_DataCenter_LedA")?.GetComponent<Renderer>(),
                        Child(root, "INT_DataCenter_LedB")?.GetComponent<Renderer>(),
                        Child(root, "INT_DataCenter_LedC")?.GetComponent<Renderer>(),
                    },
                    new InteriorServerLights.Panel { Center = new Vector3(-2.5f, 0.445f, -3.142f), Normal = Vector3.forward, Up = Vector3.up, Width = 0.9f, Height = 0.46f },
                    new InteriorServerLights.Panel
                    {
                        Center = new Vector3(-2.2839f, -0.31f, -2.9445f), Normal = new Vector3(-0.2419f, 0f, 0.9703f), Up = Vector3.up, Width = 0.38f, Height = 0.21f,
                    }));
        }

        /// <summary>12-5 상업 구역 (2칸): 실내 상점가 (양쪽 가게 4칸씩 · 줄무늬 차양), 가운데 유리 피라미드 아트리움 아래 화단(나무 = 따로 놓는 소품) · 둥근 벤치.</summary>
        private static void BuildCommerce()
        {
            var warm = new Color(1f, 0.9f, 0.76f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(0f, F + 3.0f, 0f), 6.5f, 2.4f, warm),
                LightSpec.Point(new Vector3(8f, F + 3.0f, 0f), 6.5f, 2.4f, warm),
                LightSpec.Spot(new Vector3(4f, F + 3.6f + 2.0f, 0f), Vector3.down, 9f, 6f, 70f, new Color(0.95f, 0.97f, 1f)), // 아트리움 햇빛
                LightSpec.Point(new Vector3(4f, F + 3.4f, 0f), 4f, 1.2f, new Color(0.9f, 0.95f, 1f)), // 피라미드 살대
            };
            foreach (float z in new[] { 2.55f, -2.55f })   // 가게 안 따뜻한 불빛 (가게마다)
            {
                foreach (float x in new[] { -2.5f, 2.9f, 5.1f, 10.5f })
                    lights.Add(LightSpec.Point(new Vector3(x, F + 2.2f, z), 3f, 0.9f, new Color(1f, 0.82f, 0.6f)));
            }
            Build("Commerce", "MD_Commerce", TwoCellSockets(Vector3.zero), new Vector3(-2.2f, F, 0f), 90f, lights);
        }

        /// <summary>12-5 행정 관제소 (2칸): 서쪽 민원 홀 (창구 3 · 대기 의자 · 발권기 · 천장 돔) + 칸막이 + 동쪽 관제실 (모듈 상태 지도 벽 · 콘솔 4 · 레이더).
        /// 지도 = 실제 정거장 + 아래 층 버튼 (<see cref="InteriorStationMap"/>), 창구 번호 · 발권기 = 실제 직원 · 민원인이 움직일 때 (<see cref="InteriorQueueBoard"/>,
        /// 주민 자리는 ResidentSpotBaker 고정 자리). 해치: 0번 칸 (−2.0, −2.0) — 구석 벽기둥 받침과 떨어지게, 1번 칸 (2.25, −2.25).</summary>
        private static void BuildAdministration()
        {
            var warm = new Color(1f, 0.93f, 0.82f);
            var cool = new Color(0.8f, 0.88f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(0.25f, F + 3.0f, 0f), 6.5f, 2.2f, warm),   // 돔 아래
                LightSpec.Point(new Vector3(-2.3f, F + 3.0f, 0.8f), 4.5f, 1.2f, warm),
                LightSpec.Point(new Vector3(2.6f, F + 3.0f, 0.5f), 4.5f, 1.2f, warm),  // 창구 앞
                LightSpec.Point(new Vector3(6.8f, F + 3.1f, 0f), 6f, 1.7f, cool),
                LightSpec.Point(new Vector3(9.6f, F + 3.1f, 0.3f), 5f, 1.4f, cool),
            };
            var sockets = new List<InteriorSocket>();
            var hatches = new[] { new Vector3(-2.0f, 0f, -2.0f), new Vector3(2.25f, 0f, -2.25f) };
            for (int x = 0; x < 2; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, hatches[x]));
                sockets.Add(new InteriorSocket(cell, Vector3Int.up, F + 3.6f, hatches[x]));
            }
            // admin_builder ANCHORS["sweep"] (레이더 화면 법선 축)
            var orbits = new List<OrbitSpec>
            {
                new OrbitSpec("INT_Admin_Sweep", new Vector3(10.1812f, 0.1083f, 2.2996f), new Vector3(-0.6116f, 0.5736f, -0.5449f), 60f),
            };
            Build("Admin", "MD_Administration", sockets, new Vector3(-2.2f, F, 0f), 90f, lights, orbits,
                configure: root =>
                {
                    // admin_builder ANCHORS["map"] · ["floor_label"] · ["floor_buttons"]
                    var map = root.AddComponent<InteriorStationMap>();
                    map.Configure(new Vector3(4.213f, 0.925f, 0.55f), Vector3.right, Vector3.up, new Vector2(4.8f, 1.55f),
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ScreenPath),
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ScreenGridPath));
                    var up = Child(root, "INT_Admin_FloorUp");
                    var down = Child(root, "INT_Admin_FloorDown");
                    foreach (var b in new[] { up, down })   // F 시선 광선에 잡히게 (메시 경계 상자)
                    {
                        if (b != null)
                            b.gameObject.AddComponent<BoxCollider>();
                    }
                    map.ConfigureFloors(up, down, new Vector3(4.193f, -0.06f, 0.55f), new Vector3(4.193f, -0.17f, 0.88f), new Vector3(4.193f, -0.17f, 0.22f),
                        new Vector3(-0.012f, 0f, 0f));
                    // ["queue"] · ["staff"] · ["customer"] · ["ticket"] · ["ticket_stand"] — 1번 · 2번 창구 (3번 = 발바닥 도장, 번호 없음)
                    InteriorQueueBoard.Window Window(float boardZ, float z) => new InteriorQueueBoard.Window
                    {
                        Board = new InteriorQueueBoard.Panel { Center = new Vector3(3.812f, 0.67f, boardZ), Normal = Vector3.left, Up = Vector3.up, Width = 0.76f, Height = 0.24f },
                        StaffSeat = new Vector3(3.5f, F + 0.49f, z),
                        Counter = new Vector4(2.4f, F, z, 90f),
                    };
                    var queue = root.AddComponent<InteriorQueueBoard>();
                    queue.Configure(new[] { Window(1.965f, 2.125f), Window(0.39f, 0.55f) },
                        new InteriorQueueBoard.Panel { Center = new Vector3(-2.752f, -0.5f, 2.6f), Normal = Vector3.right, Up = Vector3.up, Width = 0.26f, Height = 0.14f },
                        new Vector4(-2.35f, F, 2.6f, -90f), 0f, 1.55f, 2.2f, new Vector3(-2.35f, F, 2.2f));   // 발권기 길 = 의자 줄 뒤(z 2.2) — 들어오는 곳을 피함
                    // 민원인 통로 (보급 상자 · 부탁 물건을 두지 않음): 의자 앞 두 줄 · 가운데 통로 · 줄 기둥 앞 · 창구 앞 2 · 발권기 길
                    queue.ConfigureLanes(new[]
                    {
                        new Vector4(-0.2f, -1.75f, -0.2f, 2.2f), new Vector4(1.0f, -1.75f, 1.0f, 2.2f), new Vector4(-0.2f, 0f, 1.55f, 0f),
                        new Vector4(1.55f, 0f, 1.55f, 2.125f), new Vector4(1.55f, 0.55f, 2.4f, 0.55f), new Vector4(1.55f, 2.125f, 2.4f, 2.125f),
                        new Vector4(1.0f, 2.2f, -2.35f, 2.2f), new Vector4(-2.35f, 2.2f, -2.35f, 2.6f),
                    }, 0.35f);
                });
        }

        /// <summary>
        /// 12-5 방사선 차폐실 (2칸, 사용자 1안 2026-10-21): 원통 콘크리트 천장 + 칸막이 금고문 통로 — 서쪽 제염실(선량계 게이트 · 제염 샤워 부스 ·
        /// 방호복 · 열린 금고문 문짝) / 동쪽 대피실(2층 침상 · 벤치 · 비상 물자 · 공기 정화기). 경보판 = 통로 위 양면 (<see cref="InteriorRadiationBoard"/>,
        /// 정원 = 침상 4 + 벤치 8). 해치 = 원통 천장 꼭대기 평평한 띠 (0번 칸 (−0.8, 0) · 1번 칸 (1.5, 0)).
        /// </summary>
        private static void BuildRadiationShelter()
        {
            var cool = new Color(0.9f, 0.95f, 1f);
            var warm = new Color(1f, 0.9f, 0.76f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(-2.5f, F + 3.3f, 0f), 5.5f, 1.6f, cool),     // 제염실 천장 등 (shelter_builder LAMPS)
                LightSpec.Point(new Vector3(1.0f, F + 3.3f, 0f), 5.5f, 1.8f, cool),
                LightSpec.Point(new Vector3(2.7f, F + 2.0f, -2.65f), 2.2f, 0.6f, cool),   // 샤워 부스 안
                LightSpec.Point(new Vector3(5.2f, F + 3.3f, 0f), 5.5f, 1.7f, warm),      // 대피실 천장 등
                LightSpec.Point(new Vector3(7.2f, F + 3.3f, 0f), 5.5f, 1.7f, warm),
                LightSpec.Point(new Vector3(10.3f, F + 2.8f, 0f), 4.5f, 1.1f, warm),     // 동쪽 (선반 · 정화기)
            };
            var sockets = new List<InteriorSocket>();
            var hatches = new[] { new Vector3(-0.8f, 0f, 0f), new Vector3(1.5f, 0f, 0f) };
            for (int x = 0; x < 2; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, 1), Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, hatches[x]));
                sockets.Add(new InteriorSocket(cell, Vector3Int.up, F + 3.6f, hatches[x]));
            }
            Build("Shelter", "MD_RadiationShelter", sockets, new Vector3(-2.3f, F, 0f), 90f, lights,
                configure: root =>
                {
                    // shelter_builder ANCHORS["board_w"] · ["board_e"] (화면 1.88 × 0.7)
                    InteriorRadiationBoard.Panel Board(float x, float nx) => new InteriorRadiationBoard.Panel
                    {
                        Center = new Vector3(x, F + 3.02f, 0f), Normal = new Vector3(nx, 0f, 0f), Up = Vector3.up, Width = 1.88f, Height = 0.7f,
                    };
                    root.AddComponent<InteriorRadiationBoard>().Configure(new[] { Board(3.625f, -1f), Board(4.375f, 1f) }, 12);
                });
        }

        /// <summary>
        /// 12-5 여객 도킹 포트 (2칸, 사용자 결정 2026-10-21: 대합실 + 입국장을 칸별로). 앞면(+z) = 이민선 접근로라 문 자리 없음 → 앞벽 전체 유리
        /// (바깥 화면) + 둥근 탑승구 2. 서쪽 입국장 (탑승구 1 → 입국 심사 부스 → 수하물 컨베이어 → 환영 아치 → 뒤 문) / 동쪽 대합실 (탑승구 2 ·
        /// 창을 보는 의자 14 · 안내 데스크 · 자판기). 칸막이 위 양면 도착 안내판 = 실제 이민선 시계 (<see cref="InteriorArrivalBoard"/>).
        /// 천장 = 외형 물결 지붕 (칸 가운데가 높음), 뒤쪽 띠는 평평 F + 3.6 → 해치 = 0번 칸 (−2.15, −2.15) · 1번 칸 (2.15, −2.15).
        /// </summary>
        private static void BuildPassengerPort()
        {
            var warm = new Color(1f, 0.94f, 0.86f);
            var cool = new Color(0.86f, 0.92f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(0f, F + 3.4f, 0.9f), 6.5f, 2.0f, warm),     // 입국장 물결 천장 아래
                LightSpec.Point(new Vector3(-2.0f, F + 3.0f, 1.6f), 4.5f, 1.0f, warm),
                LightSpec.Point(new Vector3(2.4f, F + 3.0f, -1.0f), 4.5f, 1.1f, warm),   // 수하물 컨베이어
                LightSpec.Point(new Vector3(0f, F + 2.9f, -2.1f), 4f, 0.9f, warm),       // 환영 아치
                // 입국 심사 부스 안 (직원 얼굴 앞 · 모니터 화면 뒤 — 화면에 반사점이 생기지 않게): 사용자 요청 2026-10-21 "부스가 너무 어두워 보임" → 0.9는 머리가 하얗게 떠서 "조금만 줄이자" → 0.45
                LightSpec.Point(new Vector3(-0.7f, F + 1.42f, 1.0f), 1.5f, 0.45f, warm),
                LightSpec.Point(new Vector3(0.7f, F + 1.42f, 1.0f), 1.5f, 0.45f, warm),
                LightSpec.Point(new Vector3(8f, F + 3.4f, 0.9f), 6.5f, 2.0f, warm),     // 대합실
                LightSpec.Point(new Vector3(5.6f, F + 3.0f, 0.6f), 5f, 1.2f, warm),
                LightSpec.Point(new Vector3(10.2f, F + 3.0f, 0.6f), 4.5f, 1.0f, warm),
                LightSpec.Point(new Vector3(5.8f, F + 2.8f, -1.9f), 4f, 1.0f, warm),     // 안내 데스크
                LightSpec.Point(new Vector3(4f, F + 2.2f, 2.5f), 5f, 0.5f, cool),        // 유리벽 쪽
            };
            var sockets = new List<InteriorSocket>();
            var hatches = new[] { new Vector3(-2.15f, 0f, -2.15f), new Vector3(2.15f, 0f, -2.15f) };
            for (int x = 0; x < 2; x++)
            {
                var cell = new Vector3Int(x, 0, 0);
                sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                sockets.Add(new InteriorSocket(cell, new Vector3Int(0, 0, -1), Socket));
                sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F, hatches[x]));
                sockets.Add(new InteriorSocket(cell, Vector3Int.up, F + 3.6f, hatches[x]));
            }
            Build("Passenger", "MD_PassengerPort", sockets, new Vector3(-2.2f, F, 0f), 90f, lights,
                configure: root =>
                {
                    // passenger_builder ANCHORS["board_w"] · ["board_e"] (화면 1.96 × 0.62) · ["labels"] (표지 글자)
                    InteriorArrivalBoard.Panel Board(float x, float nx) => new InteriorArrivalBoard.Panel
                    {
                        Center = new Vector3(x, F + 2.95f, 0.7f), Normal = new Vector3(nx, 0f, 0f), Up = Vector3.up, Width = 1.96f, Height = 0.62f,
                    };
                    InteriorSignLabels.Label Label(float x, float y, float z, Vector3 normal, float w, float h, string text) => new InteriorSignLabels.Label
                    {
                        Center = new Vector3(x, y, z), Normal = normal, Up = Vector3.up, Width = w, Height = h, Text = text,
                    };
                    var labels = new[]
                    {
                        Label(0.17f, 1.51f, 3.005f, Vector3.back, 0.82f, 0.32f, "도착"),
                        Label(8.17f, 1.51f, 3.005f, Vector3.back, 0.82f, 0.32f, "탑승"),
                        Label(0f, 0.51f, 0.965f, Vector3.back, 1.1f, 0.2f, "입국 심사"),
                        Label(0f, 0.51f, 1.035f, Vector3.forward, 1.1f, 0.2f, "입국 심사"),
                        Label(2.465f, 0.36f, -0.85f, Vector3.left, 0.4f, 0.18f, "수하물"),
                        Label(2.535f, 0.36f, -0.65f, Vector3.right, 0.4f, 0.18f, "수하물"),
                        Label(0f, 1.8f, -1.745f, Vector3.back, 1.36f, 0.28f, "입국장"),
                        Label(0f, 1.8f, -1.655f, Vector3.forward, 1.36f, 0.28f, "어서 오세요"),
                        Label(5.8f, 0.86f, -1.77f, Vector3.forward, 0.6f, 0.13f, "안내"),
                    };
                    root.AddComponent<InteriorArrivalBoard>().Configure(new[] { Board(3.925f, -1f), Board(4.075f, 1f) });
                    root.AddComponent<InteriorSignLabels>().Configure(labels);
                });
        }

        /// <summary>
        /// 12-5 방열판 (1칸, 사용자 결정 2026-10-22: 냉각 펌프실 · 열 감시판 · 기술자 일터). 가운데 열교환기(관 다발 · 고온 / 저온 집합관 · 조작반),
        /// 북쪽 두 구석 펌프 2, 천장 = 바깥 날개판 뿌리 10줄 + 주황(고온) · 하늘색(저온) 냉각관. 동쪽 벽 남쪽 칸 열 감시판 = 맞닿은 모듈의 실제 열
        /// (<see cref="InteriorHeatBoard"/>). 해치 = (−2.25, −2.25) — 남서 구석 비움.
        /// </summary>
        private static void BuildRadiator()
        {
            var cool = new Color(0.86f, 0.93f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(0f, F + 3.0f, 0f), 6.5f, 1.8f, cool),          // 열교환기 위
                LightSpec.Point(new Vector3(-2.2f, F + 2.6f, 2.0f), 3.5f, 0.8f, cool),     // 펌프 2
                LightSpec.Point(new Vector3(2.2f, F + 2.6f, 2.0f), 3.5f, 0.8f, cool),
                LightSpec.Point(new Vector3(2.2f, F + 2.5f, -2.3f), 3f, 0.7f, cool),       // 열 감시판 앞
                LightSpec.Point(new Vector3(-2.0f, F + 2.8f, -1.6f), 4f, 0.8f, cool),      // 해치 구석
            };
            Build("Radiator", "MD_Radiator", OneCellSockets(Socket, new Vector3(-2.25f, 0f, -2.25f)), new Vector3(0f, F, -2.3f), 0f, lights,
                configure: root =>
                {
                    // radiator_builder ANCHORS["board"] (화면 1.2 × 1.12, 동쪽 벽 → −x를 봄) · ["labels"]
                    root.AddComponent<InteriorHeatBoard>().Configure(new Vector3(3.105f, F + 1.7f, -2.45f), Vector3.left, Vector3.up, new Vector2(1.2f, 1.12f),
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ScreenPath), AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.InteriorHullDarkPath));   // 막대 바탕 = 어두운 홈 (밝으면 0%도 가득 찬 막대처럼 보였음)
                    InteriorSignLabels.Label Label(float x, float y, float z, Vector3 normal, float w, float h, string text) => new InteriorSignLabels.Label
                    {
                        Center = new Vector3(x, y, z), Normal = normal, Up = Vector3.up, Width = w, Height = h, Text = text,
                    };
                    root.AddComponent<InteriorSignLabels>().Configure(new[]
                    {
                        Label(-1.05f, F + 1.27f, -0.145f, Vector3.back, 0.22f, 0.1f, "고온"),
                        Label(1.05f, F + 1.27f, -0.145f, Vector3.back, 0.22f, 0.1f, "저온"),
                        Label(0f, F + 1.73f, -0.53f, Vector3.back, 0.86f, 0.15f, "열교환기"),
                        Label(0f, F + 1.73f, 0.53f, Vector3.forward, 0.86f, 0.15f, "열교환기"),
                    });
                });
        }

        /// <summary>
        /// 12-5 통신 중계소 (1칸, 사용자 결정 2026-10-22: 신호 해독실 · 신호판 + 평판 · 연구원 일터). 가운데 수신 기둥 (유리 관 속 빛 고리 4가
        /// 위에서 아래로 흐름) + 천장 접시 회전 구동부 (INT_CommRelay_Drive, 바깥 접시 SpinDish와 같은 초당 8°). 북동 ㄱ자 해독 책상 + 벽 화면 2
        /// (북쪽 = 신호판, 동쪽 = 세력 평판 — 실제 외부 접촉 값, <see cref="InteriorSignalBoard"/>), 북서 기록 책상 (기록계 · 릴 테이프 데크),
        /// 남동 송수신기 함 (다이얼 · 패치 베이). 해치 = (−2.25, −2.25) — 남서 구석 비움.
        /// </summary>
        private static void BuildCommRelay()
        {
            var cool = new Color(0.88f, 0.92f, 1f);
            var warm = new Color(1f, 0.9f, 0.78f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(2.15f, F + 3.0f, 0f), 4.5f, 1.1f, cool),
                LightSpec.Point(new Vector3(-2.15f, F + 3.0f, 0f), 4.5f, 1.1f, cool),
                LightSpec.Point(new Vector3(0f, F + 3.0f, 2.15f), 4.5f, 1.1f, cool),
                LightSpec.Point(new Vector3(0f, F + 3.0f, -2.15f), 4.5f, 1.1f, cool),
                LightSpec.Point(new Vector3(0f, F + 1.75f, 0f), 2.8f, 0.7f, new Color(0.78f, 0.68f, 1f)),   // 수신 기둥 빛 (기둥 중심)
                LightSpec.Point(new Vector3(2.25f, F + 2.5f, 2.25f), 2.2f, 0.45f, warm),                    // 해독 책상 매단 등
                LightSpec.Point(new Vector3(-2.25f, F + 2.5f, 2.25f), 2.2f, 0.45f, warm),                   // 기록 책상 매단 등
                LightSpec.Point(new Vector3(-2.0f, F + 2.8f, -2.0f), 3.5f, 0.6f, cool),                     // 해치 구석
            };
            var orbits = new List<OrbitSpec> { new OrbitSpec("INT_CommRelay_Drive", Vector3.zero, Vector3.up, 8f) };
            // 스폰 = 남쪽 문 앞 조금 동쪽 — 가운데 기둥이 시야를 덜 가리고 북동 해독 책상 · 화면이 보이게
            Build("CommRelay", "MD_CommRelay", OneCellSockets(Socket, new Vector3(-2.25f, 0f, -2.25f)), new Vector3(0.7f, F, -2.4f), 10f, lights, orbits,
                configure: root =>
                {
                    // comm_builder ANCHORS["signal"] · ["reputation"] (화면 1.16 × 1.12) · ["pulse"] (빛 고리 위 끝 → 아래로 2.12)
                    var pulses = new List<Transform>();
                    for (int i = 0; i < 4; i++)
                    {
                        var p = Child(root, "INT_CommRelay_Pulse_" + i);
                        if (p != null)
                            pulses.Add(p);
                    }
                    root.AddComponent<InteriorSignalBoard>().Configure(
                        new InteriorSignalBoard.Panel { Center = new Vector3(2.51f, F + 1.92f, 3.105f), Normal = Vector3.back, Up = Vector3.up, Size = new Vector2(1.16f, 1.12f) },
                        new InteriorSignalBoard.Panel { Center = new Vector3(3.105f, F + 1.92f, 2.505f), Normal = Vector3.left, Up = Vector3.up, Size = new Vector2(1.16f, 1.12f) },
                        AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ScreenPath), AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.InteriorHullDarkPath),
                        pulses.ToArray(), 2.12f);
                });
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
        /// 가운데는 ±z 문으로 가는 교차 통로. 랙의 가운데 쪽 끝면마다 큰 저장량 게이지(세로 12칸, 조각 INT_Battery_Gauge_00~11 → <see cref="InteriorGauge"/>가 실제 저장량만큼 점등, 11-10).
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

        /// <summary>
        /// 11-7 연료전지 (1칸, 발전 장치실): 가운데 연료전지 스택(판 36장·끝판·조임 막대, 위 출력 단자함·케이블), 수소 탱크 2·산소 탱크 2(머리 위 배관으로 스택에),
        /// 부산물 물 회수 탱크·펌프, 출력 계기 패널(아날로그 게이지·막대). 문 자리 = 네 옆면, 위·아래 해치는 스택을 피해 (−2.25, −2.25).
        /// </summary>
        private static void BuildFuelCell()
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
            var white = new Color(0.94f, 0.96f, 1f);
            var lights = new List<LightSpec>
            {
                LightSpec.Point(new Vector3(2.05f, F + 3.0f, 0f), 4f, 1.2f, white),
                LightSpec.Point(new Vector3(-2.05f, F + 3.0f, 0f), 4f, 1.2f, white),
                LightSpec.Point(new Vector3(0f, F + 3.0f, 2.05f), 4f, 1.2f, white),
                LightSpec.Point(new Vector3(0f, F + 3.0f, -2.05f), 4f, 1.2f, white),
                LightSpec.Point(new Vector3(0f, F + 2.0f, 0f), 3f, 0.8f, white), // 스택 위
                LightSpec.Point(new Vector3(2.3f, F + 1.4f, 2.3f), 2.2f, 0.5f, white), // 탱크 모서리
                LightSpec.Point(new Vector3(-2.3f, F + 1.4f, 2.3f), 2.2f, 0.5f, white),
            };
            // 스폰 = 남쪽 문 앞에서 스택을 바라봄
            Build("FuelCell", "MD_FuelCell", sockets, new Vector3(0.4f, F, -2.4f), -10f, lights);
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

        /// <summary>11-9 방 환경음 묶음 (템플릿 이름 → 성격). 없으면 넓은 공간.</summary>
        private static readonly Dictionary<string, InteriorAmbience> Ambiences = new Dictionary<string, InteriorAmbience>
        {
            { "Habitat", InteriorAmbience.Life }, { "Medical", InteriorAmbience.Life }, { "Recreation", InteriorAmbience.Life },
            { "Farm", InteriorAmbience.Water }, { "WaterRecycler", InteriorAmbience.Water }, { "Oxygen", InteriorAmbience.Water },
            { "Storage", InteriorAmbience.Machine }, { "MaintenanceBay", InteriorAmbience.Machine }, { "CargoTerminal", InteriorAmbience.Machine },
            { "MiningDock", InteriorAmbience.Machine }, { "ResearchLab", InteriorAmbience.Machine },
            { "Refinery", InteriorAmbience.Heat }, { "FusionReactor", InteriorAmbience.Heat }, { "FuelCell", InteriorAmbience.Heat },
            { "Battery", InteriorAmbience.Electric }, { "Solar", InteriorAmbience.Electric }, { "Shield", InteriorAmbience.Electric },
            { "Turret", InteriorAmbience.Electric }, { "ArmorBulkhead", InteriorAmbience.Electric }, { "DamageControl", InteriorAmbience.Electric },
            { "Core", InteriorAmbience.Hall }, { "RotatingRing", InteriorAmbience.Hall },
            // 12-5 새 기본 모듈
            { "School", InteriorAmbience.Life }, { "Quarantine", InteriorAmbience.Life }, { "Commerce", InteriorAmbience.Life }, { "Admin", InteriorAmbience.Life },
            { "Shelter", InteriorAmbience.Machine },   // 공기 정화기 소리
            { "Passenger", InteriorAmbience.Life },
            { "Radiator", InteriorAmbience.Machine },   // 냉각 펌프
            { "CommRelay", InteriorAmbience.Electric }, // 송수신기 · 증폭기
            { "WaterEx", InteriorAmbience.Water }, { "Protein", InteriorAmbience.Machine },
            { "Sensor", InteriorAmbience.Electric }, { "DataCenter", InteriorAmbience.Electric },
            { "Frame", InteriorAmbience.Hall }, { "Hub", InteriorAmbience.Hall },
            // 진화 모듈
            { "EvoResearch", InteriorAmbience.Machine }, { "EvoTower", InteriorAmbience.Life }, { "EvoEco", InteriorAmbience.Water },
            { "EvoPowerBank", InteriorAmbience.Electric }, { "EvoWelfare", InteriorAmbience.Life }, { "EvoCulture", InteriorAmbience.Life },
            { "EvoSolar", InteriorAmbience.Electric }, { "EvoFusion", InteriorAmbience.Heat }, { "EvoHydrogen", InteriorAmbience.Water },
            { "EvoGravity", InteriorAmbience.Life }, { "EvoFarm", InteriorAmbience.Water }, { "EvoMineral", InteriorAmbience.Heat },
            { "EvoShield", InteriorAmbience.Electric }, { "EvoDefense", InteriorAmbience.Electric }, { "EvoArmor", InteriorAmbience.Electric }, { "EvoWorks", InteriorAmbience.Machine }, { "EvoEmergency", InteriorAmbience.Electric },
            { "EvoReclaim", InteriorAmbience.Heat },   // 가운데 재생로 (쇳물)
            { "EvoLogistics", InteriorAmbience.Machine },
            { "EvoStorage", InteriorAmbience.Machine },
            { "EvoShipyard", InteriorAmbience.Machine },
            { "EvoAutomation", InteriorAmbience.Electric },   // 학습 코어 · 기록 서버
            { "EvoSecurity", InteriorAmbience.Life },         // 민원 홀 (행정 관제소와 같음)
            { "EvoGridHub", InteriorAmbience.Electric },      // 테슬라 탑 · 번개
            { "EvoFusionPlant", InteriorAmbience.Heat },      // 융합로 · 열 관
            { "EvoArcology", InteriorAmbience.Life },         // 섬 도시
            { "EvoOrbitalCity", InteriorAmbience.Life },      // 정원 섬 도시 (3단계)
            { "EvoBiosphere", InteriorAmbience.Life },        // 물의 순환 고리 (3단계)
            { "EvoStellarGrid", InteriorAmbience.Life },      // 꿀벌 정원 (3단계)
            { "EvoGrandTorus", InteriorAmbience.Life },       // 바퀴 마을
            { "EvoCivicHall", InteriorAmbience.Life },        // 회관 · 돌봄 · 교실
            { "EvoSecurityHQ", InteriorAmbience.Life },       // 파출소 광장 · 민원 창구
            { "EvoBiodome", InteriorAmbience.Water },         // 호수 · 폭포 · 비
            { "EvoFoodFactory", InteriorAmbience.Machine },   // 가공 라인 · 바퀴
            { "EvoRefineryCombine", InteriorAmbience.Machine },   // 용광로 · 크레인
            { "EvoShieldDome", InteriorAmbience.Electric },       // 렌즈 · 실드 장치
            { "EvoFortress", InteriorAmbience.Life },             // 연병장 · 불꽃 구경
            { "EvoRadiationWall", InteriorAmbience.Life },        // 병풍 정원 · 찻집
            { "EvoDroneDepot", InteriorAmbience.Machine },        // 수리 칸 · 드론
            { "EvoCargoTransfer", InteriorAmbience.Water },       // 은하수 후룸 · 항구 못
            { "EvoStorageComplex", InteriorAmbience.Machine },    // 자동 선반 · 회전목마
            { "EvoResearchInstitute", InteriorAmbience.Machine }, // 서버 랙 · 냉각탑
        };

        /// <summary>
        /// 하늘 껍질 문 자리에 진짜 구멍 (2026-11-06 코드 검토): Blender <c>evo2_sky</c>는 꽉 찬 상자에서 문 상자를 빼서 구멍 대신 2cm 홈만 남겼음 →
        /// 문이 열려도 하늘 그림(바깥 화면)이 문 안을 덮어 통로 대신 바깥이 보였음 (2단계 개방형 방 전부). 문 자리마다 그 자리(폭 ±0.81 · 바닥 −0.2 ~ +2.5 · 문 깊이 −0.1 ~ 칸 경계) 삼각형을 지운 메시를 따로 저장.
        /// 닫힌 자리는 지금처럼 빌더가 키트 하늘 조각(칸 가운데 + 3.98)으로 메움. 구멍 크기 = Blender 하늘 구멍 · 키트 하늘 조각과 같음.
        /// </summary>
        private static void PunchSkyDoors(GameObject root, string name, List<InteriorSocket> sockets, Material skyMaterial)
        {
            const float HalfWidth = 0.81f, Bottom = -0.21f, Top = 2.51f, Front = 0.1f, Edge = 4.05f;
            MeshFilter sky = null;
            foreach (Transform part in root.transform)
            {
                var r = part.GetComponent<MeshRenderer>();
                if (part.name.EndsWith("_Sky") && r != null && r.sharedMaterial == skyMaterial && skyMaterial != null)
                    sky = part.GetComponent<MeshFilter>();
            }
            if (sky == null || sky.sharedMesh == null)
                return;
            var src = sky.sharedMesh;
            var toRoot = sky.transform.localToWorldMatrix;   // 루트 = 원점 (Build가 새로 만든 물체)
            var verts = src.vertices;
            var world = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
                world[i] = toRoot.MultiplyPoint3x4(verts[i]);
            var mesh = Object.Instantiate(src);
            mesh.name = "SM_Interior_" + name + "_SkyDoors"; // 파일 이름과 같게 (다르면 다시 가져올 때마다 이름이 바뀌어 빌드마다 변경이 생김)
            int removed = 0, emptySockets = 0;
            var boxes = new List<(Vector3 center, Vector3 dir, Vector3 right, float floor, float depth)>();
            foreach (var s in sockets)
            {
                if (s.Direction.y != 0)
                    continue;
                var dir = (Vector3)s.Direction;
                boxes.Add(((Vector3)s.Cell * InteriorGeometry.CellSize, dir, new Vector3(dir.z, 0f, -dir.x), InteriorGeometry.FloorY(s.Cell), s.Depth));
            }
            var hits = new int[boxes.Count];
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var tris = mesh.GetTriangles(sub);
                var keep = new List<int>(tris.Length);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var c = (world[tris[i]] + world[tris[i + 1]] + world[tris[i + 2]]) / 3f;
                    int inBox = -1;
                    for (int b = 0; b < boxes.Count && inBox < 0; b++)
                    {
                        var d = c - boxes[b].center;
                        float along = Vector3.Dot(d, boxes[b].dir), side = Vector3.Dot(d, boxes[b].right), up = c.y - boxes[b].floor;
                        if (along >= boxes[b].depth - Front && along <= Edge && Mathf.Abs(side) <= HalfWidth && up >= Bottom && up <= Top)
                            inBox = b;
                    }
                    if (inBox >= 0)
                    {
                        removed++;
                        hits[inBox]++;
                        continue;
                    }
                    keep.Add(tris[i]);
                    keep.Add(tris[i + 1]);
                    keep.Add(tris[i + 2]);
                }
                mesh.SetTriangles(keep, sub);
            }
            foreach (var h in hits)
            {
                if (h == 0)
                    emptySockets++;
            }
            System.IO.Directory.CreateDirectory(SkyDoorsFolder);
            string path = SkyDoorsFolder + "SM_Interior_" + name + "_SkyDoors.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
            }
            else
                AssetDatabase.CreateAsset(mesh, path);
            sky.sharedMesh = mesh;
            Debug.Log($"[InteriorTemplateBuilder] {name}: 하늘 껍질 문 구멍 {boxes.Count}곳, 지운 삼각형 {removed}" + (emptySockets > 0 ? $", 하늘이 없던 문 자리 {emptySockets}" : ""));
        }

        private const string SkyDoorsFolder = "Assets/_Project/Art/Models/Interior/Generated/";

        private static void Build(string name, string moduleName, List<InteriorSocket> sockets, Vector3 spawn, float spawnYaw,
            List<LightSpec> lights, List<OrbitSpec> orbits = null, Dictionary<string, Vector3> sunTilts = null, bool showOwnExterior = false,
            System.Action<GameObject> configure = null)
        {
            string modelPath = ModelFolder + "SM_Interior_" + name + ".fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError($"[InteriorTemplateBuilder] 모델 없음: {modelPath}");
                return;
            }
            var module = AssetDatabase.LoadAssetAtPath<ModuleData>(ModuleFolder + moduleName + ".asset");
            string moduleKey = System.IO.Path.GetFileName(moduleName); // 진화 모듈은 "Evolved/MD_Evo_*" → 강조 재질 이름은 파일 이름만
            var accent = AssetDatabase.LoadAssetAtPath<Material>(AccentFolder + "M_Accent_" + moduleKey.Replace("MD_", "") + ".mat");
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
                { "Culture", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.CulturePath) }, // 12-5 단백질 배양액
                { "Window", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.WindowPath) }, // 11-8 바깥 창
                // 12-5 소품 색 (발광 없음) + Paint = 이 모듈 강조색 도장 (발광 없음: 큰 소품 · 배관 · 의자 — Accent는 띠 · 표시등만)
                { "Paint", PaintFor(accent, moduleKey) },
                // 진화 모듈 내부 1단계 키트 (2026-10-29, Blender evo_kit)
                { "EvoWhite", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.EvoWhitePath) },
                { "EvoFloor", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.EvoFloorPath) },
                { "EvoDark", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.EvoDarkPath) },
                // 진화 내부 2단계 키트 (2026-11-02, Blender evo2_kit)
                { "Evo2Deck", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2DeckPath) },
                { "Evo2Wall", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2WallPath) },
                { "Evo2Trim", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2TrimPath) },
                { "Evo2Gold", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2GoldPath) },
                { "Evo2Path", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2PathPath) },
                { "Evo2Bed", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2BedPath) },
                { "Evo2Ice", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2IcePath) },
                { "Evo2Glass", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2GlassPath) },
                { "Evo2EggGlass", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2EggGlassPath) },
                { "Evo2ClearGlass", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2ClearGlassPath) },
                { "Evo2Window", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2WindowPath) },
                { "Evo2Curtain", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2CurtainPath) },
                { "Evo2StarDome", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2StarDomePath) },
                { "Evo2Sky", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2SkyPath) },
                { "Evo2Mirror", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2MirrorPath) },
                { "Evo2Violet", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2VioletPath) },
                { "Evo2Heat", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2HeatPath) },
                { "Evo2WaterGlow", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2WaterGlowPath) },
                { "Evo2Lawn", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2LawnPath) },
                { "Evo2Soil", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2SoilPath) },
                { "Evo2Rock", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2RockPath) },
                { "Evo2Bar", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2BarPath) },
                { "Evo2Sun", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2SunPath) },
                { "Evo2Membrane", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2MembranePath) },
                { "Evo2StarSea", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2StarSeaPath) },
                { "Evo2BeaconCore", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2BeaconCorePath) },
                { "Evo2River", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2RiverPath) },
                { "Evo2Star", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2StarPath) },
                { "Evo2Mount", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2MountPath) },
                { "Evo2Sand", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2SandPath) },
                { "Evo2Moon", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2MoonPath) },
                { "Evo2Lemon", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2LemonPath) },
                { "PureWater", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2PondPath) },
                { "Falls", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2FallsPath) },
                { "Cloud", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.CloudPath) },
                { "Paper", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.PaperPath) },
                { "PropBlue", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.PropBluePath) },
                { "PropGreen", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.PropGreenPath) },
                { "PropOrange", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.PropOrangePath) },
                { "PropPurple", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.PropPurplePath) },
                { "PropYellow", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.PropYellowPath) },
                { "Fish", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.FishPath) },
                { "Ice", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.IcePath) },
                { "Aqua", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.AquaPath) },
                { "Concrete", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.ConcretePath) },   // 12-5 방사선 차폐실
                { "WaterClear", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.WaterClearPath) }, // 투명한 물 (통 속 물 덩어리)
                // 진화 내부 3단계 (2026-11-10, 궤도 도시부터 — 진주 · 금 + 2단계 정원 재질 · 바깥 수정 · 무지개)
                { "Evo3Pearl", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3PearlPath) },
                { "Evo3Gold", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3GoldPath) },
                { "Evo3Blossom", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3BlossomPath) },
                { "Evo3Lawn", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2LawnPath) },
                { "Evo3Path", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2PathPath) },
                { "Evo3Rock", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2RockPath) },
                { "Evo3Soil", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2SoilPath) },
                { "Evo3Water", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3WaterPath) },   // KWS 패키지가 없는 PC에서 보이는 수면 (KWS 물은 KwsWaterBuilder)
                { "Evo3Falls", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3FallsPath) },
                { "Evo3Roof", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3RoofPath) },
                { "Evo3Door", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3DoorPath) },
                { "Evo3Leaf", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.PlantPath) },
                { "Evo3Prism", ModuleFxMaterials.Get("Prism") },
                // 바이오스피어 (2026-11-11): 작물 · 들꽃 · 물길 (작은 물 = 바깥 물결 재질) · 비 (바깥 빗줄기)
                { "Evo3Wheat", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3WheatPath) },
                { "Evo3Lavender", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3LavenderPath) },
                { "Evo3Tulip", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3TulipPath) },
                { "Evo3Field", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3FieldPath) },
                { "Evo3Flower", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3FlowerPath) },
                { "Evo3Leaf2", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3Leaf2Path) },
                { "Evo3Bark", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3BarkPath) },
                { "Evo3Channel", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo2PondPath) },
                { "Evo3Rain", ModuleFxMaterials.Get("Rain") },
                { "Evo3Spill", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3SpillPath) },             // 단 사이 작은 폭포 · 수문 폭포 (WaterFalls 맑은 변형)
                { "Evo3FallsColumn", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3FallsColumnPath) },
                { "Evo3SoftRainbow", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.SoftRainbowPath) },
                { "Evo3SoftHalo", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.SoftHaloPath) },
                // 항성 발전망 꿀벌 정원 (2026-11-11): 꿀 언덕 밀랍 · 흰 밀랍 · 빛나는 꿀 · 등나무 꽃
                { "Evo3Wax", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3WaxPath) },
                { "Evo3Wax2", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3Wax2Path) },
                { "Evo3Honey", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3HoneyPath) },
                { "Evo3Wisteria", AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.Evo3WisteriaPath) },
            };
            for (int i = 0; i < 7; i++)
                materials["Evo3Rainbow" + i] = ModuleFxMaterials.Get("Rainbow" + i);   // 프리즘 무지개 고리 일곱 (바깥과 같은 빛 물결)

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
                // 진화 템플릿의 _Soft = 의자 방석 · 소파 (플레이 확인 2026-10-29: 거주 타워 소파를 지나감) → 충돌체. 작물 · 잎은 _Leaves (충돌체 없음)
                bool solid = partName.EndsWith("_Shell") || partName.EndsWith("_Glass") || partName.EndsWith("_Door")   // _Door = 움직이는 문 (InteriorSlidingDoor)
                    || (name.StartsWith("Evo") && partName.EndsWith("_Soft"));
                if (solid)
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
            // 11-10 칸 게이지: 조각 INT_<이름>_Gauge_NN (NN = 아래부터) → InteriorGauge가 실제 값만큼 점등
            var gaugeParts = new List<Renderer>();
            foreach (Transform t in root.transform)
            {
                if (t.name.StartsWith("INT_" + name + "_Gauge_"))
                    gaugeParts.Add(t.GetComponent<Renderer>());
            }
            if (gaugeParts.Count > 0)
            {
                gaugeParts.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                root.AddComponent<InteriorGauge>().Configure(gaugeParts.ToArray());
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

            PunchSkyDoors(root, name, sockets, materials["Evo2Sky"]);
            configure?.Invoke(root); // 12-5 실행 중 표시 컴포넌트 (허브 안내 · 레이더 · 컨베이어 · 서버 표시등)
            AddStatusScreens(root, materials["ScreenDark"]);   // 빈 화면 = 공용 상태 화면 (사용자 2026-10-30 — 기본 · 모든 진화 단계, 전용 화면 자리는 뺌)

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
            template.EditorSetAmbience(Ambiences.TryGetValue(name, out var ambience) ? ambience : InteriorAmbience.Hall);
            template.EditorSetShowOwnExterior(showOwnExterior);
            template.EditorSetSealedSockets(name == "EvoGrandTorus");   // 전부 지붕 · 유리 안 (우주복 없음): 닫힌 문 자리가 우주로 뚫리면 안 됨 (사용자 2026-11-03 "모델링에 구멍")
            EditorUtility.SetDirty(template);
            Debug.Log($"[InteriorTemplateBuilder] {name}: 문 자리 {sockets.Count}곳, 조명 {lights.Count}개");
        }
    }
}
