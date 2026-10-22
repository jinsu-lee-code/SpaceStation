using System;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 통신 중계소 해독실 화면 — 사용자 결정 2026-10-22 "신호판 + 평판".
    /// 신호판 (북쪽 벽): 맨 위 = 수신 상태 ("수신 대기 · 다음 함선 약 m분 ss초" / "함선 접근 · 도착 …" / "함선 도착 · 응답 마감 …"(깜빡임) /
    ///   "외부 접촉 전" / "전원 꺼짐"), 그 아래 = 해독 ("해독 중 n%" + 진행 막대 / "해독 불가 · 정체 불명" / "해독 완료 · 세력 계급"),
    ///   가운데 = 파형 (평소 잔잔한 잡음 · 해독 못 함 = 거친 잡음 · 해독 중 = 잡음이 점점 고른 물결로 · 해독 끝 = 고른 물결),
    ///   맨 아래 = 통신 세기 · 해독 범위 (침입 드론이 있으면 그 경보, 깜빡임).
    /// 평판판 (동쪽 벽): 세력 3개마다 이름 · 평판 단계(+값) · −100 ~ +100 막대 위 표시 · 직전 대응. 방문 중인(해독된) 세력은 "방문 중".
    /// 수신 기둥 빛 고리 = 위에서 아래로 흐름 (평소 느리게 하나 · 함선이 오면 빠르게 모두).
    /// 색이 아니라 글자 · 막대 자리 · 움직임으로 (색약). 값과 문구는 <see cref="ContactSystem"/> · 외부 접촉 창(U)과 같음.
    /// </summary>
    public sealed class InteriorSignalBoard : InteriorRoomDisplay
    {
        [Serializable]
        public struct Panel
        {
            public Vector3 Center;
            public Vector3 Normal;
            public Vector3 Up;
            public Vector2 Size;
        }

        private enum Wave
        {
            Off,
            Calm,
            Noise,
            Decoding,
            Clean,
        }

        private const int WaveBars = 36;
        private const int Factions = ContactSystem.FactionCount;

        [SerializeField] private Panel _signal;
        [SerializeField] private Panel _reputation;
        [SerializeField] private Material _barMaterial;
        [SerializeField] private Material _trackMaterial;
        [SerializeField] private Transform[] _pulses = Array.Empty<Transform>();
        [SerializeField] private float _pulseTravel = 2f;

        // 신호판
        private TextMeshPro _title, _status, _footer;
        private readonly Transform[] _wave = new Transform[WaveBars];
        private Transform _progress;
        private GameObject _progressTrack;
        private Vector3 _progressCenter, _sigRight, _sigNormal;
        private float _progressWidth, _waveBarWidth, _waveHalf;
        // 평판판
        private TextMeshPro _repTitle;
        private readonly TextMeshPro[] _repName = new TextMeshPro[Factions];
        private readonly TextMeshPro[] _repTier = new TextMeshPro[Factions];
        private readonly TextMeshPro[] _repLast = new TextMeshPro[Factions];
        private readonly Transform[] _repMarker = new Transform[Factions];
        private readonly Vector3[] _repTrack = new Vector3[Factions];
        private Vector3 _repRight, _repNormal;
        private float _repTrackWidth;
        // 움직임 (Update)
        private Vector3[] _pulseBase = Array.Empty<Vector3>();
        private Wave _mode;
        private float _decode;
        private bool _visiting;

        public void Configure(Panel signal, Panel reputation, Material bar, Material track, Transform[] pulses, float pulseTravel)
        {
            _signal = signal;
            _reputation = reputation;
            _barMaterial = bar;
            _trackMaterial = track;
            _pulses = pulses ?? Array.Empty<Transform>();
            _pulseTravel = pulseTravel;
        }

        private void Setup()
        {
            if (_title != null)
                return;
            // 신호판
            var up = _signal.Up.normalized;
            _sigNormal = _signal.Normal.normalized;
            _sigRight = Vector3.Cross(up, -_sigNormal);   // 화면을 보는 사람의 오른쪽
            var c = _signal.Center;
            float w = _signal.Size.x, h = _signal.Size.y;
            _title = CreateText("SignalTitle", c + up * (h * 0.5f - 0.1f), _sigNormal, up, w - 0.08f, 0.15f, 1.2f, TextAlignmentOptions.Center);
            _status = CreateText("SignalStatus", c + up * (h * 0.5f - 0.265f), _sigNormal, up, w - 0.08f, 0.12f, 0.75f, TextAlignmentOptions.Center);
            _progressWidth = w * 0.8f;
            _progressCenter = c + up * (h * 0.5f - 0.37f);
            _progressTrack = CreateQuad("SignalTrack", _progressCenter, _sigNormal, up, new Vector2(_progressWidth, 0.035f), _trackMaterial, 0.002f).gameObject;
            _progress = CreateQuad("SignalProgress", _progressCenter, _sigNormal, up, new Vector2(_progressWidth, 0.035f), _barMaterial, 0.003f);
            var waveCenter = c + up * -0.1f;
            float waveWidth = w * 0.88f;
            _waveHalf = 0.2f;
            _waveBarWidth = waveWidth / WaveBars * 0.55f;
            CreateQuad("SignalBase", waveCenter, _sigNormal, up, new Vector2(waveWidth, 0.006f), _trackMaterial, 0.002f);
            for (int i = 0; i < WaveBars; i++)
                _wave[i] = CreateQuad("SignalWave", waveCenter + _sigRight * (-waveWidth * 0.5f + (i + 0.5f) * waveWidth / WaveBars), _sigNormal, up,
                    new Vector2(_waveBarWidth, 0.01f), _barMaterial, 0.003f);
            _footer = CreateText("SignalFooter", c + up * (-h * 0.5f + 0.09f), _sigNormal, up, w - 0.08f, 0.12f, 0.6f, TextAlignmentOptions.Center);

            // 평판판
            up = _reputation.Up.normalized;
            _repNormal = _reputation.Normal.normalized;
            _repRight = Vector3.Cross(up, -_repNormal);
            c = _reputation.Center;
            w = _reputation.Size.x;
            h = _reputation.Size.y;
            _repTitle = CreateText("RepTitle", c + up * (h * 0.5f - 0.1f), _repNormal, up, w - 0.08f, 0.15f, 1.2f, TextAlignmentOptions.Center);
            float rowH = (h - 0.24f) / Factions;
            _repTrackWidth = w - 0.14f;
            for (int i = 0; i < Factions; i++)
            {
                float top = h * 0.5f - 0.22f - rowH * i;
                var line = c + up * (top - 0.065f);
                _repName[i] = CreateText("RepName", line + _repRight * (-w * 0.5f + 0.05f + w * 0.27f), _repNormal, up, w * 0.54f, 0.11f, 0.7f, TextAlignmentOptions.Left);
                _repTier[i] = CreateText("RepTier", line + _repRight * (w * 0.5f - 0.05f - w * 0.19f), _repNormal, up, w * 0.38f, 0.11f, 0.7f, TextAlignmentOptions.Right);
                _repTrack[i] = c + up * (top - 0.155f);
                CreateQuad("RepTrack", _repTrack[i], _repNormal, up, new Vector2(_repTrackWidth, 0.03f), _trackMaterial, 0.002f);
                CreateQuad("RepZero", _repTrack[i], _repNormal, up, new Vector2(0.008f, 0.07f), _trackMaterial, 0.0025f);
                _repMarker[i] = CreateQuad("RepMarker", _repTrack[i], _repNormal, up, new Vector2(0.03f, 0.08f), _barMaterial, 0.004f);
                _repLast[i] = CreateText("RepLast", c + up * (top - 0.235f), _repNormal, up, w - 0.1f, 0.07f, 0.45f, TextAlignmentOptions.Center);
            }

            _pulseBase = new Vector3[_pulses.Length];
            for (int i = 0; i < _pulses.Length; i++)
                _pulseBase[i] = _pulses[i] != null ? _pulses[i].localPosition : Vector3.zero;
        }

        protected override void Refresh()
        {
            Setup();
            var contact = Station != null && Station.Simulation != null ? Station.Simulation.Contact : null;
            bool blink = Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f;
            if (!Active)
            {
                _mode = Wave.Off;
                _visiting = false;
                SetText(_title, "<b>통신 중계소 · 전원 꺼짐</b>", ScreenDim);
                SetText(_status, "", ScreenDim);
                SetText(_footer, "", ScreenDim);
                SetProgress(-1f);
                SetText(_repTitle, "<b>세력 평판</b>", ScreenDim);
                ShowReputation(null, ScreenDim);
                return;
            }
            int power = contact != null ? contact.RelayPower : 0;
            string footer = contact != null ? $"통신 세기 {power} · 해독 {DecodeRange(contact.Config, power)}" : "";
            Color footerColor = ScreenText;
            if (contact == null || !contact.Unlocked)
            {
                _mode = Wave.Calm;
                _visiting = false;
                bool rules = Station.Simulation != null && Station.Simulation.Rules != null;
                SetText(_title, "<b>외부 접촉 전</b>  신호 대기", ScreenText);
                SetText(_status, rules ? "'방문 세력' 시스템이 열리면 함선이 찾아옵니다" : "지구급부터 함선이 찾아옵니다", ScreenDim);
                SetProgress(-1f);
            }
            else if (!contact.Visiting)
            {
                _mode = Wave.Calm;
                _visiting = false;
                SetText(_title, $"<b>수신 대기</b>  다음 함선 약 {Clock(contact.TimeLeft)}", ScreenText);
                SetText(_status, $"지금까지 방문 {contact.Visits}회", ScreenDim);
                SetProgress(-1f);
            }
            else
            {
                _visiting = true;
                bool arrived = contact.Phase == ContactSystem.ContactPhase.Waiting;
                SetText(_title, arrived ? $"<b>함선 도착</b>  응답 마감 {Clock(contact.TimeLeft)}" : $"<b>함선 접근</b>  도착 {Clock(contact.TimeLeft)}",
                    arrived && !blink ? ScreenDim : ScreenText);
                int need = contact.Config.RankPower(contact.Rank);
                if (contact.Decoded)
                {
                    _mode = Wave.Clean;
                    SetText(_status, $"<b>해독 완료</b>  {contact.VisitorName}", ScreenText);
                    SetProgress(1f);
                }
                else if (contact.CanDecode)
                {
                    _mode = Wave.Decoding;
                    _decode = contact.DecodeProgress;
                    SetText(_status, $"해독 중 <b>{contact.DecodeProgress * 100f:0}%</b>  · 세기 {power} / 필요 {need}", ScreenText);
                    SetProgress(contact.DecodeProgress);
                }
                else
                {
                    _mode = Wave.Noise;
                    SetText(_status, $"<b>해독 불가</b>  정체 불명 · 세기 {power} / 필요 {need}", ScreenText);
                    SetProgress(-1f);
                }
            }
            if (contact != null && contact.Boarding)
            {
                var target = contact.BoardingTarget;
                footer = $"<b>침입 드론</b>  {(target != null && target.Data != null ? target.Data.DisplayName : "모듈")} · 남은 드론 {contact.DronesLeft}/{contact.DronesTotal}";
                footerColor = blink ? ScreenText : ScreenDim;
            }
            SetText(_footer, footer, footerColor);
            SetText(_repTitle, "<b>세력 평판</b>", ScreenText);
            ShowReputation(contact != null && contact.Unlocked ? contact : null, ScreenText);
        }

        private void ShowReputation(ContactSystem contact, Color color)
        {
            for (int i = 0; i < Factions; i++)
            {
                var f = (ContactFaction)i;
                string name = ContactConfig.FactionName(f);
                if (contact == null)
                {
                    SetText(_repName[i], name, color);
                    SetText(_repTier[i], "-", ScreenDim);
                    SetText(_repLast[i], "", ScreenDim);
                    _repMarker[i].gameObject.SetActive(false);
                    continue;
                }
                bool visiting = contact.Visiting && contact.Decoded && contact.Faction == f;
                SetText(_repName[i], visiting ? $"<b>{name}</b>  · 방문 중" : name, color);
                float rep = contact.Reputation(f);
                var tier = contact.Tier(f);
                string tierName = ContactConfig.TierName(tier);
                SetText(_repTier[i], $"{(tier == ReputationTier.Hostile ? $"<b>{tierName}</b>" : tierName)} {rep:+0;-0;0}", color);
                var last = contact.LastChoice(f);
                SetText(_repLast[i], last == ContactChoice.None ? "직전 대응 없음" : $"직전 대응 {ContactConfig.ChoiceName(last, f)}", ScreenDim);
                _repMarker[i].gameObject.SetActive(true);
                float x = Mathf.Clamp(rep, -100f, 100f) / 200f * _repTrackWidth;
                _repMarker[i].localPosition = _repTrack[i] + _repRight * x + _repNormal * 0.004f;
            }
        }

        /// <summary>해독 진행 막대 (t &lt; 0 = 숨김, 0~1 = 왼쪽부터 채움).</summary>
        private void SetProgress(float t)
        {
            bool on = t >= 0f;
            _progressTrack.SetActive(on);
            _progress.gameObject.SetActive(on);
            if (!on)
                return;
            t = Mathf.Clamp01(t);
            _progress.localScale = new Vector3(Mathf.Max(0.001f, _progressWidth * t), _progress.localScale.y, 1f);
            _progress.localPosition = _progressCenter + _sigRight * (-_progressWidth * 0.5f + _progressWidth * t * 0.5f) + _sigNormal * 0.003f;
        }

        private static string DecodeRange(ContactConfig cfg, int power)
        {
            int top = -1;
            for (int r = 0; r < cfg.RankCount; r++)
                if (power >= cfg.RankPower(r))
                    top = r;
            return top < 0 ? "불가" : top == 0 ? "말단까지" : $"말단 ~ {ContactConfig.RankName(top)}";
        }

        private void Update()
        {
            if (_wave[0] == null)
                return;
            float t = Time.unscaledTime;
            bool off = _mode == Wave.Off;
            for (int i = 0; i < WaveBars; i++)
            {
                float x = i / (float)(WaveBars - 1);
                float rough = 0.15f + 0.85f * Mathf.PerlinNoise(i * 1.9f, t * 7f);
                float sine = 0.2f + 0.8f * Mathf.Abs(Mathf.Sin((x * 3f - t * 0.9f) * Mathf.PI));
                float a = _mode switch
                {
                    Wave.Calm => 0.04f + 0.08f * Mathf.PerlinNoise(i * 0.73f, t * 2.6f),
                    Wave.Noise => rough,
                    Wave.Decoding => Mathf.Lerp(rough, sine, _decode),
                    Wave.Clean => sine,
                    _ => 0f,
                };
                var bar = _wave[i];
                if (bar.gameObject.activeSelf == off)
                    bar.gameObject.SetActive(!off);
                if (!off)
                    bar.localScale = new Vector3(_waveBarWidth, Mathf.Max(0.004f, a * _waveHalf * 2f), 1f);
            }
            // 수신 기둥 빛 고리: 평소 하나가 느리게, 함선이 오면 모두 빠르게 (꺼지면 없음)
            int count = off ? 0 : _visiting ? _pulses.Length : Mathf.Min(1, _pulses.Length);
            float period = _visiting ? 1.4f : 4f;
            for (int k = 0; k < _pulses.Length; k++)
            {
                var p = _pulses[k];
                if (p == null)
                    continue;
                bool on = k < count;
                if (p.gameObject.activeSelf != on)
                    p.gameObject.SetActive(on);
                if (!on)
                    continue;
                float phase = Mathf.Repeat(t / period + k / (float)count, 1f);
                p.localPosition = _pulseBase[k] + Vector3.down * (phase * _pulseTravel);
            }
        }
    }
}
