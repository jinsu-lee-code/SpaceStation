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
    /// 항성 발전망 체험 "정자 꽃 그네" (사용자 4안 — F 박자로 굴러 높이 오르기 · 수정 꽃송이 종 울리기, 규칙 = <see cref="FlowerSwing"/>):
    /// 정자 뒤쪽 그네 자리를 보고 F = 앉음 → 그네가 앞뒤 끝에 닿을 때 F로 굴러 점점 높이. 굴대 위 가지에 늘어진 수정 꽃송이 종 (앞뒤 셋씩, 28° · 44° · 60°)은
    /// 그네가 그 높이에 닿으면 흔들리며 반짝 울림, 여섯 개를 다 울리면 걸린 시간 · 최고 기록 (글자). S 꾹 = 발로 끌어 멈추고 내림, ESC = 바로 내림.
    /// 그네 조각 = 굴대 피벗 아래 (z축으로 흔들림, + = 앞 +x), 시선은 줄 위 눈 자리에 실려 그네 기울기의 0.4배만큼 위아래로 (내부 걸음 흔들림 설정을 끄면 수평).
    /// 박자 안내 = 끝에 다가오면 "지금 F!"가 깜박임 (색 대신 글 · 밝기, 색약). 시간 = 실제 시간. 값 = <see cref="StellarGrid"/>.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 앉은 채 누른 F · ESC를 여기서 먼저 씀
    public sealed class InteriorFlowerSwing : MonoBehaviour
    {
        private const float RideEye = 0.85f;           // 자리 윗면 → 눈 (줄 방향)
        private const float SeatTop = 0.035f;          // 자리 가운데 → 윗면
        private const float ViewTilt = 0.4f;           // 시선 기울기 = 그네 각 × 이만큼 (그대로 따르면 60°에서 천장만 보였음)
        private const float BellSwing = 16f, BellDecay = 2.6f, BellHz = 1.9f, GlowSeconds = 1.4f;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("굴대 피벗 (템플릿 기준 StellarGrid.SwingPivot, 그네 조각이 이 아래에서 z축으로 흔들림)")]
        [SerializeField] private Transform _pivot;
        [Tooltip("그네 자리 (피벗 아래, 보고 F = 앉기)")]
        [SerializeField] private Collider _seat;
        [Tooltip("수정 꽃송이 종 (앞 셋 = 낮은 것부터, 뒤 셋) — 원점 = 매달린 꼭대기")]
        [SerializeField] private Transform[] _bells;
        [SerializeField] private Material _sparkMaterial;
        [SerializeField] private TMP_FontAsset _font;

        private static float _bestSeconds = -1f;

        private FlowerSwing _swing;
        private readonly List<int> _rang = new List<int>();
        private Transform _rider;
        private FirstPersonController _player;
        private bool _seated;
        private int _standFrame = -1;
        private Vector3 _getOff;          // 템플릿 기준 — 앉기 전에 서 있던 자리
        private float _getOffYaw;
        private float _rideTime;
        private bool _done;
        private Quaternion[] _bellRest;
        private float[] _bellT;
        private Renderer[] _bellRenderers;
        private MaterialPropertyBlock _block;
        private string _notice;
        private float _noticeTimer;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public bool Seated => _seated;
        public float Angle => _swing != null ? _swing.Angle : 0f;
        public FlowerSwing Swing => _swing;
        public static float BestSeconds => _bestSeconds;

        public void Configure(Transform pivot, Collider seat, Transform[] bells, Material spark, TMP_FontAsset font)
        {
            _pivot = pivot;
            _seat = seat;
            _bells = bells;
            _sparkMaterial = spark;
            _font = font;
        }

        private void Awake()
        {
            _swing = new FlowerSwing(StellarGrid.SwingLength, StellarGrid.BellAngles);
            _block = new MaterialPropertyBlock();
            if (_pivot != null)
            {
                _rider = new GameObject("SwingRider").transform;   // 줄 위 눈 자리, 앞 = +x (그네가 앞으로 오르면 시선도 조금 위로 — ApplyPose)
                _rider.SetParent(_pivot.parent, false);
            }
            if (_seat != null)
            {
                var use = _seat.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "꽃 그네 타기" + (_bestSeconds < 0f ? "" : $"  <size=80%>(최고 기록 {Clock(_bestSeconds)})</size>");
                use.Blocked = () => !_swing.AtRest ? "그네가 멈추면 탈 수 있어요" : null;
                use.Completed = Sit;
            }
            int n = _bells != null ? _bells.Length : 0;
            _bellRest = new Quaternion[n];
            _bellT = new float[n];
            _bellRenderers = new Renderer[n];
            for (int i = 0; i < n; i++)
            {
                _bellT[i] = 99f;
                if (_bells[i] == null)
                    continue;
                _bellRest[i] = _bells[i].localRotation;
                _bellRenderers[i] = _bells[i].GetComponent<Renderer>();
            }
        }

        private void OnDisable()
        {
            // 방을 나가면 바로 멈추고 내려놓음
            ReleasePlayer();
            if (_swing != null)
                _swing.Stop();
            ApplyPose();
            for (int i = 0; _bellT != null && i < _bellT.Length; i++)
                _bellT[i] = 99f;
            ApplyBells();
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        private void Update()
        {
            if (_pivot == null)
                return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_noticeTimer > 0f)
                _noticeTimer -= dt;
            if (_seated)
                Ride(dt);
            else
                _swing.Braking = true;   // 아무도 안 타면 천천히 멈춤
            _rang.Clear();
            _swing.Step(dt, _rang);
            foreach (int i in _rang)
                Ring(i);
            if (_seated && !_done && _swing.AllRung)
                Finish();
            if (_seat != null && !_seated)
                _seat.enabled = _swing.AtRest;   // 흔들리는 자리에 몸이 끼지 않게
            ApplyPose();
            for (int i = 0; i < _bellT.Length; i++)
                _bellT[i] += dt;
            ApplyBells();
            UpdateHint();
        }

        private void Ride(float dt)
        {
            _rideTime += dt;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                InputGate.ConsumeEscape();   // 내부 나가기보다 먼저
                StandUp();
                return;
            }
            bool brake = KeyBindings.IsPressed(GameAction.CameraBack);
            _swing.Braking = brake;
            if (brake && _swing.AtRest)
            {
                StandUp();
                return;
            }
            if (!brake && KeyBindings.WasPressed(GameAction.Interact))
                Kick(_swing.Pump());
        }

        private void Kick(FlowerSwing.Kick kick)
        {
            switch (kick)
            {
                case FlowerSwing.Kick.Start:
                    Notice("출발! 그네가 앞뒤 끝에 닿을 때마다 F", 3f);
                    AudioService.TryPlay(l => l.UiOpen);
                    break;
                case FlowerSwing.Kick.Perfect:
                    Notice($"박자 딱!  높이 +{FlowerSwing.PerfectGain:F0}°", 1.4f);
                    AudioService.TryPlay(l => l.UiOpen);
                    break;
                case FlowerSwing.Kick.Good:
                    Notice($"좋아요  높이 +{FlowerSwing.GoodGain:F0}°", 1.4f);
                    AudioService.TryPlay(l => l.UiTab);
                    break;
                case FlowerSwing.Kick.Early:
                    Notice("조금 일러요 · 끝까지 올라간 뒤에", 1.6f);
                    AudioService.TryPlay(l => l.UiHover);
                    break;
                case FlowerSwing.Kick.Late:
                    Notice("조금 늦어요 · 끝에 닿을 때", 1.6f);
                    AudioService.TryPlay(l => l.UiHover);
                    break;
                case FlowerSwing.Kick.Again:
                    Notice("이번 흔들림엔 벌써 굴렀어요", 1.2f);
                    break;
            }
        }

        private void Finish()
        {
            _done = true;
            bool record = _bestSeconds < 0f || _rideTime < _bestSeconds;
            if (record)
                _bestSeconds = _rideTime;
            Notice($"수정 종 여섯 개를 다 울렸어요!  {Clock(_rideTime)}" + (record ? "  ·  최고 기록!" : $"  ·  최고 {Clock(_bestSeconds)}"), 5f);
            for (int i = 0; i < _bellT.Length; i++)
            {
                _bellT[i] = 0f;   // 다 같이 한 번 더 울림
                Sparkle(i, 26);
            }
            AudioService.TryPlay(l => l.GradeUp);
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return $"{s / 60}:{s % 60:00}";
        }

        // ---------------- 플레이어 ----------------

        private void Sit()
        {
            if (_seated || _player == null || !_swing.AtRest || Time.frameCount == _standFrame)
                return; // 내리기와 같은 프레임의 F (InteriorMode가 뒤에서 다시 부름)
            _getOff = transform.InverseTransformPoint(_player.transform.position);
            _getOffYaw = _player.transform.eulerAngles.y - transform.eulerAngles.y;
            _swing.Begin();
            _rideTime = 0f;
            _done = false;
            // 처음 시선 = 앞 (+x)에서 앞 종 쪽 (+z)으로 12° — 바로 앞은 정자 갈비대 (30°)가 화면 한가운데를 가렸음
            _player.RideAt(_rider, _rider.position, 0f, transform.eulerAngles.y + 90f - 12f, 0f, followPitch: true);
            _seated = true;
            if (_seat != null)
                _seat.enabled = false;
            Notice("F = 출발 · 그네가 앞뒤 끝에 닿을 때 F로 굴러 수정 종 여섯 개를 울려요", 5f);
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void StandUp()
        {
            _standFrame = Time.frameCount;
            ReleasePlayer();
            AudioService.TryPlay(l => l.UiClose);
        }

        private void ReleasePlayer()
        {
            if (!_seated)
                return;
            _seated = false;
            if (_player == null)
                return;
            _player.RideAt(transform, transform.TransformPoint(_getOff), FirstPersonController.EyeHeight, transform.eulerAngles.y + _getOffYaw);
            _player.Dismount();
        }

        // ---------------- 그네 · 종 ----------------

        private void ApplyPose()
        {
            if (_pivot == null || _swing == null)
                return;
            var q = Quaternion.AngleAxis(_swing.Angle, Vector3.forward);   // + = 자리가 +x (앞)로
            _pivot.localRotation = q;
            if (_rider != null)
            {
                _rider.localPosition = _pivot.localPosition + q * new Vector3(0f, -StellarGrid.SwingLength + SeatTop + RideEye, 0f);
                _rider.localRotation = Quaternion.AngleAxis(_swing.Angle * ViewTilt, Vector3.forward) * Quaternion.Euler(0f, 90f, 0f);
            }
        }

        private void Ring(int i)
        {
            if (i >= _bellT.Length)
                return;
            _bellT[i] = 0f;
            Sparkle(i, 16);
            AudioService.TryPlay(l => l.UiTab);
            if (_seated && !_done)
                Notice($"수정 종 {_swing.RungCount} / {FlowerSwing.BellCount} 딸랑!", 1.6f);
        }

        /// <summary>종: 매달린 꼭대기를 축으로 그네가 지나간 쪽으로 흔들렸다 잦아듦 + 반짝 (밝기 — 색 대신).</summary>
        private void ApplyBells()
        {
            if (_bells == null)
                return;
            for (int i = 0; i < _bells.Length; i++)
            {
                var b = _bells[i];
                if (b == null)
                    continue;
                float t = _bellT[i];
                float side = i < 3 ? 1f : -1f;
                float a = t < 6f ? side * BellSwing * Mathf.Exp(-BellDecay * t) * Mathf.Sin(2f * Mathf.PI * BellHz * t) : 0f;
                b.localRotation = Quaternion.AngleAxis(a, Vector3.forward) * _bellRest[i];
                var r = _bellRenderers[i];
                if (r == null)
                    continue;
                float glow = t < GlowSeconds ? 1f - t / GlowSeconds : 0f;
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
                        _block.SetColor(EmissionId, mats[m].GetColor(EmissionId) + new Color(1.2f, 1.15f, 1.3f) * (1.6f * glow * glow));
                    else if (mats[m].HasProperty(BaseColorId))
                        _block.SetColor(BaseColorId, Color.Lerp(mats[m].GetColor(BaseColorId), Color.white, 0.6f * glow));
                    r.SetPropertyBlock(_block, m);
                }
            }
        }

        /// <summary>종 둘레 반짝이 (옅은 흰빛 점이 천천히 떠올라 사라짐 — 가산 재질, 색 = 입자 색).</summary>
        private void Sparkle(int i, int count)
        {
            if (_sparkMaterial == null || _bells == null || i >= _bells.Length || _bells[i] == null)
                return;
            var at = _bellRenderers[i] != null ? _bellRenderers[i].bounds.center : _bells[i].position;
            var go = new GameObject("BellSparkle");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.97f, 1f), new Color(0.9f, 0.86f, 1f));
            main.gravityModifier = -0.04f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count, (short)(count + 8)) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.18f;
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
            if (_seated)
            {
                string cue = "";
                if (!_swing.AtRest && !_swing.Kicked && _swing.Timing >= FlowerSwing.GoodWindow)
                    cue = Mathf.Repeat(Time.unscaledTime * 4f, 1f) < 0.6f ? "   <b>지금 F!</b>" : "   <alpha=#55><b>지금 F!</b><alpha=#FF>";   // 깜박임 (색 대신 밝기)
                text = $"높이 {_swing.Amplitude:F0}°  ·  수정 종 {_swing.RungCount} / {FlowerSwing.BellCount}{cue}\n"
                     + $"<size=80%>그네가 앞뒤 끝에 닿을 때 {KeyBindings.Label(GameAction.Interact)} · {KeyBindings.Label(GameAction.CameraBack)} 꾹 = 멈추고 내리기 · ESC 바로 내리기</size>";
                if (_noticeTimer > 0f && !string.IsNullOrEmpty(_notice))
                    text = _notice + "\n<size=80%>" + text + "</size>";
            }
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
            var go = new GameObject("FlowerSwingHint", typeof(RectTransform));
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
