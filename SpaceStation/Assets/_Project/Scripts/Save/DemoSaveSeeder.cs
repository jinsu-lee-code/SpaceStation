using System.IO;
using UnityEngine;

namespace SpaceStation.Save
{
    /// <summary>
    /// 합본 검수 빌드 체험용 저장 (사용자 2026-10-30 "체험용으로 세이브 하나 넣어주고"): <c>StreamingAssets/DemoSaves/{슬롯}.json</c>(+ 같은 이름 .png)을
    /// 처음 실행할 때 한 번만 그 슬롯이 비어 있으면 저장 폴더로 복사한다 → 메인 메뉴 [이어하기] · [불러오기]에 바로 보임.
    /// 이미 그 슬롯에 저장이 있거나, 한 번 넣은 뒤(지우거나 덮어써도)에는 다시 넣지 않음 (PlayerPrefs 표시).
    /// </summary>
    public static class DemoSaveSeeder
    {
        private const string Folder = "DemoSaves";
        private const string SeededKey = "demo_saves_seeded_v1";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void SeedOnStart()
        {
            if (SaveService.Directory != null || PlayerPrefs.GetInt(SeededKey, 0) == 1)
                return;
            int copied = Seed(Path.Combine(Application.streamingAssetsPath, Folder), Path.Combine(Application.persistentDataPath, "Saves"));
            PlayerPrefs.SetInt(SeededKey, 1);
            PlayerPrefs.Save();
            if (copied > 0)
                Debug.Log($"[Save] 체험용 저장 {copied}개를 넣음");
        }

        /// <summary>source의 슬롯 저장(.json + .png)을 target에 없는 것만 복사. 복사한 저장 수.</summary>
        public static int Seed(string source, string target)
        {
            if (!System.IO.Directory.Exists(source))
                return 0;
            System.IO.Directory.CreateDirectory(target);
            int copied = 0;
            foreach (var json in System.IO.Directory.GetFiles(source, "*.json"))
            {
                string slot = Path.GetFileNameWithoutExtension(json);
                string to = Path.Combine(target, slot + ".json");
                if (File.Exists(to))
                    continue;
                File.Copy(json, to);
                string png = Path.ChangeExtension(json, ".png");
                if (File.Exists(png))
                    File.Copy(png, Path.Combine(target, slot + ".png"), true);
                copied++;
            }
            return copied;
        }
    }
}
