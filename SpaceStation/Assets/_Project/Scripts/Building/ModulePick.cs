using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 12-0 (U-5) 모듈 클릭 판정. 선택은 모델 모양 그대로의 MeshCollider("Pick" 자식, <see cref="LayerName"/> 레이어)로,
    /// 배치(옆 칸 붙이기)는 지금처럼 칸 크기 BoxCollider("Visual")로 한다.
    /// 칸 상자만 쓰면 태양광처럼 얇은 모듈은 빈 공간을 눌러도 선택되고, 뒤 모듈을 가렸다.
    /// Pick 콜라이더는 에디터 메뉴 SpaceStation/Modules/Add Pick Colliders가 모듈 프리팹에 굽는다
    /// (모델 메시가 읽기 불가라 실행 중에 MeshCollider를 붙이면 빌드에서 동작하지 않음).
    /// </summary>
    public static class ModulePick
    {
        public const string LayerName = "ModulePick";
        public const string ChildName = "Pick";

        /// <summary>Pick 레이어 번호 (없으면 -1).</summary>
        public static int Layer => LayerMask.NameToLayer(LayerName);

        /// <summary>선택용 레이캐스트 마스크: Pick 레이어만. 레이어가 없으면 기존 마스크.</summary>
        public static int SelectionMask(int fallback)
        {
            int layer = Layer;
            return layer >= 0 ? 1 << layer : fallback;
        }

        /// <summary>배치용 레이캐스트 마스크: Pick 레이어를 뺀 기존 마스크 (칸 상자로 면을 판정).</summary>
        public static int PlacementMask(int mask)
        {
            int layer = Layer;
            return layer >= 0 ? mask & ~(1 << layer) : mask;
        }
    }
}
