using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 궤도 방어망 체험 "등대 빛 돌리기" (사용자 2026-11-13 "비추면 반응", 규칙 = <see cref="LighthouseBeam"/>, 값 = <see cref="OrbitalDefense"/>):
    /// 등실 렌즈 금빛 손잡이를 보고 F = 잡음 → 손잡이 옆에 서서 빛줄기를 따라 바다를 내려다봄, A · D = 렌즈 돌리기 (손을 떼면 멈춤), F · ESC = 놓기.
    /// 렌즈 = 앞뒤 빛 두 줄기 (LightBeamFx, 거의 수평, 렌즈 자식이라 같이 돎), 아무도 안 잡으면 천천히 돎.
    /// 빛이 닿은 곳이 답함: 부표 · 포대 수정 = 환하게 + 반짝 (부표는 출렁), 매인 배 = 흔들림, 등불대 · 하늘 다리 꽃 = 깜빡 밝아짐.
    /// 잡은 동안 모든 곳이 다 답하면 걸린 시간 · 최고 기록 (글자). 시간 = 실제 시간. 반응 밝기 = 밝기 · 글 (색 대신, 색약).
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 잡은 채 누른 F · ESC를 여기서 먼저 씀
    public sealed class InteriorLighthouse : MonoBehaviour
    {
        public enum Answer { Glow, Bob, Sway, Flash }

        public const float RiderSide = -40f;          // 손잡이 왼쪽에 섬 (yaw, 도) — 빛줄기가 시야 가운데 오른쪽 아래로 뻗음
        public const float RiderRadius = 0.72f;       // 렌즈 가운데 → 발 (렌즈 받침 0.42 + 몸 0.24 바깥) — 0.95는 등실 유리 살이 눈앞 0.4 m에서 빛줄기를 가렸음
        private const float RiderPitch = 20f;         // 처음 시선 (아래 +) — 빛줄기 (10° 아래)와 바다 쪽 곳 (17 ~ 45° 아래) 사이
        private const float GlowSeconds = 2.2f, BobHeight = 0.12f, SwayAngle = 6f, FlashSeconds = 1.8f;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("렌즈 (도는 조각, 원점 = 렌즈 가운데) — 빛줄기 · 스포트라이트 · 손잡이 충돌체는 자식")]
        [SerializeField] private Transform _lens;
        [Tooltip("손잡이 끝 구슬 (보고 F = 잡기)")]
        [SerializeField] private Collider _handle;
        [Tooltip("답하는 곳 (OrbitalDefense.BeamTargets 순서) — Glow · Bob · Sway = 움직이는 조각, Flash = 비움")]
        [SerializeField] private Transform[] _targets;
        [SerializeField] private Answer[] _answers;
        [SerializeField] private Material _sparkMaterial;
        [SerializeField] private TMP_FontAsset _font;

        private static float _bestSeconds = -1f;

        private LighthouseBeam _beam;
        private SpaceStation.Building.LightBeamFx _shafts;
        private readonly float[] _reach = { -1f, -1f };
        private readonly List<int> _answered = new List<int>();
        private readonly List<int> _first = new List<int>();
        private Quaternion _lensRest;
        private Transform _rider;
        private FirstPersonController _player;
        private bool _held;
        private int _releaseFrame = -1;
        private Vector3 _getOff;
        private float _getOffYaw;
        private float _holdTime;
        private bool _done;
        private float[] _t;
        private Vector3[] _restPos;
        private Quaternion[] _restRot;
        private Renderer[][] _renderers;
        private Light[] _flashes;
        private MaterialPropertyBlock _block;
        private string _notice;
        private float _noticeTimer;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public bool Held => _held;
        public LighthouseBeam Beam => _beam;
        public static float BestSeconds => _bestSeconds;

        public void Configure(Transform lens, Collider handle, Transform[] targets, Answer[] answers, Material spark, TMP_FontAsset font)
        {
            _lens = lens;
            _handle = handle;
            _targets = targets;
            _answers = answers;
            _sparkMaterial = spark;
            _font = font;
        }

        private void Awake()
        {
            var spots = OrbitalDefense.BeamTargets;
            int n = spots.Length;
            var yaws = new float[n];
            var widths = new float[n];
            for (int i = 0; i < n; i++)
            {
                yaws[i] = OrbitalDefense.BeamTargetYaw(spots[i].At);
                widths[i] = OrbitalDefense.BeamTargetWidth(spots[i].At);
            }
            _beam = new LighthouseBeam(OrbitalDefense.LensHandleYaw, yaws, widths);
            _block = new MaterialPropertyBlock();
            _t = new float[n];
            _restPos = new Vector3[n];
            _restRot = new Quaternion[n];
            _renderers = new Renderer[n][];
            _flashes = new Light[n];
            for (int i = 0; i < n; i++)
            {
                _t[i] = 99f;
                var target = _targets != null && i < _targets.Length ? _targets[i] : null;
                var answer = AnswerOf(i);
                if (target != null)
                {
                    _restPos[i] = target.localPosition;
                    _restRot[i] = target.localRotation;
                    _renderers[i] = target.GetComponentsInChildren<Renderer>();
                }
                if (answer == Answer.Flash)
                {
                    var go = new GameObject("BeamAnswerLight");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = spots[i].At;
                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.range = 4.5f;
                    light.color = new Color(1f, 0.86f, 0.6f);
                    light.intensity = 0f;
                    light.shadows = LightShadows.None;
                    light.enabled = false;
                    _flashes[i] = light;
                }
            }
            if (_lens != null)
            {
                _lensRest = _lens.localRotation;
                _shafts = _lens.GetComponentInChildren<SpaceStation.Building.LightBeamFx>(true);
                // 손잡이 옆 발 자리 (렌즈 자식 — 렌즈와 같이 돎), 앞 = 손잡이 · 앞 빛줄기 쪽
                _rider = new GameObject("LighthouseRider").transform;
                _rider.SetParent(transform, false);
                var c = OrbitalDefense.LensCenter;
                _rider.localPosition = new Vector3(c.x, OrbitalDefense.GalleryY, c.z) + Quaternion.Euler(0f, OrbitalDefense.LensHandleYaw + RiderSide, 0f) * Vector3.forward * RiderRadius;
                _rider.localRotation = Quaternion.Euler(0f, OrbitalDefense.LensHandleYaw, 0f);
                _rider.SetParent(_lens, true);
            }
            if (_handle != null)
            {
                var use = _handle.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "등대 빛 돌리기" + (_bestSeconds < 0f ? "" : $"  <size=80%>(최고 기록 {Clock(_bestSeconds)})</size>");
                use.Completed = Grab;
            }
        }

        private Answer AnswerOf(int i) => _answers != null && i < _answers.Length ? _answers[i] : Answer.Glow;

        private void OnDisable()
        {
            // 방을 나가면 바로 놓음 (렌즈 각 · 반응은 그대로 두고 다음에 이어서)
            ReleasePlayer();
            if (_beam != null)
                _beam.Release();
            for (int i = 0; _t != null && i < _t.Length; i++)
                _t[i] = 99f;
            ApplyAnswers();
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        private void Update()
        {
            if (_lens == null || _beam == null)
                return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_noticeTimer > 0f)
                _noticeTimer -= dt;
            if (_held)
                Steer(dt);
            _answered.Clear();
            _first.Clear();
            _beam.Step(dt, _answered, _first);
            _lens.localRotation = Quaternion.Euler(0f, _beam.Yaw - OrbitalDefense.LensHandleYaw, 0f) * _lensRest;
            FitShafts();
            foreach (int i in _answered)
                Answered(i, _first.Contains(i));
            if (_held && !_done && _beam.AllFound)
                Finish();
            for (int i = 0; i < _t.Length; i++)
                _t[i] += dt;
            ApplyAnswers();
            UpdateHint();
        }

        /// <summary>
        /// 빛줄기 끝 = 지금 방향의 <see cref="OrbitalDefense.BeamReach"/> (벽 쪽이면 하늘 면 앞에서 스러짐).
        /// 빛줄기 좌표 = 쉴 때 렌즈 기준 (렌즈와 같이 돎)이라 방향은 그대로, 길이만 바꿈.
        /// </summary>
        private void FitShafts()
        {
            if (_shafts == null)
                return;
            for (int k = 0; k < 2; k++)
            {
                float reach = OrbitalDefense.BeamReach(_beam.Yaw + 180f * k);
                if (Mathf.Abs(reach - _reach[k]) < 0.01f)
                    continue;
                var dir = Quaternion.Euler(OrbitalDefense.BeamPitch, OrbitalDefense.LensHandleYaw + 180f * k, 0f) * Vector3.forward;
                if (_shafts.SetEnd(k, OrbitalDefense.LensCenter + dir * reach))
                    _reach[k] = reach;
            }
        }

        private void Steer(float dt)
        {
            _holdTime += dt;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                InputGate.ConsumeEscape();   // 내부 나가기보다 먼저
                Release();
                return;
            }
            if (KeyBindings.WasPressed(GameAction.Interact))
            {
                Release();
                return;
            }
            _beam.Steer = (KeyBindings.IsPressed(GameAction.CameraRight) ? 1f : 0f) - (KeyBindings.IsPressed(GameAction.CameraLeft) ? 1f : 0f);
        }

        private void Answered(int i, bool first)
        {
            _t[i] = 0f;
            var at = OrbitalDefense.BeamTargets[i].At;
            Sparkle(transform.TransformPoint(at), 18);
            if (!_held)
                return;
            AudioService.TryPlay(l => first ? l.UiOpen : l.UiTab);
            if (first && !_beam.AllFound)
                Notice($"{OrbitalDefense.BeamTargets[i].Name}{Josa(OrbitalDefense.BeamTargets[i].Name)} 빛으로 답해요  {_beam.FoundCount} / {_beam.TargetCount}", 2.4f);
        }

        /// <summary>받침 이/가 (받침 있으면 "이").</summary>
        private static string Josa(string word)
        {
            if (string.IsNullOrEmpty(word))
                return "가";
            char c = word[word.Length - 1];
            return c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 != 0 ? "이" : "가";
        }

        private void Finish()
        {
            _done = true;
            bool record = _bestSeconds < 0f || _holdTime < _bestSeconds;
            if (record)
                _bestSeconds = _holdTime;
            Notice($"등대 빛이 닿는 {_beam.TargetCount}곳이 모두 답했어요!  {Clock(_holdTime)}" + (record ? "  ·  최고 기록!" : $"  ·  최고 {Clock(_bestSeconds)}"), 5.5f);
            for (int i = 0; i < _t.Length; i++)
            {
                _t[i] = 0f;   // 다 같이 한 번 더 답함
                Sparkle(transform.TransformPoint(OrbitalDefense.BeamTargets[i].At), 24);
            }
            AudioService.TryPlay(l => l.GradeUp);
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return $"{s / 60}:{s % 60:00}";
        }

        // ---------------- 플레이어 ----------------

        private void Grab()
        {
            if (_held || _player == null || _rider == null || Time.frameCount == _releaseFrame)
                return; // 놓기와 같은 프레임의 F (InteriorMode가 뒤에서 다시 부름)
            _getOff = transform.InverseTransformPoint(_player.transform.position);
            _getOffYaw = _player.transform.eulerAngles.y - transform.eulerAngles.y;
            _beam.Begin();
            _holdTime = 0f;
            _done = false;
            _player.RideAt(_rider, _rider.position, FirstPersonController.EyeHeight, _rider.eulerAngles.y, RiderPitch);
            _held = true;
            if (_handle != null)
                _handle.enabled = false;
            Notice($"{KeyBindings.Label(GameAction.CameraLeft)} · {KeyBindings.Label(GameAction.CameraRight)}로 렌즈를 돌려 밤바다를 비춰요 · 빛을 잠깐 머물면 그곳이 답해요", 5f);
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void Release()
        {
            _releaseFrame = Time.frameCount;
            _beam.Release();
            ReleasePlayer();
            AudioService.TryPlay(l => l.UiClose);
        }

        private void ReleasePlayer()
        {
            if (!_held)
                return;
            _held = false;
            if (_handle != null)
                _handle.enabled = true;
            if (_player == null)
                return;
            _player.RideAt(transform, transform.TransformPoint(_getOff), FirstPersonController.EyeHeight, transform.eulerAngles.y + _getOffYaw);
            _player.Dismount();
        }

        // ---------------- 답 ----------------

        private void ApplyAnswers()
        {
            for (int i = 0; i < _t.Length; i++)
            {
                float t = _t[i];
                var target = _targets != null && i < _targets.Length ? _targets[i] : null;
                switch (AnswerOf(i))
                {
                    case Answer.Glow:
                        Glow(i, t);
                        break;
                    case Answer.Bob:
                        Glow(i, t);
                        if (target != null)
                            target.localPosition = _restPos[i] + Vector3.up * (t < 4f ? BobHeight * Mathf.Exp(-1.3f * t) * Mathf.Sin(2f * Mathf.PI * 0.8f * t) : 0f);
                        break;
                    case Answer.Sway:
                        if (target != null)
                            target.localRotation = Quaternion.AngleAxis(t < 5f ? SwayAngle * Mathf.Exp(-1f * t) * Mathf.Sin(2f * Mathf.PI * 0.6f * t) : 0f, Vector3.forward) * _restRot[i];
                        break;
                    case Answer.Flash:
                        var light = _flashes[i];
                        if (light != null)
                        {
                            float env = t < FlashSeconds ? 1f - t / FlashSeconds : 0f;
                            light.intensity = 5f * env * (0.65f + 0.35f * Mathf.Cos(t * 16f));   // 두어 번 깜박이며 잦아듦
                            light.enabled = env > 0f;
                        }
                        break;
                }
            }
        }

        /// <summary>수정 · 빛 구슬: 빛이 닿으면 환하게 (밝기 — 색 대신) 잦아듦.</summary>
        private void Glow(int i, float t)
        {
            var renderers = _renderers[i];
            if (renderers == null)
                return;
            float glow = t < GlowSeconds ? 1f - t / GlowSeconds : 0f;
            foreach (var r in renderers)
            {
                if (r == null)
                    continue;
                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null)
                        continue;
                    if (glow <= 0f)
                    {
                        r.SetPropertyBlock(null, m);
                        continue;
                    }
                    r.GetPropertyBlock(_block, m);
                    if (mats[m].HasProperty(EmissionId))
                        _block.SetColor(EmissionId, mats[m].GetColor(EmissionId) + new Color(1.1f, 1.05f, 0.9f) * (1.8f * glow * glow));
                    else if (mats[m].HasProperty(BaseColorId))
                        _block.SetColor(BaseColorId, Color.Lerp(mats[m].GetColor(BaseColorId), Color.white, 0.6f * glow));
                    r.SetPropertyBlock(_block, m);
                }
            }
        }

        /// <summary>답한 곳 둘레 반짝이 (옅은 흰빛 점이 천천히 떠올라 사라짐 — 가산 재질).</summary>
        private void Sparkle(Vector3 at, int count)
        {
            if (_sparkMaterial == null)
                return;
            var go = new GameObject("BeamSparkle");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.97f, 0.9f), new Color(1f, 0.9f, 0.7f));
            main.gravityModifier = -0.04f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count, (short)(count + 8)) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _sparkMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
        }

        // ---------------- 안내 ----------------

        private void Notice(string text, float seconds)
        {
            _notice = text;
            _noticeTimer = seconds;
        }

        private void UpdateHint()
        {
            string text = null;
            if (_held)
            {
                string aim = "";
                for (int i = 0; i < _beam.TargetCount; i++)
                {
                    if (_beam.Lit(i) && !_beam.Found(i))
                        aim = $"  ·  {OrbitalDefense.BeamTargets[i].Name} 비추는 중 {Mathf.RoundToInt(_beam.Charge(i) * 100f)}%";
                }
                text = $"빛이 닿은 곳 {_beam.FoundCount} / {_beam.TargetCount}{aim}\n"
                     + $"<size=80%>{KeyBindings.Label(GameAction.CameraLeft)} · {KeyBindings.Label(GameAction.CameraRight)} 렌즈 돌리기 · 마우스 둘러보기 · {KeyBindings.Label(GameAction.Interact)} / ESC 놓기</size>";
                if (_noticeTimer > 0f && !string.IsNullOrEmpty(_notice))
                    text = _notice + "\n<size=80%>" + text + "</size>";
            }
            else if (_noticeTimer > 0f && _done && !string.IsNullOrEmpty(_notice))
                text = _notice;
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
            var go = new GameObject("LighthouseHint", typeof(RectTransform));
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
            Place(back.rectTransform, new Vector2(1060f, 112f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(1030f, 104f));
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
