using System;
using System.Collections.Generic;
using System.IO;
using SpaceStation.Data;
using SpaceStation.Editor.Balance;
using SpaceStation.Save;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 플레이 확인 루트 (사용자 요청 2026-10-20): 메뉴 하나로 장 · 절정 확인 지점에서 바로 플레이를 시작한다.
    /// 정거장 = 밸런스 봇이 그 등급(1장 소형 · 2장 중형 · 3장 대형)에 처음 닿은 순간 (누를 때마다 지금 규칙으로 새로 만듦, 같은 시드 = 같은 정거장)
    /// + 장 상태(번호 · 단계 · 지난 장 기록)를 덮어씀. 장 시작은 앞 장 결과 창에서 시작해 방향을 고르면 실제와 같은 흐름
    /// (전환 연출 → 제목 카드 → 장 시작 알림 · 첫 한마디 · 장 규칙)으로 넘어간다.
    /// 확인용 판의 저장은 Temp/PlayCheck/Saves로 — 실제 세이브를 덮지 않음 (<see cref="PlayCheckBoot"/>).
    /// </summary>
    public static class PlayCheckMenu
    {
        private const string Menu = "SpaceStation/플레이 확인/";
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";
        /// <summary>절정 예고 지점: 절정까지 남길 시간 (초).</summary>
        private const float WarningLead = 15f;

        [MenuItem(Menu + "1. 1장 결과 → 장 전환 연출 → 2장 시작", priority = 1)]
        private static void ChapterOneResult() => Start("1장 결과 창", 1, chapter: 0, ChapterPhase.Transition);

        [MenuItem(Menu + "2. 2장 절정 '큰 유성우' 15초 전", priority = 2)]
        private static void ChapterTwoClimax() => Start("2장 절정 15초 전", 2, chapter: 1, ChapterPhase.Warning);

        [MenuItem(Menu + "3. 2장 결과 → 장 전환 연출 → 3장 시작", priority = 3)]
        private static void ChapterTwoResult() => Start("2장 결과 창", 2, chapter: 1, ChapterPhase.Transition);

        [MenuItem(Menu + "4. 3장 절정 '대이주' 15초 전", priority = 4)]
        private static void ChapterThreeClimax() => Start("3장 절정 15초 전", 3, chapter: 2, ChapterPhase.Warning);

        [MenuItem(Menu + "5. 3장 결과 → 마지막 연출 → 이어질 이야기", priority = 5)]
        private static void ChapterThreeResult() => Start("3장 결과 창", 3, chapter: 2, ChapterPhase.Transition);

        [MenuItem(Menu + "1. 1장 결과 → 장 전환 연출 → 2장 시작", true)]
        [MenuItem(Menu + "2. 2장 절정 '큰 유성우' 15초 전", true)]
        [MenuItem(Menu + "3. 2장 결과 → 장 전환 연출 → 3장 시작", true)]
        [MenuItem(Menu + "4. 3장 절정 '대이주' 15초 전", true)]
        [MenuItem(Menu + "5. 3장 결과 → 마지막 연출 → 이어질 이야기", true)]
        private static bool CanStart() => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>
        /// grade 등급 정거장 + chapter 장의 phase 단계로 시작. Transition = 그 장 절정이 끝나 결과 창 (기록 '깔끔히'),
        /// Warning = 필수 목표를 채우고 절정 <see cref="WarningLead"/>초 전.
        /// </summary>
        private static void Start(string label, int grade, int chapter, ChapterPhase phase)
        {
            var config = FindChapters();
            if (config == null || chapter >= config.Count)
            {
                Debug.LogError("[플레이 확인] 장 데이터(ChapterConfig)를 찾을 수 없음 — SpaceStation/Chapters/Setup Greybox를 먼저 돌리세요");
                return;
            }
            var station = BotStation(grade, out string gradeName, out float playSeconds);
            if (station == null)
            {
                Debug.LogError($"[플레이 확인] 봇이 등급 {grade}에 닿지 못해 시작 정거장을 만들지 못함");
                return;
            }
            SetSystem(station, ChapterSystem.SystemKey, JsonUtility.ToJson(ChapterState(config, chapter, phase)));
            if (chapter >= 1)
                SetSystem(station, ArkShipSystem.SystemKey, JsonUtility.ToJson(ArkState(config, chapter, station)));

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (EditorSceneManager.GetActiveScene().path != GameScene)
                EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            KeepSceneResearch(station);

            var file = new SaveFile
            {
                Meta = new SaveMeta
                {
                    SavedAtTicks = DateTime.Now.Ticks,
                    GradeName = gradeName,
                    Population = station.Population,
                    Modules = station.Modules.Count + 1, // + 코어
                    PlaySeconds = playSeconds,
                },
                Station = station,
            };
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "PlayCheck");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "start.json");
            File.WriteAllText(path, JsonUtility.ToJson(file));

            UnityEditor.SessionState.SetString(PlayCheckBoot.StartKey, path); // (SpaceStation.Simulation.SessionState와 다른 것)
            Debug.Log($"[플레이 확인] {label} · {gradeName} 정거장 (봇 {playSeconds / 60f:0}분)으로 플레이 시작");
            EditorApplication.EnterPlaymode();
        }

        private static ChapterConfig FindChapters()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:ChapterConfig"))
            {
                var c = AssetDatabase.LoadAssetAtPath<ChapterConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (c != null && c.Count > 0)
                    return c;
            }
            return null;
        }

        /// <summary>봇이 처음부터 플레이하다 그 등급에 처음 닿은 순간 (시드 1부터, 못 닿으면 다음 시드).</summary>
        private static StationState BotStation(int grade, out string gradeName, out float playSeconds)
        {
            gradeName = "";
            playSeconds = 0f;
            for (int seed = 1; seed <= 4; seed++)
            {
                var s = BalanceSimulatorWindow.DefaultSettings(60f, 1, seed);
                s.CaptureGrades = new[] { grade };
                s.StopAtGrade = grade;
                s.SampleInterval = 60f;
                var report = BalanceRunner.Run(s);
                if (report.Captured.Count == 0)
                    continue;
                var start = report.Captured[0];
                gradeName = start.GradeName;
                playSeconds = start.ReachedAt;
                return start.Station;
            }
            return null;
        }

        /// <summary>
        /// 봇은 공통 6분야 연구로 플레이하지만 서사판 씬 연구는 기록 4갈래 + 정비 자동화 (13-5b) —
        /// 씬에 없는 연구 레벨 · 진행 중 연구는 빼서 불러오기 "찾을 수 없는 항목" 경고가 나지 않게.
        /// </summary>
        private static void KeepSceneResearch(StationState station)
        {
            var host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            if (host == null)
                return;
            var names = new List<string>();
            var list = new SerializedObject(host).FindProperty("_researchCategories");
            for (int i = 0; list != null && i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is ResearchCategoryData c)
                    names.Add(c.name);
            if (names.Count == 0)
                return;
            station.ResearchLevels.RemoveAll(lv => !names.Contains(lv.Name));
            station.Projects.RemoveAll(p => !names.Contains(p.Category));
        }

        /// <summary><see cref="ChapterSystem"/> 저장 형식과 같은 이름의 칸 (JsonUtility가 이름으로 맞춤).</summary>
        [Serializable]
        private sealed class ChapterStateData
        {
            public int Index;
            public int Phase;
            public float ChapterSeconds;
            public float WarningLeft;
            public bool GradeUp;
            public List<bool> Done = new List<bool>();
            public List<string> Flags = new List<string>();
            public List<ChapterRecord> Records = new List<ChapterRecord>();
            public int ClimaxKind;
            public string Climax;
        }

        private static ChapterStateData ChapterState(ChapterConfig config, int chapter, ChapterPhase phase)
        {
            var data = config.Chapters[chapter];
            var s = new ChapterStateData
            {
                Index = chapter,
                Phase = (int)phase,
                ChapterSeconds = 900f,
                WarningLeft = phase == ChapterPhase.Warning ? WarningLead : 0f,
                GradeUp = true, // 이 장의 목표 등급에 닿은 정거장
            };
            s.Flags.Add(ChapterSystem.TutorialFlag);
            // 결과 창이면 선택 목표까지, 예고면 필수 목표만 채움
            foreach (var goal in data.Goals)
                s.Done.Add(phase == ChapterPhase.Transition || !goal.Optional);
            // 지난 장은 깔끔히 끝내고 첫 번째 방향을 고른 것으로
            for (int i = 0; i < chapter; i++)
            {
                var past = config.Chapters[i];
                s.Records.Add(Record(i, past.Choices.Count > 0 ? past.Choices[0].Id : ""));
            }
            if (phase == ChapterPhase.Transition)
                s.Records.Add(Record(chapter, "")); // 방금 끝난 장 (방향은 결과 창에서 고름)
            return s;
        }

        /// <summary><see cref="ArkShipSystem"/> 저장 형식과 같은 이름의 칸.</summary>
        [Serializable]
        private sealed class ArkStateData
        {
            public float Timer = -1f;
            public bool HasForecast;
            public ArkShip Forecast;
            public bool HasPending;
            public ArkShip Pending;
            public float PendingWait;
            public int NewSpeciesChapter = -1;
            public List<string> Met = new List<string>();
            public int Ships;
            public int Accepted;
            public int TurnedAway;
        }

        /// <summary>
        /// 2장부터 이민선 시계가 돌고 있던 것으로. 그 장까지 새 종족(3장 곰)이 있으면 이미 맞아 주민 6명이 그 종족
        /// (나머지 주민은 비워 두면 게임이 시작 종족으로 채움) — 3장 대이주가 섞인 무리로 오게.
        /// </summary>
        private static ArkStateData ArkState(ChapterConfig config, int chapter, StationState station)
        {
            var s = new ArkStateData { Timer = config.Chapters[chapter].ShipInterval };
            for (int i = 0; i <= chapter; i++)
            {
                var species = config.Chapters[i].NewSpecies;
                if (species == null || s.Met.Contains(species.Id))
                    continue;
                s.Met.Add(species.Id);
                s.NewSpeciesChapter = i;
                s.Ships++;
                s.Accepted++;
                for (int r = 0; r < station.Residents.Count && r < 6; r++)
                    station.Residents[r].Kind = species.Id;
            }
            return s;
        }

        private static ChapterRecord Record(int chapter, string choice) => new ChapterRecord
        {
            Chapter = chapter,
            Outcome = ChapterOutcome.Clean,
            Summary = "플레이 확인용 기록",
            Choice = choice,
            Seconds = 900f,
            Monologue = "",
        };

        private static void SetSystem(StationState station, string key, string state)
        {
            foreach (var s in station.Systems)
            {
                if (s.Key == key)
                {
                    s.State = state;
                    return;
                }
            }
            station.Systems.Add(new SystemState { Key = key, State = state });
        }
    }
}
