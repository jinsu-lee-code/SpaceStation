using System.Collections;
using System.IO;
using System.Linq;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Interior;
using SpaceStation.Save;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 지스타 시연 세션 (사용자 2026-11-05 "10~15분의 짧은 시간에 압축된 경험"): 빌드 폴더 StreamingAssets/Demo/demo_save.json이 있을 때만 켜짐 (일반 빌드 · 에디터 Play는 그대로).
    /// 시작 화면 → 완성된 정거장 세이브에서 시작 → 15분 타이머 + 체험 안내 카드 (짓기 · 합성 · 들어가기, 대표 체험 바로 가기 F6 ~ F9) → 끝 화면 → 시작 화면으로. 자원은 계속 채워 줌 (실패 없이 마음껏 짓게).
    /// 입력이 없으면 처음으로 되돌림 (다음 관람객). 진행 요원 키: Ctrl+Shift+R 처음으로 · Ctrl+Shift+T 5분 더 · Ctrl+Shift+Q 종료.
    /// </summary>
    public sealed class GStarDemo : MonoBehaviour
    {
        private const float SessionSeconds = 15f * 60f;
        private const float IdleSeconds = 150f;
        private const float IdleWarnSeconds = 20f;
        private const float EndSeconds = 20f;
        private const float RefillGap = 2f;
        private const string Slot = "slot3";
#if UNITY_EDITOR
        public const string EditorSaveKey = "GStarDemoSave";
#endif

        private static readonly (Key key, string module, string label)[] Jumps =
        {
            (Key.F6, "MD_Evo_DroneDepot", "드론 정비창 옥상 드론 쇼"),
            (Key.F7, "MD_Evo_FoodFactory", "식량 공장 관람차"),
            (Key.F8, "MD_Evo_GrandTorus", "대형 토러스 은하철도"),
            (Key.F9, "MD_Evo_Fortress", "요새 포대 불꽃놀이"),
        };

        private string _savePath;
        private Canvas _canvas;
        private HoloArt _art;
        private HoloUi _ui;
        private GameObject _attract, _hud, _card, _end, _idle;
        private TMP_Text _prompt, _timer, _steps, _endBody, _idleText, _error;
        private float _timeLeft, _lastInput, _endLeft, _nextRefill;
        private bool _inGame, _ended, _leaving, _jumping;
        private int _baseModules, _baseSyntheses;
        private bool _doneBuilt, _doneSynth, _doneInside;
        private StationController _station;
        private InteriorMode _interior;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Demo", "demo_save.json");
#if UNITY_EDITOR
            path = UnityEditor.EditorPrefs.GetString(EditorSaveKey, "");
#endif
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;
            var go = new GameObject("GStarDemo");
            DontDestroyOnLoad(go);
            var demo = go.AddComponent<GStarDemo>();
            demo._savePath = path;
            demo.Setup(SceneManager.GetActiveScene());
        }

        private void Awake() => SceneManager.sceneLoaded += HandleSceneLoaded;

        private void OnDestroy() => SceneManager.sceneLoaded -= HandleSceneLoaded;

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single)
                Setup(scene);
        }

        private void Setup(Scene scene)
        {
            _leaving = false;
            _jumping = false;
            var skin = FindFirstObjectByType<HoloSkin>(FindObjectsInactive.Include);
            if (skin != null && _art == null)
                _art = skin.Art;
            if (_canvas == null)
                BuildUi();
            if (scene.name != SceneNames.Game)
            {
                var menu = FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
                if (menu != null)
                    menu.gameObject.SetActive(false);   // 시작 화면이 대신함 (메뉴 뒤 정거장 배경은 메뉴 UI 밖)
            }
            Time.timeScale = 1f;
            _inGame = scene.name == SceneNames.Game;
            if (_inGame)
                FillMissingIcons();
            _attract.SetActive(!_inGame);
            _hud.SetActive(_inGame);
            _card.SetActive(_inGame);
            _end.SetActive(false);
            _idle.SetActive(false);
            _error.SetText("");
            if (_inGame)
            {
                _ended = false;
                _timeLeft = SessionSeconds;
                _lastInput = Time.unscaledTime;
                _doneBuilt = _doneSynth = _doneInside = false;
                _station = FindFirstObjectByType<StationController>();
                _interior = FindFirstObjectByType<InteriorMode>();
                _baseModules = _station != null ? _station.Grid.Modules.Count() : 0;
                _baseSyntheses = _station != null ? _station.Simulation.Session.Syntheses : 0;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                InputGate.Blocked = false;
            }
        }

        private void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null && kb.ctrlKey.isPressed && kb.shiftKey.isPressed)
            {
                if (kb.rKey.wasPressedThisFrame)
                    ReturnToStart();
                if (kb.qKey.wasPressedThisFrame)
                    Application.Quit();
                if (kb.tKey.wasPressedThisFrame)
                    _timeLeft += 300f;
            }
            if (!_inGame)
            {
                UpdateAttract(kb, mouse);
                return;
            }
            if ((kb != null && kb.anyKey.isPressed) || (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 0.01f || mouse.leftButton.isPressed || mouse.rightButton.isPressed)))
                _lastInput = Time.unscaledTime;
            if (_ended)
            {
                UpdateEnd(kb, mouse);
                return;
            }
            UpdateSession(kb);
        }

        private void UpdateAttract(Keyboard kb, Mouse mouse)
        {
            _prompt.alpha = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 3f);
            if ((mouse != null && mouse.leftButton.wasPressedThisFrame) || (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)))
                StartDemo();
        }

        private void StartDemo()
        {
            if (_leaving || SceneFader.Busy)
                return;
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "Saves");
                Directory.CreateDirectory(dir);
                File.Copy(_savePath, Path.Combine(dir, Slot + ".json"), true);   // 관람객마다 같은 정거장에서 시작
            }
            catch (IOException e)
            {
                Debug.LogError("[GStarDemo] 세이브 복사 실패: " + e.Message);
            }
            if (SaveManager.LoadAndPlay(Slot))
                _leaving = true;
            else
                _error.SetText("정거장을 불러오지 못했습니다. 진행 요원을 불러 주세요.");
        }

        private void UpdateSession(Keyboard kb)
        {
            _timeLeft -= Time.unscaledDeltaTime;
            int s = Mathf.Max(0, Mathf.CeilToInt(_timeLeft));
            _timer.SetText($"{s / 60}:{s % 60:00}");
            _timer.alpha = _timeLeft < 60f ? 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f) : 1f;   // 끝나 감 = 깜빡임 (색약: 색으로만 알리지 않음)

            if (_station != null && Time.unscaledTime >= _nextRefill)
            {
                _nextRefill = Time.unscaledTime + RefillGap;
                var res = _station.Simulation.Resources;
                foreach (ResourceType t in System.Enum.GetValues(typeof(ResourceType)))
                    res.SetStock(t, res.GetCapacity(t));
            }
            if (_station != null)
            {
                _doneBuilt |= _station.Grid.Modules.Count() > _baseModules;
                _doneSynth |= _station.Simulation.Session.Syntheses > _baseSyntheses;
                if (_station.Simulation.Session.IsGameOver)
                    _timeLeft = 0f;
            }
            if (_interior != null)
                _doneInside |= _interior.IsInside;
            RefreshSteps();

            if (kb != null)
            {
                if (kb.f10Key.wasPressedThisFrame)
                    _card.SetActive(!_card.activeSelf);
                foreach (var j in Jumps)
                {
                    if (kb[j.key].wasPressedThisFrame && !_jumping)
                        StartCoroutine(Jump(j.module));
                }
            }

            float idle = Time.unscaledTime - _lastInput;
            _idle.SetActive(idle > IdleSeconds);
            if (idle > IdleSeconds)
            {
                _idleText.SetText($"계속하시려면 아무 키나 눌러 주세요\n<size=70%>{Mathf.CeilToInt(IdleSeconds + IdleWarnSeconds - idle)}초 뒤 처음 화면으로 돌아갑니다</size>");
                if (idle > IdleSeconds + IdleWarnSeconds)
                    ReturnToStart();
            }
            if (_timeLeft <= 0f)
                ShowEnd();
        }

        private void RefreshSteps()
        {
            string Mark(bool done) => done ? $"   <color={HudTheme.GreenHex}><b>완료</b></color>" : "";
            string Num(string n) => $"<color={HudTheme.AccentHex}><b>{n}</b></color>";
            string Sub(string text) => $"<size=16><color={HudTheme.MutedHex}>{text}</color></size>";
            string Key_(string k) => $"<color={HudTheme.AccentHex}><b>{k}</b></color>";
            _steps.SetText(
                $"{Num("01")}  <b>짓기</b>{Mark(_doneBuilt)}\n{Sub("아래 메뉴에서 모듈을 고르고 빈 칸을 클릭")}\n" +
                $"{Num("02")}  <b>합치기</b>{Mark(_doneSynth)}\n{Sub($"{Key_(KeyBindings.Label(GameAction.Recipes))} 합성 창에서 모듈을 진화")}\n" +
                $"{Num("03")}  <b>들어가기</b>{Mark(_doneInside)}\n{Sub($"모듈을 클릭하고 {Key_(KeyBindings.Label(GameAction.EnterInterior))}, 1인칭으로 걷기")}");
        }

        private static string KeyName(Key key) => "F" + (key - Key.F1 + 1);

        /// <summary>대표 체험으로 바로: 안이면 나왔다가 들어감. 드론 정비창은 관람석 앞 옥상으로.</summary>
        private IEnumerator Jump(string moduleName)
        {
            _jumping = true;
            var module = _station != null ? _station.Grid.Modules.FirstOrDefault(m => m.Data != null && m.Data.name == moduleName) : null;
            if (module == null || _interior == null)
            {
                _jumping = false;
                yield break;
            }
            if (_interior.IsInside)
                _interior.Exit();
            float give = Time.unscaledTime + 6f;
            while (_interior.CheckEnter(module) != null && Time.unscaledTime < give)
                yield return null;
            _interior.Enter(module);
            give = Time.unscaledTime + 10f;
            while (!_interior.IsInside && Time.unscaledTime < give)
                yield return null;
            yield return new WaitForSecondsRealtime(1.6f);
            if (moduleName == "MD_Evo_DroneDepot" && _interior.IsInside)
            {
                var show = FindFirstObjectByType<InteriorDroneShow>();
                var player = FindFirstObjectByType<FirstPersonController>();
                if (show != null && player != null)
                    player.Teleport(show.transform.TransformPoint(new Vector3(5.3f, 8.37f, 3.4f)), show.transform.eulerAngles.y, -8f);
            }
            _jumping = false;
        }

        private void ShowEnd()
        {
            _ended = true;
            _endLeft = EndSeconds;
            _idle.SetActive(false);
            _card.SetActive(false);
            _hud.SetActive(false);
            _end.SetActive(true);
            int built = _station != null ? Mathf.Max(0, _station.Grid.Modules.Count() - _baseModules) : 0;
            int synth = _station != null ? _station.Simulation.Session.Syntheses - _baseSyntheses : 0;
            _endBody.SetText($"이번 시연에서 모듈 <b>{built}</b>개를 더 짓고 <b>{synth}</b>번 합성했습니다.\n\n체험해 주셔서 감사합니다.");
        }

        private void UpdateEnd(Keyboard kb, Mouse mouse)
        {
            _endLeft -= Time.unscaledDeltaTime;
            bool skip = _endLeft < EndSeconds - 3f && ((mouse != null && mouse.leftButton.wasPressedThisFrame) || (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)));
            if (_endLeft <= 0f || skip)
                ReturnToStart();
        }

        private void ReturnToStart()
        {
            if (_leaving || SceneFader.Busy)
                return;
            _leaving = true;
            StopAllCoroutines();
            _jumping = false;
            Time.timeScale = 1f;
            SceneFader.Load(SceneNames.MainMenu, new LoadingCard { Title = "처음 화면으로" });
        }

        // ---------------- 아이콘 없는 모듈 (건설 메뉴 빈 칸) ----------------

        /// <summary>아이콘이 비어 있는 모듈은 바깥 모델을 그 자리에서 찍어 채움 (사용자 2026-11-05 "모듈 선택칸에 이미지가 안 들어간 게 있네"). 실패해도 시연은 이어 감.</summary>
        private static void FillMissingIcons()
        {
            try
            {
                var field = typeof(ModuleData).GetField("_icon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var prefabField = typeof(ModuleData).GetField("_prefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field == null || prefabField == null)
                    return;
                foreach (var data in Resources.FindObjectsOfTypeAll<ModuleData>())
                {
                    if (data.Icon != null || !(prefabField.GetValue(data) is GameObject prefab))
                        continue;
                    var sprite = RenderIcon(prefab);
                    if (sprite != null)
                        field.SetValue(data, sprite);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[GStarDemo] 아이콘 만들기 실패: " + e.Message);
            }
        }

        private static Sprite RenderIcon(GameObject prefab)
        {
            const int Size = 256, Layer = 30;
            var at = new Vector3(5000f, 5000f, 5000f);
            bool was = prefab.activeSelf;
            var go = Instantiate(prefab, at, Quaternion.identity);
            var camGo = new GameObject("IconCam");
            var lightGo = new GameObject("IconLight");
            var rt = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                foreach (var b in go.GetComponentsInChildren<MonoBehaviour>(true))
                    b.enabled = false;
                var bounds = new Bounds(at, Vector3.one);
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = Layer;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    bounds.Encapsulate(r.bounds);
                var sun = lightGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.6f;
                sun.cullingMask = 1 << Layer;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                var cam = camGo.AddComponent<Camera>();
                cam.cullingMask = 1 << Layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.01f;
                float dist = bounds.extents.magnitude / Mathf.Sin(15f * Mathf.Deg2Rad);
                cam.farClipPlane = dist * 3f;
                cam.transform.position = bounds.center + Quaternion.Euler(30f, 35f, 0f) * Vector3.back * dist;
                cam.transform.LookAt(bounds.center);
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.ARGB32, false);
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                DestroyImmediate(go);
                DestroyImmediate(camGo);
                DestroyImmediate(lightGo);
            }
        }

        // ---------------- 화면 (게임 UI와 같은 테크 홀로그램 패널 HoloUi) ----------------

        private void BuildUi()
        {
            var go = new GameObject("GStarDemoCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 2000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            _ui = HoloUi.For(_art, _art != null ? _art.Font : TMP_Settings.defaultFontAsset, null, null, null);
            BuildAttract();
            BuildHud();
            BuildEnd();
        }

        private void BuildAttract()
        {
            _attract = Backdrop("Attract", new Color(0.01f, 0.03f, 0.06f, 0.5f));
            var parent = _attract.transform;
            var title = Label(parent, "ANOTHER EARTH", 104f, TextAlignmentOptions.Center, new Vector2(0f, 340f), new Vector2(1500f, 140f));
            title.fontStyle = FontStyles.Bold;
            HoloUi.Glow(title, 0.3f);
            HoloChroma.Add(title, 1.2f, 0.55f);
            Line(parent, new Vector2(0f, 268f), 900f);
            Label(parent, $"<color={HudTheme.AccentHex}>G-STAR 시연</color>  ·  15분 체험", 32f, TextAlignmentOptions.Center, new Vector2(0f, 228f), new Vector2(1200f, 50f));

            var panel = Window(parent, "G-STAR DEMO", new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(900f, 370f));
            Row(panel, "01", "짓기", "칸을 이어 붙여 나만의 우주 정거장을 키웁니다", 100f);
            Row(panel, "02", "합치기", "모듈을 합성해 더 크고 멋진 모듈로 진화시킵니다", 0f);
            Row(panel, "03", "들어가기", "1인칭으로 걸어 들어가 동물 주민과 놀이기구를 만납니다", -100f);

            var start = _ui.Button(parent, "시연 시작", 30f, StartDemo);
            var srt = (RectTransform)start.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(0f, -262f);
            srt.sizeDelta = new Vector2(380f, 68f);
            _prompt = Label(parent, "클릭하거나 Enter를 누르면 시작합니다", 22f, TextAlignmentOptions.Center, new Vector2(0f, -332f), new Vector2(1000f, 40f));
            _error = Label(parent, "", 24f, TextAlignmentOptions.Center, new Vector2(0f, -380f), new Vector2(1200f, 40f));
            _error.color = HudTheme.Warning;
        }

        private void BuildHud()
        {
            // 오른쪽 자원 패널 바로 아래 (튜토리얼 카드와 같은 자리)
            const float Top = 396f, Width = 430f;
            var corner = new Vector2(1f, 1f);
            var hud = Window(_canvas.transform, null, corner, new Vector2(-16f, -Top), new Vector2(Width, 60f));
            _hud = hud.gameObject;
            PassClicks(_hud);
            var label = Label(hud, "시연 남은 시간", 18f, TextAlignmentOptions.Left, new Vector2(-70f, 0f), new Vector2(250f, 40f));
            label.color = HoloUi.MutedColor;
            _timer = Label(hud, "", 34f, TextAlignmentOptions.Right, new Vector2(100f, 0f), new Vector2(190f, 50f));
            _timer.fontStyle = FontStyles.Bold;
            _timer.color = HudTheme.HoloBright;

            var card = Window(_canvas.transform, "DEMO GUIDE", corner, new Vector2(-16f, -Top - 68f), new Vector2(Width, 452f));
            _card = card.gameObject;
            PassClicks(_card);
            const float Inner = Width - 44f;
            var head = Label(card, "체험 순서", HudTheme.TitleSize, TextAlignmentOptions.Left, new Vector2(0f, 186f), new Vector2(Inner, 34f));
            head.fontStyle = FontStyles.Bold;
            _steps = Label(card, "", HudTheme.BodySize, TextAlignmentOptions.TopLeft, new Vector2(0f, 76f), new Vector2(Inner, 176f));
            _steps.textWrappingMode = TextWrappingModes.Normal;
            _steps.lineSpacing = 16f;
            Line(card, new Vector2(0f, -22f), Inner);
            var jump = Label(card, "바로 가기", 20f, TextAlignmentOptions.Left, new Vector2(0f, -44f), new Vector2(Inner, 30f));
            jump.fontStyle = FontStyles.Bold;
            var lines = string.Join("\n", Jumps.Select(j => $"<color={HudTheme.AccentHex}><b>{KeyName(j.key)}</b></color>  {j.label}"));
            var list = Label(card, lines, 18f, TextAlignmentOptions.TopLeft, new Vector2(0f, -112f), new Vector2(Inner, 104f));
            list.lineSpacing = 4f;
            Line(card, new Vector2(0f, -170f), Inner);
            var keys = Label(card, $"WASD 걷기 · {KeyBindings.Label(GameAction.Interact)} 앉기 · 타기 · 누르기 · ESC 나가기\nF10 이 안내 끄기", 15f, TextAlignmentOptions.TopLeft, new Vector2(0f, -196f), new Vector2(Inner, 44f));
            keys.color = HoloUi.MutedColor;

            var idle = Window(_canvas.transform, "STANDBY", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 190f));
            _idle = idle.gameObject;
            _idleText = Label(idle, "", 32f, TextAlignmentOptions.Center, new Vector2(0f, -6f), new Vector2(760f, 130f));
        }

        private void BuildEnd()
        {
            _end = Backdrop("End", new Color(0.01f, 0.03f, 0.06f, 0.72f));
            var parent = _end.transform;
            var title = Label(parent, "ANOTHER EARTH", 96f, TextAlignmentOptions.Center, new Vector2(0f, 250f), new Vector2(1500f, 130f));
            title.fontStyle = FontStyles.Bold;
            HoloUi.Glow(title, 0.3f);
            HoloChroma.Add(title, 1.2f, 0.55f);
            Line(parent, new Vector2(0f, 182f), 900f);
            var panel = Window(parent, "THANK YOU", new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(900f, 300f));
            _endBody = Label(panel, "", 32f, TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(820f, 220f));
            var hint = Label(parent, "잠시 뒤 처음 화면으로 돌아갑니다", 22f, TextAlignmentOptions.Center, new Vector2(0f, -200f), new Vector2(1000f, 40f));
            hint.color = HoloUi.MutedColor;
        }

        private GameObject Backdrop(string name, Color color)
        {
            var rt = HoloUi.Rect(name, _canvas.transform);
            HoloUi.Stretch(rt);
            rt.gameObject.AddComponent<Image>().color = color;
            return rt.gameObject;
        }

        /// <summary>게임 창과 같은 테크 패널 (격자 · 주사선 · 스캔 띠). 크기를 먼저 정해야 무늬 간격이 맞음.</summary>
        private RectTransform Window(Transform parent, string tag, Vector2 corner, Vector2 position, Vector2 size)
        {
            var rt = HoloUi.Rect(tag ?? "DemoPanel", parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = corner;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            _ui.Window(rt.gameObject, HudTheme.PanelFill, tag);
            return rt;
        }

        private TMP_Text Label(Transform parent, string text, float size, TextAlignmentOptions align, Vector2 position, Vector2 box)
        {
            var t = _ui.Label(parent, text, size, align);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = box;
            t.raycastTarget = false;
            return t;
        }

        private void Row(RectTransform panel, string number, string name, string text, float y)
        {
            var n = Label(panel, number, 44f, TextAlignmentOptions.Center, new Vector2(-376f, y), new Vector2(90f, 60f));
            n.fontStyle = FontStyles.Bold;
            n.color = HudTheme.Accent;
            var head = Label(panel, name, 28f, TextAlignmentOptions.Left, new Vector2(50f, y + 20f), new Vector2(740f, 40f));
            head.fontStyle = FontStyles.Bold;
            var body = Label(panel, text, 22f, TextAlignmentOptions.Left, new Vector2(50f, y - 20f), new Vector2(740f, 36f));
            body.color = HoloUi.MutedColor;
        }

        private static void Line(Transform parent, Vector2 position, float width)
        {
            var rt = HoloUi.Rect("Line", parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(width, 2f);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = HudTheme.AccentDim;
            image.raycastTarget = false;
        }

        /// <summary>안내 패널 위로도 건설 · 선택 클릭이 지나가게.</summary>
        private static void PassClicks(GameObject go)
        {
            var group = go.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
        }
    }
}
