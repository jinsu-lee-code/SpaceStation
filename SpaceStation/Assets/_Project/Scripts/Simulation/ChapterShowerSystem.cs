using System;
using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 13-5c 서사판 장 유성우 규칙 (사용자 결정 2026-10-18). 유성우 자체는 공통 틀(<see cref="MeteorShowerSystem"/>) + 장 규칙(StationRule.MeteorShower)이고, 여기서는
    /// - 장이 시작되면 장 데이터대로 첫 유성우를 앞당김 (2장 = 3분 뒤 1파, 그 뒤는 공통 간격 7~10분 · 3파 — M-20 <see cref="MeteorShowerSystem.ScheduleNext"/>)
    /// - 평소 유성우를 파괴 없이 버티면 장 표시 'shower' (2장 필수 목표). 유성우가 끝난 뒤 남은 파손이 모두 고쳐지거나 파괴될 때까지(최대 파괴 유예 + 1초) 지켜봄.
    ///   예보만 하고 거둬진 유성우(절정이 대신 옴)와 특별 유성우(절정 · 포격)는 셈하지 않음.
    /// </summary>
    public sealed class ChapterShowerSystem : StationSystem
    {
        public const string SystemKey = "chapterShower";
        /// <summary>평소 유성우를 파괴 없이 버팀 (장 목표 표시).</summary>
        public const string ShowerFlag = "shower";

        private bool _watching;
        private bool _fell;
        private int _destroyedAtStart;
        private float _settle = -1f;
        private MeteorShowerSystem.ShowerPhase _lastPhase;

        public override string Key => SystemKey;

        private ChapterSystem Chapters => Sim.Chapters;

        protected override void OnAttached()
        {
            if (Chapters != null)
                Chapters.ChapterStarted += HandleChapterStarted;
        }

        private void HandleChapterStarted(int index)
        {
            var data = Chapters.Current;
            if (data.FirstShowerDelay > 0f)
                Sim.MeteorShower?.ScheduleNext(data.FirstShowerDelay, data.FirstShowerWaves);
        }

        public override void Tick(float dt)
        {
            var shower = Sim.MeteorShower;
            if (shower == null || Chapters == null)
                return;
            var phase = shower.Phase;
            if (!shower.IsRaid && !_watching && _settle < 0f && _lastPhase == MeteorShowerSystem.ShowerPhase.Idle && phase != MeteorShowerSystem.ShowerPhase.Idle)
            {
                _watching = true;
                _destroyedAtStart = Sim.Session.ModulesDestroyed;
            }
            if (_watching && _settle < 0f && shower.IsRaid)
                _watching = false; // 지켜보던 예보가 거둬지고 특별 유성우(절정)가 같은 틱에 시작됨
            if (_watching && _settle < 0f && phase == MeteorShowerSystem.ShowerPhase.Idle && _lastPhase != MeteorShowerSystem.ShowerPhase.Idle)
            {
                // 1파짜리는 한 틱 안에 예보 끝 → 낙하 → 끝이 될 수 있어 낙하 단계를 못 볼 수 있음 → 떨어진 파 수로 판단 (거둬진 예보는 0)
                _fell = shower.WavesDone > 0;
                if (_fell)
                    _settle = Sim.Balance.DestroyAfterSeconds + 1f;
                else
                    _watching = false; // 거둬진 예보
            }
            _lastPhase = phase;
            if (_settle < 0f)
                return;
            _settle -= dt;
            bool lost = Sim.Session.ModulesDestroyed > _destroyedAtStart;
            if (!lost && Sim.Damage.DamagedCount > 0 && _settle > 0f)
                return;
            _settle = -1f;
            _watching = false;
            if (Chapters.HasFlag(ShowerFlag))
                return;
            if (lost)
            {
                Sim.Report("유성우에 모듈을 잃었습니다 · 다음 유성우에서 다시", false);
                return;
            }
            Chapters.SetFlag(ShowerFlag);
            Sim.Report("유성우를 파괴 없이 버텼습니다", true);
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public bool Watching;
            public bool Fell;
            public int DestroyedAtStart;
            public float Settle = -1f;
            public int LastPhase;
        }

        public override string Capture(Func<ModuleInstance, int> indexOf) => JsonUtility.ToJson(new State
        {
            Watching = _watching,
            Fell = _fell,
            DestroyedAtStart = _destroyedAtStart,
            Settle = _settle,
            LastPhase = (int)_lastPhase,
        });

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            var s = string.IsNullOrEmpty(state) ? null : JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            _watching = s.Watching;
            _fell = s.Fell;
            _destroyedAtStart = s.DestroyedAtStart;
            _settle = s.Settle;
            _lastPhase = (MeteorShowerSystem.ShowerPhase)Mathf.Clamp(s.LastPhase, 0, 2);
        }
    }
}
