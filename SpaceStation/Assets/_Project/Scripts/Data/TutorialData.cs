using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>튜토리얼 단계의 목표 종류 (Phase 9).</summary>
    public enum TutorialGoal
    {
        /// <summary>카메라 회전·확대·이동을 한 번씩.</summary>
        Camera,
        /// <summary>목록의 모듈을 종류마다 1개 이상 (코어에 연결된 것).</summary>
        Build,
        /// <summary>배속을 올리고 밤을 한 번 넘기기.</summary>
        SurviveNight,
        /// <summary>대본 운석에 맞은 모듈을 모두 수리.</summary>
        Repair,
    }

    /// <summary>안내 카드 외에 깜빡일 HUD 영역 (건설 단계는 모듈 버튼·탭을 자동으로 강조).</summary>
    public enum TutorialHighlight { None, Resources, TimeControl, Repair }

    [Serializable]
    public sealed class TutorialStep
    {
        [SerializeField] private string _title;
        [Tooltip("본문 (리치 텍스트, 줄바꿈 가능)")]
        [SerializeField, TextArea(3, 8)] private string _body;
        [SerializeField] private TutorialGoal _goal;
        [Tooltip("Build 목표: 지어야 할 모듈 (종류마다 1개)")]
        [SerializeField] private List<ModuleData> _modules = new List<ModuleData>();
        [SerializeField] private TutorialHighlight _highlight;
        [Tooltip("이 단계가 끝나기 전에는 첫 밤 직전에 시간을 멈춘다 (배터리)")]
        [SerializeField] private bool _holdBeforeFirstNight;

        public string Title => _title;
        public string Body => _body;
        public TutorialGoal Goal => _goal;
        public IReadOnlyList<ModuleData> Modules => _modules;
        public TutorialHighlight Highlight => _highlight;
        public bool HoldBeforeFirstNight => _holdBeforeFirstNight;

        public TutorialStep() { }

        public TutorialStep(string title, string body, TutorialGoal goal, TutorialHighlight highlight = TutorialHighlight.None,
            bool holdBeforeFirstNight = false, params ModuleData[] modules)
        {
            _title = title;
            _body = body;
            _goal = goal;
            _highlight = highlight;
            _holdBeforeFirstNight = holdBeforeFirstNight;
            _modules = new List<ModuleData>(modules);
        }
    }

    /// <summary>
    /// Phase 9 튜토리얼: 단계 목록과 진행 수치. 첫 새 게임에서 자동으로 시작한다 (설정에서 다시 켤 수 있음).
    /// 진행 중에는 무작위 이벤트를 멈추고, 마지막 단계에서 정해진 운석 1발로 파손·수리를 가르친다.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Tutorial", fileName = "TutorialData")]
    public sealed class TutorialData : ScriptableObject
    {
        [SerializeField] private List<TutorialStep> _steps = new List<TutorialStep>();

        [Header("카메라 단계 (누적량)")]
        [Tooltip("회전 각도 합 (도)")]
        [SerializeField] private float _cameraRotateDegrees = 45f;
        [Tooltip("확대/축소 거리 변화 합")]
        [SerializeField] private float _cameraZoomDistance = 4f;
        [Tooltip("초점 이동 거리 합")]
        [SerializeField] private float _cameraMoveDistance = 3f;

        [Header("첫 밤")]
        [Tooltip("해질녘이 시작되기 몇 초 전에 시간을 멈출지 (배터리 단계 미완료 시)")]
        [SerializeField] private float _nightHoldLead = 5f;
        [Tooltip("밤 넘기기 단계에서 요구하는 최소 배속")]
        [SerializeField] private float _requiredSpeed = 2f;

        [Header("자원 지원")]
        [Tooltip("다음에 지을 모듈(또는 수리) 비용이 이 시간(초, 게임 시간) 동안 모자라면 부족분을 보급")]
        [SerializeField] private float _supplyDelay = 8f;
        [Tooltip("보급 때 부족분에 더 얹어 주는 양")]
        [SerializeField] private float _supplyMargin = 5f;

        [Header("대본 운석")]
        [Tooltip("수리 단계가 시작되고 운석이 떨어질 때까지 (초)")]
        [SerializeField] private float _meteorDelay = 8f;
        [Tooltip("튜토리얼이 끝난 뒤 첫 무작위 이벤트까지 최소 시간 (초)")]
        [SerializeField] private float _postTutorialGrace = 180f;

        public IReadOnlyList<TutorialStep> Steps => _steps;
        public float CameraRotateDegrees => _cameraRotateDegrees;
        public float CameraZoomDistance => _cameraZoomDistance;
        public float CameraMoveDistance => _cameraMoveDistance;
        public float NightHoldLead => _nightHoldLead;
        public float RequiredSpeed => _requiredSpeed;
        public float SupplyDelay => _supplyDelay;
        public float SupplyMargin => _supplyMargin;
        public float MeteorDelay => _meteorDelay;
        public float PostTutorialGrace => _postTutorialGrace;

        /// <summary>에디터 설정·테스트용.</summary>
        public void SetSteps(IEnumerable<TutorialStep> steps)
        {
            _steps = new List<TutorialStep>(steps);
        }
    }
}
