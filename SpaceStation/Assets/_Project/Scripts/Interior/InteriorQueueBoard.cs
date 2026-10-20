using System;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 행정 관제소 민원 홀 번호 대기 — 사용자 결정 2026-10-20: 번호가 시간에 따라 올라감.
    /// 발권기가 몇 초마다 번호표를 하나씩 뽑고(화면 = 다음 번호), 창구마다 몇 초마다 다음 번호를 부른다 (기다리는 번호가 없으면 그대로).
    /// 번호가 바뀐 창구 · 발권기 글자는 잠깐 밝기로 깜빡임 (색약). 실제 시간(unscaled)으로 — 방문 중 게임 시간이 멈춰 있어도 움직임.
    /// 모듈이 꺼지면 "---"(어둡게) · 멈춤.
    /// </summary>
    public sealed class InteriorQueueBoard : InteriorRoomDisplay
    {
        private const int MaxWaiting = 9;          // 기다리는 번호가 이만큼이면 발권 쉼
        private const float FlashSeconds = 1.5f;

        [Serializable]
        public struct Panel
        {
            public Vector3 Center;
            public Vector3 Normal;
            public Vector3 Up;
            public float Width;
            public float Height;
        }

        [SerializeField] private Panel[] _windows = Array.Empty<Panel>();
        [SerializeField] private Panel _ticket;
        [SerializeField] private Vector2 _issueSeconds = new Vector2(4f, 8f);
        [SerializeField] private Vector2 _serveSeconds = new Vector2(7f, 14f);

        private TextMeshPro[] _windowTexts;
        private TextMeshPro _ticketText;
        private int[] _serving;
        private float[] _nextServe, _flash;
        private int _issued, _called;
        private float _nextIssue, _ticketFlash;
        private bool _running;

        public void Configure(Panel[] windows, Panel ticket)
        {
            _windows = windows;
            _ticket = ticket;
        }

        private void Setup()
        {
            if (_windowTexts != null)
                return;
            int n = _windows.Length;
            _windowTexts = new TextMeshPro[n];
            _serving = new int[n];
            _nextServe = new float[n];
            _flash = new float[n];
            for (int i = 0; i < n; i++)
            {
                var p = _windows[i];
                _windowTexts[i] = CreateText("QueueNumber", p.Center, p.Normal, p.Up, p.Width, p.Height, 4f, TextAlignmentOptions.Center);
            }
            _ticketText = CreateText("TicketNumber", _ticket.Center, _ticket.Normal, _ticket.Up, _ticket.Width, _ticket.Height, 1.2f, TextAlignmentOptions.Center);
            // 처음: 이미 몇 명이 기다리는 중 (창구마다 앞뒤 번호)
            _called = UnityEngine.Random.Range(30, 70);
            for (int i = 0; i < n; i++)
                _serving[i] = _called - 2 * (n - 1 - i);
            _issued = _called + UnityEngine.Random.Range(2, 5);
            float t = Time.unscaledTime;
            _nextIssue = t + UnityEngine.Random.Range(_issueSeconds.x, _issueSeconds.y);
            for (int i = 0; i < n; i++)
                _nextServe[i] = t + UnityEngine.Random.Range(1f, _serveSeconds.y);
        }

        protected override void Refresh()
        {
            Setup();
            if (_running == Active)
                return;
            _running = Active;
            if (Active)
            {
                // 꺼져 있던 동안은 건너뜀 (다시 켜진 뒤 바로 몰아서 바뀌지 않게)
                float t = Time.unscaledTime;
                _nextIssue = t + UnityEngine.Random.Range(_issueSeconds.x, _issueSeconds.y);
                for (int i = 0; i < _nextServe.Length; i++)
                    _nextServe[i] = t + UnityEngine.Random.Range(1f, _serveSeconds.y);
            }
            Draw(Time.unscaledTime);
        }

        private void Update()
        {
            if (!Polled || _windowTexts == null)
                return;
            float t = Time.unscaledTime;
            if (_running)
            {
                if (t >= _nextIssue)
                {
                    if (_issued - _called < MaxWaiting)
                    {
                        _issued++;
                        _ticketFlash = t + FlashSeconds;
                    }
                    _nextIssue = t + UnityEngine.Random.Range(_issueSeconds.x, _issueSeconds.y);
                }
                for (int i = 0; i < _serving.Length; i++)
                {
                    if (t < _nextServe[i])
                        continue;
                    if (_called < _issued)
                    {
                        _called++;
                        _serving[i] = _called;
                        _flash[i] = t + FlashSeconds;
                    }
                    _nextServe[i] = t + UnityEngine.Random.Range(_serveSeconds.x, _serveSeconds.y);
                }
            }
            Draw(t);
        }

        private void Draw(float t)
        {
            for (int i = 0; i < _windowTexts.Length; i++)
            {
                if (!_running)
                    SetText(_windowTexts[i], "---", ScreenDim);
                else
                    SetText(_windowTexts[i], Number(_serving[i]), Blink(t, _flash[i]));
            }
            if (!_running)
                SetText(_ticketText, "<size=55%>번호표</size>\n---", ScreenDim);
            else
                SetText(_ticketText, "<size=55%>다음 번호</size>\n" + Number(_issued + 1), Blink(t, _ticketFlash));
        }

        private static string Number(int n) => (((n % 1000) + 1000) % 1000).ToString("000");

        /// <summary>바뀐 뒤 잠깐 밝음 ↔ 어두움 깜빡임 (0.25초씩).</summary>
        private static Color Blink(float t, float until) => t < until && Mathf.Repeat(until - t, 0.5f) < 0.25f ? ScreenDim : ScreenText;
    }
}
