using System.Collections.Generic;
using System.IO;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// CONNECT_1121 A2 · A8 인접 태그 틀 1단계 (2026-11-27): 태그 에셋 39개 · 기본 36종 모듈의 태그 · 기존 인접 규칙에 태그 달기.
    /// 기존 규칙은 모듈 칸을 그대로 두고 태그를 더해 "둘 다 맞아야 함"으로 재현한다 (수치 · 화면 · 봇 결과가 바뀌지 않음).
    /// 모듈 칸을 비워 태그 쌍으로 넓히는 것은 4단계(새 관계)에서. 같은 메뉴를 다시 눌러도 결과가 같다.
    /// </summary>
    public static class AdjacencyTagSetup
    {
        private const string TagDir = "Assets/_Project/Data/Adjacency/Tags";
        private const string ModuleDir = "Assets/_Project/Data/Modules";
        private const string RulesPath = "Assets/_Project/Data/AdjacencyRules.asset";

        /// <summary>(키 = 파일 이름 AT_키, 화면 이름, 설명) — CONNECT A8 ① ~ ④.</summary>
        private static readonly (string Key, string Name, string Description)[] Tags =
        {
            // ① 생명 유지 · 전력
            ("WaterSource", "수원", "물을 내놓는 모듈 (물 재활용기 · 물 추출기)"),
            ("Greenery", "녹지", "식물을 기르는 모듈 (수경 농장)"),
            ("WaterUse", "물 사용", "물을 쓰는 모듈 (수경 농장 · 단백질 배양실 · 산소 생성기 · 연료전지 · 핵융합로)"),
            ("Culture", "배양", "배양조를 쓰는 모듈 (단백질 배양실)"),
            ("OxygenSource", "산소원", "산소를 만드는 모듈 (산소 생성기)"),
            ("SolarCollector", "집광", "빛을 모으는 모듈 (태양광 패널) · 이웃이 그늘을 만듦"),
            ("Generator", "발전", "전력을 만드는 모듈 (태양광 패널 · 연료전지 · 핵융합로)"),
            ("HeatSource", "발열원", "열을 내는 모듈 (연료전지 · 핵융합로 · 제련소 · 데이터 센터)"),
            ("RadiationSource", "방사선원", "방사선을 내는 모듈 (핵융합로)"),
            ("PowerStorage", "축전", "전력을 모아 두는 모듈 (배터리)"),
            ("Cooling", "냉각", "열을 빼는 모듈 (방열판)"),
            // ② 거주
            ("Residence", "주거", "주민이 사는 모듈 (거주 모듈 · 회전 링)"),
            ("Leisure", "여가", "쉬는 모듈 (회전 링 · 휴게실)"),
            ("Medical", "의료", "치료하는 모듈 (의료실)"),
            ("Quiet", "정숙", "조용해야 하는 모듈 (의료실 · 학교 · 연구소 · 데이터 센터)"),
            ("Education", "교육", "가르치는 모듈 (학교)"),
            ("Commerce", "상업", "사고파는 모듈 (상업 구역)"),
            ("Bustle", "활기", "북적이는 모듈 (상업 구역)"),
            ("Quarantine", "검역", "검역하는 모듈 (검역소)"),
            ("Arrival", "유입", "사람이 들어오는 모듈 (여객 도킹 포트)"),
            // ③ 산업 · 기반
            ("Center", "중심", "정거장의 중심 (코어)"),
            ("Buffer", "완충", "사이에 끼우면 양쪽 관계가 끊기는 모듈 (구조 프레임)"),
            ("Relay", "중계", "맞닿은 모듈끼리 이웃으로 잇는 모듈 (연결 허브)"),
            ("Stockpile", "보관", "물건을 쌓아 두는 모듈 (창고)"),
            ("Cargo", "하역", "화물을 내리는 모듈 (화물 터미널)"),
            ("Mining", "채굴", "광물을 캐는 모듈 (채굴 도킹)"),
            ("NoiseSource", "소음원", "시끄러운 모듈 (채굴 도킹 · 제련소 · 방어 포탑)"),
            ("Processing", "가공", "광물을 가공하는 모듈 (제련소)"),
            ("Maintenance", "정비", "고치는 모듈 (정비 베이)"),
            ("Research", "연구", "연구하는 모듈 (연구소)"),
            ("Computing", "연산", "계산하는 모듈 (데이터 센터)"),
            ("Administration", "행정", "정거장을 관리하는 모듈 (행정 관제소)"),
            // ④ 방어
            ("Intercept", "요격", "운석을 쏘아 떨어뜨리는 모듈 (방어 포탑)"),
            ("Barrier", "보호막", "운석을 튕겨 내는 모듈 (실드 발생기)"),
            ("Armor", "장갑", "대신 맞아 주는 모듈 (장갑 격벽)"),
            ("Control", "통제", "손상을 다스리는 모듈 (손상 통제실)"),
            ("Sensing", "감지", "멀리 내다보는 모듈 (센서 어레이)"),
            ("Comms", "통신", "신호를 주고받는 모듈 (통신 중계소)"),
            ("Shielding", "차폐", "방사선을 막는 모듈 (방사선 차폐실)"),
        };

        /// <summary>기본 36종 모듈 → 태그 (CONNECT A8 표 그대로).</summary>
        private static readonly (string Module, string[] Tags)[] ModuleTags =
        {
            ("MD_WaterRecycler", new[] { "WaterSource" }),
            ("MD_WaterExtractor", new[] { "WaterSource" }),
            ("MD_Farm", new[] { "Greenery", "WaterUse" }),
            ("MD_ProteinVat", new[] { "Culture", "WaterUse" }),
            ("MD_Oxygen", new[] { "OxygenSource", "WaterUse" }),
            ("MD_Solar", new[] { "SolarCollector", "Generator" }),
            ("MD_FuelCell", new[] { "HeatSource", "Generator", "WaterUse" }),
            ("MD_FusionReactor", new[] { "HeatSource", "RadiationSource", "Generator", "WaterUse" }),
            ("MD_Battery", new[] { "PowerStorage" }),
            ("MD_Radiator", new[] { "Cooling" }),
            ("MD_Habitat", new[] { "Residence" }),
            ("MD_RotatingRing", new[] { "Residence", "Leisure" }),
            ("MD_Medical", new[] { "Medical", "Quiet" }),
            ("MD_School", new[] { "Education", "Quiet" }),
            ("MD_Recreation", new[] { "Leisure" }),
            ("MD_Commerce", new[] { "Commerce", "Bustle" }),
            ("MD_Quarantine", new[] { "Quarantine" }),
            ("MD_PassengerPort", new[] { "Arrival" }),
            ("MD_Core", new[] { "Center" }),
            ("MD_StructureFrame", new[] { "Buffer" }),
            ("MD_ConnectorHub", new[] { "Relay" }),
            ("MD_Storage", new[] { "Stockpile" }),
            ("MD_CargoTerminal", new[] { "Cargo" }),
            ("MD_MiningDock", new[] { "Mining", "NoiseSource" }),
            ("MD_Refinery", new[] { "Processing", "NoiseSource", "HeatSource" }),
            ("MD_MaintenanceBay", new[] { "Maintenance" }),
            ("MD_ResearchLab", new[] { "Research", "Quiet" }),
            ("MD_DataCenter", new[] { "Computing", "Quiet", "HeatSource" }),
            ("MD_Administration", new[] { "Administration" }),
            ("MD_Turret", new[] { "Intercept", "NoiseSource" }),
            ("MD_Shield", new[] { "Barrier" }),
            ("MD_ArmorBulkhead", new[] { "Armor" }),
            ("MD_DamageControl", new[] { "Control" }),
            ("MD_SensorArray", new[] { "Sensing" }),
            ("MD_CommRelay", new[] { "Comms" }),
            ("MD_RadiationShelter", new[] { "Shielding" }),
        };

        /// <summary>
        /// 기존 규칙 (대상 모듈, 이웃 모듈 — 빈 글 = 모든 모듈) → 태그 관계 (CONNECT A8). 그 쪽 모듈이 그 태그를 가질 때만 단다
        /// (서사판 거주 타워처럼 아직 태그 없는 진화 모듈 쪽은 비워 둠 — 모듈로만 맞춤).
        /// </summary>
        private static readonly (string Target, string Neighbor, string TargetTag, string NeighborTag)[] RuleTags =
        {
            ("MD_Farm", "MD_WaterRecycler", "WaterUse", "WaterSource"),          // 수로 공유
            ("MD_FusionReactor", "MD_WaterRecycler", "WaterUse", "WaterSource"), // 냉각수 공유
            ("MD_Oxygen", "MD_WaterRecycler", "WaterUse", "WaterSource"),        // 전기분해 보조
            ("MD_FuelCell", "MD_WaterRecycler", "WaterUse", "WaterSource"),      // 연료 공급
            ("MD_Oxygen", "MD_Farm", "OxygenSource", "Greenery"),                // 광합성 보조
            ("MD_Solar", "", "SolarCollector", ""),                              // 그늘
            ("MD_Habitat", "MD_MiningDock", "Residence", "NoiseSource"),         // 소음
            ("MD_Habitat", "MD_Refinery", "Residence", "NoiseSource"),           // 공장 소음
            ("MD_Habitat", "MD_FusionReactor", "Residence", "RadiationSource"),  // 방사선
            ("MD_Habitat", "MD_Habitat", "Residence", "Residence"),              // 주거 단지
            ("MD_Habitat", "MD_Farm", "Residence", "Greenery"),                  // 녹지
            ("MD_MiningDock", "MD_Refinery", "Mining", "Processing"),            // 제련 가공
            ("MD_Habitat", "MD_Evo_HabitatTower", "Residence", "Residence"),     // 서사판 주거 단지 확장 (거주 타워 쪽은 태그 없음)
            ("MD_Evo_HabitatTower", "MD_Habitat", "Residence", "Residence"),
        };

        [MenuItem("SpaceStation/Adjacency/Setup Tags (CONNECT A8)")]
        public static void Setup()
        {
            var tags = EnsureTags();
            int modules = AssignModuleTags(tags);
            int rules = TagRules(tags);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AdjacencyTagSetup] 태그 {tags.Count}개 · 모듈 {modules}개 · 규칙 {rules}개에 태그");
        }

        private static Dictionary<string, AdjacencyTag> EnsureTags()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data/Adjacency"))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Adjacency");
            if (!AssetDatabase.IsValidFolder(TagDir))
                AssetDatabase.CreateFolder("Assets/_Project/Data/Adjacency", "Tags");
            var map = new Dictionary<string, AdjacencyTag>();
            foreach (var (key, name, description) in Tags)
            {
                string path = $"{TagDir}/AT_{key}.asset";
                var tag = AssetDatabase.LoadAssetAtPath<AdjacencyTag>(path);
                if (tag == null)
                {
                    tag = ScriptableObject.CreateInstance<AdjacencyTag>();
                    tag.EditorSet(name, description);
                    AssetDatabase.CreateAsset(tag, path);
                }
                else if (tag.DisplayName != name || tag.Description != description)
                {
                    tag.EditorSet(name, description);
                    EditorUtility.SetDirty(tag);
                }
                map[key] = tag;
            }
            return map;
        }

        private static int AssignModuleTags(Dictionary<string, AdjacencyTag> tags)
        {
            int count = 0;
            foreach (var (module, keys) in ModuleTags)
            {
                var data = LoadModule(module);
                if (data == null)
                {
                    Debug.LogWarning($"[AdjacencyTagSetup] 모듈 없음: {module}");
                    continue;
                }
                var so = new SerializedObject(data);
                var list = so.FindProperty("_adjacencyTags");
                list.arraySize = keys.Length;
                for (int i = 0; i < keys.Length; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = tags[keys[i]];
                if (so.ApplyModifiedPropertiesWithoutUndo())
                    EditorUtility.SetDirty(data);
                count++;
            }
            return count;
        }

        private static int TagRules(Dictionary<string, AdjacencyTag> tags)
        {
            var set = AssetDatabase.LoadAssetAtPath<AdjacencyRuleSet>(RulesPath);
            if (set == null)
                return 0;
            var so = new SerializedObject(set);
            var list = so.FindProperty("_rules");
            int count = 0;
            for (int i = 0; i < list.arraySize; i++)
            {
                var rule = list.GetArrayElementAtIndex(i);
                var target = rule.FindPropertyRelative("_target").objectReferenceValue as ModuleData;
                var neighbor = rule.FindPropertyRelative("_neighbor").objectReferenceValue as ModuleData;
                foreach (var (t, n, targetTag, neighborTag) in RuleTags)
                {
                    if (target == null || target.name != t || (neighbor != null ? neighbor.name : "") != n)
                        continue;
                    var tt = string.IsNullOrEmpty(targetTag) ? null : tags[targetTag];
                    var nt = string.IsNullOrEmpty(neighborTag) ? null : tags[neighborTag];
                    // 그 쪽 모듈이 그 태그를 가질 때만 (아니면 규칙이 꺼짐)
                    rule.FindPropertyRelative("_targetTag").objectReferenceValue = tt != null && target.HasAdjacencyTag(tt) ? tt : null;
                    rule.FindPropertyRelative("_neighborTag").objectReferenceValue = nt != null && neighbor != null && neighbor.HasAdjacencyTag(nt) ? nt : null;
                    count++;
                    break;
                }
            }
            if (so.ApplyModifiedPropertiesWithoutUndo())
                EditorUtility.SetDirty(set);
            return count;
        }

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
