using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpaceStation.Save
{
    public enum SaveSlotStatus
    {
        Empty,
        Ok,
        /// <summary>더 새로운 버전에서 저장했거나 파일이 깨짐.</summary>
        Incompatible,
    }

    /// <summary>슬롯 하나의 목록 정보.</summary>
    public sealed class SaveSlotInfo
    {
        public string Id;
        public string Title;
        public bool IsAuto;
        public SaveSlotStatus Status;
        public SaveMeta Meta;
        public string ThumbnailPath;
    }

    /// <summary>
    /// 세이브 파일 입출력 (Phase 6). 슬롯 = 자동 1 + 수동 3. 위치 = {persistentDataPath}/Saves/{슬롯}.json (+ 같은 이름 .png 썸네일).
    /// 쓰기는 임시 파일에 쓴 뒤 바꿔치기해서 저장 도중 꺼져도 이전 저장이 깨지지 않는다.
    /// 버전: 같거나 낮은 버전은 읽는다(없어진 항목은 건너뜀), 더 높은 버전·깨진 파일은 "호환되지 않음".
    /// </summary>
    public static class SaveService
    {
        public const int CurrentVersion = 1;
        public const string AutoSlot = "auto";
        public static readonly string[] ManualSlots = { "slot1", "slot2", "slot3" };

        /// <summary>테스트에서 바꿀 수 있는 저장 폴더.</summary>
        public static string Directory { get; set; }

        private static string Root => Directory ?? Path.Combine(Application.persistentDataPath, "Saves");

        public static string JsonPath(string slot) => Path.Combine(Root, slot + ".json");
        public static string ThumbnailPath(string slot) => Path.Combine(Root, slot + ".png");

        public static string TitleOf(string slot)
        {
            if (slot == AutoSlot)
                return "자동 저장";
            int i = Array.IndexOf(ManualSlots, slot);
            return i >= 0 ? $"수동 저장 {i + 1}" : slot;
        }

        /// <summary>자동 + 수동 슬롯 전부 (빈 슬롯 포함).</summary>
        public static List<SaveSlotInfo> ListSlots(bool includeAuto = true)
        {
            var list = new List<SaveSlotInfo>();
            if (includeAuto)
                list.Add(Describe(AutoSlot));
            foreach (var slot in ManualSlots)
                list.Add(Describe(slot));
            return list;
        }

        public static SaveSlotInfo Describe(string slot)
        {
            var info = new SaveSlotInfo { Id = slot, Title = TitleOf(slot), IsAuto = slot == AutoSlot };
            var status = TryRead(slot, out var file);
            info.Status = status;
            info.Meta = file?.Meta;
            string png = ThumbnailPath(slot);
            info.ThumbnailPath = status == SaveSlotStatus.Ok && File.Exists(png) ? png : null;
            return info;
        }

        /// <summary>가장 최근에 저장된 읽을 수 있는 슬롯 (없으면 null) — 메인 메뉴 [이어하기].</summary>
        public static string MostRecentSlot()
        {
            string best = null;
            long bestTicks = long.MinValue;
            foreach (var info in ListSlots())
            {
                if (info.Status == SaveSlotStatus.Ok && info.Meta.SavedAtTicks > bestTicks)
                {
                    best = info.Id;
                    bestTicks = info.Meta.SavedAtTicks;
                }
            }
            return best;
        }

        public static SaveSlotStatus TryRead(string slot, out SaveFile file)
        {
            file = null;
            string path = JsonPath(slot);
            if (!File.Exists(path))
                return SaveSlotStatus.Empty;
            try
            {
                file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] {slot} 읽기 실패: {e.Message}");
                file = null;
            }
            if (file == null || file.Station == null || file.Meta == null || file.Version > CurrentVersion || file.Version < 1)
            {
                file = null;
                return SaveSlotStatus.Incompatible;
            }
            return SaveSlotStatus.Ok;
        }

        /// <summary>저장 (임시 파일 → 교체). 실패하면 false, 이전 저장은 그대로.</summary>
        public static bool Write(string slot, SaveFile file, byte[] thumbnailPng)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Root);
                file.Version = CurrentVersion;
                WriteAtomic(JsonPath(slot), System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(file)));
                string png = ThumbnailPath(slot);
                if (thumbnailPng != null && thumbnailPng.Length > 0)
                    WriteAtomic(png, thumbnailPng);
                else if (File.Exists(png))
                    File.Delete(png);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] {slot} 저장 실패: {e.Message}");
                return false;
            }
        }

        public static void Delete(string slot)
        {
            try
            {
                foreach (var path in new[] { JsonPath(slot), ThumbnailPath(slot) })
                    if (File.Exists(path))
                        File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] {slot} 삭제 실패: {e.Message}");
            }
        }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
    }
}
