using System;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 데이터 센터(서버 홀) 표시등 · 상황판 — 사용자 결정 2026-10-19.
    /// 랙 표시등 3묶음(INT_DataCenter_LedA/B/C): 연구 중 = 묶음마다 제각각 빠르게 깜빡임 / 대기 = 천천히 숨 쉬듯 / 모듈이 꺼지면 꺼짐
    /// (색이 아니라 깜빡임 · 밝기로 구분 — 색약). 벽 상황판 · 책상 모니터 글자 = 연구 처리 상태 · 데이터 센터 연구 속도 보너스.
    /// </summary>
    public sealed class InteriorServerLights : InteriorRoomDisplay
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Serializable]
        public struct Panel
        {
            public Vector3 Center;
            public Vector3 Normal;
            public Vector3 Up;
            public float Width;
            public float Height;
        }

        [SerializeField] private Renderer[] _groups = Array.Empty<Renderer>();
        [SerializeField] private Panel _board;
        [SerializeField] private Panel _screen;

        private enum State { Off, Idle, Busy }

        private State _state = State.Idle;
        private Color[] _base;
        private float[] _level, _next;
        private MaterialPropertyBlock _block;
        private TextMeshPro _boardText, _screenText;

        public void Configure(Renderer[] groups, Panel board, Panel screen)
        {
            _groups = groups;
            _board = board;
            _screen = screen;
        }

        private void Setup()
        {
            if (_block != null)
                return;
            _block = new MaterialPropertyBlock();
            _base = new Color[_groups.Length];
            _level = new float[_groups.Length];
            _next = new float[_groups.Length];
            for (int i = 0; i < _groups.Length; i++)
            {
                var m = _groups[i] != null ? _groups[i].sharedMaterial : null;
                _base[i] = m != null && m.HasProperty(EmissionColorId) ? m.GetColor(EmissionColorId) : new Color(0.3f, 1f, 1.2f);
            }
            _boardText = CreateText("BoardText", _board.Center, _board.Normal, _board.Up, _board.Width, _board.Height, 2.2f, TextAlignmentOptions.Center);
            _screenText = CreateText("ScreenText", _screen.Center, _screen.Normal, _screen.Up, _screen.Width, _screen.Height, 1.2f,
                TextAlignmentOptions.Center);
        }

        protected override void Refresh()
        {
            Setup();
            var research = Station != null && Station.Simulation != null ? Station.Simulation.Research : null;
            int running = 0;
            if (research != null)
            {
                foreach (var p in research.Projects)
                {
                    if (!p.Paused)
                        running++;
                }
            }
            _state = !Active ? State.Off : running > 0 ? State.Busy : State.Idle;
            float bonus = Module != null && Module.Data != null ? Module.Data.ResearchSpeedBonus : 0f;
            string bonusText = bonus > 0f ? $"연구 속도 +{Mathf.RoundToInt(bonus * 100f)}%" : "";
            switch (_state)
            {
                case State.Busy:
                    SetText(_boardText, $"<b>연구 처리 중</b>\n{running}건 · {bonusText}", ScreenText);
                    SetText(_screenText, $"처리 중 {running}건", ScreenText);
                    break;
                case State.Idle:
                    SetText(_boardText, $"<b>연구 대기</b>\n{bonusText}", ScreenDim);
                    SetText(_screenText, "대기", ScreenDim);
                    break;
                default:
                    SetText(_boardText, "<b>전원 끊김</b>", ScreenDim);
                    SetText(_screenText, "꺼짐", ScreenDim);
                    break;
            }
        }

        private void Update()
        {
            if (_block == null)
                return;
            float t = Time.unscaledTime;
            for (int i = 0; i < _groups.Length; i++)
            {
                if (_groups[i] == null)
                    continue;
                float level;
                if (_state == State.Off)
                    level = 0f;
                else if (_state == State.Idle)
                    level = 0.4f + 0.25f * Mathf.Sin(t * 0.9f + i * 2.1f);   // 천천히 숨 쉬듯 (묶음마다 엇갈림)
                else
                {
                    if (t >= _next[i])
                    {
                        _level[i] = UnityEngine.Random.value < 0.3f ? 0.15f : UnityEngine.Random.Range(0.7f, 1.3f);
                        _next[i] = t + UnityEngine.Random.Range(0.06f, 0.22f);
                    }
                    level = _level[i];
                }
                _block.Clear();
                _block.SetColor(EmissionColorId, _base[i] * level);
                _groups[i].SetPropertyBlock(_block);
            }
        }
    }
}
