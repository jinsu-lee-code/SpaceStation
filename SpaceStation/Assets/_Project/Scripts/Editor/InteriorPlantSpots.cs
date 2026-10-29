using System.Collections.Generic;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 11-17 ③ 템플릿 식물 자리 (메뉴 SpaceStation/Interior/Set Plant Spots, Build Templates 끝에도 실행).
    /// 좌표는 Blender 빌더(Interior_Kit.blend 텍스트 블록)의 화분 · 화단 흙 윗면과 같다 — 빌더를 고치면 여기도 함께.
    /// </summary>
    public static class InteriorPlantSpots
    {
        private const string DataFolder = "Assets/_Project/Data/Interior/";
        private const float F = -1.3f;

        [MenuItem("SpaceStation/Interior/Set Plant Spots")]
        public static void SetAll()
        {
            // ring_builder: 안쪽벽 화분 8 (반지름 R_IN + 0.45 = 5.75, 창과 같은 각도 22.5 + 45k), 흙 F + 0.46.
            // 안쪽 벽이 0.45m 앞이라 0.7배 (잎 폭 ±0.66 → ±0.46)
            var ring = new List<PlantSpot>();
            for (int k = 0; k < 8; k++)
            {
                float a = (22.5f + 45f * k) * Mathf.Deg2Rad;
                ring.Add(new PlantSpot(new Vector3(5.75f * Mathf.Cos(a), F + 0.46f, 5.75f * Mathf.Sin(a)), PlantKind.Pot, 0.7f, k * 47f));
            }
            Set("IT_RotatingRing", ring);
            // recreation_builder: 창 양옆 화분 2 (x 1.75 · 6.25, z −2.0), 흙 F + 0.5
            Set("IT_Recreation", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(1.75f, F + 0.5f, -2f), PlantKind.Pot, 0.9f, 20f),
                new PlantSpot(new Vector3(6.25f, F + 0.5f, -2f), PlantKind.Pot, 0.9f, 140f),
            });
            // farm_builder: 돔 아래 원형 화단 2 (x 0 · 8, z 0), 흙 F + 0.5
            Set("IT_Farm", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(0f, F + 0.5f, 0f), PlantKind.Tree, 1f, 0f),
                new PlantSpot(new Vector3(8f, F + 0.5f, 0f), PlantKind.Tree, 1f, 110f),
            });
            // 12-5 (Interior_NewBasic.blend) hub_builder: 키오스크 양옆 화분 2 (PLANTS), 흙 F + 0.415, 깎인 벽 가까이라 0.7배
            Set("IT_Hub", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(-1.934f, F + 0.415f, 2.726f), PlantKind.Pot, 0.7f, 30f),
                new PlantSpot(new Vector3(-2.726f, F + 0.415f, 1.934f), PlantKind.Pot, 0.7f, 200f),
            });
            // 12-5 cm_builder: 아트리움 아래 원형 화단 (x 4, z 0), 흙 F + 0.4
            Set("IT_Commerce", new List<PlantSpot> { new PlantSpot(new Vector3(4f, F + 0.4f, 0f), PlantKind.Tree, 1f, 40f) });
            // 12-5 passenger_builder ANCHORS["plants"]: 입국장 앞 구석 · 대합실 칸막이 옆 화분 2, 흙 F + 0.47, 벽 · 유리에서 0.45라 0.7배
            Set("IT_Passenger", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(-2.6f, F + 0.47f, 2.75f), PlantKind.Pot, 0.7f, 75f),
                new PlantSpot(new Vector3(4.6f, F + 0.47f, 2.75f), PlantKind.Pot, 0.7f, 250f),
            });
            // 진화 1단계 거주 타워 (Interior_Evo.blend ht_builder PLANTS): 흰 화분 14 — 1층 창가 4 · 엘리베이터 앞 2 / 2층 모서리 집 앞 4 / 3층 창가 4,
            //   흙 윗면 = 층 바닥 + 0.462. 창 · 벽 가까운 화분은 0.75배
            var tower = new List<PlantSpot>();
            int n = 0;
            foreach (var (x, y, z, s) in new[]
            {
                (-2.5f, F + 0.462f, 2.5f, 0.75f), (-2.5f, F + 0.462f, 5.5f, 0.75f), (10.5f, F + 0.462f, 2.5f, 0.75f), (10.5f, F + 0.462f, 5.5f, 0.75f),
                (2.4f, F + 0.462f, 2.2f, 0.9f), (5.6f, F + 0.462f, 2.2f, 0.9f),
                (-0.902f, F + 4.862f, 0.441f, 0.85f), (7.559f, F + 4.862f, -0.902f, 0.85f), (0.441f, F + 4.862f, 8.902f, 0.85f), (8.902f, F + 4.862f, 7.559f, 0.85f),
                (-2.5f, 8f + F + 0.462f, 4f, 0.75f), (10.5f, 8f + F + 0.462f, 4f, 0.75f), (4f, 8f + F + 0.462f, -2.5f, 0.75f), (4f, 8f + F + 0.462f, 10.5f, 0.75f),
            })
                tower.Add(new PlantSpot(new Vector3(x, y, z), PlantKind.Pot, s, (n++) * 53f));
            Set("IT_EvoTower", tower);
            // 진화 1단계 생태 모듈 (eco_builder PLANTS — 사용자 2026-10-29 "온실 식물은 수경 농장 등에서 쓰던 식물로"):
            //   재배대 벽 쪽 줄 화분 식물 18 (흙 F + 0.665, z ±1.95 — 통로 쪽 줄은 템플릿에 구운 수경 농장 상추) + 이음 칸 나무 화분 2
            //   (흙 F + 0.502, 0.8배 — 나무 모델 흙 위 덤불이 화분 안 테 0.64 안에 들게, 플레이 확인 2026-10-29)
            var eco = new List<PlantSpot>();
            n = 0;
            foreach (float z in new[] { -1.95f, 1.95f })
            {
                foreach (float x in new[] { 4.95f, 6.25f, 9.583f, 10.55f, 11.517f, 12.483f, 13.45f, 14.417f, 17.75f })
                    eco.Add(new PlantSpot(new Vector3(x, F + 0.665f, z), PlantKind.Pot, 0.7f, (n++) * 67f));
            }
            eco.Add(new PlantSpot(new Vector3(-1.95f, F + 0.502f, -1.95f), PlantKind.Tree, 0.8f, 30f));
            eco.Add(new PlantSpot(new Vector3(1.95f, F + 0.502f, -1.95f), PlantKind.Tree, 0.8f, 200f));
            Set("IT_EvoEco", eco);
            // 진화 1단계 복지 센터 (wc_builder PLANTS): 정자 가운데 둥근 화단 나무 (흙 F + 0.462, 안 테 0.82 > 덤불 0.75) + 여가 날개 이음 쪽 화분 둘 (0.7배)
            Set("IT_EvoWelfare", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(0f, F + 0.462f, 0f), PlantKind.Tree, 1f, 20f),
                new PlantSpot(new Vector3(-2.55f, F + 0.462f, 3.85f), PlantKind.Pot, 0.7f, 70f),
                new PlantSpot(new Vector3(2.55f, F + 0.462f, 3.85f), PlantKind.Pot, 0.7f, 220f),
            });
            // 진화 1단계 문화 센터 (cc_builder PLANTS): −x 쉼터 벤치 양옆 화분 둘 (흙 F + 0.462, 창 가까이 0.7배)
            Set("IT_EvoCulture", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(-2.7f, F + 0.462f, 2.15f), PlantKind.Pot, 0.7f, 40f),
                new PlantSpot(new Vector3(-2.7f, F + 0.462f, 5.85f), PlantKind.Pot, 0.7f, 190f),
            });
            // 진화 1단계 태양광 어레이 (sa_builder POTS): 천창 아래 대각선 화분 넷 (반지름 2.65, 45° + 90k) + −x 창가 화분 둘, 흙 F + 0.462, 0.7배
            var solar = new List<PlantSpot>();
            n = 0;
            foreach (float deg in new[] { 45f, 135f, 225f, 315f })
            {
                float a = deg * Mathf.Deg2Rad;
                solar.Add(new PlantSpot(new Vector3(4f + 2.65f * Mathf.Cos(a), F + 0.462f, 4f + 2.65f * Mathf.Sin(a)), PlantKind.Pot, 0.7f, (n++) * 71f));
            }
            solar.Add(new PlantSpot(new Vector3(-2.7f, F + 0.462f, 2.15f), PlantKind.Pot, 0.7f, 30f));
            solar.Add(new PlantSpot(new Vector3(-2.7f, F + 0.462f, 5.85f), PlantKind.Pot, 0.7f, 160f));
            Set("IT_EvoSolar", solar);
            AssetDatabase.SaveAssets();
            Debug.Log("[InteriorPlantSpots] 식물 자리: 회전 링 8 · 휴게실 2 · 수경 농장 2 · 연결 허브 2 · 상업 구역 1 · 여객 도킹 포트 2 · 거주 타워 14 · 생태 모듈 20 · 복지 센터 3 · 문화 센터 2 · 태양광 어레이 6");
        }

        private static void Set(string name, List<PlantSpot> spots)
        {
            var t = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(DataFolder + name + ".asset");
            if (t == null)
            {
                Debug.LogWarning("[InteriorPlantSpots] 템플릿 없음: " + name);
                return;
            }
            t.EditorSetPlants(spots);
            EditorUtility.SetDirty(t);
        }
    }
}
