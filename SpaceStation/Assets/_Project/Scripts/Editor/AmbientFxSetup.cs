using System.Linq;
using SpaceStation.Building;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 5-7 이벤트·배경 연출 배선 (메뉴 SpaceStation/Art/Setup Ambient Fx).
    /// StationController 오브젝트에 MeteorFx·SolarStormFx·AmbientSpace를 붙이고 재질·우주선 메시를 넣는다.
    /// </summary>
    public static class AmbientFxSetup
    {
        private const string ShipPath = "Assets/_Project/Art/Models/Modules/SM_Shuttle.fbx";

        [MenuItem("SpaceStation/Art/Setup Ambient Fx")]
        public static void Setup()
        {
            var station = Object.FindFirstObjectByType<StationController>();
            if (station == null)
            {
                Debug.LogError("[AmbientFxSetup] 씬에 StationController 없음");
                return;
            }

            var meteor = Ensure<MeteorFx>(station.gameObject);
            Assign(meteor, ("_station", station), ("_rockMaterial", Fx("Rock")), ("_glowMaterial", Fx("MeteorGlow")),
                ("_trailMaterial", Fx("MeteorTrail")), ("_flashMaterial", Fx("MeteorFlash")), ("_laserMaterial", Fx("TurretLaser")),
                ("_rippleMaterial", Fx("ShieldRipple")), ("_sparkMaterial", Fx("Spark")));

            var storm = Ensure<SolarStormFx>(station.gameObject);
            Assign(storm, ("_station", station), ("_sparkMaterial", Fx("StormSpark")));

            // 우주선: 텍스처 재질 연결 후 메시
            var importer = AssetImporter.GetAtPath(ShipPath) as ModelImporter;
            Material shipMaterial = null;
            Mesh shipMesh = null;
            if (importer != null)
            {
                var textured = ModuleTextureMaterials.EnsureAll("Shuttle");
                foreach (var slot in textured)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot.Key), slot.Value);
                importer.importAnimation = false;
                importer.SaveAndReimport();
                shipMaterial = textured.Values.FirstOrDefault();
                shipMesh = AssetDatabase.LoadAllAssetsAtPath(ShipPath).OfType<Mesh>().FirstOrDefault();
            }
            var ambient = Ensure<AmbientSpace>(station.gameObject);
            Assign(ambient, ("_station", station), ("_dustMaterial", Fx("Dust")), ("_rockMaterial", Fx("Rock")),
                ("_shipMesh", shipMesh), ("_shipMaterial", shipMaterial), ("_engineMaterial", Fx("ShipEngine")));

            EditorSceneManager.MarkSceneDirty(station.gameObject.scene);
            Debug.Log($"[AmbientFxSetup] meteor/storm/ambient 배선 완료, ship {shipMesh?.name} / {shipMaterial?.name}");
        }

        private static Material Fx(string name) => ModuleFxMaterials.Get(name);

        private static T Ensure<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : Undo.AddComponent<T>(go);
        }

        private static void Assign(Object target, params (string field, Object value)[] values)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in values)
            {
                var p = so.FindProperty(field);
                if (p == null)
                    Debug.LogWarning($"[AmbientFxSetup] {target.GetType().Name}.{field} 없음");
                else
                    p.objectReferenceValue = value;
            }
            so.ApplyModifiedProperties();
        }
    }
}
