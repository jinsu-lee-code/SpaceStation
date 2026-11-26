using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// 사람처럼 거주 늘리기 (사용자 지적 2026-11-26): "전력 같은 자원이 넉넉해지면 사람을 받을 거주를 늘리지, 거주부터 늘려 놓고 뒤늦게
    /// 자원을 채우지 않는다. 금속은 한정 자원이라 여유가 쌓이는 걸 보면서 거주를 짓는다."
    /// 전에는 금속이 차면(95%) 거주 · 다음 등급 모듈 수가 모자라면 거주를 지어, 도시급에 오를 때 수용이 인구의 7배쯤 (197 / 1,500)이었다
    /// → 제자리 봇에서 그 빈자리로 인구가 계속 늘어 생존 자원이 따라가지 못했음.
    /// 이제 거주는 ① 빈 거주가 미리 둘 칸 이하일 때만 ② 늘어날 주민(빈자리 + 새 수용)까지 전력 · 밤 배터리 · 산소 · 물 · 식량이 버틸 때만
    /// (못 버티면 그 자원 모듈을 먼저) ③ 짓고도 금속이 거주 반 채 값만큼 남을 때만 (거주 차례면 다른 데 쓰지 않고 모음).
    /// </summary>
    public sealed partial class BalanceBot
    {
        /// <summary>비교 측정용 스위치 (끄면 이전 봇 — 조건만 맞으면 바로 거주).</summary>
        public static bool HousingAfterResources = true;
        /// <summary>거주를 짓고도 남길 금속 = 거주 값 × 안전 여유 × 이 값 (0 = 값만큼 모이면 지음, 비교 측정용).</summary>
        public static float HousingSpareFactor = 0.5f; // 사용자 결정 2026-11-26: 거주 반 채 값 (한 채 값은 금속 수입이 적은 판의 중형 도달이 26분까지 늦어짐)

        /// <summary>
        /// 지금 거주를 늘려도 되면 거주(회전 링 포함), 자원이 먼저면 그 자원 모듈, 둘 다 아니면 null (다른 규칙으로 넘어감).
        /// </summary>
        private ModuleData HousingStep(ResourceSimulation r)
        {
            var house = PickHousing(r);
            if (house == null || !HousingAfterResources)
                return house;
            if (r.HousingCapacity - r.Population > _persona.HousingAhead)
                return Wait("빈자리 넉넉", null); // 빈 거주가 아직 넉넉함 — 더 늘리지 않음
            // 고장 난 것부터: 파손된 모듈이 있으면 거주를 미루고 금속도 모으지 않음 (수리 · 첫 방어가 먼저 쓰게)
            // 평소 봇 측정 (반 채 여유): 거주용으로 금속을 모으는 동안 파손이 쌓여 수리비 악순환 → 12판 중 2판 게임 오버
            // 맞고 있을 때의 방어는 PickNeed의 방어 먼저 규칙이 맡음 (생존 자원 · 밤 전력 다음 — 여기서 돌려주면 맨 위 거주 규칙 자리라 핵융합로 · 배터리보다 앞이 됨)
            if (_sim.Damage.DamagedCount > 0)
                return Wait("수리 먼저", null);
            // 회전 링(수용이 큼)이 안 되면 일반 거주로 다시 본다 (평소 봇 측정: 링 값 + 금속 여유가 한도보다 커서 영영 못 짓고 대형에서 80분 멈춤)
            var step = HousingStep(r, house);
            if (step == null && house != _housing && _housing != null)
                step = HousingStep(r, _housing);
            return step;
        }

        private ModuleData HousingStep(ResourceSimulation r, ModuleData house)
        {
            int future = Mathf.Max(0, r.HousingCapacity - r.Population) + _sim.Effects.Housing(house);
            // 전력: 밤을 배터리로 버티고, 낮 여유가 새 거주의 전력까지 감당해야 (모자라면 배터리 · 발전 먼저)
            if (NightUncovered(r))
                return Wait("밤 배터리", Buildable(_battery) ? _battery : null);
            if (PowerSurplus() < PowerHeadroom + PowerDemandOf(house))
                return Wait("낮 전력", Buildable(_power) ? _power : null);
            if (r.PowerEfficiency < 0.99f)
                return Wait("전력 부족 중", null); // 지금 전력이 모자라는 중 — 사람을 더 받지 않고 기다림
            // 산소 · 물 · 식량: 늘어날 주민 몫을 빼고도 짓는 기준 이상이어야 (모자라면 그 생산 먼저)
            if (Short(r, ResourceType.Oxygen, future))
                return Wait("산소", Buildable(_oxygen) ? _oxygen : null);
            if (Short(r, ResourceType.Water, future))
                return Wait("물", Buildable(_water) ? _water : null);
            if (Short(r, ResourceType.Food, future))
                return Wait("식량", Buildable(_food) ? _food : null);
            // 금속: 짓고도 여유가 남을 때 (여유가 쌓이는 걸 보면서 짓는다)
            // 남길 여유 = 거주 값 × 안전 여유 × HousingSpareFactor (반 채, 사용자 결정 2026-11-26), 금속 한도 안에서만
            // (값 + 여유가 한도를 넘으면 한도의 95% — 그만큼 쌓였으면 여유가 있는 것).
            // 처음엔 한도의 25%를 남기게 했더니 채굴 도킹 등이 금속을 먼저 써서 좀처럼 못 쌓아 중형 도달이 12.5 → 25.8분
            float capacity = r.GetCapacity(ResourceType.Metal);
            float spare = Cost(house) * _persona.SafetySpareScale * HousingSpareFactor;
            float need = Mathf.Min(Cost(house) + spare, capacity * 0.95f);
            if (Cost(house) > capacity)
                return Wait("금속 한도", null);
            if (r.GetStock(ResourceType.Metal) < need)
            {
                // 거주를 지을 차례 — 다른 것에 쓰지 않고 금속이 모일 때까지 기다린다 (Decide가 이 값까지 저축)
                // (처음엔 null을 돌려 아래 규칙으로 넘겼더니 채굴 도킹 · 방어가 금속을 먼저 써 여유가 끝내 안 쌓임 — 25분 중 1,062번 미룸)
                Wait("금속 모으는 중", null);
                _housingMetalNeed = need;
            }
            return house;
        }

        /// <summary>이번 결정에서 거주를 지으려고 모으는 금속 (0 = 없음). <see cref="PickNeed"/>마다 비움.</summary>
        private float _housingMetalNeed;

        /// <summary>진단: 거주를 미룬 이유별 결정 수 (모든 판 누적 — 비교 측정 전에 비움).</summary>
        public static readonly System.Collections.Generic.Dictionary<string, int> HousingWaitCounts = new System.Collections.Generic.Dictionary<string, int>();

        private static ModuleData Wait(string reason, ModuleData first)
        {
            HousingWaitCounts.TryGetValue(reason, out int n);
            HousingWaitCounts[reason] = n + 1;
            return first;
        }

        private bool Short(ResourceSimulation r, ResourceType type, int futureResidents)
        {
            return r.GetNetRate(type) - PerResident(r, type) * futureResidents < NetMargin;
        }

        /// <summary>주민 한 명이 쓰는 양 (초당, BalanceConfig.ConsumptionPerResident × 연구 · 정책 배율).</summary>
        private float PerResident(ResourceSimulation r, ResourceType type)
        {
            float sum = 0f;
            foreach (var a in _sim.Balance.ConsumptionPerResident)
                if (a.Type == type)
                    sum += a.Amount;
            return sum * r.GetResidentConsumptionMultiplier(type);
        }

        private bool Buildable(ModuleData data) => data != null && _sim.CheckBuildable(data) == PlacementResult.Valid;
    }
}
