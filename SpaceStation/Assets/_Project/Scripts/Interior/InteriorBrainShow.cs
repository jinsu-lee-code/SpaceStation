using SpaceStation.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// AI 중추 체험 "수정 뇌 깨우기 빛 쇼" (사용자 2026-11-16 체험 2안, 값 = <see cref="AICore"/>): 정자 책 받침 넷의 덮인 책을 보고 F = 책장이 펼쳐짐 (펼친 수만큼 수정 뇌 속 빛이 밝아짐) →
    /// 넷을 다 펼치면 쇼 (<see cref="ShowSeconds"/>초): 수정 뇌가 환히 맥박치고 반짝이가 터짐 · 바닥 빛 신경 (앞 = 보라 · 뒤 = 금, PulseGlow 재질)을 따라 빛 물결이 뇌에서 미로 끝까지
    /// 퍼져 나감 (평소엔 미로에서 뇌로 천천히 모여드는 옅은 물결) · 시냅스 수정 여섯이 왼쪽부터 차례로 반짝임 → 끝나면 책이 하나씩 덮이고 처음으로.
    /// 시냅스 따라 켜기 판 중에는 책을 펼치지 않음. 위치는 템플릿 루트 (이 물체) 기준, 시간은 실제 시간. 템플릿 빌더가 configure에서 붙임.
    /// </summary>
    [DefaultExecutionOrder(-320)]
    public sealed class InteriorBrainShow : MonoBehaviour
    {
        public const float ShowSeconds = 24f;
        private const float BookOpenTime = 0.7f, BookClosed = 172f, CloseStagger = 0.35f, StartDelay = 0.8f;
        private const float RampTime = 2f, FadeTime = 3.5f;
        private static readonly int CenterId = Shader.PropertyToID("_Center");
        private static readonly int WaveId = Shader.PropertyToID("_Wave");
        private static readonly int FloorId = Shader.PropertyToID("_Floor");
        private static readonly int BeatId = Shader.PropertyToID("_Beat");
        private static readonly int SparkId = Shader.PropertyToID("_Spark");
        /// <summary>빛 신경 물결: 평소 (미로 → 뇌로 천천히 모여듦, 옅게) · 쇼 (뇌 → 미로 끝으로 퍼짐, 환하게) — 파장 · 빠르기 · 날카로움 · 세기.</summary>
        private static readonly Vector4 IdleWave = new Vector4(2.4f, -0.45f, 4f, 0.55f), ShowWave = new Vector4(3.2f, 3.0f, 3f, 2.4f);
        private const float IdleFloor = 0.28f, ShowFloor = 0.75f;

        [Tooltip("수정 뇌 (따로 조각 Brain, 원점 = 뇌 가운데)")]
        [SerializeField] private Transform _brain;
        [Tooltip("책 받침 넷의 오른쪽 책장 (BookR{k}, 원점 = 책등) — 덮였다 펼쳐짐")]
        [SerializeField] private Transform[] _books;
        [Tooltip("책 받침 넷 충돌체 (보고 F = 펼치기)")]
        [SerializeField] private Collider[] _lecterns;
        [Tooltip("바닥 빛 신경 (VeinsFront · VeinsBack — PulseGlow 칸)")]
        [SerializeField] private Renderer[] _veins;
        [SerializeField] private Material _sparkMaterial;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private InteriorSynapseMemory _memory;

        private Vector3[] _bookPos;
        private Quaternion[] _bookRot;
        private float[] _bookSign;
        private float[] _open;          // 0 = 덮임 · 1 = 펼침 (지금 각)
        private bool[] _wantOpen;
        private int[] _veinSlot;
        private MaterialPropertyBlock _block;
        private Light _brainLight;
        private Vector3 _brainScale;
        private ParticleSystem _sparks;
        private float _showTime = -1f, _startTimer = -1f, _closeTimer = -1f;
        private float _sparkTimer;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public bool Showing => _showTime >= 0f;
        public int OpenBooks
        {
            get
            {
                int n = 0;
                if (_wantOpen != null)
                    foreach (bool b in _wantOpen)
                        if (b)
                            n++;
                return n;
            }
        }
        /// <summary>책 k 펼친 정도 0 ~ 1 (시험용).</summary>
        public float BookOpen(int k) => _open != null ? _open[k] : 0f;
        /// <summary>쇼 중 시냅스 k 반짝임 0 ~ 1 (왼쪽부터 차례로 — 시냅스 따라 켜기가 같은 알맹이로 보여 줌).</summary>
        public float SynapseGlow(int k)
        {
            if (_showTime < 0f)
                return 0f;
            float t = _showTime - RampTime;
            if (t < 0f)
                return 0f;
            float phase = Mathf.Repeat(t * 1.6f - k * 0.45f, AICore.Synapses.Length * 0.45f + 1.2f);
            return Mathf.Clamp01(1f - Mathf.Abs(phase - 0.5f) * 2.2f) * Fade();
        }

        public void Configure(Transform brain, Transform[] books, Collider[] lecterns, Renderer[] veins, Material spark, InteriorSynapseMemory memory, TMP_FontAsset font)
        {
            _brain = brain;
            _books = books;
            _lecterns = lecterns;
            _veins = veins;
            _sparkMaterial = spark;
            _memory = memory;
            _font = font;
        }

        /// <summary>시냅스 따라 켜기 (서로 막음 — 템플릿 빌더가 둘 다 붙인 뒤 연결).</summary>
        public void Link(InteriorSynapseMemory memory) => _memory = memory;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            int n = _books != null ? _books.Length : 0;
            _bookPos = new Vector3[n];
            _bookRot = new Quaternion[n];
            _bookSign = new float[n];
            _open = new float[n];
            _wantOpen = new bool[n];
            for (int k = 0; k < n; k++)
            {
                if (_books[k] == null)
                    continue;
                _bookPos[k] = _books[k].localPosition;
                _bookRot[k] = _books[k].localRotation;
                // 접는 쪽 = 오른쪽 책장 가운데가 판 법선 쪽 (위)으로 들리는 쪽 (반대면 받침 속으로 접힘)
                var axis = AICore.BookSpineAxis(k);
                var side = Vector3.Cross(Vector3.up, AICore.LecternOut(k)).normalized;
                var up = Quaternion.AngleAxis(90f, axis) * side;
                _bookSign[k] = Vector3.Dot(up, AICore.LecternNormal(k)) > 0f ? 1f : -1f;
            }
            ApplyBooks();
            if (_lecterns != null)
            {
                for (int k = 0; k < _lecterns.Length; k++)
                {
                    if (_lecterns[k] == null)
                        continue;
                    int book = k;
                    var use = _lecterns[k].gameObject.AddComponent<InteriorInteractable>();
                    use.Tap = true;
                    use.Prompt = () => "책 펼치기  <size=80%>(" + OpenBooks + " / " + _wantOpen.Length + " · 넷을 펼치면 수정 뇌가 깨어나요)</size>";
                    use.Blocked = () => BookBlocked(book);
                    use.Completed = () => Open(book);
                }
            }
            if (_brain != null)
            {
                _brainScale = _brain.localScale;
                var lightGo = new GameObject("BrainGlow");
                lightGo.transform.SetParent(transform, false);
                lightGo.transform.localPosition = AICore.Brain + new Vector3(0f, 0.95f, -0.75f);   // 앞 위에서 비춤 (뇌 속에 두면 수정 껍질 안이 하얗게 타서 주름 없이 하트 모양 덩어리로 보였음)
                _brainLight = lightGo.AddComponent<Light>();
                _brainLight.type = LightType.Point;
                _brainLight.range = 6f;
                _brainLight.color = new Color(0.86f, 0.78f, 1f);
                _brainLight.shadows = LightShadows.None;
                _brainLight.intensity = 0f;
                _brainLight.enabled = false;
            }
            _veinSlot = new int[_veins != null ? _veins.Length : 0];
            for (int v = 0; v < _veinSlot.Length; v++)
            {
                _veinSlot[v] = -1;
                if (_veins[v] == null)
                    continue;
                var mats = _veins[v].sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                    if (mats[m] != null && mats[m].HasProperty(WaveId))
                        _veinSlot[v] = m;
            }
            ApplyVeins(0f);
        }

        private void OnDisable()
        {
            _showTime = _startTimer = _closeTimer = -1f;
            if (_wantOpen != null)
                for (int k = 0; k < _wantOpen.Length; k++)
                {
                    _wantOpen[k] = false;
                    _open[k] = 0f;
                }
            ApplyBooks();
            ApplyBrain(0f);
            ApplyVeins(0f);
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            for (int k = 0; k < _open.Length; k++)
                _open[k] = Mathf.MoveTowards(_open[k], _wantOpen[k] ? 1f : 0f, dt / BookOpenTime);
            ApplyBooks();
            if (_startTimer >= 0f)
            {
                _startTimer -= dt;
                if (_startTimer < 0f)
                    StartShow();
            }
            if (_showTime >= 0f)
            {
                _showTime += dt;
                _sparkTimer -= dt;
                if (_sparkTimer <= 0f && _showTime < ShowSeconds - FadeTime)
                {
                    _sparkTimer = 0.5f;
                    InteriorFireflyShow.Burst(EnsureSparks(), transform.TransformPoint(AICore.Brain + Vector3.up * 0.3f), 8, 1.1f);
                }
                if (_showTime >= ShowSeconds)
                    EndShow();
            }
            if (_closeTimer >= 0f)
            {
                _closeTimer += dt;
                for (int k = 0; k < _wantOpen.Length; k++)
                    if (_wantOpen[k] && _closeTimer >= k * CloseStagger)
                        _wantOpen[k] = false;
                if (OpenBooks == 0)
                    _closeTimer = -1f;
            }
            float glow = _showTime >= 0f ? Mathf.Clamp01(_showTime / RampTime) * Fade() : OpenBooks / (float)Mathf.Max(1, _wantOpen.Length) * 0.35f;
            ApplyBrain(glow);
            ApplyVeins(_showTime >= 0f ? Mathf.Clamp01(_showTime / 1.5f) * Fade() : 0f);
            UpdateHint();
        }

        private float Fade() => _showTime < 0f ? 0f : Mathf.Clamp01((ShowSeconds - _showTime) / FadeTime);

        // ---------------- 책 ----------------

        private string BookBlocked(int k)
        {
            if (Showing || _startTimer >= 0f)
                return "수정 뇌 빛 쇼 중";
            if (_closeTimer >= 0f)
                return "책을 덮는 중";
            if (_memory != null && _memory.Standing)
                return "시냅스 따라 켜기 중";
            if (_wantOpen[k])
                return "펼친 책";
            return null;
        }

        private void Open(int k)
        {
            if (BookBlocked(k) != null)
                return;
            _wantOpen[k] = true;
            InteriorFireflyShow.Burst(EnsureSparks(), transform.TransformPoint(AICore.BookSpine(k)), 10, 0.6f);
            AudioService.TryPlayAt(l => l.AicoreBookOpen, transform.TransformPoint(AICore.BookSpine(k)));   // 책장 넘김 + 펼쳐져 닿는 "툭"
            if (OpenBooks == _wantOpen.Length)
                _startTimer = StartDelay;
        }

        /// <summary>오른쪽 책장: 펼침 0° (Blender 그대로) ↔ 덮임 172° (책등 축 · 책장 두께 위로 접힘).</summary>
        private void ApplyBooks()
        {
            if (_books == null)
                return;
            for (int k = 0; k < _books.Length; k++)
            {
                if (_books[k] == null)
                    continue;
                float a = BookClosed * (1f - Mathf.SmoothStep(0f, 1f, _open[k])) * _bookSign[k];
                var pivot = AICore.BookSpine(k) + AICore.LecternNormal(k) * 0.022f;
                var q = Quaternion.AngleAxis(a, AICore.BookSpineAxis(k));
                _books[k].localPosition = pivot + q * (_bookPos[k] - pivot);
                _books[k].localRotation = q * _bookRot[k];
            }
        }

        // ---------------- 쇼 ----------------

        private void StartShow()
        {
            _showTime = 0f;
            _sparkTimer = 0f;
            InteriorFireflyShow.Burst(EnsureSparks(), transform.TransformPoint(AICore.Brain), 40, 1.6f);
            AudioService.TryPlay(l => l.AicoreCrystalSwell);   // 수정 뇌가 깨어나 유리 화음이 차오름
        }

        private void EndShow()
        {
            _showTime = -1f;
            _closeTimer = 0f;
            AudioService.TryPlay(l => l.UiClose);
        }

        /// <summary>수정 뇌 속 빛 (책 펼친 수만큼 은은히 → 쇼 때 환히 맥박) · 크기 맥박.</summary>
        private void ApplyBrain(float glow)
        {
            if (_brainLight != null)
            {
                float beat = _showTime >= 0f ? 1f + 0.35f * Mathf.Sin(_showTime * 2f * Mathf.PI * 1.1f) : 1f;
                _brainLight.intensity = 1.8f * glow * beat;
                _brainLight.enabled = _brainLight.intensity > 0.01f;
            }
            if (_brain != null)
            {
                float pulse = _showTime >= 0f ? 1f + 0.025f * glow * Mathf.Sin(_showTime * 2f * Mathf.PI * 1.1f) : 1f;
                _brain.localScale = _brainScale * pulse;
            }
        }

        /// <summary>빛 신경 물결 (재질 칸 블록 — PulseGlow 칸만): 0 = 평소 (모여듦 · 옅게) · 1 = 쇼 (퍼짐 · 환히). 물결 가운데 = 조각 원점 (뇌 밑 바닥).</summary>
        private void ApplyVeins(float show)
        {
            if (_veins == null || _block == null)
                return;
            var wave = show > 0.001f ? new Vector4(ShowWave.x, ShowWave.y, ShowWave.z, Mathf.Lerp(IdleWave.w, ShowWave.w, show)) : IdleWave;
            for (int v = 0; v < _veins.Length; v++)
            {
                if (_veins[v] == null || _veinSlot[v] < 0)
                    continue;
                _veins[v].GetPropertyBlock(_block, _veinSlot[v]);
                _block.SetVector(CenterId, Vector4.zero);
                _block.SetVector(WaveId, wave);
                _block.SetFloat(FloorId, Mathf.Lerp(IdleFloor, ShowFloor, show));
                _block.SetVector(BeatId, new Vector4(0.6f * show, 1.1f, 2.5f, 0f));
                _block.SetVector(SparkId, Vector4.zero);
                _veins[v].SetPropertyBlock(_block, _veinSlot[v]);
            }
        }

        private ParticleSystem EnsureSparks()
        {
            if (_sparks == null)
                _sparks = InteriorFireflyShow.CreateSparks(transform, _sparkMaterial);
            return _sparks;
        }

        // ---------------- 안내 ----------------

        private void UpdateHint()
        {
            string text = null;
            if (_showTime >= 0f)
                text = "수정 뇌가 깨어났어요  ·  빛이 신경을 따라 미로 끝까지 퍼져요";
            else if (_startTimer >= 0f)
                text = "책 넷이 모두 펼쳐졌어요";
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
            var go = new GameObject("BrainShowHint", typeof(RectTransform));
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
            Place(back.rectTransform, new Vector2(860f, 52f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(840f, 44f));
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
