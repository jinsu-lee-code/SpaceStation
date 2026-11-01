using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 진화 드론 정비창 옥상 드론 라이트 쇼 (사용자 2026-10-27 "선으로 연결되어서 왠 파리떼같아보여", StationArtBuilder가 자동 배선):
    /// 모델에 구워 둔 별 모양 드론 무리 + 잇는 빛줄(SpinDish_DroneDepotSwarm)은 가져올 때 지우고(EvoBakedFxStrip),
    /// 빛 점 드론 서른이 별 → 왕관 고리 → 하트 → 은하 소용돌이 → 물결 대형을 차례로 이루며 날아다닌다 (선 없음).
    /// 대형 사이에서는 드론마다 조금씩 늦게 출발해 살짝 떠올랐다 내려앉으며 옮겨 가고, 대형 위쪽은 카메라 반대편을 향해 똑바로 읽힘.
    /// 대형마다 색과 밝기가 다름 (색약: 밝기 · 깜빡임으로도 구분). 비활성이면 그 자리에 멈춰 어두워짐. 고스트에서는 만들지 않음.
    /// </summary>
    public sealed class DroneShowFx : MonoBehaviour
    {
        public const int FormationCount = 5;

        [SerializeField] private Material _lightMaterial;
        [SerializeField] private int _count = 30;
        [Tooltip("대형 반지름 (모델 기준)")]
        [SerializeField] private float _radius = 0.6f;
        [Tooltip("대형 하나를 유지하는 시간 (게임 초)")]
        [SerializeField] private float _hold = 5f;
        [Tooltip("다음 대형으로 옮겨 가는 시간")]
        [SerializeField] private float _move = 2.4f;
        [Tooltip("빛 점 크기 — 게임 카메라 거리(약 12m)에서도 대형이 읽히게")]
        [SerializeField] private float _lightSize = 0.075f;

        // 별(금빛 흰색) · 왕관(얼음 파랑) · 하트(분홍) · 소용돌이(보라, 어둡게) · 물결(살구 — 모듈 색) — 밝기도 서로 다름
        // (입자 색은 0~1로 잘리므로 더 밝게는 재질(M_FireworkSpark HDR)이 맡음)
        private static readonly Color[] Colors =
        {
            new Color(1f, 0.93f, 0.7f), new Color(0.65f, 0.88f, 1f), new Color(1f, 0.55f, 0.78f), new Color(0.78f, 0.58f, 1f) * 0.85f, new Color(1f, 0.75f, 0.5f),
        };

        private ParticleSystem _ps;
        private ParticleSystem.Particle[] _parts;
        private Vector3[] _pose;
        private Vector3[] _a;
        private Vector3[] _b;
        private ModuleView _view;
        private bool _live;
        private float _clock;
        private float _level = 1f;
        private float _seed;

        public int Count => _parts != null ? _parts.Length : 0;

        private void Start()
        {
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            if (_live)
                Play();
        }

        /// <summary>빛 점 드론을 만들고 쇼 시작 (Start가 부름 — 확인용으로 미리보기에서 직접 불러도 됨).</summary>
        public void Play()
        {
            if (_ps != null || _lightMaterial == null || _count <= 0)
                return;
            _seed = Random.value * 100f;
            _clock = Random.Range(0f, (_hold + _move) * FormationCount);
            var go = new GameObject("DroneLights");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = _count;
            var emission = _ps.emission;
            emission.enabled = false;
            var shape = _ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _lightMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            _parts = new ParticleSystem.Particle[_count];
            _pose = new Vector3[_count];
            _a = new Vector3[_count];
            _b = new Vector3[_count];
            _ps.Play();
            Refresh();
        }

        private void Update()
        {
            if (_ps == null)
                return;
            bool on = !_live || _view.Operational;
            if (on)
                _clock += Time.deltaTime;
            _level = Mathf.MoveTowards(_level, on ? 1f : 0.2f, Time.deltaTime * 1.5f);
            Refresh();
        }

        private void Refresh()
        {
            Color color = Pose(_clock, _count, _hold, _move, _pose, _a, _b);
            // 대형 위쪽(+z)이 카메라 반대편을 향하도록 가로로 돌림 (모듈이 90도씩 돌아가 있어도 똑바로 읽힘)
            var rot = Quaternion.identity;
            var cam = Camera.main;
            if (cam != null)
            {
                var away = transform.InverseTransformDirection(Vector3.ProjectOnPlane(transform.position - cam.transform.position, Vector3.up));
                away.y = 0f;
                if (away.sqrMagnitude > 1e-4f)
                    rot = Quaternion.LookRotation(away.normalized, Vector3.up);
            }
            for (int i = 0; i < _count; i++)
            {
                float twinkle = 0.8f + 0.2f * Mathf.Sin(_clock * 7f + i * 2.3f + _seed);
                var c = color * (twinkle * _level);
                c.a = 1f;
                _parts[i].position = rot * (_pose[i] * _radius);
                _parts[i].startSize = _lightSize * (0.85f + 0.15f * twinkle);
                _parts[i].startColor = c;
                _parts[i].startLifetime = 1000f;
                _parts[i].remainingLifetime = 1000f;
            }
            _ps.SetParticles(_parts, _count);
        }

        /// <summary>
        /// time 때 드론 위치 (반지름 1 기준, 가로 x · z, 위 y) → result, 그때 빛 색을 돌려줌. a · b = 계산용 (count 길이).
        /// 대형마다 hold초 머문 뒤 move초 동안 다음 대형으로 옮겨 감 (드론마다 조금씩 늦게 출발, 옮기는 동안 살짝 떠오름).
        /// </summary>
        public static Color Pose(float time, int count, float hold, float move, Vector3[] result, Vector3[] a, Vector3[] b)
        {
            float cycle = hold + move;
            int step = Mathf.FloorToInt(time / cycle);
            float local = time - step * cycle;
            int f = ((step % FormationCount) + FormationCount) % FormationCount;
            Formation(f, count, time, a);
            if (local < hold)
            {
                System.Array.Copy(a, result, count);
                return Colors[f];
            }
            int g = (f + 1) % FormationCount;
            Formation(g, count, time, b);
            float k = (local - hold) / move;
            const float stagger = 0.35f;
            for (int i = 0; i < count; i++)
            {
                float delay = stagger * (count > 1 ? i / (float)(count - 1) : 0f);
                float ki = Mathf.Clamp01((k * (1f + stagger) - delay));
                ki = ki * ki * (3f - 2f * ki);
                float lift = 0.1f * Mathf.Sin(Mathf.PI * ki) * (0.6f + 0.4f * ((i * 7) % 5) / 4f);
                result[i] = Vector3.Lerp(a[i], b[i], ki) + Vector3.up * lift;
            }
            float kc = k * k * (3f - 2f * k);
            return Color.Lerp(Colors[f], Colors[g], kc);
        }

        /// <summary>대형 f의 드론 위치 (반지름 1 안, 위쪽 = +z, 높이 y는 ±0.1 안). 순서는 윤곽을 따라 돌아 옮길 때 길이 덜 엉킴.</summary>
        public static void Formation(int f, int count, float time, Vector3[] result)
        {
            switch (f)
            {
                case 0: Star(count, time, result); break;
                case 1: Crown(count, time, result); break;
                case 2: Heart(count, time, result); break;
                case 3: Spiral(count, time, result); break;
                default: Wave(count, time, result); break;
            }
        }

        // 다섯 꼭짓점 별 윤곽 (꼭짓점 하나가 위쪽), 천천히 숨 쉬듯 커졌다 작아짐
        private static void Star(int count, float time, Vector3[] result)
        {
            var v = new Vector2[10];
            for (int q = 0; q < 10; q++)
            {
                float ang = Mathf.PI / 2f + Mathf.PI * q / 5f;
                float r = q % 2 == 0 ? 1f : 0.42f;
                v[q] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
            }
            float breathe = 0.94f + 0.06f * Mathf.Sin(time * 1.3f);
            for (int i = 0; i < count; i++)
            {
                float s = i * 10f / count; // 열 변 길이가 모두 같아 변마다 같은 수
                int q = Mathf.FloorToInt(s) % 10;
                var p = Vector2.Lerp(v[q], v[(q + 1) % 10], s - Mathf.Floor(s)) * breathe;
                result[i] = new Vector3(p.x, 0.02f * Mathf.Sin(time * 2f + i), p.y);
            }
        }

        // 왕관 고리: 돌면서 위아래로 물결치는 원
        private static void Crown(int count, float time, Vector3[] result)
        {
            for (int i = 0; i < count; i++)
            {
                float ang = Mathf.PI / 2f + 2f * Mathf.PI * i / count + time * 0.35f;
                result[i] = new Vector3(Mathf.Cos(ang) * 0.85f, 0.07f * Mathf.Sin(ang * 5f - time * 2.5f), Mathf.Sin(ang) * 0.85f);
            }
        }

        private static Vector2[] _heart;

        // 하트 (뾰족한 끝이 아래쪽 −z), 콩닥콩닥 뜀. 매개변수 간격 그대로 놓으면 위쪽 오목한 곳에 두 점이 붙어 윤곽 길이로 고르게 놓음
        private static void Heart(int count, float time, Vector3[] result)
        {
            if (_heart == null || _heart.Length != count)
                _heart = HeartOutline(count);
            float beat = 1f + 0.06f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(time * 4.4f)), 6f);
            for (int i = 0; i < count; i++)
                result[i] = new Vector3(_heart[i].x, 0f, _heart[i].y) * beat;
        }

        private static Vector2[] HeartOutline(int count)
        {
            const int dense = 360;
            var pts = new Vector2[dense + 1];
            var len = new float[dense + 1];
            for (int j = 0; j <= dense; j++)
            {
                float t = 2f * Mathf.PI * j / dense;
                float s = Mathf.Sin(t);
                float z = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                pts[j] = new Vector2(16f * s * s * s, z + 2.5f) / 17f;
                if (j > 0)
                    len[j] = len[j - 1] + (pts[j] - pts[j - 1]).magnitude;
            }
            var result = new Vector2[count];
            int k = 0;
            for (int i = 0; i < count; i++)
            {
                float want = len[dense] * i / count;
                while (k < dense - 1 && len[k + 1] < want)
                    k++;
                float seg = len[k + 1] - len[k];
                result[i] = Vector2.Lerp(pts[k], pts[k + 1], seg > 0f ? (want - len[k]) / seg : 0f);
            }
            return result;
        }

        // 은하 소용돌이: 두 팔이 천천히 돎 (가운데일수록 높게 살짝 볼록)
        private static void Spiral(int count, float time, Vector3[] result)
        {
            int half = (count + 1) / 2;
            for (int i = 0; i < count; i++)
            {
                int arm = i % 2;
                float s = (i / 2) / (float)Mathf.Max(1, half - 1);
                float r = 0.12f + 0.86f * s;
                float ang = arm * Mathf.PI + s * 2.4f * Mathf.PI - time * 0.5f;
                result[i] = new Vector3(Mathf.Cos(ang) * r, 0.08f * (1f - s), Mathf.Sin(ang) * r);
            }
        }

        // 물결: 6 × 5 바둑판이 파도처럼 오르내림
        private static void Wave(int count, float time, Vector3[] result)
        {
            int cols = Mathf.CeilToInt(Mathf.Sqrt(count * 1.2f));
            int rows = Mathf.CeilToInt(count / (float)cols);
            for (int i = 0; i < count; i++)
            {
                int cx = i % cols, cz = i / cols;
                float x = cols > 1 ? Mathf.Lerp(-0.8f, 0.8f, cx / (float)(cols - 1)) : 0f;
                float z = rows > 1 ? Mathf.Lerp(0.62f, -0.62f, cz / (float)(rows - 1)) : 0f;
                result[i] = new Vector3(x, 0.08f * Mathf.Sin(x * 4f + z * 2f - time * 3f), z);
            }
        }
    }
}
