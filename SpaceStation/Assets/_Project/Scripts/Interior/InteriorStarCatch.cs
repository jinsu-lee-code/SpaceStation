using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 심우주 통신기지 미니게임 "별똥별 잡기" (사용자 2026-11-17 미니게임 2안, 판 = <see cref="StarCatchGame"/>, 값 = <see cref="DeepSpaceComm"/>):
    /// 별자리 광장 천체 모형을 보고 F = 광장 앞에 서서 하늘을 올려다봄 → 3 · 2 · 1 → 30초 동안 하늘에서 광장으로 별똥별 (<see cref="ShootingStar"/> 꼬리)과
    /// 우주 쓰레기 (꼬리 없이 뒹구는 각진 판)가 천천히 떨어짐 → 바라보고 F (또는 클릭) = 잡음: 큰 별 +3 · 작은 별 +1 · 쓰레기 −1, 광장에 닿으면 놓침.
    /// 종류는 색이 아니라 크기 · 모양 · 꼬리로 구분 (색약), 겨눈 것은 1.3배로 커짐. 끝나면 점수 · 최고 기록, 클릭 = 다시, F · ESC = 내려옴 (판 중엔 ESC). 실제 시간.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 선 채 누른 F · ESC를 여기서 먼저 씀
    public sealed class InteriorStarCatch : MonoBehaviour
    {
        public const float BigSize = 0.22f, SmallSize = 0.11f;
        private const float AimSlack = 1.5f, AimPad = 0.12f, AimGrow = 1.3f;
        private static int _best = -1;
        private static Mesh _starMesh;

        private sealed class Falling
        {
            public StarCatchGame.Kind Kind;
            public Transform Body;
            public ShootingStar Trail;
            public Vector3 From, To;          // 템플릿 좌표
            public float T, Duration;
            public Vector3 Spin;
            public float Size => Kind == StarCatchGame.Kind.BigStar ? BigSize : Kind == StarCatchGame.Kind.SmallStar ? SmallSize : 0.16f;
        }

        [Tooltip("천체 모형 충돌체 (보이지 않음 — 보고 F)")]
        [SerializeField] private Collider _start;
        [SerializeField] private Material _streakMaterial;
        [SerializeField] private Material _sparkMaterial;
        [Tooltip("별 몸 (빛 재질)")]
        [SerializeField] private Material _starMaterial;
        [Tooltip("우주 쓰레기 (짙은 몸체)")]
        [SerializeField] private Material _junkMaterial;
        [SerializeField] private TMP_FontAsset _font;

        private readonly StarCatchGame _game = new StarCatchGame();
        private readonly List<Falling> _falling = new List<Falling>();
        private FirstPersonController _player;
        private bool _standing;
        private int _mountFrame, _standFrame = -1;
        private Vector3 _standPoint;
        private ParticleSystem _sparks;
        private int _aim = -1;
        private bool _recorded;
        private int _streak;   // 연달아 별을 잡은 수 (점수 "띵" 음높이) — 쓰레기 · 별 놓침 · 새 판 = 0
        private int _tickLeft = -1;   // 남은 시간 째깍 — 마지막으로 본 남은 초
        private string _notice;
        private float _noticeTimer;
        private InteriorHintLine _hint;

        public StarCatchGame Game => _game;
        public bool Standing => _standing;
        public int FallingCount => _falling.Count;
        public static int Best => _best;

        public void Configure(Collider start, Material streak, Material spark, Material star, Material junk, TMP_FontAsset font)
        {
            _start = start;
            _streakMaterial = streak;
            _sparkMaterial = spark;
            _starMaterial = star;
            _junkMaterial = junk;
            _font = font;
        }

        private void Awake()
        {
            if (_start != null)
            {
                var use = _start.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "별똥별 잡기" + (_best < 0 ? "" : $"  <size=80%>(최고 기록 {_best}점)</size>");
                use.Completed = Mount;
            }
            _hint = new InteriorHintLine(transform, "StarCatchHint", _font);
        }

        private void OnDisable()
        {
            Release();
            _game.Stop();
            ClearFalling();
            _hint?.Show(null);
        }

        private void OnDestroy() => _hint?.Destroy();

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_standing && _player != null && !_player.Riding)
                ClearStand();
            if (_noticeTimer > 0f)
                _noticeTimer -= dt;
            var kind = _game.Step(dt);
            if (kind.HasValue)
                Spawn(kind.Value);
            // 남은 시간 째깍: 5초부터 1초마다 (보이는 남은 초가 바뀌는 순간)
            int left = _game.Playing ? Mathf.CeilToInt(_game.Remaining) : -1;
            if (left != _tickLeft && left >= 1 && left <= 5)
                AudioService.TryPlay(l => l.GameTick);
            _tickLeft = left;
            MoveFalling(dt);
            _aim = _standing && _game.Playing ? Aimed() : -1;
            if (_standing)
                Controls();
            if (_game.Current == StarCatchGame.Phase.Done)
            {
                if (_falling.Count > 0)
                    ClearFalling();
                if (!_recorded)
                    Record();
            }
            ApplyAim();
            UpdateHint();
        }

        // ---------------- 서기 ----------------

        private void Mount()
        {
            if (_standing || _player == null || Time.frameCount == _standFrame)
                return;
            _standPoint = _player.transform.position;
            _player.RideAt(transform, transform.TransformPoint(DeepSpaceComm.CatchStand), FirstPersonController.EyeHeight, DeepSpaceComm.CatchYaw + transform.eulerAngles.y, DeepSpaceComm.CatchPitch);
            _standing = true;
            _mountFrame = Time.frameCount;
            if (_start != null)
                _start.enabled = false;
            Begin();
            AudioService.TryPlay(l => l.GameStart);
        }

        private void Begin()
        {
            ClearFalling();
            _game.Begin();
            _recorded = false;
            _streak = 0;
        }

        private void Controls()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                InputGate.ConsumeEscape();
                StepDown();
                return;
            }
            bool f = Time.frameCount != _mountFrame && KeyBindings.WasPressed(GameAction.Interact);
            bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;
            if (_game.Playing)
            {
                if (f || click)
                    TryCatch();
                return;
            }
            if (_game.Current == StarCatchGame.Phase.Done && click)
            {
                Begin();
                AudioService.TryPlay(l => l.GameStart);
                return;
            }
            if (f)
                StepDown();
        }

        private void StepDown()
        {
            _standFrame = Time.frameCount;
            Release();
            _game.Stop();
            ClearFalling();
            AudioService.TryPlay(l => l.RideLeave);
        }

        private void Release()
        {
            if (!_standing)
                return;
            ClearStand();
            if (_player != null)
            {
                _player.RideAt(transform, _standPoint, FirstPersonController.EyeHeight, null, 0f);
                _player.Dismount();
            }
        }

        private void ClearStand()
        {
            _standing = false;
            if (_start != null)
                _start.enabled = true;
        }

        private void Record()
        {
            _recorded = true;
            bool record = _game.Score > 0 && _game.Score > _best;
            if (record)
                _best = _game.Score;
            AudioService.TryPlay(l => record ? l.GameRecord : _game.Score > 0 ? l.GameClear : l.GameFail);
        }

        // ---------------- 떨어지는 것 ----------------

        private void Spawn(StarCatchGame.Kind kind)
        {
            var from = new Vector3(Mathf.Lerp(DeepSpaceComm.CatchSpawnX.x, DeepSpaceComm.CatchSpawnX.y, _game.Next()),
                Mathf.Lerp(DeepSpaceComm.CatchSpawnY.x, DeepSpaceComm.CatchSpawnY.y, _game.Next()),
                Mathf.Lerp(DeepSpaceComm.CatchSpawnZ.x, DeepSpaceComm.CatchSpawnZ.y, _game.Next()));
            float a = _game.Next() * Mathf.PI * 2f, r = Mathf.Sqrt(_game.Next()) * DeepSpaceComm.CatchLandRadius;
            var to = new Vector3(DeepSpaceComm.Plaza.x + Mathf.Cos(a) * r, DeepSpaceComm.CatchLandY, DeepSpaceComm.Plaza.y + Mathf.Sin(a) * r);
            float speed = _game.Speed * (kind == StarCatchGame.Kind.Junk ? 0.85f : kind == StarCatchGame.Kind.SmallStar ? 1.15f : 1f);
            var item = new Falling { Kind = kind, From = from, To = to, Duration = Mathf.Max(0.5f, (to - from).magnitude / speed) };
            item.Spin = new Vector3(_game.Next() - 0.5f, _game.Next() - 0.5f, _game.Next() - 0.5f) * 360f;
            item.Body = kind == StarCatchGame.Kind.Junk ? JunkBody() : StarBody(item.Size);
            item.Body.localPosition = from;
            if (kind != StarCatchGame.Kind.Junk && _streakMaterial != null)
            {
                item.Trail = new ShootingStar(transform, _streakMaterial, transform.TransformPoint(from), transform.TransformPoint(to), speed);
                item.Trail.Line.widthMultiplier = kind == StarCatchGame.Kind.BigStar ? 1f : 0.5f;
            }
            _falling.Add(item);
        }

        private void MoveFalling(float dt)
        {
            var cam = Camera.main;
            for (int i = _falling.Count - 1; i >= 0; i--)
            {
                var item = _falling[i];
                item.T += dt;
                float u = Mathf.Clamp01(item.T / item.Duration);
                item.Body.localPosition = Vector3.Lerp(item.From, item.To, u);
                if (item.Kind == StarCatchGame.Kind.Junk)
                    item.Body.localRotation = Quaternion.Euler(item.Spin * item.T);
                else if (cam != null)   // 별은 늘 보는 쪽을 향함 (납작한 별이 옆으로 서면 가는 막대로 보임) · 천천히 돎
                    item.Body.rotation = Quaternion.LookRotation(cam.transform.position - item.Body.position, Vector3.up) * Quaternion.Euler(0f, 0f, item.Spin.z * item.T * 0.3f);
                if (item.Trail != null)
                {
                    item.Trail.Step(dt);
                    if (Random.value < 0.5f)
                        item.Trail.EmitTrail(EnsureSparks());
                }
                if (u < 1f)
                    continue;
                _game.Land(item.Kind);
                if (item.Kind != StarCatchGame.Kind.Junk)
                    _streak = 0;   // 별이 광장에 닿음 = 놓침
                InteriorFireflyShow.Burst(EnsureSparks(), item.Body.position, item.Kind == StarCatchGame.Kind.Junk ? 4 : 8, 0.4f);
                Remove(i);
            }
        }

        /// <summary>시선과 가장 가까운 것 (크기 + 여유만큼의 각 안, 없으면 −1).</summary>
        private int Aimed()
        {
            var cam = Camera.main;
            if (cam == null)
                return -1;
            int best = -1;
            float bestAngle = float.MaxValue;
            for (int i = 0; i < _falling.Count; i++)
            {
                var to = _falling[i].Body.position - cam.transform.position;
                float dist = to.magnitude;
                float angle = Vector3.Angle(cam.transform.forward, to);
                float limit = Mathf.Atan2(_falling[i].Size + AimPad, Mathf.Max(0.1f, dist)) * Mathf.Rad2Deg + AimSlack;
                if (angle < limit && angle < bestAngle)
                {
                    bestAngle = angle;
                    best = i;
                }
            }
            return best;
        }

        private void TryCatch()
        {
            if (_aim < 0 || _aim >= _falling.Count)
                return;
            var item = _falling[_aim];
            int got = _game.Catch(item.Kind);
            if (item.Kind == StarCatchGame.Kind.Junk)
            {
                InteriorFireflyShow.Burst(EnsureSparks(), item.Body.position, 6, 0.3f);
                Notice("우주 쓰레기  -1", 0.9f);
                _streak = 0;
                AudioService.TryPlay(l => l.GameMiss);
            }
            else
            {
                bool big = item.Kind == StarCatchGame.Kind.BigStar;
                InteriorFireflyShow.Burst(EnsureSparks(), item.Body.position, big ? 30 : 16, big ? 1.1f : 0.7f);
                Notice(big ? "큰 별  +3" : "작은 별  +1", 0.8f);
                AudioService.TryPlayNote(l => l.DscStarCatch, Mathf.Min(++_streak, 8) - 1, big ? 1.3f : 1f);   // 연달아 잡을수록 음계로 (끝 칸에서 멈춤), 큰 별은 크게
            }
            Remove(_aim);
            _aim = -1;
        }

        private void ApplyAim()
        {
            for (int i = 0; i < _falling.Count; i++)
                _falling[i].Body.localScale = Vector3.one * (i == _aim ? AimGrow : 1f);
        }

        private void Remove(int i)
        {
            var item = _falling[i];
            item.Trail?.Destroy();
            if (item.Body != null)
                Destroy(item.Body.gameObject);
            _falling.RemoveAt(i);
        }

        private void ClearFalling()
        {
            for (int i = _falling.Count - 1; i >= 0; i--)
                Remove(i);
        }

        // ---------------- 모양 ----------------

        /// <summary>납작한 다섯 꼭지 별 (양면 — 앞뒤 판 + 옆, 반지름 1 · 두께 0.25, 가운데가 살짝 솟음).</summary>
        private static Mesh StarMesh()
        {
            if (_starMesh != null)
                return _starMesh;
            var v = new List<Vector3>();
            var tris = new List<int>();
            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? -0.12f : 0.12f;
                int c = v.Count;
                v.Add(new Vector3(0f, 0f, z * 2.2f));
                for (int i = 0; i < 10; i++)
                {
                    float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                    float r = i % 2 == 0 ? 1f : 0.45f;
                    v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z));
                }
                for (int i = 0; i < 10; i++)
                {
                    int a = c + 1 + i, b = c + 1 + (i + 1) % 10;
                    if (side == 0)
                        tris.AddRange(new[] { c, b, a });
                    else
                        tris.AddRange(new[] { c, a, b });
                }
            }
            for (int i = 0; i < 10; i++)
            {
                int a0 = 1 + i, a1 = 1 + (i + 1) % 10, b0 = 12 + i, b1 = 12 + (i + 1) % 10;
                tris.AddRange(new[] { a0, a1, b1, a0, b1, b0 });
            }
            _starMesh = new Mesh { name = "CatchStar" };
            _starMesh.SetVertices(v);
            _starMesh.SetTriangles(tris, 0);
            _starMesh.RecalculateNormals();
            _starMesh.RecalculateBounds();
            return _starMesh;
        }

        private Transform StarBody(float size)
        {
            var go = new GameObject("CatchStar");
            go.transform.SetParent(transform, false);
            var body = new GameObject("Shape");
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = Vector3.one * size;
            body.AddComponent<MeshFilter>().sharedMesh = StarMesh();
            var r = body.AddComponent<MeshRenderer>();
            r.sharedMaterial = _starMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>우주 쓰레기: 구부러진 판 둘 + 볼트 (짙은 몸체, 꼬리 없음 — 별과 모양 · 움직임으로 구분).</summary>
        private Transform JunkBody()
        {
            var go = new GameObject("CatchJunk");
            go.transform.SetParent(transform, false);
            void Part(PrimitiveType type, Vector3 at, Vector3 scale, Vector3 euler)
            {
                var p = GameObject.CreatePrimitive(type);
                Destroy(p.GetComponent<Collider>());
                p.transform.SetParent(go.transform, false);
                p.transform.localPosition = at;
                p.transform.localScale = scale;
                p.transform.localRotation = Quaternion.Euler(euler);
                var r = p.GetComponent<MeshRenderer>();
                r.sharedMaterial = _junkMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Part(PrimitiveType.Cube, new Vector3(-0.05f, 0f, 0f), new Vector3(0.16f, 0.035f, 0.17f), Vector3.zero);
            Part(PrimitiveType.Cube, new Vector3(0.08f, 0.035f, 0f), new Vector3(0.12f, 0.035f, 0.15f), new Vector3(0f, 0f, 35f));
            Part(PrimitiveType.Cylinder, new Vector3(-0.07f, 0.03f, 0.04f), new Vector3(0.035f, 0.03f, 0.035f), Vector3.zero);
            return go.transform;
        }

        private ParticleSystem EnsureSparks()
        {
            if (_sparks == null)
                _sparks = ShootingStar.CreateSparks(transform, _sparkMaterial);
            return _sparks;
        }

        // ---------------- 안내 ----------------

        private void Notice(string text, float seconds)
        {
            _notice = text;
            _noticeTimer = seconds;
        }

        private void UpdateHint()
        {
            if (!_standing)
            {
                _hint.Show(null);
                return;
            }
            string f = KeyBindings.Label(GameAction.Interact);
            const string legend = "<size=80%>큰 별 +3 · 작은 별 +1 · 각진 우주 쓰레기 -1 (꼬리 없음)</size>";
            string tally = $"점수 {_game.Score}  (큰 별 {_game.BigCaught} · 작은 별 {_game.SmallCaught} · 쓰레기 {_game.JunkCaught})";
            switch (_game.Current)
            {
                case StarCatchGame.Phase.Countdown:
                    _hint.Show($"별똥별이 떨어져요. 바라보고 {f}  ·  {Mathf.CeilToInt(StarCatchGame.CountdownTime - _game.PhaseTime)}\n{legend}");
                    break;
                case StarCatchGame.Phase.Play:
                    string head = _noticeTimer > 0f ? _notice + "  ·  " : "";
                    _hint.Show($"{head}남은 시간 {Mathf.CeilToInt(_game.Remaining)}초  ·  {tally}\n{legend}");
                    break;
                case StarCatchGame.Phase.Done:
                    bool record = _game.Score > 0 && _game.Score >= _best;
                    _hint.Show($"끝!  {tally}  ·  놓친 별 {_game.Missed}  ·  " + (record ? "최고 기록!" : $"최고 {Mathf.Max(_best, 0)}점") +
                               $"\n<size=80%>클릭 = 다시 · {f} · ESC = 내려오기</size>");
                    break;
                default:
                    _hint.Show(null);
                    break;
            }
        }
    }
}
