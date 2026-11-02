using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 그리드 허브 번개 (사용자 2026-11-02 "번개 정원" 시안 1 "이대로 유니티로 옮겨줘"): 탑 → 나무 · 나무 ↔ 나무 번개 메시를 번갈아 잠깐씩 켬.
    /// 한 번에 2 ~ 3번 빠르게 번쩍이고 (0.05 ~ 0.1초씩), 번쩍이는 동안 찬 빛 하나가 정원을 비춤 (색약: 밝기 · 깜빡임). 모듈이 멈추면(비활성 · 전력 부족) 번개가 그침. 실제 시간(unscaled).
    /// </summary>
    public sealed class InteriorLightning : InteriorRoomDisplay
    {
        private const float FlashLight = 1.6f;

        [SerializeField] private Renderer[] _bolts = System.Array.Empty<Renderer>();
        [SerializeField] private Light _flash;
        [SerializeField] private Vector2 _strikeGap = new Vector2(0.35f, 1.1f);

        private float[] _until, _nextFlash;
        private int[] _left;
        private float _nextStrike;

        public void Configure(Renderer[] bolts, Light flash)
        {
            _bolts = bolts;
            _flash = flash;
        }

        private void Awake()
        {
            _until = new float[_bolts.Length];
            _nextFlash = new float[_bolts.Length];
            _left = new int[_bolts.Length];
            AllOff();
        }

        protected override void Refresh()
        {
            if (!Active)
                AllOff();
        }

        private void AllOff()
        {
            for (int i = 0; i < _bolts.Length; i++)
            {
                if (_bolts[i] != null)
                    _bolts[i].enabled = false;
                _left[i] = 0;
                _until[i] = 0f;
            }
            if (_flash != null)
                _flash.intensity = 0f;
        }

        private void Update()
        {
            if (!Polled || !Active || _bolts.Length == 0)
                return;
            float now = Time.unscaledTime;
            if (now >= _nextStrike)
            {
                _nextStrike = now + Random.Range(_strikeGap.x, _strikeGap.y);
                int i = Random.Range(0, _bolts.Length);
                if (_left[i] == 0)
                {
                    _left[i] = Random.Range(2, 4);
                    _nextFlash[i] = now;
                }
            }
            bool any = false;
            for (int i = 0; i < _bolts.Length; i++)
            {
                if (_left[i] > 0 && now >= _nextFlash[i])
                {
                    _left[i]--;
                    _until[i] = now + Random.Range(0.05f, 0.1f);
                    _nextFlash[i] = _until[i] + Random.Range(0.04f, 0.09f);
                }
                bool on = now < _until[i];
                any |= on;
                if (_bolts[i] != null && _bolts[i].enabled != on)
                    _bolts[i].enabled = on;
            }
            if (_flash != null)
                _flash.intensity = any ? FlashLight : 0f;
        }
    }
}
