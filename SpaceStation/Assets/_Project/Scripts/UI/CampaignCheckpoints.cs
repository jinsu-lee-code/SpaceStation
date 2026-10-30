using System.Collections;
using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Save;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 13-7d 캠페인 실패 안전망 (사용자 결정 2026-10-25: 단일 난이도 + 게임 오버면 절정 직전 · 장 처음부터 다시).
    /// 장이 시작될 때(새 캠페인 = 1장 처음 포함) · 절정 예고가 시작될 때 캠페인 체크포인트를 저장하고,
    /// 게임 오버 화면에 [절정 직전부터] [이 장 처음부터]를 넣는다 (main M-45 <see cref="ResultScreen.RetryOptions"/>, 없으면 [재시작]).
    /// 체크포인트는 목록에 없는 저장 칸이라 이어하기 · 저장 목록에 보이지 않고, 장 상태의 판 번호(<see cref="ChapterSystem.RunId"/>)로 이 판 · 이 장 것만 쓴다.
    /// 씬에는 정거장만 연결된 빈 오브젝트 (메뉴 SpaceStation/Chapters/Setup Greybox).
    /// </summary>
    public sealed class CampaignCheckpoints : MonoBehaviour
    {
        public const string ChapterSlot = "campaign_chapter";
        public const string ClimaxSlot = "campaign_climax";

        [SerializeField] private StationController _station;

        private ChapterSystem _chapters;

        private IEnumerator Start()
        {
            var sim = _station != null ? _station.Simulation : null;
            _chapters = sim?.Chapters;
            if (_chapters == null)
            {
                enabled = false;
                yield break;
            }
            _chapters.ChapterStarted += HandleChapterStarted;
            _chapters.WarningStarted += HandleWarningStarted;
            ResultScreen.RetryOptions = Options;
            yield return null; // 저장 담당(SaveManager)이 시뮬레이션을 잡은 뒤
            var host = FindFirstObjectByType<SimulationHost>();
            if (host != null && host.LoadedSave == null && _chapters.Index == 0 && _chapters.ChapterSeconds < 0.5f)
                Save(ChapterSlot); // 새 캠페인 = 1장 처음
        }

        private void OnDestroy()
        {
            if (_chapters != null)
            {
                _chapters.ChapterStarted -= HandleChapterStarted;
                _chapters.WarningStarted -= HandleWarningStarted;
            }
            if (ResultScreen.RetryOptions != null && ResultScreen.RetryOptions.Target == (object)this)
                ResultScreen.RetryOptions = null;
        }

        // 다른 구독자(장 화면 · 이민선 · 규칙)가 같은 순간을 다 처리한 다음 프레임에 저장 (전환 · 예고 중이라 그사이 시간은 거의 흐르지 않음)
        private void HandleChapterStarted(int index) => StartCoroutine(SaveNextFrame(ChapterSlot));

        private void HandleWarningStarted() => StartCoroutine(SaveNextFrame(ClimaxSlot));

        private IEnumerator SaveNextFrame(string slot)
        {
            yield return null;
            Save(slot);
        }

        private void Save(string slot)
        {
            var saver = SaveManager.Instance;
            if (saver == null || !saver.SaveTo(slot, false))
                return;
            Debug.Log($"[캠페인] 체크포인트 저장 · {(slot == ClimaxSlot ? "위기 직전" : "장 처음")} ({_chapters.ProgressText()})");
        }

        /// <summary>게임 오버 때 다시 시작 지점: 이 판 · 지금 장의 절정 예고 체크포인트, 이 판 · 지금 장의 처음 체크포인트.</summary>
        private IReadOnlyList<RetryOption> Options()
        {
            var list = new List<RetryOption>();
            if (Matches(ClimaxSlot, climax: true))
                list.Add(new RetryOption { Label = "위기 직전부터", Run = () => Load(ClimaxSlot) });
            if (Matches(ChapterSlot, climax: false))
                list.Add(new RetryOption { Label = "이 장 처음부터", Run = () => Load(ChapterSlot) });
            return list;
        }

        private bool Matches(string slot, bool climax)
        {
            if (_chapters == null || SaveService.TryRead(slot, out var file) != SaveSlotStatus.Ok || file.Station.Systems == null)
                return false;
            var entry = file.Station.Systems.Find(s => s != null && s.Key == ChapterSystem.SystemKey);
            if (entry == null || !ChapterSystem.TryReadRun(entry.State, out string run, out int index, out var phase))
                return false;
            return run == _chapters.RunId && index == _chapters.Index && (!climax || phase == ChapterPhase.Warning);
        }

        private static void Load(string slot)
        {
            if (!SaveManager.LoadAndPlay(slot))
                Debug.LogWarning($"[캠페인] 체크포인트를 불러오지 못함: {slot}");
        }
    }
}
