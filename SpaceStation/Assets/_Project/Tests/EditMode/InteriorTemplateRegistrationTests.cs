using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 모든 내부 템플릿 (Data/Interior/IT_*.asset)이 Main 씬 InteriorMode 템플릿 목록에 들어 있음 — 빠지면 그 모듈에 들어갈 때 전용 내부 대신 기본 방이 뜸
    /// (2026-11-10 화물 환적장: 템플릿 빌드 뒤 씬 변경을 되돌려 등록이 빠졌음, 사용자 "내부가 아예 날아갔는데?").
    /// </summary>
    public class InteriorTemplateRegistrationTests
    {
        [Test]
        public void EveryTemplate_IsRegisteredInMainScene()
        {
            string scene = File.ReadAllText("Assets/_Project/Scenes/Main.unity");
            var missing = new System.Collections.Generic.List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate", new[] { "Assets/_Project/Data/Interior" }))
            {
                if (!scene.Contains("guid: " + guid))
                    missing.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));
            }
            Assert.IsEmpty(missing, "Main 씬에 등록 안 된 템플릿 (SpaceStation/Interior/Setup): " + string.Join(", ", missing));
        }
    }
}
