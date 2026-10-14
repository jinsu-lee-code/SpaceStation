using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 13-1 장 목록 (1장부터 차례로, REDESIGN P1 · P3-2). 마지막 장의 전환이 끝나면 캠페인 끝
    /// (수직 단면은 3장 끝에 "이어질 이야기"). 비어 있으면 장 틀 없음 = 등급이 해금 · 규모를 정하는 이전 방식.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Chapter Config", fileName = "Chapters")]
    public sealed class ChapterConfig : ScriptableObject
    {
        [SerializeField] private List<ChapterData> _chapters = new List<ChapterData>();

        // 난이도 덮어쓰기 (복사본에만, 장 에셋은 공유라 건드리지 않음). 음수 = 장 값 유지
        private List<float> _intervalOverrides;

        public IReadOnlyList<ChapterData> Chapters => _chapters;
        public int Count => _chapters.Count;

        /// <summary>장의 이벤트 간격 배율 (난이도 덮어쓰기가 있으면 그것).</summary>
        public float EventIntervalMultiplier(int chapter)
        {
            if (_intervalOverrides != null && chapter >= 0 && chapter < _intervalOverrides.Count && _intervalOverrides[chapter] >= 0f)
                return Mathf.Max(0.05f, _intervalOverrides[chapter]);
            return chapter >= 0 && chapter < _chapters.Count && _chapters[chapter] != null ? _chapters[chapter].EventIntervalMultiplier : 1f;
        }

        /// <summary>난이도 덮어쓰기 — 복사본에만 호출.</summary>
        internal void ApplyEventIntervals(IReadOnlyList<float> multipliers)
        {
            _intervalOverrides = multipliers != null ? new List<float>(multipliers) : null;
        }
    }
}
