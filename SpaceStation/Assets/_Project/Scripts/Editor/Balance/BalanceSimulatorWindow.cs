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
        [SerializeField] private float _durationMinutes = 30f;
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
            if (GUILayout.Button("기본 에셋 다시 불러오기"))
                LoadDefaults(true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("실행", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_so.FindProperty("_durationMinutes"), new GUIContent("시간(분)"));
            EditorGUILayout.PropertyField(_so.FindProperty("_runs"), new GUIContent("시드 수"));
            EditorGUILayout.PropertyField(_so.FindProperty("_baseSeed"), new GUIContent("시작 시드"));
            _so.ApplyModifiedProperties();

            if (GUILayout.Button("실행", GUILayout.Height(32)))
                RunFromWindow();

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
                DurationSeconds = _durationMinutes * 60f,
                Runs = Mathf.Max(1, _runs),
                BaseSeed = _baseSeed,
            };
        }

        /// <summary>기본 에셋으로 실행 (MCP/스크립트용). 반환: (리포트 폴더, 요약 텍스트).</summary>
        public static (string folder, string summary) RunWithDefaults(float durationMinutes = 30f, int runs = 20, int baseSeed = 1)
        {
            var window = CreateInstance<BalanceSimulatorWindow>();
            try
            {
                window.LoadDefaults(true);
                window._durationMinutes = durationMinutes;
                window._runs = runs;
                window._baseSeed = baseSeed;
                return RunAndSave(window.BuildSettings(), null);
            }
            finally
            {
                DestroyImmediate(window);
            }
        }

        private static (string folder, string summary) RunAndSave(BalanceRunSettings settings, Action<int, float> progress)
        {
            var report = BalanceRunner.Run(settings, progress);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string folder = Path.Combine(projectRoot, "BalanceReports", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(folder);

            var utf8Bom = new UTF8Encoding(true); // 엑셀에서 한글 깨짐 방지
            File.WriteAllText(Path.Combine(folder, "summary.csv"), BalanceRunner.BuildSummaryCsv(report), utf8Bom);
            File.WriteAllText(Path.Combine(folder, "timeseries.csv"), report.TimeSeriesCsv.ToString(), utf8Bom);

            string header = $"duration={settings.DurationSeconds / 60f:0.#}min runs={settings.Runs} seeds={settings.BaseSeed}..{settings.BaseSeed + settings.Runs - 1}";
            string summary = header + "\n" + BalanceRunner.BuildSummaryText(report);
            File.WriteAllText(Path.Combine(folder, "summary.txt"), summary, utf8Bom);
            return (folder, summary);
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
            if (overwrite || _buildable.Count == 0)
            {
                _buildable.Clear();
                foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { $"{DataRoot}/Modules" }))
                {
                    var m = AssetDatabase.LoadAssetAtPath<ModuleData>(AssetDatabase.GUIDToAssetPath(guid));
                    if (m != null && m != _core)
                        _buildable.Add(m);
                }
            }
        }
    }
}
