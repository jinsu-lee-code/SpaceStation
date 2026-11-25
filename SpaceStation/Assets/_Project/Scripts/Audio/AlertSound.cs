using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>경보 위계 4단계 (사운드 개편 2026-11-25, SOUND_LIST_NEW D-3): 안내 &lt; 주의 &lt; 위험 &lt; 치명.</summary>
    public enum AlertLevel
    {
        /// <summary>안내: 알아 두면 좋은 일 (사건 끝남 · 함선 접근). 밝은 한 음.</summary>
        Info,
        /// <summary>주의: 곧 올 일 · 나빠지는 중 (조기 경보 · 유성우 예보 · 역병 · 파업 조짐 · 함선 도착 대응 · 등급 하락 · 사고 놓침). 두 음.</summary>
        Caution,
        /// <summary>위험: 지금 피해가 나는 중 (나쁜 사건 · 모듈 파손 · 자원 바닥 · 내부 사고 · 습격 · 침입 드론 · 역병 · 파업). 세 음 내림 + 낮은 "둥".</summary>
        Danger,
        /// <summary>치명: 질 수 있음 (모듈 파괴 · 실패 조건 경보 고리). 세 음 내림을 빠르게 두 번 + 낮은 울림.</summary>
        Critical,
    }

    /// <summary>
    /// 사건 · 경고 알림을 한 가족의 소리 넷으로 (SOUND_LIST_NEW D-3): UI 한 벌과 같은 유리 결 · 레 미 솔 라 음 (바깥 낮 D 장조 · 위기 D 단조 · 내부 F 장조 모두 안),
    /// 단계가 오를수록 음이 많고 낮게 끝나고 빨라지고 낮은 울림이 커짐 — 처음 듣는 경보도 얼마나 급한지 앎 (색 말고 소리로도).
    /// 몰릴 때 (D-2 "사건 알림"): 높은 단계가 막 울렸으면 (<see cref="HigherHold"/>초 안) 낮은 단계는 생략 · 같은 프레임에 낮은 것이 먼저 울렸으면 멈추고 높은 것만 ·
    /// 같은 단계 반복은 큐 쉬는 시간 (Cooldown). 좋은 소식 (보급선 · 등급 상승 · 연구 완료 · 해결)은 위계 밖 — 각자 소리 그대로.
    /// </summary>
    public static class AlertSound
    {
        /// <summary>높은 단계가 울린 뒤 이 시간 (실시간 초) 동안 낮은 단계는 생략 (위험 · 치명 소리 길이 1.2 ~ 1.5초).</summary>
        public const float HigherHold = 1.2f;

        private static readonly float[] LastAt = { -999f, -999f, -999f, -999f };

        public static SoundCue CueOf(SoundLibrary lib, AlertLevel level)
        {
            switch (level)
            {
                case AlertLevel.Info: return lib.AlertInfo;
                case AlertLevel.Caution: return lib.AlertCaution;
                case AlertLevel.Danger: return lib.AlertDanger;
                default: return lib.AlertCritical;
            }
        }

        /// <summary>그 단계 소리를 2D로 (서비스가 없거나 위계 · 쉬는 시간에 걸리면 false).</summary>
        public static bool TryPlay(AlertLevel level, float volume = 1f)
        {
            var s = AudioService.Instance;
            if (s == null || s.Library == null)
                return false;
            float now = Time.unscaledTime;
            int k = (int)level;
            for (int i = k + 1; i < LastAt.Length; i++)
            {
                float since = now - LastAt[i]; // 플레이를 다시 켜면 시계가 0부터라 음수 = 지난 판 기록 (무시)
                if (since >= 0f && since < HigherHold)
                    return false;
            }
            if (!s.Play(CueOf(s.Library, level), volume))
                return false;
            for (int i = 0; i < k; i++)
            {
                if (LastAt[i] == now)
                    s.StopStartedThisFrame(CueOf(s.Library, (AlertLevel)i));
            }
            LastAt[k] = now;
            return true;
        }

        /// <summary>
        /// 그 큐가 이 프레임에 막 울렸는지 — 한 동작에 "완료" 소리가 둘 겹칠 때 하나만 남기기 (2026-11-25: F 길게 누르기 완료 화음 hold_done이
        /// 울린 같은 프레임의 사고 해결 RepairDone · 현장 수리 Maintain은 생략).
        /// </summary>
        public static bool PlayedThisFrame(System.Func<SoundLibrary, SoundCue> pick)
        {
            var s = AudioService.Instance;
            if (s == null || s.Library == null)
                return false;
            var cue = pick(s.Library);
            return cue != null && cue.LastPlayed == Time.unscaledTime;
        }
    }

    /// <summary>
    /// 소리가 없던 경고 (역병 · 파업 · 외부 접촉 · 침입 드론)를 단계가 바뀌는 순간에 위계 소리로 (D-3 "쓰는 곳").
    /// 시뮬레이션은 바꾸지 않고 공개 상태만 읽음 — 각 시스템의 Changed마다 이전 단계와 견줌. 불러온 판의 상태는 처음 2초 동안 소리 없이 맞춤.
    /// </summary>
    internal sealed class AlertWatch
    {
        private const float Quiet = 2f;

        private readonly StationSimulation _sim;
        private readonly float _bornAt;
        private PlagueSystem _plague;
        private PlagueSystem.PlaguePhase _plaguePhase;
        private SocialCrisisSystem.CrisisPhase _crisisPhase;
        private ContactSystem.ContactPhase _contactPhase;
        private bool _boarding;

        public AlertWatch(StationSimulation sim)
        {
            _sim = sim;
            _bornAt = Time.unscaledTime;
        }

        public void Bind(bool on)
        {
            if (_sim == null)
                return;
            if (on)
            {
                _plague = _sim.GetSystem<PlagueSystem>();
                Sync();
            }
            if (_plague != null)
            {
                if (on) _plague.Changed += HandlePlague;
                else _plague.Changed -= HandlePlague;
            }
            if (_sim.Crisis != null)
            {
                if (on) _sim.Crisis.Changed += HandleCrisis;
                else _sim.Crisis.Changed -= HandleCrisis;
            }
            if (_sim.Contact != null)
            {
                if (on) _sim.Contact.Changed += HandleContact;
                else _sim.Contact.Changed -= HandleContact;
            }
        }

        private void Sync()
        {
            if (_plague != null) _plaguePhase = _plague.Phase;
            if (_sim.Crisis != null) _crisisPhase = _sim.Crisis.Phase;
            if (_sim.Contact != null)
            {
                _contactPhase = _sim.Contact.Phase;
                _boarding = _sim.Contact.Boarding;
            }
        }

        private bool Quieted => Time.unscaledTime - _bornAt < Quiet || _sim.Session.IsGameOver;

        private void HandlePlague()
        {
            var p = _plague.Phase;
            if (p != _plaguePhase && !Quieted)
            {
                if (p == PlagueSystem.PlaguePhase.Warning)
                    AlertSound.TryPlay(AlertLevel.Caution); // 역병 징후 (대응 시간)
                else if (p == PlagueSystem.PlaguePhase.Outbreak)
                    AlertSound.TryPlay(AlertLevel.Danger); // 역병 발생
            }
            _plaguePhase = p;
        }

        private void HandleCrisis()
        {
            var p = _sim.Crisis.Phase;
            if (p != _crisisPhase && !Quieted)
            {
                if (p == SocialCrisisSystem.CrisisPhase.Warning)
                    AlertSound.TryPlay(AlertLevel.Caution); // 파업 조짐
                else if (p == SocialCrisisSystem.CrisisPhase.Striking)
                    AlertSound.TryPlay(AlertLevel.Danger); // 파업 발생
            }
            _crisisPhase = p;
        }

        private void HandleContact()
        {
            var c = _sim.Contact;
            bool boarding = c.Boarding;
            if (!Quieted)
            {
                if (boarding && !_boarding)
                    AlertSound.TryPlay(AlertLevel.Danger); // 침입 드론
                else if (c.Phase != _contactPhase)
                {
                    if (c.Phase == ContactSystem.ContactPhase.Approaching)
                        AlertSound.TryPlay(AlertLevel.Info); // 미확인 함선 접근
                    else if (c.Phase == ContactSystem.ContactPhase.Waiting)
                        AlertSound.TryPlay(AlertLevel.Caution); // 함선 도착 · 대응 시간
                }
            }
            _contactPhase = c.Phase;
            _boarding = boarding;
        }
    }
}
