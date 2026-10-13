using SpaceStation.Building;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 12-3c 등급별 핵심 규칙 그레이박스 설정 · 확인 메뉴 (SpaceStation/Grade Rules/...).
    /// </summary>
    public static class GradeRulesSetup
    {
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";

        [MenuItem("SpaceStation/Grade Rules/Setup Meteor Shower View")]
        public static void SetupMeteorShower()
        {
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var station = Object.FindFirstObjectByType<StationController>();
            var view = Object.FindFirstObjectByType<MeteorShowerZoneView>();
            if (view == null)
            {
                view = new GameObject("MeteorShowerZone").AddComponent<MeteorShowerZoneView>();
                Undo.RegisterCreatedObjectUndo(view.gameObject, "MeteorShowerZone");
            }
            var so = new SerializedObject(view);
            so.FindProperty("_station").objectReferenceValue = station;
            so.FindProperty("_material").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_DefenseRange.mat");
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[GradeRulesSetup] 유성우 표시 연결 완료");
        }

        [MenuItem("SpaceStation/Grade Rules/Meteor Shower Now (60s)")]
        public static void ShowerNow() => Trigger(60f);

        [MenuItem("SpaceStation/Grade Rules/Meteor Shower Now (10s)")]
        public static void ShowerSoon() => Trigger(10f);

        [MenuItem("SpaceStation/Grade Rules/Meteor Shower Now (60s)", true)]
        [MenuItem("SpaceStation/Grade Rules/Meteor Shower Now (10s)", true)]
        public static bool CanTrigger() => Application.isPlaying;

        private static void Trigger(float warning)
        {
            var station = Object.FindFirstObjectByType<StationController>();
            var shower = station != null && station.Simulation != null ? station.Simulation.MeteorShower : null;
            if (shower == null)
            {
                Debug.LogWarning("[GradeRulesSetup] 게임 씬 플레이 중에만");
                return;
            }
            shower.BeginWarning(warning);
        }
    }
}
