using System.Collections.Generic;
using System.IO;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// CONNECT_1121 3번 ④ V1 (2026-11-28): 기본 36종의 새 관계 (A8 ① ~ ④ · A12 · A13 결정) — 엔드리스(타이쿤) 규칙 묶음을 이 표로 통째로 다시 씀.
    /// 기존 12개는 모듈 칸을 비워 태그 쌍으로 넓힘 (예: 물 사용 ← 수원 = 물 추출기도 수원, 주거 ← 소음원 = 포탑도 소음원).
    /// 캠페인(서사판) 규칙 묶음은 건드리지 않는다 (A4 — 통합 밸런싱 때): 거주 타워 규칙이 있으면 멈춤.
    /// 수치는 가안 → 봇 측정 (사용자 결정 2026-11-28: 관계를 보는 봇과 안 보는 봇의 성적 차이가 뚜렷하게). 같은 메뉴를 다시 눌러도 결과가 같다.
    /// V2 (2026-11-28): 안쪽 얼굴 (사고 변형 · 놓침 손실 · 번짐) + 안쪽만 관계 넷 (아무 방 ← 의료 · 중심 · 통제 · 연구). 현장 수리 · 부탁 쪽은 V3.
    /// </summary>
    public static class AdjacencyRelationSetup
    {
        private const string TagDir = "Assets/_Project/Data/Adjacency/Tags";
        private const string ModuleDir = "Assets/_Project/Data/Modules";
        private const string RulesPath = "Assets/_Project/Data/AdjacencyRules.asset";

        /// <summary>관계 한 줄. Target · Neighbor = 모듈 파일 이름 (빈 글 = 태그만), TargetTag · NeighborTag = 태그 키 (AT_키).</summary>
        public struct Relation
        {
            public string Label;
            public AdjacencyEffect Effect;
            public float Value;
            public int Max;
            public string Target, TargetTag, Neighbor, NeighborTag;
            public bool AnyTarget, ExcludeSame;
            public int Free;
            public InnerFace Inner;
        }

        private static Relation R(string label, AdjacencyEffect effect, float value, int max, string targetTag, string neighborTag, string target = "", InnerFace inner = InnerFace.None)
            => new Relation { Label = label, Effect = effect, Value = value, Max = max, Target = target, TargetTag = targetTag, Neighbor = "", NeighborTag = neighborTag, Inner = inner };

        /// <summary>안쪽만 관계 (아무 방 ← 이웃 태그).</summary>
        private static Relation Inside(string label, string neighborTag, InnerFace inner)
            => new Relation { Label = label, Effect = AdjacencyEffect.InnerOnly, Value = 0f, Max = 1, AnyTarget = true, Target = "", TargetTag = "", Neighbor = "", NeighborTag = neighborTag, Inner = inner };

        /// <summary>CONNECT A8 바깥 얼굴 (엔드리스). 순서 = 화면 설명 순서.</summary>
        public static readonly Relation[] Relations =
        {
            // ① 생명 유지 · 전력 — 물 사용 ← 수원 (기존 4 + 단백질 배양실, 물 추출기도 수원)
            R("수로 공유", AdjacencyEffect.Consumption, -0.25f, 3, "WaterUse", "WaterSource", "MD_Farm", InnerFace.LeakNeighborValve),
            R("냉각수 공유", AdjacencyEffect.Consumption, -0.25f, 2, "WaterUse", "WaterSource", "MD_FusionReactor", InnerFace.LeakNeighborValve),
            R("전기분해 보조", AdjacencyEffect.Consumption, -0.2f, 2, "WaterUse", "WaterSource", "MD_Oxygen", InnerFace.LeakNeighborValve),
            R("연료 공급", AdjacencyEffect.Consumption, -0.25f, 1, "WaterUse", "WaterSource", "MD_FuelCell", InnerFace.LeakNeighborValve),
            R("배양수 공유", AdjacencyEffect.Consumption, -0.25f, 2, "WaterUse", "WaterSource", "MD_ProteinVat", InnerFace.LeakNeighborValve),
            R("광합성 보조", AdjacencyEffect.Production, 0.15f, 3, "OxygenSource", "Greenery", "MD_Oxygen", InnerFace.FilterEasy), // 생산 관계는 대상 모듈도 (설명에 "산소 생산" — 태그만이면 "생산"), ⑥ 진화 태그 때 넓힘
            new Relation { Label = "그늘", Effect = AdjacencyEffect.Production, Value = -0.1f, Max = 3, TargetTag = "SolarCollector", Target = "", Neighbor = "", NeighborTag = "", ExcludeSame = true, Free = 1 },
            R("직결 충전", AdjacencyEffect.ChargeRate, 0.25f, 2, "PowerStorage", "Generator"), // 안쪽 얼굴(스위치 하나만)은 "아무 방 ← 축전"으로 — 배터리 방엔 차단기가 안 생김 (만들지도 쓰지도 않음)
            R("사료 순환", AdjacencyEffect.Production, 0.1f, 2, "Culture", "Greenery", "MD_ProteinVat", inner: InnerFace.FetchNearby),
            R("인화 위험", AdjacencyEffect.SpreadTime, -0.3f, 2, "OxygenSource", "HeatSource", inner: InnerFace.FireTwoFlames),
            // ② 거주
            R("주거 단지", AdjacencyEffect.Housing, 1f, 3, "Residence", "Residence", inner: InnerFace.FetchNearby | InnerFace.TalkBonus),
            R("녹지", AdjacencyEffect.Housing, 1f, 2, "Residence", "Greenery", inner: InnerFace.FilterEasy | InnerFace.RequestBonus),
            R("소음", AdjacencyEffect.Housing, -1f, 3, "Residence", "NoiseSource", inner: InnerFace.FilterTwo | InnerFace.RequestPenalty | InnerFace.NoiseComplaint), // 가안 −2 → −1 (봇 측정 2026-11-28: −2면 막 짓기와 차이 29% — 사용자 기준 10 ~ 20%, −1 = 13.7%)
            R("방사선", AdjacencyEffect.Housing, -2f, 2, "Residence", "RadiationSource", inner: InnerFace.FilterFrequent | InnerFace.FilterRadiation),
            R("냄새", AdjacencyEffect.Housing, -1f, 2, "Residence", "Culture", inner: InnerFace.FilterFrequent),
            R("소란", AdjacencyEffect.ServiceCapacity, -0.2f, 2, "Quiet", "NoiseSource", inner: InnerFace.RequestPenalty | InnerFace.NoiseComplaint),
            R("북적임", AdjacencyEffect.ServiceCapacity, -0.2f, 2, "Quiet", "Bustle", inner: InnerFace.RequestPenalty | InnerFace.NoiseComplaint),
            R("연계 진료", AdjacencyEffect.ServiceCapacity, 0.25f, 1, "Medical", "Quarantine", inner: InnerFace.FetchNearby),
            R("입항 검역", AdjacencyEffect.PlagueChance, -0.5f, 1, "Quarantine", "Arrival"),
            R("유동 인구", AdjacencyEffect.ServiceCapacity, 0.25f, 1, "Commerce", "Arrival", inner: InnerFace.FetchHere),
            R("공원 산책", AdjacencyEffect.ServiceCapacity, 0.15f, 2, "Leisure", "Greenery", inner: InnerFace.RequestLight | InnerFace.RequestBonus),
            R("상권 나들이", AdjacencyEffect.ServiceCapacity, 0.15f, 1, "Leisure", "Commerce", inner: InnerFace.RequestLight | InnerFace.RequestBonus),
            R("마중", AdjacencyEffect.Passengers, 1f, 1, "Arrival", "Residence"),
            R("현장 학습", AdjacencyEffect.ServiceCapacity, 0.2f, 1, "Education", "Research"),
            // ③ 산업 · 기반
            R("제련 가공", AdjacencyEffect.Production, 0.5f, 1, "Mining", "Processing", "MD_MiningDock"),
            new Relation { Label = "정비 지원", Effect = AdjacencyEffect.RepairTime, Value = -0.2f, Max = 1, AnyTarget = true, Target = "", TargetTag = "", Neighbor = "", NeighborTag = "Maintenance", Inner = InnerFace.FireDoorExtinguisher | InnerFace.RepairQuick },
            R("바로 하역", AdjacencyEffect.CargoAmount, 0.2f, 2, "Cargo", "Stockpile"),
            R("연산 지원", AdjacencyEffect.ResearchSpeed, 0.05f, 2, "Research", "Computing"),
            R("소음 방해", AdjacencyEffect.ResearchSpeed, -0.05f, 2, "Quiet", "NoiseSource", inner: InnerFace.RequestPenalty | InnerFace.NoiseComplaint),
            R("습기", AdjacencyEffect.ResearchSpeed, -0.03f, 2, "Quiet", "Greenery"),
            R("행정 직결", AdjacencyEffect.PolicyDelay, -0.3f, 1, "Administration", "Center"),
            // ④ 방어
            R("표적 유도", AdjacencyEffect.InterceptChance, 0.25f, 2, "Intercept", "Sensing"),
            R("엄폐", AdjacencyEffect.HitWeight, -0.3f, 2, "Intercept", "Armor", inner: InnerFace.RepairFewer),
            R("실드 연동", AdjacencyEffect.RicochetChance, -0.3f, 2, "Barrier", "Barrier", inner: InnerFace.BreakerBacked),
            R("통제 지원", AdjacencyEffect.SpreadTime, 0.25f, 1, "Control", "Maintenance", inner: InnerFace.MissHalf),
            R("신호 보정", AdjacencyEffect.DecodeSpeed, 0.25f, 2, "Comms", "Sensing"),
            R("전파 차폐", AdjacencyEffect.RelayPower, -0.5f, 1, "Comms", "Barrier"),
            // 안쪽만 (A8 — 바깥 수치 없음, V2 2026-11-28)
            Inside("진료 지원", "Medical", InnerFace.MissNoMood),
            Inside("코어 처리", "Center", InnerFace.MissNoLoss),
            Inside("번짐 차단", "Control", InnerFace.NoSpread),
            Inside("연구 힌트", "Research", InnerFace.BreakerForgiving),
            Inside("전력 완충", "PowerStorage", InnerFace.BreakerOneSwitch), // A5 "옆이 배터리 계열 → 내려간 스위치 1개"
            // V3 (2026-11-28): 현장 수리
            Inside("보관 공구", "Stockpile", InnerFace.RepairFewer),
            Inside("장갑 보강", "Armor", InnerFace.RepairFewer),
        };

        [MenuItem("SpaceStation/Adjacency/Setup Relations (CONNECT A8 4)")]
        public static void Setup()
        {
            var set = AssetDatabase.LoadAssetAtPath<AdjacencyRuleSet>(RulesPath);
            if (set == null)
            {
                Debug.LogError($"[AdjacencyRelationSetup] 규칙 묶음 없음: {RulesPath}");
                return;
            }
            foreach (var rule in set.Rules)
            {
                if ((rule.Target != null && rule.Target.name.StartsWith("MD_Evo_")) || (rule.Neighbor != null && rule.Neighbor.name.StartsWith("MD_Evo_")))
                {
                    Debug.LogError("[AdjacencyRelationSetup] 캠페인(서사판) 규칙 묶음 — 새 관계는 엔드리스 먼저 (CONNECT A4, 통합 밸런싱 때). 멈춤");
                    return;
                }
            }
            var rules = Build(LoadTag, LoadModule, out var missing);
            if (missing.Count > 0)
            {
                Debug.LogError("[AdjacencyRelationSetup] 없는 태그 · 모듈: " + string.Join(", ", missing));
                return;
            }
            set.EditorSetRules(rules);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AdjacencyRelationSetup] 관계 {rules.Count}개");
        }

        /// <summary>표 → 규칙 (시험도 같은 길로). missing = 못 찾은 태그 · 모듈 이름.</summary>
        public static List<AdjacencyRule> Build(System.Func<string, AdjacencyTag> tag, System.Func<string, ModuleData> module, out List<string> missing)
        {
            var list = new List<AdjacencyRule>();
            missing = new List<string>();
            foreach (var r in Relations)
            {
                var target = Find(r.Target, module, missing);
                var neighbor = Find(r.Neighbor, module, missing);
                var targetTag = Find(r.TargetTag, tag, missing);
                var neighborTag = Find(r.NeighborTag, tag, missing);
                list.Add(AdjacencyRule.Create(r.Effect, r.Value, r.Max, r.Label, target, targetTag, neighbor, neighborTag, r.AnyTarget, r.ExcludeSame, r.Free, r.Inner));
            }
            return list;
        }

        private static T Find<T>(string key, System.Func<string, T> load, List<string> missing) where T : Object
        {
            if (string.IsNullOrEmpty(key))
                return null;
            var x = load(key);
            if (x == null)
                missing.Add(key);
            return x;
        }

        private static AdjacencyTag LoadTag(string key) => AssetDatabase.LoadAssetAtPath<AdjacencyTag>($"{TagDir}/AT_{key}.asset");

        private static ModuleData LoadModule(string name)
        {
            var data = AssetDatabase.LoadAssetAtPath<ModuleData>($"{ModuleDir}/{name}.asset");
            if (data != null)
                return data;
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:ModuleData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == name)
                    return AssetDatabase.LoadAssetAtPath<ModuleData>(path);
            }
            return null;
        }
    }
}
