using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Interior;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 13-7c 주민 한마디 (REDESIGN T7, 사용자 결정 2026-10-19): 장 시작(지난 절정 결과에 따라) · 절정 예고 · 이민선에 답한 뒤 주민 한 명이 종족 말투로 한마디.
    /// - 바깥: 화면 왼쪽 아래에 "토끼 미나 · …" 한 줄이 몇 초 떴다 저절로 사라짐 / 안(1인칭): 그 주민이 보이면 머리 위 말풍선 (테두리 = 종족 색), 보이는 주민이 없으면 안에서도 한 줄
    /// - 할 일처럼 보이는 것은 붙이지 않음 ('!' · 개수 · 보상 없음, 사용자 결정: 안에 들어갈 의무도 놓칠 걱정도 없게)
    /// - 시간이 멈춘 동안(결과 창 · 연출 · 도착 창)은 기다렸다가 다시 흐른 뒤 차례로. 평소 이민선은 5분(게임 시간)에 한 번까지만
    /// 문장은 종족 데이터(<see cref="SpeciesData.Remarks"/>), 고르기는 <see cref="ResidentRemarks"/>.
    /// </summary>
    public sealed class ResidentRemarksView : MonoBehaviour
    {
        private const string Soft = "#C8D2DE";

        [SerializeField] private StationController _station;
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [SerializeField] private HoloArt _holoArt;
        [Tooltip("한마디가 떠 있는 시간 (실제 초, 글이 길면 조금 더)")]
        [SerializeField] private float _holdSeconds = 5.5f;
        [Tooltip("시간이 다시 흐른 뒤 기다리는 시간 (실제 초) — 창이 닫히자마자 겹치지 않게")]
        [SerializeField] private float _afterResume = 1.5f;
        [Tooltip("평소 이민선 한마디 사이 최소 간격 (게임 초, 처음 만나는 종족은 늘)")]
        [SerializeField] private float _shipCooldown = 300f;
        [Tooltip("안에서: 이 거리 안에 보이는 주민이 말함 (m)")]
        [SerializeField] private float _bubbleRange = 15f;

        private StationSimulation _sim;
        private ChapterSystem _chapters;
        private ArkShipSystem _ark;
        private InteriorMode _interior;
        private HoloUi _ui;
        private CanvasGroup _group;
        private TMP_Text _who, _line;
        private SpeechBubble _bubble;
        private Material _bubbleMaterial;
        private ResidentFigure _bubbleFigure;
        private readonly Queue<(string[] keys, string exclude)> _queue = new Queue<(string[], string)>();
        private readonly RaycastHit[] _sightHits = new RaycastHit[16];
        private ChapterOutcome? _lastOutcome;
        private float _shownAt = -1f, _shownFor, _resumedAt, _lastShip = -99999f;
        private bool _wasPaused = true;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            _chapters = _sim?.Chapters;
            if (_chapters == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _ark = _sim.GetSystem<ArkShipSystem>();
            _interior = FindFirstObjectByType<InteriorMode>(FindObjectsInactive.Include);
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            Build();
            _chapters.ChapterStarted += HandleChapterStarted;
            _chapters.ClimaxEnded += HandleClimaxEnded;
            _chapters.WarningStarted += HandleWarning;
            if (_ark != null)
                _ark.Resolved += HandleShip;
            var host = FindFirstObjectByType<SimulationHost>();
            if (host != null && host.LoadedSave == null && _chapters.Index == 0 && _chapters.ChapterSeconds < 0.5f)
                _queue.Enqueue((ResidentRemarks.ChapterStartKeys(1, null), null)); // 새 캠페인: 컷신이 끝나고 첫 한마디
        }

        private void OnDestroy()
        {
            if (_chapters != null)
            {
                _chapters.ChapterStarted -= HandleChapterStarted;
                _chapters.ClimaxEnded -= HandleClimaxEnded;
                _chapters.WarningStarted -= HandleWarning;
            }
            if (_ark != null)
                _ark.Resolved -= HandleShip;
            if (_bubble != null)
                Destroy(_bubble.gameObject);
        }

        /// <summary>확인용: 이 키로 한마디를 줄에 넣음 (시간이 흐르면 바로).</summary>
        public void SayNow(params string[] keys) => _queue.Enqueue((keys, null));

        private void HandleChapterStarted(int index) => _queue.Enqueue((ResidentRemarks.ChapterStartKeys(index + 1, _lastOutcome), null));

        private void HandleClimaxEnded(ChapterRecord record) => _lastOutcome = record?.Outcome; // 결과는 다음 장 시작 한마디에 담김

        private void HandleWarning() => _queue.Enqueue((ResidentRemarks.WarningKeys(_chapters.Index + 1), null));

        private void HandleShip(ArkShip ship, ShipAnswer answer, int boarded)
        {
            if (ship == null)
                return;
            if (!ship.NewSpecies && _sim.ElapsedSeconds - _lastShip < _shipCooldown)
                return; // 평소 배는 드물게 (대이주처럼 연달아 와도 한 번)
            _lastShip = _sim.ElapsedSeconds;
            // 처음 만나는 종족을 맞는 말은 원래 살던 주민이
            _queue.Enqueue((ResidentRemarks.ShipKeys(answer, ship.NewSpecies, boarded), ship.NewSpecies ? ship.Species : null));
        }

        // ---------------- 보이기 ----------------

        private void Update()
        {
            bool paused = _clock == null || _clock.Clock.IsPaused;
            if (_wasPaused && !paused)
                _resumedAt = Time.unscaledTime;
            _wasPaused = paused;

            if (_shownAt >= 0f)
            {
                float e = Time.unscaledTime - _shownAt;
                float a = Mathf.Clamp01(e / 0.3f) * Mathf.Clamp01((_shownFor - e) / 0.6f);
                _group.alpha = _bubbleFigure == null ? a : 0f;
                PlaceBubble();
                if (e >= _shownFor)
                    Hide();
                return;
            }
            if (_queue.Count == 0 || paused || Time.unscaledTime - _resumedAt < _afterResume)
                return;
            var (keys, exclude) = _queue.Dequeue();
            Show(keys, exclude);
        }

        private void Show(string[] keys, string exclude)
        {
            // 안이면 보이는(세워 둔) 가까운 주민부터, 없으면 누구든
            var residents = _sim.Residents != null ? _sim.Residents.Residents : null;
            if (residents == null || residents.Count == 0)
                return;
            InteriorResidents inside = _interior != null && _interior.IsInside ? FindFirstObjectByType<InteriorResidents>() : null;
            var cam = Camera.main;
            Resident speaker = null;
            string text = null;
            if (inside != null && cam != null)
            {
                var near = new List<Resident>();
                foreach (var r in residents)
                {
                    var f = inside.Find(r.Id);
                    if (f != null && InSight(cam, f))
                        near.Add(r);
                }
                ResidentRemarks.Pick(near, Species, keys, Random01, out speaker, out text, exclude);
            }
            bool seen = speaker != null; // 보이는 주민이 말함 → 말풍선, 아니면 안에서도 바깥과 같은 한 줄
            if (!seen && !ResidentRemarks.Pick(residents, Species, keys, Random01, out speaker, out text, exclude))
                return;
            var species = Species(speaker.Kind);
            string kindName = species != null ? species.DisplayName : "";
            _shownAt = Time.unscaledTime;
            _shownFor = _holdSeconds + text.Length * 0.04f;
            _bubbleFigure = seen ? inside.Find(speaker.Id) : null;
            if (_bubbleFigure != null)
            {
                EnsureBubble();
                _bubble.gameObject.SetActive(true);
                _bubble.Set($"<size=70%><color=#5A6472>{kindName} {speaker.Name}</color></size>\n{text}", species != null ? species.ShipColor : Color.white);
                PlaceBubble();
                return;
            }
            _who.SetText($"<color={Soft}>{kindName} {speaker.Name}</color>");
            _line.SetText(text);
            _group.alpha = 0f;
        }

        private void Hide()
        {
            _shownAt = -1f;
            _group.alpha = 0f;
            _bubbleFigure = null;
            if (_bubble != null)
                _bubble.gameObject.SetActive(false);
        }

        private void PlaceBubble()
        {
            if (_bubble == null || !_bubble.gameObject.activeSelf)
                return;
            var eye = Camera.main != null ? Camera.main.transform : null;
            if (_bubbleFigure == null || eye == null || _interior == null || !_interior.IsInside)
            {
                _bubble.gameObject.SetActive(false); // 나왔거나 내부를 다시 만듦 → 말풍선만 접음
                return;
            }
            var inside = FindFirstObjectByType<InteriorResidents>();
            float lift = inside != null && inside.Looked == _bubbleFigure ? 0.62f : 0.12f; // 이름표가 뜨면 그 위로
            _bubble.Place(_bubbleFigure.TagPoint + Vector3.up * lift, eye);
        }

        private void EnsureBubble()
        {
            if (_bubble != null)
                return;
            if (_bubbleMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                    _bubbleMaterial = new Material(shader) { name = "RemarkBubble (runtime)" };
            }
            var root = new GameObject("ResidentRemarkBubble").transform; // 화면 캔버스가 아니라 월드에
            _bubble = SpeechBubble.Create(root, _font, _bubbleMaterial);
        }

        /// <summary>안에서 말풍선을 띄울 만큼 보이는가: 머리와 말풍선이 차지할 양 끝이 모두 화면 안 · 거리 안 · 벽에 가리지 않음 (말할 때 한 번만 봄).
        /// 아니면 안에서도 한 줄로 — 문틀에 반쯤 잘린 말풍선보다 읽힘.</summary>
        private bool InSight(Camera cam, ResidentFigure figure)
        {
            var eye = cam.transform;
            float d = Vector3.Distance(figure.TagPoint, eye.position);
            if (d < 0.5f || d > _bubbleRange)
                return false;
            // 말풍선 자리 (SpeechBubble.Place와 같은 크기: 5m까지 1배 · 최대 1.8배, 반폭 약 0.7m)
            float scale = Mathf.Clamp(d / 5f, 0.75f, 1.8f);
            var mid = figure.TagPoint + Vector3.up * (0.12f + 0.25f * scale);
            var half = eye.right * (0.7f * scale);
            var left = cam.WorldToViewportPoint(mid - half);
            var right = cam.WorldToViewportPoint(mid + half);
            // 오른쪽은 0.74까지 — 오른쪽 위 정거장 · 장 패널 밑으로 들어가지 않게
            if (left.z <= 0f || right.z <= 0f || left.x < 0.03f || right.x > 0.74f || left.y < 0.1f || left.y > 0.9f)
                return false;
            var from = eye.position;
            return Clear(from, figure.TagPoint - Vector3.up * 0.25f) && Clear(from, mid - half) && Clear(from, mid + half);
        }

        /// <summary>눈 → 점 사이에 벽이 없는가. 유리 · 창(이름에 Glass · Window — InteriorSpots와 같은 규칙)과 주민은 가림으로 치지 않음.</summary>
        private bool Clear(Vector3 from, Vector3 to)
        {
            var dir = to - from;
            int n = Physics.RaycastNonAlloc(from, dir.normalized, _sightHits, dir.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = _sightHits[i].collider;
                if (c.GetComponentInParent<ResidentFigure>() == null && !c.name.Contains("Glass") && !c.name.Contains("Window"))
                    return false;
            }
            return true;
        }

        private SpeciesData Species(string kind) => _ark != null ? _ark.Find(kind) : null;

        private static float Random01() => Random.value;

        private void Build()
        {
            var root = (RectTransform)transform;
            var panel = HoloUi.Rect("Remark", root);
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.zero;
            panel.anchoredPosition = new Vector2(24f, 250f); // 아래 조작 안내 줄 · 건설 막대보다 위
            panel.sizeDelta = new Vector2(540f, 78f);
            _ui.Panel(panel.gameObject, new Color(0.03f, 0.06f, 0.09f, 0.9f), HudTheme.AccentDim);
            _group = panel.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _who = _ui.Label(panel, "", 15f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(_who.rectTransform, new Vector2(16f, -8f), new Vector2(508f, 22f));
            _line = _ui.Label(panel, "", 20f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(_line.rectTransform, new Vector2(16f, -30f), new Vector2(508f, 44f));
        }
    }
}
