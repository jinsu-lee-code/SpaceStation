using System.Collections.Generic;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 5-3 공용 재질 세트 + 그레이박스 모듈 조립 (메뉴 SpaceStation/Art/Rebuild Greybox Modules).
    /// 선체(공통 금속) + 모듈색 띠(은은한 발광) + 창문 불빛. 모듈 구분은 띠·불빛 색으로.
    /// "Visual" 자식(BoxCollider = 셀 판정용)은 유지하고 크기만 셀 경계에 맞춘다. 장식 자식에는 콜라이더를 두지 않는다.
    /// 5-5: Art/Models/Modules/SM_{모듈}.fbx가 있으면 그 모델을 "Model" 자식으로 넣고 재질 슬롯을 공용 재질에 연결한다.
    /// 모델이 없는 모듈(태양광)은 그레이박스, 창고는 StorageModelBuilder 코드 조립.
    /// </summary>
    public static class StationArtBuilder
    {
        private const string MatRoot = "Assets/_Project/Art/Materials/";
        private const string StationMats = MatRoot + "Station/";
        private const string ModelRoot = "Assets/_Project/Art/Models/Modules/";
        private const float Inset = 0.92f; // 모듈 사이 이음새가 보이도록 셀보다 약간 작게

        private enum Style { Standard, Solar, Battery, Storage, Core, Topped }

        [MenuItem("SpaceStation/Art/Rebuild Greybox Modules")]
        public static void RebuildAll() => RebuildModules(null);

        /// <summary>모듈 하나만 다시 조립 (예: "Storage").</summary>
        public static void RebuildModule(string key) => RebuildModules(key);

        private static void RebuildModules(string only)
        {
            var hull = Mat("M_Hull", new Color(0.72f, 0.72f, 0.75f), 0.6f, 0.55f);
            var hullDark = Mat("M_HullDark", new Color(0.22f, 0.23f, 0.26f), 0.7f, 0.45f);
            var window = Mat("M_Window", new Color(0.08f, 0.07f, 0.06f), 0.1f, 0.9f, new Color(1f, 0.82f, 0.55f) * 2.2f);
            var solar = Mat("M_SolarPanel", new Color(0.05f, 0.1f, 0.3f), 0.35f, 0.88f);

            var log = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { "Assets/_Project/Data/Modules" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<ModuleData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.Prefab == null)
                    continue;
                string key = data.name.Replace("MD_", "");
                if (only != null && key != only)
                    continue;
                var accent = AccentFor(key);
                if (accent == null)
                {
                    log.Add($"{key}: 강조 재질 없음, 건너뜀");
                    continue;
                }
                var model = ImportedModel(key, hull, hullDark, window, solar, accent);
                Rebuild(data, key, StyleFor(key), model, hull, hullDark, window, solar, accent);
                ConnectorDepthBaker.Measure(data); // 5-6: 모델이 바뀌었으니 통로 표면 깊이도 다시 측정
                log.Add(model != null ? key + "(모델)" : key);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[StationArtBuilder] 조립: " + string.Join(", ", log));
        }

        /// <summary>
        /// 5-5 제작 모델: Art/Models/Modules/SM_{key}.fbx가 있으면 그 모델을 쓴다.
        /// FBX의 재질 슬롯(Hull/HullDark/Window/Accent/SolarPanel)을 공용 재질과 모듈 강조 재질로 연결한다.
        /// 파일이 없으면 null (태양광·창고처럼 그레이박스/코드 조립 유지).
        /// </summary>
        private static GameObject ImportedModel(string key, Material hull, Material hullDark, Material window,
            Material solar, Material accent)
        {
            string path = ModelRoot + $"SM_{key}.fbx";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                return null;
            var slots = new Dictionary<string, Material>
            {
                { "Hull", hull }, { "HullDark", hullDark }, { "Window", window }, { "Accent", accent }, { "SolarPanel", solar },
            };
            // 원본 텍스처를 쓰는 모델: 슬롯 "Tex_{이름}" → M_{이름}_Tex (ModuleTextureMaterials)
            foreach (var textured in ModuleTextureMaterials.EnsureAll(key))
                slots[textured.Key] = textured.Value;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var used = new HashSet<string>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null)
                        used.Add(m.name);
            // 연출 재질: 슬롯 "FX_{이름}" → Art/Materials/FX/M_{이름} (ModuleFxMaterials)
            foreach (var name in used)
            {
                // 처음엔 "FX_ShieldShell", 리맵 후엔 "M_ShieldShell" — 둘 다 재질 값을 갱신
                bool slot = name.StartsWith(ModuleFxMaterials.SlotPrefix);
                if (!slot && !name.StartsWith("M_"))
                    continue;
                var fx = ModuleFxMaterials.Get(name.Substring(slot ? ModuleFxMaterials.SlotPrefix.Length : 2));
                if (fx != null && slot)
                    slots[name] = fx;
            }

            var existing = importer.GetExternalObjectMap();
            bool changed = false;
            foreach (var slot in slots)
            {
                if (!used.Contains(slot.Key) && !used.Contains(slot.Value.name))
                    continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), slot.Key);
                if (existing.TryGetValue(id, out var current) && current == slot.Value)
                    continue;
                importer.AddRemap(id, slot.Value);
                changed = true;
            }
            foreach (var name in used)
            {
                // "M_"로 시작 = 이미 프로젝트 재질로 리맵된 슬롯
                if (!slots.ContainsKey(name) && !IsSharedMaterial(name, slots) && !name.StartsWith("M_"))
                    Debug.LogWarning($"[StationArtBuilder] {key}: 알 수 없는 재질 슬롯 '{name}' (공용 재질에 연결되지 않음)");
            }
            if (importer.importAnimation || importer.importCameras || importer.importLights)
            {
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                changed = true;
            }
            if (changed)
                importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static void WireShieldFx(Transform emitter, Transform ringA, Transform ringB, Transform core)
        {
            var fx = emitter.gameObject.AddComponent<SpaceStation.Building.ShieldEmitterFx>();
            var so = new SerializedObject(fx);
            so.FindProperty("_ringA").objectReferenceValue = ringA;
            so.FindProperty("_ringB").objectReferenceValue = ringB;
            so.FindProperty("_core").objectReferenceValue = core;
            so.FindProperty("_orbMaterial").objectReferenceValue = ModuleFxMaterials.Get("ShieldOrb");
            so.FindProperty("_trailMaterial").objectReferenceValue = ModuleFxMaterials.Get("ShieldTrail");
            // 게임 카메라 거리(약 12m)에서도 읽히도록 크고 빠르게
            so.FindProperty("_ringASpeed").floatValue = 120f;
            so.FindProperty("_ringBSpeed").floatValue = -80f;
            so.FindProperty("_orbCount").intValue = 4;
            so.FindProperty("_orbRadius").floatValue = 0.125f;
            so.FindProperty("_orbSize").floatValue = 0.05f;
            so.FindProperty("_orbSpeed").floatValue = 260f;
            so.FindProperty("_trailTime").floatValue = 0.45f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }

        private static bool IsSharedMaterial(string name, Dictionary<string, Material> slots)
        {
            foreach (var m in slots.Values)
                if (m.name == name)
                    return true;
            return false;
        }

        private static Style StyleFor(string key)
        {
            switch (key)
            {
                case "Solar": return Style.Solar;
                case "Battery": return Style.Battery;
                case "Storage": return Style.Storage;
                case "Core": return Style.Core;
                case "Shield":
                case "Turret": return Style.Topped;
                default: return Style.Standard;
            }
        }

        /// <summary>모듈색 강조 재질 (기존 M_Greybox_X를 M_Accent_X로 이름만 바꿔 GUID 유지) + 은은한 발광.</summary>
        private static Material AccentFor(string key)
        {
            string accentPath = MatRoot + $"M_Accent_{key}.mat";
            string oldPath = MatRoot + $"M_Greybox_{key}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(accentPath) == null && AssetDatabase.LoadAssetAtPath<Material>(oldPath) != null)
                AssetDatabase.RenameAsset(oldPath, $"M_Accent_{key}");
            var m = AssetDatabase.LoadAssetAtPath<Material>(accentPath);
            if (m == null)
                return null;
            var c = m.GetColor("_BaseColor");
            m.SetFloat("_Metallic", 0.3f);
            m.SetFloat("_Smoothness", 0.6f);
            SetEmission(m, c * 0.6f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Mat(string name, Color baseColor, float metallic, float smoothness, Color? emission = null)
        {
            if (!AssetDatabase.IsValidFolder(StationMats.TrimEnd('/')))
                AssetDatabase.CreateFolder(MatRoot.TrimEnd('/'), "Station");
            string path = StationMats + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
                SetEmission(m, emission.Value);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void SetEmission(Material m, Color emission)
        {
            // Unity 6: 문자열 EnableKeyword는 저장되지 않는 경우가 있어 LocalKeyword로 켠다
            m.SetKeyword(new LocalKeyword(m.shader, "_EMISSION"), true);
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        private static void Rebuild(ModuleData data, string key, Style style, GameObject model, Material hull, Material hullDark,
            Material window, Material solar, Material accent)
        {
            string path = AssetDatabase.GetAssetPath(data.Prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // 셀 경계 (회전 0 기준)
                var min = data.CellOffsets[0];
                var max = data.CellOffsets[0];
                foreach (var c in data.CellOffsets)
                {
                    min = Vector3Int.Min(min, c);
                    max = Vector3Int.Max(max, c);
                }
                Vector3 center = (Vector3)(min + max) * 0.5f;
                Vector3 size = (Vector3)(max - min + Vector3Int.one);

                // 장식 자식 정리 (Visual만 남김)
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    var child = root.transform.GetChild(i);
                    if (child.name != "Visual")
                        Object.DestroyImmediate(child.gameObject);
                }

                var visual = root.transform.Find("Visual");
                float bodyHeight = style == Style.Solar ? 0.3f : style == Style.Topped ? 0.55f : 1f;
                Vector3 body = style == Style.Solar
                    ? new Vector3(0.4f, bodyHeight * Inset, 0.4f) // 작은 받침 (패널이 기울어도 몸체와 겹치지 않게)
                    : new Vector3(size.x - (1f - Inset), bodyHeight * Inset, size.z - (1f - Inset));
                float bodyY = style == Style.Solar || style == Style.Topped ? -0.5f + body.y * 0.5f + 0.04f : 0f;
                visual.localPosition = new Vector3(center.x, bodyY, center.z);
                visual.localRotation = Quaternion.identity;
                visual.localScale = body;
                var box = visual.GetComponent<BoxCollider>();
                box.center = new Vector3(0f, (0f - bodyY) / body.y, 0f);
                box.size = new Vector3(size.x / body.x, size.y / body.y, size.z / body.z); // 월드 기준 셀 크기 유지
                var visualRenderer = visual.GetComponent<MeshRenderer>();
                if (model != null || style == Style.Storage)
                {
                    // 제작 모델(FBX) 또는 전용 모델(StorageModelBuilder)을 쓰므로 Visual은 셀 판정 콜라이더만 남긴다
                    visual.localPosition = center;
                    visual.localScale = Vector3.one;
                    box.center = Vector3.zero;
                    box.size = size;
                    if (visualRenderer != null)
                        Object.DestroyImmediate(visualRenderer);
                    var filter = visual.GetComponent<MeshFilter>();
                    if (filter != null)
                        Object.DestroyImmediate(filter);
                }
                else
                {
                    if (visualRenderer == null)
                    {
                        visual.gameObject.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                        visualRenderer = visual.gameObject.AddComponent<MeshRenderer>();
                    }
                    visualRenderer.sharedMaterial = style == Style.Solar || style == Style.Battery ? hullDark : hull;
                }

                if (model != null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                    instance.name = "Model";
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = model.transform.localRotation; // 임포트 축 보정은 유지
                    instance.transform.localScale = model.transform.localScale;
                    // 태양광: 모델의 "Panel" 부품이 태양을 향해 기운다 (MODELING_SPEC 1-5)
                    var panel = FindChild(instance.transform, "Panel");
                    if (panel != null)
                        panel.gameObject.AddComponent<SpaceStation.Building.SunFacingPanel>();
                    // 실드: Emitter 회전 띠 + 궤도 빛점 연출
                    var ringA = FindChild(instance.transform, "EmitterRingA");
                    var emitter = FindChild(instance.transform, "Emitter");
                    if (ringA != null && emitter != null)
                        WireShieldFx(emitter, ringA, FindChild(instance.transform, "EmitterRingB"), FindChild(instance.transform, "EmitterCore"));
                    // 8-4 회전 링: "Spin" 부품이 모듈 Y축으로 회전
                    var spin = FindChild(instance.transform, "Spin");
                    if (spin != null)
                        spin.gameObject.AddComponent<SpaceStation.Building.ModuleSpinner>();
                }
                else switch (style)
                {
                    case Style.Solar:
                    {
                        // 받침 위 기둥 + 태양을 향해 기우는 패널 (SunFacingPanel, 모듈 회전과 무관하게 월드 기준)
                        float pedestalTop = bodyY + body.y * 0.5f;
                        Part(root, "Mast", hullDark, new Vector3(center.x, pedestalTop + 0.09f, center.z), new Vector3(0.08f, 0.18f, 0.08f));
                        var mount = new GameObject("PanelMount");
                        mount.transform.SetParent(root.transform, false);
                        mount.transform.localPosition = new Vector3(center.x, pedestalTop + 0.2f, center.z);
                        mount.AddComponent<SpaceStation.Building.SunFacingPanel>();
                        Part(mount, "Panel", solar, new Vector3(0f, 0.012f, 0f), new Vector3(0.86f, 0.03f, 0.86f));
                        Part(mount, "Frame", accent, Vector3.zero, new Vector3(0.9f, 0.02f, 0.9f));
                        break;
                    }
                    case Style.Battery:
                        Band(root, accent, center, body, 0.3f, 0.1f);
                        Band(root, accent, center, body, -0.3f, 0.1f);
                        Part(root, "Terminal", hull, new Vector3(center.x, body.y * 0.5f + 0.04f, center.z), new Vector3(0.25f, 0.08f, 0.25f));
                        break;
                    case Style.Storage:
                        StorageModelBuilder.Build(root.transform, hull, hullDark, accent);
                        break;
                    case Style.Core:
                        Band(root, accent, center, body, 0.3f, 0.12f);
                        Band(root, accent, center, body, -0.3f, 0.12f);
                        Windows(root, window, min, max, body, 0f, true);
                        break;
                    case Style.Topped:
                        var top = GameObject.CreatePrimitive(key == "Shield" ? PrimitiveType.Sphere : PrimitiveType.Cylinder);
                        top.name = "Top";
                        Object.DestroyImmediate(top.GetComponent<Collider>());
                        top.transform.SetParent(root.transform, false);
                        float topBase = bodyY + body.y * 0.5f;
                        if (key == "Shield")
                        {
                            top.transform.localPosition = new Vector3(center.x, topBase + 0.12f, center.z);
                            top.transform.localScale = Vector3.one * 0.62f;
                        }
                        else
                        {
                            top.transform.localPosition = new Vector3(center.x, topBase + 0.2f, center.z);
                            top.transform.localScale = new Vector3(0.28f, 0.2f, 0.28f);
                        }
                        top.GetComponent<MeshRenderer>().sharedMaterial = accent;
                        Band(root, accent, center, body, 0f, 0.1f, bodyY);
                        break;
                    default:
                        Band(root, accent, center, body, 0.3f, 0.12f);
                        Windows(root, window, min, max, body, -0.1f, false);
                        break;
                }

                foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    // 연출 재질(빛나는 코어·반투명 외피)은 그림자 없음
                    bool fx = r.sharedMaterial != null && AssetDatabase.GetAssetPath(r.sharedMaterial).Contains("/Materials/FX/");
                    r.shadowCastingMode = fx ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    r.receiveShadows = !fx;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>선체를 두르는 띠 (선체보다 살짝 커서 겉에 보임).</summary>
        private static void Band(GameObject root, Material mat, Vector3 center, Vector3 body, float relY, float height, float baseY = 0f)
        {
            Part(root, "Band", mat, new Vector3(center.x, baseY + relY * body.y, center.z),
                new Vector3(body.x + 0.02f, height, body.z + 0.02f));
        }

        /// <summary>셀마다 앞뒤(±Z) 면에 창문 한 쌍. allSides면 좌우(±X)에도.</summary>
        private static void Windows(GameObject root, Material mat, Vector3Int min, Vector3Int max, Vector3 body, float y, bool allSides)
        {
            float zFace = body.z * 0.5f + 0.006f;
            for (int x = min.x; x <= max.x; x++)
            {
                for (int z = min.z; z <= max.z; z++)
                {
                    Part(root, "Window", mat, new Vector3(x, y, z + zFace), new Vector3(0.42f, 0.14f, 0.01f));
                    Part(root, "Window", mat, new Vector3(x, y, z - zFace), new Vector3(0.42f, 0.14f, 0.01f));
                }
            }
            if (!allSides)
                return;
            float xFace = body.x * 0.5f + 0.006f;
            Part(root, "Window", mat, new Vector3((min.x + max.x) * 0.5f + xFace, y, (min.z + max.z) * 0.5f), new Vector3(0.01f, 0.14f, 0.42f));
            Part(root, "Window", mat, new Vector3((min.x + max.x) * 0.5f - xFace, y, (min.z + max.z) * 0.5f), new Vector3(0.01f, 0.14f, 0.42f));
        }

        private static void Part(GameObject root, string name, Material mat, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // 셀 판정은 Visual 콜라이더만
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
