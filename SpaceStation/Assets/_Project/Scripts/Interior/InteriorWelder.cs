using SpaceStation.Audio;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 정비 공창 수리 패드 용접 (사용자 2026-10-30 "용접 빛이면 별도로 이펙트로" · "아무것도 닿는 게 없는데 용접이 되는 것도 이상"):
    /// 패드 회전판 위 용접 팔의 토치(도는 부품 <see cref="InteriorOrbit"/>의 자식) 끝에서 현장 수리와 같은 작은 불꽃(<see cref="InteriorFx.Spark"/>)이 튀고,
    /// 용접하는 동안 토치가 살짝 떨린다. 한 번에 토치 하나만 — 몇 초 용접하고 쉬었다가 다른 토치로 (순간 빛이 겹쳐 방 조명 수를 넘지 않게).
    /// 모듈이 꺼지면 멈춤. 끝 · 방향은 토치 로컬 좌표.
    /// </summary>
    public sealed class InteriorWelder : InteriorRoomDisplay
    {
        private const float Jitter = 0.008f;   // 토치 떨림 (m)

        [SerializeField] private Transform[] _torches;
        [SerializeField] private Vector3[] _tips;
        [SerializeField] private Vector3[] _normals;
        [SerializeField] private Material _spark;

        private Vector3[] _rest;
        private int _current = -1;
        private int _last = -1;
        private float _until;
        private float _nextSpark;
        private ObjectLoop[] _arcs;
        private bool _arcsTried;

        public void Configure(Transform[] torches, Vector3[] tips, Vector3[] normals, Material spark)
        {
            _torches = torches;
            _tips = tips;
            _normals = normals;
            _spark = spark;
        }

        private void Awake()
        {
            _rest = new Vector3[_torches != null ? _torches.Length : 0];
            for (int i = 0; i < _rest.Length; i++)
                _rest[i] = _torches[i] != null ? _torches[i].localPosition : Vector3.zero;
        }

        protected override void Refresh()
        {
        }

        private void Update()
        {
            if (!Polled || _torches == null || _torches.Length == 0)
                return;
            if (!Active || _spark == null)
            {
                Stop();
                UpdateArcs();
                return;
            }
            float now = Time.unscaledTime;
            if (now >= _until)
            {
                if (_current >= 0)
                {
                    Stop();                                      // 쉬기
                    _until = now + Random.Range(1f, 2.5f);
                }
                else
                {
                    int next = Random.Range(0, _torches.Length);
                    if (_torches.Length > 1 && next == _last)
                        next = (next + 1) % _torches.Length;
                    _current = _last = next;                     // 용접 (방금 한 토치는 건너뜀)
                    _until = now + Random.Range(2f, 4f);
                }
            }
            UpdateArcs();
            if (_current < 0 || _torches[_current] == null)
                return;
            var torch = _torches[_current];
            float t = now * 9f;
            torch.localPosition = _rest[_current] + new Vector3(Mathf.PerlinNoise(t, _current) - 0.5f, Mathf.PerlinNoise(_current, t) - 0.5f,
                Mathf.PerlinNoise(t + 3f, _current + 7f) - 0.5f) * (2f * Jitter);
            if (now < _nextSpark)
                return;
            _nextSpark = now + Random.Range(0.12f, 0.2f);
            var at = torch.TransformPoint(_tips[_current]);
            InteriorFx.Spark(torch, at, torch.TransformDirection(_normals[_current]) + Vector3.up * 0.3f, _spark, 4, 8, 0.8f);   // 현장 수리 용접과 같은 세기
        }

        private void Stop()
        {
            if (_current >= 0 && _torches[_current] != null)
                _torches[_current].localPosition = _rest[_current];
            _current = -1;
        }

        /// <summary>용접 "쉬이 · 치직" (토치 끝마다 붙은 3D 루프): 지금 불꽃이 튀는 토치만 · 플레이어가 이 방에 있을 때만.</summary>
        private void UpdateArcs()
        {
            if (_arcs == null)
            {
                if (_arcsTried || AudioService.Instance == null)
                    return;
                _arcsTried = true;
                _arcs = new ObjectLoop[_torches.Length];
                for (int i = 0; i < _torches.Length; i++)
                {
                    if (_torches[i] == null)
                        continue;
                    _arcs[i] = AudioService.AttachLoop(l => l.T1WeldLoop, _torches[i], 1.5f, 12f);
                    if (_arcs[i] == null)
                        continue;
                    if (_tips != null && i < _tips.Length)
                        _arcs[i].transform.localPosition = _tips[i];
                    _arcs[i].FadeSpeed = 8f;   // 불꽃과 함께 바로 켜지고 꺼짐
                }
            }
            bool here = _current >= 0 && PlayerInRoom;
            for (int i = 0; i < _arcs.Length; i++)
                if (_arcs[i] != null)
                    _arcs[i].Level = here && i == _current ? 1f : 0f;
        }
    }
}
