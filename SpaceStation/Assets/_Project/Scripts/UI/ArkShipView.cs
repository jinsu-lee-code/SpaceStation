using System;
using System.Collections;
using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 13-6b 이민선 모습 + 처음 만나는 종족의 도킹 연출 (사용자 결정 2026-10-19). 화면 연출만 — 시뮬레이션은 바꾸지 않는다.
    /// - 모든 배: 도착 전 접근(남은 게임 시간으로 위치, 절정 · 전환 중엔 안 보임 — 13-6c 대이주 배는 절정 중에도) → 도착 = 도킹(<see cref="ArkDocking"/>: 여객 도킹 포트 입구, 없으면 코어 쪽 바깥 끝)
    ///   → 받으면 잠시 머물다 떠남, 거부하면 뒤로 물러나 돌아 떠남. 몸통 띠 = 종족 색
    /// - 처음 만나는 종족의 배: 도착 순간 게임을 멈추고 레터박스 → 배를 따라가는 카메라 → 포트 근처에서 포트 쪽 고정 시점 → 도킹 → 코어 독백 2~3줄
    ///   → 원래 시점으로 돌아오며 도착 창. 답한 뒤에도 짧은 장면(받으면 입구에 불이 켜짐 · 거부하면 배가 돌아 떠남). ESC = 건너뛰기
    /// 내용은 코드로 만든다 (씬에는 글꼴 · 스프라이트 · 테마 아트 · 배 모델만 연결된 빈 오브젝트, 메뉴 SpaceStation/Chapters/Setup Greybox).
    /// </summary>
    [DefaultExecutionOrder(-240)]
    public sealed class ArkShipView : MonoBehaviour
    {
        private const string CoreTint = "#9FE8FF";
        private const string Soft = "#C8D2DE";

        [SerializeField] private StationController _station;
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [SerializeField] private HoloArt _holoArt;
        [Tooltip("이민선 모델 (BlenderWork/ArkShip → SM_ArkShip.fbx, 원점 = 코 도킹 고리 끝, 코 = +Z)")]
        [SerializeField] private GameObject _shipModel;
        [Tooltip("배 길이 (칸)")]
        [SerializeField] private float _shipLength = 2.2f;
        [Tooltip("추진 불꽃 (가산 파티클 재질, 원뿔 정점 색이 안쪽 흰빛 → 바깥 푸른빛 · 끝으로 흐려짐)")]
        [SerializeField] private Material _flameMaterial;
        [Tooltip("불꽃 최대 길이 (모델 단위 — 배 길이 약 4.9)")]
        [SerializeField] private float _flameLength = 1.9f;
        [Header("평소 배 (게임 초)")]
        [SerializeField] private float _approachSeconds = 12f;
        [SerializeField] private float _stayAfterAccept = 4f;
        [SerializeField] private float _leaveSeconds = 8f;
        [Header("처음 만나는 종족 연출 (실제 초)")]
        [SerializeField] private float _followSeconds = 6f;
        [SerializeField] private float _dockSeconds = 4f;
        [SerializeField] private float _returnSeconds = 1.2f;
        [SerializeField] private float _charsPerSecond = 26f;
        [Tooltip("독백 동안 고정 시점에서 보는 곳 쪽으로 다가가는 비율 (0 = 그대로, 방향은 바뀌지 않음)")]
        [Range(0f, 0.7f)] [SerializeField] private float _monologuePush = 0.4f;

        private enum Mode { None, Approach, Docked, Leaving }

        private StationSimulation _sim;
        private ArkShipSystem _ark;
        private OrbitCameraController _orbit;
        private HoloUi _ui;
        private Transform _root;
        private GameObject _ship;
        private ArkShip _shipData;
        private Mode _mode;
        private float _stayLeft, _leaveT;
        private Vector3 _nose, _out;
        // 길 · 카메라 자리 (모듈에 막히지 않는 쪽, ArkShot): 접근 · 떠나는 길이 휘는 쪽(Side 배수), 따라가는 카메라 후보 번호
        private float _approachSide = 1f, _leaveSide = -1f;
        private int _follow;
        private float _scale = 1f;
        private MaterialPropertyBlock _block; // MonoBehaviour 필드 초기화에서 만들면 Unity가 막음 → Start에서

        // 추진 불꽃 (노즐마다 원뿔 두 겹 + 선체를 비추는 푸른 불빛), 추력 0 ~ 1
        private const float NozzleX = 0.24f; // BlenderWork/ArkShip/arkship_builder.py 노즐 위치
        private readonly List<(Transform flame, float seed)> _flames = new List<(Transform, float)>();
        private Light _engineLight;
        private float _thrust, _thrustTarget;
        private static Mesh _coneInner, _coneOuter;

        // 연출
        private CinematicStage _stage; // 레터박스 · 자막 · HUD 감추기 · 시간 멈춤 · 카메라 복귀 (13-7b 장 전환 연출과 같은 무대)
        private ArkShip _sceneShip;

        /// <summary>연출 중 (도착 창은 끝난 뒤에).</summary>
        public bool Playing => _stage != null && _stage.Active;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            _ark = _sim?.GetSystem<ArkShipSystem>();
            if (_ark == null || _shipModel == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _orbit = FindFirstObjectByType<OrbitCameraController>();
            _block = new MaterialPropertyBlock();
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            _root = new GameObject("ArkShips").transform;
            _ark.Resolved += HandleResolved;
            _stage = new CinematicStage((RectTransform)transform, _ui, _clock, _orbit);
        }

        private void OnDestroy()
        {
            if (_ark != null)
                _ark.Resolved -= HandleResolved;
            if (_root != null)
                Destroy(_root.gameObject);
        }

        // ---------------- 평소 배 ----------------

        private float SimDelta => _clock == null || _clock.Clock.IsPaused ? 0f : Time.deltaTime * _clock.Clock.Speed;

        private void Update()
        {
            if (_ark == null || Playing)
                return;
            float dt = SimDelta;
            var pending = _ark.Pending;
            if (pending != null)
            {
                if (_mode != Mode.Docked || _shipData != pending)
                    DockNow(pending);
                return;
            }
            if (_mode == Mode.Docked && _shipData != null)
            {
                _stayLeft -= dt;
                if (_stayLeft <= 0f)
                    BeginLeave();
                return;
            }
            if (_mode == Mode.Leaving)
            {
                _leaveT += dt / Mathf.Max(0.1f, _leaveSeconds);
                PlaceLeaving(_leaveT);
                if (_leaveT >= 1f)
                    Clear();
                return;
            }
            // 예보된 배가 도착 전 날아옴 (처음 만나는 종족은 연출이 보여 줌, 절정 · 전환 중엔 시계가 멈춰 안 보임)
            var next = _ark.NextShip;
            var phase = _sim.Chapters != null ? _sim.Chapters.Phase : ChapterPhase.Preparation;
            float left = SmoothTimeLeft();
            bool showing = next != null && !next.NewSpecies && _ark.ClockOn && left <= _approachSeconds
                           && (phase == ChapterPhase.Preparation || phase == ChapterPhase.Warning || next.Convoy) && FindDock(); // 13-6c 대이주 배는 절정 중에도
            if (showing)
            {
                if (_mode != Mode.Approach || _shipData != next)
                {
                    Spawn(next);
                    _mode = Mode.Approach;
                }
                PlaceApproach(Ease(1f - Mathf.Clamp01(left / _approachSeconds)));
            }
            else if (_mode == Mode.Approach)
                Clear();
        }

        /// <summary>
        /// 다음 배까지 남은 게임 시간을 틱 사이에서도 매끄럽게: 시계는 1초 틱마다만 줄어 접근하는 배가 1초에 한 번씩 뚝뚝 옮겨졌음
        /// (사용자 피드백 2026-10-20, 대이주) → 마지막 틱 뒤 흐른 시간(틱 진행률)만큼 더 뺌. 이 뷰는 시계보다 먼저 돌아 둘 다 지난 프레임 끝 값.
        /// </summary>
        private float SmoothTimeLeft()
        {
            float left = _ark.TimeLeft;
            if (_clock == null || _clock.Clock == null)
                return left;
            var clock = _clock.Clock;
            float since = clock.Progress * clock.TickInterval * (_ark.Slowed ? _ark.Config.CrisisClockScale : 1f);
            return Mathf.Max(0f, left - since);
        }

        private void HandleResolved(ArkShip ship, ShipAnswer answer, int boarded)
        {
            if (ship == null)
                return;
            if (ship == _sceneShip)
            {
                _sceneShip = null;
                _stayLeft = float.PositiveInfinity; // 마무리 장면이 시작될 때까지(0.4초) 붙어 있게 — 먼저 떠나기 시작해 받은 배에도 불꽃이 켜졌음
                StartCoroutine(Aftermath(ship, boarded));
                return;
            }
            if (_shipData != ship)
                return;
            if (boarded > 0)
            {
                _mode = Mode.Docked;
                _stayLeft = _stayAfterAccept;
            }
            else
                BeginLeave();
        }

        private bool FindDock() => ArkDocking.Find(_sim.Grid, _sim.Core, m => _station.Connectivity.IsActive(m), out _nose, out _out);

        private Vector3 Side => Vector3.Cross(Vector3.up, _out).normalized;

        private Quaternion DockRotation => Quaternion.LookRotation(-_out, Vector3.up);

        private void DockNow(ArkShip ship)
        {
            if (!FindDock())
                return;
            if (_shipData != ship || _ship == null)
                Spawn(ship);
            _mode = Mode.Docked;
            _ship.transform.SetPositionAndRotation(_nose, DockRotation);
            _stayLeft = _stayAfterAccept;
            _thrustTarget = IdleThrust;
        }

        private void Spawn(ArkShip ship)
        {
            Clear();
            _shipData = ship;
            PlanPaths();
            _ship = Instantiate(_shipModel, _root);
            _ship.name = "ArkShip";
            float length = 0f;
            var renderers = _ship.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                length = Mathf.Max(length, r.bounds.size.z);
            }
            _scale = length > 0.01f ? _shipLength * GridConfig.CellSize / length : 1f;
            _ship.transform.localScale = Vector3.one * _scale;
            var species = _ark.Find(ship.Species);
            var color = species != null ? species.ShipColor : Color.white;
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].name.StartsWith("M_ArkBand"))
                        continue;
                    r.GetPropertyBlock(_block, i);
                    _block.SetColor("_BaseColor", color);
                    r.SetPropertyBlock(_block, i);
                }
            }
            _thrustTarget = 1f; // 날아오는 배는 엔진을 켠 채로 (붙은 채 불러온 배는 DockNow가 바로 줄임)
            AddFlames(renderers);
        }

        private void Clear()
        {
            if (_ship != null)
                Destroy(_ship);
            _ship = null;
            _shipData = null;
            _mode = Mode.None;
            _flames.Clear();
            _engineLight = null;
        }

        // ---------------- 추진 불꽃 ----------------

        private const float IdleThrust = 0f; // 붙어 있는 동안은 엔진을 끔 (작게 남긴 불꽃이 납작한 조각처럼 보였음 — 노즐 안 발광만 남음)
        private static Mesh _coneHalo;

        /// <summary>노즐 끝마다 불꽃(안쪽 흰빛 · 가운데 푸른빛 · 바깥 옅은 빛 원뿔 세 겹, 뒤 = -Z) + 그 사이 푸른 불빛. 모델 단위로 붙여 배 크기를 따라감.</summary>
        private void AddFlames(Renderer[] renderers)
        {
            if (_flameMaterial == null)
                return;
            float back = 0f;
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    back = Mathf.Min(back, mf.sharedMesh.bounds.min.z); // 노즐 끝 (모델 맨 뒤)
            }
            // 가산 재질은 옆에서 보면 원뿔 테두리가 딱딱해 보임 → 바깥으로 갈수록 옅은 겹을 둘러 가장자리를 흐림
            _coneInner = _coneInner != null ? _coneInner : Cone(0.15f, 0.6f, new Color(1f, 1f, 1f, 1f), new Color(0.75f, 0.9f, 1f, 0f));
            _coneOuter = _coneOuter != null ? _coneOuter : Cone(0.21f, 1f, new Color(0.45f, 0.7f, 1f, 0.5f), new Color(0.2f, 0.4f, 1f, 0f));
            _coneHalo = _coneHalo != null ? _coneHalo : Cone(0.29f, 0.8f, new Color(0.3f, 0.5f, 1f, 0.22f), new Color(0.15f, 0.3f, 1f, 0f));
            int seed = 0;
            foreach (var x in new[] { -NozzleX, NozzleX })
            {
                var flame = new GameObject("Flame").transform;
                flame.SetParent(_ship.transform, false);
                flame.localPosition = new Vector3(x, 0f, back + 0.04f); // 노즐 입구 안쪽에서 시작
                foreach (var mesh in new[] { _coneHalo, _coneOuter, _coneInner })
                {
                    var go = new GameObject("Cone");
                    go.transform.SetParent(flame, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _flameMaterial;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
                _flames.Add((flame, 17.3f * ++seed));
            }
            var lightGo = new GameObject("EngineLight");
            lightGo.transform.SetParent(_ship.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, back - 0.5f);
            _engineLight = lightGo.AddComponent<Light>();
            _engineLight.type = LightType.Point;
            _engineLight.color = new Color(0.45f, 0.72f, 1f);
            _engineLight.range = 2.2f;
            _engineLight.shadows = LightShadows.None;
            _thrust = _thrustTarget;
            UpdateFlames(0f);
        }

        /// <summary>추력을 목표로 부드럽게 옮기고 불꽃 길이 · 굵기를 일렁이게 (실제 시간 — 멈춘 연출 중에도).</summary>
        private void UpdateFlames(float dt)
        {
            if (_ship == null || _flames.Count == 0)
                return;
            _thrust = Mathf.MoveTowards(_thrust, _thrustTarget, dt * 1.6f);
            float time = Time.unscaledTime;
            bool on = _thrust > 0.03f;
            foreach (var (flame, seed) in _flames)
            {
                if (flame.gameObject.activeSelf != on)
                    flame.gameObject.SetActive(on);
                float n = Mathf.PerlinNoise(time * 9f, seed);
                float len = _flameLength * _thrust * (0.82f + 0.36f * n);
                float wide = 0.5f + 0.6f * _thrust + 0.08f * Mathf.PerlinNoise(seed, time * 13f); // 약할 때는 가늘게 (짧고 넓으면 납작한 조각처럼 보임)
                flame.localScale = new Vector3(wide, wide, len);
            }
            if (_engineLight != null)
                _engineLight.intensity = 3.4f * _thrust * (0.9f + 0.2f * Mathf.PerlinNoise(time * 7f, 3.1f));
        }

        /// <summary>뒤(-Z)로 뻗는 원뿔 (길이 1 기준, 정점 색 = 밑동 → 끝으로 흐려짐, 가산 재질이 곱함).</summary>
        private static Mesh Cone(float radius, float length, Color baseColor, Color tipColor)
        {
            const int seg = 18, rings = 8;
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float f = (float)r / rings;
                float rad = radius * (1f - f) * (1f + 0.35f * Mathf.Sin(f * Mathf.PI)); // 밑동 뒤에서 살짝 부풀었다 끝으로 좁아짐
                var c = Color.Lerp(baseColor, tipColor, Mathf.Pow(f, 0.8f));
                for (int s = 0; s < seg; s++)
                {
                    float a = 2f * Mathf.PI * s / seg;
                    verts.Add(new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad, -length * f));
                    colors.Add(c);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * seg + s, b = r * seg + (s + 1) % seg, c = a + seg, d = b + seg;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            var mesh = new Mesh { name = "ArkFlameCone" };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void BeginLeave()
        {
            if (_ship == null)
            {
                Clear();
                return;
            }
            _mode = Mode.Leaving;
            _leaveT = 0f;
        }

        private static float Ease(float t) => 1f - (1f - t) * (1f - t);
        private static float Smooth(float t) => t * t * (3f - 2f * t);

        private bool Occupied(Vector3Int cell) => _sim.Grid.IsOccupied(cell);

        /// <summary>배가 나타날 때: 접근 · 떠나는 길이 모듈을 덜 지나는 쪽으로 (기본 = 접근은 Side 쪽, 떠날 때는 반대쪽).</summary>
        private void PlanPaths()
        {
            _approachSide = 1f;
            _leaveSide = -1f;
            if (!FindDock())
                return;
            _approachSide = ArkShot.PickSide(Occupied, side => t => ArkShot.Approach(_nose, _out, side, t), Side);
            _leaveSide = -_approachSide * ArkShot.PickSide(Occupied, side => e => ArkShot.Leave(_nose, _out, side, e), -Side * _approachSide);
        }

        /// <summary>접근 경로 (멀리 옆 위 → 접근로 → 코 = 도킹 점), t = 0 ~ 1.</summary>
        private Vector3 ApproachPoint(float t) => ArkShot.Approach(_nose, _out, Side * _approachSide, t);

        private void PlaceApproach(float t)
        {
            if (_ship == null || !FindDock())
                return;
            var p = ApproachPoint(t);
            var ahead = ApproachPoint(Mathf.Min(1f, t + 0.01f)) - p;
            var rot = t >= 0.999f || ahead.sqrMagnitude < 1e-6f ? DockRotation : Quaternion.Slerp(Quaternion.LookRotation(ahead.normalized, Vector3.up), DockRotation, Smooth(Mathf.InverseLerp(0.8f, 1f, t)));
            _ship.transform.SetPositionAndRotation(p, rot);
            // 날아올 때 최대 추력 → 마지막 20%에서 줄이며 붙음 → 붙으면 약하게
            _thrustTarget = t >= 0.999f ? IdleThrust : t < 0.8f ? 1f : Mathf.Lerp(1f, 0.2f, (t - 0.8f) / 0.2f);
        }

        /// <summary>떠나는 길: 뒤로 물러남(25%) → 돌아서 옆 위로 멀어짐.</summary>
        private void PlaceLeaving(float t)
        {
            if (_ship == null)
                return;
            var up = Vector3.up;
            var back = _nose + _out * 2.5f;
            _thrustTarget = t < 0.25f ? 0.35f : 1f; // 물러날 때 약하게 → 돌아서 최대 추력
            if (t < 0.25f)
            {
                _ship.transform.SetPositionAndRotation(Vector3.Lerp(_nose, back, Smooth(t / 0.25f)), DockRotation);
                return;
            }
            float u = (t - 0.25f) / 0.75f;
            float e = Mathf.Pow(u, 1.6f);
            var side = Side * _leaveSide;
            var p = ArkShot.Leave(_nose, _out, side, e);
            var ahead = ArkShot.Leave(_nose, _out, side, Mathf.Min(1f, e + 0.01f)) - p;
            var travel = ahead.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(ahead.normalized, up) : DockRotation;
            _ship.transform.SetPositionAndRotation(p, Quaternion.Slerp(DockRotation, travel, Smooth(Mathf.Clamp01(u / 0.35f))));
        }

        // ---------------- 처음 만나는 종족 연출 ----------------

        private static float Dt => CinematicStage.Dt;

        /// <summary>이 배의 도착을 연출로 보일지 (처음 만나는 종족 · 붙을 자리가 있을 때).</summary>
        public bool WantsArrival(ArkShip ship) => isActiveAndEnabled && _ark != null && ship != null && ship.NewSpecies && !Playing && FindDock();

        /// <summary>도착 연출 (게임 멈춤) → 끝나면 done (도착 창).</summary>
        public void PlayArrival(ArkShip ship, Action done) => StartCoroutine(Arrival(ship, done));

        private IEnumerator Arrival(ArkShip ship, Action done)
        {
            _stage.Begin(this);
            _sceneShip = ship;
            Spawn(ship);
            _mode = Mode.Docked;
            var cam = _orbit.transform;
            float shipT = 0f;
            // 1) 배를 따라가는 카메라 (뒤 · 왼쪽 위에서 정거장 쪽을 봄 — 그 자리가 모듈에 막히거나 배가 가려지면 오른쪽 · 더 높이)
            _follow = ArkShot.PickFollow(Occupied, ApproachPoint, CutT, ShipLength);
            for (float k = 0f; k < 1f && !_stage.Skip; k += Dt / _followSeconds)
            {
                shipT = CutT * Smooth(Mathf.Clamp01(k));
                PlaceApproach(shipT);
                cam.SetPositionAndRotation(FollowPosition(), Quaternion.LookRotation(Vector3.Lerp(_ship.transform.position, _nose, 0.35f) - FollowPosition(), Vector3.up));
                yield return null;
            }
            // 2) 포트 쪽 고정 시점으로 순간 전환(컷) → 천천히 도킹 (사용자 피드백: 미끄러지듯 옮겨 가는 전환이 어색함)
            var fixedPos = FixedCameraPosition();
            for (float k = 0f; k < 1f && !_stage.Skip; k += Dt / _dockSeconds)
            {
                PlaceApproach(Mathf.Lerp(CutT, 1f, Ease(Mathf.Clamp01(k))));
                cam.SetPositionAndRotation(fixedPos, Quaternion.LookRotation(Vector3.Lerp(_nose, ShipCenter, DockLook) - fixedPos, Vector3.up));
                yield return null;
            }
            PlaceApproach(1f);
            if (!_stage.Skip)
                AudioService.TryPlay(l => l.BuildSelect);
            var lookAt = Vector3.Lerp(_nose, ShipCenter, DockLook);
            var hold = Quaternion.LookRotation(lookAt - fixedPos, Vector3.up);
            cam.SetPositionAndRotation(fixedPos, hold);
            // 3) 코어 독백 (코어 기록에도) — 그동안 방향은 그대로 둔 채 보는 곳 쪽으로 천천히 다가감 (사용자 요청: 고정 시점이 멀어 배가 작게 보임)
            if (!_stage.Skip)
            {
                var lines = ArrivalLines(ship);
                var pushTo = ArkShot.PushIn(Occupied, fixedPos, lookAt, _monologuePush);
                float total = CinematicStage.SaySeconds(lines, _charsPerSecond), t = 0f;
                var core = _sim.GetSystem<CorePersonalitySystem>();
                var talk = _stage.Say("코어", CoreTint, lines, _charsPerSecond, line => core?.Log(line));
                while (talk.MoveNext())
                {
                    t += Dt;
                    cam.position = Vector3.Lerp(fixedPos, pushTo, Smooth(Mathf.Clamp01(t / total)));
                    yield return talk.Current;
                }
            }
            _stage.ClearText();
            // 4) 원래 시점으로 돌아오며 도착 창
            yield return _stage.ReturnCamera(_returnSeconds);
            _stage.End();
            done?.Invoke();
        }

        private IEnumerator Aftermath(ArkShip ship, int boarded)
        {
            yield return new WaitForSecondsRealtime(0.4f); // 도착 창이 다 닫히고(글리치 효과 포함) 시간이 다시 흐른 뒤
            if (!FindDock() || _ship == null)
            {
                _stayLeft = boarded > 0 ? _stayAfterAccept : 0f; // 장면 없이 평소 흐름대로 (머물다 떠남)
                yield break;
            }
            _stage.Begin(this);
            var cam = _orbit.transform;
            var fixedPos = FixedCameraPosition();
            Vector3 fromPos = cam.position;
            Quaternion fromRot = cam.rotation;
            string name = _ark.SpeciesName(ship.Species);
            GameObject glow = null;
            if (boarded > 0)
            {
                // 받음: 입구에 따뜻한 불이 켜짐
                glow = new GameObject("ArkHatchLight");
                glow.transform.SetParent(_root, false);
                glow.transform.position = _nose + _out * 0.15f + Vector3.up * 0.1f;
                var light = glow.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.78f, 0.5f);
                light.range = 2.6f;
                light.intensity = 0f;
                // 코어 혼잣말 (사용자 피드백: 안내문 말투가 어색함 → 독백과 같은 목소리)
                _stage.SetSpeaker($"<color={CoreTint}>코어</color>");
                _stage.SetLine($"<color={CoreTint}>" + (boarded < ship.Size
                    ? $"{boarded}명만 들어왔다. 나머지는 저 배에 남아 다시 떠난다."
                    : $"{name} {boarded}명이 들어왔다. 이제 같은 지붕 아래다.") + "</color>");
                for (float k = 0f; k < 1f && !_stage.Skip; k += Dt / 3.2f)
                {
                    float b = Smooth(Mathf.Clamp01(k / 0.25f));
                    cam.SetPositionAndRotation(Vector3.Lerp(fromPos, fixedPos, b), Quaternion.Slerp(fromRot, Quaternion.LookRotation(Vector3.Lerp(_nose, ShipCenter, 0.4f) - fixedPos, Vector3.up), b));
                    light.intensity = Mathf.Lerp(0f, 1.2f, Smooth(Mathf.Clamp01(k / 0.4f)));
                    yield return null;
                }
                _mode = Mode.Docked;
                _stayLeft = _stayAfterAccept;
            }
            else
            {
                // 거부: 배가 뒤로 물러나 돌아 떠남
                _stage.SetSpeaker($"<color={CoreTint}>코어</color>");
                _stage.SetLine($"<color={CoreTint}>배가 멀어진다. 저 배는 이제 어디로 가게 될까.</color>");
                for (float k = 0f; k < 1f && !_stage.Skip; k += Dt / 5f)
                {
                    float b = Smooth(Mathf.Clamp01(k / 0.15f));
                    PlaceLeaving(Mathf.Lerp(0f, 0.7f, Mathf.Clamp01(k)));
                    var look = Quaternion.LookRotation(Vector3.Lerp(_nose, ShipCenter, 0.7f) - fixedPos, Vector3.up);
                    cam.SetPositionAndRotation(Vector3.Lerp(fromPos, fixedPos, b), Quaternion.Slerp(fromRot, look, b));
                    yield return null;
                }
                _mode = Mode.Leaving;
                _leaveT = 0.7f;
            }
            _stage.ClearText();
            yield return _stage.ReturnCamera(_returnSeconds);
            _stage.End();
            if (glow != null)
                Destroy(glow, 0.1f);
        }

        private Vector3 ShipCenter => _ship != null ? _ship.transform.position - _ship.transform.forward * (_shipLength * GridConfig.CellSize * 0.5f) : _nose;

        private float ShipLength => _shipLength * GridConfig.CellSize;

        private Vector3 FollowPosition()
        {
            var t = _ship.transform;
            var (side, lift) = ArkShot.FollowOptions[_follow];
            return ArkShot.FollowCamera(t.position, t.forward, ShipLength, side, lift);
        }

        private const float CutT = 0.72f;     // 따라가기 → 고정 시점으로 컷하는 접근 진행도
        private const float DockLook = 0.45f; // 고정 시점이 보는 곳 = 포트 ~ 배 가운데 사이

        /// <summary>
        /// 포트 옆 고정 시점: 포트와 컷 순간의 배가 한 화면에 들어오도록 접근로 옆으로 물러난 자리 (사용자 피드백: 컷 직후 빈 우주만 보임).
        /// 따라가던 카메라와 같은 쪽을 먼저 봐 컷 전후 방향이 뒤집히지 않게 하고, 카메라 자리에 모듈이 있거나 포트 · 배가 모듈에 가려지면
        /// 반대쪽 · 더 높이 · 더 멀리 → 모두 막히면 정거장 위에서 내려다봄 (<see cref="ArkShot.PickFixed"/>).
        /// </summary>
        private Vector3 FixedCameraPosition()
        {
            var p = ApproachPoint(CutT);
            var ahead = ApproachPoint(CutT + 0.01f) - p;
            var cutShip = p - (ahead.sqrMagnitude > 1e-8f ? ahead.normalized : -_out) * (ShipLength * 0.5f); // 컷 순간 배 가운데
            var dockedShip = _nose + _out * (ShipLength * 0.5f);
            float followSide = ArkShot.FollowOptions[_follow].side;
            return ArkShot.PickFixed(Occupied, _nose, cutShip, dockedShip, DockLook, followSide > 0f ? -1f : 1f, StationTop(), out _);
        }

        /// <summary>가장 높은 모듈 칸의 높이 (내려다보는 시점을 그보다 위에 둠).</summary>
        private float StationTop()
        {
            float top = _nose.y;
            foreach (var m in _sim.Grid.Modules)
                foreach (var c in m.Cells)
                    top = Mathf.Max(top, GridConfig.CellToWorld(c).y);
            return top;
        }

        private List<string> ArrivalLines(ArkShip ship)
        {
            var species = _ark.Find(ship.Species);
            var lines = new List<string>();
            if (species != null)
                lines.AddRange(species.ArrivalLines);
            if (lines.Count == 0)
            {
                string name = _ark.SpeciesName(ship.Species);
                lines.Add($"배 한 척이 방주에 닿았다. 처음 보는 {name}들이다.");
                lines.Add("들일지 돌려보낼지, 이번엔 내가 정해야 한다.");
            }
            return lines;
        }

        private void LateUpdate()
        {
            UpdateFlames(Dt);
            _stage?.PollSkip();
        }
    }
}
