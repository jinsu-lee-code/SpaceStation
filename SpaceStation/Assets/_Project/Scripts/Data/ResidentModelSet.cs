using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    public enum GarmentSlot { Top, Bottom, Suit, Shoes, Under }

    /// <summary>옷 한 벌: 이름(모델 속 메시 `R2M_C_이름` · 텍스처 `cloth_이름_m|w`), 자리, 가리는 몸 구역, 입을 수 있는 성별, 함께 입는 옷.</summary>
    [Serializable]
    public sealed class GarmentDef
    {
        public string Name;
        public GarmentSlot Slot;
        [Tooltip("이 옷을 입으면 끄는 몸 구역 (몸 메시 `R2M_B_구역`) — 옷을 뚫고 나오지 않게")]
        public string[] Hides = Array.Empty<string>();
        public bool Man = true;
        public bool Woman = true;
        [Tooltip("함께 입히는 옷 (치마 → 팬티)")]
        public string With = "";
        [Tooltip("앉기 자세에서 켜는 블렌드셰이프 (미니스커트가 허벅지 위로 내려앉음), 없으면 비움")]
        public string SitShape = "";
    }

    /// <summary>
    /// 11-11b 주민 모델 묶음 (Blender `Interior_Kit.blend` 텍스트 `resident2_export` → `Art/Models/Resident/*.fbx`).
    /// 남 · 여 모델 = 리그 + 몸 구역 메시 `R2M_B_구역` + 옷 메시 `R2M_C_이름` (사람형 본 이름 → Humanoid), 머리 = 조합 40개 `R2H_앞_옆_뒤_x`(Head 본에 붙임).
    /// 얼굴은 조각 텍스처 `face_*`를 주민마다 합성, 옷은 `cloth_*` (RGB 무늬 · A = 색 바꾸는 천), 머리는 결 텍스처 × 머리색.
    /// 생성 · 갱신은 에디터 메뉴 SpaceStation/Interior/Wire Resident Models.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Resident Model Set", fileName = "ResidentModelSet")]
    public sealed class ResidentModelSet : ScriptableObject
    {
        [SerializeField] private GameObject _man;
        [SerializeField] private GameObject _woman;
        [SerializeField] private GameObject _hair;
        [SerializeField] private Texture2D _hairTexture;
        [SerializeField] private Texture2D[] _faceParts = Array.Empty<Texture2D>();
        [SerializeField] private Texture2D[] _clothTextures = Array.Empty<Texture2D>();
        [SerializeField] private GarmentDef[] _garments = Array.Empty<GarmentDef>();
        [Tooltip("모델 원래 크기에서 눈 높이 (m, Blender `resident2_face` EYE_Z) — 인물 크기 = 목표 눈 높이 ÷ 이 값")]
        [SerializeField] private float _eyeHeight = 0.975f;

        [Header("머리 조합 (앞 · 옆 · 뒤)")]
        [SerializeField] private string[] _manFront = { "blunt", "swept", "up" };
        [SerializeField] private string[] _manSide = { "short" };
        [SerializeField] private string[] _manBack = { "short", "bob" };
        [SerializeField] private string[] _womanFront = { "blunt", "swept", "part", "up" };
        [SerializeField] private string[] _womanSide = { "short", "lock" };
        [SerializeField] private string[] _womanBack = { "short", "bob", "long", "ponytail", "updo" };

        [Header("얼굴 조각")]
        [SerializeField] private string[] _eyes = { "round", "sleepy", "smile", "sharp" };
        [SerializeField] private string[] _brows = { "straight", "arched", "thick" };
        [SerializeField] private string[] _mouths = { "smile", "flat", "open" };
        [Tooltip("점 (none = 없음)")]
        [SerializeField] private string[] _marks = { "none", "freckles", "blush" };

        public GameObject Man => _man;
        public GameObject Woman => _woman;
        public GameObject Hair => _hair;
        public Texture2D HairTexture => _hairTexture;
        public float EyeHeight => _eyeHeight;
        public IReadOnlyList<GarmentDef> Garments => _garments;
        public bool IsValid => _man != null && _woman != null && _hair != null && _garments.Length > 0;

        public Texture2D FacePart(string name) => Find(_faceParts, "face_" + name);
        public Texture2D ClothTexture(string garment, bool woman) => Find(_clothTextures, $"cloth_{garment}_{(woman ? "w" : "m")}");

        public GarmentDef Garment(string name)
        {
            foreach (var g in _garments)
            {
                if (g.Name == name)
                    return g;
            }
            return null;
        }

        /// <summary>외형 고르기에 쓰는 표 (순수 규칙 <c>ResidentLooks</c>용).</summary>
        public ResidentLookTables Tables()
        {
            var t = new ResidentLookTables
            {
                Eyes = _eyes, Brows = _brows, Mouths = _mouths, Marks = _marks,
                ManFront = _manFront, ManSide = _manSide, ManBack = _manBack,
                WomanFront = _womanFront, WomanSide = _womanSide, WomanBack = _womanBack,
            };
            foreach (var g in _garments)
                t.Garments.Add(new ResidentLookTables.Garment { Name = g.Name, Slot = g.Slot, Man = g.Man, Woman = g.Woman, With = g.With });
            return t;
        }

        private static Texture2D Find(Texture2D[] list, string name)
        {
            foreach (var t in list)
            {
                if (t != null && t.name == name)
                    return t;
            }
            return null;
        }

#if UNITY_EDITOR
        public void EditorSet(GameObject man, GameObject woman, GameObject hair, Texture2D hairTexture, Texture2D[] faceParts, Texture2D[] clothTextures, GarmentDef[] garments)
        {
            _man = man;
            _woman = woman;
            _hair = hair;
            _hairTexture = hairTexture;
            _faceParts = faceParts;
            _clothTextures = clothTextures;
            _garments = garments;
        }
#endif
    }

    /// <summary>외형 고르기 표 (Unity 없이 테스트할 수 있게 순수 데이터).</summary>
    public sealed class ResidentLookTables
    {
        public sealed class Garment
        {
            public string Name;
            public GarmentSlot Slot;
            public bool Man = true, Woman = true;
            public string With = "";
        }

        public string[] Eyes, Brows, Mouths, Marks;
        public string[] ManFront, ManSide, ManBack, WomanFront, WomanSide, WomanBack;
        public readonly List<Garment> Garments = new List<Garment>();
    }
}
