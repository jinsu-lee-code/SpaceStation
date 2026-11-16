using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 정원 식물 하나 (식물 아틀라스 메시 <c>Art/Plants/Meshes/{Mesh}.fbx</c> — 템플릿 빌드가 그 자리에 놓음, 충돌체 없음):
    /// 원점 자리 · yaw (Unity, 0° = +z) · 크기 (생울타리에 붙는 덩굴은 면 법선 쪽 축을 눌러 길 폭을 지킴).
    /// </summary>
    public readonly struct GardenPlant
    {
        public readonly string Mesh;
        public readonly Vector3 Position;
        public readonly float Yaw;
        public readonly Vector3 Scale;

        public GardenPlant(string mesh, float x, float y, float z, float yaw, float sx, float sy, float sz)
        {
            Mesh = mesh;
            Position = new Vector3(x, y, z);
            Yaw = yaw;
            Scale = new Vector3(sx, sy, sz);
        }
    }

    public static partial class AICore
    {
        /// <summary>
        /// 정원 식물 (Blender ai3_builder <c>garden</c> → DECOR 그대로, 2026-11-16 식물 아틀라스 ②): 생울타리 어깨에서 늘어진 꽃송이 단 등나무 벽 덩굴
        /// (앞 반구 보라 · 뒤 반구 금빛 — 길 찾기 색) · 풀밭 맥문동 · 수호초 · 벚나무 덤불 — 빌더가 생울타리 속 · 뜸 · 길 · 소품 · 해치 · 주민 자리를 검사함.
        /// </summary>
        public static readonly GardenPlant[] Garden =
        {
            new GardenPlant("WisteriaVineWallBloom_Mesh", 2.267f, -0.108f, 3.014f, -177.3f, 0.559f, 0.559f, 0.235f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 4.798f, -0.117f, 0.79f, -132.1f, 0.596f, 0.596f, 0.25f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 7.299f, -0.147f, -0.848f, 9.3f, 0.569f, 0.569f, 0.239f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 9.178f, -0.114f, -0.75f, -14.4f, 0.581f, 0.581f, 0.244f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 12.552f, -0.072f, 2.812f, 149.8f, 0.647f, 0.647f, 0.272f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 17.576f, -0.138f, 0.926f, -3.1f, 0.561f, 0.561f, 0.236f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 18.592f, -0.122f, 0.374f, -92.6f, 0.704f, 0.704f, 0.296f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 17.927f, -0.111f, -1.268f, 141.6f, 0.676f, 0.676f, 0.284f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 13.527f, -0.119f, -2.203f, 177.6f, 0.705f, 0.705f, 0.296f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 11.875f, -0.111f, -2.605f, 0.5f, 0.709f, 0.709f, 0.298f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 10.307f, -0.08f, -2.256f, -178.5f, 0.669f, 0.669f, 0.281f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 8.518f, -0.114f, -2.583f, -0.7f, 0.552f, 0.552f, 0.232f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 6.399f, -0.121f, -2.26f, -178.3f, 0.55f, 0.55f, 0.231f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 4.384f, -0.082f, -2.604f, 2.8f, 0.634f, 0.634f, 0.266f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", -0.524f, -0.118f, -2.245f, 14.7f, 0.673f, 0.673f, 0.283f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", -1.976f, -0.066f, -1.686f, 35.7f, 0.625f, 0.625f, 0.262f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", -2.268f, -0.124f, 0.181f, -95.1f, 0.566f, 0.566f, 0.238f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 0.402f, -0.2f, 0.889f, -3.4f, 0.619f, 0.619f, 0.26f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 2.67f, -0.186f, 1.899f, 88.2f, 0.564f, 0.564f, 0.237f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 5.02f, -0.114f, -0.749f, 16f, 0.586f, 0.586f, 0.246f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 10.668f, -0.148f, -0.805f, 0.3f, 0.553f, 0.553f, 0.232f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 12.564f, -0.155f, -0.479f, 178.6f, 0.557f, 0.557f, 0.234f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 14.264f, -0.104f, -0.806f, 0.8f, 0.707f, 0.707f, 0.297f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 12.657f, -0.185f, 1.312f, 162.4f, 0.582f, 0.582f, 0.32f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 15.459f, -0.141f, 0.901f, 0.8f, 0.552f, 0.552f, 0.232f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 5.704f, -0.121f, 10.261f, -3.5f, 0.624f, 0.624f, 0.262f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 7.531f, -0.16f, 10.606f, -178.7f, 0.583f, 0.583f, 0.245f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 9.683f, -0.161f, 10.257f, 3.1f, 0.683f, 0.683f, 0.287f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 11.531f, -0.079f, 10.593f, -179.7f, 0.614f, 0.614f, 0.258f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 13.594f, -0.091f, 10.21f, 2.5f, 0.7f, 0.7f, 0.294f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 17.59f, -0.065f, 9.518f, 24.2f, 0.55f, 0.55f, 0.231f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 18.598f, -0.12f, 7.859f, -90.9f, 0.582f, 0.582f, 0.245f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 13.918f, -0.117f, 5.016f, -1.7f, 0.568f, 0.568f, 0.239f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 9.838f, -0.165f, 8.169f, 24.6f, 0.681f, 0.681f, 0.286f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 8.374f, -0.151f, 8.499f, 7.8f, 0.576f, 0.576f, 0.242f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 6.777f, -0.119f, 8.731f, 166.2f, 0.603f, 0.603f, 0.253f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 5.118f, -0.111f, 7.488f, -38.8f, 0.705f, 0.705f, 0.296f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 2.737f, -0.071f, 4.977f, 2.2f, 0.576f, 0.576f, 0.242f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", -1.635f, -0.123f, 6.724f, -8.5f, 0.645f, 0.645f, 0.271f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", -2.257f, -0.071f, 7.963f, -93.3f, 0.712f, 0.712f, 0.299f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", -1.904f, -0.112f, 9.73f, 151.9f, 0.708f, 0.708f, 0.297f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 0.121f, -0.157f, 9.989f, -10f, 0.653f, 0.653f, 0.274f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 3.031f, -0.21f, 6.246f, -90.3f, 0.625f, 0.625f, 0.263f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 3.021f, -0.18f, 8.015f, -91f, 0.616f, 0.616f, 0.259f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 13.331f, -0.117f, 8.725f, 48.4f, 0.695f, 0.695f, 0.292f),
            new GardenPlant("CherryShrub_02_Mesh", 19.275f, -2.08f, 11.43f, 30f, 0.75f, 0.75f, 0.75f),
            new GardenPlant("LiriopeSmall_Mesh", -3.3f, -2.08f, -2.9f, 0f, 0.55f, 0.55f, 0.55f),
            new GardenPlant("LiriopeSmall_Mesh", -3.15f, -2.08f, 10.9f, 140f, 0.55f, 0.55f, 0.55f),
            new GardenPlant("JapaneseSpurgeSmall_Mesh", -2.75f, -2.08f, -3.45f, 60f, 0.6f, 0.6f, 0.6f),
            new GardenPlant("JapaneseSpurgeSmall_Mesh", -2.75f, -2.08f, 11.45f, 200f, 0.6f, 0.6f, 0.6f),
            new GardenPlant("CherryShrub_02_Mesh", 19.4f, -2.08f, -3.3f, 210f, 0.7f, 0.7f, 0.7f),
            new GardenPlant("LiriopeSmall_Mesh", 20.325f, -2.08f, -3.53f, 80f, 0.55f, 0.55f, 0.55f),
            new GardenPlant("JapaneseSpurgeSmall_Mesh", 19.13f, -2.08f, -1.47f, 10f, 0.6f, 0.6f, 0.6f),
            new GardenPlant("LiriopeSmall_Mesh", 20.18f, -2.08f, -0.47f, 220f, 0.5f, 0.5f, 0.5f),
            new GardenPlant("JapaneseSpurgeSmall_Mesh", 27.587f, -2.08f, -2.165f, 120f, 0.6f, 0.6f, 0.6f),
        };
    }
}
