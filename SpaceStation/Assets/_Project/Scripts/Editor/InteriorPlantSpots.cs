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
            // 진화 2단계 화물 환적장 (ct_builder PLANTS): 모두 유리 덮개 속 — 라운지 나무 · 화분, 서쪽 광장 화분
            Set("IT_EvoCargoTransfer", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(3.05f, -0.92f, 5.0f), PlantKind.Tree, 0.55f, 0f),
                new PlantSpot(new Vector3(6.4f, -0.92f, 4.55f), PlantKind.Pot, 0.4f, 0f),
                new PlantSpot(new Vector3(5.05f, -0.92f, 1.3f), PlantKind.Pot, 0.4f, 53f),
            });
            // 진화 2단계 저장 단지 (sc_builder PLANTS): 창고 진열창 안 화분 둘 (실내라 유리 덮개 없음)
            Set("IT_EvoStorageComplex", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(-1.16f, -0.92f, -1.39f), PlantKind.Pot, 0.45f, 0f),
                new PlantSpot(new Vector3(25.16f, -0.92f, -1.39f), PlantKind.Pot, 0.45f, 53f),
            });
            // 진화 3단계 궤도 도시 (oc_builder PLANTS, 사용자 2026-11-10 "나무가 뭉뚱그려져있네" → 잎 한 장씩 나무 모델): 뒤 왼쪽 숲 소나무 · 활엽수, 뒤 오른쪽 꽃 들판 꽃나무,
            //  언덕 단 위 나무 · 꽃나무 (반쯤 묻힌 집 사이), 기둥 테라스 난간 밖 화단 (층마다 둘). 크기 = 바깥 나무 4 ~ 5 m (모델 높이 Tree 1.87 · Pine 2.47 · Blossom 1.67)
            Set("IT_EvoOrbitalCity", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(34.688f, -1.3f, 30.082f), PlantKind.Blossom, 1.7f, 0f),
                new PlantSpot(new Vector3(30.082f, -1.3f, 34.688f), PlantKind.Tree, 1.8f, 47f),
                new PlantSpot(new Vector3(1.918f, -1.3f, 34.688f), PlantKind.Pine, 1.7f, 94f),
                new PlantSpot(new Vector3(-2.688f, -1.3f, 30.082f), PlantKind.Tree, 1.8f, 141f),
                new PlantSpot(new Vector3(-2.688f, -1.3f, 1.918f), PlantKind.Tree, 1.7f, 188f),
                new PlantSpot(new Vector3(1.918f, -1.3f, -2.688f), PlantKind.Blossom, 1.8f, 235f),
                new PlantSpot(new Vector3(30.082f, -1.3f, -2.688f), PlantKind.Pine, 1.7f, 282f),
                new PlantSpot(new Vector3(34.688f, -1.3f, 1.918f), PlantKind.Pine, 1.8f, 329f),
                new PlantSpot(new Vector3(2.199f, -1.3f, 33.253f), PlantKind.Tree, 1.81f, 16f),
                new PlantSpot(new Vector3(5.933f, -1.3f, 33.716f), PlantKind.Pine, 1.68f, 63f),
                new PlantSpot(new Vector3(6.517f, -1.3f, 33.045f), PlantKind.Tree, 2.02f, 110f),
                new PlantSpot(new Vector3(2.339f, -1.3f, 30.851f), PlantKind.Pine, 1.46f, 157f),
                new PlantSpot(new Vector3(2.449f, -1.3f, 30.902f), PlantKind.Tree, 2.2f, 204f),
                new PlantSpot(new Vector3(3.331f, -1.3f, 34.069f), PlantKind.Pine, 1.72f, 251f),
                new PlantSpot(new Vector3(-1.869f, -1.3f, 32.216f), PlantKind.Tree, 1.95f, 298f),
                new PlantSpot(new Vector3(6.23f, -1.3f, 33.862f), PlantKind.Pine, 1.83f, 345f),
                new PlantSpot(new Vector3(5.287f, -1.3f, 33.017f), PlantKind.Tree, 1.79f, 32f),
                new PlantSpot(new Vector3(-1.563f, -1.3f, 27.677f), PlantKind.Pine, 1.51f, 79f),
                new PlantSpot(new Vector3(-0.722f, -1.3f, 30.031f), PlantKind.Tree, 1.94f, 126f),
                new PlantSpot(new Vector3(6.207f, -1.3f, 33.48f), PlantKind.Pine, 1.45f, 173f),
                new PlantSpot(new Vector3(-0.191f, -1.3f, 28.881f), PlantKind.Tree, 1.97f, 220f),
                new PlantSpot(new Vector3(0.254f, -1.3f, 30.138f), PlantKind.Pine, 1.64f, 267f),
                new PlantSpot(new Vector3(-0.682f, -1.3f, 27.424f), PlantKind.Tree, 2.13f, 314f),
                new PlantSpot(new Vector3(-1.289f, -1.3f, 31.743f), PlantKind.Pine, 1.67f, 1f),
                new PlantSpot(new Vector3(29.623f, -1.3f, 34.25f), PlantKind.Blossom, 1.97f, 48f),
                new PlantSpot(new Vector3(33.982f, -1.3f, 26.912f), PlantKind.Blossom, 2.05f, 95f),
                new PlantSpot(new Vector3(32.823f, -1.3f, 26.705f), PlantKind.Blossom, 2.09f, 142f),
                new PlantSpot(new Vector3(29.739f, -1.3f, 33f), PlantKind.Blossom, 2.26f, 189f),
                new PlantSpot(new Vector3(26.957f, -1.3f, 33.892f), PlantKind.Blossom, 1.99f, 236f),
                new PlantSpot(new Vector3(30.412f, -1.3f, 32.231f), PlantKind.Blossom, 2.15f, 283f),
                new PlantSpot(new Vector3(27.269f, -1.3f, 33.512f), PlantKind.Blossom, 2.37f, 330f),
                new PlantSpot(new Vector3(30.087f, -1.3f, 33.34f), PlantKind.Blossom, 1.84f, 17f),
                new PlantSpot(new Vector3(30.539f, -1.3f, 33.51f), PlantKind.Blossom, 2.4f, 64f),
                new PlantSpot(new Vector3(33.432f, -1.3f, 29.254f), PlantKind.Blossom, 2.03f, 111f),
                new PlantSpot(new Vector3(14.463f, 1.5f, 24.716f), PlantKind.Blossom, 1.2f, 158f),
                new PlantSpot(new Vector3(9.221f, 1.5f, 21.689f), PlantKind.Tree, 1.15f, 205f),
                new PlantSpot(new Vector3(7.15f, 1.5f, 16f), PlantKind.Blossom, 1.2f, 252f),
                new PlantSpot(new Vector3(19.027f, 1.5f, 7.684f), PlantKind.Tree, 1.15f, 299f),
                new PlantSpot(new Vector3(23.422f, 1.5f, 11.18f), PlantKind.Blossom, 1.2f, 346f),
                new PlantSpot(new Vector3(21.406f, 3.9f, 19.786f), PlantKind.Blossom, 1.08f, 33f),
                new PlantSpot(new Vector3(12.7f, 3.9f, 21.716f), PlantKind.Tree, 1.03f, 80f),
                new PlantSpot(new Vector3(14.292f, 3.9f, 9.625f), PlantKind.Blossom, 1.08f, 127f),
                new PlantSpot(new Vector3(19.3f, 3.9f, 10.284f), PlantKind.Tree, 1.03f, 174f),
                new PlantSpot(new Vector3(18.864f, 39.2f, 18.864f), PlantKind.Blossom, 0.5f, 221f),
                new PlantSpot(new Vector3(13.136f, 39.2f, 18.864f), PlantKind.Tree, 0.5f, 268f),
                new PlantSpot(new Vector3(13.136f, 39.2f, 13.136f), PlantKind.Blossom, 0.5f, 315f),
                new PlantSpot(new Vector3(18.864f, 39.2f, 13.136f), PlantKind.Tree, 0.5f, 2f),
            });
            // 진화 3단계 바이오스피어 (bio3_builder PLANTS, 2026-11-11): 숲 비탈 나무 · 꽃나무 · 소나무 (문 · 아치 · 샘 비킴), 네 모서리 (과수원 꽃나무 · 반딧불이 숲 · 연못가),
            //  고리 산책길 모서리 끝 꽃나무 (모델 = 궤도 도시와 같은 잎 한 장씩 나무)
            Set("IT_EvoBiosphere", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(17.454f, -0.395f, 9.619f), PlantKind.Tree, 2.13f, 0f),
                new PlantSpot(new Vector3(17.212f, -0.493f, 10.98f), PlantKind.Blossom, 2.19f, 47f),
                new PlantSpot(new Vector3(15.121f, -0.227f, 14.176f), PlantKind.Tree, 2.26f, 94f),
                new PlantSpot(new Vector3(14.239f, -0.117f, 14.902f), PlantKind.Tree, 2.3f, 141f),
                new PlantSpot(new Vector3(13.316f, -0.272f, 15.839f), PlantKind.Blossom, 2.23f, 188f),
                new PlantSpot(new Vector3(12.057f, -0.351f, 16.646f), PlantKind.Pine, 1.97f, 235f),
                new PlantSpot(new Vector3(3.534f, 0.037f, 15.926f), PlantKind.Blossom, 2.12f, 282f),
                new PlantSpot(new Vector3(-1.123f, -0.486f, 11.223f), PlantKind.Tree, 2f, 329f),
                new PlantSpot(new Vector3(3.175f, 0.091f, 0.396f), PlantKind.Blossom, 2.19f, 16f),
                new PlantSpot(new Vector3(3.819f, 0.013f, -0.122f), PlantKind.Pine, 1.68f, 63f),
                new PlantSpot(new Vector3(12.932f, -0.418f, -0.252f), PlantKind.Tree, 2.03f, 110f),
                new PlantSpot(new Vector3(17.333f, -0.426f, 5.666f), PlantKind.Tree, 2.23f, 157f),
                new PlantSpot(new Vector3(20.226f, -1.3f, 11.048f), PlantKind.Blossom, 2.16f, 204f),
                new PlantSpot(new Vector3(11.048f, -1.3f, 20.226f), PlantKind.Tree, 2.07f, 251f),
                new PlantSpot(new Vector3(4.952f, -1.3f, 20.226f), PlantKind.Tree, 2.07f, 298f),
                new PlantSpot(new Vector3(-4.226f, -1.3f, 11.048f), PlantKind.Blossom, 2.16f, 345f),
                new PlantSpot(new Vector3(-4.226f, -1.3f, 4.952f), PlantKind.Blossom, 2.16f, 32f),
                new PlantSpot(new Vector3(4.952f, -1.3f, -4.226f), PlantKind.Tree, 2.07f, 79f),
                new PlantSpot(new Vector3(11.048f, -1.3f, -4.226f), PlantKind.Tree, 2.07f, 126f),
                new PlantSpot(new Vector3(20.226f, -1.3f, 4.952f), PlantKind.Blossom, 2.16f, 173f),
                new PlantSpot(new Vector3(20.641f, -1.3f, -1.141f), PlantKind.Tree, 2.18f, 220f),
                new PlantSpot(new Vector3(17.441f, -1.3f, -4.441f), PlantKind.Blossom, 2.28f, 267f),
                new PlantSpot(new Vector3(20.041f, -1.3f, -3.941f), PlantKind.Pine, 1.9f, 314f),
                new PlantSpot(new Vector3(4.091f, -1.3f, 20.398f), PlantKind.Blossom, 2.4f, 1f),
                new PlantSpot(new Vector3(1.033f, -1.3f, 20.831f), PlantKind.Blossom, 2.64f, 48f),
                new PlantSpot(new Vector3(-0.269f, -1.3f, 18.031f), PlantKind.Blossom, 2.88f, 95f),
                new PlantSpot(new Vector3(-3.266f, -1.3f, 17.287f), PlantKind.Blossom, 2.4f, 142f),
                new PlantSpot(new Vector3(-3.425f, -1.3f, 14.203f), PlantKind.Blossom, 2.64f, 189f),
                new PlantSpot(new Vector3(-5.924f, -1.3f, 12.39f), PlantKind.Blossom, 2.88f, 236f),
                new PlantSpot(new Vector3(20.27f, -1.3f, 10.385f), PlantKind.Tree, 2.53f, 283f),
                new PlantSpot(new Vector3(22.517f, -1.3f, 13.141f), PlantKind.Pine, 2.24f, 330f),
                new PlantSpot(new Vector3(19.037f, -1.3f, 13.868f), PlantKind.Tree, 2.9f, 17f),
                new PlantSpot(new Vector3(20.379f, -1.3f, 17.16f), PlantKind.Pine, 2.09f, 64f),
                new PlantSpot(new Vector3(16.839f, -1.3f, 16.839f), PlantKind.Tree, 2.71f, 111f),
                new PlantSpot(new Vector3(17.16f, -1.3f, 20.379f), PlantKind.Pine, 2.39f, 158f),
                new PlantSpot(new Vector3(13.868f, -1.3f, 19.037f), PlantKind.Tree, 2.53f, 205f),
                new PlantSpot(new Vector3(13.141f, -1.3f, 22.517f), PlantKind.Pine, 2.24f, 252f),
                new PlantSpot(new Vector3(10.385f, -1.3f, 20.27f), PlantKind.Tree, 2.9f, 299f),
            });
            // 진화 2단계 연구원 (mz_builder PLANTS): 연구실 안 화분 하나 (유리 연구실 = 실내)
            Set("IT_EvoResearchInstitute", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(2.883f, -0.94f, 5.117f), PlantKind.Pot, 0.42f, 0f),
            });
            // 진화 2단계 식량 공장 (ff_builder PLANTS): 2층 카페 화분 둘
            Set("IT_EvoFoodFactory", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(13.75f, 4.81f, 2.1f), PlantKind.Pot, 0.62f, 0f),
                new PlantSpot(new Vector3(20.2f, 4.81f, 2.1f), PlantKind.Pot, 0.62f, 53f),
            });
            // 진화 2단계 바이오돔 (bd_builder PLANTS): 큰 돔 잔디에 바로 심은 나무 아홉 (돔 안이라 유리 덮개 없음, 회전은 시안에서 유리까지 잰 그대로)
            var biodome = new List<PlantSpot>();
            for (int i = 0; i < InteriorTemplateBuilder.BiodomeTrees.Length; i++)
            {
                var (x, z, s) = InteriorTemplateBuilder.BiodomeTrees[i];
                biodome.Add(new PlantSpot(new Vector3(x, -1.288f, z), PlantKind.Tree, s, -47f * i));
            }
            Set("IT_EvoBiodome", biodome);
            // 진화 2단계 치안 본부 (sh_builder PLANTS): 청사 안 화단 나무 둘 (실내라 유리 덮개 없음)
            Set("IT_EvoSecurityHQ", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(-2.2f, -0.99f, 10.2f), PlantKind.Tree, 0.62f, 0f),
                new PlantSpot(new Vector3(2.3f, -0.99f, 10.6f), PlantKind.Tree, 0.55f, 47f),
            });
            // 진화 2단계 시민 회관 (civ_builder PLANTS): 의료동 화단 나무 넷 · 라운지 화분 넷 (실내라 유리 덮개 없음)
            Set("IT_EvoCivicHall", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(10.35f, -0.99f, -2.73f), PlantKind.Tree, 0.62f, 0f),
                new PlantSpot(new Vector3(13.65f, -0.99f, -2.73f), PlantKind.Tree, 0.62f, 47f),
                new PlantSpot(new Vector3(11f, -0.99f, 10.83f), PlantKind.Tree, 0.62f, 94f),
                new PlantSpot(new Vector3(13f, -0.99f, 10.83f), PlantKind.Tree, 0.62f, 141f),
                new PlantSpot(new Vector3(26.298f, -0.92f, 2.298f), PlantKind.Pot, 0.7f, 188f),
                new PlantSpot(new Vector3(21.702f, -0.92f, 2.298f), PlantKind.Pot, 0.7f, 235f),
                new PlantSpot(new Vector3(21.702f, -0.92f, -2.298f), PlantKind.Pot, 0.7f, 282f),
                new PlantSpot(new Vector3(26.298f, -0.92f, -2.298f), PlantKind.Pot, 0.7f, 329f),
            });
            // 진화 2단계 대형 토러스 (gt_builder PLANTS): 동네 나무 12 (집 사이 8 · 0.42배, 마당 4 · 0.5배) · 아치 집 화분 8 (0.42배). 유리 지붕 안이라 살아 있는 식물도 유리 덮개 없음
            Set("IT_EvoGrandTorus", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(30.611f, -0.99f, 27.864f), PlantKind.Pot, 0.42f, 0f),
                new PlantSpot(new Vector3(27.864f, -0.99f, 30.611f), PlantKind.Pot, 0.42f, 47f),
                new PlantSpot(new Vector3(4.136f, -0.99f, 30.611f), PlantKind.Pot, 0.42f, 94f),
                new PlantSpot(new Vector3(1.389f, -0.99f, 27.864f), PlantKind.Pot, 0.42f, 141f),
                new PlantSpot(new Vector3(1.389f, -0.99f, 4.136f), PlantKind.Pot, 0.42f, 188f),
                new PlantSpot(new Vector3(4.136f, -0.99f, 1.389f), PlantKind.Pot, 0.42f, 235f),
                new PlantSpot(new Vector3(27.864f, -0.99f, 1.389f), PlantKind.Pot, 0.42f, 282f),
                new PlantSpot(new Vector3(30.611f, -0.99f, 4.136f), PlantKind.Pot, 0.42f, 329f),
                new PlantSpot(new Vector3(7.57f, -1.175f, 3.053f), PlantKind.Tree, 0.42f, 16f),
                new PlantSpot(new Vector3(24.43f, -1.175f, 3.053f), PlantKind.Tree, 0.42f, 63f),
                new PlantSpot(new Vector3(27.314f, -1.175f, 4.686f), PlantKind.Tree, 0.5f, 110f),
                new PlantSpot(new Vector3(3.053f, -1.175f, 24.43f), PlantKind.Tree, 0.42f, 157f),
                new PlantSpot(new Vector3(3.053f, -1.175f, 7.57f), PlantKind.Tree, 0.42f, 204f),
                new PlantSpot(new Vector3(4.686f, -1.175f, 4.686f), PlantKind.Tree, 0.5f, 251f),
                new PlantSpot(new Vector3(24.43f, -1.175f, 28.947f), PlantKind.Tree, 0.42f, 298f),
                new PlantSpot(new Vector3(7.57f, -1.175f, 28.947f), PlantKind.Tree, 0.42f, 345f),
                new PlantSpot(new Vector3(4.686f, -1.175f, 27.314f), PlantKind.Tree, 0.5f, 32f),
                new PlantSpot(new Vector3(28.947f, -1.175f, 7.57f), PlantKind.Tree, 0.42f, 79f),
                new PlantSpot(new Vector3(28.947f, -1.175f, 24.43f), PlantKind.Tree, 0.42f, 126f),
                new PlantSpot(new Vector3(27.314f, -1.175f, 27.314f), PlantKind.Tree, 0.5f, 173f),
            });
            // 진화 2단계 아콜로지 (ar_builder PLANTS): 섬 도시 나무 다섯 (돌 테 안 반지름 0.42 — 0.62배), 뿌리 층 집 창 아래 화분 여섯 (0.42배). 알 속이라 살아 있는 식물도 유리 덮개 없음
            Set("IT_EvoArcology", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(7.999f, 11.005f, 4.105f), PlantKind.Tree, 0.62f, 20f),
                new PlantSpot(new Vector3(5.136f, 11.005f, 7.835f), PlantKind.Tree, 0.62f, 92f),
                new PlantSpot(new Vector3(0.703f, 11.005f, 6.266f), PlantKind.Tree, 0.62f, 164f),
                new PlantSpot(new Vector3(0.827f, 11.005f, 1.565f), PlantKind.Tree, 0.62f, 236f),
                new PlantSpot(new Vector3(5.335f, 11.005f, 0.229f), PlantKind.Tree, 0.62f, 308f),
                new PlantSpot(new Vector3(8.38f, 6.86f, 4f), PlantKind.Pot, 0.42f, 30f),
                new PlantSpot(new Vector3(6.19f, 6.86f, 7.793f), PlantKind.Pot, 0.42f, 90f),
                new PlantSpot(new Vector3(1.81f, 6.86f, 7.793f), PlantKind.Pot, 0.42f, 150f),
                new PlantSpot(new Vector3(-0.38f, 6.86f, 4f), PlantKind.Pot, 0.42f, 210f),
                new PlantSpot(new Vector3(1.81f, 6.86f, 0.207f), PlantKind.Pot, 0.42f, 270f),
                new PlantSpot(new Vector3(6.19f, 6.86f, 0.207f), PlantKind.Pot, 0.42f, 330f),
            });
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
            // 진화 1단계 수소 플랜트 (hp_builder POTS): +z 칸 끝 창가 화분 둘 (흙 F + 0.462, 0.7배)
            Set("IT_EvoHydrogen", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(-2.3f, F + 0.462f, 10.4f), PlantKind.Pot, 0.7f, 50f),
                new PlantSpot(new Vector3(2.3f, F + 0.462f, 10.4f), PlantKind.Pot, 0.7f, 210f),
            });
            // 진화 1단계 중력 거주구 (gh_builder PLANTS): 허브 대각선 화분 넷 (반지름 4.3, 흙 F + 0.385, 0.7배) · 원형 통로 대각선 나무 화단 넷 (반지름 7.65, 흙 F + 0.475) ·
            //   골목 집 문 앞 작은 화분 넷 (0.55배) · 골목 끝 화분 둘
            var gravity = new List<PlantSpot>();
            n = 0;
            foreach (float deg in new[] { 45f, 135f, 225f, 315f })
            {
                float a = deg * Mathf.Deg2Rad;
                gravity.Add(new PlantSpot(new Vector3(8f + 4.3f * Mathf.Cos(a), F + 0.385f, 8f + 4.3f * Mathf.Sin(a)), PlantKind.Pot, 0.7f, (n++) * 71f));
                gravity.Add(new PlantSpot(new Vector3(8f + 7.65f * Mathf.Cos(a), F + 0.475f, 8f + 7.65f * Mathf.Sin(a)), PlantKind.Tree, 0.95f, (n++) * 71f));
            }
            foreach (var (x, z) in new[] { (6.69f, 27.78f), (9.31f, 26.12f), (6.69f, 29.88f), (9.31f, 28.22f) })
                gravity.Add(new PlantSpot(new Vector3(x, F + 0.385f, z), PlantKind.Pot, 0.55f, (n++) * 71f));
            gravity.Add(new PlantSpot(new Vector3(5.6f, F + 0.385f, 34.2f), PlantKind.Pot, 0.7f, 40f));
            gravity.Add(new PlantSpot(new Vector3(10.4f, F + 0.385f, 34.2f), PlantKind.Pot, 0.7f, 190f));
            Set("IT_EvoGravity", gravity);
            // 진화 1단계 수직 농장 (vf_builder PLANTS): 1층 엘리베이터 뒤 양옆 화분 둘 · 2층 하나 (흙 층 바닥 + 0.415, 0.7배)
            Set("IT_EvoFarm", new List<PlantSpot>
            {
                new PlantSpot(new Vector3(-1.9f, F + 0.415f, 2.25f), PlantKind.Pot, 0.7f, 30f),
                new PlantSpot(new Vector3(-1.9f, F + 0.415f, -2.25f), PlantKind.Pot, 0.7f, 150f),
                new PlantSpot(new Vector3(-1.9f, 8f + F + 0.415f, 2.25f), PlantKind.Pot, 0.7f, 260f),
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[InteriorPlantSpots] 식물 자리: 회전 링 8 · 휴게실 2 · 수경 농장 2 · 연결 허브 2 · 상업 구역 1 · 여객 도킹 포트 2 · 거주 타워 14 · 생태 모듈 20 · 복지 센터 3 · 문화 센터 2 · 태양광 어레이 6 · 수소 플랜트 2 · 중력 거주구 14 · 수직 농장 3");
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
