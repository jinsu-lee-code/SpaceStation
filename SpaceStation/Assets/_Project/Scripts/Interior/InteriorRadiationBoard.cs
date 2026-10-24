using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 방사선 차폐실 경보판 (금고문 통로 위 칸막이 양면) — 사용자 결정 2026-10-21 "방사선 경보판".
    /// 외부 방사선 상태 + 대피 수용 (지금 이 방에 들어와 있는 주민 / 정원 = 침상 + 벤치 자리).
    /// 방사선 폭풍은 아직 없어 늘 "정상" — 폭풍이 생기면(12-8) 여기서 경보 글 · 경광등으로 잇는다.
    /// 모듈이 꺼지면 "전원 꺼짐"(어둡게 — 색이 아니라 밝기로 구분, 색약).
    /// </summary>
    public sealed class InteriorRadiationBoard : InteriorRoomDisplay
    {
        private const float ScanSeconds = 1f;

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
        [Tooltip("대피 정원 (침상 + 벤치 자리)")]
        [SerializeField] private int _capacity = 12;

        private TextMeshPro[] _titles, _bodies;
        private readonly List<ResidentFigure> _figures = new List<ResidentFigure>();
        private float _nextScan;
        private int _inside;

        public void Configure(Panel[] panels, int capacity)
        {
            _panels = panels ?? Array.Empty<Panel>();
            _capacity = capacity;
        }

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
                _titles[i] = CreateText("RadiationTitle", p.Center + up * (p.Height * 0.5f - 0.1f), p.Normal, p.Up, p.Width - 0.3f, 0.16f, 1.4f,
                    TextAlignmentOptions.Center);
                _bodies[i] = CreateText("RadiationBody", p.Center - up * 0.08f, p.Normal, p.Up, p.Width - 0.12f, p.Height - 0.26f, 2.2f,
                    TextAlignmentOptions.Center);
            }
        }

        protected override void Refresh()
        {
            Setup();
            if (!Active)
            {
                Show("<b>방사선 경보</b>", ScreenDim, "<b>전원 꺼짐</b>", ScreenDim);
                return;
            }
            float now = Time.unscaledTime;
            if (now >= _nextScan)
            {
                _nextScan = now + ScanSeconds;
                var room = transform.parent != null ? transform.parent : transform;
                _figures.Clear();
                room.GetComponentsInChildren(_figures);   // 숨긴 주민은 세지 않음
                _inside = _figures.Count;
            }
            Show("<b>방사선 경보</b>", ScreenText, $"외부 방사선  <b>정상</b>\n<size=80%>대피 수용  {_inside} / {_capacity}명</size>", ScreenText);
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
