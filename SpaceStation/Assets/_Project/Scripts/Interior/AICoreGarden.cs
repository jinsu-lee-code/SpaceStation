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
            new GardenPlant("WisteriaVineWallBloom_Mesh", 1.593f, -0.052f, 2.594f, -48.1f, 0.646f, 0.646f, 0.272f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 5.092f, -0.078f, 0.074f, 38.1f, 0.694f, 0.694f, 0.291f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 6.261f, -0.064f, -0.233f, -175f, 0.58f, 0.58f, 0.244f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 8.764f, -0.055f, -0.78f, -12.5f, 0.784f, 0.784f, 0.329f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 1.847f, -0.156f, 2.995f, 173.6f, 0.658f, 0.658f, 0.276f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 4.796f, -0.117f, 0.792f, -132.1f, 0.729f, 0.729f, 0.306f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 5.73f, -0.176f, 0.054f, -148f, 0.77f, 0.77f, 0.323f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 7.634f, -0.106f, -0.529f, -174.8f, 0.633f, 0.633f, 0.266f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 12.485f, -0.074f, 2.129f, -66.7f, 0.816f, 0.816f, 0.343f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 13.395f, -0.074f, 3.014f, -179.7f, 0.636f, 0.636f, 0.267f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 16.681f, -0.165f, 0.973f, 13.8f, 0.648f, 0.648f, 0.272f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 18.079f, -0.117f, -1.087f, 127.9f, 0.622f, 0.622f, 0.261f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 18.593f, -0.122f, 0.398f, -92.6f, 0.63f, 0.63f, 0.264f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 17.837f, -0.118f, -1.754f, -28.7f, 0.767f, 0.767f, 0.322f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 13.774f, -0.068f, -2.216f, 174f, 0.726f, 0.726f, 0.305f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 11.634f, -0.111f, -2.231f, -179.8f, 0.63f, 0.63f, 0.264f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 9.936f, -0.057f, -2.268f, 178f, 0.728f, 0.728f, 0.306f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 7.962f, -0.158f, -2.259f, -179.5f, 0.73f, 0.73f, 0.306f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 6.073f, -0.166f, -2.27f, 179.5f, 0.776f, 0.776f, 0.326f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 4.172f, -0.116f, -2.241f, 179f, 0.743f, 0.743f, 0.312f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 13.54f, -0.158f, -2.556f, 0.5f, 0.676f, 0.676f, 0.284f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 11.313f, -0.106f, -2.591f, 1.2f, 0.67f, 0.67f, 0.281f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 9.546f, -0.112f, -2.585f, 1.4f, 0.657f, 0.657f, 0.276f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 7.748f, -0.153f, -2.598f, -0.3f, 0.721f, 0.721f, 0.303f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 5.808f, -0.079f, -2.565f, -3.4f, 0.819f, 0.819f, 0.344f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 4.099f, -0.161f, -2.579f, 3.8f, 0.735f, 0.735f, 0.309f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", -0.1f, -0.046f, -1.999f, -165.8f, 0.796f, 0.796f, 0.334f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", -2.26f, -0.155f, 0.05f, -95.1f, 0.679f, 0.679f, 0.285f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", -0.649f, -0.07f, -2.175f, 14.7f, 0.804f, 0.804f, 0.338f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", -2.305f, -0.108f, -1.362f, 51.8f, 0.514f, 0.514f, 0.216f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 0.393f, -0.186f, -0.79f, -2f, 0.695f, 0.695f, 0.292f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 0.347f, -0.141f, 0.929f, -3.4f, 0.793f, 0.793f, 0.333f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 1.306f, -0.16f, -0.107f, -94.3f, 0.714f, 0.714f, 0.3f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 1.781f, -0.144f, -0.791f, -3.5f, 0.796f, 0.796f, 0.334f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 2.739f, -0.098f, 1.64f, 74f, 0.635f, 0.635f, 0.267f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 3.3f, -0.14f, 1.34f, -161.5f, 0.675f, 0.675f, 0.283f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 4.736f, -0.152f, -0.562f, 42.5f, 0.648f, 0.648f, 0.272f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 5.042f, -0.149f, -0.407f, -161.6f, 0.785f, 0.785f, 0.33f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 10.44f, -0.095f, -0.809f, 0.3f, 0.671f, 0.671f, 0.282f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 12.592f, -0.105f, -0.792f, 3.2f, 0.789f, 0.789f, 0.332f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 13.926f, -0.115f, -0.815f, -0.3f, 0.665f, 0.665f, 0.279f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 12.364f, -0.188f, -0.494f, -177.5f, 0.724f, 0.724f, 0.304f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 14.621f, -0.194f, -0.486f, 178.2f, 0.764f, 0.764f, 0.321f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 13.27f, -0.094f, 1.555f, -71.8f, 0.63f, 0.63f, 0.265f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 13.006f, -0.083f, 1.845f, 106.9f, 0.801f, 0.801f, 0.336f),
            new GardenPlant("WisteriaVineWallBloom_Mesh", 15.63f, -0.142f, 0.903f, -2f, 0.726f, 0.726f, 0.305f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 6.407f, -0.064f, 10.272f, 2.1f, 0.669f, 0.669f, 0.281f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 8.634f, -0.108f, 10.264f, -5.1f, 0.661f, 0.661f, 0.277f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 10.555f, -0.068f, 10.257f, 4.1f, 0.715f, 0.715f, 0.3f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 12.751f, -0.061f, 10.255f, 0.3f, 0.662f, 0.662f, 0.278f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 7.306f, -0.063f, 10.603f, -178.7f, 0.745f, 0.745f, 0.313f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 9.374f, -0.118f, 10.605f, 177.9f, 0.635f, 0.635f, 0.267f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 11.549f, -0.079f, 10.564f, -179.7f, 0.698f, 0.698f, 0.293f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 13.27f, -0.161f, 10.546f, -179.7f, 0.747f, 0.747f, 0.314f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 17.435f, -0.112f, 9.627f, 18.3f, 0.706f, 0.706f, 0.296f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 18.219f, -0.102f, 8.614f, 84.8f, 0.803f, 0.803f, 0.337f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 17.945f, -0.123f, 7.003f, -175.5f, 0.711f, 0.711f, 0.299f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 18.447f, -0.157f, 9.037f, -104f, 0.671f, 0.671f, 0.282f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 18.607f, -0.114f, 7.352f, -92.3f, 0.615f, 0.615f, 0.258f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 14.08f, -0.068f, 5.373f, 175.8f, 0.729f, 0.729f, 0.306f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 14.186f, -0.12f, 5.051f, -6.9f, 0.721f, 0.721f, 0.303f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 12.787f, -0.111f, 5.034f, 15.8f, 0.549f, 0.549f, 0.231f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 8.186f, -0.074f, 8.858f, -174.6f, 0.768f, 0.768f, 0.323f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 6.792f, -0.069f, 8.717f, 166.2f, 0.621f, 0.621f, 0.261f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 9.726f, -0.123f, 8.208f, 24.6f, 0.667f, 0.667f, 0.28f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 7.899f, -0.125f, 8.56f, 0.6f, 0.784f, 0.784f, 0.329f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 5.823f, -0.056f, 7.999f, -28.6f, 0.753f, 0.753f, 0.316f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 4.631f, -0.18f, 7.069f, -50.3f, 0.748f, 0.748f, 0.314f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", -1.087f, -0.054f, 7.055f, -176.4f, 0.728f, 0.728f, 0.306f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", -2.261f, -0.073f, 7.84f, -93.3f, 0.705f, 0.705f, 0.296f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", -1.597f, -0.118f, 9.526f, -31.1f, 0.703f, 0.703f, 0.295f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 0.092f, -0.069f, 10.003f, -10f, 0.779f, 0.779f, 0.327f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 1.624f, -0.109f, 10.165f, -6.3f, 0.736f, 0.736f, 0.309f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", -2.471f, -0.124f, 6.831f, 57.9f, 0.516f, 0.516f, 0.217f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", -2.585f, -0.075f, 8.335f, 90.8f, 0.655f, 0.655f, 0.275f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", -0.884f, -0.118f, 10.133f, 163.1f, 0.776f, 0.776f, 0.326f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 0.433f, -0.114f, 10.376f, 172.5f, 0.808f, 0.808f, 0.34f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", -0.067f, -0.123f, 8.46f, -2.6f, 0.809f, 0.809f, 0.34f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 2.989f, -0.096f, 5.748f, -69.8f, 0.616f, 0.616f, 0.259f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 2.684f, -0.14f, 6.14f, 90.8f, 0.771f, 0.771f, 0.324f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 0.29f, -0.173f, 7.076f, -179.2f, 0.647f, 0.647f, 0.272f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 5.785f, -0.198f, 8.794f, 178.8f, 0.755f, 0.755f, 0.317f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 11.034f, -0.151f, 8.725f, -160.5f, 0.535f, 0.535f, 0.225f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 2.998f, -0.139f, 7.675f, -85.3f, 0.631f, 0.631f, 0.265f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 12.97f, -0.138f, 5.956f, 91.2f, 0.632f, 0.632f, 0.265f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 13.332f, -0.191f, 5.96f, -107.7f, 0.645f, 0.645f, 0.271f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 13.248f, -0.11f, 8.817f, 48.4f, 0.578f, 0.578f, 0.243f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 13.324f, -0.156f, 9.271f, -110.9f, 0.663f, 0.663f, 0.279f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 16.43f, -0.105f, 7.754f, 88.5f, 0.633f, 0.633f, 0.266f),
            new GardenPlant("WisteriaVineWallBloomGold_Mesh", 16.779f, -0.199f, 7.902f, -88.9f, 0.631f, 0.631f, 0.265f),
        };
    }
}
