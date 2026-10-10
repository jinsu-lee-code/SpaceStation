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
    /// </summary>
    public sealed class InteriorPlants : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            public GameObject Pot;
            public GameObject PotWilted;
            public GameObject Tree;
            public GameObject TreeWilted;
        }

        /// <summary>놓인 식물 하나.</summary>
        public sealed class Plant
        {
            public ModuleInstance Module;
            public PlantKind Kind;
            public Transform Root;
            public GameObject Healthy, Wilted;
            public bool IsWilted { get; private set; }

            public void SetWilted(bool on)
            {
                IsWilted = on;
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
            return p;
        }

        /// <summary>내부를 (다시) 만든 직후 — 식물은 템플릿 방 아래라 이미 지워졌음.</summary>
        public void Rebuild(InteriorLayout layout)
        {
            _plants.Clear();
            if (layout == null)
                return;
            foreach (var room in layout.Rooms)
            {
                if (!_builder.TryGetTemplateRoom(room.Module, out var template, out var instance) || template.PlantSpots.Count == 0)
                    continue;
                foreach (var spot in template.PlantSpots)
                {
                    var healthy = spot.Kind == PlantKind.Tree ? _s.Tree : _s.Pot;
                    var wilted = spot.Kind == PlantKind.Tree ? _s.TreeWilted : _s.PotWilted;
                    if (healthy == null)
                        continue;
                    var root = new GameObject("Plant").transform;
                    root.SetParent(instance, false);
                    root.localPosition = spot.Position;
                    root.localRotation = Quaternion.Euler(0f, spot.Yaw, 0f);
                    root.localScale = Vector3.one * spot.Scale;
                    var plant = new Plant
                    {
                        Module = room.Module,
                        Kind = spot.Kind,
                        Root = root,
                        Healthy = Spawn(healthy, root),
                        Wilted = wilted != null ? Spawn(wilted, root) : null,
                    };
                    plant.SetWilted(false);
                    _plants.Add(plant);
                }
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
