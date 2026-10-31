using System;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 조선소 용접 갠트리 (사용자 2026-10-31 조선소 시안 1 "이대로 유니티로 옮겨줘"):
    /// 유리 아치 레일에 매달린 갠트리가 짓는 화물선의 판 이음선 자리(Stations, z)로 옮겨 가서 양옆 토치를 선체에 대고 용접한다
    /// (토치 끝에서 현장 수리와 같은 작은 불꽃 <see cref="InteriorFx.Spark"/>, 한 번에 토치 하나만 — 순간 빛이 겹쳐 방 조명 수를 넘지 않게).
    /// 옮길 때는 토치를 선체에서 조금 떼어(법선 방향) 판을 긁지 않게 한다. 방금 용접한 이음선으로 바로 돌아가지 않음.
    /// 모듈이 멈추면(비활성 · 전력 부족) 그 자리에 멈춤. 좌표는 모두 템플릿 루트 로컬 (모델 자세 그대로 = 0).
    /// </summary>
    public sealed class InteriorShipyard : InteriorRoomDisplay
    {
        private const float Speed = 0.35f;        // m/s
        private const float Retract = 0.07f;      // 옮길 때 토치를 떼는 거리 (m)
        private const float ReachTime = 0.6f;     // 토치를 대고 떼는 시간
        private const float Jitter = 0.008f;      // 용접 중 토치 떨림 (m)
        private const float Pause = 0.8f;

        private enum Phase { Wait, Travel, Reach, Weld, Leave }

        [SerializeField] private Transform _gantry;          // 레일을 따라 z (모델 자세 z = RestZ)
        [SerializeField] private float _restZ;
        [SerializeField] private float[] _stations = Array.Empty<float>();   // 판 이음선 z
        [SerializeField] private Transform[] _torches = Array.Empty<Transform>();   // 갠트리 자식
        [SerializeField] private Vector3[] _tips = Array.Empty<Vector3>();          // 토치 로컬 — 선체에 닿는 끝
        [SerializeField] private Vector3[] _normals = Array.Empty<Vector3>();       // 토치 로컬 — 선체 바깥 (끝 → 토치 뒤)
        [SerializeField] private Material _spark;

        private Vector3 _gantryRest;
        private Vector3[] _torchRest;
        private Vector3[] _away;                 // 토치 부모 로컬 — 떼는 방향
        private float _z, _fromZ, _toZ, _t, _duration, _timer, _nextSpark;
        private float _reach;                    // 0 = 떼어 둠, 1 = 선체에 댐
        private Phase _phase;
        private int _station = -1, _torch = -1, _welds;
        private bool _ready;

        public void Configure(Transform gantry, float restZ, float[] stations, Transform[] torches, Vector3[] tips, Vector3[] normals, Material spark)
        {
            _gantry = gantry;
            _restZ = restZ;
            _stations = stations ?? Array.Empty<float>();
            _torches = torches ?? Array.Empty<Transform>();
            _tips = tips ?? Array.Empty<Vector3>();
            _normals = normals ?? Array.Empty<Vector3>();
            _spark = spark;
        }

        private void Setup()
        {
            if (_ready || _gantry == null)
                return;
            _ready = true;
            _gantryRest = _gantry.localPosition;
            _z = _restZ;
            _torchRest = new Vector3[_torches.Length];
            _away = new Vector3[_torches.Length];
            for (int i = 0; i < _torches.Length; i++)
            {
                var t = _torches[i];
                if (t == null)
                    continue;
                _torchRest[i] = t.localPosition;
                _away[i] = t.parent != null ? t.parent.InverseTransformDirection(t.TransformDirection(_normals[i])).normalized : _normals[i];
            }
            // 모델 자세 = 가운데 이음선에 댄 채 → 바로 용접부터
            for (int k = 0; k < _stations.Length; k++)
            {
                if (Mathf.Abs(_stations[k] - _restZ) < 0.01f)
                    _station = k;
            }
            _reach = 1f;
            BeginWeld();
        }

        protected override void Refresh()
        {
            Setup();
        }

        private void Update()
        {
            if (!_ready || _gantry == null)
                return;
            if (!Active || _spark == null)
                return;   // 모듈이 멈추면 그 자리에 멈춤
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            switch (_phase)
            {
                case Phase.Wait:
                    _timer -= dt;
                    if (_timer <= 0f)
                        Next();
                    break;
                case Phase.Travel:
                    _t = Mathf.Min(1f, _t + dt / _duration);
                    _z = Mathf.Lerp(_fromZ, _toZ, Smooth(_t));
                    if (_t >= 1f)
                        _phase = Phase.Reach;
                    break;
                case Phase.Reach:
                    _reach = Mathf.Min(1f, _reach + dt / ReachTime);
                    if (_reach >= 1f)
                        BeginWeld();
                    break;
                case Phase.Weld:
                    Weld(dt);
                    break;
                case Phase.Leave:
                    _reach = Mathf.Max(0f, _reach - dt / ReachTime);
                    if (_reach <= 0f)
                    {
                        _phase = Phase.Wait;
                        _timer = Pause;
                    }
                    break;
            }
            Apply();
        }

        /// <summary>다음 이음선 (방금 용접한 곳 말고).</summary>
        private void Next()
        {
            int count = 0, pick = -1;
            for (int k = 0; k < _stations.Length; k++)
            {
                if (k == _station && _stations.Length > 1)
                    continue;
                count++;
                if (UnityEngine.Random.Range(0, count) == 0)
                    pick = k;
            }
            if (pick < 0)
            {
                _timer = 1f;
                return;
            }
            _station = pick;
            _fromZ = _z;
            _toZ = _stations[pick];
            _duration = Mathf.Max(0.5f, Mathf.Abs(_toZ - _fromZ) / Speed);
            _t = 0f;
            _phase = Phase.Travel;
        }

        private void BeginWeld()
        {
            _phase = Phase.Weld;
            _welds = 0;
            _torch = UnityEngine.Random.Range(0, Mathf.Max(1, _torches.Length));
            _timer = UnityEngine.Random.Range(2f, 3f);
        }

        /// <summary>토치 하나씩 2 ~ 3초 — 양쪽 다 하면 떼고 다음 이음선으로.</summary>
        private void Weld(float dt)
        {
            _timer -= dt;
            if (_timer <= 0f)
            {
                _welds++;
                if (_welds >= Mathf.Min(2, _torches.Length))
                {
                    _torch = -1;
                    _phase = Phase.Leave;
                    return;
                }
                _torch = (_torch + 1) % _torches.Length;
                _timer = UnityEngine.Random.Range(2f, 3f);
            }
            float now = Time.unscaledTime;
            if (_torch < 0 || _torches[_torch] == null || now < _nextSpark)
                return;
            _nextSpark = now + UnityEngine.Random.Range(0.12f, 0.2f);
            var torch = _torches[_torch];
            var at = torch.TransformPoint(_tips[_torch]);
            InteriorFx.Spark(torch, at, torch.TransformDirection(_normals[_torch]) + Vector3.up * 0.3f, _spark, 4, 8, 0.8f);   // 현장 수리 용접과 같은 세기
        }

        private void Apply()
        {
            _gantry.localPosition = _gantryRest + Vector3.forward * (_z - _restZ);
            float now = Time.unscaledTime;
            for (int i = 0; i < _torches.Length; i++)
            {
                var t = _torches[i];
                if (t == null)
                    continue;
                var p = _torchRest[i] + _away[i] * (Retract * (1f - Smooth(_reach)));
                if (_phase == Phase.Weld && i == _torch)
                {
                    float k = now * 9f;
                    p += new Vector3(Mathf.PerlinNoise(k, i) - 0.5f, Mathf.PerlinNoise(i, k) - 0.5f, Mathf.PerlinNoise(k + 3f, i + 7f) - 0.5f) * (2f * Jitter);
                }
                t.localPosition = p;
            }
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
