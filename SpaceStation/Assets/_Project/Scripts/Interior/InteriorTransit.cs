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

        public ModuleInstance Module { get; private set; }
        /// <summary>발판 윗면 가운데 (월드) — 떠나고 도착하는 자리.</summary>
        public Vector3 PadTop { get; private set; }
        /// <summary>빛 기둥 (타는 동안 밝아짐) — 없을 수 있음.</summary>
        public InteriorSkyLift Shaft { get; private set; }

        public void Initialize(ModuleInstance module, Vector3 padTop, InteriorSkyLift shaft)
        {
            Module = module;
            PadTop = padTop;
            Shaft = shaft;
        }

        /// <summary>발이 발판 위 (수평 반지름 안 · 높이 ±0.6).</summary>
        public bool Contains(Vector3 feet)
        {
            var d = feet - PadTop;
            return Mathf.Abs(d.y) < 0.6f && new Vector2(d.x, d.z).sqrMagnitude < PadRadius * PadRadius;
        }

        /// <summary>
        /// 시선이 장치 몸통 (발판 ~ 꼭대기, 반지름 <see cref="PadRadius"/> 원기둥 — 빛 기둥은 충돌체가 없어 광선으로 못 잡음)에 닿는 거리.
        /// 안 닿거나 reach 밖 · 원기둥 안에서 봄이면 -1.
        /// </summary>
        public float LookDistance(Vector3 origin, Vector3 dir, float reach)
        {
            var o = new Vector2(origin.x - PadTop.x, origin.z - PadTop.z);
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
            float y = origin.y + dir.y * t - PadTop.y;
            return y > -0.2f && y < Height + 0.2f ? t : -1f;
        }

        public void SetRiding(bool riding)
        {
            if (Shaft != null)
                Shaft.SetRiding(riding);
        }
    }
}
