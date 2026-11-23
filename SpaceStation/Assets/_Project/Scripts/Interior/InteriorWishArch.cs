using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 심우주 통신기지 체험 "별똥별 아치 소원" (사용자 2026-11-17 체험 2안, 값 = <see cref="DeepSpaceComm"/>):
    /// 앞 별똥별 아치 꼭대기 별을 보고 F = 아치 앞에 서서 아치 너머 하늘을 올려다봄 → 별똥별 (<see cref="ShootingStar"/>)이 하늘을 가로질러 오른쪽 강 굽이로 떨어짐 →
    /// 떨어진 자리에서 은하수 강을 따라 별빛이 번져 강 전체가 환해졌다가 (MilkyRiver UV 강 별빛 번짐 — MPB) 천천히 가라앉고, 강 위 떠 있는 별 셋이 들썩임.
    /// 소원 한 줄 (글)이 뜨고, 끝나면 (또는 F · ESC) 원래 자리로. 강 위 별 셋은 평소에도 물결에 천천히 흔들리며 돎. 실제 시간.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 선 채 누른 F · ESC를 여기서 먼저 씀
    public sealed class InteriorWishArch : MonoBehaviour
    {
        public const float StarDelay = 0.7f, WishSeconds = 8.5f, GlowSpread = 4.5f, GlowFade = 10f, GlowPeak = 1.8f;
        private static readonly int GlowAtId = Shader.PropertyToID("_GlowAt");
        private static readonly int GlowRadiusId = Shader.PropertyToID("_GlowRadius");
        private static readonly int GlowAmountId = Shader.PropertyToID("_GlowAmount");
        private static readonly string[] Wishes =
        {
            "별똥별이 은하수에 내려앉았어요. 소원이 강을 따라 흘러가요",
            "별빛이 강 끝 별 샘까지 번졌어요. 소원이 닿았을 거예요",
            "깊은 우주 어딘가에서 누군가 같은 별똥별을 봤을지도 몰라요",
        };

        [Tooltip("아치 꼭대기 별 충돌체 (보고 F)")]
        [SerializeField] private Collider _use;
        [Tooltip("은하수 강 (MilkyRiver UV 강 — 별빛 번짐 MPB)")]
        [SerializeField] private Renderer _river;
        [Tooltip("강 위 떠 있는 별 StarFloat{k}")]
        [SerializeField] private Transform[] _floatingStars;
        [SerializeField] private Material _streakMaterial;
        [SerializeField] private Material _sparkMaterial;
        [SerializeField] private TMP_FontAsset _font;
        [Tooltip("별똥별이 떨어지는 강 자리 (강 따라 m — 템플릿 빌더가 강 메시 UV로 잼) · 강 길이")]
        [SerializeField] private float _wishU;
        [SerializeField] private float _riverLength = 24f;

        private FirstPersonController _player;
        private bool _standing;
        private int _mountFrame, _standFrame = -1;
        private Vector3 _standPoint;
        private float _time = -1f, _glow = -1f;
        private bool _starDone;
        private ShootingStar _star;
        private ParticleSystem _sparks;
        private MaterialPropertyBlock _block;
        private Vector3[] _starPos;
        private Quaternion[] _starRot;
        private float[] _starSpin;
        private int _wish;
        private InteriorHintLine _hint;

        public bool Wishing => _time >= 0f;
        public bool Standing => _standing;
        /// <summary>강 별빛 번짐 세기 (0 = 없음 — 시험용).</summary>
        public float GlowAmount => _glow < 0f ? 0f : GlowPeak * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GlowSpread * 0.8f, GlowFade, _glow)));

        public void Configure(Collider use, Renderer river, Transform[] floatingStars, Material streak, Material spark, TMP_FontAsset font, float wishU, float riverLength)
        {
            _use = use;
            _river = river;
            _floatingStars = floatingStars;
            _streakMaterial = streak;
            _sparkMaterial = spark;
            _font = font;
            _wishU = wishU;
            _riverLength = riverLength;
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            int n = _floatingStars != null ? _floatingStars.Length : 0;
            _starPos = new Vector3[n];
            _starRot = new Quaternion[n];
            _starSpin = new float[n];
            for (int k = 0; k < n; k++)
            {
                if (_floatingStars[k] == null)
                    continue;
                _starPos[k] = _floatingStars[k].localPosition;
                _starRot[k] = _floatingStars[k].localRotation;
            }
            if (_use != null)
            {
                var use = _use.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "별똥별 아치 앞에서 소원 빌기";
                use.Blocked = () => Wishing ? "소원을 비는 중" : null;
                use.Completed = Mount;
            }
            _hint = new InteriorHintLine(transform, "WishArchHint", _font);
            ApplyRiver();
        }

        private void OnDisable()
        {
            Release();
            EndWish();
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
            if (_standing)
                Controls();
            if (_time >= 0f)
                StepWish(dt);
            if (_glow >= 0f)
            {
                _glow += dt;
                if (_glow >= GlowFade)
                    _glow = -1f;
                ApplyRiver();
            }
            FloatStars(dt);
            UpdateHint();
        }

        // ---------------- 서기 ----------------

        private void Mount()
        {
            if (_standing || Wishing || _player == null || Time.frameCount == _standFrame)
                return;
            _standPoint = _player.transform.position;
            _player.RideAt(transform, transform.TransformPoint(DeepSpaceComm.WishStand), FirstPersonController.EyeHeight, DeepSpaceComm.WishYaw + transform.eulerAngles.y, DeepSpaceComm.WishPitch);
            _standing = true;
            _mountFrame = Time.frameCount;
            if (_use != null)
                _use.enabled = false;
            BeginWish();
        }

        /// <summary>소원 시작 (서 있지 않아도 — 시험 · 연출용): 잠시 뒤 별똥별이 하늘을 가로지름.</summary>
        public void BeginWish()
        {
            _time = 0f;
            _starDone = false;
            _wish = Random.Range(0, Wishes.Length);
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void Controls()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                InputGate.ConsumeEscape();
                StepDown();
                return;
            }
            if (Time.frameCount != _mountFrame && KeyBindings.WasPressed(GameAction.Interact))
                StepDown();
        }

        private void StepDown()
        {
            _standFrame = Time.frameCount;
            Release();
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
            if (_use != null)
                _use.enabled = true;
        }

        // ---------------- 별똥별 · 강 별빛 ----------------

        private void StepWish(float dt)
        {
            _time += dt;
            if (!_starDone && _star == null && _time >= StarDelay)
            {
                _star = new ShootingStar(transform, _streakMaterial, transform.TransformPoint(DeepSpaceComm.WishStarFrom), transform.TransformPoint(DeepSpaceComm.WishStarTo), DeepSpaceComm.WishStarSpeed);
                AudioService.TryPlay(l => l.UiTab);
            }
            if (_star != null)
            {
                _star.Step(dt);
                _star.EmitTrail(EnsureSparks());
                if (_star.Arrived)
                {
                    InteriorFireflyShow.Burst(EnsureSparks(), _star.To, 36, 1.1f);
                    _star.Destroy();
                    _star = null;
                    _starDone = true;
                    _glow = 0f;
                    for (int k = 0; k < _starSpin.Length; k++)
                        _starSpin[k] = 1f;
                    AudioService.TryPlay(l => l.EventPositive);
                }
            }
            if (_time < WishSeconds)
                return;
            EndWish();
            if (_standing)
                StepDown();
        }

        private void EndWish()
        {
            _time = -1f;
            if (_star != null)
            {
                _star.Destroy();
                _star = null;
            }
        }

        /// <summary>강 별빛 번짐: 떨어진 자리에서 강을 따라 양쪽으로 퍼짐 (반경이 강 길이까지) → 천천히 가라앉음.</summary>
        private void ApplyRiver()
        {
            if (_river == null)
                return;
            _river.GetPropertyBlock(_block);
            float radius = Mathf.Lerp(0.6f, _riverLength, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Max(0f, _glow) / GlowSpread)));
            _block.SetFloat(GlowAtId, _wishU);
            _block.SetFloat(GlowRadiusId, radius);
            _block.SetFloat(GlowAmountId, GlowAmount);
            _river.SetPropertyBlock(_block);
        }

        /// <summary>강 위 별 셋: 물결에 1.2 cm 오르내리며 천천히 돎 (별빛이 번지면 잠깐 들썩이며 빨리 돎).</summary>
        private void FloatStars(float dt)
        {
            if (_floatingStars == null)
                return;
            float t = Time.unscaledTime;
            for (int k = 0; k < _floatingStars.Length; k++)
            {
                if (_floatingStars[k] == null)
                    continue;
                _starSpin[k] = Mathf.Max(0f, _starSpin[k] - dt / 3f);
                float hop = 0.012f * Mathf.Sin(t * 1.1f + k * 2.1f) + 0.06f * Mathf.Sin(Mathf.PI * _starSpin[k]) * _starSpin[k];
                _floatingStars[k].localPosition = _starPos[k] + Vector3.up * hop;
                float angle = t * (8f + 3f * k) + 360f * (1f - _starSpin[k]) * (_starSpin[k] > 0f ? 1f : 0f);
                _floatingStars[k].localRotation = Quaternion.AngleAxis(angle * (k % 2 == 0 ? 1f : -1f), Vector3.up) * _starRot[k];
            }
        }

        private ParticleSystem EnsureSparks()
        {
            if (_sparks == null)
                _sparks = ShootingStar.CreateSparks(transform, _sparkMaterial);
            return _sparks;
        }

        private void UpdateHint()
        {
            if (!_standing)
            {
                _hint.Show(_glow >= 0f && _glow < GlowSpread + 1f && Wishing ? Wishes[_wish] : null);
                return;
            }
            string f = KeyBindings.Label(GameAction.Interact);
            string line = !_starDone ? "아치 너머 하늘을 올려다봐요. 별똥별이 지나가요" : Wishes[_wish];
            _hint.Show(line + $"\n<size=80%>{f} · ESC = 돌아가기</size>");
        }
    }
}
