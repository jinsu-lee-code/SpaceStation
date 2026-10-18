using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// 12-2 백그라운드 측정: 에디터 업데이트마다 정해진 시간만큼만 돌려 Unity가 멈추지 않게 한다 (긴 세션 · 구간 측정 · 구간 시작 만들기).
    /// 한 판이 끝날 때마다 리포트 파일을 다시 써서, 도중에 멈추거나 취소해도 끝난 판의 결과는 남는다.
    /// 구간 시작 상태는 끝까지 마쳤을 때만 저장한다 (취소하면 옛 파일 그대로). 스크립트가 다시 컴파일되면 작업은 사라진다.
    /// </summary>
    public sealed class BalanceJob
    {
        private const double BudgetSeconds = 0.05; // 한 번의 에디터 업데이트에 쓰는 시간
        private const int TicksPerSlice = 1; // 큰 정거장은 한 틱(봇 판단 포함)이 0.3초까지 걸려서 틱마다 시간 확인

        public static BalanceJob Current { get; private set; }

        private readonly BalanceRunSettings _settings;
        private readonly BalanceReport _report;
        private readonly int _count;
        private readonly int[] _saveStartGrades;
        private BalanceRun _run;
        private int _index;

        public string Label { get; }
        public string Folder { get; }
        public bool Running { get; private set; }
        public bool Cancelled { get; private set; }
        public string Summary { get; private set; }
        public DateTime StartedAt { get; } = DateTime.Now;
        /// <summary>에디터 업데이트 호출 수 · 그중 측정에 쓴 시간(초) — 백그라운드 처리 비율 확인용.</summary>
        public int Updates { get; private set; }
        public double BusySeconds { get; private set; }
        public int Completed => _index;
        public int Count => _count;
        public event Action<BalanceJob> Finished;

        /// <summary>0~1 (판 수 기준 + 진행 중인 판의 시간 비율).</summary>
        public float Progress => _count == 0 ? 1f
            : Mathf.Clamp01((_index + (_run != null ? Mathf.Clamp01(_run.Elapsed / _settings.DurationSeconds) : 0f)) / _count);

        private BalanceJob(BalanceRunSettings settings, string label, int[] saveStartGrades)
        {
            _settings = settings;
            _report = BalanceReport.Create(settings);
            _count = BalanceReport.RunCount(settings);
            _saveStartGrades = saveStartGrades;
            Label = label;
            Folder = BalanceReportWriter.NewFolder();
        }

        /// <summary>새 작업 시작 (진행 중인 작업이 있으면 실패). <paramref name="saveStartGrades"/>가 있으면 끝날 때 그 등급의 구간 시작 상태를 교체 저장.</summary>
        public static BalanceJob Start(BalanceRunSettings settings, string label, int[] saveStartGrades = null)
        {
            if (Current != null && Current.Running)
                throw new InvalidOperationException("측정이 이미 진행 중입니다: " + Current.Label);
            BalanceMilestones.Clear(); // 13-7
            var job = new BalanceJob(settings, label, saveStartGrades) { Running = true };
            Current = job;
            EditorApplication.update += job.Update;
            Debug.Log($"[Balance] 백그라운드 측정 시작: {label} ({job._count}판) → {job.Folder}");
            return job;
        }

        public void Cancel()
        {
            if (!Running)
                return;
            Cancelled = true;
            Complete();
        }

        private void Update()
        {
            if (!Running)
                return;
            double begin = EditorApplication.timeSinceStartup;
            double until = begin + BudgetSeconds;
            Updates++;
            try
            {
                while (EditorApplication.timeSinceStartup < until)
                {
                    if (_run == null)
                    {
                        if (_index >= _count)
                        {
                            Complete();
                            return;
                        }
                        _run = BalanceReport.CreateRun(_settings, _index, _report.TimeSeriesCsv);
                    }
                    _run.Step(TicksPerSlice);
                    if (_run.Done)
                    {
                        _report.Add(_run);
                        _run = null;
                        _index++;
                        Summary = BalanceReportWriter.Write(Folder, _settings, _report, Label + $" ({_index}/{_count}판 완료)");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Cancelled = true;
                Complete();
            }
            finally
            {
                BusySeconds += EditorApplication.timeSinceStartup - begin;
            }
        }

        private void Complete()
        {
            Running = false;
            EditorApplication.update -= Update;
            string state = Cancelled ? $"취소됨 — {_index}/{_count}판 완료" : $"{_count}판 완료";
            Summary = BalanceReportWriter.Write(Folder, _settings, _report, $"{Label} ({state})");
            if (!Cancelled && _saveStartGrades != null)
            {
                BalanceStartLibrary.Save(_saveStartGrades, _report.Captured);
                var sb = new StringBuilder("\n구간 시작 상태 저장:");
                foreach (int g in _saveStartGrades)
                    sb.Append($" {_report.GradeNames[g]} {BalanceStartLibrary.Count(g)}개");
                Summary += sb.ToString();
                File.AppendAllText(Path.Combine(Folder, "summary.txt"), sb.ToString(), new UTF8Encoding(true));
            }
            Debug.Log($"[Balance] 측정 끝 ({state}): {Folder}\n{Summary}");
            Finished?.Invoke(this);
        }
    }

    /// <summary>리포트 파일 쓰기: {Unity 프로젝트}/BalanceReports/{시각}/summary.csv, timeseries.csv, summary.txt (Git 추적 제외).</summary>
    public static class BalanceReportWriter
    {
        public static string NewFolder()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string folder = Path.Combine(projectRoot, "BalanceReports", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>파일 3개를 (다시) 쓰고 요약 텍스트를 돌려준다.</summary>
        public static string Write(string folder, BalanceRunSettings settings, BalanceReport report, string title = null)
        {
            var utf8Bom = new UTF8Encoding(true); // 엑셀에서 한글 깨짐 방지
            File.WriteAllText(Path.Combine(folder, "summary.csv"), BalanceRunner.BuildSummaryCsv(report), utf8Bom);
            File.WriteAllText(Path.Combine(folder, "timeseries.csv"), report.TimeSeriesCsv.ToString(), utf8Bom);

            string seeds = settings.Starts != null ? $"starts={settings.Starts.Count}" : $"seeds={settings.BaseSeed}..{settings.BaseSeed + settings.Runs - 1}";
            string header = $"duration={settings.DurationSeconds / 60f:0.#}min runs={BalanceReport.RunCount(settings)} {seeds}";
            if (settings.StopAtGrade >= 0)
                header += $" stop_at={report.GradeNames[settings.StopAtGrade]}";
            string summary = (string.IsNullOrEmpty(title) ? "" : title + "\n") + header
                             + (report.Results.Count > 0 ? "\n" + BalanceRunner.BuildSummaryText(report) : "");
            File.WriteAllText(Path.Combine(folder, "summary.txt"), summary, utf8Bom);
            if (BalanceMilestones.Runs.Count > 0)
                BalanceMilestones.WriteReport(folder); // 13-7 milestones.csv · milestones.txt
            return summary;
        }
    }
}
