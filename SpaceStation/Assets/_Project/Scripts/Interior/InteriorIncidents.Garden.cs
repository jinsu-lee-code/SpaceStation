using System;
using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// CONNECT_1121 A6 3 · 4단계 상황 (사용자 결정 2026-11-27 — 셋 다 추천안):
    /// - 3단계 ① 물길 막힘: 물가에 물꼬 판 (바퀴 셋 · 눈금 다섯) — F 한 번 = 한 단, 깜빡이는 눈금에 셋 다 맞추면 해결. 상황 동안 폭포 · 물보라 · 분수가 멈춤.
    /// - 3단계 ② 등불 순서 · 4단계 ② 별빛 등불: 바닥 등불 넷이 한 줄로 올라와 순서대로 깜빡임 → 같은 순서로 F (틀리면 다시 보여 줌).
    /// - 3단계 ③ 덩굴 웃자람: 길 2 ~ 3곳 덩굴 덤불 — F 길게로 쳐냄.
    /// - 4단계 ① 신호 흐트러짐: 다이얼 조작반 (빈도 · 세기 각 다섯 칸) — 화면 목표 파형(굵고 옅게) · 지금 파형(가늘고 환하게), 둘 다 맞추면 해결.
    /// 목표 칸은 깜빡임 · 맞으면 초록으로 멈춤 (색만이 아니라 깜빡임 · 글로도 — 색약). 모델 = BlenderWork/InteriorProps/stage_incident_builder.py.
    /// </summary>
    public sealed partial class InteriorIncidents
    {
        /// <summary>바닥 소품 하나 (등불 · 덩굴).</summary>
        private sealed class FloorItem
        {
            public GameObject Go;
            public Vector3 Ground;
            public float Rise;
            public Renderer Glass;
            public Light Light;
            public bool Done;
            public float Seed;
        }

        private sealed partial class Incident
        {
            // 칸 맞추기 (물꼬 바퀴 · 신호 다이얼)
            public int[] Levels, Targets;
            public readonly Transform[] Knobs = new Transform[InteriorIncidentRules.SluiceWheels];
            public readonly Quaternion[] KnobRest = new Quaternion[InteriorIncidentRules.SluiceWheels];
            public readonly float[] KnobAngle = new float[InteriorIncidentRules.SluiceWheels];
            public readonly float[] KnobTarget = new float[InteriorIncidentRules.SluiceWheels];
            public readonly Transform[] Gates = new Transform[InteriorIncidentRules.SluiceWheels];
            public readonly Vector3[] GateRest = new Vector3[InteriorIncidentRules.SluiceWheels];
            public Renderer[,] Notches;
            // 물길 막힘: 멈춘 물
            public readonly List<Renderer> HiddenFalls = new List<Renderer>();
            public readonly List<int> QuietLandings = new List<int>();
            public InteriorWaterfallFx Falls;
            public InteriorFountainFx Fountain;
            // 신호 화면
            public LineRenderer WaveTarget, WaveNow;
            // 등불 순서
            public readonly List<FloorItem> LampRow = new List<FloorItem>();
            public int[] Order;
            public int OrderEntered;
            public float ShowStart = -1f;
            public float FlashUntil;
            public int FlashLamp = -1;
            public float WrongUntil;
            // 덩굴
            public readonly List<FloorItem> VineClumps = new List<FloorItem>();
            public FloorItem HoldingVine;
        }

        /// <summary>확인용: 마지막 3 · 4단계 상황 놓기 결과.</summary>
        public static string LastGardenNote { get; private set; }

        // 물꼬 판 · 다이얼 조작반 (모델 좌표 — stage_incident_builder.py와 맞춤)
        private static readonly Vector3 SluiceHalf = new Vector3(0.62f, 0.68f, 0.62f);
        private const float SluiceHalfWidth = 0.61f;
        private const float SluiceStep = 0.11f;
        private const float WheelTurnDegrees = 72f;
        private const float DialStepDegrees = 60f;
        private static readonly Color NotchOff = new Color(0.16f, 0.16f, 0.18f);
        private const float WaveHalfWidth = 0.3f;
        private const int WavePoints = 48;

        // 등불
        private static readonly Vector3 LampHalf = new Vector3(0.22f, 0.55f, 0.22f);
        private const float LampGap = 1.2f;
        private const float LampClusterRadius = 4.5f;
        private const float LampSink = 1.2f;
        private const float LampRiseTime = 0.7f;
        private const float LampStep = 0.85f;
        private const float LampOn = 0.55f;
        private static readonly Color LampLitGarden = new Color(1f, 0.78f, 0.42f);
        private static readonly Color LampLitStar = new Color(0.86f, 0.82f, 1f);
        private static readonly Color LampDark = new Color(0.22f, 0.19f, 0.16f);

        // 덩굴
        private static readonly Vector3 VineHalf = new Vector3(0.45f, 0.3f, 0.45f);
        private const int VineMin = 2;
        private const int VineMax = 3;

        // ---------------- 공통: 바닥 소품 자리 ----------------

        /// <summary>바닥 소품 자리 (바닥 · 문길 밖 · 등 댈 것 또는 넓은 바닥 · 문 앞 바닥 · 옆 틈) — score가 있으면 가장 작은 곳, 없으면 처음 찾은 곳.</summary>
        private bool PropSpot(Incident a, Vector3 half, float halfWidth, float sight, Func<Vector3, float> score, out Vector3 spot, out Vector3 face)
        {
            spot = default;
            face = default;
            bool found = false;
            float best = float.MaxValue;
            int candidates = 0;
            for (int tries = 0; tries < ConsoleTries && candidates < ConsoleCandidates; tries++)
            {
                if (!InteriorSpots.Floor(_builder, a.Room, _rng, half, null, 0f, out var s, 0.6f, 3.5f, sightHeight: sight))
                    continue;
                if (InteriorSpots.NearDoor(_builder, a.Room, s + Vector3.up, DecoDoorMargin) || !PropFace(s, halfWidth, out var f))
                    continue;
                candidates++;
                float d = score != null ? score(s) : 0f;
                if (!found || d < best)
                {
                    found = true;
                    best = d;
                    spot = s;
                    face = f;
                }
                if (score == null)
                    break;
            }
            return found;
        }

        private GameObject RaiseProp(Incident a, GameObject prefab, Transform parent, string name, Vector3 spot, Vector3 face, float bubbleHeight)
        {
            var go = Instantiate(prefab, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(face, Vector3.up));
            a.Prop = go;
            a.ConsoleGround = spot;
            SetConsoleRise(a, 0f);
            var bubbleAt = new GameObject("BubbleAt").transform;
            bubbleAt.SetParent(go.transform, false);
            bubbleAt.localPosition = new Vector3(0f, bubbleHeight, 0f);
            a.BubbleAt = bubbleAt;
            return go;
        }

        private static void AddBox(Transform t, Vector3 center, Vector3 size)
        {
            if (t == null)
                return;
            var col = t.gameObject.AddComponent<BoxCollider>();
            col.center = center;
            col.size = size;
        }

        // ---------------- 3단계 ① 물길 막힘 ----------------

        private bool PlaceWaterChannel(Incident a)
        {
            var parent = _builder.RoomParent(a.Room);
            if (_s.Sluice == null || parent == null)
                return false;
            var waters = new List<Bounds>();
            foreach (var r in parent.GetComponentsInChildren<Renderer>())
            {
                if (r.name.IndexOf("_Water", StringComparison.Ordinal) >= 0 && !UnderSky(r.transform, parent) && r.bounds.size.sqrMagnitude > 0.01f)
                    waters.Add(r.bounds);
            }
            if (waters.Count == 0)
            {
                LastGardenNote = "물길 막힘: 물 없음";
                return false;
            }
            float Shore(Vector3 p)
            {
                float best = float.MaxValue;
                foreach (var b in waters)
                {
                    var c = b.ClosestPoint(new Vector3(p.x, b.center.y, p.z));
                    best = Mathf.Min(best, new Vector2(c.x - p.x, c.z - p.z).sqrMagnitude);
                }
                return best;
            }
            if (!PropSpot(a, SluiceHalf, SluiceHalfWidth, 1.1f, Shore, out var spot, out var face))
            {
                LastGardenNote = "물길 막힘: 물꼬 판 자리 없음";
                return false;
            }
            LastGardenNote = $"물길 막힘: 물가까지 {Mathf.Sqrt(Shore(spot)):0.0}m";
            var go = RaiseProp(a, _s.Sluice, parent, "IncidentSluice", spot, face, 1.5f);
            var body = go.transform.Find("IncSluice");
            AddBox(body, new Vector3(0f, 0.09f, 0f), new Vector3(1.22f, 0.18f, 0.42f));   // 돌 받침
            AddBox(body, new Vector3(0f, 0.72f, 0f), new Vector3(1.1f, 1.1f, 0.12f));     // 나무 판 (바퀴는 판 앞 — 덮지 않게)
            a.Targets = InteriorIncidentRules.NotchTargets(_rng, InteriorIncidentRules.SluiceWheels, out var start);
            a.Levels = start;
            a.Notches = new Renderer[InteriorIncidentRules.SluiceWheels, InteriorIncidentRules.Notches];
            for (int i = 0; i < InteriorIncidentRules.SluiceWheels; i++)
            {
                var gate = go.transform.Find("Gate" + i);
                a.Gates[i] = gate;
                if (gate != null)
                    a.GateRest[i] = gate.localPosition;
                for (int n = 0; n < InteriorIncidentRules.Notches; n++)
                {
                    var notch = go.transform.Find($"Notch{i}{n}");
                    a.Notches[i, n] = notch != null ? notch.GetComponent<Renderer>() : null;
                }
                SetupKnob(a, go.transform.Find("Wheel" + i), i, new Vector3(0.3f, 0.3f, 0.1f),
                    idx => $"바퀴 돌리기 · 물꼬 {idx + 1}  {NotchState(a, idx)}");
            }
            ApplyGates(a, 1f);
            StopWater(a, parent);
            return true;
        }

        /// <summary>물이 멈춤: 폭포 메시 · 물보라 · 분수 (바이오스피어는 물꼬 체험이 물을 다루므로 그대로).</summary>
        private static void StopWater(Incident a, Transform parent)
        {
            if (parent.GetComponentInChildren<InteriorSluiceGates>() != null)
                return;
            foreach (var r in parent.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled)
                    continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null && m.name.StartsWith("M_InteriorEvo3Falls", StringComparison.Ordinal))
                    {
                        r.enabled = false;
                        a.HiddenFalls.Add(r);
                        break;
                    }
                }
            }
            a.Falls = parent.GetComponentInChildren<InteriorWaterfallFx>();
            if (a.Falls != null)
            {
                for (int i = 0; i < a.Falls.LandingCount; i++)
                {
                    if (!a.Falls.LandingOn(i))
                        continue;
                    a.Falls.SetLanding(i, false);
                    a.QuietLandings.Add(i);
                }
            }
            a.Fountain = parent.GetComponentInChildren<InteriorFountainFx>();
            if (a.Fountain != null)
                a.Fountain.SetFlowing(false);
        }

        private static void RestoreWater(Incident a)
        {
            foreach (var r in a.HiddenFalls)
            {
                if (r != null)
                    r.enabled = true;
            }
            a.HiddenFalls.Clear();
            if (a.Falls != null)
            {
                foreach (int i in a.QuietLandings)
                    a.Falls.SetLanding(i, true);
            }
            a.QuietLandings.Clear();
            a.Falls = null;
            if (a.Fountain != null)
                a.Fountain.SetFlowing(true);
            a.Fountain = null;
        }

        private static void ApplyGates(Incident a, float t)
        {
            for (int i = 0; i < InteriorIncidentRules.SluiceWheels; i++)
            {
                var gate = a.Gates[i];
                if (gate == null || a.Levels == null)
                    continue;
                var want = a.GateRest[i] + Vector3.down * (SluiceStep * (a.Levels[i] - 1));
                gate.localPosition = Vector3.MoveTowards(gate.localPosition, want, t);
            }
        }

        // ---------------- 칸 맞추기 공통 (물꼬 바퀴 · 신호 다이얼) ----------------

        private void SetupKnob(Incident a, Transform knob, int index, Vector3 colliderSize, Func<int, string> prompt)
        {
            a.Knobs[index] = knob;
            if (knob == null)
                return;
            a.KnobRest[index] = knob.localRotation;
            float start = a.Kind == IncidentKind.SignalDrift ? (a.Levels[index] - 1) * DialStepDegrees : 0f;
            a.KnobAngle[index] = start;
            a.KnobTarget[index] = start;
            knob.localRotation = a.KnobRest[index] * Quaternion.AngleAxis(start, Vector3.forward);
            AddBox(knob, Vector3.zero, colliderSize);
            var use = knob.gameObject.AddComponent<InteriorInteractable>();
            use.HoldSeconds = _s.SwitchHold;
            use.Prompt = () => prompt(index);
            use.Completed = () => TurnKnob(a, index);
        }

        private static string NotchState(Incident a, int i) =>
            a.Levels != null && a.Levels[i] == a.Targets[i] ? "<color=#8FD19E>맞음</color>" : "<color=#FF8A5C>깜빡이는 칸까지</color>";

        private void TurnKnob(Incident a, int i)
        {
            if (a.Done || a.Levels == null)
                return;
            a.Levels[i] = InteriorIncidentRules.TurnNotch(a.Levels[i]);
            a.KnobTarget[i] = a.Kind == IncidentKind.SignalDrift ? (a.Levels[i] - 1) * DialStepDegrees : a.KnobTarget[i] + WheelTurnDegrees;
            if (a.Knobs[i] != null)
                AudioService.TryPlayAt(l => l.BreakerSwitch, a.Knobs[i].position);
            if (InteriorIncidentRules.NotchesSolved(a.Levels, a.Targets))
                Resolve(a);
        }

        /// <summary>칸 맞추기 움직임: 조작반이 올라옴 · 바퀴 · 다이얼이 돎 · 수문판이 내려감 · 목표 칸 깜빡 (맞으면 초록).</summary>
        private void AnimateNotches(Incident a, float now, bool done)
        {
            float dt = Time.unscaledDeltaTime;
            if (!done && a.ConsoleRise < 1f)
                SetConsoleRise(a, a.ConsoleRise + dt / ConsoleRiseTime);
            for (int i = 0; i < a.Knobs.Length; i++)
            {
                if (a.Knobs[i] == null)
                    continue;
                a.KnobAngle[i] = Mathf.MoveTowards(a.KnobAngle[i], a.KnobTarget[i], dt * 360f);
                a.Knobs[i].localRotation = a.KnobRest[i] * Quaternion.AngleAxis(a.KnobAngle[i], Vector3.forward);
            }
            ApplyGates(a, dt * 0.6f);
            if (a.Notches == null)
                return;
            bool blink = Mathf.Repeat(now, 0.7f) < 0.4f;
            int count = a.Notches.GetLength(0);
            for (int i = 0; i < count; i++)
            {
                for (int n = 0; n < InteriorIncidentRules.Notches; n++)
                {
                    var r = a.Notches[i, n];
                    if (r == null)
                        continue;
                    Color c = NotchOff;
                    if (n == a.Targets[i] - 1)
                        c = a.Levels[i] == a.Targets[i] || done ? LedGreen : blink ? LedRed : LedDim;
                    TintRenderer(r, "M_IncLed", c);
                }
            }
            if (a.WaveNow != null)
                DrawWaves(a, now, done);
        }

        // ---------------- 4단계 ① 신호 흐트러짐 ----------------

        private bool PlaceSignal(Incident a)
        {
            var parent = _builder.RoomParent(a.Room);
            if (_s.SignalConsole == null || parent == null)
                return false;
            if (!PropSpot(a, ConsoleHalf, 0.45f, 1.1f, null, out var spot, out var face))
            {
                LastGardenNote = "신호 흐트러짐: 조작반 자리 없음";
                return false;
            }
            LastGardenNote = "신호 흐트러짐: 조작반 놓음";
            var go = RaiseProp(a, _s.SignalConsole, parent, "IncidentSignalConsole", spot, face, 1.5f);
            var body = go.transform.Find("IncSignalConsole");
            AddBox(body, new Vector3(0f, 0.45f, 0f), new Vector3(0.8f, 0.9f, 0.46f));
            AddBox(body, new Vector3(0f, 1.14f, -0.02f), new Vector3(0.8f, 0.5f, 0.12f));
            a.Targets = InteriorIncidentRules.NotchTargets(_rng, InteriorIncidentRules.SignalDials, out var start);
            a.Levels = start;
            a.Notches = new Renderer[InteriorIncidentRules.SignalDials, InteriorIncidentRules.Notches];
            for (int d = 0; d < InteriorIncidentRules.SignalDials; d++)
            {
                for (int n = 0; n < InteriorIncidentRules.Notches; n++)
                {
                    var tick = go.transform.Find($"Tick{d}{n}");
                    a.Notches[d, n] = tick != null ? tick.GetComponent<Renderer>() : null;
                }
                SetupKnob(a, go.transform.Find("Dial" + d), d, new Vector3(0.16f, 0.16f, 0.09f),
                    idx => $"{(idx == 0 ? "빈도" : "세기")} 다이얼 돌리기  {NotchState(a, idx)}");
            }
            var screen = go.transform.Find("Screen");
            if (screen != null && _spark != null)
            {
                a.WaveTarget = MakeWave(screen, "WaveTarget", 0.014f, new Color(0.55f, 0.65f, 1f, 0.5f));
                a.WaveNow = MakeWave(screen, "WaveNow", 0.006f, new Color(1f, 1f, 1f, 0.95f));
            }
            AudioService.TryPlayAt(l => l.StormSpark, spot + Vector3.up * 1.1f);
            return true;
        }

        private LineRenderer MakeWave(Transform screen, string name, float width, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(screen, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.004f);   // 화면 앞면 바로 앞
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = WavePoints;
            line.widthMultiplier = width;
            line.startColor = line.endColor = color;
            line.sharedMaterial = _spark;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private static float WaveFreq(int level) => 1f + 0.5f * (level - 1);
        private static float WaveAmp(int level) => 0.035f + 0.025f * (level - 1);

        /// <summary>화면: 목표 파형 (굵고 옅게) · 지금 파형 (가늘고 환하게 — 어긋날수록 떨림).</summary>
        private static void DrawWaves(Incident a, float now, bool done)
        {
            float tf = WaveFreq(a.Targets[0]), ta = WaveAmp(a.Targets[1]);
            float nf = WaveFreq(a.Levels[0]), na = WaveAmp(a.Levels[1]);
            bool solved = done || InteriorIncidentRules.NotchesSolved(a.Levels, a.Targets);
            float jitter = solved ? 0f : 0.012f;
            for (int k = 0; k < WavePoints; k++)
            {
                float u = k / (float)(WavePoints - 1);
                float x = -WaveHalfWidth + 2f * WaveHalfWidth * u;
                float phase = now * 1.5f;
                a.WaveTarget.SetPosition(k, new Vector3(x, ta * Mathf.Sin(2f * Mathf.PI * tf * u + phase), 0f));
                float noise = jitter * (Mathf.PerlinNoise(u * 9f, now * 6f) - 0.5f) * 2f;
                a.WaveNow.SetPosition(k, new Vector3(x, na * Mathf.Sin(2f * Mathf.PI * nf * u + phase) + noise, 0.001f));
            }
        }

        // ---------------- 3단계 ② 등불 순서 · 4단계 ② 별빛 등불 ----------------

        private bool PlaceLanterns(Incident a)
        {
            var parent = _builder.RoomParent(a.Room);
            var prefab = a.Kind == IncidentKind.StarLanterns ? _s.StarLantern : _s.Lantern;
            if (prefab == null || parent == null)
                return false;
            var spots = new Vector3[InteriorIncidentRules.Lamps];
            var face = Vector3.forward;
            var anchor = Vector3.zero;
            bool found = false;
            for (int tries = 0; tries < 24 && !found; tries++)
            {
                if (!InteriorSpots.Floor(_builder, a.Room, _rng, LampHalf, null, 0f, out anchor, 0.6f, 3.2f, sightHeight: 0.8f))
                    continue;
                if (InteriorSpots.NearDoor(_builder, a.Room, anchor + Vector3.up, DecoDoorMargin) || !PropFace(anchor, LampHalf.x, out face))
                    continue;
                var side = Vector3.Cross(Vector3.up, face).normalized;
                found = true;
                for (int k = 0; k < spots.Length && found; k++)
                {
                    var p = anchor + side * ((k - (spots.Length - 1) * 0.5f) * LampGap);
                    found = LampSpotOk(a, p, anchor.y, out spots[k]);
                }
            }
            bool cluster = false;
            if (!found)
            {
                // 한 줄 자리가 없으면 (관측실처럼 물건이 많은 방) 가까이 모아 놓음 — 서로 1.1m · 첫 등불에서 4.5m 안
                var used = new List<Vector3>();
                for (int tries = 0; tries < 60 && used.Count < spots.Length; tries++)
                {
                    if (!InteriorSpots.Floor(_builder, a.Room, _rng, LampHalf, used, 1.1f, out var s, 0.6f, 3.5f, sightHeight: 0.8f))
                        continue;
                    if (used.Count > 0 && (s - used[0]).sqrMagnitude > LampClusterRadius * LampClusterRadius)
                        continue;
                    if (InteriorSpots.NearDoor(_builder, a.Room, s + Vector3.up, 0.8f))
                        continue;
                    used.Add(s);
                }
                if (used.Count == spots.Length)
                {
                    anchor = Vector3.zero;
                    for (int k = 0; k < spots.Length; k++)
                    {
                        spots[k] = used[k];
                        anchor += used[k] / spots.Length;
                    }
                    found = cluster = true;
                }
            }
            LastGardenNote = $"{InteriorIncidentRules.Title(a.Kind)}: {(!found ? "자리 없음" : cluster ? "등불 모아 놓음" : "등불 줄 놓음")}";
            if (!found)
                return false;
            var marker = new GameObject("IncidentLanterns");
            marker.transform.SetParent(parent, false);
            marker.transform.SetPositionAndRotation(anchor + Vector3.up * 1.4f, Quaternion.LookRotation(face, Vector3.up));
            a.Prop = marker;
            Color litColor = a.Kind == IncidentKind.StarLanterns ? LampLitStar : LampLitGarden;
            for (int k = 0; k < spots.Length; k++)
            {
                var go = Instantiate(prefab, parent);
                go.name = "IncidentLantern" + k;
                var lampFace = face;
                if (cluster)
                {
                    lampFace = anchor - spots[k];   // 모아 놓은 등불은 가운데를 봄
                    lampFace.y = 0f;
                    if (lampFace.sqrMagnitude < 0.01f)
                        lampFace = Vector3.forward;
                }
                go.transform.rotation = Quaternion.LookRotation(lampFace.normalized, Vector3.up);
                var item = new FloorItem { Go = go, Ground = spots[k] };
                SetItemRise(item, 0f);
                var body = go.transform.Find(a.Kind == IncidentKind.StarLanterns ? "IncStarLantern" : "IncLantern");
                if (body != null)
                    AddMeshCollider(body);
                var glass = go.transform.Find("LampGlass") ?? go.transform.Find("StarGlass");
                item.Glass = glass != null ? glass.GetComponent<Renderer>() : null;
                var lightGo = new GameObject("LampLight");
                lightGo.transform.SetParent(glass != null ? glass : go.transform, false);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 2.4f;
                light.color = litColor;
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                item.Light = light;
                int index = k;
                var use = (body != null ? body.gameObject : go).AddComponent<InteriorInteractable>();
                use.HoldSeconds = _s.SwitchHold;
                use.Prompt = () => $"등불 켜기 ({a.OrderEntered}/{a.Order.Length})  <color=#FF8A5C>{InteriorIncidentRules.Title(a.Kind)}</color>";
                use.Blocked = () => Showing(a, Time.unscaledTime) ? "깜빡이는 순서를 보는 중이에요" : null;
                use.Completed = () => PressLantern(a, index);
                a.LampRow.Add(item);
                a.Extra.Add(go);
            }
            a.Order = InteriorIncidentRules.LampOrder(_rng);
            a.OrderEntered = 0;
            a.ShowStart = Time.unscaledTime + LampRiseTime + 0.5f;
            return true;
        }

        /// <summary>등불 한 자리: 같은 높이의 평평한 바닥 · 몸통 자리 빔 · 문길 밖.</summary>
        private bool LampSpotOk(Incident a, Vector3 p, float floorY, out Vector3 spot)
        {
            spot = p;
            if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var hit, 1.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                || Mathf.Abs(hit.point.y - floorY) > 0.06f || hit.normal.y < 0.95f)
                return false;
            spot = hit.point + Vector3.up * 0.002f;
            if (Physics.CheckBox(spot + Vector3.up * (LampHalf.y + 0.04f), LampHalf, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            return !InteriorSpots.NearDoor(_builder, a.Room, spot + Vector3.up, 0.8f);
        }

        private static void SetItemRise(FloorItem item, float rise)
        {
            item.Rise = Mathf.Clamp01(rise);
            if (item.Go != null)
                item.Go.transform.position = item.Ground + Vector3.down * (LampSink * (1f - Mathf.SmoothStep(0f, 1f, item.Rise)));
        }

        private static bool Showing(Incident a, float now) =>
            a.Order != null && a.ShowStart >= 0f && now >= a.ShowStart - LampRiseTime && now < a.ShowStart + a.Order.Length * LampStep;

        private void PressLantern(Incident a, int lamp)
        {
            float now = Time.unscaledTime;
            if (a.Done || a.Order == null || Showing(a, now))
                return;
            var result = InteriorIncidentRules.PressLamp(a.Order, ref a.OrderEntered, lamp);
            if (result == SwitchPress.Wrong)
            {
                a.WrongUntil = now + 0.6f;
                a.ShowStart = now + 1.1f;   // 순서를 다시 보여 줌
                AudioService.TryPlay(l => l.UiError);
                Message?.Invoke("순서가 틀렸어요 · 다시 보여 줄게요", true);
                return;
            }
            a.FlashLamp = lamp;
            a.FlashUntil = now + 0.45f;
            AudioService.TryPlayAt(l => l.BreakerSwitch, a.LampRow[lamp].Go.transform.position + Vector3.up);
            if (result == SwitchPress.AllDone)
                Resolve(a);
        }

        private void AnimateLanterns(Incident a, float now, bool done)
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var item in a.LampRow)
            {
                if (!done && item.Rise < 1f)
                    SetItemRise(item, item.Rise + dt / LampRiseTime);
            }
            int lit = -1;
            if (!done && Showing(a, now) && now >= a.ShowStart)
            {
                float t = now - a.ShowStart;
                int k = Mathf.FloorToInt(t / LampStep);
                if (k < a.Order.Length && t - k * LampStep < LampOn)
                    lit = a.Order[k];
            }
            else if (now < a.FlashUntil)
            {
                lit = a.FlashLamp;
            }
            bool wrong = now < a.WrongUntil;
            Color litColor = a.Kind == IncidentKind.StarLanterns ? LampLitStar : LampLitGarden;
            for (int k = 0; k < a.LampRow.Count; k++)
            {
                var item = a.LampRow[k];
                bool on = done || k == lit || (wrong && Mathf.Repeat(now, 0.16f) < 0.08f);
                if (item.Glass != null)
                    TintRenderer(item.Glass, "M_IncLed", on ? litColor : LampDark);
                if (item.Light != null)
                    item.Light.intensity = on ? 1.3f : 0f;
            }
        }

        // ---------------- 3단계 ③ 덩굴 웃자람 ----------------

        private bool PlaceVines(Incident a)
        {
            var parent = _builder.RoomParent(a.Room);
            if (_s.Vines == null || parent == null)
                return false;
            int want = VineMin + _rng.Next(VineMax - VineMin + 1);
            var spots = new List<Vector3>();
            for (int tries = 0; tries < 20 && spots.Count < want; tries++)
            {
                float spacing = tries < 12 ? 3f : 2f;
                if (!InteriorSpots.Floor(_builder, a.Room, _rng, VineHalf, spots, spacing, out var s, 0.5f, 3.5f, sightHeight: 0.4f))
                    continue;
                if (InteriorSpots.NearDoor(_builder, a.Room, s + Vector3.up, 1.0f))
                    continue;   // 문을 아예 막지 않게
                spots.Add(s);
            }
            LastGardenNote = $"덩굴 웃자람: {spots.Count}/{want}";
            if (spots.Count < VineMin)
                return false;
            int total = spots.Count;
            for (int k = 0; k < total; k++)
            {
                var go = Instantiate(_s.Vines, parent);
                go.name = "IncidentVines" + k;
                go.transform.SetPositionAndRotation(spots[k], Quaternion.Euler(0f, (float)_rng.NextDouble() * 360f, 0f));
                go.transform.localScale = Vector3.one * 0.01f;
                var body = go.transform.Find("IncVines");
                if (body == null)
                    body = go.transform;   // 물체 하나짜리 FBX = 뿌리가 곧 메시
                AddMeshCollider(body);
                var item = new FloorItem { Go = go, Ground = spots[k], Seed = (float)_rng.NextDouble() * 10f };
                var use = body.gameObject.AddComponent<InteriorInteractable>();
                use.HoldSeconds = _s.FireHold;
                use.Prompt = () => $"덩굴 쳐내기 ({VinesCut(a) + 1}/{total})  <color=#FF8A5C>{InteriorIncidentRules.Title(a.Kind)}</color>";
                use.Holding = p =>
                {
                    a.Holding = true;
                    a.HoldProgress = p;
                    a.HoldingVine = item;
                };
                use.Completed = () => CutVine(a, item);
                a.VineClumps.Add(item);
                a.Extra.Add(go);
            }
            var marker = new GameObject("IncidentVinesMark");
            marker.transform.SetParent(parent, false);
            a.Prop = marker;
            MoveVineMarker(a);
            a.Held = AudioService.AttachHeld(l => l.VentFix, marker.transform);
            return true;
        }

        private static int VinesCut(Incident a)
        {
            int n = 0;
            foreach (var v in a.VineClumps)
            {
                if (v.Done)
                    n++;
            }
            return n;
        }

        private static void MoveVineMarker(Incident a)
        {
            foreach (var v in a.VineClumps)
            {
                if (v.Done || v.Go == null)
                    continue;
                a.Prop.transform.SetPositionAndRotation(v.Ground + Vector3.up * 1.1f, Quaternion.identity);
                return;
            }
        }

        private void CutVine(Incident a, FloorItem vine)
        {
            if (a.Done || vine.Done)
                return;
            vine.Done = true;
            foreach (var c in vine.Go.GetComponentsInChildren<Collider>())
                Destroy(c);
            foreach (var u in vine.Go.GetComponentsInChildren<InteriorInteractable>())
                Destroy(u);
            if (VinesCut(a) >= a.VineClumps.Count)
            {
                Resolve(a);
                return;
            }
            MoveVineMarker(a);
        }

        /// <summary>덩굴: 자라나 나옴 · 누르는 동안 흔들리며 줄어듦 · 쳐낸 덤불은 작아지며 사라짐.</summary>
        private void AnimateVines(Incident a, float now, bool holding)
        {
            float dt = Time.unscaledDeltaTime;
            if (holding && a.HoldingVine != null && a.Held != null)
                a.Held.transform.position = a.HoldingVine.Ground + Vector3.up * 0.5f;
            foreach (var v in a.VineClumps)
            {
                if (v.Go == null)
                    continue;
                if (v.Done)
                {
                    v.Rise = Mathf.MoveTowards(v.Rise, 0f, dt / 0.35f);
                    v.Go.transform.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.SmoothStep(0f, 1f, v.Rise));
                    if (v.Rise <= 0f && v.Go.activeSelf)
                        v.Go.SetActive(false);
                    continue;
                }
                v.Rise = Mathf.MoveTowards(v.Rise, 1f, dt / 0.8f);
                float scale = Mathf.SmoothStep(0f, 1f, v.Rise);
                bool cutting = holding && a.HoldingVine == v;
                if (cutting)
                    scale *= 1f - 0.35f * a.HoldProgress;
                v.Go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
                float sway = cutting ? 6f * Mathf.Sin(now * 30f + v.Seed) : 2f * Mathf.Sin(now * 1.3f + v.Seed);
                v.Go.transform.rotation = Quaternion.Euler(sway * 0.4f, v.Go.transform.eulerAngles.y, sway);
            }
        }

        // ---------------- 디스패치 (InteriorIncidents.Stage.cs가 부름) ----------------

        private bool PlaceGarden(Incident a)
        {
            switch (a.Kind)
            {
                case IncidentKind.WaterChannel: return PlaceWaterChannel(a);
                case IncidentKind.Lanterns:
                case IncidentKind.StarLanterns: return PlaceLanterns(a);
                case IncidentKind.Vines: return PlaceVines(a);
                case IncidentKind.SignalDrift: return PlaceSignal(a);
                default: return false;
            }
        }

        private void AnimateGarden(Incident a, float now, bool holding, bool done)
        {
            switch (a.Kind)
            {
                case IncidentKind.WaterChannel:
                case IncidentKind.SignalDrift:
                    AnimateNotches(a, now, done);
                    if (done && a.Age - a.DoneAge > 0.9f)
                        SetConsoleRise(a, a.ConsoleRise - Time.unscaledDeltaTime / ConsoleRiseTime);
                    break;
                case IncidentKind.Lanterns:
                case IncidentKind.StarLanterns:
                    AnimateLanterns(a, now, done);
                    if (done && a.Age - a.DoneAge > 1.2f)
                    {
                        foreach (var item in a.LampRow)
                            SetItemRise(item, item.Rise - Time.unscaledDeltaTime / LampRiseTime);
                    }
                    break;
                case IncidentKind.Vines:
                    AnimateVines(a, now, holding && !done);
                    break;
            }
        }

        private void ResolveGarden(Incident a)
        {
            if (a.Kind == IncidentKind.WaterChannel)
                RestoreWater(a);   // 물이 바로 다시 흐름
        }

        /// <summary>정리: 멈춘 물을 되돌리고, 해결했으면 소품을 지움 (놓쳤으면 End가 소품 · Extra를 지움).</summary>
        private void RestoreGarden(Incident a)
        {
            if (a.Kind == IncidentKind.WaterChannel)
                RestoreWater(a);
            if (!a.Done)
                return;
            if (a.Prop != null)
                Destroy(a.Prop);
            foreach (var item in a.LampRow)
            {
                if (item.Go != null)
                    Destroy(item.Go);
            }
            foreach (var v in a.VineClumps)
            {
                if (v.Go != null)
                    Destroy(v.Go);
            }
        }

        private static bool IsGarden(IncidentKind kind) =>
            kind == IncidentKind.WaterChannel || kind == IncidentKind.Lanterns || kind == IncidentKind.Vines
            || kind == IncidentKind.SignalDrift || kind == IncidentKind.StarLanterns;
    }
}
