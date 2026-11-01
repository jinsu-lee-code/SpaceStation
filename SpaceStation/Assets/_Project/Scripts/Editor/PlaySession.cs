using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Save;
using SpaceStation.Settings;
using SpaceStation.Simulation;
using SpaceStation.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 14-3 관문 (사용자 결정 2026-11-01): Claude가 직접 플레이하는 전용 명령 (봇이 아니라 사람 흉내 — 규칙 PLAYCHECK_14-3.md).
    /// 게임을 에디터 플레이 모드로 돌리면서 파일로 명령을 받아 게임과 같은 경로로 조작하고, 화면에 보이는 글만 돌려준다.
    /// 판 저장은 BalanceReports/play_14-3/Saves로 돌려 실제 세이브를 덮지 않음. 메뉴 SpaceStation/플레이 세션/켜기로 켜기 전에는 아무것도 하지 않음.
    ///
    /// 받는 파일: BalanceReports/play_14-3/cmd.txt (세미콜론으로 이어 쓴 명령) → 끝나면 report.txt
    /// 명령: start | wait | step 초 [기다림|바쁨] | b 이름 x y z [회전] | try 이름 x y z [회전] | spots 이름 [x y z] [개수] | sel x y z | desel
    ///       click 글 | click! 글 (창 결정 20초) | see | menu | info 이름 | mods | map [x y z 반경] | j | note #태그 글 | sum | shot 이름 | look x y z [거리] | save 이름 | end
    ///       adv (Space · 대사 다음 줄) | cam (카메라 조작) | goal 안내목표 | out (내부에서 나옴) | time 초 (사람 시간 더하기)
    ///       clickr 행글 [| 버튼글] (같은 글의 버튼이 여러 개일 때) | clickr! | d (바뀐 화면만) | dmg (파손 위치) | old (낡은 모듈) | load 칸 (메인 메뉴에서 불러오기)
    ///       syn 레시피 x y z [회전] | syntry 레시피 x y z [회전] | synspots 레시피 (합성 위치) | fix (파손 전부 수리 등록, 한 건 3초)
    /// </summary>
    [InitializeOnLoad]
    public static class PlaySession
    {
        private const string OnKey = "SpaceStation.PlaySession.On";
        private const string MenuScene = "Assets/_Project/Scenes/MainMenu.unity";
        private const float RunSpeed = 8f;
        private const float BuildCost = 8f, ClickCost = 5f, DecisionCost = 20f, SelectCost = 3f, ReadPerSecond = 10f, RepairCost = 3f;
        private const int GroupLineCap = 40;

        private static readonly Queue<string> Queue = new Queue<string>();
        private static readonly StringBuilder Out = new StringBuilder();
        private static readonly Dictionary<string, string> LastGroups = new Dictionary<string, string>();
        private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly List<string> Passing = new List<string>();
        private static readonly HashSet<string> PassingSeen = new HashSet<string>();
        private static readonly Dictionary<string, string> PassingLast = new Dictionary<string, string>();

        private static bool _busy, _stepping, _settling, _cycleOpen;
        private static double _waitUntil;
        private static double _lastReal;
        private static float _target, _start;
        private static string _tag = "";
        private static int _phaseAtStart, _chapterAtStart;
        private static bool _stopAlert;
        private static StationSimulation _hooked;

        private static int _cycle, _builds, _clicks;
        private static float _cycleCost, _pausedRead, _pausedScene;
        private static float _lastGame;
        private static readonly List<Row> Rows = new List<Row>();
        private static readonly Dictionary<string, int[]> NoteCounts = new Dictionary<string, int[]>();

        private sealed class Row
        {
            public int Chapter;
            public float Delta, ChapterSeconds, Read, Scene;
            public string Tag;
            public int Builds, Clicks;
        }

        static PlaySession()
        {
            if (UnityEditor.SessionState.GetBool(OnKey, false))
                EditorApplication.update += Pump;
        }

        private static string Folder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "BalanceReports", "play_14-3");
        private static string CmdPath => Path.Combine(Folder, "cmd.txt");
        private static string ReportPath => Path.Combine(Folder, "report.txt");

        [MenuItem("SpaceStation/플레이 세션/켜기 (메인 메뉴부터 플레이 시작)")]
        public static void TurnOn()
        {
            Directory.CreateDirectory(Folder);
            foreach (var old in new[] { CmdPath, ReportPath })
                if (File.Exists(old))
                    File.Delete(old);
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            UnityEditor.SessionState.SetBool(OnKey, true);
            if (EditorApplication.isPlaying)
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("SpaceStation/플레이 세션/끄기")]
        public static void TurnOff()
        {
            EditorApplication.update -= Pump;
            UnityEditor.SessionState.SetBool(OnKey, false);
        }

        // ---------------- 명령 받기 ----------------

        private static void Pump()
        {
            double now = EditorApplication.timeSinceStartup;
            double real = now - _lastReal;
            _lastReal = now;
            if (_stepping)
            {
                RunStep((float)real);
                return;
            }
            // 내가 판단하는 동안 시간이 흐르지 않게 (연출이 끝나며 시계를 되돌려도)
            if (EditorApplication.isPlaying)
            {
                var idle = ClockHost();
                if (idle != null && !idle.Clock.IsPaused)
                    idle.Clock.SetPaused(true);
            }
            if (now < _waitUntil)
                return;
            if (_settling)
            {
                _settling = false;
                FinishStep();
                return;
            }
            if (Queue.Count > 0)
            {
                string command = Queue.Dequeue();
                try
                {
                    Exec(command);
                }
                catch (Exception e)
                {
                    Out.AppendLine($"[오류] {command}: {e.GetType().Name} {e.Message}");
                }
                _waitUntil = now + 0.15;
                return;
            }
            if (_busy)
            {
                _busy = false;
                WriteReport();
                return;
            }
            if (!File.Exists(CmdPath))
                return;
            string text;
            try
            {
                text = File.ReadAllText(CmdPath, Encoding.UTF8);
                File.Delete(CmdPath);
            }
            catch (IOException)
            {
                return; // 아직 쓰는 중
            }
            Out.Clear();
            foreach (string part in text.Split(';'))
            {
                string c = part.Trim();
                if (c.Length > 0)
                    Queue.Enqueue(c);
            }
            _busy = true;
        }

        private static void WriteReport()
        {
            string tmp = ReportPath + ".tmp";
            File.WriteAllText(tmp, Out.ToString(), new UTF8Encoding(false));
            if (File.Exists(ReportPath))
                File.Delete(ReportPath);
            File.Move(tmp, ReportPath);
        }

        private static void Exec(string command)
        {
            var words = command.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            string head = words[0];
            string rest = command.Substring(head.Length).Trim();
            switch (head)
            {
                case "start": Start(); break;
                case "wait": BeginStep(0f, ""); break;
                case "step": BeginStep(ParseFloat(words, 1, 30f), words.Length > 2 ? words[2] : ""); break;
                case "b": Build(rest, false); break;
                case "try": Build(rest, true); break;
                case "spots": Spots(rest); break;
                case "sel": SelectAt(words); break;
                case "desel": Sel()?.Select(null); Out.AppendLine("선택 해제"); break;
                case "click": Click(rest, ClickCost); break;
                case "click!": Click(rest, DecisionCost); break;
                case "see": LastGroups.Clear(); Dump(true); break;
                case "menu": Menu(); break;
                case "info": Info(rest); break;
                case "mods": Mods(); break;
                case "map": Map(words); break;
                case "j": HeatMap(); break;
                case "note": Note(rest); break;
                case "sum": Out.Append(Summary()); break;
                case "shot": Shot(rest); break;
                case "look": Look(words); break;
                case "save": SaveAs(rest); break;
                case "end": End(); break;
                case "adv": Advance(); break;
                case "cam": Camera(); break;
                case "goal": InteriorGoal(rest); break;
                case "out": UnityEngine.Object.FindFirstObjectByType<SpaceStation.Interior.InteriorMode>()?.Exit(); Out.AppendLine("[내부] 나옴"); break;
                case "time": _pausedRead += ParseFloat(words, 1, 0f); Out.AppendLine($"[사람 시간] +{ParseFloat(words, 1, 0f):0}초"); break;
                case "clickr": ClickRow(rest, ClickCost); break;
                case "clickr!": ClickRow(rest, DecisionCost); break;
                case "fix": FixAll(); break;
                case "syn": Synth(rest, false); break;
                case "syntry": Synth(rest, true); break;
                case "synspots": SynthSpots(rest); break;
                case "clicki": ClickIndex(rest, ClickCost); break;
                case "d": Dump(false); break;
                case "dmg": Damaged(); break;
                case "old": Worn(); break;
                case "load": Out.AppendLine($"[불러오기] {rest} {SaveManager.LoadAndPlay(rest)}"); break;
                default: Out.AppendLine($"[모르는 명령] {head}"); break;
            }
        }

        private static float ParseFloat(string[] words, int index, float fallback)
            => words.Length > index && float.TryParse(words[index], out float v) ? v : fallback;

        // ---------------- 게임 찾기 ----------------

        private static SimulationHost Host() => UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
        private static StationSimulation Sim() { var h = Host(); return h != null ? h.Simulation : null; }
        private static SimulationClock ClockHost() => UnityEngine.Object.FindFirstObjectByType<SimulationClock>();
        private static StationController Station() => UnityEngine.Object.FindFirstObjectByType<StationController>();
        private static BuildController BuildCtl() => UnityEngine.Object.FindFirstObjectByType<BuildController>();
        private static ModuleSelectionController Sel() => UnityEngine.Object.FindFirstObjectByType<ModuleSelectionController>();

        private static void Hook(StationSimulation sim)
        {
            if (sim == null || sim == _hooked)
                return;
            if (_hooked != null)
                _hooked.EffectReported -= HandleReport;
            _hooked = sim;
            sim.EffectReported += HandleReport;
        }

        private static void HandleReport(string message, bool important)
        {
            string clean = Clean(message);
            if (clean.Length == 0)
                return;
            Passing.Add((important ? "! " : "") + clean);
            // 운석이 맞으면 사람은 바로 봄 (빨간 표식 · 소리) → 다음 주기를 기다리지 않음. 알림이 잦은 승진 등은 그대로 흘림
            if ((important || clean.Contains("충돌")) && _stepping)
                _stopAlert = true;
        }

        // ---------------- 시작 · 끝 ----------------

        /// <summary>메인 메뉴: 저장 폴더를 돌리고 캠페인 시작 창까지 (튜토리얼 켬 · 처음 하는 사람처럼).</summary>
        private static void Start()
        {
            SaveService.Directory = Path.Combine(Folder, "Saves");
            Directory.CreateDirectory(SaveService.Directory);
            LoadLedger();
            Out.AppendLine($"저장 폴더: {SaveService.Directory} · 튜토리얼 설정 지금 {GameSettings.TutorialPending} · 기록 {Rows.Count}주기 이어받음");
            Dump(true);
        }

        /// <summary>도구를 고치려고 플레이를 껐다 켜도 기록을 이어 감 (cycles.csv · notes.txt).</summary>
        private static void LoadLedger()
        {
            Rows.Clear();
            NoteCounts.Clear();
            string csv = Path.Combine(Folder, "cycles.csv");
            if (File.Exists(csv))
            {
                var lines = File.ReadAllLines(csv, Encoding.UTF8);
                for (int i = 1; i < lines.Length; i++)
                {
                    var f = lines[i].Split(',');
                    if (f.Length < 13)
                        continue;
                    Rows.Add(new Row
                    {
                        Chapter = int.Parse(f[1]) - 1,
                        Delta = float.Parse(f[4]),
                        ChapterSeconds = float.Parse(f[3]),
                        Tag = f[5],
                        Builds = int.Parse(f[6]),
                        Clicks = int.Parse(f[7]),
                        Read = float.Parse(f[9]),
                        Scene = float.Parse(f[10]),
                    });
                    _cycle = int.Parse(f[0]);
                    _lastGame = float.Parse(f[2]);
                }
            }
            string notes = Path.Combine(Folder, "notes.txt");
            if (!File.Exists(notes))
                return;
            foreach (string line in File.ReadAllLines(notes, Encoding.UTF8))
            {
                var words = line.Split(' ');
                if (words.Length < 3 || !words[2].StartsWith("#") || !int.TryParse(words[0].Replace("장", ""), out int chapter))
                    continue;
                if (!NoteCounts.TryGetValue(words[2], out var counts))
                    NoteCounts[words[2]] = counts = new int[16];
                if (chapter >= 1 && chapter <= counts.Length)
                    counts[chapter - 1]++;
            }
        }

        private static void End()
        {
            GameSettings.TutorialPending = false;
            Out.AppendLine("튜토리얼 설정 되돌림 (끔)");
            Out.Append(Summary());
        }

        // ---------------- 시간 ----------------

        private static void BeginStep(float seconds, string tag)
        {
            var clock = ClockHost();
            var sim = Sim();
            _tag = tag ?? "";
            _stopAlert = false;
            _stepping = true;
            _pausedSceneRun = false;
            _target = seconds;
            if (clock == null || sim == null)
            {
                _start = -1f; // 씬을 기다림
                return;
            }
            Hook(sim);
            _target = seconds; // 밀린 행동 시간은 한 번에 흘리지 않고 주기마다 갚음 (긴 주기 동안 파손을 놓치지 않게 — 11-02)
            _start = clock.Clock.SimulatedSeconds;
            _chapterAtStart = sim.Chapters != null ? sim.Chapters.Index : -1;
            _phaseAtStart = sim.Chapters != null ? (int)sim.Chapters.Phase : -1;
            if (seconds <= 0f || clock.InputLocked)
                return;
            clock.Clock.SetSpeed(RunSpeed);
            clock.Clock.SetPaused(false);
        }

        private static bool _busyNow;
        private static bool _pausedSceneRun;
        private static float _pausedAccum;

        private static double _nextPoll;

        private static void RunStep(float realDelta)
        {
            var clock = ClockHost();
            var sim = Sim();
            bool loading = SceneFader.Busy || clock == null || sim == null;
            if (!loading && clock.InputLocked && clock.Clock.IsPaused)
                _pausedAccum += realDelta;
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextPoll)
                return;
            _nextPoll = now + 0.1;
            CollectPassing();
            if (loading)
            {
                // 메뉴에서 게임 씬을 불러오는 중
                if (now > _waitUntil + 120.0)
                    EndRun();
                return;
            }
            Hook(sim);
            if (IntroPlaying())
            {
                if (IntroWaits())
                    EndRun();
                return; // 제목 카드 · 페이드는 저절로
            }
            if (StagePlaying() && !ModalOpen())
            {
                _pausedScene += _pausedAccum; // 연출: 건너뛰지 않고 실제 길이만큼 (사람도 봄) — 끝나며 시계가 되돌아올 때까지
                _pausedAccum = 0f;
                return;
            }
            bool control = AnyVisibleButton();
            if (_start < 0f)
            {
                _start = clock.Clock.SimulatedSeconds;
                _chapterAtStart = sim.Chapters != null ? sim.Chapters.Index : -1;
                _phaseAtStart = sim.Chapters != null ? (int)sim.Chapters.Phase : -1;
            }
            bool over = sim.Session.IsGameOver;
            if (clock.Clock.IsPaused || clock.InputLocked)
            {
                if (!control && !over)
                {
                    _pausedScene += _pausedAccum; // 연출: 건너뛰지 않고 실제 길이만큼 (사람도 봄)
                    _pausedAccum = 0f;
                    return;
                }
                _pausedAccum = 0f;
                EndRun();
                return;
            }
            _pausedAccum = 0f;
            if (!control)
                return; // 연출이 시작되는 사이
            bool phaseChanged = sim.Chapters != null && ((int)sim.Chapters.Phase != _phaseAtStart || sim.Chapters.Index != _chapterAtStart);
            if (clock.Clock.SimulatedSeconds - _start >= _target - 1e-3f || phaseChanged || _stopAlert || over)
                EndRun();
        }

        private static bool StagePlaying()
        {
            var transition = UnityEngine.Object.FindFirstObjectByType<ChapterTransitionView>();
            var ship = UnityEngine.Object.FindFirstObjectByType<ArkShipView>();
            return (transition != null && transition.Playing) || (ship != null && ship.Playing);
        }

        private static bool ModalOpen()
        {
            var view = UnityEngine.Object.FindFirstObjectByType<ChapterView>();
            return view != null && (bool)(typeof(ChapterView).GetField("_modalOpen", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view) ?? false);
        }

        private static void EndRun()
        {
            ClockHost()?.Clock.SetPaused(true);
            _stepping = false;
            _settling = true;
            _waitUntil = EditorApplication.timeSinceStartup + 0.35; // 창이 열리고 글이 바뀌는 프레임
        }

        private static void FinishStep()
        {
            var clock = ClockHost();
            var sim = Sim();
            float game = sim != null ? sim.ElapsedSeconds : 0f;
            if (game < _lastGame - 1f)
                _lastGame = game; // 재도전으로 시간이 되돌아감 (사람의 플레이 시간에는 그대로 쌓임)
            float delta = Mathf.Max(0f, game - _lastGame);
            _lastGame = game;
            int chapter = sim != null && sim.Chapters != null ? sim.Chapters.Index : -1;
            _cycle++;
            float chapterSeconds = sim != null && sim.Chapters != null ? sim.Chapters.ChapterSeconds : 0f;
            bool timeless = _target <= 0f; // wait: 시간이 흐르지 않았으니 행동 시간은 다음 주기로
            bool busy = !timeless && _cycleCost > _target + 0.5f;
            string tag = busy && _tag.Length == 0 ? "바쁨" : _tag;
            Rows.Add(new Row
            {
                Chapter = chapter,
                Delta = delta,
                ChapterSeconds = chapterSeconds,
                Read = _pausedRead,
                Scene = _pausedScene,
                Tag = tag,
                Builds = _builds,
                Clicks = _clicks,
            });
            AppendCsv(chapter, sim, game, delta, tag);
            float scene = _pausedScene;
            _builds = _clicks = 0;
            if (!timeless)
                _cycleCost = Mathf.Max(0f, _cycleCost - delta); // 일찍 멈췄거나 행동이 주기보다 길면 남은 시간은 다음 주기로
            _pausedRead = _pausedScene = 0f;
            _busyNow = busy;
            string phase = sim != null && sim.Chapters != null ? sim.Chapters.Phase.ToString() : "-";
            Out.AppendLine($"=== #{_cycle} {chapter + 1}장 {phase} · 장 {Clock(chapterSeconds)} · 총 {Clock(game)} · 이번 {delta:0}초{(scene > 0.5f ? $" · 연출 {scene:0}초" : "")}{(tag.Length > 0 ? " · " + tag : "")}{(_cycleCost > 0.5f ? $" · 밀린 행동 {_cycleCost:0}초" : "")}");
            FlushPassing();
            Dump(false);
            Unattended();
            AddModalReading();
        }

        private static void AppendCsv(int chapter, StationSimulation sim, float game, float delta, string tag)
        {
            string path = Path.Combine(Folder, "cycles.csv");
            if (!File.Exists(path))
                File.WriteAllText(path, "cycle,chapter,game,chapter_seconds,delta,tag,builds,clicks,cost,read,scene,population,grade\n", new UTF8Encoding(false));
            int pop = sim != null ? sim.Resources.Population : 0;
            int grade = sim != null ? sim.Progression.GradeIndex : 0;
            File.AppendAllText(path, $"{_cycle},{chapter + 1},{game:0},{(sim != null && sim.Chapters != null ? sim.Chapters.ChapterSeconds : 0f):0},{delta:0},{tag},{_builds},{_clicks},{_cycleCost:0},{_pausedRead:0},{_pausedScene:0},{pop},{grade}\n", new UTF8Encoding(false));
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.FloorToInt(seconds);
            return s >= 3600 ? $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }

        // ---------------- 행동 ----------------

        private static void Spend(float seconds)
        {
            var clock = ClockHost();
            if (clock == null || clock.InputLocked || clock.Clock.IsPaused && IntroPlaying())
                _pausedRead += seconds; // 창이 시간을 멈춘 동안 = 사람 시간만
            else
                _cycleCost += seconds;
        }

        private static ModuleData FindModule(string name)
        {
            var build = BuildCtl();
            if (build == null)
                return null;
            ModuleData partial = null;
            foreach (var m in build.BuildableModules)
            {
                if (m == null)
                    continue;
                if (m.DisplayName == name)
                    return m;
                if (partial == null && m.DisplayName.Contains(name))
                    partial = m;
            }
            return partial;
        }

        private static bool ParseNameAndCell(string rest, out ModuleData data, out Vector3Int cell, out int rotation, out string name)
        {
            data = null;
            cell = default;
            rotation = 0;
            var words = rest.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var nums = new List<int>();
            int i = words.Length - 1;
            for (; i >= 0 && int.TryParse(words[i], out int n); i--)
                nums.Insert(0, n);
            name = string.Join(" ", words, 0, i + 1);
            if (nums.Count < 3)
                return false;
            cell = new Vector3Int(nums[0], nums[1], nums[2]);
            rotation = nums.Count > 3 ? nums[3] : 0;
            data = FindModule(name);
            return data != null;
        }

        private static void Build(string rest, bool preview)
        {
            var station = Station();
            if (station == null)
            {
                Out.AppendLine("[건설] 게임 화면이 아님");
                return;
            }
            if (!ParseNameAndCell(rest, out var data, out var cell, out int rotation, out string name))
            {
                Out.AppendLine($"[건설] 못 찾음: {rest}");
                return;
            }
            if (!preview && !TutorialGate.Check(TutorialAction.Build, data))
            {
                Out.AppendLine($"[건설] {name}: {TutorialGate.Message}");
                return;
            }
            var result = station.EvaluatePlacement(data, cell, rotation);
            if (result != PlacementResult.Valid)
            {
                Out.AppendLine($"[건설] {data.DisplayName} @ {cell.x} {cell.y} {cell.z} r{rotation}: 안 됨 · {Clean(HudText.PlacementReason(result, station.Simulation.BuildZone))}");
                if (!preview)
                    Spend(BuildCost * 0.5f); // 사람도 빨간 고스트를 보고 다시 찾음
                return;
            }
            if (preview)
            {
                Out.AppendLine($"[미리보기] {data.DisplayName} @ {cell.x} {cell.y} {cell.z} r{rotation}: 됨 · 옆 {Neighbors(data, cell, rotation)}");
                return;
            }
            var build = BuildCtl();
            build.Select(data);
            if (station.TryPlace(data, cell, rotation, out var placed))
            {
                var placedEvent = typeof(BuildController).GetField("Placed", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(build) as Delegate;
                placedEvent?.DynamicInvoke(placed);
                _builds++;
                Spend(BuildCost);
                Out.AppendLine($"[건설] {data.DisplayName} @ {cell.x} {cell.y} {cell.z} r{rotation}");
            }
            else
            {
                Out.AppendLine($"[건설] {data.DisplayName}: 실패");
            }
            build.Select(null);
        }

        private static string Neighbors(ModuleData data, Vector3Int origin, int rotation)
        {
            var grid = Sim().Grid;
            var cells = StationGrid.ResolveCells(data.CellOffsets, origin, rotation);
            var names = new HashSet<string>();
            foreach (var c in cells)
                foreach (var d in GridDirections.Faces)
                    if (grid.TryGetModule(c + d, out var other) && other.Data != null)
                        names.Add(other.Data.DisplayName);
            return names.Count > 0 ? string.Join(", ", names) : "없음";
        }

        /// <summary>고스트를 움직여 초록 자리를 찾는 일 (사람은 눈으로) — 지정한 곳에서 가까운 순.</summary>
        private static void Spots(string rest)
        {
            var words = rest.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var nums = new List<int>();
            int i = words.Length - 1;
            for (; i >= 0 && int.TryParse(words[i], out int n); i--)
                nums.Insert(0, n);
            string name = string.Join(" ", words, 0, i + 1);
            var data = FindModule(name);
            var station = Station();
            if (data == null || station == null)
            {
                Out.AppendLine($"[자리] 못 찾음: {name}");
                return;
            }
            var sim = station.Simulation;
            var center = nums.Count >= 3 ? new Vector3Int(nums[0], nums[1], nums[2]) : (sim.Core != null ? sim.Core.Origin : Vector3Int.zero);
            int count = nums.Count == 1 ? nums[0] : nums.Count >= 4 ? nums[3] : 6;
            var seen = new HashSet<string>();
            var found = new List<(int dist, string text)>();
            foreach (var m in sim.Grid.Modules)
                foreach (var c in m.Cells)
                    foreach (var d in GridDirections.Faces)
                    {
                        var face = c + d;
                        if (sim.Grid.IsOccupied(face))
                            continue;
                        int dist = (face - center).sqrMagnitude;
                        for (int r = 0; r < 4; r++)
                        {
                            var origin = PlacementRules.AnchorOnFace(data, face, d, r);
                            if (!seen.Add($"{origin.x},{origin.y},{origin.z},{r}"))
                                continue;
                            if (station.EvaluatePlacement(data, origin, r) != PlacementResult.Valid)
                                continue;
                            found.Add((dist, $"{origin.x} {origin.y} {origin.z} r{r} (옆 {Neighbors(data, origin, r)})"));
                        }
                    }
            found.Sort((a, b) => a.dist.CompareTo(b.dist));
            Out.AppendLine($"[자리] {data.DisplayName} {found.Count}곳 · 가까운 {Mathf.Min(count, found.Count)}곳");
            for (int k = 0; k < count && k < found.Count; k++)
                Out.AppendLine("  " + found[k].text);
        }

        private static void SelectAt(string[] words)
        {
            var sim = Sim();
            var sel = Sel();
            if (sim == null || sel == null || words.Length < 4 || !int.TryParse(words[1], out int x) || !int.TryParse(words[2], out int y) || !int.TryParse(words[3], out int z))
            {
                Out.AppendLine("[선택] sel x y z");
                return;
            }
            if (!sim.Grid.TryGetModule(new Vector3Int(x, y, z), out var module))
            {
                Out.AppendLine($"[선택] {x} {y} {z}에 모듈 없음");
                return;
            }
            sel.Select(module);
            Spend(SelectCost);
            Out.AppendLine($"[선택] {module.Data.DisplayName} @ {module.Origin.x} {module.Origin.y} {module.Origin.z}");
        }

        private static void Click(string label, float cost)
        {
            Button best = null;
            string bestText = null;
            bool anyGroup = label == "재개" || label == "일시정지" || label == "x" || label == "바로 맞이하기";
            foreach (var b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!b.isActiveAndEnabled || !VisibleChain(b) || !CanBlock(b))
                    continue;
                string group = GroupOf(b);
                if (!anyGroup && (group.Contains("Time") || group.Contains("Marker") || group.Contains("Damage")))
                    continue; // 표식 라벨 "수리 24초" · 배속 "F3"를 잘못 누르지 않게
                string text = ButtonText(b);
                if (!text.Contains(label))
                    continue;
                if (best == null || text.Length < bestText.Length)
                {
                    best = b;
                    bestText = text;
                }
            }
            if (best == null)
            {
                Out.AppendLine($"[누름] 보이는 버튼 없음: {label}");
                return;
            }
            if (!best.interactable)
            {
                Out.AppendLine($"[누름] 꺼져 있음: {bestText}");
                return;
            }
            _clicks++;
            Spend(cost);
            best.onClick.Invoke();
            Out.AppendLine($"[누름] {bestText}");
        }

        /// <summary>같은 글의 버튼이 여러 개일 때: 그 행(부모)에 이 글이 있는 버튼 — clickr 행글 또는 clickr 행글 | 버튼글.</summary>
        private static void ClickRow(string rest, float cost)
        {
            var parts = rest.Split('|');
            string row = parts[0].Trim();
            string label = parts.Length > 1 ? parts[1].Trim() : "";
            foreach (var b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!b.isActiveAndEnabled || !VisibleChain(b) || !CanBlock(b) || b.transform.parent == null)
                    continue;
                if (label.Length > 0 && !ButtonText(b).Contains(label))
                    continue;
                bool hit = false;
                foreach (var t in b.transform.parent.GetComponentsInChildren<TMP_Text>(false))
                    hit |= Clean(t.text).Contains(row);
                if (!hit)
                    continue;
                if (!b.interactable)
                {
                    Out.AppendLine($"[누름] 꺼져 있음: {ButtonText(b)}");
                    return;
                }
                _clicks++;
                Spend(cost);
                b.onClick.Invoke();
                Out.AppendLine($"[누름] {row}: {ButtonText(b)}");
                return;
            }
            Out.AppendLine($"[누름] 보이는 행 없음: {rest}");
        }

        // ---------------- 합성 (사람은 레시피 창 [위치 지정] 뒤 고스트를 놓고 클릭) ----------------

        private static RecipeData FindRecipe(StationSimulation sim, string name)
        {
            if (sim?.Synthesis == null)
                return null;
            foreach (var r in sim.Synthesis.Recipes)
                if (r != null && r.DisplayName.Contains(name))
                    return r;
            return null;
        }

        private static void Synth(string rest, bool preview)
        {
            var sim = Sim();
            var words = rest.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var nums = new List<int>();
            int i = words.Length - 1;
            for (; i >= 0 && int.TryParse(words[i], out int n); i--)
                nums.Insert(0, n);
            string name = string.Join(" ", words, 0, i + 1);
            var recipe = FindRecipe(sim, name);
            if (recipe == null || nums.Count < 3)
            {
                Out.AppendLine($"[합성] 못 찾음: {rest}");
                return;
            }
            var origin = new Vector3Int(nums[0], nums[1], nums[2]);
            int rotation = nums.Count > 3 ? nums[3] : 0;
            if (preview)
            {
                var statuses = new List<IngredientStatus>();
                var result = sim.Synthesis.Evaluate(recipe, origin, rotation, null, statuses);
                var parts = new List<string>();
                foreach (var s in statuses)
                    parts.Add($"{(s.Module != null ? s.Module.DisplayName : "?")} {s.Present}/{s.Needed}");
                Out.AppendLine($"[합성 미리보기] {recipe.DisplayName} @ {origin.x} {origin.y} {origin.z} r{rotation}: {result} · {string.Join(", ", parts)}");
                return;
            }
            var controller = UnityEngine.Object.FindFirstObjectByType<SynthesisController>();
            controller?.Begin(recipe);
            var started = sim.Synthesis.TryStart(recipe, origin, rotation, out var job);
            if (started == SynthesisResult.Valid)
            {
                var startedEvent = typeof(SynthesisController).GetField("Started", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(controller) as Delegate;
                startedEvent?.DynamicInvoke(job);
                Spend(BuildCost);
                _builds++;
            }
            else
            {
                Spend(BuildCost * 0.5f);
            }
            controller?.Begin(null);
            Out.AppendLine($"[합성] {recipe.DisplayName} @ {origin.x} {origin.y} {origin.z} r{rotation}: {started}");
        }

        /// <summary>규격이 맞아떨어지는 자리 (사람은 고스트를 옮겨 초록이 뜨는 곳을 찾음).</summary>
        private static void SynthSpots(string name)
        {
            var sim = Sim();
            var recipe = FindRecipe(sim, name);
            if (recipe == null)
            {
                Out.AppendLine($"[합성 자리] 못 찾음: {name}");
                return;
            }
            var seen = new HashSet<string>();
            var found = new List<string>();
            foreach (var m in sim.Grid.Modules)
                foreach (var c in m.Cells)
                    foreach (var ing in recipe.Ingredients)
                    {
                        if (ing.Module != m.Data)
                            continue;
                        foreach (var offset in recipe.Result.CellOffsets)
                            for (int r = 0; r < 4; r++)
                            {
                                var cells = StationGrid.ResolveCells(recipe.Result.CellOffsets, Vector3Int.zero, r);
                                foreach (var rc in cells)
                                {
                                    var origin = c - rc;
                                    if (!seen.Add($"{origin.x},{origin.y},{origin.z},{r}"))
                                        continue;
                                    if (sim.Synthesis.Evaluate(recipe, origin, r) == SynthesisResult.Valid)
                                        found.Add($"{origin.x} {origin.y} {origin.z} r{r}");
                                }
                            }
                    }
            Out.AppendLine($"[합성 자리] {recipe.DisplayName} {found.Count}곳: " + string.Join(" | ", found.GetRange(0, Mathf.Min(8, found.Count))));
        }

        /// <summary>같은 글의 버튼이 한 행에 묶여 있을 때: 화면 위에서 아래로 n번째 (1부터) — clicki 글 n.</summary>
        private static void ClickIndex(string rest, float cost)
        {
            int cut = rest.LastIndexOf(' ');
            if (cut < 0 || !int.TryParse(rest.Substring(cut + 1), out int n))
            {
                Out.AppendLine("[누름] clicki 글 번호");
                return;
            }
            string label = rest.Substring(0, cut).Trim();
            var list = new List<Button>();
            foreach (var b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (b.isActiveAndEnabled && VisibleChain(b) && CanBlock(b) && ButtonText(b).Contains(label))
                    list.Add(b);
            list.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));
            if (n < 1 || n > list.Count)
            {
                Out.AppendLine($"[누름] {label} {list.Count}개뿐");
                return;
            }
            _clicks++;
            Spend(cost);
            list[n - 1].onClick.Invoke();
            Out.AppendLine($"[누름] {label} {n}번째");
        }

        /// <summary>
        /// 11-01 사용자 지적: 이 장르에 익숙한 사람은 파손 표식을 보면 바로 수리를 건다 (대기열에 넣으면 확산이 멈춤).
        /// 표식 클릭 + R = 한 건 3초. 급한 것부터, 장갑은 마지막 (방치해도 안 부서짐).
        /// </summary>
        private static void FixAll()
        {
            var sim = Sim();
            if (sim == null)
                return;
            var list = new List<(float urgent, ModuleInstance module)>();
            foreach (var m in sim.Grid.Modules)
            {
                if (!sim.Damage.IsDamaged(m) || !sim.Damage.TryGetInfo(m, out var info) || info.IsRepairing || info.IsQueued)
                    continue;
                bool armor = m.Data != null && m.Data.DisplayName.Contains("장갑");
                list.Add(((armor ? 10000f : 0f) + info.TimeUntilDestroyed, m));
            }
            list.Sort((a, b) => a.urgent.CompareTo(b.urgent));
            int started = 0, broke = 0;
            foreach (var (_, m) in list)
            {
                var result = sim.TryRepair(m);
                if (result == RepairResult.Started || result == RepairResult.Queued || result == RepairResult.Completed)
                {
                    started++;
                    Spend(RepairCost);
                }
                else if (result == RepairResult.InsufficientResources)
                {
                    broke++;
                }
            }
            Out.AppendLine($"[수리 등록] {started}건{(broke > 0 ? $" · 금속이 모자라 {broke}건 못 함" : "")}");
        }

        /// <summary>주기 보고에 항상: 수리를 걸지 않은 파손 (사람 눈에는 빨간 표식으로 늘 보임).</summary>
        private static void Unattended()
        {
            var sim = Sim();
            if (sim == null)
                return;
            var list = new List<string>();
            int working = 0;
            foreach (var m in sim.Grid.Modules)
            {
                if (!sim.Damage.IsDamaged(m) || !sim.Damage.TryGetInfo(m, out var info))
                    continue;
                if (info.IsRepairing || info.IsQueued)
                    working++;
                else
                    list.Add($"{m.Data.DisplayName}@{m.Origin.x},{m.Origin.y},{m.Origin.z}({info.TimeUntilDestroyed:0}초)");
            }
            if (list.Count > 0)
                Out.AppendLine($"[방치 {list.Count}] " + string.Join(" ", list));
            if (working > 0)
                Out.AppendLine($"[수리 중 · 대기 {working}]");
        }

        /// <summary>파손 표식 (사람은 화면의 빨간 표식으로 봄).</summary>
        private static void Damaged()
        {
            var sim = Sim();
            if (sim == null)
                return;
            var list = new List<string>();
            foreach (var m in sim.Grid.Modules)
                if (sim.Damage.IsDamaged(m))
                    list.Add($"{m.Data.DisplayName}@{m.Origin.x},{m.Origin.y},{m.Origin.z}");
            Out.AppendLine($"[파손 {list.Count}] " + string.Join(" ", list));
        }

        /// <summary>효율이 떨어진 낡은 모듈 (사람은 모듈 색 · 패널 수로 봄).</summary>
        private static void Worn()
        {
            var sim = Sim();
            if (sim == null)
                return;
            var list = new List<string>();
            foreach (var m in sim.Grid.Modules)
                if (sim.Durability.TryGetInfo(m, out var info) && sim.Durability.EfficiencyFor(info.Current) < 1f)
                    list.Add($"{m.Data.DisplayName}@{m.Origin.x},{m.Origin.y},{m.Origin.z}={info.Current:0}");
            Out.AppendLine($"[낡음 {list.Count}] " + string.Join(" ", list));
        }

        private static string ButtonText(Button b)
        {
            var parts = new List<string>();
            foreach (var t in b.GetComponentsInChildren<TMP_Text>(false))
            {
                string s = Clean(t.text);
                if (s.Length > 0)
                    parts.Add(s);
            }
            return string.Join(" / ", parts);
        }

        // ---------------- 키보드 · 마우스로만 하는 일 (사람은 Space · 클릭 · 카메라) ----------------

        private static ChapterIntroView Intro() => UnityEngine.Object.FindFirstObjectByType<ChapterIntroView>();

        private static bool IntroFlag(ChapterIntroView intro, string field)
            => (bool)(typeof(ChapterIntroView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(intro) ?? false);

        /// <summary>장 첫 대사가 다음 입력을 기다리는지.</summary>
        private static bool IntroWaits()
        {
            var intro = Intro();
            return intro != null && intro.Playing && !IntroFlag(intro, "_fading") && !IntroFlag(intro, "_titling");
        }

        private static bool IntroPlaying()
        {
            var intro = Intro();
            return intro != null && intro.Playing;
        }

        /// <summary>Space (장 첫 대사 다음 줄). 읽는 시간은 글자 수로.</summary>
        private static void Advance()
        {
            var intro = Intro();
            if (!IntroWaits())
            {
                Out.AppendLine("[다음] 기다리는 대사 없음");
                return;
            }
            typeof(ChapterIntroView).GetMethod("Next", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(intro, null);
            var line = typeof(ChapterIntroView).GetField("_line", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(intro) as TMP_Text;
            string text = line != null ? Clean(line.text) : "";
            _pausedRead += text.Length / ReadPerSecond;
            Out.AppendLine("[다음] " + text);
        }

        /// <summary>회전 · 확대 · 이동을 한 번씩 (안내 첫 단계, 사람 15초).</summary>
        private static void Camera()
        {
            var tutorial = Sim()?.Tutorial;
            if (tutorial == null)
                return;
            tutorial.ReportCamera(TutorialRunner.CameraUse.All);
            Spend(15f);
            Out.AppendLine("[카메라] 회전 · 확대 · 이동");
        }

        /// <summary>내부 방문처럼 1인칭 조작이 필요한 안내 단계를 끝난 것으로 알림 (걸린 시간은 time으로 따로 더함).</summary>
        private static void InteriorGoal(string name)
        {
            var tutorial = Sim()?.Tutorial;
            if (tutorial != null && Enum.TryParse(name, out TutorialGoal goal))
            {
                tutorial.ReportInterior(goal);
                Out.AppendLine($"[안내 단계] {goal}");
            }
        }

        private static void Note(string text)
        {
            int chapter = Sim()?.Chapters != null ? Sim().Chapters.Index : -1;
            float t = Sim()?.Chapters != null ? Sim().Chapters.ChapterSeconds : 0f;
            File.AppendAllText(Path.Combine(Folder, "notes.txt"), $"{chapter + 1}장 {Clock(t)} {text}\n", new UTF8Encoding(false));
            var words = text.Split(' ');
            if (words[0].StartsWith("#"))
            {
                if (!NoteCounts.TryGetValue(words[0], out var counts))
                    NoteCounts[words[0]] = counts = new int[16];
                if (chapter >= 0 && chapter < counts.Length)
                    counts[chapter]++;
            }
        }

        private static void Shot(string name)
        {
            string file = Path.Combine(Folder, (name.Length > 0 ? name : "shot") + ".png");
            ScreenCapture.CaptureScreenshot(file);
            _waitUntil = EditorApplication.timeSinceStartup + 1.0;
            Out.AppendLine($"[캡처] {file}");
        }

        private static void Look(string[] words)
        {
            var orbit = UnityEngine.Object.FindFirstObjectByType<OrbitCameraController>();
            if (orbit == null || words.Length < 4)
                return;
            var rig = orbit.Rig;
            float distance = words.Length > 4 ? ParseFloat(words, 4, rig.Distance) : rig.Distance;
            var focus = GridConfig.CellToWorld(new Vector3Int(int.Parse(words[1]), int.Parse(words[2]), int.Parse(words[3])));
            orbit.Restore(focus, rig.Yaw, rig.Pitch, distance);
            Out.AppendLine("[시점] 옮김");
        }

        private static void SaveAs(string slot)
        {
            var manager = UnityEngine.Object.FindFirstObjectByType<SaveManager>();
            bool ok = manager != null && manager.SaveTo(slot.Length > 0 ? slot : "play", false);
            Out.AppendLine($"[저장] {slot} {(ok ? "됨" : "실패")}");
        }

        // ---------------- 화면 읽기 ----------------

        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            return Tags.Replace(text, "").Replace('\r', ' ').Trim();
        }

        private static bool VisibleChain(Component c)
        {
            var canvas = c.GetComponentInParent<Canvas>();
            if (canvas == null || !canvas.enabled)
                return false;
            for (var p = c.transform; p != null; p = p.parent)
            {
                var g = p.GetComponent<CanvasGroup>();
                if (g == null)
                    continue;
                if (g.alpha < 0.05f)
                    return false;
                if (g.ignoreParentGroups)
                    break;
            }
            return true;
        }

        private static bool CanBlock(Component c)
        {
            for (var p = c.transform; p != null; p = p.parent)
            {
                var g = p.GetComponent<CanvasGroup>();
                if (g == null)
                    continue;
                if (!g.interactable || !g.blocksRaycasts)
                    return false;
                if (g.ignoreParentGroups)
                    break;
            }
            return true;
        }

        private static bool AnyVisibleButton()
        {
            foreach (var b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (b.isActiveAndEnabled && VisibleChain(b) && CanBlock(b) && !IsMenuCard(b))
                    return true;
            return false;
        }

        private static bool IsMenuCard(Component c) => GroupOf(c).Contains("Build");

        private static string GroupOf(Component c)
        {
            var canvas = c.GetComponentInParent<Canvas>();
            if (canvas == null)
                return "?";
            var root = canvas.rootCanvas.transform;
            var t = c.transform;
            while (t.parent != null && t.parent != root)
                t = t.parent;
            return t.name;
        }

        private static bool Silent(string group)
            => group.Contains("Build") || group.Contains("Marker") || group.Contains("Tooltip");

        private static readonly Dictionary<string, List<string>> GroupsScratch = new Dictionary<string, List<string>>();

        private static void CollectGroups()
        {
            GroupsScratch.Clear();
            foreach (var t in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!t.enabled || t.alpha < 0.05f || !VisibleChain(t))
                    continue;
                string s = Clean(t.text);
                if (s.Length == 0)
                    continue;
                string group = GroupOf(t);
                if (!GroupsScratch.TryGetValue(group, out var list))
                    GroupsScratch[group] = list = new List<string>();
                list.Add((t.transform.position.y, s).Item2);
            }
        }

        /// <summary>달리는 동안 잠깐 떴다 사라지는 글 (알림 · 자막 · 한마디): 숫자가 바뀌는 패널은 빼고, 타자 치듯 늘어나는 줄은 마지막 것만.</summary>
        private static void CollectPassing()
        {
            CollectGroups();
            foreach (var kv in GroupsScratch)
            {
                string g = kv.Key;
                if (Silent(g) || g.Contains("Resource") || g.Contains("Time") || g.Contains("Loading") || g.Contains("Launcher") || g.Contains("Tutorial") || g.Contains("Panel") || g.Contains("Selection") || g.Contains("Explore") || g.Contains("EarlyWarning") || g.Contains("ActiveEvent")
                    || g.Contains("Title") || g.Contains("Version") || (g.Contains("Chapter") && !g.Contains("Transition")))
                    continue;
                foreach (string line in kv.Value)
                {
                    if (line.Length < 4 || PassingSeen.Contains(line) || Regex.IsMatch(line, @"^\S*\d+[:.]?\d*\S*$"))
                        continue;
                    PassingLast.TryGetValue(g, out string last);
                    if (last != null && line.StartsWith(last) && Passing.Count > 0 && Passing[Passing.Count - 1] == last)
                        Passing[Passing.Count - 1] = line;
                    else
                        Passing.Add(line);
                    PassingSeen.Add(line);
                    PassingLast[g] = line;
                }
            }
        }

        private static void FlushPassing()
        {
            if (Passing.Count == 0)
                return;
            Out.AppendLine("[지나간 글]");
            int shown = 0;
            string previous = null;
            foreach (string line in Passing)
            {
                if (line == previous)
                    continue;
                previous = line;
                if (shown++ >= GroupLineCap)
                {
                    Out.AppendLine("  ...");
                    break;
                }
                Out.AppendLine("  " + line.Replace("\n", " / "));
            }
            Passing.Clear();
            if (PassingSeen.Count > 4000)
                PassingSeen.Clear();
        }

        /// <summary>바뀐 묶음만 (full이면 전부). 건설 메뉴 카드 · 표식은 빼고 묶어서.</summary>
        private static void Dump(bool full)
        {
            CollectGroups();
            var names = new List<string>(GroupsScratch.Keys);
            names.Sort(StringComparer.Ordinal);
            var markers = new Dictionary<string, int>();
            foreach (string g in names)
            {
                var lines = GroupsScratch[g];
                if (Silent(g))
                {
                    if (g.Contains("Marker"))
                        foreach (string l in lines)
                        {
                            markers.TryGetValue(l, out int n);
                            markers[l] = n + 1;
                        }
                    continue;
                }
                var counted = new List<string>();
                var counts = new Dictionary<string, int>();
                foreach (string l in lines)
                {
                    if (!counts.ContainsKey(l))
                        counted.Add(l);
                    counts.TryGetValue(l, out int n);
                    counts[l] = n + 1;
                }
                var sb = new StringBuilder();
                int shown = 0;
                foreach (string l in counted)
                {
                    if (shown++ >= GroupLineCap)
                    {
                        sb.AppendLine("  ...");
                        break;
                    }
                    sb.Append("  ").Append(l.Replace("\n", " / "));
                    if (counts[l] > 1)
                        sb.Append(" x").Append(counts[l]);
                    sb.AppendLine();
                }
                string text = sb.ToString();
                LastGroups.TryGetValue(g, out string old);
                if (!full && old == text)
                    continue;
                LastGroups[g] = text;
                Out.AppendLine($"[{g}]");
                Out.Append(text);
            }
            if (markers.Count > 0)
            {
                Out.Append("[표식] ");
                int shown = 0;
                foreach (var kv in markers)
                {
                    if (shown++ >= 12)
                    {
                        Out.Append(" ...");
                        break;
                    }
                    Out.Append(kv.Key.Replace("\n", " ")).Append(kv.Value > 1 ? $" x{kv.Value}" : "").Append(" | ");
                }
                Out.AppendLine();
            }
            var buttons = new List<string>();
            foreach (var b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!b.isActiveAndEnabled || !VisibleChain(b) || !CanBlock(b) || IsMenuCard(b))
                    continue;
                string text = ButtonText(b);
                if (text.Length == 0)
                    continue;
                buttons.Add((b.interactable ? "" : "(꺼짐)") + text.Replace("\n", " "));
            }
            buttons.Sort(StringComparer.Ordinal);
            string joined = string.Join(" | ", buttons);
            LastGroups.TryGetValue("#buttons", out string lastButtons);
            if (full || lastButtons != joined)
            {
                LastGroups["#buttons"] = joined;
                Out.AppendLine("[버튼] " + joined);
            }
        }

        /// <summary>창이 시간을 멈췄으면 읽는 시간을 사람 시간에 더함.</summary>
        private static void AddModalReading()
        {
            if (!ModalOpen())
                return;
            var view = UnityEngine.Object.FindFirstObjectByType<ChapterView>();
            int chars = 0;
            foreach (string field in new[] { "_modalTitle", "_modalBody" })
                if (typeof(ChapterView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view) is TMP_Text t)
                    chars += Clean(t.text).Length;
            _pausedRead += chars / ReadPerSecond; // 창 글만 (전에는 화면 글 전부를 세어 3장 읽기 시간이 부풀었음 - 요약 때 보정)
        }

        // ---------------- 메뉴 · 정거장 ----------------

        private static void Menu()
        {
            var build = BuildCtl();
            var station = Station();
            if (build == null || station == null)
                return;
            var byCategory = new SortedDictionary<string, List<string>>();
            foreach (var m in build.BuildableModules)
            {
                if (m == null || !build.Revealed(m))
                    continue;
                var state = station.CheckBuildable(m);
                string line = m.DisplayName + " " + CostText(m) + (state != PlacementResult.Valid ? " (" + Clean(HudText.PlacementReason(state, station.Simulation.BuildZone)) + ")" : "");
                string key = m.Category.ToString();
                if (!byCategory.TryGetValue(key, out var list))
                    byCategory[key] = list = new List<string>();
                list.Add(line);
            }
            foreach (var kv in byCategory)
                Out.AppendLine($"[{kv.Key}] " + string.Join(" | ", kv.Value));
        }

        private static string CostText(ModuleData m)
        {
            var parts = new List<string>();
            foreach (var c in m.BuildCost)
                parts.Add($"{c.Type} {c.Amount:0}");
            return string.Join("+", parts);
        }

        private static void Info(string name)
        {
            var data = FindModule(name);
            var sim = Sim();
            if (data == null || sim == null)
            {
                Out.AppendLine($"[정보] 못 찾음: {name}");
                return;
            }
            Out.AppendLine(Clean(HudText.ModuleTooltip(data, sim.Effects)));
            var rules = new List<string>();
            sim.Adjacency.DescribeRulesFor(data, rules);
            foreach (string r in rules)
                Out.AppendLine(Clean(r));
        }

        private static void Mods()
        {
            var sim = Sim();
            if (sim == null)
                return;
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int damaged = 0;
            foreach (var m in sim.Grid.Modules)
            {
                string n = m.Data != null ? m.Data.DisplayName : "?";
                counts.TryGetValue(n, out int c);
                counts[n] = c + 1;
                if (sim.Damage.IsDamaged(m))
                    damaged++;
            }
            var parts = new List<string>();
            foreach (var kv in counts)
                parts.Add($"{kv.Key} {kv.Value}");
            Out.AppendLine($"[모듈] 파손 {damaged} · " + string.Join(" · ", parts));
        }

        private static readonly Dictionary<string, string> Codes = new Dictionary<string, string>();

        private static string CodeOf(ModuleData data)
        {
            string name = data != null ? data.DisplayName : "?";
            if (Codes.TryGetValue(name, out string code))
                return code;
            foreach (char ch in name)
            {
                string candidate = ch.ToString();
                if (ch != ' ' && !Codes.ContainsValue(candidate))
                {
                    Codes[name] = candidate;
                    return candidate;
                }
            }
            code = "#" + Codes.Count;
            Codes[name] = code;
            return code;
        }

        /// <summary>층마다 칸 지도 (사람은 3D 화면으로 보는 것). x → 오른쪽, z → 위. 대문자 없이 모듈 이름의 한 글자, ! = 파손.</summary>
        private static void Map(string[] words)
        {
            var sim = Sim();
            if (sim == null)
                return;
            var cells = new Dictionary<Vector3Int, ModuleInstance>();
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            bool local = words.Length >= 5;
            var center = local ? new Vector3Int(int.Parse(words[1]), int.Parse(words[2]), int.Parse(words[3])) : Vector3Int.zero;
            int radius = local ? int.Parse(words[4]) : 0;
            var used = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var m in sim.Grid.Modules)
                foreach (var c in m.Cells)
                {
                    if (local && (Mathf.Abs(c.x - center.x) > radius || Mathf.Abs(c.y - center.y) > radius || Mathf.Abs(c.z - center.z) > radius))
                        continue;
                    cells[c] = m;
                    minX = Mathf.Min(minX, c.x);
                    maxX = Mathf.Max(maxX, c.x);
                    minY = Mathf.Min(minY, c.y);
                    maxY = Mathf.Max(maxY, c.y);
                    minZ = Mathf.Min(minZ, c.z);
                    maxZ = Mathf.Max(maxZ, c.z);
                    used[CodeOf(m.Data)] = m.Data != null ? m.Data.DisplayName : "?";
                }
            if (cells.Count == 0)
            {
                Out.AppendLine("[지도] 비어 있음");
                return;
            }
            var legend = new List<string>();
            foreach (var kv in used)
                legend.Add($"{kv.Key}={kv.Value}");
            Out.AppendLine("[범례] " + string.Join(" ", legend));
            for (int y = maxY; y >= minY; y--)
            {
                Out.AppendLine($"y={y} (x {minX}..{maxX})");
                for (int z = maxZ; z >= minZ; z--)
                {
                    var row = new StringBuilder();
                    bool any = false;
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (cells.TryGetValue(new Vector3Int(x, y, z), out var m))
                        {
                            row.Append(CodeOf(m.Data)).Append(sim.Damage.IsDamaged(m) ? "!" : "");
                            any = true;
                        }
                        else
                        {
                            row.Append('.');
                        }
                    }
                    if (any)
                        Out.AppendLine($"z={z,3} {row}");
                }
            }
        }

        /// <summary>J 지도와 같은 내용 (한도를 넘는 모듈 · 방사선 높은 집).</summary>
        private static void HeatMap()
        {
            var sim = Sim();
            var view = UnityEngine.Object.FindFirstObjectByType<HeatMapView>();
            if (sim == null || view == null)
                return;
            var field = typeof(HeatMapView).GetField("_mapOn", BindingFlags.Instance | BindingFlags.NonPublic);
            var dirty = typeof(HeatMapView).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(view, true);
            dirty?.SetValue(view, true);
            sim.Tutorial?.ReportEnvironmentMap();
            var heat = sim.Heat;
            var over = new List<string>();
            foreach (var kv in heat.HeatMap)
                if (heat.IsOverheated(kv.Key))
                    over.Add($"{(heat.IsDanger(kv.Key) ? "위험 " : "")}{kv.Key.Data.DisplayName}@{kv.Key.Origin.x},{kv.Key.Origin.y},{kv.Key.Origin.z}={kv.Value:0.0}");
            Out.AppendLine($"[J 열] 활성 {heat.Active} · 한도 {heat.Limit:0} · 위험 {heat.DangerLevel:0} · 과열 {over.Count}: " + string.Join(" ", over));
            var radiation = sim.Radiation;
            var roster = sim.Residents;
            if (radiation != null && radiation.Active && roster != null)
            {
                var high = new List<string>();
                foreach (var home in roster.Homes)
                {
                    float level = radiation.LevelOf(home);
                    if (level > radiation.Comfort)
                        high.Add($"{home.Origin.x},{home.Origin.y},{home.Origin.z}={level:0.0}");
                }
                Out.AppendLine($"[J 방사선] 한도 {radiation.Comfort:0} · 높은 집 {high.Count}: " + string.Join(" ", high));
            }
        }

        // ---------------- 요약 ----------------

        private static string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[요약] 장 | 게임시간 | 기다림 | 바쁨(주기) | 건설 | 클릭 | 읽기·결정(멈춤) | 연출 | 실제시간(1배속) | 실제시간(기다림 4배속)");
            var chapters = new SortedDictionary<int, List<Row>>();
            foreach (var r in Rows)
            {
                if (!chapters.TryGetValue(r.Chapter, out var list))
                    chapters[r.Chapter] = list = new List<Row>();
                list.Add(r);
            }
            float allOne = 0f, allMix = 0f;
            foreach (var kv in chapters)
            {
                float game = 0f, wait = 0f, read = 0f, scene = 0f;
                int busy = 0, builds = 0, clicks = 0;
                foreach (var r in kv.Value)
                {
                    game += r.Delta;
                    read += r.Read;
                    scene += r.Scene;
                    builds += r.Builds;
                    clicks += r.Clicks;
                    if (r.Tag == "기다림")
                        wait += r.Delta;
                    if (r.Tag == "바쁨")
                        busy++;
                }
                float one = game + read + scene, mix = game - wait + wait / 4f + read + scene;
                allOne += one;
                allMix += mix;
                sb.AppendLine($"  {kv.Key + 1}장 | {Clock(game)} | {Clock(wait)} ({(game > 0f ? wait / game * 100f : 0f):0}%) | {busy} | {builds} | {clicks} | {Clock(read)} | {Clock(scene)} | {Clock(one)} | {Clock(mix)}");
            }
            sb.AppendLine($"  합계 실제시간: 1배속 {Clock(allOne)} · 기다림 4배속 {Clock(allMix)}");
            foreach (var kv in NoteCounts)
            {
                sb.Append("  ").Append(kv.Key).Append(':');
                for (int i = 0; i < kv.Value.Length; i++)
                    if (kv.Value[i] > 0)
                        sb.Append($" {i + 1}장 {kv.Value[i]}");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
