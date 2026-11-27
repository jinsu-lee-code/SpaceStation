using System;
using System.Collections.Generic;
using SpaceStation.Audio;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// CONNECT_1121 A6 2단계 ① 놀이기구 · 쇼 멈춤 (사용자 결정 2026-11-27, 2단계 셋 중 하나):
    /// 방의 탈것 · 쇼 · 대표 장치가 멈추고 (컴포넌트를 꺼 둠 — 각 기구의 OnDisable이 제자리에 세우고 타던 사람을 내려 줌),
    /// 사람 손으로 도는 장식(고리 · 바람개비 등)은 천천히 멈춤. 기구의 좌석 · 버튼은 "운행 정지"로 막힘.
    /// 바닥에서 운행 조작반이 올라오고, 경고등 켜진 레버만 올리면 (차단기 규칙 — <see cref="InteriorIncidentRules.Press"/>) 다시 움직임.
    /// 모델 = BlenderWork/InteriorProps/stage_incident_builder.py (IncRideConsole · Switch0~5 · Led0~5).
    /// </summary>
    public sealed partial class InteriorIncidents
    {
        private sealed partial class Incident
        {
            public readonly List<Behaviour> Stopped = new List<Behaviour>();
            public readonly List<(InteriorOrbit Orbit, float Speed)> Slowed = new List<(InteriorOrbit, float)>();
            public readonly List<(InteriorInteractable Use, Func<string> Blocked)> Closed = new List<(InteriorInteractable, Func<string>)>();
            public Vector3 ConsoleGround;
            public float ConsoleRise;
            public float OrbitLevel = 1f;
        }

        /// <summary>멈출 수 있는 기구 · 쇼 · 장치 (엘리베이터 · 망원경 · 미로처럼 플레이어가 쓰는 도구는 뺌).</summary>
        private static readonly HashSet<Type> RideTypes = new HashSet<Type>
        {
            typeof(InteriorFerrisWheel), typeof(InteriorCarousel), typeof(InteriorTrain), typeof(InteriorFlumeRide),
            typeof(InteriorDroneShow), typeof(InteriorFireworkShow), typeof(InteriorStellaDome), typeof(InteriorPatrolCar),
            typeof(InteriorStorageCrane), typeof(InteriorOreCrane), typeof(InteriorSunForge), typeof(InteriorHeliostat),
            typeof(InteriorRadar), typeof(InteriorTurretAim), typeof(InteriorFoodLine), typeof(InteriorLightning),
        };

        /// <summary>자연스러운 움직임 (물고기 · 구름 · 거품 · 플라스마 · 기체 · 흐름 · 떠 있는 돌) — 멈추지 않음.</summary>
        private static readonly string[] NaturalMotion = { "Fish", "Cloud", "Bubble", "Plasma", "Gas", "Flow", "Stone" };

        /// <summary>확인용: 마지막 놀이기구 멈춤 놓기 결과 (멈춘 수 · 조작반 자리).</summary>
        public static string LastRideNote { get; private set; }

        private static readonly Vector3 ConsoleHalf = new Vector3(0.52f, 0.7f, 0.52f);
        /// <summary>조작반 몸 충돌체 (모델 좌표, m — stage_incident_builder.py와 맞춤): 아래 함 · 레버 판 (판 앞면 z 0.06, 레버는 그 앞).</summary>
        private static readonly Vector3 ConsoleCabinetCenter = new Vector3(0f, 0.45f, 0f);
        private static readonly Vector3 ConsoleCabinetSize = new Vector3(0.98f, 0.9f, 0.46f);
        private static readonly Vector3 ConsoleBoardCenter = new Vector3(0f, 1.125f, -0.02f);
        private static readonly Vector3 ConsoleBoardSize = new Vector3(0.9f, 0.47f, 0.16f);
        private const float ConsoleHalfWidth = 0.5f;
        private const float ConsoleSink = 1.45f;
        private const float ConsoleRiseTime = 0.7f;
        private const float ConsoleBubbleHeight = 1.35f;
        private const int ConsoleTries = 32;
        private const int ConsoleCandidates = 12;
        private const float OrbitStopSeconds = 1.6f;
        private static readonly Color LedDim = new Color(0.25f, 0.05f, 0.04f);

        private bool PlaceRideStop(Incident a)
        {
            var parent = _builder.RoomParent(a.Room);
            if (_s.RideConsole == null || parent == null)
                return false;
            // 멈출 것
            var rides = new List<Behaviour>();
            var rideTypes = new HashSet<Type>();
            foreach (var b in parent.GetComponentsInChildren<Behaviour>())
            {
                if (b == null || !b.enabled || !RideTypes.Contains(b.GetType()))
                    continue;
                if (PlayerAboard(b))
                    return false;   // 지금 타고 있는 기구는 세우지 않음 (다른 상황 · 다음에)
                rides.Add(b);
                rideTypes.Add(b.GetType());
            }
            var orbits = new List<InteriorOrbit>();
            foreach (var o in parent.GetComponentsInChildren<InteriorOrbit>())
            {
                if (o.enabled && !Natural(o.name))
                    orbits.Add(o);
            }
            if (rides.Count == 0 && orbits.Count == 0)
                return false;
            // 기구의 좌석 · 버튼 (그 기구 코드가 만든 상호작용)
            var uses = new List<InteriorInteractable>();
            foreach (var use in parent.GetComponentsInChildren<InteriorInteractable>())
            {
                var owner = Owner(use.Completed) ?? Owner(use.Prompt);
                if (owner != null && rideTypes.Contains(owner))
                    uses.Add(use);
            }
            // 조작반 자리: 멈춘 것 가까이 (좌석 · 버튼 · 도는 장식의 가운데)
            bool hasAnchor = false;
            var anchor = Vector3.zero;
            int n = 0;
            foreach (var use in uses)
            {
                anchor += use.transform.position;
                n++;
            }
            foreach (var o in orbits)
            {
                var r = o.GetComponentInChildren<Renderer>();
                anchor += r != null ? r.bounds.center : o.transform.position;
                n++;
            }
            if (n > 0)
            {
                anchor /= n;
                hasAnchor = true;
            }
            bool placed = ConsoleSpot(a, hasAnchor, anchor, out var spot, out var face);
            LastRideNote = $"기구 {rides.Count} · 도는 장식 {orbits.Count} · 막은 좌석 · 버튼 {uses.Count} · 조작반 {(placed ? "놓음" : "자리 없음")}"
                + (placed && hasAnchor ? $" (멈춘 것까지 {Vector2.Distance(new Vector2(spot.x, spot.z), new Vector2(anchor.x, anchor.z)):0.0}m)" : "");
            if (!placed)
                return false;

            // 세움
            foreach (var b in rides)
            {
                b.enabled = false;
                a.Stopped.Add(b);
            }
            foreach (var o in orbits)
                a.Slowed.Add((o, o.SpeedScale));
            foreach (var use in uses)
            {
                a.Closed.Add((use, use.Blocked));
                use.Blocked = () => "운행 정지 · 조작반에서 레버를 올려 주세요";
            }

            // 조작반
            var go = Instantiate(_s.RideConsole, parent);
            go.name = "IncidentRideConsole";
            go.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(face, Vector3.up));
            // 몸 충돌체 = 아래 함 + 레버 판 (경계 상자 하나로 하면 앞으로 나온 레버가 그 안에 묻혀 시선이 레버에 안 닿음 — 2026-11-27 플레이)
            var body = go.transform.Find("IncRideConsole");
            if (body != null)
            {
                var cabinet = body.gameObject.AddComponent<BoxCollider>();
                cabinet.center = ConsoleCabinetCenter;
                cabinet.size = ConsoleCabinetSize;
                var board = body.gameObject.AddComponent<BoxCollider>();
                board.center = ConsoleBoardCenter;
                board.size = ConsoleBoardSize;
            }
            a.Prop = go;
            a.ConsoleGround = spot;
            SetConsoleRise(a, 0f);
            var bubbleAt = new GameObject("BubbleAt").transform;
            bubbleAt.SetParent(go.transform, false);
            bubbleAt.localPosition = new Vector3(0f, ConsoleBubbleHeight, 0f);
            a.BubbleAt = bubbleAt;

            a.Pattern = InteriorIncidentRules.TripPattern(_rng);
            a.Lit = (bool[])a.Pattern.Clone();
            for (int i = 0; i < InteriorIncidentRules.Switches; i++)
            {
                var lever = go.transform.Find("Switch" + i);
                var led = go.transform.Find("Led" + i);
                a.Levers[i] = lever;
                a.Leds[i] = led != null ? led.GetComponent<Renderer>() : null;
                if (lever == null)
                    continue;
                a.LeverRest[i] = lever.localRotation;
                var sc = lever.gameObject.AddComponent<BoxCollider>();
                sc.center = new Vector3(0f, 0f, 0.07f);   // 판에서 앞으로 나온 막대 + 손잡이
                sc.size = new Vector3(0.07f, 0.07f, 0.15f);
                int index = i;
                var su = lever.gameObject.AddComponent<InteriorInteractable>();
                su.HoldSeconds = _s.SwitchHold;
                su.Prompt = () => a.Lit[index] ? "레버 올리기  <color=#FF8A5C>경고등 깜빡임</color>" : "레버  <color=#AFC4D8>정상 (건드리지 마세요)</color>";
                su.Completed = () => PressSwitch(a, index);
            }
            ShowSwitches(a);
            AudioService.TryPlayAt(l => l.StormSpark, spot + Vector3.up * 1.1f); // 기구가 서는 순간 "지직"
            return true;
        }

        /// <summary>조작반 자리 후보 (바닥 · 문길 밖 · 등 댈 것 · 문 앞 바닥 · 옆 틈) 가운데 멈춘 것에 가장 가까운 곳.</summary>
        private bool ConsoleSpot(Incident a, bool hasAnchor, Vector3 anchor, out Vector3 spot, out Vector3 face)
        {
            spot = default;
            face = default;
            bool found = false;
            float best = float.MaxValue;
            int candidates = 0;
            for (int tries = 0; tries < ConsoleTries && candidates < ConsoleCandidates; tries++)
            {
                if (!InteriorSpots.Floor(_builder, a.Room, _rng, ConsoleHalf, null, 0f, out var s, 0.6f, 3.5f, sightHeight: 1.1f))
                    continue;
                if (InteriorSpots.NearDoor(_builder, a.Room, s + Vector3.up, DecoDoorMargin) || !PropFace(s, ConsoleHalfWidth, out var f))
                    continue;
                candidates++;
                float d = hasAnchor ? new Vector2(s.x - anchor.x, s.z - anchor.z).sqrMagnitude : 0f;
                if (!found || d < best)
                {
                    found = true;
                    best = d;
                    spot = s;
                    face = f;
                }
                if (!hasAnchor)
                    break;
            }
            return found;
        }

        /// <summary>경고등: 켜진 것(내려간 레버)은 빨강으로 깜빡 (색 · 밝기 · 깜빡임으로 구분), 정상은 초록 그대로. 조작반이 올라오고 도는 장식이 천천히 멈춤.</summary>
        private void AnimateRideStop(Incident a, float now)
        {
            float dt = Time.unscaledDeltaTime;
            if (a.ConsoleRise < 1f)
                SetConsoleRise(a, a.ConsoleRise + dt / ConsoleRiseTime);
            a.OrbitLevel = Mathf.MoveTowards(a.OrbitLevel, 0f, dt / OrbitStopSeconds);
            ApplyOrbitLevel(a);
            bool blink = Mathf.Repeat(now, 0.7f) < 0.4f;
            for (int i = 0; i < InteriorIncidentRules.Switches; i++)
            {
                if (a.Leds[i] != null && a.Lit != null && a.Lit[i])
                    TintRenderer(a.Leds[i], "M_IncLed", blink ? LedRed : LedDim);
            }
        }

        /// <summary>해결: 기구를 다시 켜고 좌석 · 버튼을 풂 (도는 장식은 <see cref="AnimateRideDone"/>에서 천천히).</summary>
        private void RestartRides(Incident a)
        {
            foreach (var b in a.Stopped)
            {
                if (b != null)
                    b.enabled = true;
            }
            a.Stopped.Clear();
            foreach (var (use, blocked) in a.Closed)
            {
                if (use != null)
                    use.Blocked = blocked;
            }
            a.Closed.Clear();
        }

        private void AnimateRideDone(Incident a, float dt)
        {
            a.OrbitLevel = Mathf.MoveTowards(a.OrbitLevel, 1f, dt / OrbitStopSeconds);
            ApplyOrbitLevel(a);
            if (a.Age - a.DoneAge > 0.9f)
                SetConsoleRise(a, a.ConsoleRise - dt / ConsoleRiseTime);
        }

        /// <summary>정리: 기구 · 좌석 · 도는 장식을 원래대로, 해결했으면 내려간 조작반을 지움 (놓쳤으면 End가 지움).</summary>
        private void RestoreRides(Incident a)
        {
            RestartRides(a);
            foreach (var (orbit, speed) in a.Slowed)
            {
                if (orbit != null)
                    orbit.SpeedScale = speed;
            }
            a.Slowed.Clear();
            if (a.Done && a.Prop != null)
                Destroy(a.Prop);
        }

        private static void ApplyOrbitLevel(Incident a)
        {
            float k = Mathf.SmoothStep(0f, 1f, a.OrbitLevel);
            foreach (var (orbit, speed) in a.Slowed)
            {
                if (orbit != null)
                    orbit.SpeedScale = speed * k;
            }
        }

        private static void SetConsoleRise(Incident a, float rise)
        {
            a.ConsoleRise = Mathf.Clamp01(rise);
            if (a.Prop != null)
                a.Prop.transform.position = a.ConsoleGround + Vector3.down * (ConsoleSink * (1f - Mathf.SmoothStep(0f, 1f, a.ConsoleRise)));
        }

        private static bool PlayerAboard(Behaviour b) =>
            (b is InteriorFerrisWheel f && f.Riding) || (b is InteriorCarousel c && c.Riding) || (b is InteriorTrain t && t.Riding)
            || (b is InteriorFlumeRide r && r.Riding) || (b is InteriorStellaDome d && d.Seated);

        private static bool Natural(string name)
        {
            foreach (var w in NaturalMotion)
            {
                if (name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>상호작용을 만든 기구 종류 (람다가 담긴 클래스 — 컴파일러가 만든 안쪽 클래스면 그 바깥).</summary>
        private static Type Owner(Delegate d)
        {
            var t = d?.Method.DeclaringType;
            while (t != null && t.IsNested && t.Name.StartsWith("<", StringComparison.Ordinal))
                t = t.DeclaringType;
            return t;
        }
    }
}
