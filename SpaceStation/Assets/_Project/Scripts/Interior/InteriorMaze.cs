using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 연구원 "별빛 데이터 미로" (MODELING 7-10 직접 체험 "미로 체험", 값 = <see cref="ResearchMaze"/>).
    /// 입구 아치를 안으로 지나면 시간을 재고, 가운데 연구실에 들어서면 도착 (기록 · 이번 실행 최고 기록 — 아치 위 간판에도). 아치 밖으로 되돌아 나가면 취소.
    /// 길잡이 점은 평소 희미함 — 입구 오른쪽 길잡이 화면을 보고 F = 입구 → 연구실 차례로 켜져 <see cref="HintSeconds"/>초 머묾.
    /// 막다른 길 화면 셋 = "404". 갓 밑 데이터 빛줄을 따라 빛 알갱이가 고리마다 돌고 (고리마다 반대쪽), 도착하면 빨라지고 코어 위 빛 고리가 빨리 돌며 코어가 환해짐.
    /// 위치는 템플릿 루트(이 물체) 기준, 시간은 실제 시간. 템플릿 빌더가 configure에서 붙임.
    /// </summary>
    [DefaultExecutionOrder(-320)]
    public sealed class InteriorMaze : MonoBehaviour
    {
        public const float HintSeconds = 20f;
        private const float HintStep = 0.35f;       // 구간마다 켜지는 간격
        private const float HintFade = 1.5f;
        private const float DimEmission = 0.12f;
        private const float DimBase = 0.45f;
        private const float PulseSpeed = 1.4f;
        private const float PulseLength = 0.45f;
        private const float PulseGlow = 2.6f;
        private const float CelebrateSeconds = 6f;
        private const float SignInterval = 0.5f;
        private static readonly int[] PulseCounts = { 12, 9, 6 };   // 고리마다 약 4 m 간격
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        /// <summary>이번 실행에서 가장 빠른 도착 (초, 없으면 0) — 연구원이 여럿이어도 같은 미로라 함께 씀.</summary>
        private static float _best;

        [Tooltip("길잡이 점 구간 (길 순서)")]
        [SerializeField] private Renderer[] _dots;
        [Tooltip("코어 위 빛 고리 피벗 (축 y)")]
        [SerializeField] private Transform _coreRing;
        [Tooltip("길잡이 화면 (보고 F)")]
        [SerializeField] private Collider _hintUse;
        [Tooltip("빛 알갱이 재질 (흰 빛)")]
        [SerializeField] private Material _pulseMaterial;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private string _title = "별빛 데이터 미로";

        private Color[][] _dotBase, _dotEmission;
        private MaterialPropertyBlock _block;
        private float _hintTime = -1f;
        private bool _running;
        private float _elapsed;
        private float _prevZ = float.NaN;
        private float _celebrate;
        private float _ringAngle;
        private float _message;
        private string _messageText;
        private float _signTimer;
        private Transform[][] _pulses;
        private Material _pulseInstance;
        private float _pulseS;
        private FirstPersonController _player;
        private TextMeshPro _signText, _hintText;
        private Light _coreLight;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public bool Running => _running;
        public float Elapsed => _elapsed;
        public bool HintActive => _hintTime >= 0f;
        public static float Best => _best;

        public void Configure(Renderer[] dots, Transform coreRing, Collider hintUse, Material pulseMaterial, TMP_FontAsset font)
        {
            _dots = dots;
            _coreRing = coreRing;
            _hintUse = hintUse;
            _pulseMaterial = pulseMaterial;
            _font = font;
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            _dotBase = new Color[_dots.Length][];
            _dotEmission = new Color[_dots.Length][];
            for (int i = 0; i < _dots.Length; i++)
            {
                var mats = _dots[i].sharedMaterials;
                _dotBase[i] = new Color[mats.Length];
                _dotEmission[i] = new Color[mats.Length];
                for (int m = 0; m < mats.Length; m++)
                {
                    _dotBase[i][m] = mats[m] != null && mats[m].HasProperty(BaseColorId) ? mats[m].GetColor(BaseColorId) : Color.white;
                    _dotEmission[i][m] = mats[m] != null && mats[m].HasProperty(EmissionId) ? mats[m].GetColor(EmissionId) : Color.black;
                }
            }
            if (_hintUse != null)
            {
                var use = _hintUse.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => HintActive ? "길잡이 켜짐" : "길잡이 켜기";
                use.Completed = StartHint;
            }
            BuildTexts();
            BuildPulses();
            var lightGo = new GameObject("CoreGlow");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = ResearchMaze.Center + new Vector3(0f, 1.6f, 0f);
            _coreLight = lightGo.AddComponent<Light>();
            _coreLight.type = LightType.Point;
            _coreLight.range = 4f;
            _coreLight.color = new Color(0.78f, 0.66f, 1f);
            _coreLight.intensity = 0f;
            _coreLight.enabled = false;
            ApplyDots(0f);
        }

        private void OnDisable()
        {
            _running = false;
            _hintTime = -1f;
            _celebrate = 0f;
            _prevZ = float.NaN;
            if (_coreLight != null)
                _coreLight.enabled = false;
            ApplyDots(0f);
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
            if (_pulseInstance != null)
                Destroy(_pulseInstance);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            TrackPlayer(dt);
            if (_hintTime >= 0f)
            {
                _hintTime += dt;
                if (_hintTime > HintSeconds + HintFade)
                    _hintTime = -1f;
            }
            ApplyDots(dt);
            // 도착 축하: 코어 고리 · 빛 알갱이 빨라짐, 코어 빛
            _celebrate = Mathf.Max(0f, _celebrate - dt);
            float boost = _celebrate / CelebrateSeconds;
            _ringAngle += dt * Mathf.Lerp(25f, 260f, boost);
            if (_coreRing != null)
                _coreRing.localRotation = Quaternion.Euler(0f, _ringAngle, 0f);
            if (_coreLight != null)
            {
                _coreLight.enabled = boost > 0.01f;
                _coreLight.intensity = 2.2f * boost;
            }
            _pulseS += dt * PulseSpeed * (1f + 2.5f * boost);
            MovePulses();
            _message = Mathf.Max(0f, _message - dt);
            UpdateTexts(dt);
            UpdateHint();
        }

        // ---------------- 시간 재기 ----------------

        private void TrackPlayer(float dt)
        {
            if (_player == null || !_player.isActiveAndEnabled)
                return;
            var p = transform.InverseTransformPoint(_player.transform.position);
            float archZ = ResearchMaze.ArchZ;
            bool atArch = Mathf.Abs(p.x - ResearchMaze.Center.x) < 0.9f && p.y < ResearchMaze.Floor + 1f;
            if (!float.IsNaN(_prevZ) && atArch)
            {
                if (!_running && _prevZ < archZ - 0.1f && p.z >= archZ - 0.1f)
                {
                    _running = true;
                    _elapsed = 0f;
                    AudioService.TryPlay(l => l.UiOpen);
                }
                else if (_running && _prevZ >= archZ - 0.2f && p.z < archZ - 0.2f)
                {
                    _running = false;
                    Flash("미로 기록 취소 (입구 밖으로 나감)");
                }
            }
            _prevZ = p.z;
            if (!_running)
                return;
            _elapsed += dt;
            var flat = new Vector2(p.x - ResearchMaze.Center.x, p.z - ResearchMaze.Center.z);
            if (flat.magnitude < ResearchMaze.LabRadius - 0.3f && p.y < ResearchMaze.Floor + 1f)
                Arrive();
        }

        private void Arrive()
        {
            _running = false;
            bool record = _best <= 0f || _elapsed < _best;
            if (record)
                _best = _elapsed;
            _celebrate = CelebrateSeconds;
            Flash(record ? $"도착!  {Clock(_elapsed)}  ·  새 최고 기록" : $"도착!  {Clock(_elapsed)}  ·  최고 {Clock(_best)}");
            AudioService.TryPlay(l => l.UiOpen);
            _signTimer = 0f;
        }

        private void Flash(string text)
        {
            _messageText = text;
            _message = 5f;
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.FloorToInt(seconds);
            return $"{s / 60}:{s % 60:00}";
        }

        // ---------------- 길잡이 점 ----------------

        private void StartHint()
        {
            if (HintActive)
                return;
            _hintTime = 0f;
            AudioService.TryPlay(l => l.UiOpen);
        }

        /// <summary>구간마다 밝기 (0 = 희미, 1 = 원래 빛): 길잡이 켠 뒤 구간 i는 i × HintStep초에 켜지고, 끝나면 함께 어두워짐.</summary>
        public float DotLevel(int segment)
        {
            if (_hintTime < 0f)
                return 0f;
            float on = Mathf.Clamp01((_hintTime - segment * HintStep) / HintStep);
            float off = Mathf.Clamp01((HintSeconds + HintFade - _hintTime) / HintFade);
            return Mathf.Min(on, off);
        }

        private void ApplyDots(float dt)
        {
            if (_dots == null || _block == null)
                return;
            for (int i = 0; i < _dots.Length; i++)
            {
                if (_dots[i] == null)
                    continue;
                float level = DotLevel(i);
                float front = _hintTime >= 0f ? Mathf.Clamp01(1f - Mathf.Abs(_hintTime - i * HintStep - HintStep) / HintStep) : 0f;   // 막 켜진 구간은 잠깐 더 환하게
                float e = Mathf.Lerp(DimEmission, 1f, level) * (1f + 0.8f * front);
                float b = Mathf.Lerp(DimBase, 1f, level);
                for (int m = 0; m < _dotBase[i].Length; m++)
                {
                    _dots[i].GetPropertyBlock(_block, m);
                    _block.SetColor(BaseColorId, _dotBase[i][m] * b);
                    _block.SetColor(EmissionId, _dotEmission[i][m] * e);
                    _dots[i].SetPropertyBlock(_block, m);
                }
            }
        }

        // ---------------- 데이터 빛 알갱이 ----------------

        private void BuildPulses()
        {
            if (_pulseMaterial == null)
                return;
            var root = new GameObject("DataPulses").transform;
            root.SetParent(transform, false);
            // 빛줄 (청백 발광)보다 환해야 알갱이로 보임 → 재질 사본의 발광을 키움
            _pulseInstance = new Material(_pulseMaterial) { name = _pulseMaterial.name + " (Pulse)" };
            if (_pulseInstance.HasProperty(EmissionId))
                _pulseInstance.SetColor(EmissionId, _pulseInstance.GetColor(EmissionId) * PulseGlow);
            _pulses = new Transform[ResearchMaze.Rings.Length][];
            for (int r = 0; r < _pulses.Length; r++)
            {
                _pulses[r] = new Transform[PulseCounts[r]];
                for (int k = 0; k < PulseCounts[r]; k++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(go.GetComponent<Collider>());
                    go.name = $"Pulse{r}_{k}";
                    go.transform.SetParent(root, false);
                    go.transform.localScale = new Vector3(PulseLength, 0.052f, ResearchMaze.Wall + 0.03f);
                    var rend = go.GetComponent<MeshRenderer>();
                    rend.sharedMaterial = _pulseInstance;
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _pulses[r][k] = go.transform;
                }
            }
            MovePulses();
        }

        private void MovePulses()
        {
            if (_pulses == null)
                return;
            for (int r = 0; r < _pulses.Length; r++)
            {
                float perimeter = 8f * ResearchMaze.Rings[r];
                float sign = r % 2 == 0 ? 1f : -1f;
                for (int k = 0; k < _pulses[r].Length; k++)
                {
                    float s = sign * _pulseS + perimeter * k / _pulses[r].Length;
                    bool shown = ResearchMaze.RingPoint(r, s, PulseLength * 0.5f, out var p, out var dir);
                    var t = _pulses[r][k];
                    if (t.gameObject.activeSelf != shown)
                        t.gameObject.SetActive(shown);
                    if (!shown)
                        continue;
                    t.localPosition = p;
                    t.localRotation = Quaternion.FromToRotation(Vector3.right, dir);
                }
            }
        }

        // ---------------- 간판 · 화면 글씨 · 안내 ----------------

        private void BuildTexts()
        {
            if (_font == null)
                return;
            // 글씨 상자 = 화면 판보다 조금 작게, 넘치면 저절로 줄어듦 (사용자 2026-11-10 "글자가 화면 밖으로 나와있는 문제")
            _signText = Text("MazeSign", ResearchMaze.SignFace, new Vector2(0f, -1f), 1.6f, new Vector2(1.0f, 0.34f));
            _hintText = Text("MazeHintText", ResearchMaze.HintScreen, new Vector2(0f, -1f), 0.9f, new Vector2(0.56f, 0.42f));
            foreach (var (face, normal) in ResearchMaze.DeadScreens)
            {
                var t = Text("Maze404", face, normal, 1.5f, new Vector2(0.62f, 0.38f));
                t.color = new Color(0.82f, 0.74f, 1f);
                t.SetText("404\n<size=40%>길을 찾을 수 없음\n되돌아가세요</size>");
            }
        }

        private TextMeshPro Text(string name, Vector3 face, Vector2 normal, float size, Vector2 rect)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = face + new Vector3(normal.x, 0f, normal.y) * 0.006f;
            go.transform.localRotation = Quaternion.Euler(0f, Mathf.Atan2(-normal.x, -normal.y) * Mathf.Rad2Deg, 0f);
            var text = go.AddComponent<TextMeshPro>();
            text.font = _font;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMax = size;
            text.fontSizeMin = size * 0.2f;
            text.color = new Color(0.92f, 0.96f, 1f);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            text.rectTransform.sizeDelta = rect;
            return text;
        }

        private void UpdateTexts(float dt)
        {
            _signTimer -= dt;
            if (_signTimer > 0f)
                return;
            _signTimer = 0.5f;
            if (_signText != null)
                _signText.SetText($"{_title}\n<size=58%>{(_best > 0f ? "최고 기록 " + Clock(_best) : "아치를 지나면 시간을 잼")}</size>");
            if (_hintText != null)
            {
                string key = KeyBindings.Label(GameAction.Interact);
                _hintText.SetText(HintActive
                    ? $"길잡이\n<size=60%>켜짐 · {Mathf.CeilToInt(Mathf.Max(0f, HintSeconds - _hintTime))}초</size>"
                    : $"길잡이\n<size=60%>{key} 누르면 길이 켜져요</size>");
            }
        }

        private void UpdateHint()
        {
            string text = null;
            if (_message > 0f)
                text = _messageText;
            else if (_running)
                text = $"데이터 미로  ·  {Clock(_elapsed)}" + (HintActive ? "  ·  길잡이 켜짐" : "");
            ShowHint(text);
        }

        private void ShowHint(string text)
        {
            if (text == _hintShown)
                return;
            _hintShown = text;
            if (text == null)
            {
                if (_canvas != null)
                    _canvas.gameObject.SetActive(false);
                return;
            }
            EnsureCanvas();
            _canvas.gameObject.SetActive(true);
            _hint.SetText(text);
        }

        private void EnsureCanvas()
        {
            if (_canvas != null)
                return;
            var go = new GameObject("MazeHint", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 300;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var back = new GameObject("Back", typeof(RectTransform)).AddComponent<Image>();
            back.transform.SetParent(go.transform, false);
            Place(back.rectTransform, new Vector2(620f, 52f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(600f, 44f));
            if (_font != null)
                _hint.font = _font;
            _hint.fontSize = 26f;
            _hint.color = new Color(0.92f, 0.96f, 1f);
            _hint.alignment = TextAlignmentOptions.Center;
            _hint.raycastTarget = false;
            _hint.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private static void Place(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 64f);
            rt.sizeDelta = size;
        }
    }
}
