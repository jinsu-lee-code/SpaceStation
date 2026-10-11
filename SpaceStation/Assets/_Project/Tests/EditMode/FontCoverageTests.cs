using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine.TextCore.LowLevel;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 화면 글에 Maplestory 글꼴에 없는 글자가 섞이면 네모(□)로 나옴 — 빼기 "−"(U+2212, 11-17 ②) · 줄표 "—"(U+2014, 11-17 ④)에서 실제로 겪음.
    /// 스크립트 문자열(주석 · Tooltip · Header 제외, Editor 폴더 제외)과 Data 에셋 글자를 글꼴 원본 파일에서 조회 (동적 아틀라스는 건드리지 않음).
    /// </summary>
    public class FontCoverageTests
    {
        private static readonly string[] Fonts =
        {
            "Assets/_Project/Art/Fonts/Maplestory SDF.asset",
            "Assets/_Project/Art/Fonts/Maplestory Bold SDF.asset",
        };

        [Test]
        public void DisplayedText_HasNoMissingGlyphs()
        {
            var chars = new Dictionary<char, string>();
            foreach (var path in Directory.GetFiles("Assets/_Project/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                if (path.Replace('\\', '/').Contains("/Editor/"))
                    continue;
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                    Collect(StringLiterals(lines[i]), Path.GetFileName(path) + ":" + (i + 1), chars);
            }
            foreach (var path in Directory.GetFiles("Assets/_Project/Data", "*.asset", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                    Collect(DecodeYaml(lines[i]), Path.GetFileName(path) + ":" + (i + 1), chars);
            }

            var missing = new StringBuilder();
            foreach (var fontPath in Fonts)
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
                Assert.IsNotNull(font, fontPath);
                Assert.IsNotNull(font.sourceFontFile, fontPath + " 원본 글꼴 파일");
                Assert.AreEqual(FontEngineError.Success, FontEngine.LoadFontFace(font.sourceFontFile));
                foreach (var pair in chars)
                {
                    if (!FontEngine.TryGetGlyphIndex(pair.Key, out uint index) || index == 0)
                        missing.AppendLine($"{font.name}: U+{(int)pair.Key:X4} '{pair.Key}' ({pair.Value})");
                }
            }
            Assert.IsEmpty(missing.ToString(), "글꼴에 없는 글자 (네모로 나옴):\n" + missing);
        }

        private static void Collect(string text, string where, Dictionary<char, string> chars)
        {
            foreach (char c in text)
            {
                if (c >= 128 && !char.IsSurrogate(c) && !chars.ContainsKey(c))
                    chars[c] = where;
            }
        }

        /// <summary>한 줄의 문자열 리터럴 내용 (주석 · Tooltip · Header 줄은 빈 문자열 — 에디터에만 보임).</summary>
        private static string StringLiterals(string line)
        {
            string t = line.Trim();
            if (t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("[Tooltip") || t.StartsWith("[Header"))
                return string.Empty;
            var sb = new StringBuilder();
            bool inString = false;
            for (int k = 0; k < t.Length; k++)
            {
                char c = t[k];
                if (!inString && c == '/' && k + 1 < t.Length && t[k + 1] == '/')
                    break;
                if (c == '"' && (k == 0 || t[k - 1] != '\\'))
                {
                    inString = !inString;
                    continue;
                }
                if (inString)
                    sb.Append(c);
            }
            // 여러 줄 Tooltip의 이어지는 줄 ("..." + 으로 시작)은 앞 줄을 모르니 그대로 둠 — 걸리면 그 문구를 고치거나 한 줄로
            return sb.ToString();
        }

        /// <summary>YAML 줄의 \uXXXX를 글자로.</summary>
        private static string DecodeYaml(string line)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '\\' && i + 5 < line.Length && line[i + 1] == 'u'
                    && int.TryParse(line.Substring(i + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out int v))
                {
                    sb.Append((char)v);
                    i += 5;
                    continue;
                }
                sb.Append(line[i]);
            }
            return sb.ToString();
        }
    }
}
