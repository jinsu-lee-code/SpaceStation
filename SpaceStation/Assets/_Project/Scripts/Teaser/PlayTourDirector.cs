#if UNITY_EDITOR
// 11-19 플레이 영상(기능 투어) 녹화 전용 — 빌드에서는 빠짐. 녹화는 Editor/Teaser/TeaserRecorder (메뉴 SpaceStation/Teaser/Record Play Tour).
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Interior;
using SpaceStation.Save;
using SpaceStation.Settings;
using SpaceStation.Simulation;
using SpaceStation.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpaceStation.Teaser
{
    /// <summary>
    /// 11-19 플레이 영상: 사람 대신 <see cref="VirtualInput"/>으로 실제 게임을 조작하며 기능을 하나씩 보여 준다 (약 4분).
    /// 메인 메뉴 → 새 게임(이지) → 건설 · 자원/낮밤 → (slot1 쇼케이스 정거장) 위기 · 수리 · 정비 · 방어 · 연구 · 주민 →
    /// 1인칭 내부 · 부탁 · 사고 · 현장 수리 · 패드 → 제목 카드.
    /// 장마다 위쪽 가운데에 장 제목 · 설명을 띄우고, 바깥에서는 가짜 커서를 그린다 (녹화에는 OS 커서가 찍히지 않음).
    /// 시간은 녹화기 고정 프레임을 따라 unscaledDeltaTime으로만 센다.
    /// </summary>
    public sealed class PlayTourDirector : MonoBehaviour
    {
        public string StillFolder { get; set; }
        /// <summary>확인용: 이 장부터 시작 (0 = 처음). 3 이상이면 메뉴 · 새 게임을 건너뛰고 slot1을 불러옴.</summary>
        public int StartChapter { get; set; }
        public System.Action Finished;

        private readonly VirtualInput _input = new VirtualInput();
        private float _t;
        private bool _tutorialWas;

        // 게임 씬 참조 (씬을 불러올 때마다 다시 찾음)
        private StationController _station;
        private BuildController _build;
        private BuildMenu _buildMenu;
        private ModuleSelectionController _selection;
        private SelectionActionsPanel _actions;
        private SimulationClock _clock;
        private OrbitCameraController _orbit;
        private InteriorMode _interior;

        // 화면
        private Canvas _overlay;
        private CanvasGroup _captionGroup;
        private TMP_Text _captionTitle;
        private TMP_Text _captionBody;
        private Image _fade;
        private Image _flash;
        private RectTransform _cursor;
        private bool _showCursor;
        private CanvasGroup _titleGroup;
        private TMP_Text _titleSub;
        private TMP_FontAsset _font;

        private StationSimulation Sim => _station.Simulation;

        // ---------------- 시작 · 끝 ----------------

        private IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            _tutorialWas = GameSettings.TutorialPending;
            GameSettings.TutorialPending = false;
            var anyText = FindFirstObjectByType<TMP_Text>();
            _font = anyText != null ? anyText.font : null;
            BuildOverlay();
            SetFade(1f);
            _input.Begin();

            if (StartChapter < 3)
            {
                yield return Intro();
                yield return Menu();
                yield return Build();
                yield return ResourcesAndNight();
            }
            yield return LoadShowcase();
            if (StartChapter <= 3) yield return Warning();
            if (StartChapter <= 5) yield return Wear();
            if (StartChapter <= 6) yield return Defense();
            if (StartChapter <= 7) yield return Research();
            if (StartChapter <= 8) yield return Roster();
            yield return EnterInterior();
            if (StartChapter <= 10) yield return Request();
            if (StartChapter <= 11) yield return Incident();
            if (StartChapter <= 12) yield return FieldRepair();
            yield return Pad();
            yield return Outro();
            Finish();
        }

        private void Finish()
        {
            _input.End();
            GameSettings.TutorialPending = _tutorialWas;
            Finished?.Invoke();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_input.Active)
            {
                _input.End();
                GameSettings.TutorialPending = _tutorialWas;
            }
        }

        private void LateUpdate()
        {
            _input.Flush();
            if (_cursor != null)
            {
                _cursor.gameObject.SetActive(_showCursor);
                if (_showCursor)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_overlay.transform, _input.Position, null, out var local);
                    _cursor.anchoredPosition = local;
                }
            }
        }

        private void Bind()
        {
            _station = FindFirstObjectByType<StationController>();
            _build = FindFirstObjectByType<BuildController>();
            _buildMenu = FindFirstObjectByType<BuildMenu>();
            _selection = FindFirstObjectByType<ModuleSelectionController>();
            _actions = FindFirstObjectByType<SelectionActionsPanel>();
            _clock = FindFirstObjectByType<SimulationClock>();
            _orbit = FindFirstObjectByType<OrbitCameraController>();
            _interior = FindFirstObjectByType<InteriorMode>();
        }

        private IEnumerator LoadScene(System.Action load, string sceneName)
        {
            // 같은 씬을 다시 부를 수도 있어(Main → slot1의 Main) 이름이 아니라 로드 완료 이벤트를 기다림
            bool loaded = false;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (scene, mode) =>
            {
                if (scene.name == sceneName)
                    loaded = true;
            };
            SceneManager.sceneLoaded += onLoaded;
            load();
            float waited = 0f;
            while (!loaded && waited < 20f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            SceneManager.sceneLoaded -= onLoaded;
            for (int i = 0; i < 10; i++)
                yield return null; // Start · 저장 복원
            if (sceneName == SceneNames.Game)
                Bind();
        }

        // ---------------- 장면 ----------------

        private IEnumerator Intro()
        {
            _titleGroup.alpha = 0f;
            _titleSub.text = "플레이 영상 · 기능 소개";
            float s = _t;
            while (_t < s + 3.6f)
            {
                float k = _t - s;
                _titleGroup.alpha = Mathf.Clamp01(k / 0.8f) - Mathf.Clamp01((k - 2.8f) / 0.8f);
                yield return Step();
            }
            _titleGroup.alpha = 0f;
            yield return LoadScene(() => SceneFader.Load(SceneNames.MainMenu), SceneNames.MainMenu);
        }

        private IEnumerator Menu()
        {
            AudioService.Instance?.PlayMusic(AudioService.Instance.Library.MusicMenu);
            yield return FadeTo(0f, 0.8f);
            _showCursor = true;
            Caption("메인 메뉴", "새 게임 · 이어하기 · 불러오기 · 설정");
            yield return Wait(1.2f);
            var newGame = ButtonWithText("새 게임");
            if (newGame != null)
                yield return ClickUi(newGame);
            yield return Wait(0.8f);
            Caption("난이도", "이지 · 노멀 · 하드 · 처음이면 이지");
            var normal = ButtonWithText("노멀");
            if (normal != null)
                yield return MoveCursor(ScreenOf(normal), 0.5f);
            yield return Wait(0.6f);
            var easy = ButtonWithText("이지");
            if (easy != null)
                yield return LoadScene(() => StartCoroutine(ClickUi(easy)), SceneNames.Game); // 로드 이벤트를 먼저 걸고 클릭
            else
                yield return LoadScene(() => SceneFader.Load(SceneNames.Game), SceneNames.Game);
            HideCaption();
        }

        // 01 건설
        private IEnumerator Build()
        {
            foreach (ResourceType r in System.Enum.GetValues(typeof(ResourceType)))
            {
                if (r != ResourceType.Power)
                    Sim.Resources.AddStock(r, 400f); // 영상 동안 비용 걱정 없이
            }
            var core = Sim.Core;
            var focus = Center(core);
            _orbit.Restore(focus, 35f, 32f, 6.5f);
            _showCursor = true;
            yield return FadeTo(0f, 0.6f);
            Caption("01  건설", "모듈을 골라 정거장 면에 붙입니다 · 초록 = 가능 · 빨강 = 불가 · R 회전");
            yield return Wait(1.0f);
            yield return BuildOne(ModuleCategory.Life, "MD_Oxygen", true);
            yield return BuildOne(ModuleCategory.Power, "MD_Solar", false);
            yield return BuildOne(ModuleCategory.Power, "MD_Battery", false); // 새 게임엔 채굴 도킹이 이미 최대(1개)
            _build.Select(null);
            Still("01_build");
            yield return OrbitTo(focus, 75f, 28f, 7.5f, 2.5f);
        }

        private IEnumerator BuildOne(ModuleCategory category, string dataName, bool showRotate)
        {
            var tab = _buildMenu.FindTab(category);
            if (tab != null && _build.Category != category)
            {
                yield return ClickUi(tab);
                yield return Wait(0.3f);
            }
            ModuleData data = null;
            foreach (var m in _build.BuildableModules)
            {
                if (m != null && m.name == dataName)
                    data = m;
            }
            if (data == null)
                yield break;
            var button = _buildMenu.FindButton(data);
            if (button != null)
                yield return ClickUi(button);
            else
                _build.Select(data);
            yield return Wait(0.4f);

            // 정거장 면을 돌아다니며 놓을 자리 찾기 (사람이 고르는 것처럼)
            var candidates = FaceCandidates();
            bool placed = false;
            int tries = 0;
            foreach (var p in candidates)
            {
                if (++tries > 14)
                    break;
                yield return MoveCursor(p, 0.35f);
                yield return Wait(0.15f);
                if (!_build.HasTarget)
                    continue;
                if (_build.TargetResult != PlacementResult.Valid)
                {
                    yield return Wait(0.5f); // 빨강 고스트를 잠깐 보여 줌
                    continue;
                }
                yield return Wait(0.5f);
                if (showRotate)
                {
                    for (int r = 0; r < 4; r++)
                    {
                        yield return Tap(Key.R);
                        yield return Wait(0.45f);
                        if (_build.TargetResult == PlacementResult.Valid && r >= 1)
                            break;
                    }
                    if (_build.TargetResult != PlacementResult.Valid)
                        continue;
                }
                int before = CountModules();
                yield return Click();
                yield return Wait(0.6f);
                placed = CountModules() > before;
                if (placed)
                    break;
            }
            if (!placed)
                _build.Select(null);
            yield return Wait(0.4f);
        }

        /// <summary>카메라를 향한 빈 면 가운데의 화면 위치 (화면 가운데에 가까운 순). UI가 없는 가운데 영역만.</summary>
        private List<Vector2> FaceCandidates()
        {
            var cam = Camera.main;
            var list = new List<(Vector2 P, float D)>();
            var grid = Sim.Grid;
            var dirs = new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            foreach (var m in grid.Modules)
            {
                foreach (var c in m.Cells)
                {
                    foreach (var d in dirs)
                    {
                        if (grid.TryGetModule(c + d, out _))
                            continue;
                        if (Vector3.Dot((Vector3)d, cam.transform.forward) > -0.15f)
                            continue;
                        var world = GridConfig.CellToWorld(c) + (Vector3)d * (GridConfig.CellSize * 0.45f);
                        var sp = cam.WorldToScreenPoint(world);
                        if (sp.z <= 0f)
                            continue;
                        var p = new Vector2(sp.x, sp.y);
                        if (p.x < Screen.width * 0.22f || p.x > Screen.width * 0.72f || p.y < Screen.height * 0.3f || p.y > Screen.height * 0.82f)
                            continue;
                        list.Add((p, (p - center).sqrMagnitude));
                    }
                }
            }
            list.Sort((a, b) => a.D.CompareTo(b.D));
            var result = new List<Vector2>();
            foreach (var x in list)
                result.Add(x.P);
            return result;
        }

        // 02 자원 · 낮과 밤
        private IEnumerator ResourcesAndNight()
        {
            var day = Sim.DayNight;
            Sim.RestoreElapsed(Mathf.Max(0f, day.DayLength - day.Transition - 6f));
            Sim.Resources.RestoreBattery(Sim.Resources.BatteryCapacity * 0.8f);
            Caption("02  자원 · 낮과 밤", "밤에는 태양광 대신 배터리 · 배속 1x / 2x / 4x");
            var speed = Named<RectTransform>("Speed4");
            if (speed != null)
                yield return ClickUi(speed);
            else
                _clock.SetSpeedPreset(2);
            yield return MoveCursor(new Vector2(Screen.width * 0.8f, Screen.height * 0.62f), 0.8f); // 자원 패널 쪽
            var focus = Center(Sim.Core);
            float s = _t;
            while (_t < s + 6.5f)
            {
                float k = Smooth((_t - s) / 6.5f);
                _orbit.Restore(focus, Mathf.Lerp(75f, 150f, k), Mathf.Lerp(28f, 22f, k), Mathf.Lerp(7.5f, 8.5f, k));
                if (_t > s + 5f && _t < s + 5.1f)
                    Still("02_night");
                yield return Step();
            }
            _clock.Clock.SetPaused(true);
            Caption("시간이 흘러","모듈 22종 · 정거장이 자랐습니다");
            yield return FadeTo(1f, 0.8f);
        }

        private IEnumerator LoadShowcase()
        {
            SetFade(1f);
            _showCursor = false;
            yield return LoadScene(() => SaveManager.LoadAndPlay("slot1"), SceneNames.Game);
            foreach (ResourceType r in System.Enum.GetValues(typeof(ResourceType)))
            {
                if (r != ResourceType.Power)
                    Sim.Resources.AddStock(r, 400f);
            }
            _selection.Select(null);
        }

        // 03 위기 · 04 파손과 수리
        private IEnumerator Warning()
        {
            HideCaption();
            var research = Sim.Research;
            var defense = research.Find(ResearchCategory.Defense);
            if (defense != null && research.GetLevel(defense) < 1)
                research.SetLevel(defense, 1); // 조기 경보 (Lv.2는 파손 면역 20%라 수리 장면이 사라질 수 있음)
            Sim.ForceNextMeteorHits(3); // 1발이면 포탑 격추 · 실드에 막혀 파손이 없을 때가 있었음
            _clock.Clock.SetPaused(true);
            // 운석이 다음 이벤트로 뽑혀 경보가 뜰 때까지 시뮬레이션을 한 프레임 안에서 직접 돌림 (화면에 기다리는 시간이 남지 않게)
            Sim.Tick(1f); // 경보 시간(연구) 반영
            for (int attempt = 0; attempt < 60 && !(Sim.Events.Upcoming is MeteorEventData); attempt++)
            {
                Sim.Events.Postpone(0f);
                Sim.Events.RestoreTimer(Sim.Events.WarningLead + 1.5f);
                Sim.Tick(1f);
                Sim.Tick(1f);
            }
            // 경보는 15초 전부터라 떨어지기 몇 초 전까지 감음 (전개 속도)
            while (Sim.Events.Upcoming is MeteorEventData && Sim.Events.TimeUntilNext > 4.5f)
                Sim.Tick(1f);
            Debug.Log($"[PlayTour] 경보 준비: upcoming={Sim.Events.Upcoming?.name} 남은 {Sim.Events.TimeUntilNext:0.0}s lead={Sim.Events.WarningLead} 파손 {Sim.Damage.DamagedModules.Count}");
            yield return null;
            _clock.SetSpeedPreset(0);
            var target = FindMeteorTarget();
            var focus = target != null ? Center(target) : Center(Sim.Core);
            _orbit.Restore(focus, 210f, 30f, 11f);
            yield return FadeTo(0f, 0.6f);
            Caption("03  위기 · 조기 경보", "운석 · 태양 폭풍이 찾아옵니다 · 연구하면 미리 경보");
            float s = _t;
            int damagedBefore = Sim.Damage.DamagedModules.Count;
            while (_t < s + 8f && Sim.Damage.DamagedModules.Count <= damagedBefore)
            {
                float k = Smooth((_t - s) / 8f);
                _orbit.Restore(focus, Mathf.Lerp(210f, 240f, k), 30f, Mathf.Lerp(11f, 9f, k));
                if (_t > s + 1.5f && _t < s + 1.6f)
                    Still("03_warning");
                yield return Step();
            }
            Debug.Log($"[PlayTour] 경보 장면 끝: {_t - s:0.0}s 파손 {Sim.Damage.DamagedModules.Count} (전 {damagedBefore})");
            yield return Wait(1.5f);

            // 04 수리
            ModuleInstance hit = null;
            foreach (var info in Sim.Damage.DamagedModules)
                hit = info.Module;
            if (hit == null)
            {
                // 모두 막혔으면 화면 가운데 근처 모듈을 파손시켜 수리 장면을 이어감
                hit = FindModule("MD_ArmorBulkhead") ?? FindModule("MD_Storage") ?? Neighbor(Sim.Core);
                if (hit == null || !Sim.Damage.Damage(hit))
                {
                    Debug.LogWarning("[PlayTour] 운석 파손 없음 → 수리 장 건너뜀");
                    yield break;
                }
            }
            Caption("04  파손과 수리", "맞은 모듈은 멈추고, 오래 두면 번지거나 파괴됩니다 · 클릭해 수리");
            _showCursor = true;
            var hf = Center(hit);
            yield return OrbitTo(hf, 240f, 32f, 8f, 1.2f);
            yield return ClickWorld(hit);
            yield return Wait(0.6f);
            var repair = _actions.RepairButtonIfShown;
            if (repair != null)
                yield return ClickUi(repair);
            else
                Sim.TryRepair(hit);
            _clock.SetSpeedPreset(2);
            Still("04_repair");
            float r0 = _t;
            while (_t < r0 + 2.5f && Sim.Damage.TryGetInfo(hit, out _))
                yield return Step();
            if (Sim.Damage.TryGetInfo(hit, out _))
                Sim.Damage.CompleteNow(hit); // 수리 진행을 잠깐 보여 준 뒤 마무리 (전개 속도)
            _clock.SetSpeedPreset(0);
            yield return Wait(1.0f);
            _selection.Select(null);
        }

        private ModuleInstance FindMeteorTarget()
        {
            var markers = FindObjectsByType<RectTransform>(FindObjectsSortMode.None);
            // 미리 정한 대상은 시뮬레이션 내부라, 보이는 표시가 없으면 코어 근처를 비춘다
            return null;
        }

        // 05 노후화와 정비
        private IEnumerator Wear()
        {
            var m = FindModule("MD_Habitat") ?? Sim.Core;
            if (Sim.Durability.TryGetInfo(m, out var info) && info.Current > 32f)
                Sim.Durability.ApplyImpact(m, info.Current - 30f);
            _clock.SetSpeedPreset(0);
            Caption("05  노후화와 정비", "모듈은 시간이 지나며 낡습니다 · 정비로 되살리고, 많이 낡으면 재건축");
            _showCursor = true;
            yield return OrbitTo(Center(m), 300f, 30f, 8f, 1.4f);
            yield return Wait(1.0f);
            yield return ClickWorld(m);
            yield return Wait(0.8f);
            var maintain = _actions.MaintainButtonIfShown;
            if (maintain != null)
                yield return ClickUi(maintain);
            else
                Sim.TryMaintain(m);
            yield return Wait(2.2f);
            Still("05_wear");
            _selection.Select(null);
            yield return Wait(0.5f);
        }

        // 06 방어
        private IEnumerator Defense()
        {
            _showCursor = false;
            var fx = FindFirstObjectByType<MeteorFx>();
            var turret = FindModule("MD_Turret");
            var shield = FindModule("MD_Shield");
            Caption("06  방어", "포탑이 운석을 격추하고, 실드가 튕겨 냅니다");
            if (turret != null && fx != null)
            {
                var near = Neighbor(turret) ?? turret;
                yield return OrbitTo(Center(Sim.Core), 300f, 30f, 11f, 1.2f); // 가까이 붙으면 정거장 안으로 들어가 포탑이 안 보였음
                fx.Preview(new MeteorFlight { Target = near, InterceptedAt = near });
                yield return Wait(1.6f);
                Still("06_turret");
                yield return Wait(1.6f);
            }
            if (shield != null && fx != null)
            {
                var near = Neighbor(shield) ?? shield;
                yield return OrbitTo(Center(Sim.Core), 30f, 30f, 11f, 1.2f);
                fx.Preview(new MeteorFlight { Target = near, DeflectedBy = shield });
                yield return Wait(3.2f);
            }
        }

        // 07 연구
        private IEnumerator Research()
        {
            _showCursor = true;
            yield return OrbitTo(Center(Sim.Core), 45f, 30f, 12f, 1.0f);
            Caption("07  연구", "유지보수 · 방어 · 생산 · 에너지 · 거주 · 건설, 레벨을 올려 정거장을 강화");
            var launcher = ButtonWithText("연구", "ResearchPanel");
            if (launcher != null)
                yield return ClickUi(launcher);
            else
                FindFirstObjectByType<ResearchPanel>()?.Open();
            yield return Wait(1.0f);
            var row = ButtonWithText("생산", "ResearchWindow") ?? ButtonWithText("에너지", "ResearchWindow");
            if (row != null)
                yield return ClickUi(row);
            yield return Wait(0.6f);
            var start = Named<RectTransform>("연구 시작");
            if (start != null)
                yield return ClickUi(start);
            yield return Wait(2.0f);
            Still("07_research");
            var close = ButtonWithText("닫기", "ResearchWindow");
            if (close != null)
                yield return ClickUi(close);
            else
                FindFirstObjectByType<ResearchPanel>()?.Close();
            yield return Wait(0.4f);
        }

        // 08 주민 명단
        private IEnumerator Roster()
        {
            Caption("08  주민 명단", "주민마다 이름 · 특성 · 집이 있고, 특성이 생산과 만족도에 영향을 줍니다");
            var launcher = ButtonWithText("주민", "RosterPanel");
            if (launcher != null)
                yield return ClickUi(launcher);
            else
                FindFirstObjectByType<RosterPanel>()?.Open();
            yield return Wait(1.0f);
            yield return MoveCursor(new Vector2(Screen.width * 0.45f, Screen.height * 0.55f), 0.8f);
            yield return Wait(2.2f);
            Still("08_roster");
            var close = ButtonWithText("닫기", "RosterWindow");
            if (close != null)
                yield return ClickUi(close);
            else
                FindFirstObjectByType<RosterPanel>()?.Close();
            yield return Wait(0.4f);
        }

        // 09 1인칭 내부
        private IEnumerator EnterInterior()
        {
            var room = FindModule("MD_Recreation") ?? FindModule("MD_Habitat") ?? Sim.Core;
            _showCursor = true;
            Caption("09  1인칭 내부", "모듈을 고르고 [들어가기] · 정거장 배치 그대로 안을 걸어 다닙니다");
            yield return OrbitTo(Center(room), 200f, 30f, 8f, 1.2f);
            yield return ClickWorld(room);
            yield return Wait(0.7f);
            var enter = _actions.EnterButtonIfShown;
            if (enter != null)
                yield return ClickUi(enter);
            else
                _interior.Enter(room);
            _showCursor = false;
            while (!_interior.IsInside)
                yield return Step();
            yield return Wait(0.8f);
            var player = FindFirstObjectByType<FirstPersonController>();
            // 방 안 주민에게 걸어가 반응 보기
            var figure = NearestResident(player.transform.position);
            if (figure != null)
            {
                yield return WalkTo(player, figure.transform.position, 1.9f, 6f);
                yield return LookAt(player, figure.TagPoint, 0.8f);
                yield return Wait(2.0f);
                Still("09_interior");
            }
            yield return LookAround(player, 60f, 1.6f);
        }

        // 10 주민 부탁
        private IEnumerator Request()
        {
            var player = FindFirstObjectByType<FirstPersonController>();
            var requests = FindFirstObjectByType<InteriorRequests>();
            Caption("10  주민 부탁", "주민의 부탁을 들어주면 만족도가 오릅니다");
            if (requests == null || !(requests.ForceRequest(RequestKind.Talk) || requests.ForceRequest()))
                yield break;
            yield return Wait(0.6f);
            var use = NearestInteractable(player.transform.position, i => i.GetComponent<ResidentFigure>() != null && i.Completed != null);
            if (use == null)
                yield break;
            yield return InteractWith(player, use, 1.4f);
            yield return Wait(2.2f);
            Still("10_request");
        }

        // 11 사고 대응
        private IEnumerator Incident()
        {
            var player = FindFirstObjectByType<FirstPersonController>();
            var incidents = FindFirstObjectByType<InteriorIncidents>();
            Caption("11  사고 대응", "누출 · 차단기 · 화재 · 환기, 제한 시간 안에 직접 처리합니다");
            if (incidents == null)
                yield break;
            if (!incidents.ForceIncident(IncidentKind.Fire))
                yield break;
            yield return Wait(1.2f);
            var fire = FindNamedInteractable("IncidentFire");
            if (fire != null)
                yield return LookAt(player, Bounds(fire).center, 0.8f);
            yield return Wait(1.0f);
            Still("11_fire");
            var bottle = FindNamedInteractable("IncidentExtinguisher") ?? FindNamedInteractable("ExtBottle");
            if (bottle != null)
                yield return InteractWith(player, bottle, 1.2f);
            fire = FindNamedInteractable("IncidentFire");
            if (fire != null)
                yield return InteractWith(player, fire, 1.5f);
            yield return Wait(2.2f);
        }

        // 12 현장 수리
        private IEnumerator FieldRepair()
        {
            var player = FindFirstObjectByType<FirstPersonController>();
            var room = _interior.IsInside ? CurrentRoom(player) : null;
            if (room == null)
                yield break;
            Caption("12  현장 수리", "파손된 방은 안에서 직접 고칠 수 있습니다 (금속 사용)");
            Sim.Damage.Damage(room);
            yield return Wait(1.2f);
            for (int i = 0; i < 4; i++)
            {
                var point = NearestInteractable(player.transform.position, x => x.name.StartsWith("Damage") && x.Completed != null && (x.Blocked == null || x.Blocked() == null));
                if (point == null)
                    break;
                yield return InteractWith(player, point, 1.3f);
                yield return Wait(0.4f);
                if (i == 0)
                    Still("12_field");
            }
            yield return Wait(1.0f);
        }

        // 13 휴대 패드
        private IEnumerator Pad()
        {
            Caption("13  휴대 패드", "정거장 지도 · 다른 방으로 이동 · 건설 · 연구 · 주민 관리를 손 안에서");
            yield return Tap(Key.G);
            yield return Wait(1.0f);
            yield return Tap(Key.Tab); // 든 채 배속 순환 키 = 화면 확대
            yield return Wait(1.2f);
            _showCursor = true;
            foreach (var tab in new[] { "건설", "연구", "주민", "관리" })
            {
                var rt = PadTab(tab);
                if (rt == null)
                    continue;
                yield return ClickUi(rt);
                yield return Wait(1.6f);
                if (tab == "건설")
                    Still("13_pad");
            }
            yield return MoveCursor(new Vector2(Screen.width * 0.5f, Screen.height * 0.78f), 0.8f); // 홀로그램 지도 위
            yield return Wait(1.5f);
            _showCursor = false;
            yield return Tap(Key.Tab);
            yield return Wait(0.6f);
            yield return Tap(Key.G);
            yield return Wait(0.6f);
        }

        private IEnumerator Outro()
        {
            HideCaption();
            yield return Tap(Key.Escape); // 밖으로
            while (_interior.IsInside)
                yield return Step();
            yield return Wait(0.6f);
            yield return OrbitTo(Center(Sim.Core), 120f, 24f, 14f, 3f);
            yield return FadeTo(1f, 1.0f);
            _titleSub.text = "우주정거장 건설 경영  ·  1인칭 정거장 탐험";
            float s = _t;
            while (_t < s + 5f)
            {
                float k = _t - s;
                _titleGroup.alpha = Mathf.Clamp01(k / 1f) - Mathf.Clamp01((k - 4.2f) / 0.8f);
                if (k > 3.5f)
                    AudioService.Instance?.DuckMusic(0f, 0.8f);
                yield return Step();
            }
        }

        // ---------------- 1인칭 조작 ----------------

        private float LookFactor(FirstPersonController player)
        {
            var field = typeof(FirstPersonController).GetField("_lookDegreesPerPixel", BindingFlags.Instance | BindingFlags.NonPublic);
            float perPixel = field != null ? (float)field.GetValue(player) : 0.12f;
            return Mathf.Max(0.001f, perPixel * GameSettings.OrbitSensitivity);
        }

        private static float Pitch(FirstPersonController player)
        {
            float x = player.Eye.localEulerAngles.x;
            return x > 180f ? x - 360f : x;
        }

        /// <summary>마우스를 움직여 이 지점을 바라봄 (최대 초당 120도).</summary>
        private IEnumerator LookAt(FirstPersonController player, Vector3 world, float maxSeconds)
        {
            float factor = LookFactor(player);
            float s = _t;
            while (_t < s + maxSeconds)
            {
                var eye = player.Eye.position;
                var d = world - eye;
                var flat = new Vector3(d.x, 0f, d.z);
                float wantYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float wantPitch = -Mathf.Atan2(d.y, Mathf.Max(0.01f, flat.magnitude)) * Mathf.Rad2Deg;
                float yawErr = Mathf.DeltaAngle(player.transform.eulerAngles.y, wantYaw);
                float pitchErr = wantPitch - Pitch(player);
                if (Mathf.Abs(yawErr) < 0.8f && Mathf.Abs(pitchErr) < 0.8f)
                    break;
                float dt = Mathf.Max(1f / 60f, Time.unscaledDeltaTime);
                float yawStep = Mathf.Clamp(yawErr * 0.25f, -120f * dt, 120f * dt);
                float pitchStep = Mathf.Clamp(pitchErr * 0.25f, -90f * dt, 90f * dt);
                _input.AddDelta(new Vector2(yawStep / factor, -pitchStep / factor));
                yield return Step();
            }
        }

        private IEnumerator LookAround(FirstPersonController player, float degrees, float seconds)
        {
            float factor = LookFactor(player);
            float s = _t, done = 0f;
            while (_t < s + seconds)
            {
                float k = Smooth((_t - s) / seconds);
                float want = Mathf.Sin(k * Mathf.PI * 2f) * degrees * 0.5f;
                _input.AddDelta(new Vector2((want - done) / factor, 0f));
                done = want;
                yield return Step();
            }
        }

        /// <summary>W를 누른 채 방향을 맞추며 걸어감 (stop 거리 안에 들면 멈춤).</summary>
        private IEnumerator WalkTo(FirstPersonController player, Vector3 target, float stop, float maxSeconds)
        {
            float factor = LookFactor(player);
            float s = _t;
            while (_t < s + maxSeconds)
            {
                var d = target - player.transform.position;
                d.y = 0f;
                if (d.magnitude <= stop)
                    break;
                float wantYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float yawErr = Mathf.DeltaAngle(player.transform.eulerAngles.y, wantYaw);
                float dt = Mathf.Max(1f / 60f, Time.unscaledDeltaTime);
                float yawStep = Mathf.Clamp(yawErr * 0.2f, -100f * dt, 100f * dt);
                float pitchErr = 5f - Pitch(player); // 걸을 땐 살짝 아래
                _input.AddDelta(new Vector2(yawStep / factor, -(pitchErr * 0.1f) / factor));
                if (Mathf.Abs(yawErr) < 30f)
                    _input.Press(Key.W);
                else
                    _input.Release(Key.W);
                yield return Step();
            }
            _input.Release(Key.W);
            yield return Wait(0.2f);
        }

        /// <summary>
        /// 물건 앞까지 걸어가 바라보고 F를 길게 (다 차면 실행). 가구에 막혀 못 가면 짧게 끊어(페이드) 물건 앞에 선다.
        /// 시선 광선(InteriorMode와 같은 2.6m)이 물건에 맞는 조준점을 찾은 뒤 누른다.
        /// </summary>
        private IEnumerator InteractWith(FirstPersonController player, InteriorInteractable item, float stop)
        {
            if (item == null)
                yield break;
            var center = Bounds(item).center;
            yield return WalkTo(player, center, stop, 4f);
            var flat = player.transform.position - center;
            flat.y = 0f;
            if (flat.magnitude > stop + 0.6f)
            {
                // 막힘: 물건 정면(벽 소품은 앞쪽, 그 밖엔 지금 방향)에 서기
                var normal = item.transform.forward;
                normal.y = 0f;
                if (normal.sqrMagnitude < 0.01f || Vector3.Dot(normal, flat) < 0f)
                    normal = flat.sqrMagnitude > 0.01f ? flat : -player.transform.forward;
                normal.Normalize();
                yield return FadeTo(1f, 0.15f);
                var feet = center + normal * stop;
                feet.y = player.transform.position.y;
                var d = center - feet;
                player.Teleport(feet, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 10f);
                yield return Step();
                yield return FadeTo(0f, 0.15f);
            }
            var aims = new[] { center, center + Vector3.up * 0.15f, center - Vector3.up * 0.15f, item.transform.position };
            var aim = center;
            foreach (var a in aims)
            {
                yield return LookAt(player, a, 0.9f);
                if (Aimed(player, item))
                {
                    aim = a;
                    break;
                }
            }
            if (!Aimed(player, item))
                Debug.LogWarning($"[PlayTour] 조준 실패: {item.name} 거리 {Vector3.Distance(player.Eye.position, center):0.0}m");
            yield return Wait(0.3f);
            _input.Press(Key.F);
            float hold = item.HoldSeconds + 0.4f;
            float s = _t;
            while (_t < s + hold && item != null && item.Completed != null)
            {
                yield return LookAt(player, aim, 0.02f); // 시선 유지
                yield return Step();
            }
            _input.Release(Key.F);
            yield return Wait(0.3f);
        }

        /// <summary>InteriorMode와 같은 시선 광선이 이 물건에 닿는지.</summary>
        private static bool Aimed(FirstPersonController player, InteriorInteractable item)
        {
            var eye = player.Eye;
            if (!Physics.Raycast(eye.position, eye.forward, out var hit, 2.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            return hit.collider.GetComponentInParent<InteriorInteractable>() == item;
        }

        private static Bounds Bounds(Component c)
        {
            var col = c.GetComponentInChildren<Collider>();
            if (col != null)
                return col.bounds;
            var r = c.GetComponentInChildren<Renderer>();
            return r != null ? r.bounds : new Bounds(c.transform.position, Vector3.one * 0.3f);
        }

        private ResidentFigure NearestResident(Vector3 from)
        {
            var residents = FindFirstObjectByType<InteriorResidents>();
            if (residents == null)
                return null;
            ResidentFigure best = null;
            float bestD = float.MaxValue;
            foreach (var f in residents.Figures)
            {
                if (f == null)
                    continue;
                float d = (f.transform.position - from).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = f;
                }
            }
            return best;
        }

        private static InteriorInteractable NearestInteractable(Vector3 from, System.Func<InteriorInteractable, bool> match)
        {
            InteriorInteractable best = null;
            float bestD = float.MaxValue;
            foreach (var i in FindObjectsByType<InteriorInteractable>(FindObjectsSortMode.None))
            {
                if (i == null || !i.isActiveAndEnabled || !match(i))
                    continue;
                float d = (i.transform.position - from).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        private static InteriorInteractable FindNamedInteractable(string name)
        {
            foreach (var i in FindObjectsByType<InteriorInteractable>(FindObjectsSortMode.None))
            {
                if (i == null || !i.isActiveAndEnabled)
                    continue;
                for (var t = i.transform; t != null; t = t.parent)
                {
                    if (t.name.StartsWith(name))
                        return i;
                }
            }
            return null;
        }

        private ModuleInstance CurrentRoom(FirstPersonController player)
        {
            var cell = GridConfig.WorldToCell(player.transform.position); // 내부는 별도 좌표 — 방 이름표 대신 가장 가까운 사고 · 수리용으로 InteriorMode 기준 사용
            var incidents = FindFirstObjectByType<InteriorIncidents>();
            return incidents != null ? incidents.CurrentRoom : null;
        }

        // ---------------- 바깥 조작 ----------------

        private IEnumerator ClickWorld(ModuleInstance module)
        {
            var cam = Camera.main;
            var sp = cam.WorldToScreenPoint(Center(module));
            yield return MoveCursor(new Vector2(sp.x, sp.y), 0.6f);
            yield return Wait(0.15f);
            yield return Click();
        }

        private Vector3 Center(ModuleInstance module)
        {
            if (module != null && _station.TryGetView(module, out var view) && view != null)
            {
                var renderers = view.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var b = renderers[0].bounds;
                    foreach (var r in renderers)
                        b.Encapsulate(r.bounds);
                    return b.center;
                }
            }
            if (module == null)
                return Vector3.zero;
            var sum = Vector3.zero;
            foreach (var c in module.Cells)
                sum += GridConfig.CellToWorld(c);
            return sum / module.Cells.Count;
        }

        private ModuleInstance FindModule(string dataName)
        {
            foreach (var m in Sim.Grid.Modules)
            {
                if (m.Data != null && m.Data.name == dataName)
                    return m;
            }
            return null;
        }

        private ModuleInstance Neighbor(ModuleInstance module)
        {
            var list = new List<ModuleInstance>();
            Sim.Grid.GetNeighborModules(module, list);
            return list.Count > 0 ? list[0] : null;
        }

        private int CountModules()
        {
            int n = 0;
            foreach (var _ in Sim.Grid.Modules)
                n++;
            return n;
        }

        private IEnumerator OrbitTo(Vector3 focus, float yaw, float pitch, float distance, float seconds)
        {
            seconds = Mathf.Max(0.3f, seconds * Pace);
            var rig = _orbit.Rig;
            Vector3 f0 = rig.Focus;
            float y0 = rig.Yaw, p0 = rig.Pitch, d0 = rig.Distance;
            float s = _t;
            while (_t < s + seconds)
            {
                float k = Smooth((_t - s) / seconds);
                _orbit.Restore(Vector3.Lerp(f0, focus, k), Mathf.LerpAngle(y0, yaw, k), Mathf.Lerp(p0, pitch, k), Mathf.Lerp(d0, distance, k));
                yield return Step();
            }
            _orbit.Restore(focus, yaw, pitch, distance);
        }

        // ---------------- 커서 · 키 ----------------

        private IEnumerator MoveCursor(Vector2 to, float seconds)
        {
            seconds = Mathf.Max(0.15f, seconds * Pace);
            var from = _input.Position;
            float s = _t;
            while (_t < s + seconds)
            {
                _input.Position = Vector2.Lerp(from, to, Smooth((_t - s) / seconds));
                yield return Step();
            }
            _input.Position = to;
            yield return Step();
        }

        private IEnumerator Click()
        {
            _input.LeftButton = true;
            yield return Step();
            yield return Step();
            _input.LeftButton = false;
            yield return Step();
            yield return Step();
        }

        private IEnumerator ClickUi(RectTransform target)
        {
            yield return MoveCursor(ScreenOf(target), 0.55f);
            yield return Wait(0.15f);
            yield return Click();
        }

        private IEnumerator Tap(Key key)
        {
            _input.Press(key);
            yield return Step();
            yield return Step();
            _input.Release(key);
            yield return Step();
        }

        private static Vector2 ScreenOf(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            canvas = canvas != null ? canvas.rootCanvas : null;
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
        }

        /// <summary>글자에 text가 들어간 보이는 버튼 (under가 있으면 그 이름의 조상 아래만).</summary>
        private static RectTransform ButtonWithText(string text, string under = null)
        {
            foreach (var b in FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                if (b == null || !b.isActiveAndEnabled || !b.interactable)
                    continue;
                var label = b.GetComponentInChildren<TMP_Text>();
                if (label == null || !label.text.Contains(text))
                    continue;
                if (under != null && !HasAncestor(b.transform, under))
                    continue;
                return (RectTransform)b.transform;
            }
            return null;
        }

        private static bool HasAncestor(Transform t, string name)
        {
            for (; t != null; t = t.parent)
            {
                if (t.name == name)
                    return true;
            }
            return false;
        }

        private static T Named<T>(string name) where T : Component
        {
            foreach (var c in FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                if (c != null && c.name == name && c.gameObject.activeInHierarchy)
                    return c;
            }
            return null;
        }

        private static RectTransform PadTab(string name)
        {
            foreach (var b in FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                if (b != null && b.isActiveAndEnabled && b.name == name && HasAncestor(b.transform, "PadScreen"))
                    return (RectTransform)b.transform;
            }
            return null;
        }

        // ---------------- 화면 (장 제목 · 페이드 · 커서 · 제목 카드) ----------------

        private void BuildOverlay()
        {
            var go = new GameObject("TourOverlay", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _overlay = go.AddComponent<Canvas>();
            _overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlay.sortingOrder = 1000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var root = (RectTransform)go.transform;

            // 장 제목 (왼쪽 가운데 — 위 가운데는 경보 배너 · 주민 말풍선, 아래는 건설 메뉴 · 안내 줄)
            var cap = NewRect("Caption", root);
            cap.anchorMin = cap.anchorMax = cap.pivot = new Vector2(0f, 0.5f);
            cap.anchoredPosition = new Vector2(24f, 150f);
            cap.sizeDelta = new Vector2(620f, 112f);
            var bg = cap.gameObject.AddComponent<Image>();
            bg.color = new Color(0.02f, 0.06f, 0.1f, 0.86f);
            bg.raycastTarget = false;
            var line = NewRect("Line", cap);
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 0f);
            line.sizeDelta = new Vector2(0f, 3f);
            var li = line.gameObject.AddComponent<Image>();
            li.color = new Color(0.31f, 0.85f, 1f);
            li.raycastTarget = false;
            _captionGroup = cap.gameObject.AddComponent<CanvasGroup>();
            _captionGroup.alpha = 0f;
            _captionTitle = Label(cap, 32f, new Vector2(0f, 26f), new Vector2(580f, 44f));
            _captionTitle.fontStyle = FontStyles.Bold;
            _captionTitle.color = new Color(0.31f, 0.85f, 1f);
            _captionTitle.alignment = TextAlignmentOptions.Left;
            _captionBody = Label(cap, 20f, new Vector2(0f, -20f), new Vector2(580f, 56f));
            _captionBody.alignment = TextAlignmentOptions.TopLeft;
            _captionBody.textWrappingMode = TextWrappingModes.Normal;

            var fadeRt = NewRect("Fade", root);
            fadeRt.anchorMin = Vector2.zero;
            fadeRt.anchorMax = Vector2.one;
            fadeRt.offsetMin = fadeRt.offsetMax = Vector2.zero;
            _fade = fadeRt.gameObject.AddComponent<Image>();
            _fade.color = Color.black;
            _fade.raycastTarget = false;

            var flashRt = NewRect("Flash", root);
            flashRt.anchorMin = Vector2.zero;
            flashRt.anchorMax = Vector2.one;
            flashRt.offsetMin = flashRt.offsetMax = Vector2.zero;
            _flash = flashRt.gameObject.AddComponent<Image>();
            _flash.color = new Color(1f, 1f, 1f, 0f);
            _flash.raycastTarget = false;

            // 제목 카드 (페이드 위)
            var title = NewRect("Title", root);
            title.anchorMin = Vector2.zero;
            title.anchorMax = Vector2.one;
            title.offsetMin = title.offsetMax = Vector2.zero;
            _titleGroup = title.gameObject.AddComponent<CanvasGroup>();
            _titleGroup.alpha = 0f;
            var logo = Label(title, 120f, new Vector2(0f, 60f), new Vector2(1800f, 160f));
            logo.fontStyle = FontStyles.Bold;
            logo.characterSpacing = 14f;
            logo.color = new Color(0.87f, 0.97f, 1f);
            logo.text = "ANOTHER EARTH";
            if (logo.fontMaterial != null)
            {
                var mat = new Material(logo.fontMaterial);
                mat.EnableKeyword("UNDERLAY_ON");
                mat.SetColor("_UnderlayColor", new Color(0.31f, 0.85f, 1f, 0.75f));
                mat.SetFloat("_UnderlaySoftness", 0.6f);
                mat.SetFloat("_UnderlayDilate", 0.4f);
                logo.fontMaterial = mat;
            }
            _titleSub = Label(title, 40f, new Vector2(0f, -70f), new Vector2(1600f, 60f));
            var meta = Label(title, 26f, new Vector2(0f, -150f), new Vector2(1600f, 40f));
            meta.text = "v0.11  ·  Windows  ·  2026";
            meta.color = new Color(0.56f, 0.64f, 0.72f);

            // 가짜 커서 (맨 위)
            _cursor = NewRect("Cursor", root);
            _cursor.anchorMin = _cursor.anchorMax = new Vector2(0.5f, 0.5f);
            _cursor.pivot = new Vector2(0f, 1f);
            _cursor.sizeDelta = new Vector2(30f, 44f);
            var ci = _cursor.gameObject.AddComponent<RawImage>();
            ci.texture = CursorTexture();
            ci.raycastTarget = false;
            _cursor.gameObject.SetActive(false);
        }

        private static Texture2D CursorTexture()
        {
            const int w = 30, h = 44;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            // 화살표: 끝(0,0) · (0,36) · (25,27) — 위가 y=0
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool outer = InTri(x, y, 0f, -2f, 0f, 41f, 29f, 29f) || InStem(x, y, 1.6f);
                    bool inner = InTri(x, y, 2f, 3f, 2f, 34f, 22f, 26f) || InStem(x, y, 0f);
                    var c = inner ? new Color32(255, 255, 255, 255) : outer ? new Color32(10, 14, 20, 255) : new Color32(0, 0, 0, 0);
                    px[(h - 1 - y) * w + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        private static bool InStem(int x, int y, float pad)
        {
            // 꼬리: (11,28)에서 (17,41) 방향 막대
            var a = new Vector2(11f, 28f);
            var b = new Vector2(17f, 40f);
            var p = new Vector2(x + 0.5f, y + 0.5f);
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).magnitude <= 2.2f + pad;
        }

        private static bool InTri(int x, int y, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float px = x + 0.5f, py = y + 0.5f;
            float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            float d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            float d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private TMP_Text Label(RectTransform parent, float size, Vector2 pos, Vector2 box)
        {
            var rt = NewRect("Text", parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null)
                t.font = _font;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        private Coroutine _captionFade;

        /// <summary>장 제목: 번호가 붙은 장이면 트레일러식 전환 (흰 플래시 + 효과음 + 왼쪽에서 미끄러져 들어옴).</summary>
        private void Caption(string title, string body)
        {
            _captionTitle.text = title;
            _captionBody.text = body;
            if (_captionFade != null)
                StopCoroutine(_captionFade);
            bool chapter = title.Length > 0 && char.IsDigit(title[0]);
            _captionFade = StartCoroutine(CaptionIn(chapter));
            if (chapter)
            {
                StartCoroutine(Flash());
                AudioService.TryPlay(l => l.UiOpen, 0.7f);
            }
        }

        private IEnumerator CaptionIn(bool slide)
        {
            var rt = (RectTransform)_captionGroup.transform;
            float s = 0f;
            const float seconds = 0.28f;
            while (s < seconds)
            {
                s += Time.unscaledDeltaTime;
                float k = Smooth(s / seconds);
                _captionGroup.alpha = k;
                rt.anchoredPosition = new Vector2(slide ? Mathf.Lerp(-90f, 24f, k) : 24f, rt.anchoredPosition.y);
                yield return null;
            }
            _captionGroup.alpha = 1f;
            rt.anchoredPosition = new Vector2(24f, rt.anchoredPosition.y);
        }

        private IEnumerator Flash()
        {
            float s = 0f;
            const float seconds = 0.28f;
            while (s < seconds)
            {
                s += Time.unscaledDeltaTime;
                var c = _flash.color;
                c.a = 0.55f * (1f - s / seconds);
                _flash.color = c;
                yield return null;
            }
            _flash.color = new Color(1f, 1f, 1f, 0f);
        }

        private void HideCaption()
        {
            if (_captionFade != null)
                StopCoroutine(_captionFade);
            _captionFade = StartCoroutine(FadeGroup(_captionGroup, 0f, 0.3f));
        }

        private static IEnumerator FadeGroup(CanvasGroup g, float to, float seconds)
        {
            float from = g.alpha, s = 0f;
            while (s < seconds)
            {
                s += Time.unscaledDeltaTime;
                g.alpha = Mathf.Lerp(from, to, s / seconds);
                yield return null;
            }
            g.alpha = to;
        }

        private IEnumerator FadeTo(float to, float seconds)
        {
            float from = _fade.color.a, s = _t;
            while (_t < s + seconds)
            {
                SetFade(Mathf.Lerp(from, to, (_t - s) / seconds));
                yield return Step();
            }
            SetFade(to);
        }

        private void SetFade(float a)
        {
            var c = _fade.color;
            c.a = Mathf.Clamp01(a);
            _fade.color = c;
        }

        private void Still(string name)
        {
            if (!string.IsNullOrEmpty(StillFolder))
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(StillFolder, $"tour_{name}.png"));
        }

        // ---------------- 시간 ----------------

        private IEnumerator Step()
        {
            yield return null;
            _t += Time.unscaledDeltaTime;
        }

        /// <summary>
        /// 전개 속도 (사용자 피드백 2026-10-12: 너무 느림 → 대기 · 커서 · 카메라 이동을 이 비율로 줄임).
        /// 판정용 짧은 대기(고스트 갱신 등)도 최소 2프레임은 남도록 <see cref="Wait"/>에서 하한을 둔다.
        /// </summary>
        private const float Pace = 0.55f;

        private IEnumerator Wait(float seconds)
        {
            float end = _t + Mathf.Max(0.07f, seconds * Pace);
            while (_t < end)
                yield return Step();
        }

        private static float Smooth(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3f - 2f * k);
        }
    }
}
#endif
