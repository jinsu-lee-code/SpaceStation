using System;
using System.Collections;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Settings;
using SpaceStation.Simulation;
using SpaceStation.UI;
using UnityEngine;

namespace SpaceStation.Save
{
    /// <summary>
    /// 게임 씬의 저장 담당 (Phase 6 세이브/로드).
    /// - 수동 저장: <see cref="SaveTo"/> (ESC 메뉴 → 저장 창)
    /// - 자동 저장: 게임 시간 N분마다(설정, 기본 3분), 메인 메뉴로 나갈 때, 게임을 끌 때. 게임 오버면 자동 저장을 지운다.
    /// - 불러온 판: 카메라를 저장 당시로 되돌리고 일시정지 상태로 시작, 알림 표시.
    /// 썸네일은 UI 없이 월드 카메라만 작게 렌더링한다.
    /// </summary>
    public sealed class SaveManager : MonoBehaviour
    {
        private const int ThumbWidth = 384;
        private const int ThumbHeight = 216;

        [SerializeField] private SimulationHost _host;
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private OrbitCameraController _cameraController;
        [Tooltip("썸네일을 찍을 카메라 (비우면 Main Camera)")]
        [SerializeField] private Camera _camera;

        private StationSimulation _sim;
        private float _lastAutosaveAt;
        private bool _leaving;

        public static SaveManager Instance { get; private set; }
        /// <summary>(메시지, 경고 여부). 상태 표시줄이 구독.</summary>
        public static event Action<string, bool> Notice;

        /// <summary>게임 오버가 아니면 저장할 수 있다.</summary>
        public bool CanSave => _sim != null && !_sim.Session.IsGameOver;

        private void Awake()
        {
            if (gameObject.scene.name != SceneNames.Game)
            {
                enabled = false; // 메인 메뉴 전시 정거장은 저장하지 않음
                return;
            }
            Instance = this;
            if (_camera == null)
                _camera = Camera.main;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (_sim != null)
                _sim.Session.GameOver -= HandleGameOver;
        }

        private void Start()
        {
            _sim = _host.Simulation;
            _lastAutosaveAt = _sim.ElapsedSeconds;
            _sim.Session.GameOver += HandleGameOver;

            var loaded = _host.LoadedSave;
            if (loaded == null)
                return;
            var c = loaded.Camera;
            if (c != null && c.Valid && _cameraController != null)
                _cameraController.Restore(c.Focus, c.Yaw, c.Pitch, c.Distance);
            _clock.Clock.SetPaused(true); // 상황을 보고 재개하도록
            StartCoroutine(NotifyLoaded(_host.LoadMissingCount));
        }

        private IEnumerator NotifyLoaded(int missing)
        {
            yield return null; // 상태 표시줄이 구독한 뒤
            Notice?.Invoke($"저장을 불러왔습니다 · 일시정지 상태 ({KeyBindings.Label(GameAction.Pause)} 또는 배속 버튼으로 재개)", false);
            if (missing > 0)
            {
                yield return new WaitForSecondsRealtime(3f);
                Notice?.Invoke($"게임 데이터가 바뀌어 일부 항목 {missing}개를 불러오지 못했습니다", true);
            }
        }

        private void Update()
        {
            if (_sim == null || _leaving)
                return;
            int minutes = GameSettings.AutosaveMinutes;
            if (minutes > 0 && CanSave && _sim.ElapsedSeconds - _lastAutosaveAt >= minutes * 60f)
                Autosave(true);
        }

        /// <summary>자동 저장 슬롯에 저장.</summary>
        public bool Autosave(bool notify)
        {
            _lastAutosaveAt = _sim != null ? _sim.ElapsedSeconds : 0f;
            return SaveTo(SaveService.AutoSlot, notify);
        }

        /// <summary>지금 상태를 슬롯에 저장. 게임 오버면 저장하지 않는다.</summary>
        public bool SaveTo(string slot, bool notify)
        {
            if (!CanSave)
                return false;
            var file = new SaveFile();
            var d = _host.Difficulty;
            file.Meta = new SaveMeta
            {
                SavedAtTicks = DateTime.Now.Ticks,
                Difficulty = d != null ? d.name : null,
                DifficultyName = d != null ? d.DisplayName : "",
                GradeName = _sim.Progression.Current.DisplayName,
                Population = _sim.Resources.Population,
                Modules = _sim.Grid.ModuleCount,
                PlaySeconds = _sim.ElapsedSeconds,
            };
            if (_cameraController != null && _cameraController.Rig != null)
            {
                var rig = _cameraController.Rig;
                file.Camera = new CameraState { Valid = true, Focus = rig.Focus, Yaw = rig.Yaw, Pitch = rig.Pitch, Distance = rig.Distance };
            }
            file.Station = StationStateSerializer.Capture(_sim);
            bool ok = SaveService.Write(slot, file, CaptureThumbnail());
            Debug.Log($"[Save] {slot} 저장 {(ok ? "완료" : "실패")} · 게임 시간 {_sim.ElapsedSeconds:0}초 · 인구 {_sim.Resources.Population}");
            if (notify)
            {
                string title = SaveService.TitleOf(slot);
                Notice?.Invoke(ok ? $"{title} 완료" : $"{title} 실패 (디스크 공간·권한을 확인하세요)", !ok);
            }
            return ok;
        }

        /// <summary>메인 메뉴로 나가기 전 (자동 저장 후 이동).</summary>
        public void SaveBeforeLeaving()
        {
            if (_leaving)
                return;
            _leaving = true;
            if (CanSave)
                Autosave(false);
        }

        private void OnApplicationQuit()
        {
            if (!_leaving && CanSave)
                Autosave(false);
            _leaving = true;
        }

        private void HandleGameOver()
        {
            SaveService.Delete(SaveService.AutoSlot); // 끝난 판을 [이어하기]로 열지 않게 (수동 저장은 유지)
            Debug.Log($"[Save] 게임 오버 ({_sim.Session.Reason}) → 자동 저장 삭제, 남아 있음: {System.IO.File.Exists(SaveService.JsonPath(SaveService.AutoSlot))}");
        }

        private byte[] CaptureThumbnail()
        {
            if (_camera == null)
                return null;
            var rt = RenderTexture.GetTemporary(ThumbWidth, ThumbHeight, 24);
            var previousTarget = _camera.targetTexture;
            var previousActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                _camera.targetTexture = rt;
                _camera.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(ThumbWidth, ThumbHeight, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, ThumbWidth, ThumbHeight), 0, 0);
                tex.Apply();
                return tex.EncodeToPNG();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] 썸네일 실패: {e.Message}");
                return null;
            }
            finally
            {
                _camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(rt);
                if (tex != null)
                    Destroy(tex);
            }
        }

        // ---------------- 불러오기 (메인 메뉴·게임 공용) ----------------

        /// <summary>슬롯을 읽어 게임 씬을 새로 연다. 읽을 수 없으면 false.</summary>
        public static bool LoadAndPlay(string slot)
        {
            if (SceneFader.Busy || SaveService.TryRead(slot, out var file) != SaveSlotStatus.Ok)
                return false;
            GameStartOptions.PendingLoad = file; // 난이도는 게임 씬 호스트가 이름으로 찾는다
            if (Instance != null)
                Instance._leaving = true; // 지금 판을 자동 저장으로 덮지 않음 (불러오기 확인에서 경고함)
            SceneFader.Load(SceneNames.Game);
            return true;
        }
    }
}
