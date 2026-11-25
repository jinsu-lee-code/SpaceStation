using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>연결 변화 한 프레임 묶음의 소리 (끊김 · 다시 이어짐).</summary>
    public enum ConnectChange
    {
        None,
        Lost,
        Regained,
    }

    /// <summary>
    /// 바깥 알림 (사운드 개편 2026-11-26, SOUND_LIST_NEW 2번): 소리 없던 (또는 좋은 소식 · 경보를 빌려 쓰던) 바깥 공통 시스템의 소리를 고르는 규칙.
    /// 경보 위계와 겹치는 알림 (사회 위기 · 역병 발생 · 접촉 도착 · 유성우 예보 · 과열)은 <see cref="AlertSound.TryPlayAs"/>로 그 단계 자리에 뜻 소리를,
    /// 좋은 소식 (연구 완료 · 역병 끝남 · 다시 이어짐)은 위계 밖에서 작게 — 위험 이상 경보가 막 울렸으면 생략 (<see cref="PlayGood"/>).
    /// </summary>
    public static class NoticeSound
    {
        /// <summary>과열 경고 반복 간격 (실시간 초, D-2 — 열은 매 틱 다시 계산돼 여러 모듈이 차례로 위험 열에 들어감).</summary>
        public const float HeatRepeat = 30f;
        /// <summary>연결 끊김은 철거 소리 (철컥 · 쉬익) 뒤에, 다시 이어짐은 배치 · 통로 소리 (0.28초) 뒤에 — 같은 순간 겹치지 않게 순서대로.</summary>
        public const float DisconnectDelay = 0.35f;
        public const float ReconnectDelay = 0.55f;

        /// <summary>알던 상태 → 새 상태. 처음 보는 모듈 (새로 지음 · 불러옴)은 소리 없음.</summary>
        public static ConnectChange Classify(bool known, bool before, bool after)
        {
            if (!known || before == after)
                return ConnectChange.None;
            return after ? ConnectChange.Regained : ConnectChange.Lost;
        }

        /// <summary>한 프레임에 모인 변화 → 소리 하나 (D-2 "같은 때 여러 개 = 한 번"). 끊김이 이어짐보다 많거나 같으면 끊김 (나쁜 쪽을 알림).</summary>
        public static ConnectChange Pick(int lost, int regained)
        {
            if (lost <= 0 && regained <= 0)
                return ConnectChange.None;
            return lost >= regained ? ConnectChange.Lost : ConnectChange.Regained;
        }

        /// <summary>과열 경고: 위험 열에 새로 든 모듈이 있고 지난 경고에서 <see cref="HeatRepeat"/>초 지났을 때만 (플레이를 다시 켜 시계가 처음부터면 바로).</summary>
        public static bool HeatWarn(int fresh, float now, float lastAt) => fresh > 0 && (now - lastAt >= HeatRepeat || now < lastAt);

        /// <summary>낮밤 바뀜: prev = -1 모름 · 0 밤 · 1 낮 → 1 = 새벽 (밤에서 낮), -1 = 해 질 녘, 0 = 그대로 (처음 읽을 때도).</summary>
        public static int DayChange(int prev, bool day)
        {
            if (prev < 0 || (prev == 1) == day)
                return 0;
            return day ? 1 : -1;
        }

        /// <summary>외부 접촉 도착: 해독된 함선만 세력 신호 (상인 · 학술 · 무법), 정체 불명이면 주의 경보 그대로 — 소리로 정체가 새지 않게.</summary>
        public static SoundCue ContactArriveCue(SoundLibrary lib, ContactFaction faction, bool decoded)
        {
            if (lib == null)
                return null;
            if (!decoded)
                return lib.AlertCaution;
            switch (faction)
            {
                case ContactFaction.Merchant: return lib.ContactArrive1;
                case ContactFaction.Scholar: return lib.ContactArrive2;
                default: return lib.ContactArrive3;
            }
        }

        /// <summary>오는 방향 (월드) → 화면 좌우 (카메라 오른쪽과의 내적, 위 · 아래 방향은 가운데).</summary>
        public static float DirectionPan(Vector3 direction, Vector3 cameraRight, float amount = 0.6f)
        {
            if (direction.sqrMagnitude < 1e-6f)
                return 0f;
            var r = new Vector3(cameraRight.x, 0f, cameraRight.z);
            if (r.sqrMagnitude < 1e-6f)
                return 0f;
            return Mathf.Clamp(Vector3.Dot(direction.normalized, r.normalized) * amount, -amount, amount);
        }

        public static bool Play(SoundCue cue, float volume = 1f, float pan = 0f, float delay = 0f)
        {
            var s = AudioService.Instance;
            return s != null && cue != null && s.Play(cue, volume, pan, delay);
        }

        /// <summary>위계 밖 좋은 소식 · 알림 — 위험 이상 경보가 막 울렸으면 (<see cref="AlertSound.HigherHold"/>초 안) 생략.</summary>
        public static bool PlayGood(SoundCue cue, float delay = 0f)
            => !AlertSound.RecentAtLeast(AlertLevel.Danger) && Play(cue, 1f, 0f, delay);
    }

    /// <summary>
    /// 바깥 알림 중 시뮬레이션 이벤트 · 상태를 읽는 것 (연구 완료 · 연결 끊김 · 과열 · 낮밤) — GameAudio가 만들고 매 프레임 <see cref="Tick"/>.
    /// 시뮬레이션은 바꾸지 않음. 불러온 판의 상태는 처음 2초 동안 소리 없이 맞춤 (AlertWatch와 같음).
    /// </summary>
    internal sealed class NoticeWatch
    {
        private const float Quiet = 2f;
        private const float PruneEvery = 10f;

        private readonly StationSimulation _sim;
        private readonly float _bornAt;
        private ResearchExpansionSystem _expansion;
        private HeatSystem _heat;
        private readonly Dictionary<ModuleInstance, bool> _active = new Dictionary<ModuleInstance, bool>();
        private readonly List<ModuleInstance> _stale = new List<ModuleInstance>();
        private HashSet<ModuleInstance> _danger = new HashSet<ModuleInstance>();
        private HashSet<ModuleInstance> _dangerNow = new HashSet<ModuleInstance>();
        private int _lost, _regained;
        private float _heatWarnAt = -999f;
        private int _day = -1;
        private float _pruneAt;

        public NoticeWatch(StationSimulation sim)
        {
            _sim = sim;
            _bornAt = Time.unscaledTime;
            _pruneAt = _bornAt + PruneEvery;
        }

        private bool Quieted => Time.unscaledTime - _bornAt < Quiet || _sim.Session.IsGameOver;

        private static SoundLibrary Lib => AudioService.Instance != null ? AudioService.Instance.Library : null;

        public void Bind(bool on)
        {
            if (_sim == null)
                return;
            if (on)
            {
                _expansion = _sim.ResearchExpansion;
                _heat = _sim.Heat;
                _active.Clear();
                foreach (var m in _sim.Grid.Modules)
                    _active[m] = _sim.Connectivity.IsActive(m);
            }
            if (on)
            {
                _sim.Research.Completed += HandleResearch;
                _sim.Connectivity.ActiveStateChanged += HandleActive;
            }
            else
            {
                _sim.Research.Completed -= HandleResearch;
                _sim.Connectivity.ActiveStateChanged -= HandleActive;
            }
            if (_expansion != null)
            {
                if (on)
                {
                    _expansion.RecipeUnlocked += HandleRecipe;
                    _expansion.LinkedCompleted += HandleLinked;
                }
                else
                {
                    _expansion.RecipeUnlocked -= HandleRecipe;
                    _expansion.LinkedCompleted -= HandleLinked;
                }
            }
            if (_heat != null)
            {
                if (on) _heat.Changed += HandleHeat;
                else _heat.Changed -= HandleHeat;
            }
        }

        // ---------------- 연구 완료 (예전 연구 창이 좋은 소식 EventPositive 0.8을 빌림 · 레시피 · 연계 연구는 무음) ----------------

        private void HandleResearch(ResearchCategoryData category, int level) => Research();
        private void HandleRecipe(RecipeData recipe) => Research();
        private void HandleLinked(LinkedResearchData linked) => Research();

        private void Research()
        {
            if (!Quieted && Lib != null)
                NoticeSound.PlayGood(Lib.ResearchDone);
        }

        // ---------------- 연결 끊김 · 다시 이어짐 (CONNECT M9 — 끝쪽 모듈이 꺼짐 · 켜짐) ----------------

        private void HandleActive(ModuleInstance module, bool active)
        {
            if (module == null)
                return;
            bool known = _active.TryGetValue(module, out bool before);
            _active[module] = active;
            var change = NoticeSound.Classify(known, before, active);
            if (change == ConnectChange.Lost)
                _lost++;
            else if (change == ConnectChange.Regained)
                _regained++;
        }

        // ---------------- 과열 (대륙급 열 — 모듈이 위험 열에 들면 60초 뒤 파손) ----------------

        private void HandleHeat()
        {
            _dangerNow.Clear();
            int fresh = 0;
            if (_heat.Active)
            {
                foreach (var kv in _heat.HeatMap)
                {
                    var m = kv.Key;
                    if (m == _sim.Core || !_heat.IsDanger(m) || _sim.Damage.IsDamaged(m))
                        continue;
                    _dangerNow.Add(m);
                    if (!_danger.Contains(m))
                        fresh++;
                }
            }
            (_danger, _dangerNow) = (_dangerNow, _danger);
            float now = Time.unscaledTime;
            if (Quieted || Lib == null || !NoticeSound.HeatWarn(fresh, now, _heatWarnAt))
                return;
            if (AlertSound.TryPlayAs(AlertLevel.Caution, Lib.HeatWarning))
                _heatWarnAt = now;
        }

        // ---------------- 매 프레임 (GameAudio.Update) ----------------

        /// <param name="inside">1인칭 내부 — 낮밤 전환음은 바깥에서만</param>
        /// <param name="gameOver">결과 화면 — 모두 조용</param>
        public void Tick(bool inside, bool gameOver)
        {
            var lib = Lib;
            if (lib == null)
                return;
            if (_lost > 0 || _regained > 0)
            {
                var pick = NoticeSound.Pick(_lost, _regained);
                _lost = _regained = 0;
                if (!Quieted && !gameOver)
                {
                    if (pick == ConnectChange.Lost)
                        NoticeSound.PlayGood(lib.ModuleDisconnect, NoticeSound.DisconnectDelay);
                    else if (pick == ConnectChange.Regained)
                        NoticeSound.PlayGood(lib.ModuleReconnect, NoticeSound.ReconnectDelay);
                }
            }

            var dayNight = _sim.DayNight;
            if (dayNight != null && dayNight.Enabled)
            {
                bool day = dayNight.IsDay(_sim.ElapsedSeconds);
                int change = NoticeSound.DayChange(_day, day);
                _day = day ? 1 : 0;
                if (change != 0 && !Quieted && !inside && !gameOver)
                    NoticeSound.Play(change > 0 ? lib.Dawn : lib.Dusk);
            }

            if (Time.unscaledTime >= _pruneAt)
            {
                _pruneAt = Time.unscaledTime + PruneEvery;
                Prune();
            }
        }

        /// <summary>철거된 모듈의 연결 기록을 지움 (그리드에 없는 모듈).</summary>
        private void Prune()
        {
            _stale.Clear();
            foreach (var m in _active.Keys)
            {
                if (!_sim.Grid.TryGetModule(m.Origin, out var current) || current != m)
                    _stale.Add(m);
            }
            foreach (var m in _stale)
                _active.Remove(m);
        }
    }
}
