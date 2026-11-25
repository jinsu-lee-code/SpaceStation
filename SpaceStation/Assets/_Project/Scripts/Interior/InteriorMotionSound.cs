using System;
using SpaceStation.Audio;
using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 사운드 개편 D-8 (2026-11-26, SOUND_LIST_NEW 4-4 obj_*): 움직이는 부품에 붙는 3D 모터 루프 — 실제로 움직인 만큼 (<see cref="InteriorObjectSound.MotionLevel"/>) ·
    /// 지금 있는 방일 때만 (D-4). 부품 (다리 크레인 집게 · 스태커 짐판 · 셔틀 · 태양 가대 · 거울 머리)에 붙이면 템플릿을 다시 지어도 따라감.
    /// 모듈이 멈추면 부품이 서므로 소리도 저절로 멎음. 만들기 = <see cref="Attach"/>.
    /// </summary>
    public sealed class InteriorMotionSound : MonoBehaviour
    {
        private const float Smoothing = 6f;   // 빠르기를 고르는 정도 (1/초)

        private ObjectLoop _loop;
        private float _fullMeters, _fullDegrees, _gain;
        private Vector2 _pitch;
        private Vector3 _lastPos;
        private Quaternion _lastRot;
        private bool _has;
        private float _speed, _turn;
        private ModuleInstance _room;
        private float _roomAt = -99f, _hereAt = -99f;
        private bool _here;

        /// <summary>
        /// part에 모터 루프를 붙임: fullMeters (m/s) · fullDegrees (°/s) 빠르기에서 소리 1 (gain), 천천히 = 작고 낮게 (pitch.x) → 빠르게 = pitch.y.
        /// 소리 자리 = 부품 렌더러 가운데 (부품 원점이 방 원점인 경우가 있어서 — 겪은 일 11-25 ③). 서비스 · 클립이 없으면 null.
        /// </summary>
        public static InteriorMotionSound Attach(Transform part, Func<SoundLibrary, SoundCue> pick, float fullMeters, float fullDegrees,
            float gain = 1f, float pitchSlow = 0.92f, float pitchFast = 1.04f, float minDistance = 2f, float maxDistance = 16f)
        {
            if (part == null)
                return null;
            var loop = AudioService.AttachLoop(pick, part, minDistance, maxDistance);
            if (loop == null)
                return null;
            loop.transform.localPosition = LocalCenter(part);
            loop.FadeSpeed = 4f;
            var m = loop.gameObject.AddComponent<InteriorMotionSound>();
            m._loop = loop;
            m._fullMeters = fullMeters;
            m._fullDegrees = fullDegrees;
            m._gain = gain;
            m._pitch = new Vector2(pitchSlow, pitchFast);
            loop.Level = 0f;
            return m;
        }

        /// <summary>부품 (자식 포함) 렌더러들의 가운데를 부품 로컬로 — 렌더러가 없으면 원점.</summary>
        public static Vector3 LocalCenter(Transform part)
        {
            var rs = part.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0)
                return Vector3.zero;
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++)
                b.Encapsulate(rs[i].bounds);
            return part.InverseTransformPoint(b.center);
        }

        /// <summary>
        /// 그 자리 (at 로컬 local)에 붙는 3D 루프 — 처음엔 0, Level은 부르는 쪽이 정함 (모듈이 돌고 이 방에 있을 때 1 — 벨트 · 서버 팬 · 화면 웅처럼 늘 도는 것).
        /// 서비스 · 클립이 없으면 null (다음에 다시 부름).
        /// </summary>
        public static ObjectLoop LoopAt(Transform at, Vector3 local, Func<SoundLibrary, SoundCue> pick, float minDistance, float maxDistance)
        {
            var loop = at != null ? AudioService.AttachLoop(pick, at, minDistance, maxDistance) : null;
            if (loop == null)
                return null;
            loop.transform.localPosition = local;
            loop.Level = 0f;
            return loop;
        }

        /// <summary>
        /// 그 물체가 지금 플레이어가 있는 방에 있는지 (0.25초마다 다시 봄 — 방 모듈은 1초마다). <see cref="InteriorRoomDisplay"/>가 아닌 부품 (궤도 · 태양 가대)이 씀.
        /// </summary>
        public static bool PlayerInRoomOf(Transform t, ref ModuleInstance room, ref float roomAt, ref float hereAt, ref bool here)
        {
            float now = Time.unscaledTime;
            if (now < hereAt)
                return here;
            hereAt = now + 0.25f;
            if (room == null || now >= roomAt)
            {
                room = InteriorRoomDisplay.RoomOf(t);
                roomAt = now + 1f;
            }
            here = room != null && InteriorRoomDisplay.PlayerIn(room);
            return here;
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (_loop == null || dt <= 0f)
                return;
            var p = transform.position;
            var r = transform.rotation;
            float v = 0f, w = 0f;
            if (_has)
            {
                float moved = Vector3.Distance(p, _lastPos);
                if (moved < 2f)   // 방을 다시 짓거나 옮긴 순간 (순간 이동)은 빼고
                {
                    v = moved / dt;
                    w = Quaternion.Angle(_lastRot, r) / dt;
                }
            }
            _lastPos = p;
            _lastRot = r;
            _has = true;
            float k = 1f - Mathf.Exp(-Smoothing * dt);
            _speed += (v - _speed) * k;
            _turn += (w - _turn) * k;
            float level = InteriorObjectSound.MotionLevel(_speed, _turn, _fullMeters, _fullDegrees);
            bool here = PlayerInRoomOf(transform, ref _room, ref _roomAt, ref _hereAt, ref _here);
            _loop.Level = here ? level * _gain : 0f;
            _loop.Pitch = Mathf.Lerp(_pitch.x, _pitch.y, level);
        }
    }
}
