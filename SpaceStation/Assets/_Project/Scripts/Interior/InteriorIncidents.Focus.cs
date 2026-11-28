using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// CONNECT_1121 3번 ③ 위치 표시 (사용자 결정 2026-11-28): 지금 손댈 것 — 테두리 · 화면 끝 화살표가 가리킬 자리 (<see cref="InteriorFocusMarker"/>).
    /// 여러 개를 고치는 상황은 아직 안 고친 것 중 가장 가까운 것, 물건을 옮기는 상황은 들고 있으면 둘 곳.
    /// </summary>
    public sealed partial class InteriorIncidents
    {
        private static readonly List<Renderer> FocusRenderers = new List<Renderer>();

        /// <summary>진행 중인 상황에서 지금 손댈 것의 범위 (월드)와 그 방. 해결했거나 없으면 false.</summary>
        public bool TryGetFocus(out Bounds bounds, out ModuleInstance room)
        {
            bounds = default;
            room = null;
            if (_active == null || _active.Done || _eye == null)
                return false;
            room = _active.Room;
            return FocusBounds(_active, _eye.position, out bounds, ref room);
        }

        private static bool FocusBounds(Incident a, Vector3 eye, out Bounds bounds, ref ModuleInstance room)
        {
            bounds = default;
            switch (a.Kind)
            {
                case IncidentKind.Leak:
                    // CONNECT A5 옆방 차단 밸브: 둘 중 가까운 쪽 (그 방의 문을 가리킴)
                    if (a.Valve2 != null && a.Prop != null
                        && (a.Valve2.transform.position - eye).sqrMagnitude < (a.Prop.transform.position - eye).sqrMagnitude)
                    {
                        room = a.Valve2Room;
                        return BoundsOf(a.Valve2, out bounds);
                    }
                    break;
                case IncidentKind.Fire:
                {
                    // 소화기를 아직 안 들었으면 가장 가까운 소화기부터 (CONNECT A5 문 옆 소화기), 들었으면 남은 불길 가운데 가까운 것
                    GameObject best = null;
                    float bestD = float.MaxValue;
                    void Try(GameObject go)
                    {
                        if (go == null || !go.activeInHierarchy)
                            return;
                        float d = (go.transform.position - eye).sqrMagnitude;
                        if (d < bestD)
                        {
                            bestD = d;
                            best = go;
                        }
                    }
                    if (!a.Carrying)
                    {
                        foreach (var b in a.Bottles)
                            Try(b);
                    }
                    else
                    {
                        if (!a.MainOut)
                            Try(a.Prop);
                        if (a.Fire2 != null && !a.Fire2.Out)
                            Try(a.Fire2.Prop);
                    }
                    if (best != null)
                        return BoundsOf(best, out bounds);
                    break;
                }
                case IncidentKind.DecoLights:
                {
                    GameObject best = null;
                    float bestD = float.MaxValue;
                    foreach (var b in a.Boxes)
                    {
                        if (b.Fixed || b.Go == null)
                            continue;
                        float d = (b.Go.transform.position - eye).sqrMagnitude;
                        if (d < bestD)
                        {
                            bestD = d;
                            best = b.Go;
                        }
                    }
                    return best != null && BoundsOf(best, out bounds);
                }
                case IncidentKind.ScatteredToys:
                {
                    if (a.CarriedToy != null)
                        break;   // 들고 있으면 바구니 (= 소품)
                    GameObject best = null;
                    float bestD = float.MaxValue;
                    foreach (var t in a.Toys)
                    {
                        if (t.InBasket || t.Go == null || !t.Go.activeInHierarchy)
                            continue;
                        float d = (t.Go.transform.position - eye).sqrMagnitude;
                        if (d < bestD)
                        {
                            bestD = d;
                            best = t.Go;
                        }
                    }
                    if (best != null)
                        return BoundsOf(best, out bounds);
                    break;
                }
                case IncidentKind.Lanterns:
                case IncidentKind.StarLanterns:
                {
                    // 등불 한 줄 전체 (순서를 보고 따라 누름)
                    bool any = false;
                    foreach (var item in a.LampRow)
                    {
                        if (item.Go == null || !BoundsOf(item.Go, out var b))
                            continue;
                        if (any)
                            bounds.Encapsulate(b);
                        else
                            bounds = b;
                        any = true;
                    }
                    return any;
                }
                case IncidentKind.Vines:
                {
                    GameObject best = null;
                    float bestD = float.MaxValue;
                    foreach (var v in a.VineClumps)
                    {
                        if (v.Done || v.Go == null)
                            continue;
                        float d = (v.Go.transform.position - eye).sqrMagnitude;
                        if (d < bestD)
                        {
                            bestD = d;
                            best = v.Go;
                        }
                    }
                    return best != null && BoundsOf(best, out bounds);
                }
            }
            return a.Prop != null && BoundsOf(a.Prop, out bounds);
        }

        /// <summary>보이는 메시 렌더러만 (입자 · 선 · 빛 제외)의 범위, 없으면 자리 둘레 0.5m.</summary>
        internal static bool BoundsOf(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds(go.transform.position, Vector3.one * 0.5f);
            go.GetComponentsInChildren(false, FocusRenderers);
            bool any = false;
            foreach (var r in FocusRenderers)
            {
                if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer))
                    continue;
                if (any)
                    bounds.Encapsulate(r.bounds);
                else
                    bounds = r.bounds;
                any = true;
            }
            FocusRenderers.Clear();
            return true;
        }
    }
}
