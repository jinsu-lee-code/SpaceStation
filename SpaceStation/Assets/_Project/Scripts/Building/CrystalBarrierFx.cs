using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 3단계 궤도 방어망 실드 연출 (사용자 2026-10-29 "실드 계열 단계별 이펙트 — 차단 = 수정 결정 막", StationArtBuilder가 자동 배선):
    /// 운석이 실드 구면에 닿으면 그 자리에 육각 수정판이 가운데부터 바깥 고리로 차례로 맺혀(무지개빛 · 맺힐 때 번쩍) 잠깐 버티다가,
    /// 가운데부터 깨져 세모 조각으로 흩어지며 사라진다 (조각이 돌며 퍼지다 멈춤, 무지개 반짝이). 둘레로는 같은 모듈의
    /// AuroraBarrierFx 오로라 파문이 구면을 따라 퍼짐. 수정판 · 조각은 한 메시로 매 프레임 다시 그림 (셰이더 SpaceStation/CrystalShard).
    /// MeteorFx가 실드 모듈에서 이 컴포넌트를 찾으면 파란 파문 · 오로라 파문 대신 Play. 시간은 게임 시간.
    /// </summary>
    public sealed class CrystalBarrierFx : MonoBehaviour
    {
        [SerializeField] private Material _material;       // 수정판 · 조각 (SpaceStation/CrystalShard)
        [SerializeField] private Material _sparkMaterial;  // 무지개 반짝이 (가산 빛 점, 색은 입자 색)
        [Tooltip("육각 수정판 하나의 크기 (가운데 → 꼭짓점, m)")]
        [SerializeField] private float _plateSize = 0.3f;
        [Tooltip("가운데 판을 둘러싼 고리 수 (2 = 판 19장)")]
        [SerializeField] private int _rings = 2;
        [Tooltip("고리마다 늦게 맺히는 시간")]
        [SerializeField] private float _growStep = 0.06f;
        [SerializeField] private float _growTime = 0.22f;
        [Tooltip("다 맺힌 뒤 버티는 시간")]
        [SerializeField] private float _hold = 0.35f;
        [Tooltip("깨진 조각이 흩어져 사라지는 시간")]
        [SerializeField] private float _shatterTime = 0.8f;

        private const float ShatterStep = 0.04f; // 고리마다 늦게 깨짐 (가운데부터)
        private const float HalfThick = 0.09f;   // 판 두께 절반 (판 크기 기준)
        private const float Drag = 2.5f;         // 조각이 퍼지다 멈추는 정도

        private static readonly Color[] Rainbow =
        {
            new Color(1f, 0.22f, 0.22f), new Color(1f, 0.58f, 0.12f), new Color(1f, 0.94f, 0.22f), new Color(0.3f, 1f, 0.38f),
            new Color(0.25f, 0.62f, 1f), new Color(0.42f, 0.32f, 1f), new Color(0.82f, 0.38f, 1f),
        };

        private static readonly Vector2[] Corners = HexCorners();
        private static readonly float[] CornerEdges = { 1f, 1f, 1f, 1f, 1f, 1f };
        private static readonly float[] ShardEdges = { 0f, 1f, 1f };

        public struct Plate
        {
            public Vector3 Position;
            public Quaternion Rotation; // 앞(z) = 구 바깥, 판은 xy 평면
            public int Ring;
            public Color Tint;
        }

        public struct Shard
        {
            public Vector3 Velocity;
            public Vector3 Axis;
            public float Spin;
        }

        /// <summary>한 번의 수정 결정 막 (확인용으로 Begin → Apply(t) → End를 직접 불러도 됨).</summary>
        public sealed class Shot
        {
            public GameObject Go;
            public Mesh Mesh;
            public Plate[] Plates;
            public Shard[] Shards; // 판마다 6조각 (판 i의 k번째 = i * 6 + k)
            public bool Shattered;
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<Vector3> Normals = new List<Vector3>();
            internal readonly List<Color> Colors = new List<Color>();
            internal readonly List<Vector2> Uvs = new List<Vector2>();
            internal readonly List<int> Triangles = new List<int>();
        }

        private float ShatterAt => _rings * _growStep + _growTime + _hold;
        public float Duration => ShatterAt + _rings * ShatterStep + _shatterTime;

        /// <summary>실드 구면(center, radius)의 contact 자리에 수정 결정 막을 한 번 맺었다 깨뜨린다 (같은 모듈의 오로라 파문도 함께).</summary>
        public void Play(Vector3 center, float radius, Vector3 contact)
        {
            if (_material == null || !isActiveAndEnabled)
                return;
            var aurora = GetComponent<AuroraBarrierFx>();
            if (aurora != null)
                aurora.Play(center, radius, contact);
            StartCoroutine(Run(center, radius, contact));
        }

        private IEnumerator Run(Vector3 center, float radius, Vector3 contact)
        {
            var shot = Begin(center, radius, contact);
            Sparkle(contact, 14, 1.4f, true);
            float t = 0f;
            float duration = Duration;
            while (t < duration && shot.Go != null)
            {
                t += Time.deltaTime;
                if (!shot.Shattered && t >= ShatterAt)
                {
                    shot.Shattered = true;
                    foreach (var p in shot.Plates)
                        Sparkle(p.Position, 2, 0.9f, false);
                }
                Apply(shot, t);
                yield return null;
            }
            End(shot);
        }

        public Shot Begin(Vector3 center, float radius, Vector3 contact)
        {
            var go = new GameObject("CrystalBarrier");
            go.transform.SetParent(transform, false); // 모듈이 사라지면 함께 사라짐 (꼭짓점은 월드 → 이 물체 기준으로 바꿔 넣음)
            var mesh = new Mesh { name = "CrystalBarrier" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            Vector3 hit = (contact - center).normalized;
            if (hit.sqrMagnitude < 1e-6f)
                hit = Vector3.up;
            Vector3 t2 = Vector3.ProjectOnPlane(Vector3.up, hit);
            if (t2.sqrMagnitude < 1e-4f)
                t2 = Vector3.ProjectOnPlane(Vector3.forward, hit);
            t2.Normalize();
            Vector3 t1 = Vector3.Cross(t2, hit); // LookRotation(hit, t2)의 오른쪽 = 판 x축

            var plates = new List<Plate>();
            for (int q = -_rings; q <= _rings; q++)
            {
                for (int s = -_rings; s <= _rings; s++)
                {
                    int ring = Mathf.Max(Mathf.Abs(q), Mathf.Max(Mathf.Abs(s), Mathf.Abs(q + s)));
                    if (ring > _rings)
                        continue;
                    // 꼭짓점이 위아래인 육각 격자 (판 사이 틈 = 판을 조금 작게 그려서)
                    var p = new Vector2(Mathf.Sqrt(3f) * (q + s * 0.5f), 1.5f * s) * _plateSize;
                    Vector3 dir = (hit * radius + t1 * p.x + t2 * p.y).normalized;
                    plates.Add(new Plate
                    {
                        Position = center + dir * (radius + 0.02f),
                        Rotation = Quaternion.LookRotation(dir, Vector3.ProjectOnPlane(t2, dir)),
                        Ring = ring,
                        Tint = Color.Lerp(Color.white, Rainbow[Random.Range(0, Rainbow.Length)], 0.3f),
                    });
                }
            }
            var shards = new Shard[plates.Count * 6];
            float speed = _plateSize / 0.3f;
            for (int i = 0; i < plates.Count; i++)
            {
                for (int k = 0; k < 6; k++)
                {
                    Vector2 g = (Corners[k] + Corners[(k + 1) % 6]) / 3f;
                    Vector3 outward = plates[i].Rotation * Vector3.forward;
                    Vector3 radial = plates[i].Rotation * new Vector3(g.x, g.y, 0f).normalized;
                    shards[i * 6 + k] = new Shard
                    {
                        Velocity = (outward * Random.Range(0.5f, 1.2f) + radial * Random.Range(0.7f, 1.4f) + Random.insideUnitSphere * 0.25f) * speed,
                        Axis = Random.onUnitSphere,
                        Spin = Random.Range(180f, 540f),
                    };
                }
            }
            var shot = new Shot { Go = go, Mesh = mesh, Plates = plates.ToArray(), Shards = shards };
            Apply(shot, 0f);
            return shot;
        }

        public void Apply(Shot shot, float t)
        {
            shot.Vertices.Clear();
            shot.Normals.Clear();
            shot.Colors.Clear();
            shot.Uvs.Clear();
            shot.Triangles.Clear();
            float shatterAt = ShatterAt;
            var shardPts = new Vector2[3];
            for (int i = 0; i < shot.Plates.Length; i++)
            {
                var plate = shot.Plates[i];
                float born = plate.Ring * _growStep;
                float broken = shatterAt + plate.Ring * ShatterStep;
                if (t < broken)
                {
                    float k = Mathf.Clamp01((t - born) / _growTime);
                    if (k <= 0f)
                        continue;
                    float flash = Mathf.Max(Mathf.Exp(-(t - born) * 7f), Smooth(broken - 0.12f, broken, t) * 0.8f); // 맺힐 때 · 깨지기 직전 번쩍
                    AddPrism(shot, plate.Position, plate.Rotation, EaseOutBack(k) * _plateSize * 0.92f, Corners, CornerEdges, true, plate.Tint, 1f, flash);
                    continue;
                }
                float u = t - broken;
                if (u > _shatterTime)
                    continue;
                float fade = 1f - Smooth(0.25f * _shatterTime, _shatterTime, u);
                float shrink = Mathf.Lerp(1f, 0.55f, u / _shatterTime);
                float travel = (1f - Mathf.Exp(-Drag * u)) / Drag;
                float shardFlash = Mathf.Exp(-u * 8f) * 0.8f;
                for (int k = 0; k < 6; k++)
                {
                    var shard = shot.Shards[i * 6 + k];
                    Vector2 a = Corners[k], b = Corners[(k + 1) % 6];
                    Vector2 g = (a + b) / 3f;
                    shardPts[0] = -g;
                    shardPts[1] = a - g;
                    shardPts[2] = b - g;
                    Vector3 pos = plate.Position + plate.Rotation * new Vector3(g.x, g.y, 0f) * (_plateSize * 0.92f) + shard.Velocity * travel;
                    Quaternion rot = Quaternion.AngleAxis(shard.Spin * u, shard.Axis) * plate.Rotation;
                    AddPrism(shot, pos, rot, _plateSize * 0.92f * shrink, shardPts, ShardEdges, false, plate.Tint, fade, shardFlash);
                }
            }
            var toLocal = shot.Go.transform.worldToLocalMatrix;
            for (int v = 0; v < shot.Vertices.Count; v++)
            {
                shot.Vertices[v] = toLocal.MultiplyPoint3x4(shot.Vertices[v]);
                shot.Normals[v] = toLocal.MultiplyVector(shot.Normals[v]).normalized;
            }
            shot.Mesh.Clear();
            shot.Mesh.SetVertices(shot.Vertices);
            shot.Mesh.SetNormals(shot.Normals);
            shot.Mesh.SetColors(shot.Colors);
            shot.Mesh.SetUVs(0, shot.Uvs);
            shot.Mesh.SetTriangles(shot.Triangles, 0);
        }

        public static void End(Shot shot)
        {
            if (Application.isPlaying)
            {
                if (shot.Go != null)
                    Destroy(shot.Go);
                Destroy(shot.Mesh);
                return;
            }
            if (shot.Go != null)
                DestroyImmediate(shot.Go);
            DestroyImmediate(shot.Mesh);
        }

        /// <summary>
        /// 얇은 각기둥 하나 (판 기준 xy 평면 다각형 pts × 두께, 면마다 따로 꼭짓점 — 모서리가 또렷하게).
        /// edges = 꼭짓점마다 가장자리 정도 (uv.x), withCenter = 위 · 아래 면을 가운데 점에서 부채꼴로 (가운데는 가장자리 0).
        /// </summary>
        private static void AddPrism(Shot shot, Vector3 pos, Quaternion rot, float scale, Vector2[] pts, float[] edges, bool withCenter,
            Color tint, float alpha, float flash)
        {
            var color = new Color(tint.r, tint.g, tint.b, alpha);
            Vector3 up = rot * Vector3.forward;
            Vector2 mid = Vector2.zero;
            foreach (var p in pts)
                mid += p;
            mid /= pts.Length;
            for (int face = 0; face < 2; face++)
            {
                float z = face == 0 ? HalfThick : -HalfThick;
                Vector3 n = face == 0 ? up : -up;
                int start = shot.Vertices.Count;
                if (withCenter)
                    AddVertex(shot, pos + rot * new Vector3(0f, 0f, z) * scale, n, color, 0f, flash);
                for (int i = 0; i < pts.Length; i++)
                    AddVertex(shot, pos + rot * new Vector3(pts[i].x, pts[i].y, z) * scale, n, color, edges[i], flash);
                if (withCenter)
                {
                    for (int i = 0; i < pts.Length; i++)
                        AddTriangle(shot, start, start + 1 + i, start + 1 + (i + 1) % pts.Length);
                }
                else
                {
                    for (int i = 1; i < pts.Length - 1; i++)
                        AddTriangle(shot, start, start + i, start + i + 1);
                }
            }
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % pts.Length];
                Vector2 side = new Vector2(b.y - a.y, a.x - b.x).normalized;
                if (Vector2.Dot(side, (a + b) * 0.5f - mid) < 0f)
                    side = -side;
                Vector3 n = rot * new Vector3(side.x, side.y, 0f);
                int start = shot.Vertices.Count;
                AddVertex(shot, pos + rot * new Vector3(a.x, a.y, HalfThick) * scale, n, color, 1f, flash);
                AddVertex(shot, pos + rot * new Vector3(b.x, b.y, HalfThick) * scale, n, color, 1f, flash);
                AddVertex(shot, pos + rot * new Vector3(b.x, b.y, -HalfThick) * scale, n, color, 1f, flash);
                AddVertex(shot, pos + rot * new Vector3(a.x, a.y, -HalfThick) * scale, n, color, 1f, flash);
                AddTriangle(shot, start, start + 1, start + 2);
                AddTriangle(shot, start, start + 2, start + 3);
            }
        }

        private static void AddVertex(Shot shot, Vector3 position, Vector3 normal, Color color, float edge, float flash)
        {
            shot.Vertices.Add(position);
            shot.Normals.Add(normal);
            shot.Colors.Add(color);
            shot.Uvs.Add(new Vector2(edge, flash));
        }

        private static void AddTriangle(Shot shot, int a, int b, int c)
        {
            shot.Triangles.Add(a);
            shot.Triangles.Add(b);
            shot.Triangles.Add(c);
        }

        /// <summary>무지개 반짝이 (flash = 닿은 자리 흰 섬광 함께). 스스로 지워지는 입자 묶음, 모듈 아래.</summary>
        private void Sparkle(Vector3 at, int count, float speed, bool flash)
        {
            if (_sparkMaterial == null)
                return;
            var go = new GameObject("CrystalSparkle");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.1f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.maxParticles = 100;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = 2f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _sparkMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            if (flash)
                ps.Emit(new ParticleSystem.EmitParams { position = at, startLifetime = 0.25f, startSize = _plateSize * 3f, startColor = Color.white, applyShapeToPosition = false }, 1);
            for (int i = 0; i < count; i++)
            {
                ps.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = Random.onUnitSphere * speed * Random.Range(0.5f, 1f),
                    startLifetime = Random.Range(0.6f, 1.1f),
                    startSize = Random.Range(0.03f, 0.06f),
                    startColor = Color.Lerp(Rainbow[Random.Range(0, Rainbow.Length)], Color.white, 0.25f),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        private static Vector2[] HexCorners()
        {
            var c = new Vector2[6];
            for (int k = 0; k < 6; k++)
            {
                float a = Mathf.Deg2Rad * (30f + 60f * k); // 꼭짓점이 위아래 (90° 포함)
                c[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
            return c;
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float m = x - 1f;
            return 1f + c3 * m * m * m + c1 * m * m;
        }

        private static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));
    }
}
