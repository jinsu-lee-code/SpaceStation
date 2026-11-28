using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// CONNECT_1121 7번 ③ 타이쿤판 대륙급 '고급 사슬' = 공급 사슬 (사용자 결정 2026-11-28, 추천안): 대륙급 고급 모듈(3단계 진화 · 치안 본부)이
    /// 금속 · 식량 · 물을 계속 먹어야 제 성능 — 아래 생산이 받쳐야 위가 돌아가는 Anno식. 끊기면 **효율만 떨어짐** (공급 비율만큼 효과, 바닥 있음 — 꺼지지 않음).
    /// 공급은 모듈의 소비(입력 0이면 정지)와 따로 셈 — 정거장 전체 소비에 더해지고, 재고가 바닥나면 그 자원의 생산 ÷ 소비 비율이 공급 비율.
    /// 값은 가안 (BALANCE 51번 뒤 "공급 사슬"). 메뉴 SpaceStation/Tycoon/Setup Supply Chain이 만든다.
    /// </summary>
    [CreateAssetMenu(fileName = "SupplyChainConfig", menuName = "SpaceStation/Tycoon/Supply Chain Config")]
    public sealed class SupplyChainConfig : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public ModuleData Module;
            [Tooltip("초당 공급 (금속 · 식량 · 물만 — 전력은 모듈 소비로, 산소는 누출 몫과 겹쳐 받지 않음)")]
            public List<ResourceAmount> Supply = new List<ResourceAmount>();
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();
        [Tooltip("공급이 아무리 모자라도 남는 효율 (꺼지지 않음 — 사용자 결정 '효율만 떨어짐')")]
        [SerializeField, Range(0f, 1f)] private float _minEfficiency = 0.25f;
        [Tooltip("재고가 이만큼 남아 있으면 공급 100%")]
        [SerializeField, Min(0f)] private float _stockFloor = 0.5f;

        public IReadOnlyList<Entry> Entries => _entries;
        public float MinEfficiency => _minEfficiency;
        public float StockFloor => _stockFloor;

        /// <summary>이 모듈의 공급 목록 (없으면 null).</summary>
        public IReadOnlyList<ResourceAmount> SupplyOf(ModuleData module)
        {
            if (module == null)
                return null;
            foreach (var e in _entries)
                if (e != null && e.Module == module && e.Supply != null && e.Supply.Count > 0)
                    return e.Supply;
            return null;
        }

#if UNITY_EDITOR
        /// <summary>메뉴 · 시험용.</summary>
        public void EditorSet(IEnumerable<(ModuleData Module, ResourceAmount[] Supply)> entries)
        {
            _entries.Clear();
            foreach (var (module, supply) in entries)
                if (module != null)
                    _entries.Add(new Entry { Module = module, Supply = new List<ResourceAmount>(supply) });
        }
#endif
    }
}
