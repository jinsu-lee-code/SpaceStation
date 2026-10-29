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

        /// <summary>
        /// 모델마다 다른 연출 재질 {모델 key: {FBX 슬롯: ModuleFxMaterials 이름}} (사용자 2026-10-27 플레이 점검):
        /// 실드 돔 오로라 커튼 = 일렁이는 셰이더 (방사선 차폐벽 · 환적장 은하수 띠는 같은 FX_AuroraViolet이라도 그대로),
        /// 융합 발전소 · 융합 플랜트 = 흐르는 플라즈마 (기본 핵융합로는 그대로), 궤도 미러 빛줄기 = 숨기고 LightBeamFx가 빛기둥으로 그림.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, string>> FxOverrides = new Dictionary<string, Dictionary<string, string>>
        {
            { "Evo_ShieldDome", new Dictionary<string, string> { { "FX_AuroraGreen", "AuroraCurtainGreen" }, { "FX_AuroraViolet", "AuroraCurtainViolet" } } },
            { "Evo_FusionStation", new Dictionary<string, string> { { "FX_FusionPlasma", "PlasmaBlue" } } },
            { "Evo_FusionPlant", new Dictionary<string, string> { { "FX_FusionPlasma", "PlasmaViolet" } } },
            { "Evo_OrbitalMirror", new Dictionary<string, string> { { "FX_LightBeam", "Hidden" } } },
        };

        /// <summary>
        /// 진화 3 · 4단계 도는 부품 "SpinDish_*" 속도 (초당 °, 모듈 Y축 · 기본 8): 등대 빛줄기는 쓸고 지나가게 빠르게, 천문대 돔은 천천히,
        /// 해마가 끄는 가리비 배는 회전목마와 반대로 (사용자 2026-10-27 · 28 시안 설명).
        /// </summary>
        private static readonly Dictionary<string, float> DishSpeeds = new Dictionary<string, float>
        {
            { "SpinDish_OrbitalDefenseBeam", 30f },
            { "SpinDish_OrbitalDefenseGulls", 12f },
            { "SpinDish_BiosphereClouds", 5f },
            { "SpinDish_RepairCommandFireflies", 10f },
            { "SpinDish_OrbitalPortCarousel", 10f },
            { "SpinDish_OrbitalPortBoats", -6f },
            { "SpinDish_AICoreThoughts", 6f },
            { "SpinDish_DeepSpaceCommDome", 3f },
        };

        [MenuItem("SpaceStation/Art/Rebuild Greybox Modules")]
        public static void RebuildAll() => RebuildModules(null);

        /// <summary>모듈 하나만 다시 조립 (예: "Storage").</summary>
        public static void RebuildModule(string key) => RebuildModules(key);

        private static (Material hull, Material hullDark, Material window, Material solar) SharedMaterials() => (
            Mat("M_Hull", new Color(0.72f, 0.72f, 0.75f), 0.6f, 0.55f),
            Mat("M_HullDark", new Color(0.22f, 0.23f, 0.26f), 0.7f, 0.45f),
            Mat("M_Window", new Color(0.08f, 0.07f, 0.06f), 0.1f, 0.9f, new Color(1f, 0.82f, 0.55f) * 2.2f),
            Mat("M_SolarPanel", new Color(0.05f, 0.1f, 0.3f), 0.35f, 0.88f));

        /// <summary>
        /// 진화 모듈 외형 (2026-10-22, `RecipeSetup`이 부름): Art/Models/Modules/SM_{key}.fbx가 있으면 root 아래 "Model"로 넣고
        /// 재질 슬롯 · 움직이는 부품 · 그림자를 기본 모듈과 같게 설정. 칸 판정 · 선택 콜라이더는 부르는 쪽이 칸마다 만든다
        /// (ㄱ자 · 고리 모양이라 `Rebuild`의 상자 하나 방식을 쓰지 않음). 모델 파일이 없으면 false.
        /// </summary>
        public static bool AttachModel(Transform root, string key, Material accent)
        {
            var (hull, hullDark, window, solar) = SharedMaterials();
            var model = ImportedModel(key, hull, hullDark, window, solar, accent);
            if (model == null)
                return false;
            PlaceModel(root, model, null);
            ApplyShadows(root.gameObject);
            return true;
        }

        private static void RebuildModules(string only)
        {
            var (hull, hullDark, window, solar) = SharedMaterials();

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
            ModulePickSetup.ApplyAll(); // 12-0: 다시 조립한 프리팹에 선택용 콜라이더
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
            // 모델마다 다른 연출 재질 (같은 슬롯 이름이라도) — 이미 다른 재질로 리맵돼 있어도 바꿈
            var forced = new HashSet<string>();
            if (FxOverrides.TryGetValue(key, out var overrides))
            {
                foreach (var o in overrides)
                {
                    var fx = ModuleFxMaterials.Get(o.Value);
                    if (fx == null)
                        continue;
                    slots[o.Key] = fx;
                    forced.Add(o.Key);
                }
            }

            var existing = importer.GetExternalObjectMap();
            bool changed = false;
            foreach (var slot in slots)
            {
                if (!used.Contains(slot.Key) && !used.Contains(slot.Value.name) && !forced.Contains(slot.Key))
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
            // 궤도 고리(SpinOrbit*)는 회전축을 메시에서 계산하므로 읽기 허용 — 꺼져 있으면 에디터에서도 점을 못 읽음 (OrbitAxis)
            bool orbit = false;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                orbit |= t.name.StartsWith("SpinOrbit") || t.name.StartsWith("SpinClock");
            if (orbit && !importer.isReadable)
            {
                importer.isReadable = true;
                changed = true;
            }
            if (changed)
                importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static void WireShieldFx(Transform emitter, Transform ringA, Transform ringB, Transform core, bool lighthouse = false)
        {
            var fx = emitter.gameObject.AddComponent<SpaceStation.Building.ShieldEmitterFx>();
            var so = new SerializedObject(fx);
            so.FindProperty("_ringA").objectReferenceValue = ringA;
            so.FindProperty("_ringB").objectReferenceValue = ringB;
            so.FindProperty("_core").objectReferenceValue = core;
            so.FindProperty("_orbMaterial").objectReferenceValue = ModuleFxMaterials.Get("ShieldOrb");
            so.FindProperty("_trailMaterial").objectReferenceValue = ModuleFxMaterials.Get("ShieldTrail");
            // 게임 카메라 거리(약 12m)에서도 읽히도록 크고 빠르게 — 진화 실드 돔(등대)은 렌즈 드럼 + 위아래 프리즘이 한 덩어리로
            // 천천히 돌아 등대 빛 두 줄기(렌즈 드럼의 Fx 자식)가 하늘을 쓸고 지나감 (사용자 2026-10-26)
            so.FindProperty("_ringASpeed").floatValue = lighthouse ? 40f : 120f;
            so.FindProperty("_ringBSpeed").floatValue = lighthouse ? 40f : -80f;
            so.FindProperty("_orbCount").intValue = 4;
            so.FindProperty("_orbRadius").floatValue = 0.125f;
            so.FindProperty("_orbSize").floatValue = 0.05f;
            so.FindProperty("_orbSpeed").floatValue = 260f;
            so.FindProperty("_trailTime").floatValue = 0.45f;
            so.ApplyModifiedPropertiesWithoutUndo();
            if (!lighthouse)
                return;
            // 실드 돔: 운석을 막을 때 구면 파문 대신 오로라 커튼 장벽 (MeteorFx가 이 컴포넌트를 찾음)
            var aurora = emitter.gameObject.AddComponent<SpaceStation.Building.AuroraBarrierFx>();
            var aso = new SerializedObject(aurora);
            aso.FindProperty("_material").objectReferenceValue = ModuleFxMaterials.Get("AuroraBarrier");
            aso.ApplyModifiedPropertiesWithoutUndo();
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
                    PlaceModel(root.transform, model, data);
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

                ApplyShadows(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>연출 재질(빛나는 코어·반투명 외피)은 그림자 없음.</summary>
        private static void ApplyShadows(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                bool fx = r.sharedMaterial != null && AssetDatabase.GetAssetPath(r.sharedMaterial).Contains("/Materials/FX/");
                r.shadowCastingMode = fx ? ShadowCastingMode.Off : ShadowCastingMode.On;
                r.receiveShadows = !fx;
            }
        }

        /// <summary>제작 모델을 "Model" 자식으로 넣고 이름 규칙에 따라 움직이는 부품 컴포넌트를 붙인다 (data = 보조 발전 램프용, 없으면 null).</summary>
        private static void PlaceModel(Transform root, GameObject model, ModuleData data)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
            instance.name = "Model";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = model.transform.localRotation; // 임포트 축 보정은 유지
            instance.transform.localScale = model.transform.localScale;
            // 진화 모델: 움직이는 부품 아래 "Fx_*" 자식(유리 · 빛 · 반짝임)은 부품과 같은 원점 · 축으로 구움. Blender FBX 내보내기의
            //   축 보정 굽기(bake_space_transform)가 손자 오브젝트에 축 보정을 한 번 더 넣어 X 90° + 위치가 밀려 들어옴
            //   (식량 공장 드럼 유리 커버가 누워 보임, 2026-10-26) → 부품 기준 제자리로
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                if (t.parent == instance.transform || t == instance.transform || !t.name.StartsWith("Fx_"))
                    continue;
                t.localPosition = Vector3.zero;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
            }
            // 태양광: 모델의 "Panel" 부품이 태양을 향해 기운다 (MODELING_SPEC 1-5)
            var panel = FindChild(instance.transform, "Panel");
            if (panel != null)
                panel.gameObject.AddComponent<SpaceStation.Building.SunFacingPanel>();
            // 진화 태양광 어레이: "SunPanel*" 넓은 패널판도 해를 따라 기움 (Blender 이름 "Panel"은 기본 태양광이 씀, 피벗 = 기둥 위 관절)
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("SunPanel"))
                    t.gameObject.AddComponent<SpaceStation.Building.SunFacingPanel>();
            // 실드: Emitter 회전 띠 + 궤도 빛점 연출
            var ringA = FindChild(instance.transform, "EmitterRingA");
            var emitter = FindChild(instance.transform, "Emitter");
            if (ringA != null && emitter != null)
                WireShieldFx(emitter, ringA, FindChild(instance.transform, "EmitterRingB"), FindChild(instance.transform, "EmitterCore"));
            // 진화 실드 매트릭스: 발생기 여럿 "Emitter_{이름}" + "EmitterRingA_{이름}" · "EmitterRingB_{이름}" · "EmitterCore_{이름}"
            //   (Blender 오브젝트 이름은 겹칠 수 없어 뒤를 붙임 — 기본 실드의 "Emitter"와 같은 연출을 발생기마다)
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Emitter_"))
                    continue;
                string tag = t.name.Substring("Emitter".Length);
                var a = FindChild(instance.transform, "EmitterRingA" + tag);
                if (a != null)
                    WireShieldFx(t, a, FindChild(instance.transform, "EmitterRingB" + tag), FindChild(instance.transform, "EmitterCore" + tag), tag == "_ShieldDome");
            }
            // 진화 요새 포대 (별 요새): 포탑 요격 = 별자리 불꽃, 뒤 능보 박격포대에서 가끔 불꽃 (FireworkFx)
            //   포구 = Blender build_fortress 박격포대 (0, YW + 0.03, 1) · (2, YW + 0.03, 1) 위 대롱 끝 높이
            if (model.name == "SM_Evo_Fortress")
            {
                var fw = instance.AddComponent<SpaceStation.Building.FireworkFx>();
                var wso = new SerializedObject(fw);
                wso.FindProperty("_sparkMaterial").objectReferenceValue = ModuleFxMaterials.Get("FireworkSpark");
                wso.FindProperty("_lineMaterial").objectReferenceValue = ModuleFxMaterials.Get("FireworkLine");
                var mortars = wso.FindProperty("_mortars");
                mortars.arraySize = 2;
                mortars.GetArrayElementAtIndex(0).vector3Value = new Vector3(0f, 0.05f, 1f);
                mortars.GetArrayElementAtIndex(1).vector3Value = new Vector3(2f, 0.05f, 1f);
                wso.FindProperty("_showInterval").floatValue = 4.5f; // 사용자 2026-10-27 "실제 폭죽이 자주"
                wso.ApplyModifiedPropertiesWithoutUndo();
            }
            // 진화 궤도 미러: 모델의 가는 빛줄기(FX_LightBeam — 슬롯은 FxOverrides로 비움)를 빛기둥으로 다시 그림 (LightBeamFx)
            if (model.name == "SM_Evo_OrbitalMirror")
                WireLightShafts(instance);
            // 진화 대형 토러스 · 정련 콤비나트 · 드론 정비창 (사용자 2026-10-27): 모델에 구운 멈춘 입자(가져올 때 지움, EvoBakedFxStrip)를 움직이는 연출로
            //   열차 굴뚝 = Blender build_grand_torus 기관차 굴뚝 꼭대기 (열차 부품 기준 — 부품 원점 = 허브 축),
            //   배출구 = build_refinery_combine 꼭대기 열 배출구 위, 드론 쇼 = build_drone_depot 드론 무리 부품 원점 (옥상 위 가운데)
            if (model.name == "SM_Evo_GrandTorus")
            {
                var train = FindChild(instance.transform, "SpinDish_GrandTorusTrain");
                if (train != null)
                    AddSparkEmitter(train, new Vector3(2.222f, 0.048f, -0.7f), SpaceStation.Building.SparkEmitterFx.Kind.StarSmoke);
            }
            if (model.name == "SM_Evo_RefineryCombine")
                AddSparkEmitter(instance.transform, new Vector3(1f, 0.5f, 0.5f), SpaceStation.Building.SparkEmitterFx.Kind.SparkFountain);
            // 진화 치안 본부: 광장의 떠 있는 순찰차 "PatrolCar_*" (Blender에서 본체와 떼어 낸 부품, 원점 = 차 바닥 가운데)
            //   → 합성 고리와 같은 추진 불꽃 + 가끔 출동 (PatrolCarFx, 사용자 2026-10-27)
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("PatrolCar"))
                    continue;
                var car = t.gameObject.AddComponent<SpaceStation.Building.PatrolCarFx>();
                var plume = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Synthesis/M_Synth_Particle.mat");
                var cso = new SerializedObject(car);
                cso.FindProperty("_plumeMaterial").objectReferenceValue = plume != null ? plume : ModuleFxMaterials.Get("FireworkSpark");
                cso.ApplyModifiedPropertiesWithoutUndo();
            }
            // 진화 화물 환적장: 템플릿 보트 "FlumeBoat_*" 셋 (Blender cargo_flume_split, 원점 = 보트 가운데 · 앞 +x)을 복제해
            //   여섯 척이 물길 한 바퀴를 돌고 큰 낙하 바닥에서 물보라 (FlumeRideFx, 사용자 2026-10-27)
            if (model.name == "SM_Evo_CargoTransfer")
                WireFlumeRide(instance);
            // 진화 그리드 허브 "번개 정원": 탑 에너지 구 → 번개 나무 여섯 · 이웃 나무끼리 치는 번개 (LightningFx, 사용자 2026-10-27)
            //   위치 = Blender build_grid_hub: 에너지 구 (1, YC1 + 0.17, 0.5) 반지름 0.085, 나무 = 타원 둘레 60°마다 (반지름 0.9 · 0.78),
            //   높이 0.5 / 0.58 번갈아 + 꼭대기 불꽃 구 (+0.045, 반지름 0.034)
            if (model.name == "SM_Evo_GridHub")
            {
                var lf = instance.AddComponent<SpaceStation.Building.LightningFx>();
                var lso = new SerializedObject(lf);
                lso.FindProperty("_lineMaterial").objectReferenceValue = ModuleFxMaterials.Get("FireworkLine");
                lso.FindProperty("_sparkMaterial").objectReferenceValue = ModuleFxMaterials.Get("FireworkSpark");
                lso.FindProperty("_hub").vector3Value = new Vector3(1f, 0.62f + 0.17f, 0.5f);
                var nodes = lso.FindProperty("_nodes");
                nodes.arraySize = 6;
                for (int i = 0; i < 6; i++)
                {
                    float a = Mathf.Deg2Rad * 60f * i;
                    nodes.GetArrayElementAtIndex(i).vector3Value = new Vector3(1f + 0.9f * Mathf.Cos(a), -0.26f + 0.5f + 0.08f * (i % 2) + 0.045f, 0.5f + 0.78f * Mathf.Sin(a));
                }
                lso.ApplyModifiedPropertiesWithoutUndo();
            }
            if (model.name == "SM_Evo_DroneDepot")
            {
                var show = new GameObject("DroneShow");
                show.transform.SetParent(instance.transform, false);
                show.transform.localPosition = new Vector3(1f, 1.38f, 0.5f);
                var ds = show.AddComponent<SpaceStation.Building.DroneShowFx>();
                var dso = new SerializedObject(ds);
                dso.FindProperty("_lightMaterial").objectReferenceValue = ModuleFxMaterials.Get("FireworkSpark");
                dso.ApplyModifiedPropertiesWithoutUndo();
            }
            // 8-4 회전 링: "Spin" 부품이 모듈 Y축으로 회전
            var spin = FindChild(instance.transform, "Spin");
            if (spin != null)
                spin.gameObject.AddComponent<SpaceStation.Building.ModuleSpinner>();
            // 12-5 데이터 센터: "SpinFan_*" 냉각팬 날개가 빠르게 회전 (피벗 = 팬 중심, 여러 개)
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("SpinFan"))
                    continue;
                var fan = t.gameObject.AddComponent<SpaceStation.Building.ModuleSpinner>();
                var fso2 = new SerializedObject(fan);
                fso2.FindProperty("_speed").floatValue = 300f;
                fso2.FindProperty("_acceleration").floatValue = 0.5f;
                fso2.ApplyModifiedPropertiesWithoutUndo();
            }
            // 12-5 센서 어레이 · 행정 관제소: "SpinRadar*" 레이더 판이 빠르게 회전 (피벗 = 받침 꼭대기 회전축)
            //   Blender 오브젝트 이름은 겹칠 수 없으므로 모듈마다 "SpinRadar_{모듈}"처럼 뒤를 붙여도 된다
            // 12-5 통신 중계소: "SpinDish*" 접시 안테나가 천천히 회전 (피벗 = 기둥 위 회전대 중심) — 진화 연구 단지 돔 안 망원경도 (피벗 = 돔 중심)
            // 진화 연구 단지: "SpinOrbit*" 궤도 고리 + 전자 한 덩어리가 고리 기울기 축으로 회전 → 고리는 제자리처럼, 전자가 고리를 따라 달림
            //   (피벗 = 고리 중심, 축은 메시에서 계산) — 진화 문화 센터 시계 "SpinClock*" (시계 고리 + 바늘, 초당 6°)도 같은 방식
            // 12-5 행정 관제소: "Flutter_{이름}" 깃발 사슬 맨 앞 조각에 펄럭임 (같은 부모 아래 "_1", "_2" 조각은 컴포넌트가 따라 찾음)
            // 진화 식량 공장: "SpinWheel*" 유리 드럼이 부품 로컬 z축(드럼 축)으로 천천히 회전 (피벗 = 드럼 중심) — 테가 둘인 넓은 드럼은
            //   OrbitAxis가 축을 기울게 잡으므로 메시가 아니라 부품 축을 씀
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                bool radar = t.name.StartsWith("SpinRadar");
                bool clock = t.name.StartsWith("SpinClock");
                bool orbit = t.name.StartsWith("SpinOrbit") || clock;
                bool wheel = t.name.StartsWith("SpinWheel");
                if (radar || orbit || wheel || t.name.StartsWith("SpinDish"))
                {
                    var spinner = t.gameObject.AddComponent<SpaceStation.Building.ModuleSpinner>();
                    var sso = new SerializedObject(spinner);
                    sso.FindProperty("_speed").floatValue = radar ? 40f : clock ? 6f : orbit ? 45f : wheel ? 10f : DishSpeeds.TryGetValue(t.name, out float ds) ? ds : 8f;
                    if (orbit)
                        sso.FindProperty("_axis").vector3Value = OrbitAxis(t, root);
                    else if (wheel)
                        sso.FindProperty("_axis").vector3Value = root.InverseTransformDirection(t.forward);
                    sso.ApplyModifiedPropertiesWithoutUndo();
                }
                else if (SpaceStation.Building.ModuleFlutter.IsChainRoot(t.name))
                    t.gameObject.AddComponent<SpaceStation.Building.ModuleFlutter>();
            }
            // 8-5 연료전지: 보조 발전 중에만 "Lamp" 켜짐
            var lamp = FindChild(instance.transform, "Lamp");
            if (lamp != null && data != null && data.OnDemandPower)
            {
                var fx = instance.AddComponent<SpaceStation.Building.OnDemandPowerFx>();
                var fso = new SerializedObject(fx);
                fso.FindProperty("_lamp").objectReferenceValue = lamp.gameObject;
                fso.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// 화물 환적장 후룸라이드 (FlumeRideFx): 물길 한 바퀴 = Blender build_cargo_transfer의 물길과 같은 캣멀-롬 곡선
        /// (본 물길 ctrl 0.028 간격 · 돌아오는 물길 back 0.03) + 물보라 못 이음 + 항구 부두 앞 길 (화물선 사이로, 크레인 아래를 지나 본 물길 첫 점으로).
        /// 보트 = 템플릿 셋 + 복제 셋 (짐 색이 번갈아).
        /// </summary>
        private static void WireFlumeRide(GameObject instance)
        {
            var templates = new List<Transform>();
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("FlumeBoat_"))
                    templates.Add(t);
            if (templates.Count == 0)
            {
                Debug.LogWarning("[StationArtBuilder] 화물 환적장: 템플릿 보트(FlumeBoat_*)가 없어 후룸라이드 없음");
                return;
            }
            templates.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            const float YT = -0.26f, WL = YT + 0.04f, H0 = YT + 0.56f;
            var main = CatmullRom(new[]
            {
                new Vector3(1.66f, WL, 1.24f), new Vector3(1.95f, WL, 1.24f), new Vector3(2.12f, WL, 1.24f), new Vector3(2.24f, WL, 1.12f), new Vector3(2.25f, WL, 0.6f),
                new Vector3(2.24f, WL, 0.1f), new Vector3(2.22f, WL, -0.1f), new Vector3(2.1f, WL, -0.2f), new Vector3(1.98f, WL, -0.12f), new Vector3(1.97f, WL + 0.01f, -0.02f),
                new Vector3(1.97f, WL + (H0 - WL) * 0.25f, 0.16f), new Vector3(1.97f, WL + (H0 - WL) * 0.5f, 0.33f), new Vector3(1.97f, WL + (H0 - WL) * 0.75f, 0.5f),
                new Vector3(1.97f, H0 - 0.006f, 0.64f), new Vector3(1.93f, H0, 0.76f), new Vector3(1.8f, H0, 0.84f), new Vector3(1.45f, H0 - 0.003f, 0.82f),
                new Vector3(1.18f, H0 - 0.006f, 0.75f), new Vector3(1.04f, H0 - 0.01f, 0.64f), new Vector3(1.0f, H0 - 0.014f, 0.52f), new Vector3(1.0f, H0 - 0.04f, 0.44f),
                new Vector3(1.0f, H0 - 0.17f, 0.3f), new Vector3(1.0f, H0 - 0.31f, 0.16f), new Vector3(1.0f, WL + 0.07f, 0.02f), new Vector3(1.0f, WL + 0.012f, -0.08f),
                new Vector3(1.0f, WL, -0.15f),
            }, 0.028f);
            var link = CatmullRom(new[] { new Vector3(1.0f, WL, -0.15f), new Vector3(0.93f, WL, -0.215f), new Vector3(0.86f, WL, -0.25f) }, 0.03f);
            var back = CatmullRom(new[]
            {
                new Vector3(0.86f, WL, -0.25f), new Vector3(0.6f, WL, -0.28f), new Vector3(0.25f, WL, -0.25f), new Vector3(-0.08f, WL, -0.21f), new Vector3(-0.23f, WL, -0.04f),
                new Vector3(-0.25f, WL, 0.4f), new Vector3(-0.23f, WL, 0.84f), new Vector3(-0.14f, WL, 1.03f), new Vector3(0.0f, WL, 1.1f),
            }, 0.03f);
            var harbor = CatmullRom(new[]
            {
                new Vector3(0.0f, WL, 1.1f), new Vector3(0.2f, WL, 1.06f), new Vector3(0.8f, WL, 1.055f), new Vector3(1.4f, WL, 1.06f), new Vector3(1.58f, WL, 1.09f),
                new Vector3(1.64f, WL, 1.17f), new Vector3(1.66f, WL, 1.24f),
            }, 0.03f);
            var path = new List<Vector3>(main);
            path.AddRange(link.GetRange(1, link.Count - 2));
            path.AddRange(back);
            path.AddRange(harbor.GetRange(1, harbor.Count - 2));

            var boats = new List<Transform>(templates);
            foreach (var t in templates)
            {
                var clone = Object.Instantiate(t.gameObject, t.parent);
                clone.name = t.name + "_b";
                boats.Add(clone.transform);
            }
            var fx = instance.AddComponent<SpaceStation.Building.FlumeRideFx>();
            var so = new SerializedObject(fx);
            var pb = so.FindProperty("_boats");
            pb.arraySize = boats.Count;
            for (int i = 0; i < boats.Count; i++)
                pb.GetArrayElementAtIndex(i).objectReferenceValue = boats[i];
            var pp = so.FindProperty("_path");
            pp.arraySize = path.Count;
            for (int i = 0; i < path.Count; i++)
                pp.GetArrayElementAtIndex(i).vector3Value = path[i];
            so.FindProperty("_sparkMaterial").objectReferenceValue = ModuleFxMaterials.Get("FireworkSpark");
            so.FindProperty("_rippleMaterial").objectReferenceValue = ModuleFxMaterials.Get("Ripple");
            so.ApplyModifiedPropertiesWithoutUndo();
            fx.Apply(0f); // 처음 자리 (고스트 · 정지 화면에서도 물길 위에 고르게)
        }

        /// <summary>캣멀-롬 곡선 (조절점을 지나는 매끈한 길, 간격 step 안팎) — Blender evo_builder `_cr_path`와 같은 계산.</summary>
        private static List<Vector3> CatmullRom(Vector3[] ctrl, float step)
        {
            var p = new List<Vector3> { ctrl[0] + (ctrl[0] - ctrl[1]) };
            p.AddRange(ctrl);
            p.Add(ctrl[ctrl.Length - 1] + (ctrl[ctrl.Length - 1] - ctrl[ctrl.Length - 2]));
            var result = new List<Vector3>();
            for (int i = 1; i < p.Count - 2; i++)
            {
                Vector3 p0 = p[i - 1], p1 = p[i], p2 = p[i + 1], p3 = p[i + 2];
                int n = Mathf.Max(2, (int)((p2 - p1).magnitude / step));
                for (int j = 0; j < n; j++)
                {
                    float t = j / (float)n, t2 = t * t, t3 = t2 * t;
                    result.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            result.Add(p[p.Count - 2]);
            return result;
        }

        /// <summary>parent 아래 at(parent 기준)에 반짝이 입자 방출구 (SparkEmitterFx — 위로 뿜음).</summary>
        private static void AddSparkEmitter(Transform parent, Vector3 at, SpaceStation.Building.SparkEmitterFx.Kind kind)
        {
            var go = new GameObject(kind + "Emitter");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var fx = go.AddComponent<SpaceStation.Building.SparkEmitterFx>();
            var so = new SerializedObject(fx);
            so.FindProperty("_kind").enumValueIndex = (int)kind;
            so.FindProperty("_sparkMaterial").objectReferenceValue = ModuleFxMaterials.Get("FireworkSpark");
            so.FindProperty("_trailMaterial").objectReferenceValue = ModuleFxMaterials.Get("FireworkLine");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 궤도 미러 빛기둥 (사용자 2026-10-27 "선처럼 보임 → 진짜 빛줄기"): 모델 빛줄기 메시(가는 원기둥 여럿)를 이어진 덩어리로 나눠
        /// 덩어리마다 양끝을 찾고, 끝이 몰리는 쪽(집광 수신기)을 "to"로 LightBeamFx에 넘긴다. 위치는 모델 기준.
        /// </summary>
        private static void WireLightShafts(GameObject instance)
        {
            var hidden = ModuleFxMaterials.Get("Hidden");
            var from = new List<Vector3>();
            var to = new List<Vector3>();
            foreach (var r in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int s = 0; s < mats.Length; s++)
                {
                    if (mats[s] == null || (mats[s] != hidden && !mats[s].name.Contains("LightBeam")))
                        continue;
                    var toModel = instance.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    BeamSegments(r.GetComponent<MeshFilter>().sharedMesh, s, toModel, from, to);
                }
            }
            if (from.Count == 0)
            {
                Debug.LogWarning("[StationArtBuilder] 궤도 미러: 빛줄기 메시를 못 찾아 빛기둥 없음");
                return;
            }
            var fx = instance.AddComponent<SpaceStation.Building.LightBeamFx>();
            var so = new SerializedObject(fx);
            so.FindProperty("_material").objectReferenceValue = ModuleFxMaterials.Get("LightShaft");
            var pf = so.FindProperty("_from");
            var pt = so.FindProperty("_to");
            pf.arraySize = from.Count;
            pt.arraySize = to.Count;
            for (int i = 0; i < from.Count; i++)
            {
                pf.GetArrayElementAtIndex(i).vector3Value = from[i];
                pt.GetArrayElementAtIndex(i).vector3Value = to[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>한 서브메시의 가는 원기둥들 → (거울 끝, 수신기 끝) 쌍. 수신기 = 덩어리 끝들이 가장 많이 몰린 점.</summary>
        private static void BeamSegments(Mesh mesh, int sub, Matrix4x4 toModel, List<Vector3> from, List<Vector3> to)
        {
            Vector3[] verts;
            int[] tris;
            using (var md = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var data = md[0];
                var v = new Unity.Collections.NativeArray<Vector3>(data.vertexCount, Unity.Collections.Allocator.Temp);
                data.GetVertices(v);
                verts = v.ToArray();
                v.Dispose();
                var idx = new Unity.Collections.NativeArray<int>(data.GetSubMesh(sub).indexCount, Unity.Collections.Allocator.Temp);
                data.GetIndices(idx, sub);
                tris = idx.ToArray();
                idx.Dispose();
            }
            // 같은 자리 꼭짓점(나뉜 법선)은 하나로 보고 삼각형으로 이어진 덩어리 찾기
            var key = new Dictionary<Vector3Int, int>();
            var parent = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var k = Vector3Int.RoundToInt(verts[i] * 10000f);
                parent[i] = key.TryGetValue(k, out int first) ? first : (key[k] = i);
            }
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = Find(tris[i]), b = Find(tris[i + 1]), c = Find(tris[i + 2]);
                parent[b] = a;
                parent[Find(c)] = a;
            }
            var groups = new Dictionary<int, List<Vector3>>();
            foreach (int i in tris)
            {
                int g = Find(i);
                if (!groups.TryGetValue(g, out var list))
                    groups[g] = list = new List<Vector3>();
                list.Add(toModel.MultiplyPoint3x4(verts[i]));
            }
            var ends = new List<(Vector3 a, Vector3 b)>();
            foreach (var list in groups.Values)
            {
                var c = Vector3.zero;
                foreach (var p in list)
                    c += p;
                c /= list.Count;
                Vector3 a = c, b = c;
                foreach (var p in list)
                    if ((p - c).sqrMagnitude > (a - c).sqrMagnitude)
                        a = p;
                foreach (var p in list)
                    if ((p - a).sqrMagnitude > (b - a).sqrMagnitude)
                        b = p;
                if ((a - b).magnitude > 0.1f)
                    ends.Add((a, b));
            }
            // 수신기: 끝점 가운데 0.15 안에 다른 끝이 가장 많이 모인 점
            Vector3 receiver = Vector3.zero;
            int best = -1;
            foreach (var e in ends)
            {
                foreach (var p in new[] { e.a, e.b })
                {
                    int n = 0;
                    foreach (var o in ends)
                        if ((o.a - p).magnitude < 0.15f || (o.b - p).magnitude < 0.15f)
                            n++;
                    if (n > best)
                    {
                        best = n;
                        receiver = p;
                    }
                }
            }
            foreach (var e in ends)
            {
                bool aNear = (e.a - receiver).sqrMagnitude < (e.b - receiver).sqrMagnitude;
                from.Add(aNear ? e.b : e.a);
                to.Add(aNear ? e.a : e.b);
            }
        }

        /// <summary>
        /// 궤도 고리 부품의 회전축 (모듈 루트 기준): 원점 = 고리 중심이므로 가장 먼 점 A와 A에 가장 직각인 점 B의 외적이 고리 평면 법선.
        /// 가져온 메시는 읽기 불가일 수 있어 AcquireReadOnlyMeshData로 읽는다 (MODELING 8. 검증 절차와 같은 방법).
        /// </summary>
        private static Vector3 OrbitAxis(Transform part, Transform root)
        {
            var filter = part.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                return Vector3.up;
            using (var meshData = Mesh.AcquireReadOnlyMeshData(filter.sharedMesh))
            {
                var vertices = new Unity.Collections.NativeArray<Vector3>(meshData[0].vertexCount, Unity.Collections.Allocator.Temp);
                meshData[0].GetVertices(vertices);
                var a = Vector3.zero;
                foreach (var v in vertices)
                {
                    var w = part.TransformVector(v);
                    if (w.sqrMagnitude > a.sqrMagnitude)
                        a = w;
                }
                var n = Vector3.zero;
                foreach (var v in vertices)
                {
                    var c = Vector3.Cross(a, part.TransformVector(v));
                    if (c.sqrMagnitude > n.sqrMagnitude)
                        n = c;
                }
                vertices.Dispose();
                return n == Vector3.zero ? Vector3.up : root.InverseTransformDirection(n.normalized);
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
