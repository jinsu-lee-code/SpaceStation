using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>13-5b 기록 복원 한 갈래 (연구 분야 하나에 붙는 이야기 · 봉인 단계 강화 값).</summary>
    [Serializable]
    public sealed class RecordTrack
    {
        [Tooltip("이 갈래의 연구 분야 (서사판 전용 RC_Record*)")]
        [SerializeField] private ResearchCategoryData _category;
        [Tooltip("단계마다 되살린 지구의 기억 한 줄 (1단계부터)")]
        [SerializeField, TextArea(1, 3)] private List<string> _memories = new List<string>();
        [Tooltip("봉인 단계에서만 드러나는 인간 미스터리 한 줄 (T8)")]
        [SerializeField, TextArea(1, 3)] private string _sealedHuman;
        [Tooltip("봉인 단계를 덮어쓰면 쓰는 효과 (레벨 표의 효과 × 1.5)")]
        [SerializeField] private List<ResearchModifier> _overwriteModifiers = new List<ResearchModifier>();
        [Tooltip("덮어쓴 효과 설명 (선택 창)")]
        [SerializeField] private string _overwriteDescription;

        public ResearchCategoryData Category => _category;
        public IReadOnlyList<string> Memories => _memories;
        public string SealedHuman => _sealedHuman;
        public IReadOnlyList<ResearchModifier> OverwriteModifiers => _overwriteModifiers;
        public string OverwriteDescription => _overwriteDescription;

        /// <summary>단계(1부터)의 기억 한 줄 (없으면 빈 문자열).</summary>
        public string Memory(int level) => level >= 1 && level <= _memories.Count ? _memories[level - 1] : "";

        public RecordTrack()
        {
        }

#if UNITY_EDITOR
        public RecordTrack(ResearchCategoryData category, List<string> memories, string sealedHuman, List<ResearchModifier> overwrite, string overwriteDescription)
        {
            _category = category;
            _memories = memories;
            _sealedHuman = sealedHuman;
            _overwriteModifiers = overwrite;
            _overwriteDescription = overwriteDescription;
        }
#endif
    }

    /// <summary>
    /// 13-5b 서사판 기록 복원 (사용자 결정 2026-10-18 D-7, REDESIGN V4): 연구 = 코어가 지구의 기록을 되살리는 일.
    /// 4갈래 × 3단계(1~3장), 3단계는 봉인 — 기록 조각으로 풀고, 끝나면 보존(기억) / 덮어쓰기(망각 · 효과 × 1.5)를 고른다.
    /// 1 · 2단계는 저절로 보존(기억 쪽 작게). 2장부터 열림. 14-1c (D-22): 4단계는 4장부터 · 봉인 없음 (저절로 보존). 값은 그레이박스 초안.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Record Config", fileName = "RecordConfig")]
    public sealed class RecordConfig : ScriptableObject
    {
        [Tooltip("메뉴 · 창 이름")]
        [SerializeField] private string _title = "기록 복원";
        [Tooltip("열리는 장 번호 (0 = 1장)")]
        [SerializeField, Min(0)] private int _openChapter = 1;
        [Tooltip("봉인된 단계 (갈래마다 같음)")]
        [SerializeField, Min(1)] private int _sealedLevel = 3;
        [Tooltip("봉인을 푸는 데 쓰는 기록 조각")]
        [SerializeField, Min(0)] private int _fragmentCost = 1;
        [Tooltip("14-1c 봉인 단계보다 높은 단계(4단계 ~)가 열리는 장 번호 (0 = 1장, 사용자 결정 2026-10-26 D-22: 4장)")]
        [SerializeField, Min(0)] private int _deepOpenChapter = 3;

        [Header("코어 성격 (기억 ↔ 망각)")]
        [Tooltip("1 · 2단계를 마침 (저절로 보존)")]
        [SerializeField] private List<AxisShift> _autoShifts = new List<AxisShift>();
        [SerializeField] private List<AxisShift> _preserveShifts = new List<AxisShift>();
        [SerializeField] private List<AxisShift> _overwriteShifts = new List<AxisShift>();

        [SerializeField] private List<RecordTrack> _tracks = new List<RecordTrack>();

        public string Title => _title;
        public int OpenChapter => _openChapter;
        public int SealedLevel => _sealedLevel;
        public int FragmentCost => _fragmentCost;
        public int DeepOpenChapter => _deepOpenChapter;
        public IReadOnlyList<AxisShift> AutoShifts => _autoShifts;
        public IReadOnlyList<AxisShift> PreserveShifts => _preserveShifts;
        public IReadOnlyList<AxisShift> OverwriteShifts => _overwriteShifts;
        public IReadOnlyList<RecordTrack> Tracks => _tracks;

        public RecordTrack Find(ResearchCategoryData category)
        {
            if (category == null)
                return null;
            foreach (var t in _tracks)
                if (t != null && t.Category == category)
                    return t;
            return null;
        }

#if UNITY_EDITOR
        public void EditorSet(List<RecordTrack> tracks, List<AxisShift> auto, List<AxisShift> preserve, List<AxisShift> overwrite)
        {
            _tracks = tracks;
            _autoShifts = auto;
            _preserveShifts = preserve;
            _overwriteShifts = overwrite;
        }
#endif
    }
}
