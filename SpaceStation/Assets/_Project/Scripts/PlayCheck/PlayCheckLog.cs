using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Interior;
using SpaceStation.Simulation;
using SpaceStation.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceStation.PlayCheck
{
    /// <summary>
    /// 플레이 기록 · 끝 질문 (CONNECT_1121 V3, 사용자 결정 2026-11-26): <b>검증용 빌드에서만</b> 켜진다 (<see cref="Enabled"/>).
    /// 앱이 켜질 때 저절로 하나 생겨(장면을 바꿔도 남음) 게임 장면에 붙고, 기록 파일을
    /// <c>{persistentDataPath}/PlayCheck/playcheck_{시각}.txt</c>에 1분마다 · 나갈 때 다시 쓴다 (검증용 빌드는 제품 이름이 달라 세이브 · 기록 폴더가 따로).
    /// 끝 질문 = 게임 시간 90분에 한 번 + 그 전에 메인 메뉴로 나가거나 끌 때 (<see cref="PlayCheckQuestions"/>), 답은 같은 파일에.
    /// </summary>
    public sealed class PlayCheckLog : MonoBehaviour
    {
        /// <summary>검증용 빌드의 제품 이름 (BuildSetup.PlayCheckProductName과 같아야 함).</summary>
        public const string ProductName = "Another Earth PlayCheck";
        /// <summary>끝 질문을 띄우는 게임 시간 (본 판 길이 90분, CONNECT_1121 V4-7).</summary>
        public const float QuestionGameSeconds = 90f * 60f;
        private const float SaveEveryRealSeconds = 60f;
        private const float SynthesisRecentGameSeconds = 120f;

        /// <summary>켜짐: 검증용 빌드(제품 이름) 또는 실행 인자 <c>-playcheck</c> (에디터 확인용).</summary>
        public static bool Enabled =>
            Application.productName == ProductName || Array.IndexOf(Environment.GetCommandLineArgs(), "-playcheck") >= 0 || ForceEnabled;

        /// <summary>에디터 · 시험에서 켜 보기.</summary>
        public static bool ForceEnabled;

        public static PlayCheckLog Instance { get; private set; }
        public PlayCheckRecord Record => _record;
        public string FilePath => _path;

        private PlayCheckRecord _record;
        private string _path;
        private float _nextSave;
        private bool _quitting;
        private bool _allowQuit;
        private readonly Dictionary<int, string> _gradeNames = new Dictionary<int, string>();

        // 붙은 게임 장면
        private StationController _station;
        private StationSimulation _sim;
        private SimulationClock _clock;
        private InteriorMode _interior;
        private float _lastSynthesis = float.NegativeInfinity;
        private bool _askedThisGame;
        private bool _gameUnanswered;
        private string _room;
        private PlayCheckQuestions _questions;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Enabled || Instance != null)
                return;
            var go = new GameObject("PlayCheckLog");
            DontDestroyOnLoad(go);
            go.AddComponent<PlayCheckLog>();
        }

        private void Awake()
        {
            Instance = this;
            string started = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            _record = new PlayCheckRecord(started, Application.version, g => _gradeNames.TryGetValue(g, out var n) ? n : g == 0 ? "없음" : g.ToString());
            string folder = Path.Combine(Application.persistentDataPath, "PlayCheck");
            Directory.CreateDirectory(folder);
            _path = Path.Combine(folder, $"playcheck_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            SceneManager.sceneLoaded += OnSceneLoaded;
            HudWindows.Opened += OnWindowOpened;
            InteriorIncidents.IncidentFinished += OnIncidentFinished;
            Application.wantsToQuit += OnWantsToQuit;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            Debug.Log($"[PlayCheck] 플레이 기록 켜짐 → {_path}");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            HudWindows.Opened -= OnWindowOpened;
            InteriorIncidents.IncidentFinished -= OnIncidentFinished;
            Application.wantsToQuit -= OnWantsToQuit;
            Unbind();
            if (Instance == this)
                Instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single)
                return;
            bool hadGame = _sim != null;
            Unbind();
            // 게임 장면에서만 붙는다 — 메인 메뉴 배경에도 진열용 정거장(StationController)이 있어, 그것을 게임으로 세면
            // 메인 메뉴로 나갈 때 끝 질문이 안 뜨고 판 수가 늘었음 (에디터 확인 2026-11-26)
            var station = scene.name == SceneNames.Game ? FindFirstObjectByType<StationController>() : null;
            if (station != null)
            {
                Bind(station);
            }
            else if (hadGame && _gameUnanswered)
            {
                ShowQuestions("메인 메뉴로 나감"); // 90분 전에 나감 → 메인 메뉴에서 묻는다
            }
            Save();
        }

        private void Bind(StationController station)
        {
            _station = station;
            _sim = station.Simulation;
            _clock = FindFirstObjectByType<SimulationClock>();
            _interior = FindFirstObjectByType<InteriorMode>();
            if (_sim == null)
                return;
            _askedThisGame = _sim.ElapsedSeconds >= QuestionGameSeconds; // 불러온 세이브가 이미 90분 넘음 → 나갈 때만
            _gameUnanswered = true;
            _lastSynthesis = float.NegativeInfinity;
            RememberGrade(_sim.Progression.GradeIndex);
            _sim.Progression.GradeChanged += OnGradeChanged;
            if (_sim.Synthesis != null)
                _sim.Synthesis.Completed += OnSynthesisCompleted;
            if (_interior != null)
                _interior.InsideChanged += OnInsideChanged;
            _record.BeginGame(_sim.ElapsedSeconds < 1f ? "새 게임" : $"불러온 게임 (게임 시간 {PlayCheckRecord.Clock(_sim.ElapsedSeconds)})",
                _sim.Progression.GradeIndex, _sim.ElapsedSeconds);
        }

        private void Unbind()
        {
            if (_sim != null)
            {
                _sim.Progression.GradeChanged -= OnGradeChanged;
                if (_sim.Synthesis != null)
                    _sim.Synthesis.Completed -= OnSynthesisCompleted;
            }
            if (_interior != null)
                _interior.InsideChanged -= OnInsideChanged;
            _station = null;
            _sim = null;
            _clock = null;
            _interior = null;
        }

        private void RememberGrade(int grade)
        {
            if (_sim != null && grade == _sim.Progression.GradeIndex)
            {
                // 기록은 "등급 소형"처럼 앞에 "등급"을 붙여 쓰므로 이름 앞의 "등급 "은 뗀다 ("등급 없음" → "없음", 빌드 확인 2026-11-27: "등급 등급 없음")
                string name = _sim.Progression.Current.DisplayName;
                _gradeNames[grade] = name.StartsWith("등급 ") ? name.Substring(3) : name;
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_sim != null && _station != null)
            {
                var clock = _clock != null ? _clock.Clock : null;
                bool paused = clock == null || clock.IsPaused;
                _record.Advance(dt, _sim.ElapsedSeconds, paused, clock != null ? clock.Speed : 1f);
                if (!_askedThisGame && _sim.ElapsedSeconds >= QuestionGameSeconds)
                {
                    _askedThisGame = true;
                    ShowQuestions("게임 시간 90분");
                }
            }
            else
            {
                _record.AdvanceMenu(dt); // 메인 메뉴 등 — 실제 시간만 (바깥 · 안 · 일시정지에 넣지 않음)
            }
            if (Time.unscaledTime >= _nextSave)
                Save();
        }

        private void OnGradeChanged(int previous, int next)
        {
            RememberGrade(next);
            _record.GradeChanged(next);
        }

        private void OnSynthesisCompleted(SynthesisJob job, ModuleInstance result)
        {
            _lastSynthesis = _sim != null ? _sim.ElapsedSeconds : 0f;
            _record.Note($"합성 끝: {(result != null && result.Data != null ? result.Data.DisplayName : "?")}");
        }

        private void OnInsideChanged(bool inside)
        {
            if (!inside)
            {
                _record.Exited(_room ?? "?");
                return;
            }
            var module = _interior != null ? _interior.EnteredModule : null;
            _room = module != null && module.Data != null ? module.Data.DisplayName : "?";
            var context = EntryContext.None;
            var detail = new List<string>();
            if (_sim != null)
            {
                int damaged = _sim.Damage.DamagedCount;
                if (damaged > 0)
                {
                    context |= EntryContext.Damaged;
                    detail.Add($"파손 {damaged}");
                }
                int crates = _sim.Supply.Crates.Count;
                if (crates > 0)
                {
                    context |= EntryContext.Crates;
                    detail.Add($"보급 상자 {crates}");
                }
                if (_sim.ElapsedSeconds - _lastSynthesis <= SynthesisRecentGameSeconds)
                {
                    context |= EntryContext.Synthesized;
                    detail.Add("합성 직후");
                }
            }
            _record.Entered(_room, context, string.Join(" · ", detail));
        }

        private void OnIncidentFinished(string title, bool resolved)
        {
            if (resolved)
                _record.IncidentResolved(title);
            else
                _record.IncidentMissed(title);
        }

        private void OnWindowOpened(object window)
        {
            if (window == null)
                return;
            string name = window.GetType().Name;
            if (name.EndsWith("Panel"))
                name = name.Substring(0, name.Length - "Panel".Length);
            _record.WindowOpened(name);
        }

        /// <summary>끄려 할 때: 이번 판 끝 질문에 아직 답하지 않았으면 먼저 묻고, 답(또는 건너뛰기) 뒤에 끈다.</summary>
        private bool OnWantsToQuit()
        {
            if (_allowQuit || _quitting)
            {
                Save();
                return true;
            }
            if (_sim != null && _gameUnanswered)
            {
                _quitting = true;
                ShowQuestions("게임을 끔");
                return false;
            }
            Save();
            return true;
        }

        private void ShowQuestions(string when)
        {
            if (_questions == null)
                _questions = PlayCheckQuestions.Create(transform);
            bool hadClock = _clock != null && _clock.Clock != null;
            bool wasPaused = hadClock && _clock.Clock.IsPaused;
            if (hadClock)
                _clock.Clock.SetPaused(true);
            bool wasBlocked = InputGate.Blocked;
            InputGate.Blocked = true; // 답을 적는 동안 게임 단축키가 먹지 않게
            _questions.Show(when, PlayCheckRecord.EndQuestions, answers =>
            {
                InputGate.Blocked = wasBlocked;
                if (answers != null)
                    _record.Answered(when, answers);
                else
                    _record.Note($"끝 질문 건너뜀 ({when})");
                _gameUnanswered = false;
                if (hadClock && _clock != null && _clock.Clock != null)
                    _clock.Clock.SetPaused(wasPaused);
                Save();
                if (_quitting)
                {
                    _allowQuit = true;
                    Application.Quit();
                }
            });
        }

        public void Save()
        {
            _nextSave = Time.unscaledTime + SaveEveryRealSeconds;
            try
            {
                File.WriteAllText(_path, _record.ToText(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")), new UTF8Encoding(true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayCheck] 기록 저장 실패: {e.Message}");
            }
        }
    }
}
