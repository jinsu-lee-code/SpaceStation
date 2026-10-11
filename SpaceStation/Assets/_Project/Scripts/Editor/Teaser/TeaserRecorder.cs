using System.IO;
using SpaceStation.Teaser;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace SpaceStation.Editor.Teaser
{
    /// <summary>
    /// 11-19 티저 녹화 (Unity Recorder → MP4 1920×1080 30fps, 오디오 포함).
    /// 사용: 플레이 모드에서 slot1(쇼케이스 정거장)을 불러온 뒤 메뉴 SpaceStation/Teaser/Record.
    /// 결과: 저장소 루트 Presentation/AnotherEarth_Teaser.mp4 + 확인용 장면 캡처 Presentation/teaser_stills/.
    /// </summary>
    public static class TeaserRecorder
    {
        private static RecorderController _controller;

        private static string RepoRoot => Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;

        [MenuItem("SpaceStation/Teaser/Record")]
        public static void RecordMenu() => Begin(true);

        [MenuItem("SpaceStation/Teaser/Record", true)]
        private static bool RecordMenuValidate() => Application.isPlaying && _controller == null;

        /// <param name="record">false면 녹화 없이 연출 · 장면 캡처만.</param>
        public static string Begin(bool record)
        {
            if (!Application.isPlaying)
                return "플레이 모드에서 실행하세요";
            if (Object.FindFirstObjectByType<TeaserDirector>() != null)
                return "이미 진행 중";
            string folder = Path.Combine(RepoRoot, "Presentation");
            string stills = Path.Combine(folder, "teaser_stills");
            Directory.CreateDirectory(stills);

            var go = new GameObject("TeaserDirector");
            var director = go.AddComponent<TeaserDirector>();
            if (director == null)
            {
                Object.Destroy(go);
                return "연출 스크립트를 붙이지 못함";
            }
            director.StillFolder = stills;
            if (record)
                StartRecording(Path.Combine(folder, "AnotherEarth_Teaser"));
            director.Finished = () =>
            {
                Stop();
                Debug.Log("[Teaser] 끝 → " + folder);
            };
            return record ? "녹화 시작" : "연출 시작 (녹화 없음)";
        }

        [MenuItem("SpaceStation/Teaser/Record Play Tour")]
        public static void RecordTourMenu() => BeginTour(true, 0);

        [MenuItem("SpaceStation/Teaser/Record Play Tour", true)]
        private static bool RecordTourMenuValidate() => Application.isPlaying && _controller == null;

        /// <summary>
        /// 11-19 플레이 영상 (기능 투어, 약 4분). 플레이 모드의 게임 씬에서 시작 — 메인 메뉴를 스스로 불러옴.
        /// 결과: Presentation/AnotherEarth_PlayTour.mp4 + 장면 캡처 Presentation/tour_stills/.
        /// </summary>
        /// <param name="startChapter">확인용: 이 장부터 (3 이상이면 slot1을 불러와 시작).</param>
        public static string BeginTour(bool record, int startChapter)
        {
            if (!Application.isPlaying)
                return "플레이 모드에서 실행하세요";
            if (Object.FindFirstObjectByType<PlayTourDirector>() != null)
                return "이미 진행 중";
            string folder = Path.Combine(RepoRoot, "Presentation");
            string stills = Path.Combine(folder, "tour_stills");
            Directory.CreateDirectory(stills);
            var go = new GameObject("PlayTourDirector");
            var director = go.AddComponent<PlayTourDirector>();
            if (director == null)
            {
                Object.Destroy(go);
                return "연출 스크립트를 붙이지 못함";
            }
            director.StillFolder = stills;
            director.StartChapter = startChapter;
            if (record)
                StartRecording(Path.Combine(folder, "AnotherEarth_PlayTour"));
            director.Finished = () =>
            {
                Stop();
                Debug.Log("[PlayTour] 끝 → " + folder);
            };
            return record ? "녹화 시작" : "연출 시작 (녹화 없음)";
        }

        private static void StartRecording(string outputWithoutExtension)
        {
            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "Teaser";
            movie.Enabled = true;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };
            movie.CaptureAudio = true;
            movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
            movie.OutputFile = outputWithoutExtension;
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRate = 30f;
            settings.CapFrameRate = true;
            _controller = new RecorderController(settings);
            _controller.PrepareRecording();
            _controller.StartRecording();
        }

        public static void Stop()
        {
            if (_controller != null && _controller.IsRecording())
                _controller.StopRecording();
            _controller = null;
        }
    }
}
