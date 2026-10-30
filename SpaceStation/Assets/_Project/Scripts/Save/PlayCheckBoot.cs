#if UNITY_EDITOR
using System.IO;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Save
{
    /// <summary>
    /// 에디터 전용 플레이 확인 루트 (메뉴 SpaceStation/플레이 확인, 사용자 요청 2026-10-20): 메뉴가 만든 시작 상태를 플레이가 시작될 때
    /// 불러오기로 넘긴다 (게임 씬 호스트가 <see cref="GameStartOptions.PendingLoad"/>를 복원). 확인용 판의 저장(자동 저장 포함)은
    /// 프로젝트 Temp/PlayCheck/Saves로 돌려 실제 세이브를 덮지 않는다. 메뉴 없이 그냥 플레이하면 아무것도 하지 않는다.
    /// </summary>
    public static class PlayCheckBoot
    {
        /// <summary>메뉴가 시작 상태 파일 경로를 넣는 SessionState 키 (도메인을 다시 불러와도 남음, 한 번 쓰면 지움).</summary>
        public const string StartKey = "SpaceStation.PlayCheck.Start";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            string path = UnityEditor.SessionState.GetString(StartKey, "");
            if (string.IsNullOrEmpty(path))
                return;
            UnityEditor.SessionState.EraseString(StartKey);
            if (!File.Exists(path))
                return;
            var file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path));
            if (file == null || file.Station == null)
                return;
            SaveService.Directory = Path.Combine(Path.GetDirectoryName(path), "Saves");
            GameStartOptions.PendingLoad = file;
            Debug.Log($"[플레이 확인] {file.Meta.GradeName} 정거장에서 시작 · 이번 판 저장은 {SaveService.Directory}");
        }
    }
}
#endif
