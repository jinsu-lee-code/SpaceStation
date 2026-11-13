using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 궤도 방어망 미니게임 "별똥별 막기" (사용자 2026-11-13 "포대에 앉아 조준 · 발사", 판 = <see cref="StarfallGame"/>, 값 = <see cref="OrbitalDefense"/>):
    /// 가운데 수정 부표 포대의 포수 의자를 보고 F = 앉음 → 3 · 2 · 1 → 60초 동안 하늘 면에서 바다로 떨어지는 별똥별 (<see cref="ShootingStar"/>)을
    /// 마우스로 조준해 클릭 = 포 끝 수정에서 프리즘 광선 (바깥 포대와 같은 <see cref="PrismBeamFx"/>) → 맞으면 무지개 수정 불꽃, 바다에 떨어지면 놓침.
    /// 포대 수정 무리는 조준하는 쪽으로 기움. 끝나면 맞힌 수 · 놓친 수 · 최고 기록 (글자), 클릭 = 다시, F · ESC = 내림. 실제 시간.
    /// 조준 표시 = 흰 십자 (별똥별이 겨눠지면 커지고 밝아짐 — 색 대신 크기 · 밝기, 색약).
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 앉은 채 누른 F · ESC를 여기서 먼저 씀
    public sealed class InteriorStarfall : MonoBehaviour
    {
        /// <summary>조준 너그러움: 시선과 별똥별 머리 사이 거리 (m) = 기본 + 거리마다.</summary>
        public const float AimRadius = 0.45f, AimPerMeter = 0.035f;
        private const float AimReach = 30f, TurretLean = 0.3f;
        private static readonly Color[] Rainbow =
        {
            new Color(1f, 0.3f, 0.3f), new Color(1f, 0.62f, 0.2f), new Color(1f, 0.95f, 0.3f), new Color(0.35f, 1f, 0.45f),
            new Color(0.3f, 0.65f, 1f), new Color(0.45f, 0.38f, 1f), new Color(0.85f, 0.45f, 1f),
        };

        [Tooltip("포대 수정 무리 (원점 = 받침 윗면) — 조준하는 쪽으로 기움")]
        [SerializeField] private Transform _turret;
        [Tooltip("포수 의자 충돌체 (보고 F = 앉음)")]
        [SerializeField] private Collider _seat;
        [Tooltip("프리즘 광선 (실제 시간으로 Begin · Apply · End)")]
        [SerializeField] private PrismBeamFx _beam;
        [SerializeField] private Material _streakMaterial;
        [SerializeField] private Material _sparkMaterial;
        [SerializeField] private TMP_FontAsset _font;

        private sealed class Beam
        {
            public PrismBeamFx.Shot Shot;
            public float T;
        }

        private static int _best = -1;

        private readonly StarfallGame _game = new StarfallGame();
        private readonly List<ShootingStar> _stars = new List<ShootingStar>();
        private readonly List<Beam> _beams = new List<Beam>();
        private readonly List<Vector3> _heads = new List<Vector3>();
        private ParticleSystem _trail;
        private ParticleSystem _bursts;
        private Quaternion _turretRest;
        private Vector3 _tipLocal;
        private FirstPersonController _player;
        private bool _seated;
        private int _sitFrame = -1, _standFrame = -1;
        private Vector3 _standPoint;
        private bool _recorded;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private RectTransform _cross;
        private string _hintShown;

        public StarfallGame Game => _game;
        public bool Seated => _seated;
        public static int Best => _best;

        public void Configure(Transform turret, Collider seat, PrismBeamFx beam, Material streak, Material spark, TMP_FontAsset font)
        {
            _turret = turret;
            _seat = seat;
            _beam = beam;
            _streakMaterial = streak;
            _sparkMaterial = spark;
            _font = font;
        }

        private void Awake()
        {
            if (_turret != null)
            {
                _turretRest = _turret.localRotation;
                _tipLocal = _turret.InverseTransformPoint(transform.TransformPoint(OrbitalDefense.TurretMuzzle));
            }
            if (_seat != null)
            {
                var use = _seat.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "별똥별 막기" + (_best < 0 ? "" : $"  <size=80%>(최고 기록 {_best}개)</size>");
                use.Completed = Sit;
            }
        }

        private void OnDisable()
        {
            // 방을 나가면 바로 내림 (판은 접음)
            ReleasePlayer();
            _game.Stop();
            ClearFlying();
            if (_turret != null)
                _turret.localRotation = _turretRest;
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
            if (_seated && _player != null && !_player.Riding)
                ClearSeat();   // 앉은 채 패드로 빠른 이동 (Teleport가 탈것을 지움)
            if (_seated)
                Controls();
            if (_game.Step(dt))
                Launch();
            if (_game.Current == StarfallGame.Phase.Done && !_recorded)
                Record();
            StepStars(dt);
            StepBeams(dt);
            AimTurret(dt);
            UpdateHint();
        }

        // ---------------- 앉기 ----------------

        private void Sit()
        {
            if (_seated || _player == null || Time.frameCount == _standFrame)
                return; // 내리기와 같은 프레임의 F (InteriorMode가 뒤에서 다시 부름)
            _standPoint = _player.transform.position;
            _player.RideAt(transform, transform.TransformPoint(OrbitalDefense.GunSeat), OrbitalDefense.GunEye,
                OrbitalDefense.GunSeatYaw + transform.eulerAngles.y, OrbitalDefense.GunPitch);
            _seated = true;
            _sitFrame = Time.frameCount;
            if (_seat != null)
                _seat.enabled = false;
            StartRound();
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void Controls()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                InputGate.ConsumeEscape();   // 내부 나가기보다 먼저
                StandUp();
                return;
            }
            if (Time.frameCount != _sitFrame && KeyBindings.WasPressed(GameAction.Interact))
            {
                StandUp();
                return;
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                Fire();
        }

        private void StandUp()
        {
            _standFrame = Time.frameCount;
            ReleasePlayer();
            _game.Stop();
            ClearFlying();
            AudioService.TryPlay(l => l.UiClose);
        }

        private void ReleasePlayer()
        {
            if (!_seated)
                return;
            ClearSeat();
            if (_player != null)
            {
                _player.RideAt(transform, _standPoint, FirstPersonController.EyeHeight, null, 0f);
                _player.Dismount();
            }
        }

        private void ClearSeat()
        {
            _seated = false;
            if (_seat != null)
                _seat.enabled = true;
        }

        private void StartRound()
        {
            ClearFlying();
            _game.Begin();
            _recorded = false;
        }

        private void Record()
        {
            _recorded = true;
            bool record = _game.Hits > 0 && _game.Hits > _best;
            if (record)
                _best = _game.Hits;
            AudioService.TryPlay(l => record ? l.GradeUp : l.UiTab);
        }

        // ---------------- 쏘기 ----------------

        private Ray AimRay()
        {
            var cam = Camera.main;
            return cam != null ? new Ray(cam.transform.position, cam.transform.forward) : new Ray(transform.position, transform.forward);
        }

        private void Fire()
        {
            if (_game.Current == StarfallGame.Phase.Done)
            {
                StartRound();   // 끝난 뒤 클릭 = 새 판
                AudioService.TryPlay(l => l.UiOpen);
                return;
            }
            if (!_game.TryFire())
                return;
            var ray = AimRay();
            var muzzle = _turret != null ? _turret.TransformPoint(_tipLocal) : transform.TransformPoint(OrbitalDefense.TurretMuzzle);
            int pick = Pick(ray);
            Vector3 to;
            if (pick >= 0)
            {
                var star = _stars[pick];
                to = star.Head;
                star.Destroy();
                _stars.RemoveAt(pick);
                _game.Hit();
                Burst(to);
                AudioService.TryPlay(l => l.MeteorExplode);
            }
            else
            {
                to = Physics.Raycast(ray, out var hit, AimReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.point : ray.GetPoint(AimReach * 0.5f);
                AudioService.TryPlay(l => l.UiTab);
            }
            if (_beam != null)
                _beams.Add(new Beam { Shot = _beam.Begin(muzzle, to) });
        }

        /// <summary>시선이 겨눈 별똥별 (없으면 −1).</summary>
        private int Pick(Ray ray)
        {
            _heads.Clear();
            foreach (var s in _stars)
                _heads.Add(s.Head);
            return PickStar(ray.origin, ray.direction, _heads, AimRadius, AimPerMeter);
        }

        /// <summary>시선 (origin, dir)에 가장 가까이 겨눠진 머리: 시선과의 거리 &lt; 기본 + 거리 × perMeter, 앞에 있는 것 중 가장 가까운 것. 없으면 −1.</summary>
        public static int PickStar(Vector3 origin, Vector3 dir, IReadOnlyList<Vector3> heads, float radius, float perMeter)
        {
            dir = dir.normalized;
            int best = -1;
            float bestT = float.MaxValue;
            for (int i = 0; i < heads.Count; i++)
            {
                var v = heads[i] - origin;
                float t = Vector3.Dot(v, dir);
                if (t <= 0f)
                    continue;
                float off = (v - dir * t).magnitude;
                if (off < radius + perMeter * t && t < bestT)
                {
                    bestT = t;
                    best = i;
                }
            }
            return best;
        }

        // ---------------- 별똥별 ----------------

        private void Launch()
        {
            for (int tries = 0; tries < 8; tries++)
            {
                var to = OrbitalDefense.StarfallTarget(Random.Range(-OrbitalDefense.StarfallSpread, OrbitalDefense.StarfallSpread),
                    Random.Range(OrbitalDefense.StarfallNear, OrbitalDefense.StarfallFar));
                var from = OrbitalDefense.MeteorStart(to, OrbitalDefense.StarfallIncoming(to, Random.Range(OrbitalDefense.StarfallOutMin, OrbitalDefense.StarfallOutMax), Random.Range(-1f, 1f)), 9f);
                if ((from - to).magnitude < 3f || !OrbitalDefense.StarfallVisible(from))
                    continue;   // 짧거나 포수 등 뒤 · 머리 바로 위에서 오면 다시 고름
                _stars.Add(new ShootingStar(transform, _streakMaterial, transform.TransformPoint(from), transform.TransformPoint(to), _game.Speed));
                AudioService.TryPlay(l => l.MeteorIncoming);
                return;
            }
        }

        private void StepStars(float dt)
        {
            for (int i = _stars.Count - 1; i >= 0; i--)
            {
                var s = _stars[i];
                s.Step(dt);
                if (!s.Arrived)
                {
                    if (_trail == null && _sparkMaterial != null)
                        _trail = ShootingStar.CreateSparks(transform, _sparkMaterial);
                    s.EmitTrail(_trail);
                    continue;
                }
                if (_game.Playing)
                    _game.Miss();
                Splash(s.To);
                s.Destroy();
                _stars.RemoveAt(i);
            }
        }

        private void StepBeams(float dt)
        {
            if (_beam == null)
                return;
            for (int i = _beams.Count - 1; i >= 0; i--)
            {
                var b = _beams[i];
                b.T += dt;
                if (b.T < _beam.Duration && b.Shot.Go != null)
                {
                    _beam.Apply(b.Shot, b.T);
                    continue;
                }
                PrismBeamFx.End(b.Shot);
                _beams.RemoveAt(i);
            }
        }

        private void ClearFlying()
        {
            foreach (var s in _stars)
                s.Destroy();
            _stars.Clear();
            foreach (var b in _beams)
                PrismBeamFx.End(b.Shot);
            _beams.Clear();
        }

        /// <summary>앉은 동안 포대 수정 무리가 조준하는 쪽으로 기움 (내리면 바로 섬).</summary>
        private void AimTurret(float dt)
        {
            if (_turret == null)
                return;
            var target = _turretRest;
            if (_seated)
            {
                var ray = AimRay();
                var aim = _turret.parent != null ? _turret.parent.InverseTransformDirection(ray.direction) : ray.direction;
                target = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, aim, TurretLean)) * _turretRest;
            }
            _turret.localRotation = Quaternion.Slerp(_turret.localRotation, target, 1f - Mathf.Exp(-dt * 8f));
        }

        // ---------------- 불꽃 ----------------

        private ParticleSystem Bursts()
        {
            if (_bursts != null || _sparkMaterial == null)
                return _bursts;
            _bursts = ShootingStar.CreateSparks(transform, _sparkMaterial);
            _bursts.name = "StarfallBursts";
            var limit = _bursts.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = 2.2f;
            return _bursts;
        }

        /// <summary>맞힌 자리: 흰 번쩍 + 무지개 수정 불꽃이 둥글게 흩어짐.</summary>
        private void Burst(Vector3 at)
        {
            var ps = Bursts();
            if (ps == null)
                return;
            ps.Emit(new ParticleSystem.EmitParams { position = at, startLifetime = 0.22f, startSize = 1.3f, startColor = Color.white, applyShapeToPosition = false }, 1);
            for (int i = 0; i < 46; i++)
            {
                ps.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = Random.onUnitSphere * Random.Range(1.6f, 3.4f),
                    startLifetime = Random.Range(0.6f, 1.2f),
                    startSize = Random.Range(0.07f, 0.15f),
                    startColor = Color.Lerp(Rainbow[Random.Range(0, Rainbow.Length)], Color.white, 0.2f),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        /// <summary>놓친 별똥별이 바다에 떨어진 자리: 흰 물보라 빛 (위로 튀었다 잦아듦).</summary>
        private void Splash(Vector3 at)
        {
            var ps = Bursts();
            if (ps == null)
                return;
            for (int i = 0; i < 16; i++)
            {
                var v = Random.insideUnitCircle * 0.8f;
                ps.Emit(new ParticleSystem.EmitParams
                {
                    position = at + Vector3.up * 0.05f,
                    velocity = new Vector3(v.x, Random.Range(1.2f, 2.2f), v.y),
                    startLifetime = Random.Range(0.4f, 0.8f),
                    startSize = Random.Range(0.05f, 0.1f),
                    startColor = Color.Lerp(new Color(0.75f, 0.9f, 1f), Color.white, Random.value),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        // ---------------- 안내 ----------------

        private void UpdateHint()
        {
            if (!_seated)
            {
                ShowHint(null);
                return;
            }
            string text;
            string keys = $"{KeyBindings.Label(GameAction.Interact)} / ESC 내리기";
            switch (_game.Current)
            {
                case StarfallGame.Phase.Countdown:
                    text = $"별똥별 막기  ·  {Mathf.CeilToInt(StarfallGame.CountdownTime - _game.PhaseTime)}\n<size=80%>마우스 조준 · 클릭 쏘기 · {keys}</size>";
                    break;
                case StarfallGame.Phase.Play:
                    text = $"맞힌 별똥별 {_game.Hits}  ·  남은 시간 {Mathf.CeilToInt(_game.Remaining)}초\n<size=80%>마우스 조준 · 클릭 쏘기 · {keys}</size>";
                    break;
                case StarfallGame.Phase.Done:
                    bool record = _game.Hits > 0 && _game.Hits >= _best;
                    text = $"별똥별 {_game.Hits}개를 맞혔어요 (놓친 {_game.Misses}개)  ·  " + (record ? "최고 기록!" : $"최고 {Mathf.Max(_best, 0)}개")
                         + $"\n<size=80%>클릭 다시 하기 · {keys}</size>";
                    break;
                default:
                    text = $"<size=80%>클릭 쏘기 · {keys}</size>";
                    break;
            }
            ShowHint(text);
            if (_cross != null)
            {
                bool aimed = _game.Playing && Pick(AimRay()) >= 0;
                _cross.localScale = Vector3.one * (aimed ? 1.45f : 1f);
                foreach (var g in _cross.GetComponentsInChildren<Image>())
                    g.color = new Color(1f, 1f, 1f, aimed ? 1f : 0.7f);
            }
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
            var go = new GameObject("StarfallHint", typeof(RectTransform));
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
            Place(back.rectTransform, new Vector2(1000f, 112f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(970f, 104f));
            if (_font != null)
                _hint.font = _font;
            _hint.fontSize = 26f;
            _hint.enableAutoSizing = true;
            _hint.fontSizeMin = 15f;
            _hint.fontSizeMax = 26f;
            _hint.color = new Color(0.92f, 0.96f, 1f);
            _hint.alignment = TextAlignmentOptions.Center;
            _hint.raycastTarget = false;
            // 조준 십자 (가운데 비운 네 막대)
            _cross = new GameObject("Cross", typeof(RectTransform)).GetComponent<RectTransform>();
            _cross.SetParent(go.transform, false);
            _cross.anchorMin = _cross.anchorMax = new Vector2(0.5f, 0.5f);
            _cross.sizeDelta = Vector2.zero;
            foreach (var (pos, size) in new[] { (new Vector2(0f, 15f), new Vector2(3f, 14f)), (new Vector2(0f, -15f), new Vector2(3f, 14f)), (new Vector2(15f, 0f), new Vector2(14f, 3f)), (new Vector2(-15f, 0f), new Vector2(14f, 3f)) })
            {
                var bar = new GameObject("Bar", typeof(RectTransform)).AddComponent<Image>();
                bar.transform.SetParent(_cross, false);
                bar.rectTransform.anchoredPosition = pos;
                bar.rectTransform.sizeDelta = size;
                bar.color = new Color(1f, 1f, 1f, 0.7f);
                bar.raycastTarget = false;
            }
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
