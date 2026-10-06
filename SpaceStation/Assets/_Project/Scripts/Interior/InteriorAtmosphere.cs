using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-10 상태가 공간에 드러남. 방마다 시뮬레이션 상태(<see cref="RoomCondition"/>)를 0.25초마다 읽어
    /// <see cref="InteriorMoodRules"/>로 연출 값을 정하고, 매 프레임 방 조명·천장 조명 판 발광에 적용한다.
    /// - 전력 부족: 효율만큼 어둡게 + 효율이 낮을수록 자주 깜빡임 (모든 방)
    /// - 노후: 내구도 효율만큼 누렇고 어둡게, 아주 낮으면 형광등처럼 떨림
    /// - 태양 폭풍: 지직거림 + 가끔 푸르게 번쩍임
    /// - 파손: 붉은 비상 조명 + 천장 경광등(회전 스포트) + 연기 + 간헐 스파크 (+ 지금 방이면 경보음, <see cref="InteriorAudio"/>)
    /// - 비활성: 방 조명 꺼짐 + 칸마다 바닥 호박색 비상등
    /// - 방 안 칸 게이지(<see cref="InteriorGauge"/>, 배터리 정비 통로): 정거장 배터리 저장량만큼 점등
    /// 시간은 실제 시간(unscaled) — 내부 방문 중 게임 시간이 멈춰 있어도 그 순간의 상태가 계속 보인다.
    /// 연출 오브젝트는 방 오브젝트(<see cref="InteriorBuilder.RoomParent"/>) 아래에 붙여 내부를 다시 만들 때 함께 지워진다.
    /// </summary>
    public sealed class InteriorAtmosphere : MonoBehaviour
    {
        private const float PollInterval = 0.25f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private sealed class Room
        {
            public ModuleInstance Module;
            public Transform Parent;
            public Bounds Bounds;
            public readonly List<Light> Lights = new List<Light>();
            public readonly List<float> LightBase = new List<float>();
            public readonly List<Color> LightColor = new List<Color>();
            public readonly List<InteriorBuilder.LightSlot> Slots = new List<InteriorBuilder.LightSlot>();
            public InteriorGauge[] Gauges;
            public RoomMood Mood;
            public bool HasMood;
            public float Seed;
            // 깜빡임 · 떨림 · 번쩍임
            public float NextFlicker, FlickerUntil, FlickerLevel = 1f;
            public float NextStutter, StutterToggleAt;
            public int StutterLeft;
            public bool StutterOff;
            public float NextSpike, SpikeUntil;
            // 연출 오브젝트
            public GameObject Alarm;
            public Transform BeaconHead;
            public float NextSpark;
            public GameObject Emergency;
            public float LastFactor = -1f;
            public Color LastTint;
        }

        private StationController _station;
        private InteriorBuilder _builder;
        private InteriorMoodTuning _tuning;
        private Material _smokeMaterial;
        private Material _sparkMaterial;
        private Material _beaconMaterial;
        private readonly List<Room> _rooms = new List<Room>();
        private readonly Dictionary<ModuleInstance, Room> _byModule = new Dictionary<ModuleInstance, Room>();
        private MaterialPropertyBlock _block;
        private MaterialPropertyBlock _empty;
        private float _nextPoll;

        /// <summary>플레이어가 있는 방 (스파크 소리는 이 방에서만).</summary>
        public ModuleInstance CurrentRoom { get; set; }

        public static InteriorAtmosphere Create(Transform parent, StationController station, InteriorBuilder builder,
            InteriorMoodTuning tuning, Material smoke, Material spark, Material beacon)
        {
            var go = new GameObject("InteriorAtmosphere");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<InteriorAtmosphere>();
            a._station = station;
            a._builder = builder;
            a._tuning = tuning ?? new InteriorMoodTuning();
            a._smokeMaterial = smoke;
            a._sparkMaterial = spark;
            a._beaconMaterial = beacon;
            a._block = new MaterialPropertyBlock();
            a._empty = new MaterialPropertyBlock();
            return a;
        }

        /// <summary>내부를 (다시) 만든 직후. 방 목록과 조명을 새로 모은다.</summary>
        public void Rebuild(InteriorLayout layout)
        {
            _rooms.Clear();
            _byModule.Clear();
            if (layout == null)
                return;
            foreach (var r in layout.Rooms)
            {
                var room = new Room
                {
                    Module = r.Module,
                    Parent = _builder.RoomParent(r.Module),
                    Bounds = _builder.RoomBounds(r.Module),
                    Seed = Random.value * 100f,
                };
                _builder.CollectRoomLights(r.Module, room.Lights, room.LightBase, room.Slots);
                foreach (var l in room.Lights)
                    room.LightColor.Add(l.color);
                room.Gauges = room.Parent != null ? room.Parent.GetComponentsInChildren<InteriorGauge>() : new InteriorGauge[0];
                _rooms.Add(room);
                _byModule[r.Module] = room;
            }
            Poll();
            _nextPoll = Time.unscaledTime + PollInterval;
        }

        public bool IsAlarm(ModuleInstance module)
        {
            return module != null && _byModule.TryGetValue(module, out var r) && r.HasMood && r.Mood.Mode == RoomLightMode.Alarm;
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            if (now >= _nextPoll)
            {
                Poll();
                _nextPoll = now + PollInterval;
            }
            foreach (var room in _rooms)
            {
                if (!room.HasMood)
                    continue;
                float factor = Factor(room, now, out var tint);
                Apply(room, factor, tint);
                if (room.Mood.Mode == RoomLightMode.Alarm)
                    UpdateAlarm(room, now, dt);
            }
        }

        // ---------------- 상태 읽기 ----------------

        private void Poll()
        {
            if (_station == null || _station.Simulation == null)
                return;
            var sim = _station.Simulation;
            bool storm = false;
            foreach (var e in sim.Events.ActiveEvents)
            {
                if (e.Data is SolarStormEventData)
                    storm = true;
            }
            float power = sim.Resources.PowerEfficiency;
            float charge = sim.Resources.BatteryCapacity > 0f ? sim.Resources.BatteryCharge / sim.Resources.BatteryCapacity : 0f;
            foreach (var room in _rooms)
            {
                var m = room.Module;
                var c = new RoomCondition
                {
                    Active = _station.Connectivity.IsActive(m),
                    Damaged = sim.Damage.TryGetInfo(m, out _),
                    Power = power,
                    Wear = sim.Durability.TryGetInfo(m, out var d) ? sim.Durability.EfficiencyFor(d.Current) : 1f,
                    Storm = storm,
                };
                var mood = InteriorMoodRules.Evaluate(c, _tuning);
                if (!room.HasMood || mood.Mode != room.Mood.Mode)
                {
                    SetAlarm(room, mood.Mode == RoomLightMode.Alarm);
                    SetEmergency(room, mood.Mode == RoomLightMode.Emergency);
                }
                room.Mood = mood;
                room.HasMood = true;
                // 배터리 저장량 게이지 (정거장 전체 저장량, 비활성 방은 꺼짐)
                foreach (var g in room.Gauges)
                    g.SetFill(charge, mood.Mode != RoomLightMode.Emergency);
            }
        }

        // ---------------- 조명 ----------------

        private float Factor(Room room, float now, out Color tint)
        {
            var m = room.Mood;
            tint = m.Tint;
            if (m.Mode == RoomLightMode.Emergency)
                return 0f;
            float f = m.Brightness;

            // 전력 부족 깜빡임: 평균 FlickerRate회/초, 가끔 두 번 연달아
            if (m.FlickerRate > 0f)
            {
                if (now >= room.NextFlicker && now >= room.FlickerUntil)
                {
                    room.FlickerUntil = now + Random.Range(_tuning.FlickerDuration.x, _tuning.FlickerDuration.y);
                    room.FlickerLevel = Random.Range(_tuning.FlickerLevel.x, _tuning.FlickerLevel.y);
                    room.NextFlicker = Random.value < 0.35f ? room.FlickerUntil + 0.06f : now + Interval(m.FlickerRate);
                }
                if (now < room.FlickerUntil)
                    f *= room.FlickerLevel;
            }

            // 노후 떨림: 0.03~0.09초 간격으로 3~6번 꺼졌다 켜짐
            if (m.StutterRate > 0f)
            {
                if (room.StutterLeft == 0 && now >= room.NextStutter)
                {
                    room.StutterLeft = Random.Range(3, 7);
                    room.StutterToggleAt = now;
                }
                if (room.StutterLeft > 0 && now >= room.StutterToggleAt)
                {
                    room.StutterOff = !room.StutterOff;
                    room.StutterLeft--;
                    room.StutterToggleAt = now + Random.Range(0.03f, 0.09f);
                    if (room.StutterLeft == 0)
                    {
                        room.StutterOff = false;
                        room.NextStutter = now + Interval(m.StutterRate);
                    }
                }
                if (room.StutterOff)
                    f *= 0.15f;
            }
            else
            {
                room.StutterLeft = 0;
                room.StutterOff = false;
            }

            // 태양 폭풍: 빠른 잡음 + 가끔 번쩍
            if (m.Jitter > 0f)
            {
                float n = Mathf.PerlinNoise(room.Seed, now * 18f) * 2f - 1f;
                f *= 1f + m.Jitter * n;
                if (now >= room.NextSpike)
                {
                    room.SpikeUntil = now + Random.Range(0.04f, 0.09f);
                    room.NextSpike = now + Interval(_tuning.StormSpikeRate);
                }
                if (now < room.SpikeUntil)
                {
                    f *= 1.5f;
                    tint *= _tuning.StormSpikeTint;
                }
            }
            return Mathf.Max(0f, f);
        }

        private static float Interval(float rate)
        {
            return rate > 0f ? -Mathf.Log(1f - Random.Range(0f, 0.999f)) / rate : float.MaxValue;
        }

        private void Apply(Room room, float factor, Color tint)
        {
            if (Mathf.Abs(factor - room.LastFactor) < 0.004f && tint == room.LastTint)
                return;
            room.LastFactor = factor;
            room.LastTint = tint;
            bool plain = Mathf.Abs(factor - 1f) < 0.004f && tint == Color.white;
            for (int i = 0; i < room.Lights.Count; i++)
            {
                var light = room.Lights[i];
                if (light == null)
                    continue;
                light.intensity = room.LightBase[i] * factor;
                light.color = room.LightColor[i] * tint;
            }
            foreach (var slot in room.Slots)
            {
                if (slot.Renderer == null)
                    continue;
                if (plain)
                {
                    slot.Renderer.SetPropertyBlock(_empty, slot.Slot);
                    continue;
                }
                _block.Clear();
                _block.SetColor(BaseColorId, slot.BaseColor * Color.Lerp(Color.black, Color.white, Mathf.Clamp01(0.4f + factor)));
                _block.SetColor(EmissionColorId, slot.Emission * tint * factor);
                slot.Renderer.SetPropertyBlock(_block, slot.Slot);
            }
        }

        // ---------------- 비활성: 비상등 ----------------

        private void SetEmergency(Room room, bool on)
        {
            if (!on)
            {
                if (room.Emergency != null)
                    Destroy(room.Emergency);
                room.Emergency = null;
                return;
            }
            if (room.Emergency != null || room.Parent == null)
                return;
            room.Emergency = new GameObject("EmergencyLights");
            room.Emergency.transform.SetParent(room.Parent, false);
            foreach (var cell in room.Module.Cells)
            {
                var go = new GameObject("Emergency");
                go.transform.SetParent(room.Emergency.transform, false);
                go.transform.position = _builder.FloorPoint(cell) + Vector3.up * 0.4f;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.color = _tuning.EmergencyColor;
                light.intensity = _tuning.EmergencyIntensity;
                light.range = _tuning.EmergencyRange;
            }
        }

        // ---------------- 파손: 경광등 · 연기 · 스파크 ----------------

        private void SetAlarm(Room room, bool on)
        {
            if (!on)
            {
                if (room.Alarm != null)
                    Destroy(room.Alarm);
                room.Alarm = null;
                room.BeaconHead = null;
                return;
            }
            if (room.Alarm != null || room.Parent == null)
                return;
            room.Alarm = new GameObject("DamageFx");
            room.Alarm.transform.SetParent(room.Parent, false);
            BuildBeacon(room);
            BuildSmoke(room);
            room.NextSpark = Time.unscaledTime + Random.Range(0.2f, 0.8f);
        }

        private void UpdateAlarm(Room room, float now, float dt)
        {
            if (room.BeaconHead != null)
                room.BeaconHead.Rotate(0f, _tuning.BeaconSpeed * dt, 0f, Space.Self);
            if (now >= room.NextSpark)
            {
                Spark(room);
                room.NextSpark = now + Random.Range(_tuning.SparkInterval.x, _tuning.SparkInterval.y);
            }
        }

        /// <summary>천장 아래 경광등 자리: 칸마다 사람 눈높이에서 위로 광선을 쏴 천장(0.9 ~ 3.2m 위)이 있는 첫 자리.</summary>
        private Vector3 BeaconPoint(Room room)
        {
            var offsets = new[] { Vector3.zero, new Vector3(2.2f, 0f, 0f), new Vector3(-2.2f, 0f, 0f), new Vector3(0f, 0f, 2.2f), new Vector3(0f, 0f, -2.2f),
                new Vector3(2.2f, 0f, 2.2f), new Vector3(-2.2f, 0f, -2.2f), new Vector3(2.2f, 0f, -2.2f), new Vector3(-2.2f, 0f, 2.2f) };
            foreach (var cell in room.Module.Cells)
            {
                foreach (var o in offsets)
                {
                    var p = _builder.FloorPoint(cell) + o + Vector3.up * FirstPersonController.EyeHeight;
                    if (Physics.CheckSphere(p, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        continue;
                    if (Physics.Raycast(p, Vector3.up, out var hit, 3.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.distance > 0.9f)
                        return hit.point - Vector3.up * 0.02f;
                }
            }
            var b = room.Bounds;
            return new Vector3(b.center.x, b.min.y + InteriorGeometry.RoomHeight - 0.15f, b.center.z);
        }

        private void BuildBeacon(Room room)
        {
            var beacon = new GameObject("Beacon").transform;
            beacon.SetParent(room.Alarm.transform, false);
            beacon.position = BeaconPoint(room);

            var mount = Primitive(PrimitiveType.Cylinder, beacon, new Vector3(0f, -0.03f, 0f), new Vector3(0.26f, 0.03f, 0.26f));
            Paint(mount, new Color(0.18f, 0.18f, 0.2f), Color.black);

            var head = new GameObject("Head").transform;
            head.SetParent(beacon, false);
            head.localPosition = new Vector3(0f, -0.15f, 0f);
            room.BeaconHead = head;
            var dome = Primitive(PrimitiveType.Sphere, head, Vector3.zero, new Vector3(0.2f, 0.17f, 0.2f));
            Paint(dome, _tuning.BeaconColor, _tuning.BeaconColor * 4f);

            // 등 맞댄 스포트 둘이 살짝 아래로 → 돌면서 벽을 쓸고 지나감
            foreach (float yaw in new[] { 0f, 180f })
            {
                var go = new GameObject("Spot");
                go.transform.SetParent(head, false);
                go.transform.localRotation = Quaternion.Euler(22f, yaw, 0f);
                var spot = go.AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.shadows = LightShadows.None;
                spot.color = _tuning.BeaconColor;
                spot.intensity = _tuning.BeaconIntensity;
                spot.range = _tuning.BeaconRange;
                spot.spotAngle = 60f;
                spot.innerSpotAngle = 25f;
            }
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(head, false);
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.shadows = LightShadows.None;
            glow.color = _tuning.BeaconColor;
            glow.intensity = 1.5f;
            glow.range = 4f;
        }

        private Renderer Primitive(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            if (_beaconMaterial != null)
                r.sharedMaterial = _beaconMaterial;
            return r;
        }

        private void Paint(Renderer r, Color baseColor, Color emission)
        {
            _block.Clear();
            _block.SetColor(BaseColorId, baseColor);
            _block.SetColor(EmissionColorId, emission);
            r.SetPropertyBlock(_block);
        }

        private void BuildSmoke(Room room)
        {
            if (_smokeMaterial == null)
                return;
            var b = room.Bounds;
            var go = new GameObject("Smoke");
            go.transform.SetParent(room.Alarm.transform, false);
            go.transform.position = new Vector3(b.center.x, b.min.y + 0.2f, b.center.z);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.0f, 2.0f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.36f, 0.36f, 0.38f, 1f);
            main.maxParticles = 160;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            var emission = ps.emission;
            emission.rateOverTime = _tuning.SmokePerCell * room.Module.Cells.Count;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(b.size.x * 0.55f, 0.1f, b.size.z * 0.55f);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.15f, 0.32f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.3f, 0.2f), new GradientAlphaKey(0.2f, 0.7f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _smokeMaterial;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
        }

        /// <summary>방 안 무작위 칸 가운데(높이 2.4)에서 수평으로 광선을 쏴 맞은 벽·장비 면에 스파크 + 순간 주황빛.</summary>
        private void Spark(Room room)
        {
            if (_sparkMaterial == null || room.Alarm == null)
                return;
            var cells = room.Module.Cells;
            var origin = _builder.FloorPoint(cells[Random.Range(0, cells.Count)]) + Vector3.up * 2.4f;
            if (Physics.CheckSphere(origin, 0.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return;
            float a = Random.Range(0f, Mathf.PI * 2f);
            var dir = new Vector3(Mathf.Cos(a), Random.Range(-0.1f, 0.35f), Mathf.Sin(a)).normalized;
            if (!Physics.Raycast(origin, dir, out var hit, InteriorGeometry.CellSize, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return;

            var go = new GameObject("Spark");
            go.transform.SetParent(room.Alarm.transform, false);
            go.transform.SetPositionAndRotation(hit.point + hit.normal * 0.04f, Quaternion.LookRotation(hit.normal));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.4f), new Color(1f, 0.55f, 0.2f));
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14, 26) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.02f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _sparkMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();

            var flash = go.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.shadows = LightShadows.None;
            flash.color = new Color(1f, 0.65f, 0.3f);
            flash.intensity = 5f;
            flash.range = 3f;
            Destroy(flash, 0.08f);

            if (room.Module == CurrentRoom)
                AudioService.TryPlay(l => l.StormSpark, 0.4f);
        }
    }
}
