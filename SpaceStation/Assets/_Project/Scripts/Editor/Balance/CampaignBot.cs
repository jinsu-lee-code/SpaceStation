using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// 13-8 준비 ① 캠페인 봇에 쓸 서사판 데이터 — 게임 씬 <see cref="SimulationHost"/>와 같은 구성 (종류마다 에셋 하나라 종류로 찾음).
    /// 장 = 노멀 적용본 (캠페인 = 단일 난이도 노멀, D-12), 연구 = 기록 4갈래 + 정비 자동화 (씬 연구 목록과 같음, 13-5b).
    /// </summary>
    public sealed class CampaignBotAssets
    {
        public ChapterConfig Chapters;
        public CoreConfig Core;
        public ArkShipConfig Ark;
        public ExplorationConfig Exploration;
        public RecordConfig Records;
        public SocietyConfig Society;
        public PolicyConfig Policies;
        public readonly List<ResearchCategoryData> Research = new List<ResearchCategoryData>();
        /// <summary>절정 대비에 짓는 모듈 (이민선 앞 빈 집 · 유성우 예보 방향 방어).</summary>
        public ModuleData Housing, Shield, Turret;

        private static readonly string[] ResearchFiles =
        {
            "Records/RC_RecordSurvival", "Records/RC_RecordStructure", "Records/RC_RecordDefense", "Records/RC_RecordLife", "RC_Automation",
        };

        public static CampaignBotAssets Load()
        {
            var a = new CampaignBotAssets
            {
                Chapters = Find<ChapterConfig>(c => c.Count > 0),
                Core = Find<CoreConfig>(),
                Ark = Find<ArkShipConfig>(),
                Exploration = Find<ExplorationConfig>(),
                Records = Find<RecordConfig>(),
                Society = Find<SocietyConfig>(),
                Policies = Find<PolicyConfig>(),
            };
            var normal = AssetDatabase.LoadAssetAtPath<DifficultyPreset>("Assets/_Project/Data/Difficulty/DIFF_Normal.asset");
            if (a.Chapters != null && normal != null)
                a.Chapters = normal.ApplyTo(a.Chapters);
            foreach (string file in ResearchFiles)
            {
                var c = AssetDatabase.LoadAssetAtPath<ResearchCategoryData>($"Assets/_Project/Data/Research/{file}.asset");
                if (c != null)
                    a.Research.Add(c);
            }
            a.Housing = AssetDatabase.LoadAssetAtPath<ModuleData>("Assets/_Project/Data/Modules/MD_Habitat.asset");
            a.Shield = AssetDatabase.LoadAssetAtPath<ModuleData>("Assets/_Project/Data/Modules/MD_Shield.asset");
            a.Turret = AssetDatabase.LoadAssetAtPath<ModuleData>("Assets/_Project/Data/Modules/MD_Turret.asset");
            return a;
        }

        private static T Find<T>(Func<T, bool> ok = null) where T : UnityEngine.Object
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && (ok == null || ok(asset)))
                    return asset;
            }
            return null;
        }
    }

    /// <summary>캠페인 봇 한 판의 장 기록 (시각 = 판 시작부터, 다시 시작으로 되돌아간 시간 포함).</summary>
    public sealed class CampaignChapterRun
    {
        public float Start = float.NaN;
        /// <summary>필수 목표를 다 채워 절정 예고가 시작된 때 (처음).</summary>
        public float Warning = float.NaN;
        public float Climax = float.NaN;
        /// <summary>절정이 끝난 때 (마지막 — 다시 시작했으면 성공한 시도).</summary>
        public float End = float.NaN;
        public bool HasOutcome;
        public ChapterOutcome Outcome;
        /// <summary>절정 결과 사유 한 줄 (결과 화면과 같음 — 막은 비율 · 받은 비율 등).</summary>
        public string Summary = "";
        public int Population = -1;
        /// <summary>이 장에서 게임 오버가 난 횟수 · 그중 체크포인트로 다시 시작한 횟수.</summary>
        public int Falls, Retries;
        /// <summary>절정 중 운석: 전체 · 격추 · 빗겨냄 · 면역 · 장갑 격벽이 대신 맞음 (판정은 둘 다 막음 — 사용자 결정 2026-10-25).</summary>
        public int ClimaxMeteors, ClimaxBlocked, ClimaxArmorHits;
        /// <summary>절정 예고 중에 평소 유성우가 떨어졌는지 (그 파손이 절정 동안 파괴로 넘어가면 절정 판정의 파괴로 셈).</summary>
        public bool ShowerInWarning;
    }

    /// <summary>캠페인 봇 한 판 결과 (<see cref="BalanceRunResult.Extra"/>).</summary>
    public sealed class CampaignBotStats
    {
        public CampaignChapterRun[] Chapters = new CampaignChapterRun[0];
        public bool Finished;
        public float FinishedAt = float.NaN;
        public bool GameOver;
        public int Accepted, Partial, Refused, Offered, Boarded;
        public float ProbeAt = float.NaN, PolicyAt = float.NaN;
        public string Choices = "";
        /// <summary>절정 대비로 지은 수: 이민선 앞 거주 · 유성우 예보 방향 실드 · 포탑.</summary>
        public int PrepHousing, PrepDefense;
        /// <summary>기록 연구 단계 (<see cref="CampaignBotAssets.Research"/> 순서 — 공통 요약의 연구 줄은 분야 번호가 겹쳐 서사판에서 틀림).</summary>
        public int[] ResearchLevels = new int[0];
    }

    /// <summary>
    /// 13-8 준비 ① 서사판 캠페인 봇 (사용자 결정 2026-10-25, S5-3): 공통 봇(건설 · 수리 · 정비 · 연구) 위에 게임과 같은 서사판 시스템을 붙이고
    /// 봇이 직접 해야 하는 것만 처리한다 (main M-46 고리).
    /// - 필수 목표만: 2장 센서 어레이를 짓고 탐사선으로 신호 밝히기 · 3장 정책 하나 시행 (등급 도달은 공통 봇, 코어 기동은 튜토리얼이 없어 저절로)
    /// - 이민선 답 = 봇 성향 (성장 쪽 모두 받기 · 가운데 빈 집만큼 · 안전 쪽은 위기 때 거부)
    /// - 장 끝 방향 · 3장 정책 = 시드마다 무작위 (같은 시드 = 같은 선택)
    /// - 실패 안전망 흉내: 장 시작 · 절정 예고 때 상태를 잡아 두고, 게임 오버면 절정 직전부터 → (두 번째 뒤에는) 장 처음부터, 장마다 3번까지
    /// - 절정 대비 (사용자 결정 2026-10-25, 첫 측정 뒤 · <see cref="Prepare"/>): 이민선 예보 · 대이주 예고 때 빈 집을 미리 (성향: 성장 쪽 전부 · 가운데 3/4 · 안전 쪽 절반),
    ///   유성우 예보 중 오는 방향 앞면을 가장 많이 덮는 자리에 실드 · 포탑 (예보 한 번에 평소 1 · 절정 2, 안전 쪽 +1)
    /// - 금속 관리 (같은 결정, main M-47 고리): 내구도 효율이 하는 일에 걸리지 않는 모듈(거주 · 배터리 · 창고 · 센서 …, 장갑 격벽은 빼고)은 늦게 정비 ·
    ///   재건축 (<see cref="IdleMaintainAt"/> · <see cref="IdleRebuildBelow"/>, 공통 봇은 모두 55 · 55 → 첫 측정에서 정비 · 재건축이 금속 수입을 거의 다 씀)
    /// </summary>
    public sealed class CampaignBotExtension : IBalanceExtension
    {
        /// <summary>장마다 다시 시작 상한 (넘으면 그 판 게임 오버).</summary>
        public const int RetryLimit = 3;
        private const float Accept = 0.33f;
        /// <summary>절정 대비 · 금속 관리를 하는지 (비교 측정용, 기본 켬 — 끄면 첫 측정과 같은 봇).</summary>
        public static bool Prepare = true;
        /// <summary>늦게 정비해도 되는 모듈(<see cref="Idle"/>): 이 내구도 아래면 정비 (운석 한 발 -20에도 0이 안 되게) · 정비 뒤 최대가 이 아래면 재건축.</summary>
        public const float IdleMaintainAt = 30f, IdleRebuildBelow = 45f;
        private const float PrepDefenseScore = 100f; // 덮는 앞면 모듈 하나당 (조밀 · 인접 점수보다 크게)

        private readonly CampaignBotAssets _a;
        private readonly CampaignBotStats _stats = new CampaignBotStats();
        private readonly List<ExploreSite> _sites = new List<ExploreSite>();
        private StationSimulation _sim;
        private ChapterSystem _chapters;
        private ArkShipSystem _ark;
        private ExplorationSystem _explore;
        private BalanceBotPersona _persona;
        private System.Random _choiceRng;
        private int _attempt;
        private float _now;
        private StationState _chapterCp, _climaxCp;
        private int _chapterCpIndex = -1, _climaxCpIndex = -1;
        private bool _wantChapterCp, _wantClimaxCp;
        private BalanceBot _policyBot;
        private bool _defendWarning;
        private int _defendBuilt;
        private readonly List<ModuleInstance> _facing = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _covered = new List<ModuleInstance>();

        public CampaignBotExtension(CampaignBotAssets assets) => _a = assets ?? throw new ArgumentNullException(nameof(assets));

        public bool Finished => _chapters != null && _chapters.Phase == ChapterPhase.Finished;

        public void Configure(StationSimulationSettings settings, int seed)
        {
            settings.Chapters = _a.Chapters;
            settings.Policies = _a.Policies;
        }

        /// <summary>게임 씬 <see cref="SimulationHost"/>와 같은 순서로 서사판 시스템을 붙임 (다시 시작이면 새 시뮬레이션에 또 — 상태는 그 뒤 복원).</summary>
        public void Attach(StationSimulation sim, int seed)
        {
            _sim = sim;
            if (_persona == null)
            {
                _persona = BalanceBot.Persona ? BalanceBotPersona.FromSeed(seed) : BalanceBotPersona.Neutral;
                _choiceRng = new System.Random(seed * 7349 + 101);
                int count = _a.Chapters != null ? _a.Chapters.Count : 0;
                _stats.Chapters = new CampaignChapterRun[count];
                for (int i = 0; i < count; i++)
                    _stats.Chapters[i] = new CampaignChapterRun();
                if (count > 0)
                    _stats.Chapters[0].Start = 0f;
                _wantChapterCp = true; // 새 캠페인 = 1장 처음
            }
            var arkRng = new System.Random(seed * 613 + 5 + _attempt * 7);
            var exploreRng = new System.Random(seed * 389 + 11 + _attempt * 13);
            if (_a.Core != null)
                sim.AddSystem(new CorePersonalitySystem(_a.Core), TickStepNames.Progression);
            _ark = _a.Ark != null && sim.Chapters != null ? sim.AddSystem(new ArkShipSystem(_a.Ark, () => (float)arkRng.NextDouble()), TickStepNames.Population) : null;
            _explore = _a.Exploration != null && sim.Chapters != null ? sim.AddSystem(new ExplorationSystem(_a.Exploration, () => (float)exploreRng.NextDouble()), TickStepNames.Population) : null;
            if (sim.Chapters != null)
                sim.AddSystem(new ChapterShowerSystem(), TickStepNames.Events);
            if (_a.Records != null && sim.Chapters != null)
                sim.AddSystem(new RecordArchiveSystem(_a.Records), TickStepNames.Research);
            if (sim.Policies != null && sim.Chapters != null)
                sim.AddSystem(new ChapterPolicySystem(_a.Society), TickStepNames.Population);
            if (_a.Society != null && sim.Chapters != null)
                sim.AddSystem(new TensionSystem(_a.Society), TickStepNames.Population);
            _chapters = sim.Chapters;
            if (_chapters != null)
            {
                _chapters.ChapterStarted += HandleChapterStarted;
                _chapters.WarningStarted += HandleWarning;
                _chapters.ClimaxStarted += HandleClimax;
                _chapters.ClimaxEnded += HandleClimaxEnded;
            }
            sim.MeteorResolved += HandleMeteor;
            _defendWarning = false;
            _attempt++;
        }

        public void Decide(BalanceBot bot, float elapsed)
        {
            _now = elapsed;
            if (_chapters == null)
                return;
            // 체크포인트는 그 사건이 난 틱 다음 결정 때 (다른 시스템이 같은 순간을 다 처리한 뒤 — 게임은 다음 프레임에 저장)
            if (_wantChapterCp)
            {
                _chapterCp = StationStateSerializer.Capture(_sim);
                _chapterCpIndex = _chapters.Index;
                _wantChapterCp = false;
            }
            if (_wantClimaxCp)
            {
                _climaxCp = StationStateSerializer.Capture(_sim);
                _climaxCpIndex = _chapters.Index;
                _wantClimaxCp = false;
            }
            if (_sim.Session.IsGameOver)
                return;
            if (_chapters.Phase == ChapterPhase.Transition)
            {
                Choose();
                return;
            }
            if (_chapters.Phase == ChapterPhase.Finished)
            {
                if (float.IsNaN(_stats.FinishedAt))
                    _stats.FinishedAt = elapsed;
                return;
            }
            var shower = _sim.MeteorShower;
            if (_chapters.Phase == ChapterPhase.Warning && shower != null && shower.Phase == MeteorShowerSystem.ShowerPhase.Falling && !shower.IsRaid && Current != null)
                Current.ShowerInWarning = true;
            AnswerShip();
            Explore(bot);
            EnactPolicy();
            if (!Prepare)
                return;
            UsePolicy(bot);
            PrepareHousing(bot);
            DefendShower(bot);
        }

        /// <summary>금속 관리: 이 봇(다시 시작이면 새 봇)에 정비 기준 고리를 한 번 꽂음.</summary>
        private void UsePolicy(BalanceBot bot)
        {
            if (_policyBot == bot)
                return;
            _policyBot = bot;
            bot.MaintainAtFor = m => Idle(m.Data) ? IdleMaintainAt : BalanceBot.DefaultMaintainAt;
            bot.RebuildBelowFor = m => Idle(m.Data) ? IdleRebuildBelow : BalanceBot.DefaultRebuildBelowMax;
        }

        /// <summary>늦게 정비해도 되는 모듈: 효율과 무관하고 장갑 격벽(운석을 대신 맞음 — 낮은 내구도로 두면 연달아 맞아 부서짐)이 아님.</summary>
        private static bool Idle(ModuleData data) => data != null && !BalanceBot.WearMatters(data) && !data.IsDecoy;

        /// <summary>
        /// 예보된 이민선(대이주 배 포함) 인원 · 대이주 예고 중이면 오는 배 전체 인원만큼 빈 집을 미리 (성향에 따라 그 일부, 결정마다 거주 1개).
        /// 거부할 배도 짓는다 — 답은 도착 때 정해지고, 안전 쪽은 위기일 때만 거부하므로.
        /// </summary>
        private void PrepareHousing(BalanceBot bot)
        {
            if (_ark == null || _a.Housing == null)
                return;
            int expected = _ark.NextShip != null ? _ark.NextShip.Size : 0;
            var chapter = _chapters.Current;
            if (chapter != null && chapter.Climax.Kind == ChapterClimaxKind.GreatMigration && _chapters.Phase == ChapterPhase.Warning)
            {
                var c = chapter.Climax;
                expected = Math.Max(expected, c.MigrationShips * (c.MigrationSizeMin + c.MigrationSizeMax) / 2);
            }
            if (expected <= 0)
                return;
            float share = _persona.Growth >= Accept ? 1f : _persona.Growth <= -Accept ? 0.5f : 0.75f;
            if (_ark.FreeHousing >= Mathf.CeilToInt(expected * share))
                return;
            if (bot.TryBuild(_a.Housing))
                _stats.PrepHousing++;
        }

        /// <summary>유성우 예보 중: 오는 방향 앞면 모듈을 아직 그 종류로 보호받지 않는 만큼 가장 많이 덮는 자리에 실드 또는 포탑 (2개 이상 덮을 때만).</summary>
        private void DefendShower(BalanceBot bot)
        {
            var shower = _sim.MeteorShower;
            if (shower == null || shower.Phase != MeteorShowerSystem.ShowerPhase.Warning)
            {
                _defendWarning = false;
                return;
            }
            if (!_defendWarning)
            {
                _defendWarning = true;
                _defendBuilt = 0;
            }
            int cap = (shower.IsRaid ? 2 : 1) + (_persona.Growth <= -Accept ? 1 : 0);
            if (_defendBuilt >= cap)
                return;
            _facing.Clear();
            foreach (var m in shower.FacingModules)
                _facing.Add(m);
            ModuleData pick = null;
            int bestOpen = 1;
            foreach (var data in new[] { _a.Shield, _a.Turret })
            {
                if (data == null || !_sim.CanAfford(data) || _sim.CheckBuildable(data) != PlacementResult.Valid)
                    continue;
                int open = 0;
                foreach (var m in _facing)
                    if (!Protected(data, m))
                        open++;
                if (open > bestOpen)
                {
                    bestOpen = open;
                    pick = data;
                }
            }
            if (pick == null)
                return;
            if (bot.TryBuild(pick, cells => FacingCovered(pick, cells) * PrepDefenseScore))
            {
                _defendBuilt++;
                _stats.PrepDefense++;
            }
        }

        private bool Protected(ModuleData data, ModuleInstance m) => data.IsShield
            ? _sim.Defense.GetShieldBlockChance(_sim.Grid, m) > 0.001f
            : _sim.Defense.GetInterceptChance(_sim.Grid, m) >= _sim.Effects.TurretMaxIntercept - 1e-3f;

        /// <summary>이 칸에 지으면 새로 덮는 앞면 모듈 수.</summary>
        private int FacingCovered(ModuleData data, IReadOnlyList<Vector3Int> cells)
        {
            DefenseSystem.CountCovered(_sim.Grid, data, cells, _covered, _sim.Effects.DefenseRadiusBonus);
            int n = 0;
            foreach (var m in _covered)
                if (_facing.Contains(m) && !Protected(data, m))
                    n++;
            return n;
        }

        /// <summary>절정 중 운석 집계 (격추 · 빗겨냄 · 면역 / 장갑 격벽이 대신 맞음 — 판정의 '막음'은 둘의 합).</summary>
        private void HandleMeteor(MeteorFlight flight)
        {
            if (_chapters == null || _chapters.Phase != ChapterPhase.Climax || flight == null)
                return;
            var c = Current;
            if (c == null)
                return;
            c.ClimaxMeteors++;
            if (flight.Hit == null || flight.Immune)
                c.ClimaxBlocked++;
            else if (flight.Hit.Data != null && flight.Hit.Data.IsDecoy)
                c.ClimaxArmorHits++;
        }

        /// <summary>장 끝 방향: 시드마다 무작위 (1~3장 방향은 코어 성향만 바꿈).</summary>
        private void Choose()
        {
            var choices = _chapters.Current.Choices;
            int pick = choices.Count > 0 ? _choiceRng.Next(choices.Count) : -1;
            if (_chapters.Choose(pick))
                _stats.Choices += (pick >= 0 ? choices[pick].Id : "-") + " ";
        }

        /// <summary>이민선 답 = 봇 성향: 성장 쪽 모두 받기 · 가운데 빈 집만큼 · 안전 쪽은 위기 때 거부 (그 밖엔 빈 집만큼).</summary>
        private void AnswerShip()
        {
            var ship = _ark?.Pending;
            if (ship == null)
                return;
            var answer = _persona.Growth >= Accept ? ShipAnswer.Accept
                : _persona.Growth <= -Accept && Crisis() ? ShipAnswer.Refuse
                : ShipAnswer.Partial;
            int boarding = _ark.Boarding(answer);
            if (!_ark.Answer(answer))
                return;
            _stats.Offered += ship.Size;
            _stats.Boarded += boarding;
            if (answer == ShipAnswer.Accept)
                _stats.Accepted++;
            else if (answer == ShipAnswer.Refuse)
                _stats.Refused++;
            else
                _stats.Partial++;
        }

        private bool Crisis()
        {
            var r = _sim.Resources;
            return _sim.Population.Satisfaction < _a.Ark.CrisisSatisfaction
                   || r.IsDepleted(ResourceType.Oxygen) || r.IsDepleted(ResourceType.Water) || r.IsDepleted(ResourceType.Food);
        }

        /// <summary>2장 필수 '탐사선으로 신호 밝히기': 센서 어레이가 없으면 짓고, 신호가 뜨면 첫 지점으로 탐사선.</summary>
        private void Explore(BalanceBot bot)
        {
            if (_explore == null || !_explore.ChapterHasSignals || _chapters.HasFlag(ExplorationSystem.ProbeFlag) || _explore.ProbeUsedThisChapter)
                return;
            if (!_explore.SensorActive)
            {
                if (!HasModule(_a.Exploration.Sensor))
                    bot.TryBuild(_a.Exploration.Sensor);
                return;
            }
            _explore.CurrentSites(_sites);
            foreach (var site in _sites)
            {
                if (_explore.ProbeBlocked(site) != null)
                    continue;
                if (_explore.LaunchProbe(site) && float.IsNaN(_stats.ProbeAt))
                    _stats.ProbeAt = _now;
                break;
            }
        }

        private bool HasModule(ModuleData data)
        {
            if (data == null)
                return true;
            foreach (var m in _sim.Grid.Modules)
                if (m.Data == data)
                    return true;
            return false;
        }

        /// <summary>3장 필수 '정책 하나 시행': 칸이 열리면 고를 수 있는 정책 중 시드마다 무작위 하나 (바꾸지 않음).</summary>
        private void EnactPolicy()
        {
            var policies = _sim.Policies;
            if (policies == null || policies.SlotCount <= 0)
                return;
            foreach (var slot in policies.Slots)
                if (slot.Active != null || slot.HasPending)
                    return;
            var options = new List<PolicyData>();
            foreach (var p in policies.Policies)
                if (policies.Offers(p) && policies.CheckAssign(0, p) == PolicySystem.AssignResult.Ok)
                    options.Add(p);
            if (options.Count == 0)
                return;
            if (policies.Assign(0, options[_choiceRng.Next(options.Count)]) == PolicySystem.AssignResult.Ok && float.IsNaN(_stats.PolicyAt))
                _stats.PolicyAt = _now;
        }

        // ---------------- 장 사건 (그 틱 안에서 불림 → 시각은 다음 결정 시각 근사) ----------------

        private CampaignChapterRun Current => _chapters.Index < _stats.Chapters.Length ? _stats.Chapters[_chapters.Index] : null;

        private void HandleChapterStarted(int index)
        {
            _wantChapterCp = true;
            if (index < _stats.Chapters.Length && float.IsNaN(_stats.Chapters[index].Start))
                _stats.Chapters[index].Start = _now + 1f;
        }

        private void HandleWarning()
        {
            _wantClimaxCp = true;
            var c = Current;
            if (c != null && float.IsNaN(c.Warning))
                c.Warning = _now + 1f;
        }

        private void HandleClimax()
        {
            var c = Current;
            if (c != null && float.IsNaN(c.Climax))
                c.Climax = _now + 1f;
        }

        private void HandleClimaxEnded(ChapterRecord record)
        {
            var c = record != null && record.Chapter < _stats.Chapters.Length ? _stats.Chapters[record.Chapter] : null;
            if (c == null)
                return;
            c.End = _now + 1f;
            c.HasOutcome = true;
            c.Outcome = record.Outcome;
            c.Summary = record.Summary ?? "";
            c.Population = _sim.Resources.Population;
        }

        /// <summary>게임 오버: 이 장 절정 예고 체크포인트 → (두 번 무너진 뒤엔) 이 장 처음 체크포인트, 장마다 <see cref="RetryLimit"/>번까지.</summary>
        public StationState RetryState()
        {
            var c = Current;
            if (c == null)
                return null;
            c.Falls++;
            if (c.Retries >= RetryLimit)
                return null;
            int index = _chapters.Index;
            StationState state = null;
            if (_climaxCp != null && _climaxCpIndex == index && c.Retries < RetryLimit - 1)
                state = _climaxCp;
            else if (_chapterCp != null && _chapterCpIndex == index)
                state = _chapterCp;
            if (state != null)
                c.Retries++;
            return state;
        }

        public void Finish(BalanceRunResult result)
        {
            _stats.Finished = Finished;
            _stats.GameOver = result.GameOver;
            if (_stats.Finished && float.IsNaN(_stats.FinishedAt))
                _stats.FinishedAt = result.EndSeconds;
            _stats.ResearchLevels = new int[_a.Research.Count];
            for (int i = 0; i < _a.Research.Count; i++)
                _stats.ResearchLevels[i] = _sim != null ? _sim.Research.GetLevel(_a.Research[i]) : 0;
            result.Extra = _stats;
        }
    }

    /// <summary>13-8 준비 ① 캠페인 봇 측정 (노멀 · 판마다 캠페인 끝 또는 상한까지) → BalanceReports 폴더에 공통 보고서 + campaign.txt · campaign.csv.</summary>
    public static class CampaignBotRunner
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [MenuItem("SpaceStation/Balance/Campaign Bot (노멀 20판 · 3장 끝)")]
        private static void RunMenu()
        {
            var (folder, summary) = Run();
            Debug.Log(summary);
            EditorUtility.RevealInFinder(Path.Combine(folder, "campaign.txt"));
        }

        public static (string folder, string summary) Run(int runs = 20, int baseSeed = 1, float capMinutes = 150f)
        {
            var assets = CampaignBotAssets.Load();
            if (assets.Chapters == null)
                throw new InvalidOperationException("장 데이터(ChapterConfig) 없음 — SpaceStation/Chapters/Setup Greybox 먼저");
            var s = BalanceSimulatorWindow.DefaultSettings(capMinutes, runs, baseSeed);
            s.ResearchCategories = assets.Research;
            s.SampleInterval = 30f;
            s.Extension = () => new CampaignBotExtension(assets);
            var report = BalanceRunner.Run(s);
            string folder = BalanceReportWriter.NewFolder();
            BalanceReportWriter.Write(folder, s, report, $"캠페인 봇 (노멀) · 상한 {capMinutes:0}분 × {runs}판");
            string text = Summary(report, assets, runs, capMinutes);
            var utf8 = new UTF8Encoding(true);
            File.WriteAllText(Path.Combine(folder, "campaign.txt"), text, utf8);
            File.WriteAllText(Path.Combine(folder, "campaign.csv"), Csv(report, assets), utf8);
            return (folder, text);
        }

        private static List<CampaignBotStats> Stats(BalanceReport report)
        {
            var list = new List<CampaignBotStats>();
            foreach (var r in report.Results)
                if (r.Extra is CampaignBotStats stats)
                    list.Add(stats);
            return list;
        }

        private static string Summary(BalanceReport report, CampaignBotAssets assets, int runs, float capMinutes)
        {
            var all = Stats(report);
            var sb = new StringBuilder();
            sb.AppendLine($"캠페인 봇 (노멀) · {runs}판 · 상한 {capMinutes:0}분 · 13-8 준비 ① (필수 목표만 · 이민선 = 봇 성향 · 방향 · 정책 = 시드 무작위 · 안전망 흉내 장마다 {CampaignBotExtension.RetryLimit}번"
                          + (CampaignBotExtension.Prepare ? " · 위기 대비 + 금속 관리 켬)" : " · 위기 대비 끔 = 첫 측정 봇)"));
            int finished = all.FindAll(x => x.Finished).Count;
            int gameOver = all.FindAll(x => x.GameOver).Count;
            var totals = new List<float>();
            foreach (var x in all)
                if (x.Finished)
                    totals.Add(x.FinishedAt);
            sb.AppendLine($"캠페인 끝 {finished}/{all.Count} · 안전망을 다 쓰고 게임 오버 {gameOver} · 상한에 걸림 {all.Count - finished - gameOver} · 끝까지 간 판 총 시간 {Spread(totals)}");
            sb.AppendLine("장별 (분, 평균 · 최소 ~ 최대) — 목표: 1장 약 15분 · 2 · 3장 약 25~35분");
            int count = assets.Chapters.Count;
            for (int i = 0; i < count; i++)
            {
                var prep = new List<float>();
                var climax = new List<float>();
                var whole = new List<float>();
                int clean = 0, hurt = 0, lost = 0, falls = 0, retries = 0, reached = 0;
                int meteors = 0, blocked = 0, armorHits = 0, showerInWarning = 0;
                var pop = new List<float>();
                foreach (var x in all)
                {
                    if (i >= x.Chapters.Length)
                        continue;
                    var c = x.Chapters[i];
                    if (!float.IsNaN(c.Start))
                        reached++;
                    if (!float.IsNaN(c.Start) && !float.IsNaN(c.Warning))
                        prep.Add(c.Warning - c.Start);
                    if (!float.IsNaN(c.Climax) && !float.IsNaN(c.End))
                        climax.Add(c.End - c.Climax);
                    if (!float.IsNaN(c.Start) && !float.IsNaN(c.End))
                        whole.Add(c.End - c.Start);
                    if (c.HasOutcome)
                    {
                        if (c.Outcome == ChapterOutcome.Clean)
                            clean++;
                        else if (c.Outcome == ChapterOutcome.Hurt)
                            hurt++;
                        else
                            lost++;
                        pop.Add(c.Population);
                    }
                    falls += c.Falls;
                    retries += c.Retries;
                    meteors += c.ClimaxMeteors;
                    blocked += c.ClimaxBlocked;
                    armorHits += c.ClimaxArmorHits;
                    if (c.ShowerInWarning)
                        showerInWarning++;
                }
                var data = assets.Chapters.Chapters[i];
                sb.AppendLine($"{data.DisplayName} {data.Subtitle}: 시작 {reached}판 · 준비(시작 → 예고) {Spread(prep)} · 위기 {Spread(climax)} · 장 전체 {Spread(whole)}");
                sb.AppendLine($"    결과 깔끔 {clean} · 피해 {hurt} · 잃음 {lost} · 무너짐 {falls}번 (다시 시작 {retries}번) · 장 끝 인구 {Spread(pop, minutes: false)}");
                if (meteors > 0)
                    sb.AppendLine($"    위기 중 운석 {meteors}개 · 막음 {blocked + armorHits} ({(blocked + armorHits) * 100f / meteors:0}%) = 실드 · 포탑 · 면역 {blocked} + 장갑 격벽이 대신 맞음 {armorHits}"
                                  + (showerInWarning > 0 ? $" · 위기 예고 중 평소 유성우 {showerInWarning}판" : ""));
            }
            int accepted = 0, partial = 0, refused = 0, offered = 0, boarded = 0, prepHousing = 0, prepDefense = 0;
            var probe = new List<float>();
            var policy = new List<float>();
            foreach (var x in all)
            {
                prepHousing += x.PrepHousing;
                prepDefense += x.PrepDefense;
                accepted += x.Accepted;
                partial += x.Partial;
                refused += x.Refused;
                offered += x.Offered;
                boarded += x.Boarded;
                if (!float.IsNaN(x.ProbeAt))
                    probe.Add(x.ProbeAt);
                if (!float.IsNaN(x.PolicyAt))
                    policy.Add(x.PolicyAt);
            }
            sb.AppendLine($"이민선 답: 모두 받기 {accepted} · 빈 집만큼 {partial} · 거부 {refused} · 받은 인원 {boarded}/{offered} ({(offered > 0 ? boarded * 100f / offered : 0f):0}%)");
            sb.AppendLine($"탐사선 출발 (판 시작부터) {Spread(probe)} · {probe.Count}판 / 정책 시행 {Spread(policy)} · {policy.Count}판");
            if (CampaignBotExtension.Prepare && all.Count > 0)
                sb.AppendLine($"위기 대비로 지음 (판 평균): 이민선 앞 거주 {prepHousing / (float)all.Count:0.0} · 유성우 방향 실드 · 포탑 {prepDefense / (float)all.Count:0.0}");
            sb.Append("기록 연구 최종 단계 평균 (공통 요약의 연구 줄 대신):");
            for (int i = 0; i < assets.Research.Count; i++)
            {
                var levels = new List<float>();
                foreach (var x in all)
                    if (i < x.ResearchLevels.Length)
                        levels.Add(x.ResearchLevels[i]);
                float avg = 0f;
                foreach (float v in levels)
                    avg += v;
                avg = levels.Count > 0 ? avg / levels.Count : 0f;
                sb.Append(i == 0 ? " " : " · ").Append(assets.Research[i].DisplayName).Append(' ').Append(avg.ToString("0.0", Inv));
            }
            sb.AppendLine();
            sb.AppendLine("위기 결과 사유 (판별):");
            foreach (var r in report.Results)
            {
                if (!(r.Extra is CampaignBotStats x))
                    continue;
                for (int i = 0; i < x.Chapters.Length; i++)
                    if (x.Chapters[i].HasOutcome)
                        sb.AppendLine($"    시드 {r.Seed} {i + 1}장 {x.Chapters[i].Outcome}: {x.Chapters[i].Summary}");
            }
            return sb.ToString();
        }

        private static string Spread(List<float> values, bool minutes = true)
        {
            if (values.Count == 0)
                return "-";
            float sum = 0f, min = float.MaxValue, max = float.MinValue;
            foreach (float v in values)
            {
                sum += v;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
            float k = minutes ? 1f / 60f : 1f;
            string f = minutes ? "0.0" : "0";
            return $"{(sum / values.Count * k).ToString(f, Inv)} ({(min * k).ToString(f, Inv)} ~ {(max * k).ToString(f, Inv)})";
        }

        private static string Csv(BalanceReport report, CampaignBotAssets assets)
        {
            var sb = new StringBuilder("seed,finished,game_over,finished_at_s,retries,ships_accept,ships_partial,ships_refuse,offered,boarded,probe_at_s,policy_at_s,choices,prep_housing,prep_defense");
            int count = assets.Chapters.Count;
            for (int i = 1; i <= count; i++)
                sb.Append($",ch{i}_start_s,ch{i}_warning_s,ch{i}_climax_s,ch{i}_end_s,ch{i}_outcome,ch{i}_falls,ch{i}_retries,ch{i}_population,ch{i}_climax_meteors,ch{i}_climax_blocked,ch{i}_climax_armor,ch{i}_shower_in_warning,ch{i}_summary");
            sb.Append('\n');
            foreach (var r in report.Results)
            {
                if (!(r.Extra is CampaignBotStats x))
                    continue;
                sb.Append(r.Seed).Append(',').Append(x.Finished ? 1 : 0).Append(',').Append(x.GameOver ? 1 : 0).Append(',').Append(F(x.FinishedAt)).Append(',')
                    .Append(r.Retries).Append(',').Append(x.Accepted).Append(',').Append(x.Partial).Append(',').Append(x.Refused).Append(',')
                    .Append(x.Offered).Append(',').Append(x.Boarded).Append(',').Append(F(x.ProbeAt)).Append(',').Append(F(x.PolicyAt)).Append(',')
                    .Append(x.Choices.Trim().Replace(' ', '|')).Append(',').Append(x.PrepHousing).Append(',').Append(x.PrepDefense);
                for (int i = 0; i < count; i++)
                {
                    var c = i < x.Chapters.Length ? x.Chapters[i] : new CampaignChapterRun();
                    sb.Append(',').Append(F(c.Start)).Append(',').Append(F(c.Warning)).Append(',').Append(F(c.Climax)).Append(',').Append(F(c.End))
                        .Append(',').Append(c.HasOutcome ? c.Outcome.ToString() : "").Append(',').Append(c.Falls).Append(',').Append(c.Retries).Append(',').Append(c.Population)
                        .Append(',').Append(c.ClimaxMeteors).Append(',').Append(c.ClimaxBlocked).Append(',').Append(c.ClimaxArmorHits).Append(',').Append(c.ShowerInWarning ? 1 : 0)
                        .Append(",\"").Append(c.Summary.Replace("\"", "'")).Append('"');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string F(float v) => float.IsNaN(v) ? "" : v.ToString("0", Inv);
    }
}
