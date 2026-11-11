using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 바이오스피어 체험 "물꼬 맞추기" (사용자 2026-11-11, 규칙 = <see cref="SluicePuzzle"/>): 능선 수문 바퀴를 보고 F = 시작 · 물꼬 한 단 내리기.
    /// 물은 수문에서 단마다 흘러 내려가 물꼬 단에서 밭으로 들어감 (아래 물길 · 폭포는 마름, 물레방아는 물이 지나갈 때만 돎).
    /// 목마른 밭 = 시들어 낮아지고 빛바랜 색 + "목말라요" 글자 (깜박임 — 색약이라 색만으로 알리지 않음), 물이 닿으면 다시 피어남.
    /// 세 판을 마치면 걸린 시간 · 최고 기록 (글자), 잠시 뒤 물꼬가 모두 호수까지 열림. 체험 밖 (쉬는 동안) = 물이 호수까지 흐르고 밭이 다 핌.
    /// 시간 = 실제 시간 (다른 내부 체험과 같음). 값 = <see cref="Biosphere"/>.
    /// </summary>
    public sealed class InteriorSluiceGates : MonoBehaviour
    {
        private const int S = SluicePuzzle.StreamCount, L = SluicePuzzle.Levels;
        private const float FlowSpeed = 2.4f;        // 물 앞이 내려가는 빠르기 (단 / 초)
        private const float PlateSpeed = 1.1f;       // 문판 오르내림 (m / 초)
        private const float TurnSeconds = 0.6f, TurnDegrees = 120f;
        private const float WiltSeconds = 0.9f, BloomSeconds = 1.3f, WiltHeight = 0.55f;
        private const float RoundPause = 1.6f, ResultSeconds = 5.5f, BloomedSeconds = 1.8f;
        private static readonly Color DryColor = new Color(0.82f, 0.7f, 0.48f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CullModeId = Shader.PropertyToID("_CullMode");

        [Tooltip("수문 바퀴 피벗 셋 (바퀴 가운데, 시선 충돌체)")]
        [SerializeField] private Transform[] _wheels;
        [Tooltip("금빛 문판 셋 (처음 자리 = 닫힘)")]
        [SerializeField] private Transform[] _plates;
        [Tooltip("물길 물 [물길 × 6 + 단] (단 0 = 능선 밑)")]
        [SerializeField] private Renderer[] _chans;
        [Tooltip("폭포 [물길 × 6 + 단] (단 0 = 수문 · 능선 끝 폭포, n = 그 단에서 아래로)")]
        [SerializeField] private Renderer[] _spills;
        [Tooltip("밭 피벗 [물길 × 5 + 단 − 1] (피벗 높이 = 단 윗면 — 세로로 줄이면 시듦)")]
        [SerializeField] private Transform[] _crops;
        [SerializeField] private InteriorOrbit[] _waterWheels;
        [SerializeField] private InteriorWaterfallFx _fx;
        [SerializeField] private TMP_FontAsset _font;

        private static float _best = -1f;

        private readonly SluicePuzzle _puzzle = new SluicePuzzle();
        private readonly float[] _front = new float[S];       // 물 앞 (0 = 수문 앞 … n = n단 물길까지 참, 6 = 호수)
        private readonly float[] _lift = new float[S];
        private readonly float[] _turn = new float[S];        // 남은 바퀴 돌림 (도)
        private readonly float[] _wheelAngle = new float[S];
        private float[] _wilt;                                 // 밭마다 0 = 핌 · 1 = 시듦
        private float[] _bloomedTimer;
        private Vector3[] _plateBase;
        private Color[][] _cropBase;
        private Renderer[] _cropRenderers;
        private MaterialPropertyBlock _block;
        private TextMeshPro[] _labels;
        private TextMeshPro[] _boards;
        private float _time;
        private float _pause;                                  // 판 사이 · 결과 보여 주는 시간
        private bool _showingResult;
        private string _notice;
        private float _noticeTimer;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;
        private Transform _camera;

        public SluicePuzzle Puzzle => _puzzle;
        public static float Best => _best;
        /// <summary>물길 s의 물 앞 (시험 · 연출 확인용).</summary>
        public float Front(int stream) => _front[stream];
        public bool Wilted(int stream, int level) => _wilt != null && _wilt[stream * L + level - 1] > 0.5f;

        public void Configure(Transform[] wheels, Transform[] plates, Renderer[] chans, Renderer[] spills, Transform[] crops,
            InteriorOrbit[] waterWheels, InteriorWaterfallFx fx, TMP_FontAsset font)
        {
            _wheels = wheels;
            _plates = plates;
            _chans = chans;
            _spills = spills;
            _crops = crops;
            _waterWheels = waterWheels;
            _fx = fx;
            _font = font;
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            _wilt = new float[S * L];
            _bloomedTimer = new float[S * L];
            _plateBase = new Vector3[S];
            for (int i = 0; i < S; i++)
            {
                if (_plates != null && i < _plates.Length && _plates[i] != null)
                    _plateBase[i] = _plates[i].localPosition;
                if (_wheels != null && i < _wheels.Length && _wheels[i] != null)
                {
                    int s = i;
                    var use = _wheels[i].gameObject.AddComponent<InteriorInteractable>();
                    use.Tap = true;
                    use.Prompt = () => _puzzle.Running ? $"물꼬 한 단 내리기  <size=80%>(지금 {LevelName(_puzzle.Gate(s))})</size>" : "물꼬 맞추기 시작";
                    use.Blocked = () => _pause > 0f ? (_showingResult ? "잠깐만요 · 물이 다시 호수까지 흘러요" : "잠깐만요 · 다음 판 준비 중") : _turn[s] > 0f ? "바퀴가 도는 중" : null;
                    use.Completed = () => Use(s);
                }
            }
            CollectCrops();
            BuildTexts();
            ResetAll();
        }

        private void OnDisable()
        {
            // 방을 나가면 체험을 끝내고 쉬는 모습으로 (물이 호수까지 · 밭이 다 핌)
            ResetAll();
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        private void ResetAll()
        {
            _puzzle.Reset();
            _showingResult = false;
            _pause = 0f;
            _time = 0f;
            for (int i = 0; i < S; i++)
            {
                _front[i] = SluicePuzzle.Lake;
                _lift[i] = Biosphere.GateLift;
                _turn[i] = 0f;
            }
            if (_wilt != null)
            {
                for (int k = 0; k < _wilt.Length; k++)
                {
                    _wilt[k] = 0f;
                    _bloomedTimer[k] = 0f;
                }
            }
            ApplyAll(0f);
        }

        // ---------------- 조작 ----------------

        private void Use(int stream)
        {
            if (_pause > 0f || _turn[stream] > 0f)
                return;
            if (!_puzzle.Running)
            {
                _puzzle.Start(System.Environment.TickCount);
                _time = 0f;
                _showingResult = false;
                for (int i = 0; i < S; i++)
                    _front[i] = 0f;   // 물꼬를 모두 닫음 → 물길이 마름
                Notice("1판: 목마른 밭에 물을 대요. 바퀴를 돌릴 때마다 물꼬가 한 단씩 내려가요", 5f);
                AudioService.TryPlay(l => l.UiOpen);
                return;
            }
            _puzzle.Turn(stream);
            _turn[stream] = TurnDegrees;
            _front[stream] = Mathf.Min(_front[stream], _puzzle.Gate(stream));   // 위 단으로 바뀌면 아래는 바로 마름
            AudioService.TryPlay(l => l.BuildRotate);
        }

        private static string LevelName(int gate) => gate == 0 ? "닫힘" : gate > L ? "호수까지" : $"{gate}단";

        // ---------------- 매 프레임 ----------------

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_camera == null && Camera.main != null)
                _camera = Camera.main.transform;
            if (_noticeTimer > 0f)
                _noticeTimer -= dt;
            if (_puzzle.Running && _pause <= 0f)
                _time += dt;
            if (_pause > 0f)
            {
                _pause -= dt;
                if (_pause <= 0f)
                    EndPause();
            }
            else if (_puzzle.Running && RoundWatered())
            {
                int done = _puzzle.Round + 1;
                if (done >= SluicePuzzle.Rounds)
                    Finish();
                else
                {
                    Notice($"{done}판 성공! 다음 판은 목마른 밭이 {done + 1}곳", RoundPause + 2.5f);
                    _pause = RoundPause;
                    AudioService.TryPlay(l => l.RepairDone);
                }
            }
            for (int i = 0; i < S; i++)
                Step(i, dt);
            ApplyAll(dt);
            UpdateTexts();
            UpdateHint();
        }

        /// <summary>목마른 밭 모두에 물꼬가 맞고 물이 실제로 닿아 다 피었음.</summary>
        private bool RoundWatered()
        {
            if (!_puzzle.Solved)
                return false;
            foreach (var t in _puzzle.Thirsty)
            {
                if (_front[t.Stream] < t.Level || _wilt[t.Stream * L + t.Level - 1] > 0.05f)
                    return false;
            }
            return true;
        }

        private void EndPause()
        {
            if (_showingResult)
            {
                ResetAll();   // 결과를 보여 준 뒤: 물꼬를 모두 호수까지 (물이 다시 흘러 내려감)
                for (int i = 0; i < S; i++)
                    _front[i] = 0f;
                return;
            }
            _puzzle.Advance();
        }

        private void Finish()
        {
            _puzzle.Advance();
            bool record = _best < 0f || _time < _best;
            if (record)
                _best = _time;
            Notice($"다 피었어요!  걸린 시간 {Clock(_time)}" + (record ? "  ·  최고 기록!" : $"  ·  최고 {Clock(_best)}"), ResultSeconds);
            _showingResult = true;
            _pause = ResultSeconds;
            AudioService.TryPlay(l => l.UiOpen);
        }

        private static string Clock(float t) => $"{(int)(t / 60f)}:{(int)(t % 60f):00}";

        private void Step(int s, float dt)
        {
            int gate = _puzzle.Gate(s);
            // 문판: 열림 (물꼬 1단 이상) = 0.5 m 올라감
            _lift[s] = Mathf.MoveTowards(_lift[s], gate > 0 ? Biosphere.GateLift : 0f, PlateSpeed * dt);
            // 물 앞: 문판이 반쯤 열린 뒤부터 물꼬 단까지 흘러 내려감
            if (gate > 0 && _lift[s] > Biosphere.GateLift * 0.5f)
                _front[s] = Mathf.MoveTowards(_front[s], gate, FlowSpeed * dt);
            else if (gate == 0)
                _front[s] = 0f;
            // 바퀴
            if (_turn[s] > 0f)
            {
                float step = Mathf.Min(_turn[s], TurnDegrees / TurnSeconds * dt);
                _turn[s] -= step;
                _wheelAngle[s] = Mathf.Repeat(_wheelAngle[s] + step, 360f);
            }
            // 밭: 목마르고 물이 안 닿으면 시듦, 물이 닿으면 핌
            for (int n = 1; n <= L; n++)
            {
                int k = s * L + n - 1;
                bool watered = gate == n && _front[s] >= n;
                bool thirsty = _puzzle.IsThirsty(s, n);
                float before = _wilt[k];
                if (thirsty && !watered)
                    _wilt[k] = Mathf.MoveTowards(_wilt[k], 1f, dt / WiltSeconds);
                else
                    _wilt[k] = Mathf.MoveTowards(_wilt[k], 0f, dt / BloomSeconds);
                if (before > 0.5f && _wilt[k] <= 0.5f && watered)
                {
                    _bloomedTimer[k] = BloomedSeconds;
                    AudioService.TryPlay(l => l.SupplyArrive);
                }
                if (_bloomedTimer[k] > 0f)
                    _bloomedTimer[k] -= dt;
            }
        }

        // ---------------- 보이는 것 ----------------

        private void ApplyAll(float dt)
        {
            for (int s = 0; s < S; s++)
            {
                float f = _front[s];
                for (int n = 0; n <= L; n++)
                {
                    bool chan = n == 0 ? f > 0.3f : f >= n - 0.3f;
                    bool spill = n == 0 ? f > 0.05f : f >= n + 0.35f;
                    SetOn(_chans, s * (L + 1) + n, chan);
                    SetOn(_spills, s * (L + 1) + n, spill);
                    if (_fx != null)
                    {
                        foreach (int idx in Biosphere.SpillLandings(s, n))
                            _fx.SetLanding(idx, spill);
                    }
                }
                if (_plates != null && s < _plates.Length && _plates[s] != null)
                    _plates[s].localPosition = _plateBase[s] + Vector3.up * _lift[s];
                if (_wheels != null && s < _wheels.Length && _wheels[s] != null)
                    _wheels[s].localRotation = Quaternion.AngleAxis(_wheelAngle[s], Biosphere.GateWheelAxis(s));
                if (_waterWheels != null && s < _waterWheels.Length && _waterWheels[s] != null)
                {
                    float want = f >= 2.35f ? 1f : 0f;   // 물레방아 = 2단 → 3단 폭포 앞
                    var w = _waterWheels[s];
                    w.SpeedScale = dt <= 0f ? want : Mathf.MoveTowards(w.SpeedScale, want, dt * 0.8f);
                }
            }
            if (_crops == null)
                return;
            for (int k = 0; k < _crops.Length && k < _wilt.Length; k++)
            {
                var pivot = _crops[k];
                if (pivot == null)
                    continue;
                float w = _wilt[k];
                float ease = w * w * (3f - 2f * w);
                pivot.localScale = new Vector3(1f, Mathf.Lerp(1f, WiltHeight, ease), 1f);
                Tint(k, ease);
            }
        }

        private static void SetOn(Renderer[] list, int index, bool on)
        {
            if (list != null && index < list.Length && list[index] != null && list[index].enabled != on)
                list[index].enabled = on;
        }

        private void CollectCrops()
        {
            if (_crops == null)
                return;
            _cropRenderers = new Renderer[_crops.Length];
            _cropBase = new Color[_crops.Length][];
            for (int k = 0; k < _crops.Length; k++)
            {
                if (_crops[k] == null)
                    continue;
                var r = _crops[k].GetComponentInChildren<Renderer>();
                _cropRenderers[k] = r;
                if (r == null)
                    continue;
                var mats = r.sharedMaterials;
                _cropBase[k] = new Color[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                    _cropBase[k][i] = mats[i] != null && mats[i].HasProperty(BaseColorId) ? mats[i].GetColor(BaseColorId) : Color.white;
            }
        }

        private readonly float[] _tintApplied = new float[S * L];

        /// <summary>시든 정도만큼 빛바랜 흙빛 (재질마다 — 밀 · 잎 · 꽃잎이 한 물체에 섞여 있음).</summary>
        private void Tint(int k, float w)
        {
            var r = _cropRenderers != null ? _cropRenderers[k] : null;
            if (r == null || Mathf.Abs(_tintApplied[k] - w) < 0.004f)
                return;
            _tintApplied[k] = w;
            var bases = _cropBase[k];
            if (w <= 0.004f)
            {
                _block.Clear();   // 빈 블록 = 덮어쓰기 없음 (재질 색 그대로)
                for (int i = 0; i < bases.Length; i++)
                    r.SetPropertyBlock(_block, i);
                return;
            }
            for (int i = 0; i < bases.Length; i++)
            {
                var c = bases[i];
                float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                float bright = 0.55f + 0.5f * lum;
                var dry = new Color(DryColor.r * bright, DryColor.g * bright, DryColor.b * bright, c.a);
                _block.Clear();
                _block.SetColor(BaseColorId, Color.Lerp(c, dry, w * 0.85f));
                r.SetPropertyBlock(_block, i);
            }
        }

        // ---------------- 글자 ----------------

        private void BuildTexts()
        {
            if (_font == null)
                return;
            _labels = new TextMeshPro[S];
            for (int i = 0; i < S; i++)
                _labels[i] = MakeText("ThirstyLabel" + i, Vector3.zero, 0f, new Vector2(1.6f, 0.7f), 1.6f);
            _boards = new TextMeshPro[S * 2];
            for (int i = 0; i < S; i++)
            {
                var at = Biosphere.GateWheelHub(i) + Vector3.up * 0.62f;
                float yaw = Biosphere.OutwardYaw(Biosphere.Streams[i]);   // 앞 = 바깥을 봄 → 글자는 허브 쪽 (능선 길)에서 읽힘
                for (int k = 0; k < 2; k++)
                    _boards[i * 2 + k] = MakeText($"SluiceBoard{i}_{k}", at, yaw + 180f * k, new Vector2(1.5f, 0.62f), 1.2f);
            }
        }

        private TextMeshPro MakeText(string name, Vector3 at, float yaw, Vector2 size, float maxSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var text = go.AddComponent<TextMeshPro>();
            text.font = _font;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.5f;
            text.fontSizeMax = maxSize;
            text.fontSize = maxSize;
            text.color = new Color(1f, 0.96f, 0.84f);
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.sizeDelta = size;
            text.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (text.fontMaterial.HasProperty(CullModeId))
                text.fontMaterial.SetFloat(CullModeId, (float)UnityEngine.Rendering.CullMode.Back);
            return text;
        }

        private void UpdateTexts()
        {
            if (_labels != null)
            {
                for (int i = 0; i < S; i++)
                {
                    var label = _labels[i];
                    int show = -1;
                    bool bloomed = false;
                    // 이 물길의 목마른 밭 (판마다 물길당 하나) — 막 핀 밭은 잠깐 "피었어요"
                    for (int n = 1; n <= L; n++)
                    {
                        int k = i * L + n - 1;
                        if (_puzzle.IsThirsty(i, n) && _wilt[k] > 0.05f)
                            show = n;
                        else if (_bloomedTimer[k] > 0f && show < 0)
                        {
                            show = n;
                            bloomed = true;
                        }
                    }
                    bool on = show > 0;
                    if (label.gameObject.activeSelf != on)
                        label.gameObject.SetActive(on);
                    if (!on)
                        continue;
                    label.transform.localPosition = Biosphere.FieldLabel(i, show);
                    if (_camera != null)
                    {
                        var to = label.transform.position - _camera.position;
                        to.y = 0f;
                        if (to.sqrMagnitude > 1e-4f)
                            label.transform.rotation = Quaternion.LookRotation(to);
                    }
                    float pulse = bloomed ? 1f : 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));   // 깜박임 = 밝기 (색약)
                    label.color = new Color(1f, 0.95f, 0.8f, pulse);
                    label.SetText(bloomed ? "피었어요!" : $"목말라요\n<size=70%>{show}단</size>");
                }
            }
            if (_boards == null)
                return;
            for (int i = 0; i < S; i++)
            {
                string text;
                if (_showingResult)
                    text = $"다 피었어요!\n<size=65%>걸린 시간 {Clock(_time)} · 최고 {Clock(_best)}</size>";
                else if (_puzzle.Running)
                    text = $"{_puzzle.Round + 1} / {SluicePuzzle.Rounds}판  ·  {Clock(_time)}\n<size=70%>이 물꼬: {LevelName(_puzzle.Gate(i))}</size>";
                else
                    text = "물꼬 맞추기\n<size=60%>바퀴를 보고 F = 시작" + (_best >= 0f ? $"  ·  최고 {Clock(_best)}" : "") + "</size>";
                _boards[i * 2].SetText(text);
                _boards[i * 2 + 1].SetText(text);
            }
        }

        // ---------------- 화면 안내 ----------------

        private void Notice(string text, float seconds)
        {
            _notice = text;
            _noticeTimer = seconds;
        }

        private void UpdateHint()
        {
            string text = null;
            if (_puzzle.Running)
            {
                int left = 0;
                foreach (var t in _puzzle.Thirsty)
                {
                    if (_wilt[t.Stream * L + t.Level - 1] > 0.05f)
                        left++;
                }
                text = $"물꼬 맞추기  {_puzzle.Round + 1} / {SluicePuzzle.Rounds}판  ·  목마른 밭 {left}곳  ·  {Clock(_time)}\n"
                     + $"<size=80%>수문 바퀴 {KeyBindings.Label(GameAction.Interact)} = 물꼬 한 단 내리기 (닫힘 → 1단 (위) … 5단 (호수 쪽) → 닫힘) · 목마른 밭에 한꺼번에 물을 대요</size>";
            }
            if (_noticeTimer > 0f && !string.IsNullOrEmpty(_notice))
                text = text == null ? _notice : _notice + "\n<size=80%>" + text + "</size>";
            if (InteriorFireflyCatch.Active)
                text = null;
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
            var go = new GameObject("SluiceHint", typeof(RectTransform));
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
            Place(back.rectTransform, new Vector2(1180f, 112f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(1150f, 104f));
            if (_font != null)
                _hint.font = _font;
            _hint.fontSize = 26f;
            _hint.enableAutoSizing = true;
            _hint.fontSizeMin = 15f;
            _hint.fontSizeMax = 26f;
            _hint.color = new Color(0.92f, 0.96f, 1f);
            _hint.alignment = TextAlignmentOptions.Center;
            _hint.raycastTarget = false;
        }

        private static void Place(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 92f);
            rt.sizeDelta = size;
        }
    }
}
