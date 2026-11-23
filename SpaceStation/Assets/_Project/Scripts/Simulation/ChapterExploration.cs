using System.Collections.Generic;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 13-5a 서사판 탐사를 공통 탐사 틀(<see cref="ExplorationSystem"/>, CONNECT_1121 M8) 위에 얹는 쪽:
    /// - 신호 = 장 데이터 (장마다 신호 종류 · 거리, 가로 4방향, 한 장에 탐사선 하나)
    /// - 장 표시: 밝히면 'probe', 닿으면 'reach' (2장 목표)
    /// - 난파선에 닿으면 기록 조각(13-5b <see cref="RecordArchiveSystem"/>) + 기록 로그 한 줄(코어 기록)
    /// 상태는 탐사 틀이 저장함 (이 쪽은 저장할 것 없음).
    /// </summary>
    public sealed class ChapterExploration : StationSystem, IExploreSignals
    {
        public const string SystemKey = "chapterExploration";
        public override string Key => SystemKey;

        private ExplorationSystem _explore;
        private static readonly List<SiteKind> Empty = new List<SiteKind>();

        private ChapterSystem Chapters => Sim.Chapters;

        protected override void OnAttached()
        {
            _explore = Sim.GetSystem<ExplorationSystem>();
            if (_explore == null)
            {
                Debug.LogWarning("[ChapterExploration] 탐사 틀이 먼저 붙어야 함");
                return;
            }
            _explore.Signals = this;
            _explore.Revealed += HandleRevealed;
            _explore.Reached += HandleReached;
        }

        // ---------------- 신호 = 장 데이터 ----------------

        public int Round => Chapters != null ? Chapters.Index : 0;
        public IReadOnlyList<SiteKind> Kinds => Chapters != null ? Chapters.Current.Signals : Empty;
        public int DistanceMin => Chapters != null ? Chapters.Current.SignalDistanceMin : 0;
        public int DistanceMax => Chapters != null ? Chapters.Current.SignalDistanceMax : 0;
        public IReadOnlyList<Vector3Int> Directions => null;

        public string RoundName(int round)
        {
            if (Chapters == null || round < 0 || round >= Chapters.Count)
                return "";
            return Chapters.GetChapter(round)?.DisplayName ?? "";
        }

        // ---------------- 장 표시 · 기록 ----------------

        private void HandleRevealed(ExploreSite site) => Chapters?.SetFlag(ExplorationSystem.ProbeFlag);

        private void HandleReached(ExploreSite site)
        {
            if (site.Kind == SiteKind.Wreck)
            {
                Sim.GetSystem<RecordArchiveSystem>()?.AddFragments(_explore.Config.WreckFragments); // 봉인된 기록을 푸는 데 씀
                string log = _explore.NextWreckLog();
                if (log != null)
                    Sim.GetSystem<CorePersonalitySystem>()?.Log("기록 조각 · " + log);
            }
            Chapters?.SetFlag(ExplorationSystem.ReachFlag);
        }
    }
}
