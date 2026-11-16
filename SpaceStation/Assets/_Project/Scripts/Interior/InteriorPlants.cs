using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-17 ③ 식물 소품: 템플릿의 식물 자리(<see cref="InteriorTemplate.PlantSpots"/>)에 화분 식물 · 나무를 놓는다.
    /// 템플릿에는 화분 · 화단만 있고 잎은 여기서 — 하나씩 시들게 하고(<see cref="Plant.SetWilted"/>) 물을 주면 되살아나게 (주민 요청).
    /// 모델: BlenderWork/InteriorProps/plant_builder.py (잎 하나하나 모양 있는 잎, 시든 것은 처지고 누렇게).
    /// 식물 아틀라스 화분 식물 (맥문동 · 대나무 새싹 · 벚나무 덤불, 2026-11-16 ③)은 모델 하나 — 시듦 = 셰이더 (MPB <c>_Wilt</c>: 누렇게 · 잎이 바깥 · 아래로 처짐).
    /// </summary>
    public sealed class InteriorPlants : MonoBehaviour
    {
        /// <summary>식물 아틀라스 식물 하나: 종류 → 모델 (Art/Plants/Meshes) · 모델 배율 (우리 화분 식물 크기 안 — InteriorPlantVariants가 화분마다 테를 재서 고름).</summary>
        [Serializable]
        public sealed class AtlasPlant
        {
            public PlantKind Kind;
            public GameObject Model;
            public float Scale = 1f;
        }

        [Serializable]
        public sealed class Settings
        {
            public GameObject Pot;
            public GameObject PotWilted;
            public GameObject Tree;
            public GameObject TreeWilted;
            public GameObject Pine;
            public GameObject PineWilted;
            public GameObject Blossom;
            public GameObject BlossomWilted;
            public List<AtlasPlant> Atlas = new List<AtlasPlant>();

            public AtlasPlant AtlasFor(PlantKind kind)
            {
                foreach (var a in Atlas)
                {
                    if (a != null && a.Kind == kind && a.Model != null)
                        return a;
                }
                return null;
            }

            public GameObject Healthy(PlantKind kind) => AtlasFor(kind)?.Model ?? kind switch
            {
                PlantKind.Tree => Tree,
                PlantKind.Pine => Pine != null ? Pine : Tree,
                PlantKind.Blossom => Blossom != null ? Blossom : Tree,
                _ => Pot,
            };

            public GameObject Wilted(PlantKind kind) => AtlasFor(kind) != null ? null : kind switch
            {
                PlantKind.Tree => TreeWilted,
                PlantKind.Pine => Pine != null ? PineWilted : TreeWilted,
                PlantKind.Blossom => Blossom != null ? BlossomWilted : TreeWilted,
                _ => PotWilted,
            };
        }

        /// <summary>놓인 식물 하나.</summary>
        public sealed class Plant
        {
            public ModuleInstance Module;
            public PlantKind Kind;
            public Transform Root;
            public GameObject Healthy, Wilted;
            /// <summary>시듦 = 셰이더 (아틀라스 식물 — 모델 하나에 MPB <c>_Wilt</c>), 아니면 시든 모델로 바꿔 끼움.</summary>
            public bool ShaderWilt;
            public bool IsWilted { get; private set; }

            private static readonly int WiltId = Shader.PropertyToID("_Wilt");
            private static MaterialPropertyBlock _block;

            public void SetWilted(bool on)
            {
                IsWilted = on;
                if (ShaderWilt)
                {
                    if (Healthy == null)
                        return;
                    _block ??= new MaterialPropertyBlock();
                    _block.Clear();
                    _block.SetFloat(WiltId, 1f);
                    foreach (var r in Healthy.GetComponentsInChildren<Renderer>(true))
                        r.SetPropertyBlock(on ? _block : null);   // 멀쩡할 땐 블록을 비워 SRP Batcher로 같이 그림
                    return;
                }
                if (Healthy != null)
                    Healthy.SetActive(!on);
                if (Wilted != null)
                    Wilted.SetActive(on);
            }
        }

        private InteriorBuilder _builder;
        private Settings _s;
        private readonly List<Plant> _plants = new List<Plant>();

        public IReadOnlyList<Plant> Plants => _plants;

        public static InteriorPlants Create(Transform parent, InteriorBuilder builder, Settings settings)
        {
            var go = new GameObject("InteriorPlants");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<InteriorPlants>();
            p._builder = builder;
            p._s = settings ?? new Settings();
            builder.RoomBuilt += p.AddRoom;
            return p;
        }

        private void OnDestroy()
        {
            if (_builder != null)
                _builder.RoomBuilt -= AddRoom;
        }

        private readonly HashSet<ModuleInstance> _rooms = new HashSet<ModuleInstance>();

        /// <summary>내부를 (다시) 만든 직후 — 식물은 템플릿 방 아래라 이미 지워졌음. 12-3c-opt: 방을 지을 때 그 방 식물을 놓음.</summary>
        public void Rebuild(InteriorLayout layout)
        {
            _plants.Clear();
            _rooms.Clear();
            if (layout == null)
                return;
            foreach (var module in _builder.BuiltRooms)
                AddRoom(module);
        }

        private void AddRoom(ModuleInstance module)
        {
            if (!_rooms.Add(module))
                return;
            if (!_builder.TryGetTemplateRoom(module, out var template, out var instance) || template.PlantSpots.Count == 0)
                return;
            foreach (var spot in template.PlantSpots)
            {
                var atlas = _s.AtlasFor(spot.Kind);
                var healthy = _s.Healthy(spot.Kind);
                var wilted = _s.Wilted(spot.Kind);
                if (healthy == null)
                    continue;
                var root = new GameObject("Plant").transform;
                root.SetParent(instance, false);
                root.localPosition = spot.Position;
                root.localRotation = Quaternion.Euler(0f, spot.Yaw, 0f);
                root.localScale = Vector3.one * spot.Scale;
                var plant = new Plant
                {
                    Module = module,
                    Kind = spot.Kind,
                    Root = root,
                    Healthy = Spawn(healthy, root),
                    Wilted = wilted != null ? Spawn(wilted, root) : null,
                    ShaderWilt = atlas != null,
                };
                if (atlas != null)
                    plant.Healthy.transform.localScale = Vector3.one * atlas.Scale;
                plant.SetWilted(false);
                _plants.Add(plant);
            }
        }

        private static GameObject Spawn(GameObject prefab, Transform parent)
        {
            var go = Instantiate(prefab, parent, false);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }
    }
}
