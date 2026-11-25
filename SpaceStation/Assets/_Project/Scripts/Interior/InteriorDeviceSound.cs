using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 사운드 개편 4-1 · 4-2 · 4-3 (2026-11-26, SOUND_LIST_NEW): 내부 장치 소리를 고르는 규칙 — 순수 함수 (EditMode InteriorDeviceSoundTests).
    /// 문 종류 (금고문) · 해독실 신호판 잡음 크기 · 레이더 핑 크기 · 우주복 (헬멧 밖) · 침입 드론 뚫음이 새 습격인지.
    /// </summary>
    public static class InteriorDeviceSound
    {
        /// <summary>
        /// 그 방 쪽 문짝 소리 (D-6 문 종류): 트인 문간 = 없음 (null) · 에어록 · 방사선 차폐실 쪽 일반 문 = 금고문 · 1단계 이상 · 기본.
        /// shielded = 방사선 경보판이 있는 방 (차폐실 — 진화 방호벽은 문이 트임 · 에어록이라 일반 문이 없음).
        /// </summary>
        public static DoorSound? DoorFor(DoorKind kind, int stage, bool shielded)
        {
            if (kind == DoorKind.Open)
                return null;
            if (kind == DoorKind.Airlock)
                return DoorSound.Airlock;
            if (shielded)
                return DoorSound.Vault;
            return stage >= 1 ? DoorSound.Stage1 : DoorSound.Basic;
        }

        /// <summary>
        /// 해독실 신호판 먼 무전 잡음 크기 (0 ~ 1, 루프 Level): 꺼짐 0 · 함선 없음 (잔잔한 잡음) 0.3 · 해독 못 함 (거친 잡음) 1 ·
        /// 해독 중 = 1에서 0.25로 (진행만큼 고른 물결이 되며 잡음이 걷힘) · 해독 끝 0.1. 화면 파형 (InteriorSignalBoard.Wave)과 같은 갈래.
        /// </summary>
        public static float SignalNoise(bool on, bool visiting, bool decoded, bool canDecode, float progress)
        {
            if (!on)
                return 0f;
            if (!visiting)
                return 0.3f;
            if (decoded)
                return 0.1f;
            return canDecode ? Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(progress)) : 1f;
        }

        /// <summary>센서 레이더 핑 크기 (0 ~ 1): 꺼짐 0 · 예보 없음 0.55 · 유성우 · 포격 예보나 낙하 중 1.</summary>
        public static float RadarLevel(bool active, bool alert) => !active ? 0f : alert ? 1f : 0.55f;

        /// <summary>
        /// 우주복 (헬멧 밖)인지: 2단계 개방형 방 (주민 우주복 판정 <see cref="InteriorOpenAir"/>이 붙은 방)에서 실내 구역 밖.
        /// 막 경계를 오가며 헬멧 소리가 잇달아 나지 않게, 바뀐 뒤 gap초 안에는 그대로 둠 (now − changedAt).
        /// </summary>
        public static bool SuitOn(bool hasOpenAir, bool outdoors, bool wasOn, float now, float changedAt, float gap = 1.5f)
        {
            bool want = hasOpenAir && outdoors;
            if (want == wasOn)
                return wasOn;
            if (!hasOpenAir)
                return false;   // 다른 방으로 옮김 = 바로 벗음 (소리 없이)
            return now - changedAt >= gap ? want : wasOn;
        }

        /// <summary>침입 드론 "뚫음" 소리를 낼지: 새 습격 대상이 생김 (들어오기 전부터 있던 습격 · 같은 습격의 방 다시 짓기는 아님).</summary>
        public static bool BreachIsNew(object knownTarget, object newTarget) => newTarget != null && !ReferenceEquals(knownTarget, newTarget);
    }
}
