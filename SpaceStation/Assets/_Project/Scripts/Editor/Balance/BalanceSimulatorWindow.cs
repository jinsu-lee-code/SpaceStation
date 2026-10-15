using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// 메뉴 SpaceStation/Balance Simulator: 규칙 기반 봇으로 N분 × 시드 M회를 돌려 CSV 리포트를 만든다.
    /// 출력: {Unity 프로젝트}/BalanceReports/{시각}/summary.csv, timeseries.csv, summary.txt (Git 추적 제외).
    /// </summary>
    public sealed class BalanceSimulatorWindow : EditorWindow
    {
        private const string DataRoot = "Assets/_Project/Data";

        [SerializeField] private BalanceConfig _balance;
        [SerializeField] private StationGradeConfig _grades;
        [SerializeField] private ModuleData _core;
        [SerializeField] private AdjacencyRuleSet _adjacency;
        [SerializeField] private List<GameEventData> _events = new List<GameEventData>();
        [SerializeField] private List<ModuleData> _buildable = new List<ModuleData>();
        [SerializeField] private List<ResearchCategoryData> _research = new List<ResearchCategoryData>();
        [SerializeField] private ResearchLevelCapConfig _researchCaps;
        [SerializeField] private List<RecipeData> _recipes = new List<RecipeData>();
        [SerializeField] private ResearchExpansionConfig _expansion;
        [Tooltip("Phase 10 주민 특성 (비우면 명단 없이 측정)")]
        [SerializeField] private ResidentConfig _residents;
        [Tooltip("13-1 타이쿤판 해금 선택 (비우면 등급 숫자로 시스템이 열림)")]
        [SerializeField] private UnlockChoiceConfig _unlockChoices;
        [Tooltip("13-2 타이쿤판 성장 비례 위협 (비우면 등급 표)")]
        [SerializeField] private GrowthThreatConfig _growthThreat;
        [SerializeField] private float _durationMinutes = 30f;

        /// <summary>Phase 10 비교 측정용: true면 기본 에셋 실행에서도 주민 특성을 끈다.</summary>
        public static bool DisableResidents;
        [SerializeField] private int _runs = 20;
        [SerializeField] private int _baseSeed = 1;

        private string _lastSummary;
        private string _lastFolder;
        private Vector2 _scroll;
        private SerializedObject _so;

        [MenuItem("SpaceStation/Balance Simulator")]
        public static void Open()
        {
            GetWindow<BalanceSimulatorWindow>("Balance Simulator");
        }

        private void OnEnable()
        {
            LoadDefaults(false);
            _so = new SerializedObject(this);
        }

        private void OnGUI()
        {
            _so.Update();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("데이터", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_so.FindProperty("_balance"));
            EditorGUILayout.PropertyField(_so.FindProperty("_grades"));
            EditorGUILayout.PropertyField(_so.FindProperty("_core"));
            EditorGUILayout.PropertyField(_so.FindProperty("_adjacency"));
            EditorGUILayout.PropertyField(_so.FindProperty("_events"), true);
            EditorGUILayout.PropertyField(_so.FindProperty("_buildable"), true);
            EditorGUILayout.PropertyField(_so.FindProperty("_research"), true);
            EditorGUILayout.PropertyField(_so.FindProperty("_researchCaps"));
            EditorGUILayout.PropertyField(_so.FindProperty("_residents"));
            if (GUILayout.Button("기본 에셋 다시 불러오기"))
                LoadDefaults(true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("실행", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_so.FindProperty("_durationMinutes"), new GUIContent("시간(분)"));
            EditorGUILayout.PropertyField(_so.FindProperty("_runs"), new GUIContent("시드 수"));
            EditorGUILayout.PropertyField(_so.FindProperty("_baseSeed"), new GUIContent("시작 시드"));
            _so.ApplyModifiedProperties();

            using (new EditorGUI.DisabledScope(JobRunning))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("실행", GUILayout.Height(32)))
                    RunFromWindow();
                if (GUILayout.Button("백그라운드로 실행", GUILayout.Height(32)))
                    StartJob(() => BalanceJob.Start(BuildSettings(), $"일반 측정 ({_durationMinutes:0}분 × {_runs}판)"));
                EditorGUILayout.EndHorizontal();
            }

            DrawSegmentTools();

            if (!string.IsNullOrEmpty(_lastSummary))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("최근 결과", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(_lastSummary, MessageType.None);
                if (!string.IsNullOrEmpty(_lastFolder) && GUILayout.Button("리포트 폴더 열기"))
                    EditorUtility.RevealInFinder(Path.Combine(_lastFolder, "summary.csv"));
            }
            EditorGUILayout.EndScrollView();
        }

        // 12-2 창 설정
        [SerializeField] private float _startsMinutes = 240f;
        [SerializeField] private int _startsRuns = 10;
        [SerializeField] private int _segmentGrade = -1;
        [SerializeField] private float _segmentCapMinutes = 120f;
        [SerializeField] private float _longMinutes = 300f;
        [SerializeField] private int _longRuns = 3;

        private static bool JobRunning => BalanceJob.Current != null && BalanceJob.Current.Running;

        private void OnInspectorUpdate()
        {
            if (JobRunning)
                Repaint();
        }

        /// <summary>12-2: 구간 시작 만들기 · 구간 측정 · 긴 세션 (모두 백그라운드 — Unity가 멈추지 않고, 한 판마다 리포트 저장).</summary>
        private void DrawSegmentTools()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("상위 등급 측정 (12-2, 백그라운드)", EditorStyles.boldLabel);

            var job = BalanceJob.Current;
            if (job != null && job.Running)
            {
                var rect = EditorGUILayout.GetControlRect(false, 20f);
                var elapsed = DateTime.Now - job.StartedAt;
                EditorGUI.ProgressBar(rect, job.Progress, $"{job.Label} — {job.Completed}/{job.Count}판 · {elapsed:hh\\:mm\\:ss}");
                if (GUILayout.Button("취소 (끝난 판 결과는 남음)"))
                    job.Cancel();
                return;
            }

            if (_grades == null)
                return;
            int[] segmentGrades = BalanceStartLibrary.SegmentGrades(_grades);
            if (segmentGrades.Length == 0)
                return;

            // 구간 시작 상태 현황
            var status = new StringBuilder("구간 시작 상태:");
            foreach (int g in segmentGrades)
                status.Append($" {_grades.Grades[g].DisplayName} {BalanceStartLibrary.Count(g)}개");
            EditorGUILayout.HelpBox(status + "\n(" + BalanceStartLibrary.Folder + ", 저장소에 커밋. 규칙 · 모듈이 바뀌면 다시 만들기)", MessageType.None);

            _so.Update();
            EditorGUILayout.PropertyField(_so.FindProperty("_startsMinutes"), new GUIContent("만들기: 최대 시간(분)"));
            EditorGUILayout.PropertyField(_so.FindProperty("_startsRuns"), new GUIContent("만들기: 시드 수"));
            _so.ApplyModifiedProperties();
            if (GUILayout.Button("구간 시작 만들기 (처음부터 → 각 등급 도달 순간 저장)"))
                StartJob(() => StartBuildSegmentStarts(_startsMinutes, _startsRuns, _baseSeed));

            EditorGUILayout.Space();
            var names = new string[segmentGrades.Length];
            int selected = 0;
            for (int i = 0; i < segmentGrades.Length; i++)
            {
                names[i] = $"{_grades.Grades[segmentGrades[i]].DisplayName} → {_grades.Grades[segmentGrades[i] + 1].DisplayName}";
                if (segmentGrades[i] == _segmentGrade)
                    selected = i;
            }
            selected = EditorGUILayout.Popup("구간", selected, names);
            _segmentGrade = segmentGrades[selected];
            _segmentCapMinutes = EditorGUILayout.FloatField("구간: 상한(분)", _segmentCapMinutes);
            using (new EditorGUI.DisabledScope(BalanceStartLibrary.Count(_segmentGrade) == 0))
            {
                if (GUILayout.Button("구간 측정 (시작 상태 → 다음 등급 또는 상한)"))
                    StartJob(() => StartSegment(_segmentGrade, _segmentCapMinutes));
            }

            EditorGUILayout.Space();
            _longMinutes = EditorGUILayout.FloatField("긴 세션: 최대 시간(분)", _longMinutes);
            _longRuns = EditorGUILayout.IntField("긴 세션: 시드 수", _longRuns);
            if (GUILayout.Button("긴 세션 (처음 → 마지막 등급 또는 최대 시간)"))
                StartJob(() => StartLongSession(_longMinutes, Mathf.Max(1, _longRuns), _baseSeed));
        }

        private void StartJob(Func<BalanceJob> start)
        {
            try
            {
                var job = start();
                _lastFolder = job.Folder;
                _lastSummary = "진행 중: " + job.Label;
                job.Finished += j =>
                {
                    _lastFolder = j.Folder;
                    _lastSummary = j.Summary;
                    Repaint();
                };
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Balance Simulator", e.Message, "확인");
            }
        }

        private void RunFromWindow()
        {
            try
            {
                var (folder, summary) = RunAndSave(BuildSettings(), (i, p) =>
                    EditorUtility.DisplayProgressBar("Balance Simulator", $"시드 {i + 1}/{_runs}", p));
                _lastFolder = folder;
                _lastSummary = summary;
                Debug.Log($"[Balance] 리포트 저장: {folder}\n{summary}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private BalanceRunSettings BuildSettings()
        {
            return new BalanceRunSettings
            {
                Balance = _balance,
                Grades = _grades,
                CoreModule = _core,
                Events = _events,
                Buildable = _buildable,
                AdjacencyRules = _adjacency,
                ResearchCategories = _research,
                ResearchCaps = _researchCaps,
                Recipes = _recipes,
                ResearchExpansion = _expansion,
                UnlockChoices = _unlockChoices,
                GrowthThreat = _growthThreat,
                Residents = DisableResidents ? null : _residents,
                DurationSeconds = _durationMinutes * 60f,
                Runs = Mathf.Max(1, _runs),
                BaseSeed = _baseSeed,
            };
        }

        /// <summary>기본 에셋으로 실행 (MCP/스크립트용). 반환: (리포트 폴더, 요약 텍스트).</summary>
        public static (string folder, string summary) RunWithDefaults(float durationMinutes = 30f, int runs = 20, int baseSeed = 1)
        {
            return RunAndSave(DefaultSettings(durationMinutes, runs, baseSeed), null);
        }

        /// <summary>난이도 프리셋을 적용한 복사본으로 실행 (5-9 측정용, 에셋은 바뀌지 않음).</summary>
        public static (string folder, string summary) RunWithDifficulty(DifficultyPreset preset, float durationMinutes = 60f, int runs = 20, int baseSeed = 1)
        {
            var settings = DefaultSettings(durationMinutes, runs, baseSeed);
            if (preset != null)
            {
                settings.Balance = preset.ApplyTo(settings.Balance);
                settings.Grades = preset.ApplyTo(settings.Grades);
            }
            return RunAndSave(settings, null);
        }

        private static (string folder, string summary) RunAndSave(BalanceRunSettings settings, Action<int, float> progress)
        {
            var report = BalanceRunner.Run(settings, progress);
            string folder = BalanceReportWriter.NewFolder();
            return (folder, BalanceReportWriter.Write(folder, settings, report));
        }

        // ---- 12-2 측정 도구 확장 (구간 시작 상태 · 구간 측정 · 긴 세션) ----

        /// <summary>기본 에셋 설정 (MCP/스크립트용 진입점이 공유).</summary>
        public static BalanceRunSettings DefaultSettings(float minutes, int runs, int baseSeed)
        {
            var window = CreateInstance<BalanceSimulatorWindow>();
            try
            {
                window.LoadDefaults(true);
                window._durationMinutes = minutes;
                window._runs = runs;
                window._baseSeed = baseSeed;
                return window.BuildSettings();
            }
            finally
            {
                DestroyImmediate(window);
            }
        }

        /// <summary>
        /// 구간 시작 만들기 (백그라운드): 시드마다 처음부터 최대 <paramref name="maxMinutes"/>분 플레이하며 초대형 · 도시급 · 국가급 · 대륙급에
        /// 처음 도달한 순간을 캡처 → 끝나면 <see cref="BalanceStartLibrary"/>에 등급별로 교체 저장. 마지막 구간 등급에 도달하면 그 판은 끝.
        /// </summary>
        public static BalanceJob StartBuildSegmentStarts(float maxMinutes = 240f, int runs = 10, int baseSeed = 1)
        {
            var s = DefaultSettings(maxMinutes, runs, baseSeed);
            var grades = BalanceStartLibrary.SegmentGrades(s.Grades);
            s.CaptureGrades = grades;
            s.StopAtGrade = grades[grades.Length - 1];
            s.SampleInterval = 60f;
            return BalanceJob.Start(s, $"구간 시작 만들기 (최대 {maxMinutes:0}분 × {runs}판)", grades);
        }

        /// <summary>구간 측정 (백그라운드): 그 등급의 시작 상태들에서 다음 등급까지, 시작부터 최대 <paramref name="capMinutes"/>분.</summary>
        public static BalanceJob StartSegment(int startGrade, float capMinutes = 120f)
        {
            var starts = BalanceStartLibrary.Load(startGrade);
            if (starts.Count == 0)
                throw new InvalidOperationException($"등급 {startGrade}의 구간 시작 상태가 없습니다. 먼저 '구간 시작 만들기'를 실행하세요.");
            var s = DefaultSettings(capMinutes, starts.Count, 1);
            s.Starts = starts;
            return BalanceJob.Start(s, $"구간 측정: {s.Grades.Grades[startGrade].DisplayName} → 다음 등급 (상한 {capMinutes:0}분, {starts.Count}개)");
        }

        /// <summary>긴 세션 (백그라운드): 처음부터 마지막 등급(지구급)까지, 최대 <paramref name="maxMinutes"/>분. 시드 수는 줄여서 (D1).</summary>
        public static BalanceJob StartLongSession(float maxMinutes = 300f, int runs = 3, int baseSeed = 1)
        {
            var s = DefaultSettings(maxMinutes, runs, baseSeed);
            s.StopAtGrade = s.Grades.Grades.Count - 1;
            s.SampleInterval = 60f;
            return BalanceJob.Start(s, $"긴 세션 (최대 {maxMinutes:0}분 × {runs}판)");
        }

        /// <summary>일반 측정을 백그라운드로 (창의 "백그라운드로 실행").</summary>
        public static BalanceJob StartDefault(float minutes = 30f, int runs = 20, int baseSeed = 1)
        {
            return BalanceJob.Start(DefaultSettings(minutes, runs, baseSeed), $"일반 측정 ({minutes:0}분 × {runs}판)");
        }

        private void LoadDefaults(bool overwrite)
        {
            if (overwrite || _balance == null)
                _balance = AssetDatabase.LoadAssetAtPath<BalanceConfig>($"{DataRoot}/BalanceConfig.asset");
            if (overwrite || _grades == null)
                _grades = AssetDatabase.LoadAssetAtPath<StationGradeConfig>($"{DataRoot}/StationGrades.asset");
            if (overwrite || _core == null)
                _core = AssetDatabase.LoadAssetAtPath<ModuleData>($"{DataRoot}/Modules/MD_Core.asset");
            if (overwrite || _adjacency == null)
                _adjacency = AssetDatabase.LoadAssetAtPath<AdjacencyRuleSet>($"{DataRoot}/AdjacencyRules.asset");

            if (overwrite || _events.Count == 0)
            {
                _events.Clear();
                foreach (var guid in AssetDatabase.FindAssets("t:GameEventData", new[] { $"{DataRoot}/Events" }))
                    _events.Add(AssetDatabase.LoadAssetAtPath<GameEventData>(AssetDatabase.GUIDToAssetPath(guid)));
            }
            if (overwrite || _research.Count == 0)
            {
                _research.Clear();
                foreach (var guid in AssetDatabase.FindAssets("t:ResearchCategoryData", new[] { $"{DataRoot}/Research" }))
                    _research.Add(AssetDatabase.LoadAssetAtPath<ResearchCategoryData>(AssetDatabase.GUIDToAssetPath(guid)));
            }
            if (overwrite || _researchCaps == null)
                _researchCaps = AssetDatabase.LoadAssetAtPath<ResearchLevelCapConfig>($"{DataRoot}/Research/ResearchLevelCaps.asset");
            // 12-4 합성 레시피 · 연구 확장 (봇이 도시급부터 해금 연구 · 합성)
            if (overwrite || _recipes.Count == 0)
            {
                _recipes.Clear();
                foreach (var guid in AssetDatabase.FindAssets("t:RecipeData", new[] { $"{DataRoot}/Recipes" }))
                    _recipes.Add(AssetDatabase.LoadAssetAtPath<RecipeData>(AssetDatabase.GUIDToAssetPath(guid)));
            }
            if (overwrite || _expansion == null)
                _expansion = AssetDatabase.LoadAssetAtPath<ResearchExpansionConfig>($"{DataRoot}/Research/ResearchExpansionConfig.asset");
            if (overwrite || _residents == null)
                _residents = AssetDatabase.LoadAssetAtPath<ResidentConfig>($"{DataRoot}/Residents/ResidentConfig.asset");
            if (overwrite || _unlockChoices == null)
                _unlockChoices = AssetDatabase.LoadAssetAtPath<UnlockChoiceConfig>($"{DataRoot}/Tycoon/UnlockChoiceConfig.asset"); // 13-1 (없으면 null)
            if (overwrite || _growthThreat == null)
                _growthThreat = AssetDatabase.LoadAssetAtPath<GrowthThreatConfig>($"{DataRoot}/Tycoon/GrowthThreatConfig.asset"); // 13-2 (없으면 null)
            if (overwrite || _buildable.Count == 0)
            {
                _buildable.Clear();
                foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { $"{DataRoot}/Modules" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("/Evolved/"))
                        continue; // 12-3 진화 모듈은 합성으로만 (건설 목록 아님 — 봇 합성은 12-4)
                    var m = AssetDatabase.LoadAssetAtPath<ModuleData>(path);
                    if (m != null && m != _core)
                        _buildable.Add(m);
                }
            }
        }
    }
}
