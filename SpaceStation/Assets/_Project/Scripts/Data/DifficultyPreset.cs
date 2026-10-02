using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 난이도 프리셋 (5-9, BALANCE 21번). 기본 밸런스 에셋은 그대로 두고, 게임 시작 때 복사본에 덮어쓴다.
    /// 음수 값은 "기본값 유지".
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Difficulty Preset", fileName = "DIFF_New")]
    public sealed class DifficultyPreset : ScriptableObject
    {
        [SerializeField] private string _displayName = "노멀";
        [TextArea(2, 4)]
        [SerializeField] private string _description;

        [Header("덮어쓸 값 (음수 = 기본값 유지)")]
        [Tooltip("내구도 초당 감소량")]
        [SerializeField] private float _durabilityDecayPerSecond = -1f;
        [Tooltip("산소 고갈 지속 → 게임 오버 (초, 0 = 조건 끔)")]
        [SerializeField] private float _oxygenFailSeconds = -1f;
        [Tooltip("만족도 0 지속 → 게임 오버 (초, 0 = 조건 끔)")]
        [SerializeField] private float _satisfactionFailSeconds = -1f;
        [Tooltip("코어 주변 붕괴 지속 → 게임 오버 (초, 0 = 조건 끔)")]
        [SerializeField] private float _coreCollapseSeconds = -1f;
        [Tooltip("등급별 이벤트 간격 배율 (등급 순서, 음수 = 기본값 유지, 비우면 모두 유지)")]
        [SerializeField] private List<float> _eventIntervalMultipliers = new List<float>();

        public string DisplayName => _displayName;
        public string Description => _description;
        public float DurabilityDecayPerSecond => _durabilityDecayPerSecond;
        public float OxygenFailSeconds => _oxygenFailSeconds;
        public float SatisfactionFailSeconds => _satisfactionFailSeconds;
        public float CoreCollapseSeconds => _coreCollapseSeconds;
        public IReadOnlyList<float> EventIntervalMultipliers => _eventIntervalMultipliers;

        /// <summary>이 프리셋을 적용한 밸런스 복사본 (원본 에셋은 바뀌지 않음).</summary>
        public BalanceConfig ApplyTo(BalanceConfig source)
        {
            if (source == null)
                return null;
            var copy = Instantiate(source);
            copy.name = $"{source.name} ({name})";
            copy.ApplyDifficulty(this);
            return copy;
        }

        /// <summary>이 프리셋을 적용한 등급 설정 복사본.</summary>
        public StationGradeConfig ApplyTo(StationGradeConfig source)
        {
            if (source == null)
                return null;
            var copy = Instantiate(source);
            copy.name = $"{source.name} ({name})";
            copy.ApplyEventIntervals(_eventIntervalMultipliers);
            return copy;
        }
    }

    /// <summary>메인 메뉴에서 고른 시작 옵션 (씬을 넘어 유지, 재시작해도 같은 난이도).</summary>
    public static class GameStartOptions
    {
        /// <summary>null이면 게임 씬의 기본 난이도.</summary>
        public static DifficultyPreset Difficulty;

        /// <summary>불러올 저장 (메인 메뉴 [이어하기]/[불러오기], ESC [불러오기]). 게임 씬의 SimulationHost가 한 번 쓰고 비운다.</summary>
        public static SpaceStation.Save.SaveFile PendingLoad;
    }
}
