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
            Build("EvoFusion", "Evolved/MD_Evo_FusionStation", sockets, new Vector3(-1.8f, F, 0f), 90f, lights, orbits);
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
        };

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
            EditorUtility.SetDirty(template);
            Debug.Log($"[InteriorTemplateBuilder] {name}: 문 자리 {sockets.Count}곳, 조명 {lights.Count}개");
        }
    }
}
