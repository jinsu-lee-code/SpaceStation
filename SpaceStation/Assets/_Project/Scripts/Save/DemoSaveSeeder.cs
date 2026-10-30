using System.IO;
using UnityEngine;

namespace SpaceStation.Save
{
    /// <summary>
    /// 합본 검수 빌드 체험용 저장 (사용자 2026-10-30 "체험용으로 세이브 하나 넣어주고"): <c>StreamingAssets/DemoSaves/{슬롯}.json</c>(+ 같은 이름 .png)을
    /// 처음 실행할 때 저장 폴더로 복사한다 (그 슬롯이 차 있으면 비어 있는 다른 수동 슬롯) → 메인 메뉴 [이어하기] · [불러오기]에 바로 보임.
    /// 있는 저장은 덮지 않고, 한 번 넣은 뒤(지우거나 덮어써도)에는 다시 넣지 않음 (PlayerPrefs 표시).
    /// </summary>
    public static class DemoSaveSeeder
    {
        private const string Folder = "DemoSaves";
        private const string SeededKey = "demo_saves_seeded_v1";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void SeedOnStart()
        {
            // 에디터 플레이는 넣지 않음 (개발 PC 저장 폴더를 건드리지 않게) — 빌드에서만
            if (Application.isEditor || SaveService.Directory != null || PlayerPrefs.GetInt(SeededKey, 0) == 1)
                return;
            int copied = Seed(Path.Combine(Application.streamingAssetsPath, Folder), Path.Combine(Application.persistentDataPath, "Saves"));
            if (copied == 0)
                return; // 빈 수동 슬롯이 없으면 다음 실행 때 다시
            PlayerPrefs.SetInt(SeededKey, 1);
            PlayerPrefs.Save();
            Debug.Log($"[Save] 체험용 저장 {copied}개를 넣음");
        }

        /// <summary>
        /// source의 슬롯 저장(.json + .png)을 target에 복사: 같은 이름 슬롯이 비었으면 그 슬롯, 아니면 비어 있는 첫 수동 슬롯
        /// (예전 빌드 저장이 남은 PC). 빈 슬롯이 없으면 넣지 않음 — 있는 저장은 덮지 않는다. 복사한 저장 수.
        /// </summary>
        public static int Seed(string source, string target)
        {
            if (!System.IO.Directory.Exists(source))
                return 0;
            System.IO.Directory.CreateDirectory(target);
            int copied = 0;
            foreach (var json in System.IO.Directory.GetFiles(source, "*.json"))
            {
                string slot = FreeSlot(Path.GetFileNameWithoutExtension(json), target);
                if (slot == null)
                    continue;
                File.Copy(json, Path.Combine(target, slot + ".json"));
                string png = Path.ChangeExtension(json, ".png");
                if (File.Exists(png))
                    File.Copy(png, Path.Combine(target, slot + ".png"), true);
                copied++;
            }
            return copied;
        }

        /// <summary>같은 이름 슬롯 → 비어 있는 첫 수동 슬롯 → 불러올 수 없는 예전 버전 저장이 든 첫 수동 슬롯 (그 파일은 {슬롯}.old.json · .png로 옮겨 남김).</summary>
        private static string FreeSlot(string wanted, string target)
        {
            if (!File.Exists(Path.Combine(target, wanted + ".json")))
                return wanted;
            foreach (var slot in SaveService.ManualSlots)
            {
                if (!File.Exists(Path.Combine(target, slot + ".json")))
                    return slot;
            }
            foreach (var slot in SaveService.ManualSlots)
            {
                string json = Path.Combine(target, slot + ".json");
                if (!IsOutdated(json))
                    continue;
                MoveAside(json, Path.Combine(target, slot + ".old.json"));
                MoveAside(Path.Combine(target, slot + ".png"), Path.Combine(target, slot + ".old.png"));
                return slot;
            }
            return null;
        }

        /// <summary>읽을 수 있는 가장 낮은 버전보다 오래된 저장 (지금 게임이 "이전 버전"으로만 보여 주고 불러오지 못함).</summary>
        private static bool IsOutdated(string json)
        {
            try
            {
                var file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(json));
                return file != null && file.Version > 0 && file.Version < SaveService.MinReadableVersion;
            }
            catch (System.Exception)
            {
                return false; // 깨진 파일은 건드리지 않음
            }
        }

        /// <summary>옆으로 옮김 (같은 이름이 있으면 .old2 · .old3 … — 아무것도 지우지 않음).</summary>
        private static void MoveAside(string from, string to)
        {
            if (!File.Exists(from))
                return;
            string dest = to;
            for (int k = 2; File.Exists(dest); k++)
                dest = to.Replace(".old.", $".old{k}.");
            File.Move(from, dest);
        }
    }
}
