using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 메인 메뉴 전시용 정거장 배치 (5-9). 에디터 메뉴 SpaceStation/Menu/Bake Showcase Layout이
    /// 밸런스 봇이 실제로 지은 정거장을 저장한다 (모든 배치가 규칙상 유효). 코어는 포함하지 않는다.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Showcase Layout", fileName = "ShowcaseLayout")]
    public sealed class ShowcaseLayout : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public ModuleData Module;
            public Vector3Int Origin;
            public int Rotation;
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();
        [Tooltip("만든 방법 기록 (시드·시간)")]
        [SerializeField] private string _source;

        public IReadOnlyList<Entry> Entries => _entries;
        public string Source => _source;

        public void SetEntries(List<Entry> entries, string source)
        {
            _entries = entries;
            _source = source;
        }
    }
}
