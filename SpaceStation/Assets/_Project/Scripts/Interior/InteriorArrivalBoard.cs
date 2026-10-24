using System;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 여객 도킹 포트 도착 안내판 (칸막이 위 양면) — 사용자 결정 2026-10-21 "도착 안내판".
    /// 실제 이민선 시계(<see cref="SpaceStation.Simulation.ImmigrationSystem"/>): 다음 이민선까지 남은 시간 · 지금까지 도착한 주민 수.
    /// 이민선이 오면 10초 동안 깜빡이며 "이민선 도착 · n명 입국" (아무도 안 내렸으면 "내린 주민 없음") — 색이 아니라 글자 · 깜빡임으로 (색약).
    /// 모듈이 꺼지면 "운항 중지" (어둡게). 방 안 고정 표지 글자는 <see cref="InteriorSignLabels"/>.
    /// </summary>
    public sealed class InteriorArrivalBoard : InteriorRoomDisplay
    {
        private const float FlashSeconds = 10f;

        [Serializable]
        public struct Panel
        {
            public Vector3 Center;
            public Vector3 Normal;
            public Vector3 Up;
            public float Width;
            public float Height;
        }

        [SerializeField] private Panel[] _panels = Array.Empty<Panel>();

        private TextMeshPro[] _titles, _bodies;
        private int _lastArrived = -1;
        private int _lastVisits = -1;
        private float _flashUntil;
        private string _flash;

        public void Configure(Panel[] panels) => _panels = panels ?? Array.Empty<Panel>();

        private void Setup()
        {
            if (_titles != null)
                return;
            _titles = new TextMeshPro[_panels.Length];
            _bodies = new TextMeshPro[_panels.Length];
            for (int i = 0; i < _panels.Length; i++)
            {
                var p = _panels[i];
                var up = p.Up.normalized;
                _titles[i] = CreateText("ArrivalTitle", p.Center + up * (p.Height * 0.5f - 0.09f), p.Normal, p.Up, p.Width - 0.2f, 0.14f, 1.4f,
                    TextAlignmentOptions.Center);
                _bodies[i] = CreateText("ArrivalBody", p.Center - up * 0.07f, p.Normal, p.Up, p.Width - 0.12f, p.Height - 0.22f, 2.2f,
                    TextAlignmentOptions.Center);
            }
        }

        protected override void Refresh()
        {
            Setup();
            if (!Active)
            {
                Show("<b>도착 안내</b>", ScreenDim, "<b>운항 중지</b>", ScreenDim);
                return;
            }
            var immigration = Station != null && Station.Simulation != null ? Station.Simulation.Immigration : null;
            if (immigration == null)
            {
                Show("<b>도착 안내</b>", ScreenText, "운항 일정 없음", ScreenDim);
                return;
            }
            float now = Time.unscaledTime;
            int arrived = immigration.Arrived;
            int visits = immigration.Visits;
            float left = immigration.TimeLeft;
            // 이민선이 왔음: 도착 수가 늘었으면 입국, 그대로면 아무도 안 내림 (빈 집 · 만족도 부족)
            if (_lastVisits >= 0 && visits > _lastVisits)
            {
                StartFlash(now, arrived > _lastArrived ? $"이민선 도착  <b>{arrived - _lastArrived}명 입국</b>" : "이민선 출발  <b>내린 주민 없음</b>");
            }
            _lastArrived = arrived;
            _lastVisits = visits;

            string total = $"<size=80%>지금까지 도착  {arrived}명</size>";
            if (now < _flashUntil)
            {
                bool on = Mathf.Repeat(now, 1f) < 0.6f;
                Show("<b>도착 안내</b>", ScreenText, $"{_flash}\n{total}", on ? ScreenText : ScreenDim);
                return;
            }
            string next = left < 0f ? "운항 일정 없음" : $"다음 이민선  <b>{Clock(left)}</b> 후";
            Show("<b>도착 안내</b>", ScreenText, $"{next}\n{total}", ScreenText);
        }

        private void StartFlash(float now, string text)
        {
            _flash = text;
            _flashUntil = now + FlashSeconds;
        }

        private void Show(string title, Color titleColor, string body, Color bodyColor)
        {
            for (int i = 0; i < _titles.Length; i++)
            {
                SetText(_titles[i], title, titleColor);
                SetText(_bodies[i], body, bodyColor);
            }
        }
    }
}
