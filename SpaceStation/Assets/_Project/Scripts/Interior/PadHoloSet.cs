using System;
using System.Collections.Generic;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-13 패드 홀로그램 모형용 모듈 메시 묶음 (메뉴 SpaceStation/Interior/Wire Pad가 구움).
    /// 모듈 프리팹의 메시를 프리팹 원점 기준 한 메시로 합친 면(Fill)과, 각진 모서리만 뽑은 선 메시(Lines).
    /// 모듈 FBX는 읽기 불가(Read/Write 꺼짐)라 게임 중에 메시를 읽을 수 없어 에디터에서 미리 굽는다.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Interior/Pad Holo Set", fileName = "PadHoloSet")]
    public sealed class PadHoloSet : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public ModuleData Module;
            public Mesh Fill;
            public Mesh Lines;
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();
        [NonSerialized] private Dictionary<ModuleData, Entry> _lookup;

        public IReadOnlyList<Entry> Entries => _entries;

        public Entry Find(ModuleData module)
        {
            if (module == null)
                return null;
            if (_lookup == null)
            {
                _lookup = new Dictionary<ModuleData, Entry>();
                foreach (var e in _entries)
                {
                    if (e != null && e.Module != null)
                        _lookup[e.Module] = e;
                }
            }
            return _lookup.TryGetValue(module, out var entry) ? entry : null;
        }

#if UNITY_EDITOR
        public void EditorSet(List<Entry> entries)
        {
            _entries = entries;
            _lookup = null;
        }
#endif
    }
}
