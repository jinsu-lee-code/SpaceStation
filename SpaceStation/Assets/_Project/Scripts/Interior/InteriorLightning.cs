using SpaceStation.Audio;
using SpaceStation.Building;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 그리드 허브 번개 (사용자 2026-11-02 "번개 정원" 시안 1, "심이 좀 더 얇아야 번개 느낌"): 바깥 모듈과 같은 <see cref="LightningFx"/>를 방에 있을 때만 돌림.
    /// 번개가 치는 동안 찬 빛 하나가 번쩍임에 맞춰 정원을 비춤 (색약: 밝기 · 깜빡임). 모듈이 멈추면(비활성 · 전력 부족) 번개가 그침.
    /// 소리 (사운드 개편 5-2, 2026-11-23): 번개가 닿는 나무에서 "찌직 · 퐁" (F 오음계 칸을 골라 3D) · 탑 꼭대기 에너지 구에 붙은 낮은 웅 (가동 중).
    /// </summary>
    public sealed class InteriorLightning : InteriorRoomDisplay
    {
        private const float FlashLight = 1.1f;

        [SerializeField] private LightningFx _fx;
        [SerializeField] private Light _flash;

        private ObjectLoop _hum;
        private bool _humTried;

        public void Configure(LightningFx fx, Light flash)
        {
            _fx = fx;
            _flash = flash;
        }

        private void Awake()
        {
            if (_fx != null)
                _fx.Running = false;
            if (_flash != null)
                _flash.intensity = 0f;
        }

        private void OnEnable()
        {
            if (_fx != null)
                _fx.Struck += OnStruck;
        }

        private void OnDisable()
        {
            if (_fx != null)
                _fx.Struck -= OnStruck;
        }

        protected override void Refresh()
        {
            if (_fx == null)
                return;
            _fx.Running = Active;
            if (Active)
                _fx.Play();
        }

        private void Update()
        {
            if (_fx != null && _flash != null)
                _flash.intensity = FlashLight * _fx.StrikeLevel;
            UpdateHum();
        }

        /// <summary>번개가 나무에 닿음: 그 자리에서 작은 "찌직 · 퐁" (음 = 도 · 레 · 파 · 솔 · 라 중 하나 — 음악과 안 부딪힘, 큐 쉬는 시간으로 몰리지 않게).</summary>
        private void OnStruck(Vector3 at, bool fromTower)
        {
            if (!isActiveAndEnabled || !PlayerInRoom)
                return;   // 옆 방 번개는 소리 없음 (지금 있는 방 것만)
            AudioService.TryPlayNoteAt(l => l.LightningZap, at, Random.Range(0, 5), fromTower ? 1f : 0.7f);
        }

        /// <summary>탑 꼭대기 에너지 구 웅 (붙은 3D 루프) — 번개가 치는 동안 (가동 중)만.</summary>
        private void UpdateHum()
        {
            if (_fx == null || (_hum == null && AudioService.Instance == null))
                return;
            if (_hum == null)
            {
                if (_humTried)
                    return;   // 클립이 없음 — 다시 만들지 않음
                _humTried = true;
                var anchor = new GameObject("TeslaHum").transform;
                anchor.SetParent(_fx.transform, false);
                anchor.position = _fx.HubPosition;
                _hum = AudioService.AttachLoop(l => l.LightningHumLoop, anchor, 3f, 20f);
                if (_hum == null)
                {
                    Destroy(anchor.gameObject);
                    return;
                }
                _hum.Level = 0f;
            }
            _hum.Level = Polled && Active && PlayerInRoom ? 1f : 0f;
        }
    }
}
