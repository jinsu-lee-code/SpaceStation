using System;
using SpaceStation.Audio;
using SpaceStation.Building;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 드론 정비창 옥상 드론 라이트 쇼 관람 (사용자 2026-11-05 시안 1 "좋아 이대로 유니티로 옮겨줘"): 드론은 하늘 화면에 뜬다 —
    /// 옥상 위 하늘 껍질까지 3.6m뿐이라 안에 띄우지 않고 바깥 모델의 <see cref="DroneShowFx"/>를 그대로 보여 줌 (<see cref="InteriorExteriorView"/>가 드론 빛은 숨기지 않음).
    /// 쇼 버튼을 누르거나 관람석에 앉으면 시작: 드론이 높이 올라가 크게 벌어져 대형 다섯을 차례로 이룸. 앉아 있는 동안은 다시 처음부터 이어 감.
    /// 관제실 · 로비 화면 = 지금 대형 · 남은 시간. 모듈이 꺼지면 시작 못 함 (쇼 중이면 그쳐서 내려옴). 실제 시간 (unscaled).
    /// </summary>
    public sealed class InteriorDroneShow : InteriorRoomDisplay
    {
        private const float ButtonDown = 0.03f;

        [Serializable]
        public struct Screen
        {
            public Vector3 Center;
            public Vector3 Normal;
            public float Width;
            public float Height;
        }

        [SerializeField] private Transform _button;
        [Tooltip("앉으면 쇼가 시작되는 관람석")]
        [SerializeField] private InteriorBenchSeats _stands;
        [Tooltip("큰 화면 (제목 + 지금 대형 · 남은 시간)")]
        [SerializeField] private Screen[] _boards = Array.Empty<Screen>();
        [Tooltip("관제 책상 작은 화면 셋 (드론 수 · 대형 · 쇼 상태)")]
        [SerializeField] private Screen[] _desks = Array.Empty<Screen>();

        private DroneShowFx _show;
        private TextMeshPro[] _titles, _bodies, _deskTexts;
        private float _showLeft;
        private float _buttonAt;
        private Vector3 _buttonBase;

        public void Configure(Transform button, InteriorBenchSeats stands, Screen[] boards, Screen[] desks)
        {
            _button = button;
            _stands = stands;
            _boards = boards;
            _desks = desks;
        }

        private void Awake()
        {
            if (_button != null)
            {
                _buttonBase = _button.localPosition;
                var use = _button.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "드론 쇼 시작";
                use.Blocked = () => !Active ? "전력이 없어 쇼를 할 수 없음" : _showLeft > 0f ? "드론 쇼 중" : _show == null ? "드론 준비 중" : null;
                use.Completed = StartShow;
            }
        }

        private void OnEnable()
        {
            if (_stands != null)
                _stands.SatDown += StartShow;
        }

        private void OnDisable()
        {
            if (_stands != null)
                _stands.SatDown -= StartShow;
            _showLeft = 0f;
            if (_show != null)
                _show.Grand = false;
        }

        private void Setup()
        {
            if (_titles != null)
                return;
            _titles = new TextMeshPro[_boards.Length];
            _bodies = new TextMeshPro[_boards.Length];
            for (int i = 0; i < _boards.Length; i++)
            {
                var s = _boards[i];
                _titles[i] = CreateText("DroneShowTitle", s.Center + Vector3.up * (s.Height * 0.5f - 0.26f), s.Normal, Vector3.up, s.Width - 0.3f, 0.36f, 3f, TextAlignmentOptions.Center);
                _bodies[i] = CreateText("DroneShowBody", s.Center - Vector3.up * 0.16f, s.Normal, Vector3.up, s.Width - 0.3f, s.Height - 0.62f, 4f, TextAlignmentOptions.Center);
            }
            _deskTexts = new TextMeshPro[_desks.Length];
            for (int i = 0; i < _desks.Length; i++)
            {
                var s = _desks[i];
                _deskTexts[i] = CreateText("DroneShowDesk", s.Center, s.Normal, Vector3.up, s.Width - 0.06f, s.Height - 0.06f, 1.2f, TextAlignmentOptions.Center);
            }
        }

        protected override void Refresh()
        {
            Setup();
            if (_show == null && Station != null && Module != null && Station.TryGetView(Module, out var view) && view != null)
                _show = view.GetComponentInChildren<DroneShowFx>(true);
            if (!Active)
            {
                _showLeft = 0f;
                ShowBoards("<b>드론 쇼</b>", "<b>전원 꺼짐</b>", ScreenDim);
                ShowDesks("전원 꺼짐", "전원 꺼짐", "전원 꺼짐", ScreenDim);
                return;
            }
            string form = _show != null ? _show.CurrentFormationName : "-";
            int count = _show != null ? _show.Count : 0;
            if (_showLeft > 0f)
            {
                ShowBoards("<b>드론 쇼</b>", $"지금 대형  <b>{form}</b>\n<size=70%>남은 시간 {Clock(_showLeft)}</size>", ScreenText);
                ShowDesks($"비행 드론\n<b>{count}대</b>", $"지금 대형\n<b>{form}</b>", "쇼\n<b>진행 중</b>", ScreenText);
            }
            else
            {
                ShowBoards("<b>드론 쇼</b>", "옥상 관람석에서 관람\n<size=70%>버튼을 누르거나 앉으면 시작</size>", ScreenText);
                ShowDesks($"비행 드론\n<b>{count}대</b>", $"지금 대형\n<b>{form}</b>", "쇼\n<b>대기</b>", ScreenText);
            }
        }

        private void ShowBoards(string title, string body, Color color)
        {
            for (int i = 0; i < _titles.Length; i++)
            {
                SetText(_titles[i], title, color);
                SetText(_bodies[i], body, color);
            }
        }

        private void ShowDesks(string a, string b, string c, Color color)
        {
            for (int i = 0; i < _deskTexts.Length; i++)
                SetText(_deskTexts[i], i == 0 ? a : i == 1 ? b : c, color);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_showLeft > 0f)
            {
                _showLeft -= dt;
                if (_showLeft <= 0f && _stands != null && _stands.IsSeated && Active && _show != null)
                    _showLeft = _show.BeginGrand();   // 앉아 있는 동안 이어 감
            }
            if (_show != null)
                _show.Grand = _showLeft > 0f && Active;
            if (_button != null)
            {
                _buttonAt = Mathf.MoveTowards(_buttonAt, _showLeft > 0f ? ButtonDown : 0f, 0.2f * dt);
                _button.localPosition = _buttonBase + Vector3.down * _buttonAt;
            }
        }

        private void StartShow()
        {
            if (_show == null || !Active || _showLeft > 0f)
                return;
            _showLeft = _show.BeginGrand();
            AudioService.TryPlay(l => l.UiOpen);
        }
    }
}
