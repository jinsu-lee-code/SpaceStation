using SpaceStation.Building;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 그리드 허브 번개 (사용자 2026-11-02 "번개 정원" 시안 1, "심이 좀 더 얇아야 번개 느낌"): 바깥 모듈과 같은 <see cref="LightningFx"/>를 방에 있을 때만 돌림.
    /// 번개가 치는 동안 찬 빛 하나가 번쩍임에 맞춰 정원을 비춤 (색약: 밝기 · 깜빡임). 모듈이 멈추면(비활성 · 전력 부족) 번개가 그침.
    /// </summary>
    public sealed class InteriorLightning : InteriorRoomDisplay
    {
        private const float FlashLight = 1.1f;

        [SerializeField] private LightningFx _fx;
        [SerializeField] private Light _flash;

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
        }
    }
}
