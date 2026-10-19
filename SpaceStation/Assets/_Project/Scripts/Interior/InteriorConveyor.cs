using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 단백질 배양실 생산 라인 — 사용자 결정 2026-10-19 (위트: 프로틴 바 · 상자 = 차오르고 비움).
    /// 압출기 입구에서 프로틴 바(INT_Protein_Bar를 본뜬 복제)가 나와 벨트를 따라 앞으로 오고, 발바닥 압인기(INT_Protein_Press)가
    /// 바가 지날 때마다 눌러 찍는다. 벨트 끝에서 떨어져 포장 상자 자리 9곳에 차례로 쌓이고, 다 차면 뚜껑(INT_Protein_CrateLid)이
    /// 경첩을 축으로 닫혔다가 비우고 다시 열린다. 모듈이 멈추면(비활성 · 전력 부족) 라인 전체가 그 자리에 멈춘다.
    /// </summary>
    public sealed class InteriorConveyor : InteriorRoomDisplay
    {
        private const float Speed = 0.3f;          // 벨트 m/s
        private const float SpawnInterval = 2.4f;
        private const float FallTime = 0.45f;
        private const float LidTime = 1.1f;
        private const float HoldTime = 0.8f;

        [SerializeField] private Transform _bar;
        [SerializeField] private Transform _press;
        [SerializeField] private Transform _lid;
        [SerializeField] private Vector3 _beltStart;
        [SerializeField] private Vector3 _beltEnd;
        [SerializeField] private Vector3 _drop;
        [SerializeField] private float _pressZ;
        [SerializeField] private float _stroke = 0.03f;
        [Tooltip("상자 속 쌓이는 자리 (x, y, z = 바 가운데, w = y 회전 도)")]
        [SerializeField] private Vector4[] _pile = new Vector4[0];
        [SerializeField] private Vector3 _lidPivot;
        [SerializeField] private Vector3 _lidAxis = Vector3.forward;
        [Tooltip("닫을 때 경첩으로 도는 각 (모델은 열린 채)")]
        [SerializeField] private float _lidCloseDegrees = -100f;

        private sealed class Bar
        {
            public Transform T;
            public float Z;             // 벨트 위 z (떨어지기 전)
            public int Slot = -1;       // 상자 자리 (떨어지는 중 · 쌓임)
            public float FallT;
            public bool Stamped;
        }

        private readonly List<Bar> _bars = new List<Bar>();
        private readonly Stack<Transform> _pool = new Stack<Transform>();
        private Vector3 _barCenter, _barBasePos, _pressBase, _lidBase;
        private Quaternion _barBaseRot, _lidBaseRot;
        private float _spawnTimer;
        private int _filled;
        private float _lidT = -1f;      // 닫힘 · 비움 · 열림 진행 (−1 = 쉼)
        private bool _ready;

        public void Configure(Transform bar, Transform press, Transform lid, Vector3 beltStart, Vector3 beltEnd, Vector3 drop, float pressZ, float stroke,
            Vector4[] pile, Vector3 lidPivot, Vector3 lidAxis, float lidCloseDegrees)
        {
            _bar = bar;
            _press = press;
            _lid = lid;
            _beltStart = beltStart;
            _beltEnd = beltEnd;
            _drop = drop;
            _pressZ = pressZ;
            _stroke = stroke;
            _pile = pile;
            _lidPivot = lidPivot;
            _lidAxis = lidAxis;
            _lidCloseDegrees = lidCloseDegrees;
        }

        private void Setup()
        {
            if (_ready || _bar == null)
                return;
            _ready = true;
            var mf = _bar.GetComponent<MeshFilter>();
            _barBasePos = _bar.localPosition;
            _barBaseRot = _bar.localRotation;
            _barCenter = _barBasePos + _barBaseRot * (mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.center : Vector3.zero);
            _bar.gameObject.SetActive(false);   // 본뜨기 전용
            if (_press != null)
                _pressBase = _press.localPosition;
            if (_lid != null)
            {
                _lidBase = _lid.localPosition;
                _lidBaseRot = _lid.localRotation;
            }
        }

        protected override void Refresh()
        {
            Setup();
        }

        private void Update()
        {
            if (!_ready || !Active)
                return;   // 모듈이 멈추면 라인도 그 자리에 멈춤
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            // 뚜껑: 닫힘 → 잠깐 → 비움 → 열림
            if (_lidT >= 0f)
            {
                _lidT += dt;
                float total = LidTime * 2f + HoldTime;
                if (_lidT >= LidTime && _filled > 0)
                    ClearPile();
                float a = _lidT < LidTime ? Smooth(_lidT / LidTime)
                    : _lidT < LidTime + HoldTime ? 1f
                    : 1f - Smooth((_lidT - LidTime - HoldTime) / LidTime);
                SetLid(a);
                if (_lidT >= total)
                {
                    _lidT = -1f;
                    SetLid(0f);
                }
            }

            // 새 바 (상자에 남은 자리만큼만 벨트에 올림)
            _spawnTimer -= dt;
            if (_spawnTimer <= 0f && _lidT < 0f && _filled + OnBelt() < _pile.Length)
            {
                _spawnTimer = SpawnInterval;
                _bars.Add(new Bar { T = Take(), Z = _beltStart.z });
            }

            float press = 0f;
            float dir = Mathf.Sign(_beltEnd.z - _beltStart.z);
            for (int i = _bars.Count - 1; i >= 0; i--)
            {
                var b = _bars[i];
                if (b.Slot < 0)
                {
                    b.Z += dir * Speed * dt;
                    float near = 1f - Mathf.Abs(b.Z - _pressZ) / 0.05f;
                    press = Mathf.Max(press, near);
                    if (!b.Stamped && Mathf.Abs(b.Z - _pressZ) < 0.005f)
                        b.Stamped = true;
                    if ((b.Z - _beltEnd.z) * dir >= 0f)
                    {
                        if (_filled >= _pile.Length)
                        {
                            b.Z = _beltEnd.z;   // 상자가 차 있으면 벨트 끝에서 기다림
                            Pose(b.T, new Vector3(_beltStart.x, _barCenter.y, b.Z), 0f);
                            continue;
                        }
                        b.Slot = _filled++;
                        b.FallT = 0f;
                    }
                    else
                    {
                        Pose(b.T, new Vector3(_beltStart.x, _barCenter.y, b.Z), 0f);
                        continue;
                    }
                }
                if (b.FallT < 1f)
                {
                    // 벨트 끝 → 떨어지는 점 → 상자 자리 (짧은 포물선)
                    b.FallT = Mathf.Min(1f, b.FallT + dt / FallTime);
                    var slot = _pile[b.Slot];
                    var to = new Vector3(slot.x, slot.y, slot.z);
                    var from = new Vector3(_beltEnd.x, _barCenter.y, _beltEnd.z);   // 바 가운데 높이 = 벨트 윗면 + 반 두께 (모델 그대로)
                    float u = b.FallT;
                    var p = Vector3.Lerp(Vector3.Lerp(from, _drop, u), Vector3.Lerp(_drop, to, u), u);
                    Pose(b.T, p, slot.w * u);
                    if (b.FallT >= 1f && _filled >= _pile.Length && _lidT < 0f && AllSettled())
                        _lidT = 0f;
                }
            }
            if (_press != null)
                _press.localPosition = _pressBase + Vector3.down * (_stroke * Mathf.Clamp01(press));
        }

        private int OnBelt()
        {
            int n = 0;
            foreach (var b in _bars)
            {
                if (b.Slot < 0)
                    n++;
            }
            return n;
        }

        private bool AllSettled()
        {
            foreach (var b in _bars)
            {
                if (b.Slot >= 0 && b.FallT < 1f)
                    return false;
            }
            return true;
        }

        private void ClearPile()
        {
            for (int i = _bars.Count - 1; i >= 0; i--)
            {
                if (_bars[i].Slot < 0)
                    continue;
                _bars[i].T.gameObject.SetActive(false);
                _pool.Push(_bars[i].T);
                _bars.RemoveAt(i);
            }
            _filled = 0;
        }

        private Transform Take()
        {
            Transform t;
            if (_pool.Count > 0)
                t = _pool.Pop();
            else
            {
                t = Instantiate(_bar.gameObject, _bar.parent).transform;
                t.name = "ProteinBar";
            }
            t.gameObject.SetActive(true);
            return t;
        }

        /// <summary>바 가운데를 p에 놓고 위 축으로 yaw도 돌림 (메시는 처음 자리에 구워져 있으므로 가운데 기준으로 옮김).</summary>
        private void Pose(Transform t, Vector3 p, float yaw)
        {
            var r = Quaternion.AngleAxis(yaw, Vector3.up);
            t.localRotation = r * _barBaseRot;
            t.localPosition = p - r * (_barCenter - _barBasePos);
        }

        private void SetLid(float closed01)
        {
            if (_lid == null)
                return;
            var q = Quaternion.AngleAxis(_lidCloseDegrees * closed01, _lidAxis);
            _lid.localPosition = _lidPivot + q * (_lidBase - _lidPivot);
            _lid.localRotation = q * _lidBaseRot;
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
