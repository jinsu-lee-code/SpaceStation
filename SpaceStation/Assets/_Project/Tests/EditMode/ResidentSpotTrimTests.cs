using System.Text;
using NUnit.Framework;
using SpaceStation.Editor;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 주민 붐빔 줄이기 (사용자 2026-11-17 "주민들이 모듈에 너무 많아서 걸어다니기 조금 불편" → 2026-11-18 방마다 골라 뺌):
    /// <see cref="ResidentSpotBaker.TrimmedSpots"/>의 자리가 구운 템플릿에 남아 있지 않음 (메뉴 SpaceStation/Interior/Bake Resident Spots · Build Evolved Templates를 다시 돌렸는지).
    /// </summary>
    public class ResidentSpotTrimTests
    {
        [Test]
        public void TrimmedSpots_AreGoneFromTemplates()
        {
            var problems = new StringBuilder();
            int rooms = 0;
            foreach (var kv in ResidentSpotBaker.TrimmedSpots)
            {
                var template = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.InteriorTemplate>($"Assets/_Project/Data/Interior/IT_{kv.Key}.asset");
                if (template == null)
                {
                    problems.AppendLine($"{kv.Key}: 템플릿 없음");
                    continue;
                }
                rooms++;
                foreach (var cut in kv.Value)
                {
                    foreach (var spot in template.ResidentSpots)
                    {
                        if ((spot.Position - cut).sqrMagnitude < 0.03f * 0.03f)
                            problems.AppendLine($"{kv.Key}: 뺀 자리 {cut}가 남아 있음");
                    }
                }
            }
            Assert.GreaterOrEqual(rooms, 20);
            Assert.IsEmpty(problems.ToString(), problems.ToString());
        }
    }
}
