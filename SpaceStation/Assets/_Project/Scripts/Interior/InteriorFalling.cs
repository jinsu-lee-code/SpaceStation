using System;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 떨어지는 조각 (물 추출기 유리 낙하관 속 얼음 — 2026-10-19 피드백 "물방울 움직임 넣어줘").
    /// 조각마다 위에서 아래로 떨어지며 천천히 돌고, 아래 끝에 닿으면 위 끝으로 돌아간다 (위 · 아래 끝 = 깔때기 · 드럼 속이라 안 보임).
    /// 모듈이 꺼지면 멈춤. 조각 피벗 = 조각 가운데 (Blender에서 원점을 옮겨 내보냄). 실제 시간(unscaled)으로 움직임.
    /// </summary>
    public sealed class InteriorFalling : InteriorRoomDisplay
    {
        [SerializeField] private Transform[] _pieces = Array.Empty<Transform>();
        [SerializeField] private float _top;
        [SerializeField] private float _bottom;
        [SerializeField] private float _speed = 0.35f;
        [SerializeField] private float _spin = 40f;

        private Vector3[] _home;
        private float[] _phase;
        private float _fallen;

        public void Configure(Transform[] pieces, float top, float bottom, float speed, float spin)
        {
            _pieces = pieces;
            _top = top;
            _bottom = bottom;
            _speed = speed;
            _spin = spin;
        }

        protected override void Refresh()
        {
        }

        private void Update()
        {
            if (_pieces.Length == 0 || _top <= _bottom)
                return;
            if (_home == null)
            {
                _home = new Vector3[_pieces.Length];
                _phase = new float[_pieces.Length];
                for (int i = 0; i < _pieces.Length; i++)
                {
                    if (_pieces[i] == null)
                        continue;
                    _home[i] = _pieces[i].localPosition;
                    _phase[i] = _top - _home[i].y;
                }
            }
            if (Polled && !Active)
                return;
            float dt = Time.unscaledDeltaTime;
            _fallen += dt * _speed;
            float span = _top - _bottom;
            for (int i = 0; i < _pieces.Length; i++)
            {
                var p = _pieces[i];
                if (p == null)
                    continue;
                var pos = _home[i];
                pos.y = _top - Mathf.Repeat(_phase[i] + _fallen, span);
                p.localPosition = pos;
                p.Rotate(Vector3.up, (i % 2 == 0 ? 1f : -1f) * _spin * dt, Space.Self);
            }
        }
    }
}
