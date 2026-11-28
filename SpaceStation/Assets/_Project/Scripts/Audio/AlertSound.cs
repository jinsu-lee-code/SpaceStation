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
        // 단계마다 마지막에 울린 큐 (뜻 소리가 그 단계 자리를 맡았을 수 있음 — 같은 프레임 낮은 단계 멈추기에 씀)
        private static readonly SoundCue[] LastCue = new SoundCue[4];

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
            return TryPlayAs(level, CueOf(s.Library, level), volume);
        }

        /// <summary>
        /// 뜻 소리를 그 단계 자리에 (2026-11-26 바깥 알림 — 위계 소리와 뜻 소리가 같은 순간 둘 다 울리지 않게 하나로): 뜻 소리 파일은 그 단계 음형 위에
        /// "무엇인지" 층을 얹어 만듦 (사회 위기 = 주의 음형 + 긴장 스팅 · 역병 발생 = 위험 음형 + 불길음 · 접촉 도착 · 유성우 예보 · 과열). 위계 규칙 (높은 단계 우선 · 같은 프레임
        /// 낮은 것 멈춤)은 TryPlay와 같고, 그 단계가 울린 것으로 적힘. 큐에 클립이 없으면 그 단계 기본 소리.
        /// </summary>
        public static bool TryPlayAs(AlertLevel level, SoundCue cue, float volume = 1f, float pan = 0f)
        {
            var s = AudioService.Instance;
            if (s == null || s.Library == null)
                return false;
            if (cue == null || !cue.HasClip)
                cue = CueOf(s.Library, level);
            float now = Time.unscaledTime;
            int k = (int)level;
            if (HeldByHigher(k, now, LastAt))
                return false;
            if (!s.Play(cue, volume, pan))
                return false;
            for (int i = 0; i < k; i++)
            {
                if (LastAt[i] == now)
                    s.StopStartedThisFrame(LastCue[i] ?? CueOf(s.Library, (AlertLevel)i));
            }
            LastAt[k] = now;
            LastCue[k] = cue;
            return true;
        }

        /// <summary>k 단계보다 높은 단계가 HigherHold초 안에 울렸는지 (lastAt = 단계별 마지막 시각). 플레이를 다시 켜면 시계가 0부터라 음수 = 지난 판 기록 (무시).</summary>
        public static bool HeldByHigher(int k, float now, float[] lastAt)
        {
            for (int i = k + 1; i < lastAt.Length; i++)
            {
                float since = now - lastAt[i];
                if (since >= 0f && since < HigherHold)
                    return true;
            }
            return false;
        }

        /// <summary>그 단계 이상이 HigherHold초 안에 울렸는지 — 위계 밖 소리 (좋은 소식 · 연결 끊김)가 큰 경보에 바로 겹치지 않게 (2026-11-26).</summary>
        public static bool RecentAtLeast(AlertLevel level) => HeldByHigher((int)level - 1, Time.unscaledTime, LastAt);

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
    /// 2026-11-26 바깥 알림: 역병 발생 · 파업 조짐 · 해독된 함선 도착은 그 단계 자리에 뜻 소리 (TryPlayAs), 역병 막음 · 끝남 = 회복음 · 접촉 결과 = 확인음 (위계 밖).
    /// 시뮬레이션은 바꾸지 않고 공개 상태만 읽음 — 각 시스템의 Changed마다 이전 단계와 견줌. 불러온 판의 상태는 처음 2초 동안 소리 없이 맞춤.
    /// </summary>
    internal sealed class AlertWatch
    {
        private const float Quiet = 2f;

        private readonly StationSimulation _sim;
        private readonly float _bornAt;
        private PlagueSystem _plague;
        private PlagueSystem.PlaguePhase _plaguePhase;
        private CorruptionSystem _corruption;
        private CorruptionSystem.CorruptionPhase _corruptionPhase;
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
                _corruption = _sim.GetSystem<CorruptionSystem>();
                Sync();
            }
            if (_plague != null)
            {
                if (on) _plague.Changed += HandlePlague;
                else _plague.Changed -= HandlePlague;
            }
            if (_corruption != null)
            {
                if (on) _corruption.Changed += HandleCorruption;
                else _corruption.Changed -= HandleCorruption;
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
            if (_corruption != null) _corruptionPhase = _corruption.Phase;
            if (_sim.Crisis != null) _crisisPhase = _sim.Crisis.Phase;
            if (_sim.Contact != null)
            {
                _contactPhase = _sim.Contact.Phase;
                _boarding = _sim.Contact.Boarding;
            }
        }

        private bool Quieted => Time.unscaledTime - _bornAt < Quiet || _sim.Session.IsGameOver;

        private static SoundLibrary Lib => AudioService.Instance != null ? AudioService.Instance.Library : null;

        private void HandlePlague()
        {
            var p = _plague.Phase;
            if (p != _plaguePhase && !Quieted && Lib != null)
            {
                if (p == PlagueSystem.PlaguePhase.Warning)
                    AlertSound.TryPlay(AlertLevel.Caution); // 역병 징후 (대응 시간)
                else if (p == PlagueSystem.PlaguePhase.Outbreak)
                    AlertSound.TryPlayAs(AlertLevel.Danger, Lib.PlagueOutbreak); // 역병 발생 = 위험 음형 + 불길음 (2026-11-26 바깥 알림)
                else if (p == PlagueSystem.PlaguePhase.Calm)
                    NoticeSound.PlayGood(Lib.PlagueCured); // 막음 (방역 물자 · 격리 · 캠페인) · 끝남 = 회복음 (선택 버튼은 클릭음 없이 이것 하나)
            }
            _plaguePhase = p;
        }

        /// <summary>CONNECT_1121 7번 ② 비리 — 전용 소리는 예정 자리만 (CONNECT_1121 결정): 징후 = 주의 · 횡령 = 위험 · 막음 = 확인음 (선택 버튼은 클릭음 없이 이것 하나).</summary>
        private void HandleCorruption()
        {
            var p = _corruption.Phase;
            if (p != _corruptionPhase && !Quieted && Lib != null)
            {
                if (p == CorruptionSystem.CorruptionPhase.Warning)
                    AlertSound.TryPlay(AlertLevel.Caution);
                else if (p == CorruptionSystem.CorruptionPhase.Embezzling)
                    AlertSound.TryPlay(AlertLevel.Danger);
                else if (p == CorruptionSystem.CorruptionPhase.Calm && _corruptionPhase == CorruptionSystem.CorruptionPhase.Warning)
                    NoticeSound.Play(Lib.ContactResult);
            }
            _corruptionPhase = p;
        }

        private void HandleCrisis()
        {
            var p = _sim.Crisis.Phase;
            if (p != _crisisPhase && !Quieted && Lib != null)
            {
                if (p == SocialCrisisSystem.CrisisPhase.Warning)
                    AlertSound.TryPlayAs(AlertLevel.Caution, Lib.SocialCrisis); // 파업 조짐 (선택지) = 주의 음형 + 긴장 스팅 (2026-11-26)
                else if (p == SocialCrisisSystem.CrisisPhase.Striking)
                    AlertSound.TryPlay(AlertLevel.Danger); // 파업 발생
            }
            _crisisPhase = p;
        }

        private void HandleContact()
        {
            var c = _sim.Contact;
            bool boarding = c.Boarding;
            if (!Quieted && Lib != null)
            {
                if (boarding && !_boarding)
                    AlertSound.TryPlay(AlertLevel.Danger); // 침입 드론
                else if (c.Phase != _contactPhase)
                {
                    if (c.Phase == ContactSystem.ContactPhase.Approaching)
                        AlertSound.TryPlay(AlertLevel.Info); // 미확인 함선 접근
                    else if (c.Phase == ContactSystem.ContactPhase.Waiting)
                        AlertSound.TryPlayAs(AlertLevel.Caution, NoticeSound.ContactArriveCue(Lib, c.Faction, c.Decoded)); // 함선 도착 · 대응 시간 — 해독됐으면 세력 신호 (2026-11-26)
                    else if (c.Phase == ContactSystem.ContactPhase.Idle && _contactPhase == ContactSystem.ContactPhase.Waiting)
                        NoticeSound.Play(Lib.ContactResult); // 교역 · 협상 · 경계 · 응답 없이 떠남 (대응 버튼은 클릭음 없이 이것 하나 — 누른 답이라 큰 경보 뒤에도 울림)
                }
            }
            _contactPhase = c.Phase;
            _boarding = boarding;
        }
    }
}
