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
    /// - 모든 배: 도착 전 접근(남은 게임 시간으로 위치, 절정 · 전환 중엔 안 보임) → 도착 = 도킹(<see cref="ArkDocking"/>: 여객 도킹 포트 입구, 없으면 코어 쪽 바깥 끝)
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
        private float _scale = 1f;
        private MaterialPropertyBlock _block; // MonoBehaviour 필드 초기화에서 만들면 Unity가 막음 → Start에서

        // 추진 불꽃 (노즐마다 원뿔 두 겹 + 선체를 비추는 푸른 불빛), 추력 0 ~ 1
        private const float NozzleX = 0.24f; // BlenderWork/ArkShip/arkship_builder.py 노즐 위치
        private readonly List<(Transform flame, float seed)> _flames = new List<(Transform, float)>();
        private Light _engineLight;
        private float _thrust, _thrustTarget;
        private static Mesh _coneInner, _coneOuter;

        // 연출
        private bool _scene;
        private ArkShip _sceneShip;
        private bool _skip;
        private bool _wasPaused, _wasLocked, _wasBlocked;
        private Vector3 _savedFocus;
        private float _savedYaw, _savedPitch, _savedDistance;
        private RectTransform _barTop, _barBottom;
        private CanvasGroup _group;
        private TMP_Text _speaker, _line, _hint;
        private readonly List<(CanvasGroup group, float alpha, bool blocks)> _hidden = new List<(CanvasGroup, float, bool)>();

        /// <summary>연출 중 (도착 창은 끝난 뒤에).</summary>
        public bool Playing => _scene;

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
            BuildOverlay();
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
            if (_ark == null || _scene)
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
            bool showing = next != null && !next.NewSpecies && _ark.ClockOn && _ark.TimeLeft <= _approachSeconds
                           && (phase == ChapterPhase.Preparation || phase == ChapterPhase.Warning) && FindDock();
            if (showing)
            {
                if (_mode != Mode.Approach || _shipData != next)
                {
                    Spawn(next);
                    _mode = Mode.Approach;
                }
                PlaceApproach(Ease(1f - Mathf.Clamp01(_ark.TimeLeft / _approachSeconds)));
            }
            else if (_mode == Mode.Approach)
                Clear();
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

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        /// <summary>접근 경로 (멀리 옆 위 → 접근로 → 코 = 도킹 점), t = 0 ~ 1.</summary>
        private Vector3 ApproachPoint(float t)
        {
            var up = Vector3.up;
            return Bezier(_nose + _out * 42f + up * 9f + Side * 12f, _nose + _out * 20f + up * 4f + Side * 6f, _nose + _out * 6f + up * 0.3f, _nose, t);
        }

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
            Vector3 b1 = _nose + _out * 6f - Side * 2f, b2 = _nose + _out * 14f + up * 4f - Side * 8f, end = _nose + _out * 40f + up * 10f - Side * 16f;
            var p = Bezier(back, b1, b2, end, e);
            var ahead = Bezier(back, b1, b2, end, Mathf.Min(1f, e + 0.01f)) - p;
            var travel = ahead.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(ahead.normalized, up) : DockRotation;
            _ship.transform.SetPositionAndRotation(p, Quaternion.Slerp(DockRotation, travel, Smooth(Mathf.Clamp01(u / 0.35f))));
        }

        // ---------------- 처음 만나는 종족 연출 ----------------

        /// <summary>
        /// 연출 한 프레임 (실제 초, 최대 0.05) — 끊김(로딩 · 에디터 멈춤)이 있어도 장면이 건너뛰지 않게.
        /// 녹화로 프레임 시간을 고정했으면(<see cref="Time.captureDeltaTime"/>) 그 값 — 영상 속도가 실제와 같게.
        /// </summary>
        private static float Dt => Time.captureDeltaTime > 0f ? Time.captureDeltaTime : Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        /// <summary>이 배의 도착을 연출로 보일지 (처음 만나는 종족 · 붙을 자리가 있을 때).</summary>
        public bool WantsArrival(ArkShip ship) => isActiveAndEnabled && _ark != null && ship != null && ship.NewSpecies && !_scene && FindDock();

        /// <summary>도착 연출 (게임 멈춤) → 끝나면 done (도착 창).</summary>
        public void PlayArrival(ArkShip ship, Action done) => StartCoroutine(Arrival(ship, done));

        private IEnumerator Arrival(ArkShip ship, Action done)
        {
            BeginScene();
            _sceneShip = ship;
            Spawn(ship);
            _mode = Mode.Docked;
            var cam = _orbit.transform;
            float shipT = 0f;
            // 1) 배를 따라가는 카메라 (뒤 · 왼쪽 위에서 정거장 쪽을 봄)
            for (float k = 0f; k < 1f && !_skip; k += Dt / _followSeconds)
            {
                shipT = CutT * Smooth(Mathf.Clamp01(k));
                PlaceApproach(shipT);
                cam.SetPositionAndRotation(FollowPosition(), Quaternion.LookRotation(Vector3.Lerp(_ship.transform.position, _nose, 0.35f) - FollowPosition(), Vector3.up));
                yield return null;
            }
            // 2) 포트 쪽 고정 시점으로 순간 전환(컷) → 천천히 도킹 (사용자 피드백: 미끄러지듯 옮겨 가는 전환이 어색함)
            var fixedPos = FixedCameraPosition();
            for (float k = 0f; k < 1f && !_skip; k += Dt / _dockSeconds)
            {
                PlaceApproach(Mathf.Lerp(CutT, 1f, Ease(Mathf.Clamp01(k))));
                cam.SetPositionAndRotation(fixedPos, Quaternion.LookRotation(Vector3.Lerp(_nose, ShipCenter, DockLook) - fixedPos, Vector3.up));
                yield return null;
            }
            PlaceApproach(1f);
            if (!_skip)
                AudioService.TryPlay(l => l.BuildSelect);
            var hold = Quaternion.LookRotation(Vector3.Lerp(_nose, ShipCenter, DockLook) - fixedPos, Vector3.up);
            cam.SetPositionAndRotation(fixedPos, hold);
            // 3) 코어 독백
            if (!_skip)
                yield return Monologue(ArrivalLines(ship));
            _line.SetText("");
            _speaker.SetText("");
            // 4) 원래 시점으로 돌아오며 도착 창
            yield return Return();
            EndScene();
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
            BeginScene();
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
                _speaker.SetText($"<color={CoreTint}>코어</color>");
                _line.SetText($"<color={CoreTint}>" + (boarded < ship.Size
                    ? $"{boarded}명만 들어왔다. 나머지는 저 배에 남아 다시 떠난다."
                    : $"{name} {boarded}명이 들어왔다. 이제 같은 지붕 아래다.") + "</color>");
                for (float k = 0f; k < 1f && !_skip; k += Dt / 3.2f)
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
                _speaker.SetText($"<color={CoreTint}>코어</color>");
                _line.SetText($"<color={CoreTint}>배가 멀어진다. 저 배는 이제 어디로 가게 될까.</color>");
                for (float k = 0f; k < 1f && !_skip; k += Dt / 5f)
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
            _line.SetText("");
            _speaker.SetText("");
            yield return Return();
            EndScene();
            if (glow != null)
                Destroy(glow, 0.1f);
        }

        private Vector3 ShipCenter => _ship != null ? _ship.transform.position - _ship.transform.forward * (_shipLength * GridConfig.CellSize * 0.5f) : _nose;

        private Vector3 FollowPosition()
        {
            var t = _ship.transform;
            float s = _shipLength * GridConfig.CellSize;
            return t.position - t.forward * (s * 1.9f) + t.right * (-s * 0.9f) + Vector3.up * (s * 0.55f);
        }

        private const float CutT = 0.72f;     // 따라가기 → 고정 시점으로 컷하는 접근 진행도
        private const float DockLook = 0.45f; // 고정 시점이 보는 곳 = 포트 ~ 배 가운데 사이

        /// <summary>
        /// 포트 옆 고정 시점: 포트와 컷 순간의 배가 한 화면에 들어오도록 접근로 옆으로 물러난 자리 중 모듈이 없는 곳 (모듈 속에 카메라가 들어가지 않게).
        /// 따라가던 카메라와 같은 쪽(배의 왼쪽 위)에 둬 컷 전후 방향이 뒤집히지 않게 한다 (사용자 피드백: 컷 직후 빈 우주만 보임).
        /// </summary>
        private Vector3 FixedCameraPosition()
        {
            var from = ApproachPoint(CutT) + _out * (_shipLength * GridConfig.CellSize * 0.5f); // 컷 순간 배 가운데쯤
            var span = from - _nose;
            var mid = Vector3.Lerp(_nose, from, 0.4f);
            var across = Vector3.Cross(Vector3.up, span.normalized);
            if (across.sqrMagnitude < 1e-4f)
                across = Side;
            across.Normalize();
            float d = span.magnitude * 0.85f;
            foreach (var k in new[] { 1f, 1.25f, 1.5f })
                foreach (var s in new[] { 1f, -1f })
                {
                    var p = mid + across * (s * d * k) + Vector3.up * (d * k * 0.35f);
                    if (Free(p))
                        return p;
                }
            return mid + across * (d * 1.8f) + Vector3.up * (d * 0.7f);
        }

        private bool Free(Vector3 p)
        {
            var c = GridConfig.WorldToCell(p);
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                        if (_sim.Grid.IsOccupied(c + new Vector3Int(x, y, z)))
                            return false;
            return true;
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
                lines.Add($"배 한 척이 포트에 닿았다. 처음 보는 {name}들이다.");
                lines.Add("들일지 돌려보낼지, 이번엔 내가 정해야 한다.");
            }
            return lines;
        }

        /// <summary>코어 독백: 줄마다 타자 → 다 나오면 2.4초 또는 클릭 · Space · Enter로 다음. 코어 기록에도 남김.</summary>
        private IEnumerator Monologue(List<string> lines)
        {
            var core = _sim.GetSystem<CorePersonalitySystem>();
            _speaker.SetText($"<color={CoreTint}>코어</color>");
            foreach (var text in lines)
            {
                core?.Log(text);
                float typed = 0f;
                while (!_skip && typed < text.Length)
                {
                    typed += Dt * _charsPerSecond;
                    if (NextPressed())
                        typed = text.Length;
                    _line.SetText($"<color={CoreTint}>{text.Substring(0, Mathf.Min(text.Length, Mathf.FloorToInt(typed)))}</color>");
                    yield return null;
                }
                _line.SetText($"<color={CoreTint}>{text}</color>");
                yield return null;
                for (float w = 0f; w < 2.4f && !_skip && !NextPressed(); w += Dt)
                    yield return null;
                if (_skip)
                    yield break;
            }
        }

        private static bool NextPressed()
        {
            var k = Keyboard.current;
            var m = Mouse.current;
            return (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame)) || (m != null && m.leftButton.wasPressedThisFrame);
        }

        private IEnumerator Return()
        {
            var cam = _orbit.transform;
            var rig = _orbit.Rig;
            rig.Set(_savedFocus, _savedYaw, _savedPitch, _savedDistance);
            Vector3 fromPos = cam.position;
            Quaternion fromRot = cam.rotation;
            float seconds = _skip ? 0.25f : _returnSeconds;
            for (float k = 0f; k < 1f; k += Dt / seconds)
            {
                float b = Smooth(Mathf.Clamp01(k));
                cam.SetPositionAndRotation(Vector3.Lerp(fromPos, rig.Position, b), Quaternion.Slerp(fromRot, rig.Rotation, b));
                SetBars(1f - b);
                yield return null;
            }
            SetBars(0f);
        }

        // ---------------- 장면 시작 · 끝 ----------------

        private void BeginScene()
        {
            _scene = true;
            _skip = false;
            _wasPaused = _clock.Clock.IsPaused;
            _wasLocked = _clock.InputLocked;
            _wasBlocked = InputGate.Blocked;
            _clock.Clock.SetPaused(true);
            _clock.InputLocked = true;
            InputGate.Blocked = true;
            var rig = _orbit.Rig;
            _savedFocus = rig.Focus;
            _savedYaw = rig.Yaw;
            _savedPitch = rig.Pitch;
            _savedDistance = rig.Distance;
            _orbit.enabled = false;
            HideHud(true);
            transform.SetAsLastSibling();
            _group.alpha = 1f;
            _line.SetText("");
            _speaker.SetText("");
            StartCoroutine(BarsIn());
        }

        private void EndScene()
        {
            _orbit.Restore(_savedFocus, _savedYaw, _savedPitch, _savedDistance);
            _orbit.enabled = true;
            HideHud(false);
            _group.alpha = 0f;
            _clock.InputLocked = _wasLocked;
            _clock.Clock.SetPaused(_wasPaused);
            InputGate.Blocked = _wasBlocked;
            _scene = false;
        }

        private IEnumerator BarsIn()
        {
            for (float k = 0f; k < 1f && _scene; k += Dt / 0.4f)
            {
                SetBars(Smooth(Mathf.Clamp01(k)));
                yield return null;
            }
            if (_scene)
                SetBars(1f);
        }

        private void LateUpdate()
        {
            UpdateFlames(Dt);
            if (!_scene)
                return;
            var k = Keyboard.current;
            if (k != null && k.escapeKey.wasPressedThisFrame)
            {
                InputGate.ConsumeEscape();
                _skip = true;
            }
        }

        /// <summary>장면 동안 다른 HUD를 감춤 (CanvasGroup 투명도, 끝나면 되돌림).</summary>
        private void HideHud(bool hide)
        {
            if (!hide)
            {
                foreach (var (group, alpha, blocks) in _hidden)
                {
                    if (group == null)
                        continue;
                    group.alpha = alpha;
                    group.blocksRaycasts = blocks;
                }
                _hidden.Clear();
                return;
            }
            var parent = transform.parent;
            if (parent == null)
                return;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == transform || !child.gameObject.activeSelf)
                    continue;
                var group = child.GetComponent<CanvasGroup>();
                if (group == null)
                    group = child.gameObject.AddComponent<CanvasGroup>();
                _hidden.Add((group, group.alpha, group.blocksRaycasts));
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
        }

        // ---------------- 화면 (레터박스 · 자막) ----------------

        private void BuildOverlay()
        {
            var root = (RectTransform)transform;
            _group = root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _barTop = Bar(root, "BarTop", top: true);
            _barBottom = Bar(root, "BarBottom", top: false);
            _speaker = _ui.Label(_barBottom, "", 18f, TextAlignmentOptions.Center);
            _speaker.rectTransform.anchorMin = new Vector2(0f, 1f);
            _speaker.rectTransform.anchorMax = new Vector2(1f, 1f);
            _speaker.rectTransform.pivot = new Vector2(0.5f, 1f);
            _speaker.rectTransform.anchoredPosition = new Vector2(0f, -10f);
            _speaker.rectTransform.sizeDelta = new Vector2(0f, 24f);
            _line = _ui.Label(_barBottom, "", 26f, TextAlignmentOptions.Center, wrap: true);
            _line.rectTransform.anchorMin = new Vector2(0.15f, 0f);
            _line.rectTransform.anchorMax = new Vector2(0.85f, 1f);
            _line.rectTransform.offsetMin = new Vector2(0f, 30f);
            _line.rectTransform.offsetMax = new Vector2(0f, -34f);
            _hint = _ui.Label(_barBottom, $"<color={Soft}>ESC 건너뛰기</color>", 14f, TextAlignmentOptions.BottomRight);
            _hint.rectTransform.anchorMin = new Vector2(1f, 0f);
            _hint.rectTransform.anchorMax = new Vector2(1f, 0f);
            _hint.rectTransform.pivot = new Vector2(1f, 0f);
            _hint.rectTransform.anchoredPosition = new Vector2(-28f, 12f);
            _hint.rectTransform.sizeDelta = new Vector2(260f, 24f);
            SetBars(0f);
        }

        private static RectTransform Bar(RectTransform parent, string name, bool top)
        {
            var rt = HoloUi.Rect(name, parent);
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.sizeDelta = new Vector2(0f, 150f);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            return rt;
        }

        /// <summary>레터박스 0 ~ 1 (화면 위아래 검은 띠가 들어오고 나감).</summary>
        private void SetBars(float k)
        {
            if (_barTop == null)
                return;
            _barTop.anchoredPosition = new Vector2(0f, (1f - k) * 150f);
            _barBottom.anchoredPosition = new Vector2(0f, -(1f - k) * 150f);
        }
    }
}
