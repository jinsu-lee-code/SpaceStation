using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 남은 요구 2종 (사용자 결정 2026-10-22) 공통 데이터: 식단 · 치안 감점(만족도 상한 −15, 교육 · 검역 · 소비 · 행정과 같음) +
    /// 식량 모듈의 식품 종류 (수경 농장 = 채소, 단백질 배양실 = 단백질 — 진화 모듈은 Recipes/Setup이 넣음).
    /// 어느 등급에서 요구가 열리는지는 판마다 (타이쿤판 = Tycoon/Setup Needs). 치안을 맡는 보안청 · 치안 본부는 Recipes/Setup.
    /// 다시 실행해도 같은 값.
    /// </summary>
    public static class DietSecuritySetup
    {
        private const float Penalty = 15f;

        [MenuItem("SpaceStation/Needs/Setup Diet and Security")]
        public static void Setup()
        {
            var config = AssetDatabase.LoadAssetAtPath<BalanceConfig>("Assets/_Project/Data/BalanceConfig.asset");
            if (config == null)
            {
                Debug.LogError("[DietSecurity] BalanceConfig 없음");
                return;
            }
            var so = new SerializedObject(config);
            var list = so.FindProperty("_needPenalties");
            foreach (var need in new[] { ResidentNeed.Diet, ResidentNeed.Security })
            {
                int found = -1;
                for (int i = 0; i < list.arraySize; i++)
                {
                    if (list.GetArrayElementAtIndex(i).FindPropertyRelative("Need").enumValueIndex == (int)need)
                        found = i;
                }
                if (found < 0)
                {
                    found = list.arraySize;
                    list.arraySize++;
                }
                var e = list.GetArrayElementAtIndex(found);
                e.FindPropertyRelative("Need").enumValueIndex = (int)need;
                e.FindPropertyRelative("Penalty").floatValue = Penalty;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);

            SetFood("MD_Farm", FoodKind.Vegetable);
            SetFood("MD_ProteinVat", FoodKind.Protein);
            AssetDatabase.SaveAssets();
            Debug.Log("[DietSecurity] 식단 · 치안 감점 15 · 수경 농장 = 채소 · 단백질 배양실 = 단백질");
        }

        private static void SetFood(string module, FoodKind kind)
        {
            var data = AssetDatabase.LoadAssetAtPath<ModuleData>($"Assets/_Project/Data/Modules/{module}.asset");
            if (data == null)
            {
                Debug.LogWarning($"[DietSecurity] 모듈 없음: {module}");
                return;
            }
            var so = new SerializedObject(data);
            so.FindProperty("_foodKinds").enumValueFlag = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
        }
    }
}
