using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 물류 허브 하역문 위 화물선 도착 안내판 (사용자 2026-10-31 물류 허브 시안 1 "이대로 유니티로 옮겨줘"):
    /// 이 모듈의 실제 화물선 시계 (<see cref="CargoSystem"/> 진행도 · 가동률로 남은 시간)와 화물선 주기.
    /// 화물선이 오면 10초 동안 깜빡이며 "화물선 도착 · 자원 +양" (한도가 가득이면 "저장 한도 가득") — 색이 아니라 글자 · 깜빡임으로 (색약).
    /// 모듈이 꺼지면 "운항 중지" (어둡게). 꼴은 여객 도킹 포트 이민선 안내판 <see cref="InteriorArrivalBoard"/>과 같음.
    /// </summary>
    public sealed class InteriorCargoBoard : InteriorRoomDisplay
    {
        private const float FlashSeconds = 10f;
        private const string Title = "<b>화물선 도착 안내</b>";

        [SerializeField] private InteriorArrivalBoard.Panel _panel;

        private TextMeshPro _title, _body;
        private CargoSystem _cargo;
        private float _flashUntil;
        private string _flash;

        public void Configure(InteriorArrivalBoard.Panel panel) => _panel = panel;

        private void Setup()
        {
            if (_title != null)
                return;
            var up = _panel.Up.normalized;
            _title = CreateText("CargoTitle", _panel.Center + up * (_panel.Height * 0.5f - 0.09f), _panel.Normal, _panel.Up, _panel.Width - 0.2f, 0.14f, 1.4f,
                TextAlignmentOptions.Center);
            _body = CreateText("CargoBody", _panel.Center - up * 0.07f, _panel.Normal, _panel.Up, _panel.Width - 0.12f, _panel.Height - 0.22f, 2.2f,
                TextAlignmentOptions.Center);
        }

        protected override void Refresh()
        {
            Setup();
            Hook();
            if (!Active)
            {
                Show(ScreenDim, "<b>운항 중지</b>", ScreenDim);
                return;
            }
            var sim = Station != null ? Station.Simulation : null;
            if (sim == null || Module == null || Module.Data == null || !Module.Data.IsCargoTerminal)
            {
                Show(ScreenText, "운항 일정 없음", ScreenDim);
                return;
            }
            float now = Time.unscaledTime;
            string every = $"<size=80%>{Module.Data.CargoInterval:0}초마다 · 가장 부족한 자원</size>";
            if (now < _flashUntil)
            {
                bool on = Mathf.Repeat(now, 1f) < 0.6f;
                Show(ScreenText, $"{_flash}\n{every}", on ? ScreenText : ScreenDim);
                return;
            }
            float strength = sim.Defense.GetStrength(Module);
            if (strength <= 0.01f)
            {
                Show(ScreenText, $"화물선 대기 중지 <size=80%>(가동률 0)</size>\n{every}", ScreenDim);
                return;
            }
            float left = (1f - sim.Cargo.GetProgress(Module)) * Module.Data.CargoInterval / strength;
            Show(ScreenText, $"다음 화물선  <b>{Clock(left)}</b> 후\n{every}", ScreenText);
        }

        /// <summary>화물선 도착 알림 (정거장이 바뀌면 다시 붙임).</summary>
        private void Hook()
        {
            var cargo = Station != null && Station.Simulation != null ? Station.Simulation.Cargo : null;
            if (cargo == _cargo)
                return;
            if (_cargo != null)
                _cargo.Delivered -= HandleDelivered;
            _cargo = cargo;
            if (_cargo != null)
                _cargo.Delivered += HandleDelivered;
        }

        private void OnDestroy()
        {
            if (_cargo != null)
                _cargo.Delivered -= HandleDelivered;
        }

        private void HandleDelivered(ModuleInstance terminal, ResourceType type, float amount)
        {
            if (terminal != Module)
                return;
            _flash = amount > 0.5f ? $"화물선 도착  <b>{type.DisplayName()} +{amount:0}</b>" : $"화물선 도착  <b>{type.DisplayName()}</b> (저장 한도 가득)";
            _flashUntil = Time.unscaledTime + FlashSeconds;
        }

        private void Show(Color titleColor, string body, Color bodyColor)
        {
            SetText(_title, Title, titleColor);
            SetText(_body, body, bodyColor);
        }
    }
}
