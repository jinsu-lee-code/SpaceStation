using System.Collections.Generic;
using System.Linq;
using SpaceStation.Data;
using SpaceStation.Interior;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 11-11b 주민 모델 연결 (메뉴 SpaceStation/Interior/Wire Resident Models).
    /// Blender 텍스트 `resident2_export`가 내보낸 FBX 3개(남 · 여 = 리그 + 몸 구역 + 옷, 머리 40조합)와 얼굴 · 옷 · 머리 텍스처를
    /// <see cref="ResidentModelSet"/> 에셋에 묶고 InteriorMode에 연결한다. 옷 표(가리는 몸 구역 · 성별 · 함께 입는 옷)는 Blender `resident2_cloth`의 HIDES · POOLS · WITH와 같다.
    /// FBX 가져오기 설정: 남 · 여 = Humanoid(사람형 본 이름 자동 매핑, 자세 코드가 HumanBodyBones로 본을 찾음), 재질 가져오지 않음(공유 Lit + MaterialPropertyBlock).
    /// </summary>
    public static class ResidentFigureSetup
    {
        private const string ModelDir = "Assets/_Project/Art/Models/Resident/";
        private const string TexDir = "Assets/_Project/Art/Textures/Resident/";
        private const string SetPath = "Assets/_Project/Data/Residents/ResidentModelSet.asset";

        private static GarmentDef G(string name, GarmentSlot slot, string[] hides, bool man = true, string with = "", string sit = "")
            => new GarmentDef { Name = name, Slot = slot, Hides = hides, Man = man, Woman = true, With = with, SitShape = sit };

        private static GarmentDef[] Garments() => new[]
        {
            G("tshirt", GarmentSlot.Top, new[] { "chest" }),
            G("shirt", GarmentSlot.Top, new[] { "chest", "arm" }),
            G("hoodie", GarmentSlot.Top, new[] { "chest", "arm" }),
            G("pants", GarmentSlot.Bottom, new[] { "hips", "thigh", "shin" }),
            G("shorts", GarmentSlot.Bottom, new[] { "hips" }),
            G("skirt", GarmentSlot.Bottom, new string[0], man: false, with: "panties", sit: "Sit"),
            G("skirt_h", GarmentSlot.Bottom, new[] { "hips" }, man: false, with: "panties"),
            G("panties", GarmentSlot.Under, new string[0], man: false),
            G("jumpsuit", GarmentSlot.Suit, new[] { "chest", "arm", "hips", "thigh", "shin" }),
            G("sneakers", GarmentSlot.Shoes, new[] { "foot" }),
            G("boots", GarmentSlot.Shoes, new[] { "foot", "heel", "ankle", "shin" }),
        };

        [MenuItem("SpaceStation/Interior/Wire Resident Models")]
        public static void Wire()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[ResidentFigureSetup] 플레이 모드에서는 실행하지 않음");
                return;
            }
            ConfigureModel(ModelDir + "SM_ResidentMan.fbx", true);
            ConfigureModel(ModelDir + "SM_ResidentWoman.fbx", true);
            ConfigureModel(ModelDir + "SM_ResidentHair.fbx", false);
            var face = Textures(TexDir + "Face");
            var cloth = Textures(TexDir + "Cloth");
            var hairTex = Textures(TexDir + "Hair").FirstOrDefault(t => t.name == "hair_strands");

            var set = AssetDatabase.LoadAssetAtPath<ResidentModelSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<ResidentModelSet>();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SetPath));
                AssetDatabase.CreateAsset(set, SetPath);
            }
            set.EditorSet(AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + "SM_ResidentMan.fbx"),
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + "SM_ResidentWoman.fbx"),
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + "SM_ResidentHair.fbx"),
                hairTex, face, cloth, Garments());
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();

            var mode = Object.FindFirstObjectByType<InteriorMode>(FindObjectsInactive.Include);
            if (mode == null)
            {
                Debug.LogError("[ResidentFigureSetup] InteriorMode 없음 (Main 씬을 열고 다시)");
                return;
            }
            var so = new SerializedObject(mode);
            so.FindProperty("_residentSet").objectReferenceValue = set;
            so.FindProperty("_residentMaterial").objectReferenceValue = LitMaterial();
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(mode.gameObject.scene);
            EditorSceneManager.SaveScene(mode.gameObject.scene);
            Debug.Log($"[ResidentFigureSetup] 모델 3 · 얼굴 조각 {face.Length} · 옷 텍스처 {cloth.Length} 연결");
        }

        private static void ConfigureModel(string path, bool humanoid)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null)
            {
                Debug.LogError("[ResidentFigureSetup] 모델 없음: " + path);
                return;
            }
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.importBlendShapes = true;
            imp.importAnimation = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importNormals = ModelImporterNormals.Import;
            imp.animationType = humanoid ? ModelImporterAnimationType.Human : ModelImporterAnimationType.None;
            if (humanoid)
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport();
            if (humanoid)
            {
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                if (avatar == null || !avatar.isHuman || !avatar.isValid)
                    Debug.LogError("[ResidentFigureSetup] Humanoid 아바타 만들기 실패: " + path);
            }
        }

        private static Texture2D[] Textures(string dir)
        {
            var list = new List<Texture2D>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp != null && (imp.wrapMode != TextureWrapMode.Clamp || !imp.sRGBTexture))
                {
                    imp.wrapMode = TextureWrapMode.Clamp;
                    imp.sRGBTexture = true;
                    imp.SaveAndReimport();
                }
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (t != null)
                    list.Add(t);
            }
            return list.ToArray();
        }

        /// <summary>인물 재질: URP Lit (텍스처 · 색은 주민마다 MaterialPropertyBlock).</summary>
        private static Material LitMaterial()
        {
            const string path = "Assets/_Project/Art/Materials/Interior/M_ResidentLit.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", null);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.15f);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }
    }
}
