using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// AI 중추 미니게임 "뇌 미로 빨리 빠져나가기" (사용자 2026-11-16 미니게임 1안 + "빛 구슬 모으는 부분도 같이 넣어줘", 판 = <see cref="MazeRunGame"/>, 값 = <see cref="AICore"/>):
    /// 장미 아치 문 기록판을 보고 F = 3초 센 뒤 시간을 잼 → 미로 막다른 길 다섯 곳 중 셋에 빛 구슬이 떠 있음 (구슬 빛 · 위로 오르는 반짝이가 생울타리 너머로 보임) →
    /// 구슬에 다가가면 주움 → 셋을 다 모아 가운데 정자 단에 오르면 도착 (기록 · 이번 실행 최고 기록 — 기록판에도). 모자란 채 정자에 오면 남은 수를 알려 줌.
    /// 달리는 중 기록판 F = 처음부터 다시, 5분이 넘거나 방을 나가면 기록 없이 끝. 위치는 템플릿 루트 (이 물체) 기준, 시간은 실제 시간. 템플릿 빌더가 configure에서 붙임.
    /// </summary>
    [DefaultExecutionOrder(-320)]
    public sealed class InteriorMazeRun : MonoBehaviour
    {
        private const float OrbSize = 0.26f, OrbBob = 0.08f, SparkEvery = 0.45f, NeedMoreEvery = 3f, MessageTime = 6f, SignInterval = 0.25f;
        private static readonly Color OrbLight = new Color(1f, 0.93f, 0.78f);

        /// <summary>이번 실행에서 가장 빠른 도착 (초, 없으면 0).</summary>
        private static float _best;

        [Tooltip("기록판 (따로 조각 MazeBoard, 원점 = 판 앞면 가운데 · +x를 봄)")]
        [SerializeField] private Transform _board;
        [Tooltip("기록판 충돌체 (보고 F = 출발)")]
        [SerializeField] private Collider _boardUse;
        [SerializeField] private Material _orbMaterial;
        [SerializeField] private Material _sparkMaterial;
        [SerializeField] private TMP_FontAsset _font;

        private MazeRunGame _game;
        private FirstPersonController _player;
        private Transform[] _orbs;
        private Light[] _orbLights;
        private float _sparkTimer, _needMoreTimer;
        private ParticleSystem _sparks;
        private TextMeshPro _boardText;
        private float _signTimer;
        private string _message;
        private float _messageTimer;
        private int _lastCount = -1;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public MazeRunGame Game => _game;
        public static float Best => _best;
        /// <summary>이번 판 i번째 빛 구슬 (템플릿 기준 자리).</summary>
        public Vector3 OrbPosition(int i) => AICore.Orbs[_game.Orbs[i]].At;

        public void Configure(Transform board, Collider boardUse, Material orbMaterial, Material sparkMaterial, TMP_FontAsset font)
        {
            _board = board;
            _boardUse = boardUse;
            _orbMaterial = orbMaterial;
            _sparkMaterial = sparkMaterial;
            _font = font;
        }

        private void Awake()
        {
            var front = new bool[AICore.Orbs.Length];
            for (int i = 0; i < front.Length; i++)
                front[i] = AICore.Orbs[i].Front;
            _game = new MazeRunGame(front);
            if (_boardUse != null)
            {
                var use = _boardUse.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => _game.Running ? "뇌 미로 처음부터 다시"
                    : "뇌 미로 출발  <size=80%>(빛 구슬 3개를 모아 정자로" + (_best > 0f ? $" · 최고 {Clock(_best)})" : ")") + "</size>";
                use.Completed = Begin;
            }
            BuildOrbs();
            BuildBoardText();
        }

        private void OnDisable()
        {
            // 방을 나가면 판을 접음 (기록 없음)
            _game?.Stop();
            ShowOrbs(false);
            _message = null;
            _messageTimer = 0f;
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
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            var before = _game.Current;
            _game.Step(dt);
            if (_game.Current != before)
                Entered(_game.Current);
            if (_game.Current == MazeRunGame.Phase.Run)
                TrackPlayer();
            MoveOrbs(dt);
            _messageTimer = Mathf.Max(0f, _messageTimer - dt);
            _needMoreTimer = Mathf.Max(0f, _needMoreTimer - dt);
            UpdateBoard(dt);
            UpdateHint();
        }

        // ---------------- 판 ----------------

        private void Begin()
        {
            _game.Begin();
            _messageTimer = 0f;
            for (int i = 0; i < _orbs.Length; i++)
            {
                bool on = i < _game.Needed;
                _orbs[i].gameObject.SetActive(on);
                if (on)
                    _orbs[i].localPosition = OrbPosition(i);
            }
            AudioService.TryPlay(l => l.GameStart);
        }

        private void Entered(MazeRunGame.Phase phase)
        {
            if (phase == MazeRunGame.Phase.Run)
            {
                AudioService.TryPlay(l => l.GameScore);   // 3초 세기 뒤 "출발" — 점수 "띵" 도6 한 음 (시작음은 Begin에서 이미, 점수 연속과는 따로)
                for (int i = 0; i < _game.Needed; i++)
                    InteriorFireflyShow.Burst(EnsureSparks(), _orbs[i].position, 18, 0.9f);
            }
            if (phase == MazeRunGame.Phase.Done)
            {
                ShowOrbs(false);
                if (_game.TimedOut)
                    Flash("시간이 다 됐어요 (5분) · 기록판에서 다시 출발");
            }
        }

        private void TrackPlayer()
        {
            if (_player == null || !_player.isActiveAndEnabled)
                return;
            var p = transform.InverseTransformPoint(_player.transform.position);
            for (int i = 0; i < _game.Needed; i++)
            {
                if (_game.Taken(i))
                    continue;
                var o = OrbPosition(i);
                if (new Vector2(p.x - o.x, p.z - o.z).magnitude < AICore.OrbReach && Mathf.Abs(p.y + 1f - o.y) < 1.6f)
                {
                    var r = _game.Collect(i);
                    InteriorFireflyShow.Burst(EnsureSparks(), _orbs[i].position, 28, 1.3f);
                    _orbs[i].gameObject.SetActive(false);
                    AudioService.TryPlayNote(l => l.AicoreOrbCollect, Mathf.Min(_game.Collected, 8) - 1);   // 모은 차례대로 음계 (끝 칸에서 멈춤)
                    if (r == MazeRunGame.Result.AllCollected)
                        Flash("빛 구슬을 다 모았어요!  가운데 정자로");
                }
            }
            var c = AICore.Pavilion;
            if (new Vector2(p.x - c.x, p.z - c.z).magnitude < AICore.MazeGoalRadius && p.y > AICore.PodTop - 0.15f)
            {
                var r = _game.Arrive();
                if (r == MazeRunGame.Result.Finished)
                    Finish();
                else if (r == MazeRunGame.Result.NeedMore && _needMoreTimer <= 0f)
                {
                    _needMoreTimer = NeedMoreEvery;
                    Flash($"빛 구슬 {_game.Collected} / {_game.Needed}  ·  남은 구슬은 미로 막다른 길에 있어요");
                    AudioService.TryPlay(l => l.GameMiss);
                }
            }
        }

        private void Finish()
        {
            bool record = _best <= 0f || _game.Score < _best;
            if (record)
                _best = _game.Score;
            Flash(record ? $"도착!  {Clock(_game.Score)}  ·  새 최고 기록" : $"도착!  {Clock(_game.Score)}  ·  최고 {Clock(_best)}");
            InteriorFireflyShow.Burst(EnsureSparks(), transform.TransformPoint(AICore.Brain), 40, 1.6f);
            AudioService.TryPlay(l => record ? l.GameRecord : l.GameClear);
            _signTimer = 0f;
        }

        private void Flash(string text)
        {
            _message = text;
            _messageTimer = MessageTime;
        }

        /// <summary>분:초.십분의 일초 (0:23.4).</summary>
        private static string Clock(float seconds)
        {
            int tenths = Mathf.FloorToInt(seconds * 10f);
            int s = tenths / 10;
            return $"{s / 60}:{s % 60:00}.{tenths % 10}";
        }

        // ---------------- 빛 구슬 ----------------

        private void BuildOrbs()
        {
            _orbs = new Transform[AICore.MazeOrbCount];
            _orbLights = new Light[_orbs.Length];
            for (int i = 0; i < _orbs.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(go.GetComponent<Collider>());
                go.name = "MazeOrb" + i;
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * OrbSize;
                var rend = go.GetComponent<MeshRenderer>();
                rend.sharedMaterial = _orbMaterial;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var lightGo = new GameObject("OrbLight");
                lightGo.transform.SetParent(go.transform, false);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 3.2f;
                l.intensity = 1.6f;
                l.color = OrbLight;
                l.shadows = LightShadows.None;
                _orbLights[i] = l;
                _orbs[i] = go.transform;
                go.SetActive(false);
            }
        }

        private void ShowOrbs(bool on)
        {
            if (_orbs == null)
                return;
            foreach (var o in _orbs)
                if (o != null)
                    o.gameObject.SetActive(on && o.gameObject.activeSelf);
        }

        /// <summary>구슬: 위아래로 살짝 떠다니며 숨 쉬듯 밝기 · 위로 오르는 반짝이 (생울타리 너머에서도 자리가 보임).</summary>
        private void MoveOrbs(float dt)
        {
            if (_orbs == null || !_game.Running)
                return;
            float t = Time.unscaledTime;
            _sparkTimer -= dt;
            bool spark = _sparkTimer <= 0f && _game.Current == MazeRunGame.Phase.Run;
            if (spark)
                _sparkTimer = SparkEvery;
            for (int i = 0; i < _game.Needed && i < _orbs.Length; i++)
            {
                var o = _orbs[i];
                if (!o.gameObject.activeSelf)
                    continue;
                o.localPosition = OrbPosition(i) + Vector3.up * (OrbBob * Mathf.Sin(t * 2.1f + i * 1.7f));
                float breathe = 0.5f + 0.5f * Mathf.Sin(t * 3.1f + i);
                o.localScale = Vector3.one * OrbSize * (1f + 0.12f * breathe);
                _orbLights[i].intensity = 1.2f + 0.8f * breathe;
                if (spark)
                    RiseSparks(o.position);
            }
        }

        private void RiseSparks(Vector3 at)
        {
            var ps = EnsureSparks();
            if (ps == null)
                return;
            for (int k = 0; k < 3; k++)
            {
                ps.Emit(new ParticleSystem.EmitParams
                {
                    position = at + Random.insideUnitSphere * 0.12f,
                    velocity = new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(1.4f, 2.0f), Random.Range(-0.1f, 0.1f)),
                    startLifetime = Random.Range(1.2f, 1.7f),
                    startSize = Random.Range(0.06f, 0.11f),
                    startColor = Color.Lerp(OrbLight, Color.white, Random.value),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        private ParticleSystem EnsureSparks()
        {
            if (_sparks == null)
                _sparks = InteriorFireflyShow.CreateSparks(transform, _sparkMaterial);
            return _sparks;
        }

        // ---------------- 기록판 · 안내 (색 대신 글 · 숫자) ----------------

        private void BuildBoardText()
        {
            if (_font == null || _board == null)
                return;
            var go = new GameObject("MazeBoardText");
            go.transform.SetParent(_board, false);
            go.transform.localPosition = new Vector3(0.004f, 0f, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);   // 판 앞면 (+x)을 봄
            _boardText = go.AddComponent<TextMeshPro>();
            _boardText.font = _font;
            _boardText.fontSize = 1.0f;
            _boardText.enableAutoSizing = true;
            _boardText.fontSizeMax = 1.0f;
            _boardText.fontSizeMin = 0.2f;
            _boardText.color = new Color(0.36f, 0.24f, 0.1f);
            _boardText.alignment = TextAlignmentOptions.Center;
            _boardText.textWrappingMode = TextWrappingModes.NoWrap;
            _boardText.overflowMode = TextOverflowModes.Truncate;
            _boardText.rectTransform.sizeDelta = AICore.MazeBoardSize - new Vector2(0.06f, 0.05f);
        }

        private void UpdateBoard(float dt)
        {
            _signTimer -= dt;
            if (_boardText == null || _signTimer > 0f)
                return;
            _signTimer = SignInterval;
            string line;
            if (_game.Current == MazeRunGame.Phase.Countdown)
                line = $"준비 {Mathf.CeilToInt(MazeRunGame.CountdownTime - _game.PhaseTime)}";
            else if (_game.Current == MazeRunGame.Phase.Run)
                line = $"{Clock(_game.Elapsed)}  ·  구슬 {_game.Collected}/{_game.Needed}";
            else
                line = _best > 0f ? $"최고 기록 {Clock(_best)}" : "빛 구슬 3개를 모아 정자로";
            int key = (int)_game.Current * 100000 + Mathf.FloorToInt(_game.Elapsed * 10f) * 10 + _game.Collected;
            if (key == _lastCount)
                return;
            _lastCount = key;
            _boardText.SetText($"뇌 미로\n<size=62%>{line}</size>");
        }

        private void UpdateHint()
        {
            string text = null;
            if (_messageTimer > 0f && _message != null)
                text = _message;
            else if (_game.Current == MazeRunGame.Phase.Countdown)
                text = $"뇌 미로  ·  준비 {Mathf.CeilToInt(MazeRunGame.CountdownTime - _game.PhaseTime)}  ·  막다른 길의 빛 구슬 {_game.Needed}개를 모아 가운데 정자로";
            else if (_game.Current == MazeRunGame.Phase.Run)
                text = $"뇌 미로  ·  {Clock(_game.Elapsed)}  ·  빛 구슬 {_game.Collected} / {_game.Needed}" + (_game.Collected >= _game.Needed ? "  ·  정자로!" : "");
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
            var go = new GameObject("MazeRunHint", typeof(RectTransform));
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
            Place(back.rectTransform, new Vector2(900f, 52f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(880f, 44f));
            if (_font != null)
                _hint.font = _font;
            _hint.fontSize = 26f;
            _hint.enableAutoSizing = true;
            _hint.fontSizeMin = 15f;
            _hint.fontSizeMax = 26f;
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
