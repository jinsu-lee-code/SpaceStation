using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>13-5a 탐사 지점 종류 (REDESIGN V5: 자원 · 기록 조각 · 위험).</summary>
    public enum SiteKind
    {
        /// <summary>소행성 — 닿아 있는 동안 금속.</summary>
        Asteroid,
        /// <summary>얼음 혼석 — 닿아 있는 동안 물.</summary>
        IceRock,
        /// <summary>난파선 — 닿으면 기록 조각 (기록 복원 연구 · 인간 미스터리 로그).</summary>
        Wreck,
        /// <summary>파편 지대 — 닿으면 큰 보상, 그 뒤 그 방향에서 파편이 날아옴.</summary>
        DebrisField,
    }

    /// <summary>
    /// 13-5a 서사판 탐사 (사용자 결정 2026-10-18 D-6): 센서 어레이가 가동 중이면 장마다 신호(장 데이터)가 잡히고,
    /// [탐사] 창에서 한 장에 하나를 탐사선으로 밝힘 → 그 지점 근처(반경)까지 모듈을 뻗어 닿으면 효과. 값은 그레이박스 초안.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Exploration Config", fileName = "ExplorationConfig")]
    public sealed class ExplorationConfig : ScriptableObject
    {
        [Tooltip("신호를 잡는 모듈 (하나라도 가동 중이어야 장의 신호가 생김)")]
        [SerializeField] private ModuleData _sensor;
        [SerializeField, Min(0f)] private float _probeMetal = 60f;
        [Tooltip("탐사선이 지점에 닿아 정체를 밝히기까지 (초)")]
        [SerializeField, Min(1f)] private float _probeSeconds = 45f;
        [Tooltip("코어에 연결된 모듈이 지점에서 이 칸 수 안(가로 · 세로 · 높이 각각)에 들어가면 닿음")]
        [SerializeField, Min(0)] private int _reachRadius = 2;

        [Header("자원")]
        [SerializeField, Min(0f)] private float _asteroidMetalPerSecond = 0.4f;
        [SerializeField, Min(0f)] private float _iceWaterPerSecond = 0.4f;

        [Header("기록 (난파선)")]
        [SerializeField, Min(0)] private int _wreckFragments = 1;
        [SerializeField, Min(0)] private int _wreckResearchPoints = 2;
        [Tooltip("난파선에서 나오는 기록 조각 (차례대로, 인간 미스터리 T8)")]
        [SerializeField, TextArea(2, 4)] private List<string> _wreckLogs = new List<string>();

        [Header("위험 (파편 지대)")]
        [SerializeField, Min(0f)] private float _debrisMetal = 150f;
        [Tooltip("닿은 뒤 이 간격(초)마다 그 방향 앞면에 파편")]
        [SerializeField, Min(5f)] private float _debrisInterval = 120f;
        [SerializeField, Min(1)] private int _debrisHits = 1;

        public ModuleData Sensor => _sensor;
        public float ProbeMetal => _probeMetal;
        public float ProbeSeconds => _probeSeconds;
        public int ReachRadius => _reachRadius;
        public float AsteroidMetalPerSecond => _asteroidMetalPerSecond;
        public float IceWaterPerSecond => _iceWaterPerSecond;
        public int WreckFragments => _wreckFragments;
        public int WreckResearchPoints => _wreckResearchPoints;
        public IReadOnlyList<string> WreckLogs => _wreckLogs;
        public float DebrisMetal => _debrisMetal;
        public float DebrisInterval => _debrisInterval;
        public int DebrisHits => _debrisHits;

        /// <summary>밝히기 전 흐릿한 힌트 (사용자 결정: 종류를 짐작만).</summary>
        public static string Hint(SiteKind kind)
        {
            switch (kind)
            {
                case SiteKind.Asteroid: return "강한 금속 반응";
                case SiteKind.IceRock: return "차가운 반사 · 수소 흔적";
                case SiteKind.Wreck: return "규칙적인 인공 신호";
                default: return "불규칙한 반사 · 많은 조각";
            }
        }

        public static string Name(SiteKind kind)
        {
            switch (kind)
            {
                case SiteKind.Asteroid: return "소행성";
                case SiteKind.IceRock: return "얼음 혼석";
                case SiteKind.Wreck: return "난파선";
                default: return "파편 지대";
            }
        }

        /// <summary>밝힌 뒤 닿으면 무엇을 주는지 한 줄.</summary>
        public string Effect(SiteKind kind)
        {
            switch (kind)
            {
                case SiteKind.Asteroid: return $"탐사하는 동안 금속 초당 +{_asteroidMetalPerSecond:0.#}";
                case SiteKind.IceRock: return $"탐사하는 동안 물 초당 +{_iceWaterPerSecond:0.#}";
                case SiteKind.Wreck: return $"기록 조각 +{_wreckFragments} · 연구 포인트 +{_wreckResearchPoints}";
                default: return $"금속 +{_debrisMetal:0} · 그 뒤 {_debrisInterval / 60f:0.#}분마다 그쪽에서 파편";
            }
        }

#if UNITY_EDITOR
        public void EditorSet(ModuleData sensor, List<string> wreckLogs)
        {
            _sensor = sensor;
            _wreckLogs = wreckLogs;
        }
#endif
    }
}
