using System.Collections.Generic;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 11-17 ③ 템플릿 식물 자리 (메뉴 SpaceStation/Interior/Set Plant Spots, Build Templates 끝에도 실행).
    /// 좌표는 Blender 빌더(Interior_Kit.blend 텍스트 블록)의 화분 · 화단 흙 윗면과 같다 — 빌더를 고치면 여기도 함께.
    /// </summary>
    public static class InteriorPlantSpots
    {
        private const string DataFolder = "Assets/_Project/Data/Interior/";
        private const float F = -1.3f;

        [MenuItem("SpaceStation/Interior/Set Plant Spots")]
        public static void SetAll()
        {
            // ring_builder: 안쪽벽 화분 8 (반지름 R_IN + 0.45 = 5.75, 창과 같은 각도 22.5 + 45k), 흙 F + 0.46.
            // 안쪽 벽이 0.45m 앞이라 0.7배 (잎 폭 ±0.66 → ±0.46)
            var ring = new List<PlantSpot>();
            for (int k = 0; k < 8; k++)
            {
                float a = (22.5f + 45f * k) * Mathf.Deg2Rad;
                ring.Add(new PlantSpot(new Vector3(5.75f * Mathf.Cos(a), F + 0.46f, 5.75f * Mathf.Sin(a)), PlantKind.Pot, 0.7f, k * 47f));
            }
            Set("IT_RotatingRing", ring);
            // recreation_builder: 창 양옆 화분 2 (x 1.75 · 6.25, z −2.0), 흙 F + 0.5
            Set("IT_Recreation", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(1.75f, F + 0.5f, -2f), PlantKind.Pot, 0.9f, 20f),
                new PlantSpot(new Vector3(6.25f, F + 0.5f, -2f), PlantKind.Pot, 0.9f, 140f),
            });
            // farm_builder: 돔 아래 원형 화단 2 (x 0 · 8, z 0), 흙 F + 0.5
            Set("IT_Farm", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(0f, F + 0.5f, 0f), PlantKind.Tree, 1f, 0f),
                new PlantSpot(new Vector3(8f, F + 0.5f, 0f), PlantKind.Tree, 1f, 110f),
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[InteriorPlantSpots] 식물 자리: 회전 링 8 · 휴게실 2 · 수경 농장 2");
        }

        private static void Set(string name, List<PlantSpot> spots)
        {
            var t = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(DataFolder + name + ".asset");
            if (t == null)
            {
                Debug.LogWarning("[InteriorPlantSpots] 템플릿 없음: " + name);
                return;
            }
            t.EditorSetPlants(spots);
            EditorUtility.SetDirty(t);
        }
    }
}
