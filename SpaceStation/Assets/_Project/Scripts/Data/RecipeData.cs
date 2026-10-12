using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>레시피 재료 한 종류 (모듈 + 개수).</summary>
    [Serializable]
    public struct RecipeIngredient
    {
        public ModuleData Module;
        [Min(1)] public int Count;

        public RecipeIngredient(ModuleData module, int count)
        {
            Module = module;
            Count = count;
        }
    }

    /// <summary>
    /// 12-3 합성 레시피 (PHASESET.md 3번 A). 결과 모듈의 점유 칸이 규격이고, 그 칸을 재료 모듈로 빈틈없이 채우면 합성할 수 있다.
    /// 어느 칸에 어떤 재료가 있는지는 자유(구성만 맞으면 됨, A3-1), 규격은 90도 회전 가능. 비용 · 시간은 BALANCE.md 32번.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Recipe Data", fileName = "RC_NewRecipe")]
    public sealed class RecipeData : ScriptableObject
    {
        [SerializeField] private string _displayName = "Recipe";

        [Tooltip("결과 진화 모듈. 점유 칸 = 합성 규격, 건설비 = 재료 건설비 합 + 합성 비용, 등급 모듈 수 = 재료 수 (메뉴 SpaceStation/Recipes/Setup이 맞춤)")]
        [SerializeField] private ModuleData _result;

        [SerializeField] private List<RecipeIngredient> _ingredients = new List<RecipeIngredient>();

        [Tooltip("합성 시작 때 내는 추가 비용 (건설 연구 할인 적용)")]
        [SerializeField] private List<ResourceAmount> _cost = new List<ResourceAmount>();

        [Tooltip("합성 시간 (게임 초). 파손된 재료가 있으면 멈춤")]
        [SerializeField, Min(0f)] private float _duration = 60f;

        [Tooltip("진화 단계 (1 = 도시급, 2 = 국가급, 3 = 대륙급)")]
        [SerializeField, Min(1)] private int _stage = 1;

        [Tooltip("이 등급(인덱스) 이상일 때만 합성 가능 (5 = 도시급)")]
        [SerializeField, Min(0)] private int _unlockGrade = 5;

        [Tooltip("연구 없이 해금 (연구소 계통 1단계 = 도시급 자동 해금, E2-3-2). 12-4 전까지는 시험 레시피도 켬")]
        [SerializeField] private bool _unlockedByDefault = true;

        [Tooltip("왜 이 재료들이 합쳐지는가 (당위성 한 줄, A2)")]
        [SerializeField, TextArea] private string _rationale;

        public string DisplayName => _displayName;
        public ModuleData Result => _result;
        public IReadOnlyList<RecipeIngredient> Ingredients => _ingredients;
        public IReadOnlyList<ResourceAmount> Cost => _cost;
        public float Duration => _duration;
        public int Stage => _stage;
        public int UnlockGrade => _unlockGrade;
        public bool UnlockedByDefault => _unlockedByDefault;
        public string Rationale => _rationale;

        /// <summary>재료 모듈 칸 수 합 (= 규격 칸 수여야 빈틈없이 채울 수 있음).</summary>
        public int IngredientCells
        {
            get
            {
                int n = 0;
                foreach (var i in _ingredients)
                    if (i.Module != null)
                        n += i.Module.CellOffsets.Count * i.Count;
                return n;
            }
        }

        /// <summary>등급 조건 모듈 수 (A4: 재료 개수만큼, 다단계는 기본 재료 누적).</summary>
        public int IngredientGradeModules
        {
            get
            {
                int n = 0;
                foreach (var i in _ingredients)
                    if (i.Module != null && i.Module.CountsTowardGrade)
                        n += i.Module.GradeModuleCount * i.Count;
                return n;
            }
        }

        /// <summary>이 레시피에 이 모듈이 재료로 몇 개 필요한지 (재료가 아니면 0).</summary>
        public int CountOf(ModuleData module)
        {
            int n = 0;
            foreach (var i in _ingredients)
                if (i.Module == module)
                    n += i.Count;
            return n;
        }

        private void OnValidate()
        {
            if (_result == null)
                return;
            if (IngredientCells != _result.CellOffsets.Count)
                Debug.LogWarning($"{name}: 재료 칸 수 {IngredientCells} ≠ 규격 칸 수 {_result.CellOffsets.Count}: 빈틈없이 채울 수 없음", this);
            if (IngredientGradeModules != _result.GradeModuleCount)
                Debug.LogWarning($"{name}: 결과 모듈의 등급 모듈 수 {_result.GradeModuleCount} ≠ 재료 수 {IngredientGradeModules}", this);
        }
    }
}
