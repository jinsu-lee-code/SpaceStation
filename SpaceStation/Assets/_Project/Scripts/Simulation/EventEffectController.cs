using System;
using System.Collections.Generic;
using System.Text;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 이벤트 4종의 실제 효과 적용 (3-3). 이벤트 데이터 타입으로 분기한다.
    /// 운석: 외곽 모듈 파손 / 산소 누출: 재고 비율 손실 / 태양 폭풍: 지속 중 전력 배율 / 보급선: 자원 추가.
    /// </summary>
    public sealed class EventEffectController : MonoBehaviour
    {
        [SerializeField] private EventController _events;
        [SerializeField] private ResourceController _resources;
        [SerializeField] private StationController _station;

        private readonly List<ModuleInstance> _candidates = new List<ModuleInstance>();

        /// <summary>(요약 메시지, 긍정 여부). 상태 표시줄 알림용.</summary>
        public event Action<string, bool> Reported;

        private void Start()
        {
            _events.Scheduler.EventStarted += HandleStarted;
            _events.Scheduler.EventEnded += HandleEnded;
        }

        private void OnDestroy()
        {
            if (_events == null || _events.Scheduler == null)
                return;
            _events.Scheduler.EventStarted -= HandleStarted;
            _events.Scheduler.EventEnded -= HandleEnded;
        }

        private void HandleStarted(GameEventData data)
        {
            switch (data)
            {
                case MeteorEventData _:
                    ApplyMeteor();
                    break;
                case OxygenLeakEventData leak:
                    ApplyOxygenLeak(leak);
                    break;
                case SolarStormEventData storm:
                    _resources.Simulation.PowerSupplyMultiplier = storm.PowerSupplyMultiplier;
                    Report($"전력 생산 -{(1f - storm.PowerSupplyMultiplier) * 100f:0}% ({storm.Duration:0}초)", false);
                    break;
                case SupplyShipEventData supply:
                    ApplySupply(supply);
                    break;
            }
        }

        private void HandleEnded(ActiveEvent active)
        {
            if (active.Data is SolarStormEventData)
                _resources.Simulation.PowerSupplyMultiplier = 1f;
        }

        private void ApplyMeteor()
        {
            _resources.Damage.FindMeteorCandidates(_station.Grid, _station.Core, _candidates);
            if (_candidates.Count == 0)
            {
                Report("운석이 정거장을 빗나갔습니다", true);
                return;
            }
            var target = _candidates[UnityEngine.Random.Range(0, _candidates.Count)];
            _resources.Damage.Damage(target);
            string name = target.Data != null ? target.Data.DisplayName : target.ToString();
            Report($"{name} 파손! {_resources.Balance.DestroyAfterSeconds:0}초 안에 수리하지 않으면 파괴됩니다 (선택 후 R)", false);
        }

        private void ApplyOxygenLeak(OxygenLeakEventData leak)
        {
            var sim = _resources.Simulation;
            float lost = sim.RemoveStock(ResourceType.Oxygen, sim.GetStock(ResourceType.Oxygen) * leak.StockLossRatio);
            Report($"산소 -{lost:0.#}", false);
        }

        private void ApplySupply(SupplyShipEventData supply)
        {
            var sim = _resources.Simulation;
            var sb = new StringBuilder();
            bool clamped = false;
            foreach (var reward in supply.Rewards)
            {
                float added = sim.AddStock(reward.Type, reward.Amount);
                if (added < reward.Amount - 1e-3f)
                    clamped = true;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(reward.Type.DisplayName()).Append(" +").Append(added.ToString("0.#"));
            }
            if (clamped)
                sb.Append(" (저장 한도 초과분 제외)");
            Report(sb.ToString(), true);
        }

        private void Report(string message, bool positive)
        {
            Reported?.Invoke(message, positive);
        }
    }
}
