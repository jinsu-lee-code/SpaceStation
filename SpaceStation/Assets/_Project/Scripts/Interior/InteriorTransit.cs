using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 위아래 이동 장치 (사용자 2026-11-19 "상하이동을 위치별 하나씩하기보다는 아예 묶어서 하나의 장치를 두고, 이동을 f키로 선택하면 이동할 곳을 패드상에서 선택"):
    /// 위나 아래로 이어진 모듈마다 하나 — 바라보거나 발판에 서서 F → 목적지 판 (<see cref="InteriorTransitPanel"/>) → 고른 모듈의 장치로
    /// (위 = 떠오르기 · 아래 = 가라앉기, 설정 <see cref="Settings.GameSettings.TransitRide"/>로 끔). 해치 · 빛 기둥 승강판을 대신함.
    /// 모양은 단계별 장치 (기본 화물 승강기 · 1 반중력 원판 · 2 혼천의 · 3 수정 꽃 · 4 성운) — 모델 전까지는 단계 승강판 판 · 고리 · 빛 기둥.
    /// </summary>
    public sealed class InteriorTransit : MonoBehaviour
    {
        /// <summary>발판 반지름 (이 안에 서면 장치 위).</summary>
        public const float PadRadius = 0.8f;
        /// <summary>장치 높이 (발판 윗면 ~ 꼭대기 고리).</summary>
        public const float Height = 2.9f;
        /// <summary>단계 장치 조각의 발판 윗면 (땅에서, BlenderWork/Connectors/connector_builder.py TR_PAD).</summary>
        public const float PadHeight = 0.14f;
        /// <summary>벽 장치의 서는 자리 (벽 면에서 방 쪽으로, BlenderWork/Connectors/connector_builder.py TRW_STAND).</summary>
        public const float WallStand = 0.5f;

        public ModuleInstance Module { get; private set; }
        /// <summary>발판 윗면 가운데 (월드) — 떠나고 도착하는 자리.</summary>
        public Vector3 PadTop { get; private set; }
        /// <summary>빛 기둥 (타는 동안 밝아짐) — 없을 수 있음.</summary>
        public InteriorSkyLift Shaft { get; private set; }
        /// <summary>바라보기 원기둥 높이 (발판 윗면에서) — 해치 방은 천장 해치까지.</summary>
        public float Top { get; private set; } = Height;

        /// <summary>위로 떠나고 위에서 내려앉는 자리 (해치 방 = 천장 해치 바로 밑 바닥 — 옛 설계에서 바닥 · 천장 해치 자리가 다른 방이 있음, 장치 = 발판).</summary>
        public Vector3 UpPad { get; private set; }

        /// <summary>장치 단계 (연출과 같은 값 — 해치 방 · 조각 없는 승강판 = 0) — 타는 소리를 고름 (2026-11-25 사운드, SOUND_LIST_NEW 4-1).</summary>
        public int Stage => TryGetComponent<InteriorTransitFx>(out var fx) ? fx.Stage : 0;

        /// <summary>해치 방의 뚜껑 (바닥 · 천장) — 타는 동안 숨김 (열린 것처럼).</summary>
        private Renderer[] _lids;

        /// <summary>떠나는 · 도착하는 자리: 위로 떠남 · 위에서 내려앉음 = <see cref="UpPad"/>, 아래로 가라앉음 · 밑에서 솟음 = <see cref="PadTop"/>.</summary>
        public Vector3 Pad(bool up) => up ? UpPad : PadTop;

        /// <summary>
        /// 장치가 보는 쪽: 앞 (로컬 −z, 안내 기둥 쪽)이 들어오는 자리 쪽 — 방 벽에 맞춰 90° 단위로 (구석에 놓인 화물 칸이 비스듬히 벽을 뚫지 않게).
        /// 자리 도구 (InteriorTransitSpots)와 방 짓기가 같이 씀.
        /// </summary>
        public static Quaternion Facing(Vector3 toSpawn, Quaternion fallback)
        {
            toSpawn.y = 0f;
            if (toSpawn.sqrMagnitude < 0.25f)
                return fallback;
            float yaw = Quaternion.LookRotation(-toSpawn).eulerAngles.y;
            return Quaternion.Euler(0f, Mathf.Round(yaw / 90f) * 90f, 0f);
        }

        public void Initialize(ModuleInstance module, Vector3 padTop, InteriorSkyLift shaft)
        {
            Module = module;
            PadTop = padTop;
            UpPad = padTop;
            Shaft = shaft;
        }

        /// <summary>
        /// 해치 방 (사용자 2026-11-20 "기존의 해치를 상호작용하면 지금의 층이동 시스템창이 뜨는 방식 — 방에 해치 하나를 천장과 바닥 두 곳에"):
        /// 발판 = 바닥 해치 뚜껑 윗면, 바라보기 = 바닥 ~ 천장 해치 원기둥, 타는 동안 뚜껑 둘을 숨김.
        /// </summary>
        public void InitializeHatch(ModuleInstance module, Vector3 floorLidTop, Vector3 underCeilingHatch, float top, Renderer[] lids)
        {
            Initialize(module, floorLidTop, null);
            UpPad = underCeilingHatch;
            Top = top;
            _lids = lids;
        }

        /// <summary>발이 발판 위 (수평 반지름 안 · 높이 ±0.6) — 해치 방은 천장 해치 밑도.</summary>
        public bool Contains(Vector3 feet) => Inside(feet, PadTop) || Inside(feet, UpPad);

        private static bool Inside(Vector3 feet, Vector3 pad)
        {
            var d = feet - pad;
            return Mathf.Abs(d.y) < 0.6f && new Vector2(d.x, d.z).sqrMagnitude < PadRadius * PadRadius;
        }

        /// <summary>
        /// 시선이 장치 몸통 (발판 ~ 꼭대기, 반지름 <see cref="PadRadius"/> 원기둥 — 빛 기둥은 충돌체가 없어 광선으로 못 잡음)에 닿는 거리.
        /// 안 닿거나 reach 밖 · 원기둥 안에서 봄이면 -1.
        /// </summary>
        public float LookDistance(Vector3 origin, Vector3 dir, float reach)
        {
            float a = Column(origin, dir, reach, PadTop);
            if (UpPad == PadTop)
                return a;
            float b = Column(origin, dir, reach, UpPad);   // 해치 방 = 천장 해치 밑 원기둥도
            return a < 0f ? b : b < 0f ? a : Mathf.Min(a, b);
        }

        private float Column(Vector3 origin, Vector3 dir, float reach, Vector3 pad)
        {
            var o = new Vector2(origin.x - pad.x, origin.z - pad.z);
            var d = new Vector2(dir.x, dir.z);
            float dd = d.sqrMagnitude;
            float c = o.sqrMagnitude - PadRadius * PadRadius;
            if (dd < 1e-6f || c <= 0f)
                return -1f;
            float b = Vector2.Dot(o, d);
            float disc = b * b - dd * c;
            if (disc < 0f)
                return -1f;
            float t = (-b - Mathf.Sqrt(disc)) / dd;
            if (t < 0f || t > reach)
                return -1f;
            float y = origin.y + dir.y * t - pad.y;
            return y > -0.2f && y < Top + 0.2f ? t : -1f;
        }

        public void SetRiding(bool riding)
        {
            if (Shaft != null)
                Shaft.SetRiding(riding);
            if (TryGetComponent<InteriorTransitFx>(out var fx))
                fx.SetRiding(riding);
            if (_lids != null)
            {
                foreach (var lid in _lids)
                {
                    if (lid != null)
                        lid.enabled = !riding;
                }
            }
        }
    }
}
