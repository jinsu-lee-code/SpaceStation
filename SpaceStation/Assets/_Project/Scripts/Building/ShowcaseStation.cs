using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 5-9 메인 메뉴 전시 정거장. 저장된 배치(<see cref="ShowcaseLayout"/>)를 비용·해금 없이 그리드에 바로 놓고,
    /// 시뮬레이션 시간은 멈춰 둔다 (자원·이벤트 없음). 모듈 표시·통로·실드 연출은 게임과 같은 컴포넌트가 그린다.
    /// </summary>
    public sealed class ShowcaseStation : MonoBehaviour
    {
        [SerializeField] private StationController _station;
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private ShowcaseLayout _layout;

        private void Start()
        {
            if (_clock != null)
            {
                _clock.Clock.SetPaused(true);
                _clock.InputLocked = true; // P·F1~F3 무시
            }
            if (_station == null || _layout == null)
                return;
            var grid = _station.Simulation.Grid;
            int placed = 0;
            foreach (var e in _layout.Entries)
            {
                if (e.Module != null && grid.TryPlace(e.Module, e.Origin, e.Rotation, out _))
                    placed++;
            }
            if (placed < _layout.Entries.Count)
                Debug.LogWarning($"[Showcase] {_layout.Entries.Count - placed}개 배치 실패", this);
        }
    }
}
