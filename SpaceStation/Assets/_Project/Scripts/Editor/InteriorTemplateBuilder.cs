using System.Collections.Generic;
using SpaceStation.Data;
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
            InteriorSetup.CreateMaterials(); // 유리 재질 등이 먼저 있어야 함
            BuildCore();
            BuildHabitat();
            BuildFarm();
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

        private static void Build(string name, string moduleName, List<InteriorSocket> sockets, Vector3 spawn, float spawnYaw,
            List<LightSpec> lights)
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
            };

            var root = new GameObject("PF_Interior_" + name);
            foreach (Transform part in model.transform)
            {
                var filter = part.GetComponent<MeshFilter>();
                if (filter == null)
                    continue;
                var go = new GameObject(part.name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = part.localPosition;
                go.transform.localRotation = part.localRotation;
                go.transform.localScale = part.localScale;
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
                        Debug.LogWarning($"[InteriorTemplateBuilder] {part.name}: 재질 슬롯 '{slot}' → Hull");
                        mats[i] = materials["Hull"];
                    }
                }
                r.sharedMaterials = mats;
                if (part.name.EndsWith("_Shell") || part.name.EndsWith("_Glass"))
                    go.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
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
