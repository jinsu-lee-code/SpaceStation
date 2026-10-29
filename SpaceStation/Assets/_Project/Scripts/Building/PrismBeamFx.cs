using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 3단계 궤도 방어망 격추 광선 (사용자 2026-10-29 "실드 계열 단계별 이펙트 — 격추 = 프리즘 광선 + 수정 불꽃", StationArtBuilder가 자동 배선):
    /// 운석에 가장 가까운 수정 부표 끝에서 흰 빛줄기가 나가다 프리즘처럼 일곱 빛깔로 갈라져 부채꼴로 벌어졌다가 운석에서 다시 모인다
    /// (면은 카메라를 향함). 맞은 자리는 같은 모듈의 FireworkFx(무지개 모드)가 수정 불꽃으로 터뜨림.
    /// MeteorFx가 포탑 모듈에서 이 컴포넌트를 찾으면 빨간 레이저 대신 Fire. 시간은 게임 시간.
    /// </summary>
    public sealed class PrismBeamFx : MonoBehaviour
    {
        [SerializeField] private Material _lineMaterial; // 가로질러 부드러운 띠 (가산, 색 = 선 색)
        [Tooltip("수정 부표 끝 (모델 기준 위치) — 운석에 가장 가까운 곳에서 쏨")]
        [SerializeField] private Vector3[] _muzzles;
        [SerializeField] private float _duration = 0.32f;
        [Tooltip("흰 빛줄기가 갈라지는 자리 (부표 → 운석 비율)")]
        [SerializeField] private float _splitAt = 0.3f;
        [Tooltip("일곱 빛깔이 가장 넓게 벌어진 폭 (빛줄기 길이 비율, 최대 0.6m)")]
        [SerializeField] private float _fan = 0.06f;

        private const int Points = 12;
        private static readonly Color Core = new Color(1f, 1f, 1f, 1f);
        private static readonly Color[] Rainbow =
        {
            new Color(1f, 0.22f, 0.22f), new Color(1f, 0.58f, 0.12f), new Color(1f, 0.94f, 0.22f), new Color(0.3f, 1f, 0.38f),
            new Color(0.25f, 0.62f, 1f), new Color(0.42f, 0.32f, 1f), new Color(0.82f, 0.38f, 1f),
        };

        /// <summary>한 번의 프리즘 광선 (확인용으로 Begin → Apply(t) → End를 직접 불러도 됨).</summary>
        public sealed class Shot
        {
            public GameObject Go;
            public LineRenderer CoreLine;
            public LineRenderer[] Colors;
            public Vector3 From, To, Side;
        }

        public float Duration => _duration;

        /// <summary>target에 가장 가까운 부표 끝 (월드). 부표가 없으면 모듈 원점 조금 위.</summary>
        public Vector3 Muzzle(Vector3 target)
        {
            if (_muzzles == null || _muzzles.Length == 0)
                return transform.position + Vector3.up * 0.3f;
            Vector3 best = transform.TransformPoint(_muzzles[0]);
            foreach (var m in _muzzles)
            {
                Vector3 p = transform.TransformPoint(m);
                if ((p - target).sqrMagnitude < (best - target).sqrMagnitude)
                    best = p;
            }
            return best;
        }

        /// <summary>가장 가까운 부표에서 target으로 프리즘 광선을 쏘고 그 부표 끝(월드)을 돌려준다.</summary>
        public Vector3 Fire(Vector3 target)
        {
            Vector3 muzzle = Muzzle(target);
            if (_lineMaterial != null && isActiveAndEnabled)
                StartCoroutine(Run(muzzle, target));
            return muzzle;
        }

        private IEnumerator Run(Vector3 from, Vector3 to)
        {
            var shot = Begin(from, to);
            float t = 0f;
            while (t < _duration && shot.Go != null)
            {
                t += Time.deltaTime;
                Apply(shot, t);
                yield return null;
            }
            End(shot);
        }

        public Shot Begin(Vector3 from, Vector3 to)
        {
            var go = new GameObject("PrismBeam");
            go.transform.SetParent(transform, false);
            var cam = Camera.main;
            Vector3 dir = (to - from).normalized;
            Vector3 view = cam != null ? (Vector3.Lerp(from, to, 0.5f) - cam.transform.position).normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(dir, view); // 부채꼴 면이 카메라를 향하게
            if (side.sqrMagnitude < 1e-4f)
                side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f)
                side = Vector3.right;
            var shot = new Shot { Go = go, From = from, To = to, Side = side.normalized, Colors = new LineRenderer[Rainbow.Length] };
            shot.CoreLine = NewLine(go.transform, 2, Core);
            for (int i = 0; i < Rainbow.Length; i++)
                shot.Colors[i] = NewLine(go.transform, Points, Color.Lerp(Rainbow[i], Color.white, 0.15f));
            Apply(shot, 0f);
            return shot;
        }

        public void Apply(Shot shot, float t)
        {
            float k = Mathf.Clamp01(t / _duration);
            float width = Mathf.Clamp01(t / 0.04f) * (1f - k * k); // 켜지자마자 굵고 끝에 가늘어짐
            Vector3 split = Vector3.Lerp(shot.From, shot.To, _splitAt);
            float length = Vector3.Distance(shot.From, shot.To);
            float fan = Mathf.Min(_fan * length, 0.6f);
            shot.CoreLine.SetPosition(0, shot.From);
            shot.CoreLine.SetPosition(1, split);
            shot.CoreLine.widthMultiplier = 0.07f * width;
            for (int i = 0; i < shot.Colors.Length; i++)
            {
                var line = shot.Colors[i];
                float lane = (i - (shot.Colors.Length - 1) * 0.5f) / ((shot.Colors.Length - 1) * 0.5f); // −1 빨강 ~ 1 보라
                for (int p = 0; p < Points; p++)
                {
                    float s = p / (float)(Points - 1);
                    float bulge = Mathf.Sin(s * Mathf.PI); // 갈라져 벌어졌다 운석에서 다시 모임
                    line.SetPosition(p, Vector3.Lerp(split, shot.To, s) + shot.Side * (lane * fan * bulge));
                }
                line.widthMultiplier = 0.035f * width;
            }
        }

        public static void End(Shot shot)
        {
            if (shot.Go == null)
                return;
            if (Application.isPlaying)
                Destroy(shot.Go);
            else
                DestroyImmediate(shot.Go);
        }

        private LineRenderer NewLine(Transform parent, int points, Color color)
        {
            var l = new GameObject("Line").AddComponent<LineRenderer>();
            l.transform.SetParent(parent, false);
            l.useWorldSpace = true;
            l.positionCount = points;
            l.sharedMaterial = _lineMaterial;
            l.startColor = l.endColor = color;
            l.shadowCastingMode = ShadowCastingMode.Off;
            l.receiveShadows = false;
            l.numCapVertices = 2;
            return l;
        }
    }
}
