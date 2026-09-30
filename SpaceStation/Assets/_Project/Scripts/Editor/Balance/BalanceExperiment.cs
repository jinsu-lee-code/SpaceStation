using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// 수치 실험 (4-10): 에셋 값을 메모리에서만 임시로 바꿔 측정하고, 끝나면 원래 값으로 되돌린다 (저장하지 않음).
    /// 예: BalanceExperiment.Run("decay 0.1", 60f, 20, BalanceExperiment.Set("BalanceConfig", "_durabilityDecayPerSecond", 0.1f));
    /// 에셋 경로는 Data 폴더 기준 (".asset" 생략 가능).
    /// </summary>
    public static class BalanceExperiment
    {
        public const string DataRoot = "Assets/_Project/Data/";

        public struct Override
        {
            public string AssetPath;
            public string PropertyPath;
            public float Value;
        }

        public static Override Set(string asset, string property, float value) => new Override
        {
            AssetPath = DataRoot + (asset.EndsWith(".asset") ? asset : asset + ".asset"),
            PropertyPath = property,
            Value = value,
        };

        /// <returns>측정 요약 텍스트 (첫 줄에 라벨과 적용 값)</returns>
        public static string Run(string label, float minutes, int runs, params Override[] overrides)
        {
            var restore = new List<Action>();
            var applied = new List<string>();
            try
            {
                foreach (var o in overrides)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<Object>(o.AssetPath);
                    if (asset == null)
                        throw new ArgumentException($"에셋 없음: {o.AssetPath}");
                    var so = new SerializedObject(asset);
                    var p = so.FindProperty(o.PropertyPath);
                    if (p == null)
                        throw new ArgumentException($"속성 없음: {o.AssetPath} {o.PropertyPath}");
                    string before = Read(p);
                    Write(p, o.Value);
                    so.ApplyModifiedPropertiesWithoutUndo();
                    string path = o.PropertyPath;
                    string original = before;
                    restore.Add(() =>
                    {
                        var rso = new SerializedObject(asset);
                        var rp = rso.FindProperty(path);
                        WriteRaw(rp, original);
                        rso.ApplyModifiedPropertiesWithoutUndo();
                    });
                    applied.Add($"{System.IO.Path.GetFileNameWithoutExtension(o.AssetPath)}.{o.PropertyPath} {before}→{o.Value}");
                }
                var (_, summary) = BalanceSimulatorWindow.RunWithDefaults(minutes, runs, 1);
                return $"=== {label} ===\n{string.Join("\n", applied)}\n{summary}";
            }
            finally
            {
                for (int i = restore.Count - 1; i >= 0; i--)
                    restore[i]();
            }
        }

        private static string Read(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: return p.floatValue.ToString("R");
                case SerializedPropertyType.Integer: return p.intValue.ToString();
                case SerializedPropertyType.Boolean: return p.boolValue ? "1" : "0";
                case SerializedPropertyType.Enum: return p.enumValueIndex.ToString();
                default: throw new ArgumentException($"지원하지 않는 속성 형식: {p.propertyPath} {p.propertyType}");
            }
        }

        private static void Write(SerializedProperty p, float value)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: p.floatValue = value; break;
                case SerializedPropertyType.Integer: p.intValue = Mathf.RoundToInt(value); break;
                case SerializedPropertyType.Boolean: p.boolValue = value > 0.5f; break;
                case SerializedPropertyType.Enum: p.enumValueIndex = Mathf.RoundToInt(value); break;
            }
        }

        private static void WriteRaw(SerializedProperty p, string raw)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: p.floatValue = float.Parse(raw); break;
                case SerializedPropertyType.Integer: p.intValue = int.Parse(raw); break;
                case SerializedPropertyType.Boolean: p.boolValue = raw == "1"; break;
                case SerializedPropertyType.Enum: p.enumValueIndex = int.Parse(raw); break;
            }
        }
    }
}
