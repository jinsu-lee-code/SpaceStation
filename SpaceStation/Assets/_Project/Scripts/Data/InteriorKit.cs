using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>내부 키트 조각 하나: 메시 + 재질 슬롯 (강조색·상태등 슬롯 번호).</summary>
    [Serializable]
    public sealed class InteriorKitPiece
    {
        [SerializeField] private Mesh _mesh;
        [SerializeField] private Material[] _materials;
        [Tooltip("방마다 모듈 강조색(M_Accent_X)으로 바꾸는 슬롯, 없으면 -1")]
        [SerializeField] private int _accentSlot = -1;
        [Tooltip("문 상태등 슬롯 (MaterialPropertyBlock 색), 없으면 -1")]
        [SerializeField] private int _statusSlot = -1;

        public Mesh Mesh => _mesh;
        public IReadOnlyList<Material> Materials => _materials;
        public int AccentSlot => _accentSlot;
        public int StatusSlot => _statusSlot;
        public bool IsValid => _mesh != null && _materials != null && _materials.Length == _mesh.subMeshCount;

        public InteriorKitPiece(Mesh mesh, Material[] materials, int accentSlot, int statusSlot)
        {
            _mesh = mesh;
            _materials = materials;
            _accentSlot = accentSlot;
            _statusSlot = statusSlot;
        }
    }

    /// <summary>
    /// Phase 11-2a 내부 벽 키트 (Blender `BlenderWork/Interior_Kit.blend` → `Art/Models/Interior/SM_InteriorKit.fbx`).
    /// 조각 규격: 벽 패널 = 폭 3.2 × 높이 4 (11-3, 여러 장 이어 붙임), 피벗 = 바깥면 가운데, +Z = 바깥, 방 쪽(-Z)으로 두께 0.2 + 장식, 바닥은 피벗 −1.8 /
    /// 바닥·천장 = 4×4 (방 크기에 맞춰 늘림), 윗면·아랫면 가운데 피벗.
    /// 생성·갱신은 에디터 `InteriorSetup`.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Interior Kit", fileName = "InteriorKit")]
    public sealed class InteriorKit : ScriptableObject
    {
        [SerializeField] private InteriorKitPiece _wall;
        [SerializeField] private InteriorKitPiece _wallDoor;
        [SerializeField] private InteriorKitPiece _doorLeaf;
        [SerializeField] private InteriorKitPiece _floor;
        [SerializeField] private InteriorKitPiece _ceiling;
        [SerializeField] private InteriorKitPiece _hatchFrame;
        [SerializeField] private InteriorKitPiece _hatchLid;
        [Header("11-2b 발코니·나선 계단")]
        [Tooltip("발코니 띠: 길이 방향 Z 1m(늘려 씀), 폭 X 1.4, +X = 트인 쪽, 윗면 y = 0")]
        [SerializeField] private InteriorKitPiece _deck;
        [Tooltip("난간: 길이 방향 X 1m(늘려 씀), 바닥 y = 0")]
        [SerializeField] private InteriorKitPiece _railBar;
        [SerializeField] private InteriorKitPiece _railPost;
        [Tooltip("나선 디딤판: 가운데가 +X, 각도 폭 20도, 윗면 y = 0")]
        [SerializeField] private InteriorKitPiece _stairStep;
        [Tooltip("계단 기둥: 높이 1m(늘려 씀)")]
        [SerializeField] private InteriorKitPiece _stairPole;
        [Header("11-3 연결 튜브")]
        [Tooltip("튜브: 길이 방향 Z 1m(늘려 씀), 피벗 = 바닥 높이 축 아래 점, 안쪽 팔각 아포템 1.5")]
        [SerializeField] private InteriorKitPiece _tube;
        [Tooltip("튜브 끝 고리 (벽에 닿는 쪽)")]
        [SerializeField] private InteriorKitPiece _tubeCollar;
        [Tooltip("11-8 튜브 현창: 위쪽 45도 면 양쪽 둥근 창 한 벌 (늘리지 않음, 피벗은 튜브와 같음), 유리 = 바깥 창 재질")]
        [SerializeField] private InteriorKitPiece _tubeWindow;
        [Header("방별 강조색")]
        [SerializeField] private List<ModuleData> _accentModules = new List<ModuleData>();
        [SerializeField] private List<Material> _accentMaterials = new List<Material>();
        [SerializeField] private Material _defaultAccent;

        public InteriorKitPiece Wall => _wall;
        public InteriorKitPiece WallDoor => _wallDoor;
        public InteriorKitPiece DoorLeaf => _doorLeaf;
        public InteriorKitPiece Floor => _floor;
        public InteriorKitPiece Ceiling => _ceiling;
        public InteriorKitPiece HatchFrame => _hatchFrame;
        public InteriorKitPiece HatchLid => _hatchLid;
        public InteriorKitPiece Deck => _deck;
        public InteriorKitPiece RailBar => _railBar;
        public InteriorKitPiece RailPost => _railPost;
        public InteriorKitPiece StairStep => _stairStep;
        public InteriorKitPiece StairPole => _stairPole;

        public InteriorKitPiece Tube => _tube;
        public InteriorKitPiece TubeCollar => _tubeCollar;
        public bool HasTube => _tube != null && _tube.IsValid && _tubeCollar != null && _tubeCollar.IsValid;
        public InteriorKitPiece TubeWindow => _tubeWindow != null && _tubeWindow.IsValid ? _tubeWindow : null;

        public bool HasBalcony => _deck != null && _deck.IsValid && _railBar != null && _railBar.IsValid && _railPost != null && _railPost.IsValid
                                  && _stairStep != null && _stairStep.IsValid && _stairPole != null && _stairPole.IsValid;

        public bool IsComplete => _wall != null && _wall.IsValid && _wallDoor != null && _wallDoor.IsValid && _doorLeaf != null && _doorLeaf.IsValid
                                  && _floor != null && _floor.IsValid && _ceiling != null && _ceiling.IsValid
                                  && _hatchFrame != null && _hatchFrame.IsValid && _hatchLid != null && _hatchLid.IsValid;

        public Material AccentFor(ModuleData data)
        {
            int i = data != null ? _accentModules.IndexOf(data) : -1;
            return i >= 0 && i < _accentMaterials.Count && _accentMaterials[i] != null ? _accentMaterials[i] : _defaultAccent;
        }

#if UNITY_EDITOR
        public void EditorSet(InteriorKitPiece wall, InteriorKitPiece wallDoor, InteriorKitPiece doorLeaf, InteriorKitPiece floor,
            InteriorKitPiece ceiling, InteriorKitPiece hatchFrame, InteriorKitPiece hatchLid,
            List<ModuleData> accentModules, List<Material> accentMaterials, Material defaultAccent)
        {
            _wall = wall;
            _wallDoor = wallDoor;
            _doorLeaf = doorLeaf;
            _floor = floor;
            _ceiling = ceiling;
            _hatchFrame = hatchFrame;
            _hatchLid = hatchLid;
            _accentModules = accentModules;
            _accentMaterials = accentMaterials;
            _defaultAccent = defaultAccent;
        }

        public void EditorSetBalcony(InteriorKitPiece deck, InteriorKitPiece railBar, InteriorKitPiece railPost, InteriorKitPiece stairStep, InteriorKitPiece stairPole)
        {
            _deck = deck;
            _railBar = railBar;
            _railPost = railPost;
            _stairStep = stairStep;
            _stairPole = stairPole;
        }

        public void EditorSetTube(InteriorKitPiece tube, InteriorKitPiece tubeCollar, InteriorKitPiece tubeWindow)
        {
            _tube = tube;
            _tubeCollar = tubeCollar;
            _tubeWindow = tubeWindow;
        }
#endif
    }
}
