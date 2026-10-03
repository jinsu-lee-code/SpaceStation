using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 파손 모듈 위에 경고 아이콘 + 남은 시간(파괴까지 / 수리 완료까지)을 띄운다 (GDD 12-2).
    /// 화면 좌표 추적은 파손 모듈 수만큼만 매 프레임 계산한다. 라벨은 템플릿을 복제해 풀링한다.
    /// </summary>
    public sealed class DamageMarkers : MonoBehaviour
    {
        private sealed class Marker
        {
            public RectTransform Rect;
            public TMP_Text Text;
            public Vector3 World;
            public int ShownKey = int.MinValue;
            public bool Seen;
        }

        [SerializeField] private ResourceController _resources;
        private const int SpreadWarnSeconds = 15; // 이 이하면 확산 경고 강조
        [SerializeField] private Camera _camera;
        [Tooltip("비활성 상태의 라벨 템플릿 (자식에 TMP_Text)")]
        [SerializeField] private RectTransform _template;
        [SerializeField] private RectTransform _container;
        [SerializeField] private float _heightOffset = 0.9f;

        private readonly Dictionary<ModuleInstance, Marker> _markers = new Dictionary<ModuleInstance, Marker>();
        private readonly Stack<Marker> _pool = new Stack<Marker>();
        private readonly List<ModuleInstance> _toRelease = new List<ModuleInstance>();

        private void Awake()
        {
            if (_camera == null)
                _camera = Camera.main;
            if (_container == null)
                _container = (RectTransform)transform;
            _template.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            var damage = _resources.Damage;
            if (damage == null)
                return;

            foreach (var m in _markers.Values)
                m.Seen = false;

            foreach (var info in damage.DamagedModules)
            {
                if (!_markers.TryGetValue(info.Module, out var marker))
                {
                    marker = Acquire();
                    marker.World = WorldAnchor(info.Module);
                    _markers.Add(info.Module, marker);
                }
                marker.Seen = true;
                UpdateMarker(marker, info);
            }

            _toRelease.Clear();
            foreach (var pair in _markers)
            {
                if (!pair.Value.Seen)
                    _toRelease.Add(pair.Key);
            }
            foreach (var module in _toRelease)
            {
                var marker = _markers[module];
                _markers.Remove(module);
                marker.Rect.gameObject.SetActive(false);
                marker.ShownKey = int.MinValue;
                _pool.Push(marker);
            }
        }

        private void UpdateMarker(Marker marker, DamageInfo info)
        {
            Vector3 screen = _camera.WorldToScreenPoint(marker.World);
            bool onScreen = screen.z > 0f;
            if (marker.Rect.gameObject.activeSelf != onScreen)
                marker.Rect.gameObject.SetActive(onScreen);
            if (!onScreen)
                return;
            marker.Rect.position = new Vector3(screen.x, screen.y, 0f);

            // 초 단위가 바뀔 때만 문자열 갱신 (수리 중은 음수 키로 구분)
            // 8-5 장갑: 파괴 시간 없음 → 0초로 두고 아래에서 "장갑" 표시
            int seconds = info.IsRepairing ? Mathf.CeilToInt(info.RepairRemaining) : info.NeverDestroyed ? 0 : Mathf.CeilToInt(info.TimeUntilDestroyed);
            int position = info.IsQueued ? _resources.Damage.GetQueuePosition(info.Module) : 0; // 4-6 대기 순번
            int spread = info.SpreadPending ? Mathf.CeilToInt(info.TimeUntilSpread) : 0; // 4-7 확산까지
            int key = info.IsRepairing ? -1 - seconds : seconds + position * 100000 + spread * 1000;
            if (key == marker.ShownKey)
                return;
            marker.ShownKey = key;
            string spreadText = spread <= 0 ? ""
                : spread <= SpreadWarnSeconds ? $"\n<color={HudText.Orange}><b>확산 {spread}초</b></color>"
                : $"\n<size=85%><color={HudText.Muted}>확산 {spread}초</color></size>";
            marker.Text.SetText(info.IsRepairing
                ? $"<color=#7FD8FF>수리 {seconds}초</color>"
                : info.NeverDestroyed
                    ? (position > 0 ? $"<color={HudText.Yellow}>대기 {position}</color> " : "") + $"<color={HudText.Muted}>장갑 파손</color>"
                : position > 0
                    ? $"<color={HudText.Yellow}>대기 {position}</color> <color={HudText.Red}>{seconds}초</color>{spreadText}"
                    : $"<color={HudText.Red}><b>!</b> {seconds}초</color>{spreadText}");
        }

        private Marker Acquire()
        {
            if (_pool.Count > 0)
            {
                var pooled = _pool.Pop();
                pooled.Rect.gameObject.SetActive(true);
                return pooled;
            }
            var rect = Instantiate(_template, _container);
            rect.gameObject.SetActive(true);
            return new Marker { Rect = rect, Text = rect.GetComponentInChildren<TMP_Text>(true) };
        }

        private Vector3 WorldAnchor(ModuleInstance module)
        {
            Vector3 sum = Vector3.zero;
            foreach (var cell in module.Cells)
                sum += GridConfig.CellToWorld(cell);
            return sum / module.Cells.Count + Vector3.up * (_heightOffset * GridConfig.CellSize);
        }
    }
}
