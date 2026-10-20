using SpaceStation.Building;
using SpaceStation.Core;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 템플릿 안 실행 중 표시(연결 허브 표지판 · 센서 레이더 · 데이터 센터 표시등 · 단백질 컨베이어)의 공통 틀.
    /// 템플릿 빌더가 프리팹 루트에 붙이고, <see cref="InteriorAtmosphere"/>가 방 상태를 읽을 때(0.25초마다) <see cref="Poll"/>로
    /// 정거장 · 모듈 · 활성 여부를 넘긴다. 움직임은 각자 Update에서 실제 시간(unscaled)으로 — 방문 중 게임 시간이 멈춰 있어도 보이게.
    /// 좌표는 모두 프리팹 로컬(= 템플릿 로컬, 회전 전).
    /// </summary>
    public abstract class InteriorRoomDisplay : MonoBehaviour
    {
        /// <summary>글자에 쓰는 글꼴 (<see cref="InteriorMode"/>가 내부를 만들기 전에 정함).</summary>
        public static TMP_FontAsset Font { get; set; }

        protected StationController Station { get; private set; }
        protected ModuleInstance Module { get; private set; }
        /// <summary>모듈이 활성(코어에 연결 · 전력)인지. 꺼지면 표시도 꺼지거나 멈춘다.</summary>
        protected bool Active { get; private set; }
        protected bool Polled { get; private set; }

        public void Poll(StationController station, ModuleInstance module, bool active)
        {
            Station = station;
            Module = module;
            Active = active;
            Polled = true;
            Refresh();
        }

        /// <summary>상태가 바뀌었을 수 있음 (0.25초마다).</summary>
        protected abstract void Refresh();

        /// <summary>벽 · 화면 위 글자 칸. center = 면 가운데, normal = 보는 사람 쪽, up = 글자 위쪽. 칸 크기에 맞춰 글자 크기를 줄인다.</summary>
        protected TextMeshPro CreateText(string name, Vector3 center, Vector3 normal, Vector3 up, float width, float height, float maxSize,
            TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = center + normal.normalized * 0.004f;
            go.transform.localRotation = Quaternion.LookRotation(-normal.normalized, up.normalized);
            var t = go.AddComponent<TextMeshPro>();
            if (Font != null)
                t.font = Font;
            t.rectTransform.sizeDelta = new Vector2(width, height);
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.1f;
            t.fontSizeMax = maxSize;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.color = ScreenText;
            t.richText = true;
            return t;
        }

        /// <summary>화면 글자 기본색 (밝은 청록 — 어두운 화면판 위).</summary>
        protected static readonly Color ScreenText = new Color(0.75f, 0.95f, 1f);
        /// <summary>꺼진 · 없는 항목 (색이 아니라 밝기로 구분 — 색약).</summary>
        protected static readonly Color ScreenDim = new Color(0.75f, 0.95f, 1f, 0.35f);

        protected static Vector3Int LocalToWorldDir(ModuleInstance m, Vector3Int local) => GridDirections.Rotate(local, m.Rotation);
        protected static Vector3Int WorldToLocalDir(ModuleInstance m, Vector3Int world) => GridDirections.Rotate(world, -m.Rotation);

        protected static void SetText(TextMeshPro t, string text, Color color)
        {
            if (t == null)
                return;
            if (t.text != text)
                t.SetText(text);
            if (t.color != color)
                t.color = color;
        }
    }
}
