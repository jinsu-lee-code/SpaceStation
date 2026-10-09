using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 동물 주민 한 종류 (11-11d). 모델 = Blender `BlenderWork/Animal/animal_builder.py`가 내보낸 `SM_Animal_<종류>.fbx`
    /// (모든 종류가 같은 몸 · 리그 — 본 이름 = HumanBodyBones 이름). 재질 슬롯 이름(`M_Animal_<슬롯>`)마다 이 표의 색을 입힌다.
    /// </summary>
    [Serializable]
    public sealed class AnimalSpecies
    {
        public string Id;
        [Tooltip("이름표에 보이는 종류 이름 (예: 토끼)")]
        public string DisplayName;
        public GameObject Model;
        [Tooltip("주 털색 후보 (Fur 슬롯, 주민 번호로 하나)")]
        public Color[] FurColors = Array.Empty<Color>();
        [Tooltip("배 · 주둥이 · 귀 안쪽 (FurLight 슬롯)")]
        public Color Light = Color.white;
        [Tooltip("코 · 볼터치 (Pink 슬롯)")]
        public Color Accent = new Color(0.95f, 0.62f, 0.66f);
        [Tooltip("무늬 · 어두운 코 · 판다 팔다리 (Dark 슬롯)")]
        public Color Dark = new Color(0.15f, 0.13f, 0.13f);
        [Tooltip("부리 · 물갈퀴 (Beak 슬롯)")]
        public Color Beak = new Color(1f, 0.64f, 0.18f);
    }

    /// <summary>
    /// 11-11d 동물 주민 모델 묶음 (메뉴 SpaceStation/Interior/Wire Animal Residents). 재질은 슬롯마다 하나를 모든 동물이 공유하고
    /// 색은 MaterialPropertyBlock(재질 번호별)으로 — 눈(Eye · EyeHi)은 재질 색 그대로.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Animal Model Set", fileName = "AnimalModelSet")]
    public sealed class AnimalModelSet : ScriptableObject
    {
        public const string Fur = "Fur", FurLight = "FurLight", Pink = "Pink", Eye = "Eye", EyeHi = "EyeHi", Dark = "Dark", Beak = "Beak";

        [SerializeField] private AnimalSpecies[] _species = Array.Empty<AnimalSpecies>();
        [Tooltip("슬롯 공유 재질 (이름 M_Animal_<슬롯>)")]
        [SerializeField] private Material[] _materials = Array.Empty<Material>();

        public IReadOnlyList<AnimalSpecies> Species => _species;
        public bool IsValid => _species.Length > 0 && Array.TrueForAll(_species, s => s != null && s.Model != null);

        /// <summary>FBX 재질 이름(`M_Animal_Fur` 등, 뒤에 붙는 번호 무시)에서 슬롯 이름.</summary>
        public static string SlotOf(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
                return null;
            const string prefix = "M_Animal_";
            int i = materialName.IndexOf(prefix, StringComparison.Ordinal);
            if (i < 0)
                return null;
            string s = materialName.Substring(i + prefix.Length);
            int cut = s.IndexOfAny(new[] { '.', ' ', '(' });
            return cut >= 0 ? s.Substring(0, cut) : s;
        }

        public Material Material(string slot)
        {
            foreach (var m in _materials)
            {
                if (m != null && m.name == "M_Animal_" + slot)
                    return m;
            }
            return null;
        }

#if UNITY_EDITOR
        public void EditorSet(AnimalSpecies[] species, Material[] materials)
        {
            _species = species;
            _materials = materials;
        }
#endif
    }
}
