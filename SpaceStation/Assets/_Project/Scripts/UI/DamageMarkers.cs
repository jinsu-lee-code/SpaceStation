using SpaceStation.Core;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 파손 모듈 위에 경고 아이콘 + 남은 시간(파괴까지 / 수리 완료까지)을 띄운다 (GDD 12-2).
    /// 12-0 (U-5) 가시성: 핀(모듈 윗면 위로 띄우고 연결선) · 겹치면 비켜 쌓기 · 화면 밖 방향 화살표 · 어두운 배경 + 큰 글자.
    /// 12-3c (사용자 피드백 2026-10-14): 배치 · 모양을 <see cref="WorldMarkerLayer"/>로 분리해 열 표기와 같은 창 테두리 스타일로 통일.
    /// </summary>
    public sealed class DamageMarkers : MonoBehaviour
    {
        [SerializeField] private ResourceController _resources;
        private const int SpreadWarnSeconds = 15; // 이 이하면 확산 경고 강조
        [SerializeField] private Camera _camera;
        [Tooltip("옛 라벨 템플릿 (12-3c부터 쓰지 않음 — 숨김만)")]
        [SerializeField] private RectTransform _template;
        [SerializeField] private RectTransform _container;
        [Tooltip("모듈 윗면 기준 높이 (칸 크기 배수)")]
        [SerializeField] private float _topOffset = 0.5f;

        [Header("12-0 가시성")]
        [SerializeField] private float _liftPixels = 44f;
        [SerializeField] private float _fontSize = 20f;
        [SerializeField] private Color _lineColor = new Color(1f, 0.35f, 0.25f, 0.85f);
        [SerializeField] private float _lineWidth = 2f;
        [SerializeField] private float _gap = 4f;
        [SerializeField] private float _edgeMargin = 24f;

        [Header("12-3c 창 스타일 (메뉴 SpaceStation/Grade Rules/Setup Heat가 연구 창 것을 연결)")]
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private HoloArt _holoArt;

        private static readonly Color RepairAccent = new Color(0.5f, 0.85f, 1f, 0.85f);
        private WorldMarkerLayer _layer;

        private void Awake()
        {
            if (_camera == null)
                _camera = Camera.main;
            if (_container == null)
                _container = (RectTransform)transform;
            if (_template != null)
            {
                if (_font == null)
                {
                    var t = _template.GetComponentInChildren<TMP_Text>(true);
                    if (t != null)
                        _font = t.font;
                }
                _template.gameObject.SetActive(false);
            }
            var ui = _holoArt != null || _frameSprite != null ? HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, null) : null;
            _layer = new WorldMarkerLayer(_container, _camera, ui, _font)
            {
                FontSize = _fontSize, LiftPixels = _liftPixels, LineWidth = _lineWidth, Gap = _gap, EdgeMargin = _edgeMargin,
            };
        }

        private void LateUpdate()
        {
            var damage = _resources.Damage;
            if (damage == null)
                return;
            _layer.Begin();
            foreach (var info in damage.DamagedModules)
            {
                var m = _layer.Show(info.Module, WorldAnchor(info.Module));
                UpdateText(m, info);
            }
            _layer.End();
        }

        private void UpdateText(WorldMarkerLayer.Marker marker, DamageInfo info)
        {
            // 초 단위가 바뀔 때만 문자열 갱신 (수리 중은 음수 키로 구분). 8-5 장갑: 파괴 시간 없음 → 0초 + "장갑"
            int seconds = info.IsRepairing ? Mathf.CeilToInt(info.RepairRemaining) : info.NeverDestroyed ? 0 : Mathf.CeilToInt(info.TimeUntilDestroyed);
            int position = info.IsQueued ? _resources.Damage.GetQueuePosition(info.Module) : 0; // 4-6 대기 순번
            int spread = info.SpreadPending ? Mathf.CeilToInt(info.TimeUntilSpread) : 0; // 4-7 확산까지
            int key = info.IsRepairing ? -1 - seconds : seconds + position * 100000 + spread * 1000;
            string spreadText = spread <= 0 ? ""
                : spread <= SpreadWarnSeconds ? $"\n<color={HudText.Orange}><b>확산 {spread}초</b></color>"
                : $"\n<size=85%><color={HudText.Muted}>확산 {spread}초</color></size>";
            string arrow = marker.Arrow.Length > 0 ? $"<color={HudText.Red}>{marker.Arrow}</color>" : "";
            string text = arrow + (info.IsRepairing
                ? $"<color=#7FD8FF>수리 {seconds}초</color>"
                : info.NeverDestroyed
                    ? (position > 0 ? $"<color={HudText.Yellow}>대기 {position}</color> " : "") + $"<color={HudText.Muted}>장갑 파손</color>"
                : position > 0
                    ? $"<color={HudText.Yellow}>대기 {position}</color> <color={HudText.Red}>{seconds}초</color>{spreadText}"
                    : $"<color={HudText.Red}><b>!</b> {seconds}초</color>{spreadText}");
            _layer.Set(marker, key, text, info.IsRepairing ? RepairAccent : _lineColor);
        }

        /// <summary>모듈 칸들의 가운데, 가장 높은 칸 윗면 근처 (월드).</summary>
        private Vector3 WorldAnchor(ModuleInstance module)
        {
            Vector3 sum = Vector3.zero;
            float top = float.MinValue;
            foreach (var cell in module.Cells)
            {
                var c = GridConfig.CellToWorld(cell);
                sum += c;
                top = Mathf.Max(top, c.y);
            }
            var center = sum / module.Cells.Count;
            center.y = top + _topOffset * GridConfig.CellSize;
            return center;
        }
    }
}
