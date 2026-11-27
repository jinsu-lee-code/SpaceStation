using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Core;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// CONNECT_1121 A6 단계별 상황 (사용자 결정 2026-11-27): 진화 단계 방의 상황 — 원형 표 = main CONNECT_1121 A6.
    /// 빈도 · 처리 시간 · 놓침 손실은 기본 사고와 같음 (<see cref="IncidentCatalog.LossOf"/>). 하나씩 만들어 <see cref="StageImplemented"/>에 더함.
    /// </summary>
    public sealed partial class InteriorIncidents
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>방 조명 · 조명 판을 어둡게 (방, 배율 — 1 = 그대로). InteriorMode가 InteriorAtmosphere에 이어 줌.</summary>
        public System.Action<ModuleInstance, float> DimRoom;

        /// <summary>확인용: 마지막 분전함 자리 찾기 결과 (놓은 수 · 거른 까닭별 수).</summary>
        public static string LastDecoNote { get; private set; }

        /// <summary>2단계 ② 조명 분전함 하나.</summary>
        private sealed class DecoBox
        {
            public GameObject Go;
            public Renderer Led;
            public Light Lamp;
            public Vector3 Ground;
            public float Rise;
            public bool Fixed;
            public float NextSpark;
        }

        /// <summary>2단계 ② 꺼 둔 장식 발광 재질 칸 하나 (원래 블록을 되돌리려고 적어 둠).</summary>
        private sealed class DecoSlot
        {
            public Renderer Renderer;
            public int Index;
            public Color Emission;
            public MaterialPropertyBlock Block;
            public bool WasEmpty;
            public bool HadEmission;
            public Color OldEmission;
        }

        private sealed partial class Incident
        {
            public readonly List<DecoBox> Boxes = new List<DecoBox>();
            public readonly List<DecoSlot> DecoSlots = new List<DecoSlot>();
            public DecoBox HoldingBox;
            public float DecoLevel;
            public float DecoShown = -1f;
        }

        /// <summary>놓는 코드가 있는 단계 상황 (U2 ~ U4에서 하나씩).</summary>
        private static bool StageImplemented(IncidentKind kind) => kind == IncidentKind.DecoLights;

        private bool PlaceStage(Incident a)
        {
            switch (a.Kind)
            {
                case IncidentKind.DecoLights: return PlaceDecoLights(a);
                default: return false;
            }
        }

        private void AnimateStage(Incident a, float now, bool holding)
        {
            switch (a.Kind)
            {
                case IncidentKind.DecoLights:
                    AnimateDecoLights(a, now, holding);
                    break;
            }
        }

        /// <summary>해결한 뒤 말풍선이 사라질 때까지 (2.5초): 밝기를 끝까지 올리고 분전함을 바닥으로 내림.</summary>
        private void AnimateStageDone(Incident a, float dt)
        {
            if (a.Kind != IncidentKind.DecoLights)
                return;
            a.DecoLevel = Mathf.MoveTowards(a.DecoLevel, 1f, dt / 0.8f);
            ApplyDecoLevel(a, a.DecoLevel);
            if (a.Age - a.DoneAge > 0.9f)
            {
                foreach (var b in a.Boxes)
                {
                    SetRise(b, b.Rise - dt / DecoRiseTime);
                    if (b.Lamp != null)
                        b.Lamp.intensity = DecoLampFixed * b.Rise; // 바닥 밑으로 빛이 새지 않게
                }
            }
        }

        /// <summary>정리 (놓침 · 다시 만듦 · 해결 모두): 꺼 둔 장식 · 방 조명을 원래대로. 해결했으면 내려간 분전함 · 말풍선 자리도 지움 (방에 쌓이지 않게).</summary>
        private void RestoreStage(Incident a)
        {
            if (a.Kind != IncidentKind.DecoLights)
                return;
            if (a.Done)
            {
                foreach (var b in a.Boxes)
                {
                    if (b.Go != null)
                        Destroy(b.Go);
                }
                if (a.Prop != null)
                    Destroy(a.Prop); // 손 동작 소리도 그 아래
            }
            foreach (var s in a.DecoSlots)
            {
                if (s.Renderer == null)
                    continue;
                if (s.WasEmpty)
                {
                    s.Block.Clear();
                }
                else
                {
                    s.Block.SetColor(EmissionColorId, s.HadEmission ? s.OldEmission : s.Emission);
                }
                s.Renderer.SetPropertyBlock(s.Block, s.Index);
            }
            a.DecoSlots.Clear();
            DimRoom?.Invoke(a.Room, 1f);
        }

        // ---------------- 2단계 ② 장식 조명 꺼짐 ----------------
        // 사용자 결정 (2026-11-27): 방의 장식 조명이 모두 꺼지고, 바닥에 조명 분전함 2 ~ 3개가 생김 — 하나 고칠 때마다 그만큼 다시 밝아짐.

        private const int DecoMin = 2;
        private const int DecoMax = 3;
        private const float DecoSpacing = 4f;
        private const float DecoSpacingMin = 2.5f;
        /// <summary>다 꺼졌을 때 장식 발광 · 방 조명 배율 (다 고치면 1).</summary>
        private const float DecoGlowDark = 0.06f;
        private const float DecoLightDark = 0.4f;
        /// <summary>분전함이 바닥에서 올라오고 · 다 고친 뒤 내려가는 시간 (초).</summary>
        private const float DecoRiseTime = 0.6f;
        private const float DecoSink = 1.2f;
        private static readonly Vector3 DecoBoxHalf = new Vector3(0.3f, 0.56f, 0.3f);
        /// <summary>등 댈 물건까지 (분전함 가운데에서, m) · 문 앞 빈 거리 · 문 앞을 재는 높이 (낮은 의자 · 허리 높이).</summary>
        private const float DecoBackReach = 0.75f;
        private const float DecoFrontClear = 1.5f;
        private static readonly float[] DecoFrontFloor = { 0.9f, 1.5f };
        private const float DecoOpenClear = 2f;
        private const float DecoSideGap = 1.2f;
        private const float DecoDoorMargin = 1.6f;
        private static readonly float[] DecoFrontHeights = { 0.3f, 1.0f };
        /// <summary>꼭대기 램프 빛 (분전함 기준 자리 · 닿는 거리 · 세기: 깜빡 켜짐 / 꺼짐 / 고친 뒤).</summary>
        private static readonly Vector3 DecoLampOffset = new Vector3(0f, 1.12f, 0.22f);
        private const float DecoLampRange = 3.5f;
        private const float DecoLampOn = 1.4f;
        private const float DecoLampOff = 0.12f;
        private const float DecoLampFixed = 0.7f;

        /// <summary>장식 조명으로 보는 재질 (모듈 포인트 빛 · 전구 · 얼음빛 장식). 조명 판(M_InteriorLight)은 InteriorAtmosphere가 방 조명과 함께.</summary>
        private static bool IsDecoGlow(Material m)
        {
            if (m == null || !m.HasProperty(EmissionColorId) || !m.IsKeywordEnabled("_EMISSION") || m.GetColor(EmissionColorId).maxColorComponent < 0.05f)
                return false;
            string n = m.name;
            return n.StartsWith("M_Accent_Evo", System.StringComparison.Ordinal)
                || n.StartsWith("M_InteriorEvo2Lemon", System.StringComparison.Ordinal)
                || n.StartsWith("M_InteriorEvo2Ice", System.StringComparison.Ordinal);
        }

        private static bool UnderSky(Transform t, Transform room)
        {
            for (var p = t; p != null && p != room; p = p.parent)
            {
                if (p.name.EndsWith("_Sky", System.StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>바닥에 분전함 2 ~ 3개 (손이 닿고 눈에 보이는 자리, 서로 떨어지게) + 방 장식 발광 칸을 모음. 분전함이 둘도 안 서면 이 방엔 못 냄.</summary>
        private bool PlaceDecoLights(Incident a)
        {
            var parent = _builder.RoomParent(a.Room);
            if (_s.DecoBox == null || parent == null)
                return false;
            int want = DecoMin + _rng.Next(DecoMax - DecoMin + 1);
            var spots = new List<Vector3>();
            var faces = new List<Vector3>();
            int noFloor = 0, nearDoor = 0, badSpot = 0;
            for (int tries = 0; tries < 20 && spots.Count < want; tries++)
            {
                float spacing = tries < 12 ? DecoSpacing : DecoSpacingMin;
                if (!InteriorSpots.Floor(_builder, a.Room, _rng, DecoBoxHalf, spots, spacing, out var spot, 0.6f, 3.5f, sightHeight: 1f))
                {
                    noFloor++;
                    continue;
                }
                if (InteriorSpots.NearDoor(_builder, a.Room, spot + Vector3.up, DecoDoorMargin))
                {
                    nearDoor++;
                    continue;
                }
                if (!DecoSpotFace(spot, out var face))
                {
                    badSpot++;   // 좁은 길 한가운데 · 문 앞에 설 바닥 없음 (2026-11-27 플레이: 선반 사이 길을 막음 · 문이 방 가장자리를 봄)
                    continue;
                }
                spots.Add(spot);
                faces.Add(face);
            }
            LastDecoNote = $"자리 {spots.Count}/{want} · 바닥 없음 {noFloor} · 문길 {nearDoor} · 길 막음/앞 없음 {badSpot}";
            if (spots.Count < DecoMin)
                return false;

            // 꺼 둘 장식 칸 (분전함을 놓기 전에 — 하늘 껍질은 뺌)
            foreach (var r in parent.GetComponentsInChildren<Renderer>())
            {
                if (UnderSky(r.transform, parent))
                    continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!IsDecoGlow(mats[i]))
                        continue;
                    var block = new MaterialPropertyBlock();
                    r.GetPropertyBlock(block, i);
                    a.DecoSlots.Add(new DecoSlot
                    {
                        Renderer = r,
                        Index = i,
                        Emission = mats[i].GetColor(EmissionColorId),
                        Block = block,
                        WasEmpty = block.isEmpty,
                        HadEmission = block.HasColor(EmissionColorId),
                        OldEmission = block.HasColor(EmissionColorId) ? block.GetColor(EmissionColorId) : Color.black,
                    });
                }
            }

            // 분전함: 등 뒤 물건에서 돌아서 (문 · 손잡이가 빈 쪽으로)
            int total = spots.Count;
            for (int i = 0; i < total; i++)
            {
                var spot = spots[i];
                var go = Instantiate(_s.DecoBox, parent);
                go.name = "IncidentDecoBox" + i;
                go.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(faces[i], Vector3.up));
                var body = go.transform.Find("IncDecoBox");
                var mf = body != null ? body.GetComponent<MeshFilter>() : null;
                var col = go.AddComponent<BoxCollider>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var mb = mf.sharedMesh.bounds;
                    col.center = go.transform.InverseTransformPoint(body.TransformPoint(mb.center));
                    col.size = mb.size;
                }
                var ledT = go.transform.Find("DecoLed");
                // 꼭대기 램프 빛: 장식이 꺼진 어두운 방에서도 분전함을 찾게 (고장 = 빨강 깜빡, 고침 = 초록 켜짐 — 밝기 · 깜빡임으로도 구분)
                var lampGo = new GameObject("DecoLamp");
                lampGo.transform.SetParent(go.transform, false);
                lampGo.transform.localPosition = DecoLampOffset;
                var lamp = lampGo.AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.range = DecoLampRange;
                lamp.color = LedRed;
                lamp.intensity = 0f;
                lamp.shadows = LightShadows.None;
                var box = new DecoBox
                {
                    Go = go,
                    Led = ledT != null ? ledT.GetComponent<Renderer>() : null,
                    Lamp = lamp,
                    Ground = spot,
                    NextSpark = Time.unscaledTime + DecoRiseTime + (float)_rng.NextDouble() * 1.2f,
                };
                SetRise(box, 0f); // 바닥 속에서 시작 → 올라옴
                var use = go.AddComponent<InteriorInteractable>();
                use.HoldSeconds = _s.DecoHold;
                use.Prompt = () => $"조명 분전함 고치기 ({DecoFixedCount(a) + 1}/{total})  <color=#FF8A5C>{InteriorIncidentRules.Title(a.Kind)}</color>";
                use.Holding = p =>
                {
                    a.Holding = true;
                    a.HoldProgress = p;
                    a.HoldingBox = box;
                };
                use.Completed = () => FixDecoBox(a, box);
                a.Boxes.Add(box);
                a.Extra.Add(go);
            }

            // 말풍선 · 손 동작 소리 자리 (다음에 고칠 분전함 위로 옮겨 다님 — 분전함은 이 아래에 두지 않음)
            a.Prop = new GameObject("IncidentDecoLights");
            a.Prop.transform.SetParent(parent, false);
            MoveDecoMarker(a);
            a.Held = AudioService.AttachHeld(l => l.VentFix, a.Prop.transform);
            a.DecoLevel = 0f;
            ApplyDecoLevel(a, 0f);
            AudioService.TryPlayAt(l => l.StormSpark, a.Boxes[0].Go.transform.position + Vector3.up * 0.8f); // 꺼지는 순간 "지직"
            return true;
        }

        /// <summary>
        /// 분전함 자리 (통로를 막지 않게): ① 0.75m 안에 등을 댈 물건(벽 · 난간 · 선반)이 있고 문 쪽은 비어 있음, 또는 ② 사방 2m 안에 아무것도 없는 넓은 바닥.
        /// 어느 쪽이든 문 앞에 플레이어가 설 바닥이 있어야 함 (<see cref="FrontOpen"/> — 방 가장자리 · 물길 · 유리 너머를 보면 안 됨).
        /// <paramref name="face"/> = 문이 볼 쪽.
        /// </summary>
        private bool DecoSpotFace(Vector3 spot, out Vector3 face)
        {
            face = Vector3.zero;
            var o = spot + Vector3.up * 0.5f;
            float best = float.MaxValue;
            var backed = Vector3.zero;
            for (int k = 0; k < 16; k++)
            {
                var dir = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
                if (!Physics.Raycast(o, dir, out var hit, DecoBackReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || NotFurniture(hit.collider))
                    continue;
                if (hit.distance >= best)
                    continue;
                best = hit.distance;
                // 문 = 가장 가까운 물건의 반대쪽 (평평한 면이면 그 면에 바르게 — 기둥 · 난간 기둥의 둥근 면은 방향이 엉뚱해 광선 방향)
                var n = new Vector3(hit.normal.x, 0f, hit.normal.z);
                backed = n.sqrMagnitude > 0.5f && Vector3.Angle(n, -dir) < 35f ? n.normalized : -dir;
            }
            if (best <= DecoBackReach && backed.sqrMagnitude > 0.5f)
            {
                if (!FrontOpen(spot, backed) || !SideGap(spot, backed))
                    return false;
                face = backed;
                return true;
            }
            // 등 댈 것이 없으면 넓은 바닥만 (좁은 길 한가운데 금지)
            for (int k = 0; k < 8; k++)
            {
                var dir = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                if (Physics.Raycast(o, dir, DecoOpenClear, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return false;
            }
            int start = _rng.Next(4);
            for (int k = 0; k < 4; k++)
            {
                var f = Quaternion.Euler(0f, (start + k) * 90f, 0f) * Vector3.forward;
                if (FrontOpen(spot, f))
                {
                    face = f;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 옆으로 지나갈 틈: 분전함 왼쪽 · 오른쪽 가운데 한쪽은 1.2m(분전함 반 폭 + 0.95m) 넘게 빔 — 난간 사이 좁은 다리 · 통로를 막지 않게
        /// (2026-11-27 플레이: 요새 성벽 위 다리 한가운데에 섰음).
        /// </summary>
        private static bool SideGap(Vector3 spot, Vector3 face)
        {
            var o = spot + Vector3.up * 0.5f;
            var side = Vector3.Cross(Vector3.up, face).normalized;
            float left = Physics.Raycast(o, -side, out var l, DecoSideGap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? l.distance : DecoSideGap;
            float right = Physics.Raycast(o, side, out var r, DecoSideGap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? r.distance : DecoSideGap;
            return Mathf.Max(left, right) >= DecoSideGap;
        }

        /// <summary>문 앞 1.5m: 막는 것 없음 (유리 · 난간 포함) + 0.9 · 1.5m 앞에 같은 높이의 평평한 바닥 (플레이어가 서서 F를 누를 자리).</summary>
        private static bool FrontOpen(Vector3 spot, Vector3 face)
        {
            foreach (float h in DecoFrontHeights)
            {
                if (Physics.Raycast(spot + Vector3.up * h, face, DecoFrontClear, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return false;
            }
            foreach (float d in DecoFrontFloor)
            {
                var p = spot + face * d;
                if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var down, 1.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(down.point.y - spot.y) > 0.08f || down.normal.y < 0.95f)
                    return false;
            }
            return true;
        }

        /// <summary>등 댈 물건이 아닌 것 (플레이어 몸 · 주민 · 문 · 유리 · 이동 장치 등 — 벽 자리와 같은 기준).</summary>
        private static bool NotFurniture(Collider c) => InteriorSpots.IgnoredForSpots(c);

        private static int DecoFixedCount(Incident a)
        {
            int n = 0;
            foreach (var b in a.Boxes)
            {
                if (b.Fixed)
                    n++;
            }
            return n;
        }

        /// <summary>말풍선 자리 = 아직 안 고친 첫 분전함 위.</summary>
        private static void MoveDecoMarker(Incident a)
        {
            if (a.Prop == null)
                return;
            foreach (var b in a.Boxes)
            {
                if (b.Fixed || b.Go == null)
                    continue;
                a.Prop.transform.SetPositionAndRotation(b.Ground + Vector3.up * 1.3f, b.Go.transform.rotation);
                return;
            }
        }

        private void FixDecoBox(Incident a, DecoBox box)
        {
            if (a.Done || box.Fixed)
                return;
            box.Fixed = true;
            if (box.Led != null)
                TintRenderer(box.Led, "M_IncLed", LedGreen);
            if (box.Lamp != null)
            {
                box.Lamp.color = LedGreen;
                box.Lamp.intensity = DecoLampFixed;
            }
            var use = box.Go != null ? box.Go.GetComponent<InteriorInteractable>() : null;
            if (use != null)
                Destroy(use);
            if (DecoFixedCount(a) >= a.Boxes.Count)
            {
                Resolve(a);
                return;
            }
            MoveDecoMarker(a);
        }

        /// <summary>
        /// 고친 만큼 밝기를 천천히 올림 · 가끔 짧게 번쩍 (불안한 전기) · 고치지 않은 분전함은 LED 빨강 깜빡 + 1 ~ 2.5초마다 불꽃.
        /// 손 동작 소리는 누르는 분전함 자리에서.
        /// </summary>
        private void AnimateDecoLights(Incident a, float now, bool holding)
        {
            if (holding && a.HoldingBox != null && a.HoldingBox.Go != null && a.Held != null)
                a.Held.transform.position = a.HoldingBox.Go.transform.position + Vector3.up * 0.6f;
            float target = a.Boxes.Count > 0 ? (float)DecoFixedCount(a) / a.Boxes.Count : 1f;
            a.DecoLevel = Mathf.MoveTowards(a.DecoLevel, target, Time.unscaledDeltaTime / 0.8f);
            float level = a.DecoLevel;
            if (Mathf.PerlinNoise(now * 5f, 0.7f) > 0.78f)
                level = Mathf.Lerp(level, 1f, 0.55f); // 잠깐 들어왔다 나감
            ApplyDecoLevel(a, level);
            bool blink = Mathf.Repeat(now, 0.7f) < 0.35f;
            foreach (var b in a.Boxes)
            {
                if (b.Rise < 1f)
                    SetRise(b, b.Rise + Time.unscaledDeltaTime / DecoRiseTime);
                if (b.Fixed || b.Go == null)
                    continue;
                if (b.Led != null)
                    TintRenderer(b.Led, "M_IncLed", blink ? LedRed : new Color(0.25f, 0.05f, 0.04f));
                if (b.Lamp != null)
                    b.Lamp.intensity = b.Rise < 1f ? 0f : blink ? DecoLampOn : DecoLampOff;
                if (now >= b.NextSpark)
                {
                    b.NextSpark = now + 1f + (float)_rng.NextDouble() * 1.5f;
                    var t = b.Go.transform;
                    InteriorFx.Spark(transform, t.position + Vector3.up * 0.62f + t.forward * 0.17f, (t.forward + Vector3.up * 0.4f).normalized, _spark, 4, 9, 1.5f);
                }
            }
        }

        /// <summary>분전함 높이 (0 = 바닥 속, 1 = 바닥 위) — 부드럽게 멈춤.</summary>
        private static void SetRise(DecoBox b, float rise)
        {
            b.Rise = Mathf.Clamp01(rise);
            if (b.Go != null)
                b.Go.transform.position = b.Ground + Vector3.down * (DecoSink * (1f - Mathf.SmoothStep(0f, 1f, b.Rise)));
        }

        /// <summary>장식 발광 · 방 조명을 밝기 단계(0 = 다 꺼짐, 1 = 원래)로. 같은 값이면 건너뜀.</summary>
        private void ApplyDecoLevel(Incident a, float level)
        {
            if (Mathf.Abs(level - a.DecoShown) < 0.004f)
                return;
            a.DecoShown = level;
            float glow = Mathf.Lerp(DecoGlowDark, 1f, level);
            foreach (var s in a.DecoSlots)
            {
                if (s.Renderer == null)
                    continue;
                s.Block.SetColor(EmissionColorId, (s.HadEmission ? s.OldEmission : s.Emission) * glow);
                s.Renderer.SetPropertyBlock(s.Block, s.Index);
            }
            DimRoom?.Invoke(a.Room, Mathf.Lerp(DecoLightDark, 1f, level));
        }
    }
}
