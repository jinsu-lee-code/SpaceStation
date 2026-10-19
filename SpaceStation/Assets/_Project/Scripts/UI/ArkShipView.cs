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
        }

        private void Clear()
        {
            if (_ship != null)
                Destroy(_ship);
            _ship = null;
            _shipData = null;
            _mode = Mode.None;
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
        }

        /// <summary>떠나는 길: 뒤로 물러남(25%) → 돌아서 옆 위로 멀어짐.</summary>
        private void PlaceLeaving(float t)
        {
            if (_ship == null)
                return;
            var up = Vector3.up;
            var back = _nose + _out * 2.5f;
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

        /// <summary>연출 한 프레임 (실제 초, 최대 0.05) — 끊김(로딩 · 에디터 멈춤)이 있어도 장면이 건너뛰지 않게.</summary>
        private static float Dt => Mathf.Min(Time.unscaledDeltaTime, 0.05f);

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
                shipT = 0.72f * Smooth(Mathf.Clamp01(k));
                PlaceApproach(shipT);
                cam.SetPositionAndRotation(FollowPosition(), Quaternion.LookRotation(Vector3.Lerp(_ship.transform.position, _nose, 0.35f) - FollowPosition(), Vector3.up));
                yield return null;
            }
            // 2) 포트 근처 고정 시점으로 넘어가며 천천히 도킹
            var fixedPos = FixedCameraPosition();
            Vector3 fromPos = cam.position;
            Quaternion fromRot = cam.rotation;
            for (float k = 0f; k < 1f && !_skip; k += Dt / _dockSeconds)
            {
                PlaceApproach(Mathf.Lerp(0.72f, 1f, Ease(Mathf.Clamp01(k))));
                var look = Quaternion.LookRotation(Vector3.Lerp(_nose, ShipCenter, 0.6f) - fixedPos, Vector3.up);
                float b = Smooth(Mathf.Clamp01(k / 0.2f)); // 앞 0.8초 남짓 동안 고정 시점으로 옮겨 감
                cam.SetPositionAndRotation(Vector3.Lerp(fromPos, fixedPos, b), Quaternion.Slerp(fromRot, look, b));
                yield return null;
            }
            PlaceApproach(1f);
            if (!_skip)
                AudioService.TryPlay(l => l.BuildSelect);
            var hold = Quaternion.LookRotation(Vector3.Lerp(_nose, ShipCenter, 0.6f) - fixedPos, Vector3.up);
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
                yield break;
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
                _line.SetText(boarded < ship.Size ? $"{name} {boarded}명이 방주에 오르고, 나머지는 돌아갑니다." : $"{name} {boarded}명이 방주에 올랐습니다.");
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
                _line.SetText($"{name}의 배가 돌아갑니다.");
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

        /// <summary>포트 옆 고정 시점: 접근로 쪽 후보 자리 중 모듈이 없는 곳 (모듈 속에 카메라가 들어가지 않게).</summary>
        private Vector3 FixedCameraPosition()
        {
            var side = Side;
            foreach (var a in new[] { 2.5f, 4f, 6f, 8f })
                foreach (var b in new[] { 3f, -3f, 4.5f, -4.5f })
                    foreach (var c in new[] { 1.3f, 2.6f })
                    {
                        var p = _nose + _out * a + side * b + Vector3.up * c;
                        if (Free(p))
                            return p;
                    }
            return _nose + _out * 9f + side * 3f + Vector3.up * 3f;
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
                lines.Add($"{name}의 배다. 처음 보는 무리다.");
                lines.Add("받아들일지, 돌려보낼지. 내가 문을 고른다.");
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
