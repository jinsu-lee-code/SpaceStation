using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>템플릿의 문 자리 하나 (모듈 로컬 = 회전 전).</summary>
    [Serializable]
    public sealed class InteriorSocket
    {
        [Tooltip("모듈 로컬 칸 (ModuleData.CellOffsets 중 하나)")]
        [SerializeField] private Vector3Int _cell;
        [Tooltip("모듈 로컬 방향 (6방향 단위)")]
        [SerializeField] private Vector3Int _direction;
        [Tooltip("칸 중심에서 문 벽 바깥면(수평) / 해치 면(수직)까지 m")]
        [SerializeField] private float _depth = 3.2f;
        [Tooltip("수직 문 자리(해치)만: 칸 중심에서 수평으로 옮기는 거리 (모듈 로컬). 통로 쪽에 두려고")]
        [SerializeField] private Vector3 _offset;

        public Vector3Int Cell => _cell;
        public Vector3Int Direction => _direction;
        public float Depth => _depth;
        public Vector3 Offset => _offset;

        public InteriorSocket(Vector3Int cell, Vector3Int direction, float depth, Vector3 offset = default)
        {
            _cell = cell;
            _direction = direction;
            _depth = depth;
            _offset = offset;
        }
    }

    /// <summary>11-9 방 환경음 묶음 (성격별 6). SoundLibrary의 Room* 루프와 짝.</summary>
    public enum InteriorAmbience
    {
        Life,       // 거주·의료·휴게
        Water,      // 농장·물 재활용·산소
        Machine,    // 창고·정비·화물·채굴·연구
        Heat,       // 제련·핵융합·연료전지
        Electric,   // 배터리·태양광·실드·포탑·장갑 격벽·손상 통제
        Hall,       // 코어·회전 링 (+ 템플릿 없는 방)
    }

    /// <summary>
    /// Phase 11-3 모듈별 내부 템플릿. 프리팹은 모듈 로컬 공간(원점 = 원점 칸 중심, 회전 전, 1칸 = 8m)에 바닥·벽·천장·소품·충돌을 가진다.
    /// 수평 문 자리는 폭 3.2 × 높이 4(바닥 −0.2부터)의 구멍으로 비워 두고, 빌더가 통로가 있으면 문 벽(키트)·없으면 막힌 벽을 채운다.
    /// 수직 문 자리는 통로가 있을 때만 해치를 얹는다 (프리팹 바닥·천장은 막혀 있음).
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Interior Template", fileName = "IT_Module")]
    public sealed class InteriorTemplate : ScriptableObject
    {
        [SerializeField] private ModuleData _module;
        [SerializeField] private GameObject _prefab;
        [SerializeField] private List<InteriorSocket> _sockets = new List<InteriorSocket>();
        [Tooltip("들어갔을 때 서는 곳 (모듈 로컬, 발 위치)")]
        [SerializeField] private Vector3 _spawn;
        [SerializeField] private float _spawnYaw;
        [Tooltip("11-9 방 환경음 묶음")]
        [SerializeField] private InteriorAmbience _ambience = InteriorAmbience.Hall;

        public ModuleData Module => _module;
        public InteriorAmbience Ambience => _ambience;
        public GameObject Prefab => _prefab;
        public IReadOnlyList<InteriorSocket> Sockets => _sockets;
        public Vector3 Spawn => _spawn;
        public float SpawnYaw => _spawnYaw;

        /// <summary>월드 칸·방향의 문 자리 (모듈 원점·회전을 되돌려 찾음).</summary>
        public bool TryGetSocket(Vector3Int origin, int rotation, Vector3Int worldCell, Vector3Int worldDir, out InteriorSocket socket)
        {
            var localCell = Core.GridDirections.Rotate(worldCell - origin, -rotation);
            var localDir = Core.GridDirections.Rotate(worldDir, -rotation);
            foreach (var s in _sockets)
            {
                if (s.Cell == localCell && s.Direction == localDir)
                {
                    socket = s;
                    return true;
                }
            }
            socket = null;
            return false;
        }

#if UNITY_EDITOR
        public void EditorSet(ModuleData module, GameObject prefab, List<InteriorSocket> sockets, Vector3 spawn, float spawnYaw)
        {
            _module = module;
            _prefab = prefab;
            _sockets = sockets;
            _spawn = spawn;
            _spawnYaw = spawnYaw;
        }

        public void EditorSetAmbience(InteriorAmbience ambience) => _ambience = ambience;
#endif
    }
}
