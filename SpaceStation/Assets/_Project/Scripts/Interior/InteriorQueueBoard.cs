using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 행정 관제소 민원 홀 번호 대기 — 사용자 결정 2026-10-20: 실제 주민이 움직여야 번호가 바뀜.
    /// - 창구 직원 = 창구 의자에 앉은 주민 (일터가 없는 주민, <see cref="PlacementRoom.OpenWork"/>). 직원이 없는 창구는 "창구 닫힘"(어둡게)이고 번호를 부르지 않음
    /// - 민원인 = 대기 의자의 쉬는 주민 (<see cref="ResidentWander"/>, 스스로 돌아다니지 않게 <see cref="ResidentWander.Hold"/>).
    ///   번호표가 없으면 가끔 발권기로 걸어가 번호표를 뽑고(발권기 번호가 그때만 올라감) 의자로 돌아옴
    /// - 직원이 있고 비어 있는 창구는 기다리는 번호 중 가장 작은 번호를 부르고, 그 민원인이 창구 앞으로 걸어가 몇 초 머문 뒤 돌아옴
    /// - 길 = 의자 앞 → (창구: 가운데 통로 → 줄 기둥 앞 통로 → 창구 앞 / 발권기: 의자 줄 뒤 앞벽 쪽 통로 → 발권기 앞). 방을 만들 때 비워 둔 통로.
    ///   발권기 길은 들어오는 곳(서쪽 문 앞 통로)을 피함 — 플레이어가 서 있으면 민원인이 계속 기다려 발권기가 묶였음 (2026-10-20 플레이 검수).
    ///   그래도 한 사람이 오래 막히면 발권기 · 창구를 놓아줌 (<see cref="StuckSeconds"/>)
    /// 바뀐 번호는 1.5초 밝기 깜빡임 (색약), 실제 시간(unscaled). 모듈이 꺼지면 "---".
    /// </summary>
    public sealed class InteriorQueueBoard : InteriorRoomDisplay
    {
        private const float FlashSeconds = 1.5f;
        private const float ScanSeconds = 1f;
        private const float StaffReach = 0.45f;   // 직원 의자에서 이만큼 안에 앉은 주민 = 직원
        private const float StuckSeconds = 40f;   // 심부름이 이보다 길면 발권기 · 창구를 다음 사람에게

        [Serializable]
        public struct Panel
        {
            public Vector3 Center;
            public Vector3 Normal;
            public Vector3 Up;
            public float Width;
            public float Height;
        }

        /// <summary>번호를 부르는 창구 하나 (프리팹 로컬).</summary>
        [Serializable]
        public struct Window
        {
            public Panel Board;
            [Tooltip("직원 의자 (여기 앉은 주민 = 직원)")]
            public Vector3 StaffSeat;
            [Tooltip("민원인이 서는 곳 (x, y, z) · 보는 방향(도)")]
            public Vector4 Counter;
        }

        [SerializeField] private Window[] _windows = Array.Empty<Window>();
        [SerializeField] private Panel _ticket;
        [Tooltip("민원인이 발권기 앞에 서는 곳 · 보는 방향(도)")]
        [SerializeField] private Vector4 _ticketStand;
        [Tooltip("가운데 통로 (이 z를 따라 걸음)")]
        [SerializeField] private float _aisleZ;
        [Tooltip("창구 줄 기둥 앞 통로 (이 x를 따라 창구 앞으로)")]
        [SerializeField] private float _laneX = 1.55f;
        [Tooltip("발권기로 가는 통로 (의자 줄 뒤 앞벽 쪽, 이 z를 따라 걸음)")]
        [SerializeField] private float _ticketLaneZ = 2.2f;
        [Tooltip("발권기 통로에서 발권기 앞으로 꺾는 곳")]
        [SerializeField] private Vector3 _ticketVia;
        [Tooltip("의자에서 일어서는 곳까지 (의자가 보는 쪽으로)")]
        [SerializeField] private float _seatFront = 0.5f;
        [SerializeField] private Vector2 _restSeconds = new Vector2(20f, 50f);
        [SerializeField] private Vector2 _serveSeconds = new Vector2(6f, 10f);

        private sealed class Visitor
        {
            public ResidentWander Wander;
            public int Ticket;           // 0 = 번호표 없음
            public float NextTicket;     // 이때부터 번호표를 뽑으러 감
            public bool Busy;            // 심부름 중 (발권기 · 창구)
        }

        private TextMeshPro[] _windowTexts;
        private TextMeshPro _ticketText;
        private int[] _serving;
        private float[] _flash;
        private bool[] _open;
        private Visitor[] _atWindow;
        private float[] _windowFree;   // 창구가 다시 부를 수 있는 때
        private float[] _windowSince;  // 지금 손님을 부른 때
        private float _machineSince;
        private readonly List<Visitor> _visitors = new List<Visitor>();
        private readonly List<ResidentFigure> _figures = new List<ResidentFigure>();
        private readonly List<Vector3> _route = new List<Vector3>();
        private Visitor _atMachine;
        private int _issued;
        private float _ticketFlash;
        private float _nextScan;
        private bool _seeded;
        private System.Random _rng;

        public void Configure(Window[] windows, Panel ticket, Vector4 ticketStand, float aisleZ, float laneX, float ticketLaneZ, Vector3 ticketVia)
        {
            _windows = windows;
            _ticket = ticket;
            _ticketStand = ticketStand;
            _aisleZ = aisleZ;
            _laneX = laneX;
            _ticketLaneZ = ticketLaneZ;
            _ticketVia = ticketVia;
        }

        private void Setup()
        {
            if (_windowTexts != null)
                return;
            int n = _windows.Length;
            _windowTexts = new TextMeshPro[n];
            _serving = new int[n];
            _flash = new float[n];
            _open = new bool[n];
            _atWindow = new Visitor[n];
            _windowFree = new float[n];
            _windowSince = new float[n];
            for (int i = 0; i < n; i++)
            {
                var p = _windows[i].Board;
                _windowTexts[i] = CreateText("QueueNumber", p.Center, p.Normal, p.Up, p.Width, p.Height, 4f, TextAlignmentOptions.Center);
            }
            _ticketText = CreateText("TicketNumber", _ticket.Center, _ticket.Normal, _ticket.Up, _ticket.Width, _ticket.Height, 1.2f, TextAlignmentOptions.Center);
            _rng = new System.Random(GetInstanceID());
            // 처음 번호: 창구마다 앞뒤 번호 (방에 들어오기 전에도 업무가 돌던 것처럼)
            _issued = _rng.Next(30, 70);
            for (int i = 0; i < n; i++)
                _serving[i] = _issued - (n - 1 - i);
        }

        protected override void Refresh()
        {
            Setup();
            float now = Time.unscaledTime;
            if (now >= _nextScan)
            {
                _nextScan = now + ScanSeconds;
                Scan(now);
            }
        }

        /// <summary>방 안 주민을 다시 봄: 직원 의자에 앉은 주민 → 창구 열림, 대기 의자의 쉬는 주민 → 민원인.</summary>
        private void Scan(float now)
        {
            var room = transform.parent != null ? transform.parent : transform;
            _figures.Clear();
            room.GetComponentsInChildren(_figures);
            for (int i = 0; i < _windows.Length; i++)
                _open[i] = false;
            foreach (var f in _figures)
            {
                if (f == null || f.GetComponent<ResidentWander>() != null)
                    continue;
                var local = transform.InverseTransformPoint(f.transform.position);
                for (int i = 0; i < _windows.Length; i++)
                {
                    var d = local - _windows[i].StaffSeat;
                    d.y = 0f;
                    if (d.magnitude < StaffReach)
                        _open[i] = true;
                }
            }
            // 민원인: 없어진 주민은 빼고(방을 다시 지음) 새 주민은 더함
            _visitors.RemoveAll(x => x.Wander == null);
            for (int i = 0; i < _atWindow.Length; i++)
            {
                if (_atWindow[i] != null && _atWindow[i].Wander == null)
                    _atWindow[i] = null;
            }
            if (_atMachine != null && _atMachine.Wander == null)
                _atMachine = null;
            bool first = !_seeded;
            foreach (var f in _figures)
            {
                var w = f != null ? f.GetComponent<ResidentWander>() : null;
                if (w == null || _visitors.Exists(x => x.Wander == w))
                    continue;
                w.Hold = true;
                var v = new Visitor { Wander = w, NextTicket = now + Range(3f, _restSeconds.y * 0.5f) };
                // 들어왔을 때 이미 몇 명은 번호표를 들고 기다리는 중
                if (first && _rng.NextDouble() < 0.5)
                    v.Ticket = ++_issued;
                _visitors.Add(v);
            }
            if (_visitors.Count > 0 || _figures.Count > 0)
                _seeded = true;
        }

        private void Update()
        {
            if (!Polled || _windowTexts == null)
                return;
            float now = Time.unscaledTime;
            if (Active)
            {
                // 오래 막힌 심부름 (플레이어가 길을 막고 서 있는 등): 발권기 · 창구를 다음 사람에게
                if (_atMachine != null && now - _machineSince > StuckSeconds)
                    _atMachine = null;
                for (int i = 0; i < _windows.Length; i++)
                {
                    if (_atWindow[i] != null && now - _windowSince[i] > StuckSeconds)
                        _atWindow[i] = null;
                }
                bool anyOpen = false;
                foreach (bool o in _open)
                    anyOpen |= o;
                if (anyOpen)
                    TryTakeTicket(now);
                for (int i = 0; i < _windows.Length; i++)
                {
                    if (_open[i] && _atWindow[i] == null && now >= _windowFree[i])
                        TryCall(i, now);
                }
            }
            Draw(now);
        }

        /// <summary>번호표가 없고 쉴 만큼 쉰 민원인 하나가 발권기로 (발권기는 한 번에 한 명).</summary>
        private void TryTakeTicket(float now)
        {
            if (_atMachine != null)
                return;
            foreach (var v in _visitors)
            {
                if (v.Ticket != 0 || v.Busy || now < v.NextTicket || !v.Wander.Seated)
                    continue;
                BuildRoute(v.Wander, _ticketLaneZ, _ticketVia, _ticketStand, toCounter: false);
                var visitor = v;
                bool started = v.Wander.StartErrand(_route, _ticketStand.w + transform.eulerAngles.y, 1.4f,
                    onArrive: null,
                    onLeave: () =>
                    {
                        // 번호표 나옴 → 발권기 번호가 그때 올라감
                        visitor.Ticket = ++_issued;
                        _ticketFlash = Time.unscaledTime + FlashSeconds;
                        if (_atMachine == visitor)
                            _atMachine = null;
                    },
                    onDone: () => visitor.Busy = false);
                if (started)
                {
                    v.Busy = true;
                    _atMachine = v;
                    _machineSince = now;
                }
                else
                    v.NextTicket = now + 2f;   // 일어설 수 없었음 — 조금 뒤 다시
                return;
            }
        }

        /// <summary>창구 하나가 기다리는 번호 중 가장 작은 번호를 부름 → 그 민원인이 창구 앞으로 걸어가 머문 뒤 돌아옴.</summary>
        private void TryCall(int window, float now)
        {
            Visitor next = null;
            foreach (var v in _visitors)
            {
                if (v.Ticket == 0 || v.Busy || !v.Wander.Seated)
                    continue;
                if (next == null || v.Ticket < next.Ticket)
                    next = v;
            }
            if (next == null)
                return;
            var c = _windows[window].Counter;
            BuildRoute(next.Wander, _aisleZ, new Vector3(_laneX, c.y, c.z), c, toCounter: true);
            var visitor = next;
            bool started = next.Wander.StartErrand(_route, c.w + transform.eulerAngles.y, Range(_serveSeconds.x, _serveSeconds.y),
                onArrive: null,
                onLeave: () =>
                {
                    if (_atWindow[window] == visitor)
                    {
                        _atWindow[window] = null;
                        _windowFree[window] = Time.unscaledTime + 1.5f;   // 다음 사람은 조금 뒤에
                    }
                },
                onDone: () =>
                {
                    visitor.Busy = false;
                    visitor.NextTicket = Time.unscaledTime + Range(_restSeconds.x, _restSeconds.y);
                });
            if (!started)
            {
                _windowFree[window] = now + 2f;
                return;
            }
            _serving[window] = next.Ticket;
            _flash[window] = now + FlashSeconds;
            next.Ticket = 0;
            next.Busy = true;
            _atWindow[window] = next;
            _windowSince[window] = now;
        }

        /// <summary>의자 앞 → 통로(z = <paramref name="laneZ"/>) → (창구: 줄 기둥 앞 통로) → 꺾는 곳 → 목적지 (월드 점, <see cref="_route"/>).</summary>
        private void BuildRoute(ResidentWander w, float laneZ, Vector3 via, Vector4 goal, bool toCounter)
        {
            _route.Clear();
            var seat = transform.InverseTransformPoint(w.transform.position);
            var forward = transform.InverseTransformDirection(w.transform.forward);
            var front = seat + forward * _seatFront;
            Add(new Vector3(front.x, goal.y, laneZ));
            if (toCounter)
                Add(new Vector3(_laneX, goal.y, laneZ));
            Add(via);
            Add(new Vector3(goal.x, goal.y, goal.z));
        }

        private void Add(Vector3 local)
        {
            var p = transform.TransformPoint(local);
            if (_route.Count > 0)
            {
                var d = p - _route[_route.Count - 1];
                d.y = 0f;
                if (d.sqrMagnitude < 0.01f)
                    return;
            }
            _route.Add(p);
        }

        private float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        private void Draw(float t)
        {
            bool anyOpen = false;
            for (int i = 0; i < _windowTexts.Length; i++)
            {
                if (!Active)
                    SetText(_windowTexts[i], "---", ScreenDim);
                else if (!_open[i])
                    SetText(_windowTexts[i], "<size=60%>창구 닫힘</size>", ScreenDim);
                else
                    SetText(_windowTexts[i], Number(_serving[i]), Blink(t, _flash[i]));
                anyOpen |= _open[i];
            }
            if (!Active)
                SetText(_ticketText, "<size=55%>번호표</size>\n---", ScreenDim);
            else if (!anyOpen)
                SetText(_ticketText, "<size=55%>번호표</size>\n발권 중지", ScreenDim);
            else
                SetText(_ticketText, "<size=55%>다음 번호</size>\n" + Number(_issued + 1), Blink(t, _ticketFlash));
        }

        private static string Number(int n) => (((n % 1000) + 1000) % 1000).ToString("000");

        /// <summary>바뀐 뒤 잠깐 밝음 ↔ 어두움 깜빡임 (0.25초씩).</summary>
        private static Color Blink(float t, float until) => t < until && Mathf.Repeat(until - t, 0.5f) < 0.25f ? ScreenDim : ScreenText;
    }
}
