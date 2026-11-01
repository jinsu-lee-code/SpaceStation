using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>14-4 사건 문구에 넣는 대상 (일어나는 순간 정해 저장).</summary>
    public enum SocialEventSubject
    {
        None,
        /// <summary>{a} {b} = 가장 큰 두 무리.</summary>
        BiggestTwo,
        /// <summary>{a} = 불만이 가장 큰 무리, {n} = 떠나겠다는 인원.</summary>
        MostDiscontent,
        /// <summary>{n} = 아픈 주민 (의료가 모자랄수록 많음).</summary>
        Sick,
    }

    /// <summary>14-4 청원이 묻는 근거 (앞서 내린 결정). 맞는 것이 있으면 먼저, 없으면 <see cref="Always"/>.</summary>
    public enum PetitionCause
    {
        Always,
        /// <summary>돌려보낸 이주민이 있음 ({n}명).</summary>
        RefusedShips,
        /// <summary>통제형 정책 시행 중 ({n}개).</summary>
        ControlPolicy,
        /// <summary>떠난 주민이 있음 ({n}명).</summary>
        Departed,
        /// <summary>봉인 기록을 덮어씀 ({n}개).</summary>
        OverwroteRecord,
        /// <summary>코어가 망각 쪽으로 기움.</summary>
        ForgotEarth,
        /// <summary>지난 위기를 깔끔히 넘기지 못함.</summary>
        LostOutcome,
    }

    /// <summary>14-4 사건 · 청원의 답 하나. 대가는 긴장도 단계 배율을 곱함 (긴장도를 낮추는 양에는 곱하지 않음).</summary>
    [Serializable]
    public sealed class SocialAnswer
    {
        [SerializeField] private string _title;
        [Tooltip("단추 설명 (대가는 값에서 자동으로 덧붙임)")]
        [SerializeField] private string _note;
        [SerializeField] private List<ResourceAmount> _cost = new List<ResourceAmount>();
        [Tooltip("긴장도 변화 (+ 오름 · − 내림)")]
        [SerializeField] private float _tension;
        [SerializeField] private float _satisfaction;
        [Tooltip("떠나는 수 = (이 값 + 비율 × 사건 인원) × 단계 배율")]
        [SerializeField, Min(0)] private int _leave;
        [SerializeField, Range(0f, 1f)] private float _leaveShare;
        [Tooltip("일시 효과 (없으면 비움)")]
        [SerializeField] private string _temporaryName;
        [SerializeField, Min(0f)] private float _temporarySeconds;
        [SerializeField] private List<PolicyModifier> _temporary = new List<PolicyModifier>();
        [Tooltip("이 긴장도 단계부터 고를 수 없음 (0 잔잔함 ~ 3 곧 터질 듯, 음수 = 늘 고를 수 있음)")]
        [SerializeField] private int _lockedFromLevel = -1;
        [SerializeField] private string _lockedReason;
        [SerializeField] private List<AxisShift> _shifts = new List<AxisShift>();
        [Tooltip("코어 기록 한 줄 ({a} {b} {n}, 코어 혼잣말)")]
        [SerializeField] private string _line;

        public string Title => _title;
        public string Note => _note;
        public IReadOnlyList<ResourceAmount> Cost => _cost;
        public float Tension => _tension;
        public float Satisfaction => _satisfaction;
        public int Leave => _leave;
        public float LeaveShare => _leaveShare;
        public string TemporaryName => _temporaryName;
        public float TemporarySeconds => _temporarySeconds;
        public IReadOnlyList<PolicyModifier> Temporary => _temporary;
        public int LockedFromLevel => _lockedFromLevel;
        public string LockedReason => _lockedReason;
        public IReadOnlyList<AxisShift> Shifts => _shifts;
        public string Line => _line;

        public SocialAnswer() { }

#if UNITY_EDITOR
        public SocialAnswer(string title, string note, float tension, string line, params AxisShift[] shifts)
        {
            _title = title;
            _note = note;
            _tension = tension;
            _line = line;
            _shifts.AddRange(shifts);
        }

        public SocialAnswer WithCost(params ResourceAmount[] cost)
        {
            _cost.AddRange(cost);
            return this;
        }

        public SocialAnswer WithLeave(int count, float share)
        {
            _leave = count;
            _leaveShare = share;
            return this;
        }

        public SocialAnswer WithSatisfaction(float amount)
        {
            _satisfaction = amount;
            return this;
        }

        public SocialAnswer WithTemporary(string name, float seconds, params PolicyModifier[] modifiers)
        {
            _temporaryName = name;
            _temporarySeconds = seconds;
            _temporary.AddRange(modifiers);
            return this;
        }

        public SocialAnswer LockedFrom(int level, string reason)
        {
            _lockedFromLevel = level;
            _lockedReason = reason;
            return this;
        }
#endif
    }

    /// <summary>14-4 장 중에 오는 사회 사건 하나.</summary>
    [Serializable]
    public sealed class SocialEvent
    {
        [SerializeField] private string _id;
        [Tooltip("어느 장 (번호, 1부터)")]
        [SerializeField, Min(1)] private int _chapter = 6;
        [SerializeField] private string _title;
        [TextArea(2, 4)]
        [SerializeField] private string _body;
        [Tooltip("장이 시작되고 이 시간(초) 뒤. 위기 예고가 먼저 시작되면 남은 사건을 간격을 두고 보냄")]
        [SerializeField, Min(0f)] private float _atSeconds;
        [SerializeField] private SocialEventSubject _subject;
        [SerializeField] private List<SocialAnswer> _answers = new List<SocialAnswer>();

        public string Id => _id;
        public int Chapter => _chapter;
        public string Title => _title;
        public string Body => _body;
        public float AtSeconds => _atSeconds;
        public SocialEventSubject Subject => _subject;
        public IReadOnlyList<SocialAnswer> Answers => _answers;

        public SocialEvent() { }

#if UNITY_EDITOR
        public SocialEvent(string id, int chapter, string title, string body, float atSeconds, SocialEventSubject subject, params SocialAnswer[] answers)
        {
            _id = id;
            _chapter = chapter;
            _title = title;
            _body = body;
            _atSeconds = atSeconds;
            _subject = subject;
            _answers.AddRange(answers);
        }
#endif
    }

    /// <summary>14-4 위기 '물음의 시간'의 청원 하나 (말하는 무리 · 묻는 근거 · 문장).</summary>
    [Serializable]
    public sealed class SocialPetition
    {
        [SerializeField] private string _id;
        [Tooltip("말하는 종족 Id (그 무리가 없으면 건너뜀)")]
        [SerializeField] private string _speaker;
        [SerializeField] private PetitionCause _cause;
        [TextArea(1, 3)]
        [SerializeField] private string _body;

        public string Id => _id;
        public string Speaker => _speaker;
        public PetitionCause Cause => _cause;
        public string Body => _body;

        public SocialPetition() { }

#if UNITY_EDITOR
        public SocialPetition(string id, string speaker, PetitionCause cause, string body)
        {
            _id = id;
            _speaker = speaker;
            _cause = cause;
            _body = body;
        }
#endif
    }

    /// <summary>
    /// 14-4 서사판 사회 사건 설정 (사용자 결정 2026-11-02 D-18). 사건은 늘 오고 긴장도 단계가 무게를 정함 (대가 배율 · 고를 수 있는 답),
    /// '곧 터질 듯'이면 소란이 한 번 더 옴. 메뉴 SpaceStation/Chapters/Setup Later Chapters가 만든다. 값은 그레이박스 초안.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Social Event Config", fileName = "SocialEvents")]
    public sealed class SocialEventConfig : ScriptableObject
    {
        [SerializeField] private List<SocialEvent> _events = new List<SocialEvent>();
        [Tooltip("긴장도가 '곧 터질 듯'이면 장마다 한 번 (사건 Chapter가 지금 장일 때만)")]
        [SerializeField] private SocialEvent _riot;
        [SerializeField] private List<SocialPetition> _petitions = new List<SocialPetition>();
        [SerializeField] private List<SocialAnswer> _petitionAnswers = new List<SocialAnswer>();

        [Header("무게 (긴장도 단계 0 잔잔함 ~ 3 곧 터질 듯)")]
        [SerializeField] private float _weightCalm = 0.5f;
        [SerializeField] private float _weightRestless = 0.75f;
        [SerializeField] private float _weightTense = 1f;
        [SerializeField] private float _weightCritical = 1.5f;

        [Header("때")]
        [Tooltip("위기 예고가 시작된 뒤 남은 사건 사이 간격 (초)")]
        [SerializeField, Min(1f)] private float _warningGap = 45f;
        [Tooltip("화면이 없을 때(봇 · 시험) 이 시간 뒤 마지막 답으로")]
        [SerializeField, Min(1f)] private float _autoResolveSeconds = 30f;

        [Header("인원")]
        [Tooltip("아픈 주민 = 인구 × (못 채운 의료 비율, 이 값 이상)")]
        [SerializeField, Range(0f, 1f)] private float _sickShareMin = 0.02f;
        [Tooltip("떠나겠다는 인원 = 인구 × 이 값")]
        [SerializeField, Range(0f, 1f)] private float _leavingShare = 0.03f;
        [Tooltip("코어가 이만큼 망각 쪽이면 '잊었느냐' 청원")]
        [SerializeField, Min(0f)] private float _forgetLean = 15f;

        public IReadOnlyList<SocialEvent> Events => _events;
        public SocialEvent Riot => _riot;
        public IReadOnlyList<SocialPetition> Petitions => _petitions;
        public IReadOnlyList<SocialAnswer> PetitionAnswers => _petitionAnswers;
        public float WarningGap => _warningGap;
        public float AutoResolveSeconds => _autoResolveSeconds;
        public float SickShareMin => _sickShareMin;
        public float LeavingShare => _leavingShare;
        public float ForgetLean => _forgetLean;

        public float Weight(int level)
        {
            switch (level)
            {
                case 0: return _weightCalm;
                case 1: return _weightRestless;
                case 2: return _weightTense;
                default: return _weightCritical;
            }
        }

        public SocialEvent Find(string id)
        {
            foreach (var e in _events)
                if (e != null && e.Id == id)
                    return e;
            return _riot != null && _riot.Id == id ? _riot : null;
        }

#if UNITY_EDITOR
        public void EditorSet(List<SocialEvent> events, SocialEvent riot, List<SocialPetition> petitions, List<SocialAnswer> petitionAnswers)
        {
            _events = events;
            _riot = riot;
            _petitions = petitions;
            _petitionAnswers = petitionAnswers;
        }
#endif
    }
}
