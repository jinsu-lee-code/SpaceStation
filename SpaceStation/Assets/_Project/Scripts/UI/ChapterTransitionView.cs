using System;
using System.Collections;
using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 13-7b 장 전환 연출 (REDESIGN T7 · T8 · M1, 사용자 결정 2026-10-19): 장 결과 창에서 방향을 고르면 →
    /// 카메라가 정거장 너머 지구 쪽으로 돌고(정거장 실루엣 뒤로 지구) 그 사이 지구 불빛이 눈에 띄게 꺼짐 + 코어 독백 2줄(성향에 따라, 코어 기록에도) +
    /// 방주에 남은 인간의 옛 녹음 한 조각 → 검은 막 위 다음 장 제목 카드("2장" · "배치") → 게임. 마지막 장이면 제목 카드 대신 원래 시점으로 → '이어질 이야기'.
    /// 약 15초, ESC = 건너뛰기, 보는 동안 시간 · 조작 멈춤. 불빛 수치는 글로 보이지 않음 (장면으로만).
    /// 무대(레터박스 · 자막 · HUD 감추기 · 카메라 복귀)는 이민선 연출과 같은 <see cref="CinematicStage"/>.
    /// </summary>
    public sealed class ChapterTransitionView : MonoBehaviour
    {
        [SerializeField] private StationController _station;
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [SerializeField] private HoloArt _holoArt;
        [Tooltip("옛 녹음 자막 위 작은 글씨")]
        [SerializeField] private string _recordSpeaker = "방주 기록 · 녹음 조각";
        [SerializeField] private float _charsPerSecond = 24f;
        [Tooltip("카메라가 지구 쪽 시점으로 가는 시간 (실제 초)")]
        [SerializeField] private float _approachSeconds = 3f;
        [Tooltip("시점에 닿은 뒤 불빛이 꺼지기 시작할 때까지 · 꺼지는 데 걸리는 시간")]
        [SerializeField] private float _dimDelay = 0.6f;
        [SerializeField] private float _dimSeconds = 4.5f;
        [Tooltip("제목 카드가 떠 있는 시간")]
        [SerializeField] private float _titleSeconds = 2.6f;
        [Tooltip("13-8 장 결과 창: 지구 쪽 시점으로 가는 시간 (실제 초) · 지구가 놓이는 화면 가로 자리 (결과 창은 왼쪽)")]
        [SerializeField] private float _frameSeconds = 1.6f;
        [SerializeField, Range(0.5f, 0.95f)] private float _earthScreenX = 0.72f;

        private StationSimulation _sim;
        private HoloUi _ui;
        private CinematicStage _stage;
        private EarthView _earth;
        private bool _moving;
        private bool _framing;
        private Coroutine _frameRoutine;

        public bool Playing => _stage != null && _stage.Active;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            var orbit = FindFirstObjectByType<OrbitCameraController>();
            if (_sim?.Chapters == null || orbit == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            _stage = new CinematicStage((RectTransform)transform, _ui, _clock, orbit);
            _earth = FindFirstObjectByType<EarthView>();
        }

        private void LateUpdate() => _stage?.PollSkip();

        /// <summary>
        /// 방향을 고른 직후 (장 틀은 이미 다음 장을 시작했거나 캠페인을 끝냄). finishedNumber = 방금 마친 장 번호(1부터),
        /// next = 다음 장 (마지막 장이면 null), done = 연출이 끝난 뒤 (시간은 연출 전 상태 그대로 — 부른 쪽이 다시 흐르게).
        /// </summary>
        public void Play(ChapterData finished, int finishedNumber, ChapterData next, Action done)
        {
            StopFraming(); // 결과 창의 지구 시점에서 그대로 이어짐 (궤도 카메라의 시점 기억은 그대로라 끝나면 원래 시점으로)
            StartCoroutine(Run(finished, finishedNumber, next, done));
        }

        /// <summary>
        /// 13-8 ③ 보완 (사용자 결정 2026-10-25): 장 결과 창이 열린 동안 정거장 너머 지구를 화면 오른쪽에 크게 — 시간이 멈춘 결과 창이라 볼 여유가 있음.
        /// 궤도 카메라만 끄고(시점 기억은 그대로) 방향을 고르면 장 전환 연출이 이 시점에서 이어짐. 연출 없이 창이 닫히면 <see cref="ReleaseEarth"/>.
        /// </summary>
        public bool FrameEarth()
        {
            if (_stage == null || _stage.Active)
                return false;
            if (_framing)
                return true;
            _framing = true;
            _stage.Orbit.enabled = false;
            _frameRoutine = StartCoroutine(FrameMotion());
            return true;
        }

        /// <summary>연출 없이 결과 창이 닫힐 때: 궤도 카메라(원래 시점)로 되돌림.</summary>
        public void ReleaseEarth()
        {
            if (!_framing)
                return;
            StopFraming();
            _stage.Orbit.enabled = true;
        }

        private void StopFraming()
        {
            _framing = false;
            if (_frameRoutine != null)
                StopCoroutine(_frameRoutine);
            _frameRoutine = null;
        }

        private IEnumerator FrameMotion()
        {
            var cam = _stage.Camera;
            EarthShot(out var pos, out var rot);
            Vector3 fromPos = cam.position;
            Quaternion fromRot = cam.rotation;
            for (float t = 0f; _framing; t += CinematicStage.Dt)
            {
                float k = CinematicStage.Smooth(Mathf.Clamp01(t / Mathf.Max(0.01f, _frameSeconds)));
                cam.SetPositionAndRotation(Vector3.Lerp(fromPos, pos, k), Quaternion.Slerp(fromRot, rot, k));
                yield return null;
            }
        }

        /// <summary>장 전환 연출과 같은 자리(정거장 뒤 위)에서 지구 쪽을 보되, 지구가 화면 가로 <see cref="_earthScreenX"/>에 오게 왼쪽으로 돌린 시점.</summary>
        private void EarthShot(out Vector3 pos, out Quaternion rot)
        {
            StationBounds(out var center, out float radius);
            var dir = _earth != null ? _earth.Direction : new Vector3(0.5f, -0.45f, 0.75f).normalized;
            pos = center - dir * (radius * 2.4f + 10f) + Vector3.up * (radius * 0.5f + 3f);
            var camera = _stage.Orbit.GetComponent<Camera>();
            float tanHalfW = camera != null ? Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * camera.aspect : 0.8f;
            float yaw = Mathf.Atan((_earthScreenX - 0.5f) * 2f * tanHalfW) * Mathf.Rad2Deg;
            rot = Quaternion.AngleAxis(-yaw, Vector3.up) * Quaternion.LookRotation(dir, Vector3.up);
        }

        private IEnumerator Run(ChapterData finished, int finishedNumber, ChapterData next, Action done)
        {
            // 화면 불빛은 아직 지난 장 값 (시계는 이미 다음 장 시작 값) → 연출이 꺼지는 순간을 고름
            float fromLights = _earth != null ? _earth.ShownLights : 1f;
            float toLights = _earth != null ? _earth.TargetLights : 1f;
            _earth?.HoldLights(fromLights);
            _stage.Begin(this);
            _moving = true;
            StartCoroutine(Motion(fromLights, toLights));

            for (float t = 0f; t < _approachSeconds && !_stage.Skip; t += CinematicStage.Dt)
                yield return null; // 시점에 닿을 때까지 자막 없이

            // 코어 독백 2줄 (지금 성향으로 고른 문장, 코어 기록에도)
            var core = _sim.GetSystem<CorePersonalitySystem>();
            var monologue = Lines(core?.Say($"transition.{finishedNumber}", null));
            if (monologue.Count > 0)
                yield return _stage.Say("코어", ChapterView.CoreTint, monologue, _charsPerSecond, line => core?.Log(line));
            // 방주에 남은 인간의 옛 녹음 한 조각 (T8 — 인간이 왜 방주를 만들었는지)
            var record = Lines(finished != null ? finished.Transmission : null);
            if (record.Count > 0 && !_stage.Skip)
            {
                for (float t = 0f; t < 0.5f && !_stage.Skip; t += CinematicStage.Dt)
                    yield return null;
                yield return _stage.Say(_recordSpeaker, CinematicStage.Soft, record, _charsPerSecond * 0.85f, line => core?.Log("녹음 · " + line));
            }
            _stage.ClearText();

            if (next != null)
            {
                // 검은 막 → (막 뒤에서 시점 · HUD를 되돌림) → 다음 장 제목 카드 → 막이 걷히며 게임
                yield return _stage.Fade(_stage.SetCurtain, 0f, 1f, _stage.Skip ? 0.25f : 0.7f);
                _moving = false;
                _earth?.HoldLights(toLights);
                _stage.RestoreView();
                _stage.SetTitle(next.DisplayName, next.Subtitle);
                yield return _stage.Fade(_stage.SetTitleAlpha, 0f, 1f, 0.5f);
                float hold = _stage.Skip ? 0.8f : _titleSeconds;
                for (float t = 0f; t < hold; t += CinematicStage.Dt)
                    yield return null;
                yield return _stage.Fade(_stage.SetTitleAlpha, 1f, 0f, 0.4f);
                yield return _stage.Fade(_stage.SetCurtain, 1f, 0f, 0.9f);
                _stage.Release();
            }
            else
            {
                // 마지막 장: 원래 시점으로 → 부른 쪽이 '이어질 이야기'
                _moving = false;
                _earth?.HoldLights(toLights);
                yield return _stage.ReturnCamera(1.4f);
                _stage.End();
            }
            _earth?.ReleaseLights();
            done?.Invoke();
        }

        /// <summary>"첫 줄\n둘째 줄" → 줄 목록 (빈 줄 뺌).</summary>
        private static List<string> Lines(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text))
                return list;
            foreach (var part in text.Split('\n'))
            {
                var line = part.Trim();
                if (line.Length > 0)
                    list.Add(line);
            }
            return list;
        }

        // ---------------- 카메라 · 불빛 ----------------

        /// <summary>
        /// 지금 시점 → 정거장 뒤 위에서 지구 쪽을 보는 시점(정거장 실루엣 너머로 지구)으로 부드럽게 간 뒤, 정거장 둘레를 아주 천천히 돎.
        /// 시점에 닿고 조금 뒤 불빛이 from → to로 꺼짐. 건너뛰면 불빛은 바로 to.
        /// </summary>
        private IEnumerator Motion(float fromLights, float toLights)
        {
            var cam = _stage.Camera;
            StationBounds(out var center, out float radius);
            var dir = _earth != null ? _earth.Direction : new Vector3(0.5f, -0.45f, 0.75f).normalized;
            float back = radius * 2.4f + 10f;
            Vector3 startPos = cam.position;
            Quaternion startRot = cam.rotation;
            float t = 0f;
            while (_moving)
            {
                t += CinematicStage.Dt;
                // 정거장 뒤(지구 반대쪽) 위에서 지구 쪽을 봄, 아주 천천히 둘레를 돎 (초당 0.3° — 빠르면 지구가 화면 가운데에서 벗어남)
                var orbit = Quaternion.AngleAxis(t * 0.3f, Vector3.up);
                var shotPos = center + orbit * (-dir * back) + Vector3.up * (radius * 0.5f + 3f);
                var shotRot = Quaternion.LookRotation(center + orbit * dir * 60f - shotPos, Vector3.up);
                float k = _stage.Skip ? 1f : CinematicStage.Smooth(Mathf.Clamp01(t / Mathf.Max(0.01f, _approachSeconds)));
                cam.SetPositionAndRotation(Vector3.Lerp(startPos, shotPos, k), Quaternion.Slerp(startRot, shotRot, k));
                if (_earth != null)
                {
                    float d = _stage.Skip ? 1f : CinematicStage.Smooth(Mathf.Clamp01((t - _approachSeconds - _dimDelay) / Mathf.Max(0.01f, _dimSeconds)));
                    _earth.HoldLights(Mathf.Lerp(fromLights, toLights, d));
                }
                yield return null;
            }
        }

        /// <summary>정거장 모듈 칸들의 가운데 · 반지름 (칸) — 큰 정거장이면 그만큼 뒤로 물러나 전체가 지구 앞에 들어오게.</summary>
        private void StationBounds(out Vector3 center, out float radius)
        {
            var sum = Vector3.zero;
            int n = 0;
            foreach (var m in _sim.Grid.Modules)
                foreach (var c in m.Cells)
                {
                    sum += GridConfig.CellToWorld(c);
                    n++;
                }
            center = n > 0 ? sum / n : Vector3.zero;
            radius = 1f;
            foreach (var m in _sim.Grid.Modules)
                foreach (var c in m.Cells)
                    radius = Mathf.Max(radius, Vector3.Distance(center, GridConfig.CellToWorld(c)) + 0.5f);
        }
    }
}
