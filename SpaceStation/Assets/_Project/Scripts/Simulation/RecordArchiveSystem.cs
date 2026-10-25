using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>13-5b 봉인 단계를 마친 기록을 어떻게 남겼는지.</summary>
    public enum RecordChoice
    {
        /// <summary>아직 고르지 않음 (효과 대기).</summary>
        None,
        /// <summary>원본을 남김 — 기본 효과 + 기억 · 인간 미스터리 한 줄 + 기억 쪽.</summary>
        Preserve,
        /// <summary>지우고 공간을 씀 — 효과 × 1.5 + 망각 쪽.</summary>
        Overwrite,
    }

    /// <summary>
    /// 13-5b 서사판 기록 복원 (사용자 결정 2026-10-18 D-7, REDESIGN V4). 공통 연구 틀에 판 고리(<see cref="IResearchGate"/>, M-19)로 얹는다.
    /// - 2장부터 열림 (그 전엔 [관리]에서 숨김). 갈래 = 서사판 전용 연구 분야(RC_Record*), 이름은 "기록 복원"
    /// - 기록 조각: 난파선(탐사) · 절정을 깔끔히 버티면(장 데이터). 봉인 단계는 시작할 때 조각을 씀 (취소해도 돌려받지 않음)
    /// - 1 · 2단계를 마치면 저절로 보존: 코어 기록에 지구의 기억 한 줄 + 기억 쪽 작게
    /// - 봉인 단계를 마치면 효과 대기 → 화면이 보존(기억 · 인간 미스터리) / 덮어쓰기(효과 × 1.5 · 망각)를 고르게 함
    /// </summary>
    public sealed class RecordArchiveSystem : StationSystem, IResearchGate
    {
        public const string SystemKey = "records";
        /// <summary>13-5c: 기록 하나를 되살림 (장 목표 표시, 단계 상관없이).</summary>
        public const string RecordFlag = "record";

        private readonly RecordConfig _config;
        private readonly Dictionary<RecordTrack, RecordChoice> _choices = new Dictionary<RecordTrack, RecordChoice>();
        private static readonly IReadOnlyList<ResearchModifier> NoModifiers = new ResearchModifier[0];

        /// <summary>조각 수 · 선택이 바뀜.</summary>
        public event Action Changed;
        /// <summary>봉인 단계를 마쳐 고를 차례.</summary>
        public event Action<RecordTrack> ChoiceNeeded;

        public RecordArchiveSystem(RecordConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public override string Key => SystemKey;
        public RecordConfig Config => _config;
        /// <summary>지금 가진 기록 조각.</summary>
        public int Fragments { get; private set; }
        /// <summary>지금까지 얻은 기록 조각 (회고용).</summary>
        public int FragmentsFound { get; private set; }

        private ChapterSystem Chapters => Sim.Chapters;

        protected override void OnAttached()
        {
            Sim.Research.Gate = this;
            Sim.Research.Completed += HandleCompleted;
            if (Chapters != null)
            {
                Chapters.ChapterStarted += HandleChapterStarted;
                Chapters.ClimaxEnded += RewardClimax;
            }
        }

        // ---------------- 조각 ----------------

        public void AddFragments(int count, string report = null)
        {
            if (count <= 0)
                return;
            Fragments += count;
            FragmentsFound += count;
            if (!string.IsNullOrEmpty(report))
                Sim.Report(report, true);
            Changed?.Invoke();
        }

        /// <summary>절정이 끝남 → 깔끔히 버텼으면 그 장 데이터만큼 조각 (장 이벤트가 부름).</summary>
        public void RewardClimax(ChapterRecord record)
        {
            if (record == null || record.Outcome != ChapterOutcome.Clean || Chapters == null)
                return;
            var chapters = Chapters.Config.Chapters;
            if (record.Chapter < 0 || record.Chapter >= chapters.Count)
                return;
            int n = chapters[record.Chapter].CleanFragments;
            if (n > 0)
                AddFragments(n, $"위기를 깔끔히 넘김 · 기록 조각 +{n}");
        }

        private void HandleChapterStarted(int index)
        {
            if (index != _config.OpenChapter)
                return;
            Sim.Report($"{_config.Title}이 열렸습니다 · 연구소를 지으면 [관리] → {_config.Title}", true);
            Sim.GetSystem<CorePersonalitySystem>()?.Log($"{_config.Title} · 저장소 깊은 곳에 손상된 지구의 기록이 남아 있다. 되살릴 수 있다.");
            Changed?.Invoke();
        }

        // ---------------- 고리 (IResearchGate) ----------------

        public bool IsOpen => Chapters == null || Chapters.Index >= _config.OpenChapter;

        public string ClosedReason => $"{_config.Title}은 {_config.OpenChapter + 1}장에서 열립니다";

        public string Title => _config.Title;

        public string Status => $"기록 조각 {Fragments}";

        private bool IsSealed(RecordTrack track, int level) => track != null && level == _config.SealedLevel;

        public string BlockReason(ResearchCategoryData category, int level)
        {
            if (!IsOpen)
                return ClosedReason;
            var track = _config.Find(category);
            if (track != null && level > _config.SealedLevel && Chapters != null && Chapters.Index < _config.DeepOpenChapter)
                return $"{level}단계 기록은 {_config.DeepOpenChapter + 1}장에서 열립니다"; // 14-1c D-22
            if (IsSealed(track, level) && Fragments < _config.FragmentCost)
                return $"봉인된 기록 · 기록 조각 {_config.FragmentCost}개 필요 (보유 {Fragments})";
            return null;
        }

        public void OnStarted(ResearchCategoryData category, int level)
        {
            var track = _config.Find(category);
            if (!IsSealed(track, level))
                return;
            Fragments = Math.Max(0, Fragments - _config.FragmentCost);
            Sim.Report($"봉인된 기록을 풀기 시작 · 기록 조각 -{_config.FragmentCost}", false);
            Changed?.Invoke();
        }

        public IReadOnlyList<ResearchModifier> Modifiers(ResearchCategoryData category, int level, IReadOnlyList<ResearchModifier> defaults)
        {
            var track = _config.Find(category);
            if (!IsSealed(track, level))
                return defaults;
            switch (ChoiceOf(track))
            {
                case RecordChoice.Preserve: return defaults;
                case RecordChoice.Overwrite: return track.OverwriteModifiers;
                default: return NoModifiers; // 고를 때까지 효과 대기
            }
        }

        public string Note(ResearchCategoryData category, int level)
        {
            var track = _config.Find(category);
            if (track == null)
                return null;
            bool done = level <= Sim.Research.GetLevel(category);
            if (!done)
                return IsSealed(track, level)
                    ? $"<color=#FFD36A>봉인된 기록</color> · 기록 조각 {_config.FragmentCost}개로 풂 (보유 {Fragments}) · 끝나면 보존 / 덮어쓰기를 고름"
                    : null;
            if (!IsSealed(track, level))
                return $"<color=#B9A7FF>되살린 기억</color> {track.Memory(level)}";
            switch (ChoiceOf(track))
            {
                case RecordChoice.Preserve: return $"<color=#B9A7FF>보존한 기록</color> {track.Memory(level)}";
                case RecordChoice.Overwrite: return $"<color=#FF9F5A>덮어쓴 기록</color> 효과 × 1.5 · 원본은 지워짐";
                default: return "<color=#FFD36A>고를 차례</color> 보존 / 덮어쓰기를 고르면 효과가 켜집니다";
            }
        }

        /// <summary>
        /// 13-5c 연계 연구(12-4, 상위 모듈 필요) 칸은 합성이 열리는 장(4장)부터 보임 (사용자 결정 2026-10-18, M-21).
        /// 장 규칙이 없으면(시험 등) 그대로 보임.
        /// </summary>
        public bool ShowLinked => Sim.RuleOpen(StationRule.Synthesis, true);

        // ---------------- 완료 · 선택 ----------------

        public RecordChoice ChoiceOf(RecordTrack track) => track != null && _choices.TryGetValue(track, out var c) ? c : RecordChoice.None;

        /// <summary>봉인 단계를 마쳤는데 아직 고르지 않은 갈래 (없으면 null).</summary>
        public RecordTrack Pending
        {
            get
            {
                foreach (var t in _config.Tracks)
                    if (t != null && t.Category != null && Sim.Research.GetLevel(t.Category) >= _config.SealedLevel && ChoiceOf(t) == RecordChoice.None)
                        return t;
                return null;
            }
        }

        private void HandleCompleted(ResearchCategoryData category, int level)
        {
            var track = _config.Find(category);
            if (track == null)
                return;
            Chapters?.SetFlag(RecordFlag);
            var core = Sim.GetSystem<CorePersonalitySystem>();
            if (IsSealed(track, level))
            {
                Sim.Report($"{category.DisplayName} 봉인 해제 · 보존할지 덮어쓸지 고르세요", true);
                Changed?.Invoke();
                ChoiceNeeded?.Invoke(track);
                return;
            }
            core?.Apply(_config.AutoShifts, $"기록 복원 · {category.DisplayName} {level}단계");
            core?.Log($"기록 복원 · {category.DisplayName} {level}단계 · {track.Memory(level)}");
            Sim.Report($"{category.DisplayName} {level}단계 복원 · 효과 켜짐", true);
            Changed?.Invoke();
        }

        /// <summary>봉인 단계 선택 (이미 골랐거나 아직 마치지 않았으면 false).</summary>
        public bool Choose(RecordTrack track, bool preserve)
        {
            if (track == null || track.Category == null || ChoiceOf(track) != RecordChoice.None
                || Sim.Research.GetLevel(track.Category) < _config.SealedLevel)
                return false;
            _choices[track] = preserve ? RecordChoice.Preserve : RecordChoice.Overwrite;
            Sim.Research.RefreshEffects();
            string name = track.Category.DisplayName;
            var core = Sim.GetSystem<CorePersonalitySystem>();
            if (preserve)
            {
                core?.Apply(_config.PreserveShifts, $"봉인 기록 보존 · {name}");
                core?.Log($"봉인 기록 보존 · {name} · {track.Memory(_config.SealedLevel)}");
                if (!string.IsNullOrEmpty(track.SealedHuman))
                    core?.Log($"남겨진 기록 · {track.SealedHuman}");
                Sim.Report($"{name} · 원본을 보존 · 효과 켜짐", true);
            }
            else
            {
                core?.Apply(_config.OverwriteShifts, $"봉인 기록 덮어씀 · {name}");
                core?.Log($"봉인 기록 덮어씀 · {name} · 원본 기록을 지우고 공간을 씀");
                Sim.Report($"{name} · 기록을 덮어씀 · 효과 × 1.5", true);
            }
            Changed?.Invoke();
            return true;
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class ChoiceState
        {
            public string Category;
            public RecordChoice Choice;
        }

        [Serializable]
        private sealed class State
        {
            public int Fragments;
            public int Found;
            public List<ChoiceState> Choices = new List<ChoiceState>();
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            var s = new State { Fragments = Fragments, Found = FragmentsFound };
            foreach (var kv in _choices)
                if (kv.Key.Category != null)
                    s.Choices.Add(new ChoiceState { Category = kv.Key.Category.name, Choice = kv.Value });
            return JsonUtility.ToJson(s);
        }

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            var s = string.IsNullOrEmpty(state) ? null : JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            Fragments = Math.Max(0, s.Fragments);
            FragmentsFound = Math.Max(Fragments, s.Found);
            _choices.Clear();
            if (s.Choices != null)
                foreach (var c in s.Choices)
                    foreach (var t in _config.Tracks)
                        if (t != null && t.Category != null && t.Category.name == c.Category)
                            _choices[t] = c.Choice;
            Sim.Research.RefreshEffects(); // 연구 레벨은 먼저 복원됨 → 고른 효과로 다시
            Changed?.Invoke();
        }
    }
}
