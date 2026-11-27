using System;
using SpaceStation.Audio;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// CONNECT_1121 A6 1단계 (미래 기술 · 닫힌 방) 상황 (사용자 결정 2026-11-28):
    /// ① 홀로 패널 오류 — 벽 홀로 판 6칸(2 × 3) 중 2 ~ 3칸에 경고 (밝게 깜빡 + 어두운 '!') → 경고 칸만 F, 틀리면 지직 후 처음부터 (차단기 규칙)
    /// ② 냉각 코어 과열 — 벽 냉각 장치의 유리 카트리지가 뜨겁게 맥동 · 김 · 온도 막대 가득 · 경고등 깜빡 → F 길게 한 번 (뽑았다 끼움, 필터 규칙)
    /// ③ 정비 로봇 멈춤 — 바닥에서 올라온 로봇 팔이 축 늘어져 머리 등이 깜빡 · 가끔 팔꿈치 스파크 → F 길게 → 일어나 잠깐 일하고 접혀 내려감
    /// 모델 = BlenderWork/InteriorProps/stage_incident_builder.py (IncHoloPanel · IncCoolantCore · IncRobotArm). 발광 칸 = <see cref="Settings.Glow"/>.
    /// 상태는 색만이 아니라 밝기 · 깜빡임 · '!' · 막대 높이 · 김으로도 읽힘 (사용자 색약). 소리는 기존 것을 빌린 자리 (새 소리는 사운드 작업 때).
    /// </summary>
    public sealed partial class InteriorIncidents
    {
        /// <summary>확인용: 마지막 1단계 상황 자리 찾기 결과.</summary>
        public static string LastTechNote { get; private set; }

        private const string GlowSlot = "M_IncGlow";
        private static readonly Color HoloOk = new Color(0.3f, 0.8f, 1f);
        private static readonly Color HoloWarn = new Color(1f, 0.6f, 0.18f);
        private static readonly Color CoreHot = new Color(1f, 0.42f, 0.12f);
        private static readonly Color CoreCool = new Color(0.35f, 0.85f, 1f);
        private static readonly Color BotLampStall = new Color(1f, 0.55f, 0.15f);
        private static readonly Color BotLampOn = new Color(0.35f, 0.9f, 1f);
        private const float CoolHotGauge = 1f, CoolDoneGauge = 0.3f;
        private static readonly Vector3 BotHalf = new Vector3(0.62f, 0.7f, 0.62f);
        // 로봇 팔 자세 (도): 회전대 (세로축) · 어깨 · 팔꿈치 · 손목 (앞으로 숙임 = +) — 멈춤 = 미리보기 stage_robotarm_stall과 같은 각도
        private static readonly float[] BotStall = { 20f, 40f, 80f, 35f };
        private static readonly float[] BotWork = { 0f, 25f, 55f, 30f };
        private static readonly float[] BotStow = { 0f, 0f, 0f, 0f };
        private static readonly string[] BotJoints = { "ArmTurret", "ArmUpper", "ArmFore", "ArmHead" };
        private const float BotWorkTime = 1.1f, BotFoldTime = 0.55f;

        private sealed partial class Incident
        {
            public readonly Renderer[] Cells = new Renderer[InteriorIncidentRules.Switches];
            public readonly GameObject[] CellWarn = new GameObject[InteriorIncidentRules.Switches];
            public float GlitchUntil;
            /// <summary>홀로 판 아래 막대 발광 줄 · 냉각 온도 막대 · 로봇 머리 등.</summary>
            public Renderer Emitter;
            public Renderer CoreGlass;
            public Transform Gauge;
            public float GaugeLevel = 1f;
            public readonly Transform[] Joints = new Transform[4];
            public readonly Quaternion[] JointRest = new Quaternion[4];
            /// <summary>관절 로컬 축 (놓을 때 월드에서 바꿔 둠 — FBX 뿌리가 90° 돌아 있어 로컬 위 = −Z): 회전대 = 소품 위, 나머지 = 소품 오른쪽 (+ = 앞으로 숙임).</summary>
            public readonly Vector3[] JointAxis = new Vector3[4];
            public readonly float[] Pose = new float[4];
            public float NextSpark;
            public ObjectLoop Servo;
        }

        private static bool IsTech(IncidentKind kind) =>
            kind == IncidentKind.HoloPanel || kind == IncidentKind.CoolantCore || kind == IncidentKind.MaintBot;

        // ---------------- 디스패치 (InteriorIncidents.Stage.cs가 부름) ----------------

        private bool PlaceTech(Incident a)
        {
            switch (a.Kind)
            {
                case IncidentKind.HoloPanel: return PlaceHoloPanel(a);
                case IncidentKind.CoolantCore: return PlaceCoolant(a);
                case IncidentKind.MaintBot: return PlaceRobotArm(a);
                default: return false;
            }
        }

        private void AnimateTech(Incident a, float now, bool holding, bool done)
        {
            switch (a.Kind)
            {
                case IncidentKind.HoloPanel:
                    ShowCells(a, now);
                    break;
                case IncidentKind.CoolantCore:
                    AnimateCoolant(a, now, holding, done);
                    break;
                case IncidentKind.MaintBot:
                    AnimateRobotArm(a, now, holding, done);
                    break;
            }
        }

        private void ResolveTech(Incident a)
        {
            switch (a.Kind)
            {
                case IncidentKind.HoloPanel:
                    ShowCells(a, Time.unscaledTime);
                    break;
                case IncidentKind.CoolantCore:
                    StopFx(a.Plume);
                    a.Plume = null;
                    if (a.Cartridge != null)
                        a.Cartridge.localPosition = a.CartridgeRest;
                    break;
            }
        }

        /// <summary>정리: 해결한 로봇 팔은 내려간 뒤 지움 (벽 소품은 고친 모습으로 남김 — 기본 사고와 같음, 놓쳤으면 End가 지움).</summary>
        private void RestoreTech(Incident a)
        {
            if (a.Servo != null)
                a.Servo.Level = 0f;
            if (a.Kind == IncidentKind.MaintBot && a.Done && a.Prop != null)
                Destroy(a.Prop);
        }

        // ---------------- 발광 유리 ----------------

        /// <summary>M_IncGlow 칸을 발광 반투명 재질로 바꿔 끼움 (이 소품 렌더러만 — 물들이기는 블록).</summary>
        private void UseGlow(Renderer r)
        {
            if (r == null || _s.Glow == null)
                return;
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && mats[i] != _s.Glow && mats[i].name.StartsWith(GlowSlot, StringComparison.Ordinal))
                {
                    mats[i] = _s.Glow;
                    changed = true;
                }
            }
            if (changed)
                r.sharedMaterials = mats;
        }

        /// <summary>발광 칸 물들이기: 바탕 = 색 (반투명 alpha), 발광 = 색 × 세기.</summary>
        private void Glow(Renderer r, Color color, float intensity, float alpha)
        {
            if (r == null)
                return;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || !mats[i].name.StartsWith(GlowSlot, StringComparison.Ordinal))
                    continue;
                _block.Clear();
                _block.SetColor("_BaseColor", new Color(color.r, color.g, color.b, alpha));
                _block.SetColor(EmissionColorId, color * intensity);
                r.SetPropertyBlock(_block, i);
            }
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name)
                return t;
            foreach (Transform c in t)
            {
                var found = FindDeep(c, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        // ---------------- ① 홀로 패널 오류 ----------------

        private bool PlaceHoloPanel(Incident a)
        {
            a.Prop = Mount(_s.HoloPanel, a.Room, _s.HoloHeight, "IncidentHoloPanel");
            if (a.Prop == null)
            {
                LastTechNote = "홀로 패널: 벽 자리 없음";
                return false;
            }
            LastTechNote = "홀로 패널: 놓음";
            var t = a.Prop.transform;
            var body = t.Find("IncHoloPanel");
            a.Emitter = body != null ? body.GetComponent<Renderer>() : null;
            UseGlow(a.Emitter);
            Glow(a.Emitter, HoloOk, 1.4f, 0.8f);
            a.Pattern = InteriorIncidentRules.TripPattern(_rng);
            a.Lit = (bool[])a.Pattern.Clone();
            for (int i = 0; i < InteriorIncidentRules.Switches; i++)
            {
                var cell = t.Find("Cell" + i);
                var warn = t.Find("Warn" + i);
                a.CellWarn[i] = warn != null ? warn.gameObject : null;
                if (cell == null)
                    continue;
                a.Cells[i] = cell.GetComponent<Renderer>();
                UseGlow(a.Cells[i]);
                var col = cell.gameObject.AddComponent<BoxCollider>();
                col.size = new Vector3(0.17f, 0.17f, 0.05f);
                int index = i;
                var use = cell.gameObject.AddComponent<InteriorInteractable>();
                use.HoldSeconds = _s.SwitchHold;
                use.Prompt = () => a.Lit[index] ? "경고 칸 누르기  <color=#FF8A5C>경고 켜짐</color>" : "칸  <color=#AFC4D8>정상 (건드리지 마세요)</color>";
                use.Completed = () => PressCell(a, index);
            }
            ShowCells(a, Time.unscaledTime);
            a.Loop = AudioService.AttachLoop(l => l.PadHoloLoop, t);   // 임시 소리: 홀로 판 웅
            if (a.Loop != null)
                a.Loop.Level = 0f;
            AudioService.TryPlayAt(l => l.StormSpark, t.position);   // 오류 순간 지직 (차단기와 같은 빌린 소리)
            return true;
        }

        /// <summary>경고 칸 = 밝은 주황 깜빡 + '!' (밝기 · 깜빡임 · 모양으로 구분), 정상 칸 = 옅은 청록 그대로, 틀린 순간 = 모두 하얗게 번쩍.</summary>
        private void ShowCells(Incident a, float now)
        {
            bool blink = Mathf.Repeat(now, 0.7f) < 0.42f;
            bool glitch = now < a.GlitchUntil;
            for (int i = 0; i < InteriorIncidentRules.Switches; i++)
            {
                bool warn = !a.Done && a.Lit != null && a.Lit[i];
                if (a.CellWarn[i] != null && a.CellWarn[i].activeSelf != warn)
                    a.CellWarn[i].SetActive(warn);
                if (a.Cells[i] == null)
                    continue;
                float flick = 0.85f + 0.15f * Mathf.PerlinNoise(now * 9f, i * 1.7f);   // 홀로 일렁임
                if (glitch)
                    Glow(a.Cells[i], Color.white, 3f * flick, 0.75f);
                else if (warn)
                    Glow(a.Cells[i], HoloWarn, (blink ? 2.4f : 0.8f) * flick, blink ? 0.85f : 0.6f);
                else
                    Glow(a.Cells[i], HoloOk, 0.6f * flick, 0.4f);
            }
        }

        private void PressCell(Incident a, int index)
        {
            if (a.Done)
                return;
            var result = InteriorIncidentRules.Press(a.Lit, index);
            var at = a.Cells[index] != null ? a.Cells[index].transform.position : a.Prop.transform.position;
            if (result == SwitchPress.Wrong)
            {
                // 지직: 아래 투사 막대에서 스파크 (빛판은 빛이라 원인 = 막대) · 칸이 모두 번쩍 · 처음 상태로
                InteriorFx.Spark(transform, a.Prop.transform.TransformPoint(new Vector3(0f, -0.2f, 0.07f)), Vector3.up, _spark, 8, 14, 0.8f);
                AudioService.TryPlay(l => l.UiError);
                AudioService.TryPlayAt(l => l.StormSpark, at);
                Message?.Invoke("틀렸어요! 지직 · 처음 상태로 돌아갔어요 (경고 칸만)", true);
                Array.Copy(a.Pattern, a.Lit, a.Lit.Length);
                a.GlitchUntil = Time.unscaledTime + 0.35f;
                return;
            }
            AudioService.TryPlayAt(l => l.LiftButton1, at);   // 임시 소리: 1단계 승강기 버튼 (홀로 칸 누름)
            if (result == SwitchPress.AllDone)
                Resolve(a);
        }

        // ---------------- ② 냉각 코어 과열 ----------------

        private bool PlaceCoolant(Incident a)
        {
            a.Prop = Mount(_s.CoolantCore, a.Room, _s.CoolantHeight, "IncidentCoolantCore");
            if (a.Prop == null)
            {
                LastTechNote = "냉각 코어: 벽 자리 없음";
                return false;
            }
            LastTechNote = "냉각 코어: 놓음";
            var t = a.Prop.transform;
            a.Cartridge = t.Find("Cartridge");
            if (a.Cartridge != null)
            {
                a.CartridgeRest = a.Cartridge.localPosition;
                a.CoreGlass = a.Cartridge.GetComponent<Renderer>();
                UseGlow(a.CoreGlass);
            }
            a.Gauge = t.Find("GaugeFill");
            a.Emitter = a.Gauge != null ? a.Gauge.GetComponent<Renderer>() : null;
            UseGlow(a.Emitter);
            var led = t.Find("CoolLed");
            a.FilterLed = led != null ? led.GetComponent<Renderer>() : null;
            a.GaugeLevel = CoolHotGauge;
            // 뜨거운 카트리지 위 뚜껑에서 김 (원인 = 카트리지)
            a.Plume = InteriorFx.Plume(t, t.TransformPoint(new Vector3(0f, 0.25f, 0.17f)), Vector3.up + t.forward * 0.35f, _smoke,
                new Color(0.92f, 0.94f, 0.96f, 0.5f), 9f, 0.09f, 0.45f);
            a.Loop = AudioService.AttachLoop(l => l.StateBoilLoop, t);   // 임시 소리: 끓는 소리
            if (a.Loop != null)
                a.Loop.Level = 0f;
            a.Held = AudioService.AttachHeld(l => l.FilterSwap, a.Cartridge != null ? a.Cartridge : t);   // 필터와 같은 뽑았다 끼우는 소리
            var col = a.Prop.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, 0.1f);
            col.size = new Vector3(0.5f, 0.64f, 0.2f);
            var use = a.Prop.AddComponent<InteriorInteractable>();
            use.HoldSeconds = _s.FilterHold;
            use.Prompt = () => $"냉각 카트리지 교체  <color=#FF8A5C>{InteriorIncidentRules.Title(a.Kind)}</color>";
            use.Holding = p =>
            {
                a.Holding = true;
                a.HoldProgress = p;
            };
            use.Completed = () => Resolve(a);
            AnimateCoolant(a, Time.unscaledTime, false, false);
            return true;
        }

        /// <summary>뜨거움 = 주황 맥동 · 막대 가득(떨림) · 경고등 깜빡 · 김, 누르는 동안 카트리지가 뽑혔다 들어감 (반 넘으면 새것 = 청록), 고침 = 막대가 내려감 · 초록 등.</summary>
        private void AnimateCoolant(Incident a, float now, bool holding, bool done)
        {
            float p = holding ? a.HoldProgress : 0f;
            bool cool = done || (holding && p > 0.5f);
            if (a.Cartridge != null && !done)
                a.Cartridge.localPosition = a.CartridgeRest + Vector3.forward * (0.17f * Mathf.Sin(p * Mathf.PI));
            if (cool)
                Glow(a.CoreGlass, CoreCool, 1.1f, 0.5f);
            else
                Glow(a.CoreGlass, CoreHot, 1.2f + 1.4f * (0.5f + 0.5f * Mathf.Sin(now * 5f)), 0.6f);
            a.GaugeLevel = Mathf.MoveTowards(a.GaugeLevel, cool ? CoolDoneGauge : CoolHotGauge, Time.unscaledDeltaTime / 0.8f);
            float shake = cool ? 0f : 0.03f * Mathf.Sin(now * 13f);
            if (a.Gauge != null)
                a.Gauge.localScale = new Vector3(1f, Mathf.Clamp(a.GaugeLevel + shake, 0.05f, 1f), 1f);
            Glow(a.Emitter, cool ? CoreCool : CoreHot, cool ? 1f : 1.8f, 0.75f);
            if (a.FilterLed != null)
                TintRenderer(a.FilterLed, "M_IncLed", cool ? LedGreen : (Mathf.Repeat(now, 0.8f) < 0.4f ? LedRed : new Color(0.25f, 0.05f, 0.04f)));
            if (!done)
                SetRate(a.Plume, 9f * (holding ? 1f - p : 1f));
        }

        // ---------------- ③ 정비 로봇 멈춤 ----------------

        private bool PlaceRobotArm(Incident a)
        {
            var parent = _builder.RoomParent(a.Room);
            if (_s.RobotArm == null || parent == null)
                return false;
            if (!PropSpot(a, BotHalf, 0.42f, 0.9f, null, out var spot, out var face))
            {
                LastTechNote = "정비 로봇: 바닥 자리 없음";
                return false;
            }
            LastTechNote = "정비 로봇: 놓음";
            // 관절 계층 FBX는 뿌리가 90° 돌아 있음 (Unity bakeAxisConversion) → 빈 받침 아래에 넣어 그 회전을 지킴 (RaiseProp은 뿌리 회전을 덮어씀)
            var go = new GameObject("IncidentRobotArm");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(face, Vector3.up));
            var model = Instantiate(_s.RobotArm, go.transform, false);
            model.name = "RobotArmModel";
            a.Prop = go;
            a.ConsoleGround = spot;
            SetConsoleRise(a, 0f);
            var bubbleAt = new GameObject("BubbleAt").transform;
            bubbleAt.SetParent(go.transform, false);
            bubbleAt.localPosition = new Vector3(0f, 1.3f, 0f);
            a.BubbleAt = bubbleAt;
            for (int j = 0; j < BotJoints.Length; j++)
            {
                a.Joints[j] = FindDeep(model.transform, BotJoints[j]);
                if (a.Joints[j] == null)
                    continue;
                a.JointRest[j] = a.Joints[j].localRotation;
                a.JointAxis[j] = Quaternion.Inverse(a.Joints[j].rotation) * (j == 0 ? go.transform.up : go.transform.right);
            }
            var head = a.Joints[3];
            a.Emitter = head != null ? head.GetComponent<Renderer>() : null;
            UseGlow(a.Emitter);
            Array.Copy(BotStall, a.Pose, a.Pose.Length);
            ApplyBotPose(a);
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.42f, 0.12f);
            col.size = new Vector3(0.66f, 0.84f, 0.84f);
            var use = go.AddComponent<InteriorInteractable>();
            use.HoldSeconds = _s.BotHold;
            use.Prompt = () => $"정비 로봇 다시 켜기  <color=#FF8A5C>{InteriorIncidentRules.Title(a.Kind)}</color>";
            use.Holding = p =>
            {
                a.Holding = true;
                a.HoldProgress = p;
            };
            use.Completed = () => Resolve(a);
            a.NextSpark = Time.unscaledTime + 1.2f;
            var elbow = a.Joints[2] != null ? a.Joints[2] : go.transform;
            a.Loop = AudioService.AttachLoop(l => l.StateSparkLoop, elbow);   // 임시 소리: 지직거림 (멈춘 동안)
            if (a.Loop != null)
                a.Loop.Level = 0f;
            a.Held = AudioService.AttachHeld(l => l.RepairStart, head != null ? head : go.transform);   // 임시 소리: 다시 켜는 동안
            a.Servo = AudioService.AttachLoop(l => l.T1TurretServoLoop, a.Joints[0] != null ? a.Joints[0] : go.transform);   // 임시 소리: 다시 움직일 때
            if (a.Servo != null)
                a.Servo.Level = 0f;
            AudioService.TryPlayAt(l => l.StormSpark, spot + Vector3.up * 0.6f);
            return true;
        }

        private static void ApplyBotPose(Incident a)
        {
            for (int j = 0; j < a.Joints.Length; j++)
            {
                if (a.Joints[j] != null)
                    a.Joints[j].localRotation = a.JointRest[j] * Quaternion.AngleAxis(a.Pose[j], a.JointAxis[j]);
            }
        }

        private static void PoseToward(Incident a, float[] target, float degPerSecond, float dt)
        {
            for (int j = 0; j < a.Pose.Length; j++)
                a.Pose[j] = Mathf.MoveTowards(a.Pose[j], target[j], degPerSecond * dt);
        }

        private readonly float[] _botTarget = new float[4];

        /// <summary>
        /// 멈춤: 올라오며 축 늘어짐 · 머리 등 주황 깜빡 · 2 ~ 4초마다 팔꿈치 스파크. 누르는 동안: 떨며 조금 들림 · 머리 등이 청록으로 차오름 (떼면 다시 늘어짐).
        /// 해결 뒤: 일어나 좌우로 살피며 일함(1.1초) → 곧게 접힘(0.55초) → 바닥으로 내려감.
        /// </summary>
        private void AnimateRobotArm(Incident a, float now, bool holding, bool done)
        {
            float dt = Time.unscaledDeltaTime;
            if (!done)
            {
                if (a.ConsoleRise < 1f)
                    SetConsoleRise(a, a.ConsoleRise + dt / ConsoleRiseTime);
                float p = holding ? a.HoldProgress : 0f;
                for (int j = 0; j < 4; j++)
                    _botTarget[j] = Mathf.Lerp(BotStall[j], BotWork[j], 0.35f * p) + (holding && j > 0 ? 1.5f * Mathf.Sin(now * 40f + j) : 0f);
                PoseToward(a, _botTarget, holding ? 120f : 60f, dt);
                ApplyBotPose(a);
                if (holding)
                    Glow(a.Emitter, BotLampOn, 0.4f + 2.2f * p, 0.85f);
                else
                    Glow(a.Emitter, BotLampStall, Mathf.Repeat(now, 0.9f) < 0.45f ? 2.2f : 0.25f, 0.85f);
                if (!holding && now >= a.NextSpark && a.Joints[2] != null)
                {
                    a.NextSpark = now + 2.2f + (float)_rng.NextDouble() * 1.8f;
                    InteriorFx.Spark(transform, a.Joints[2].position, Vector3.up, _spark, 6, 12, 0.5f);
                }
                return;
            }
            float t = a.Age - a.DoneAge;
            Glow(a.Emitter, BotLampOn, 2.2f, 0.85f);
            if (t < BotWorkTime)
            {
                // 일함: 좌우로 살피고 머리를 끄덕임
                _botTarget[0] = 25f * Mathf.Sin(t * 5.5f);
                _botTarget[1] = BotWork[1];
                _botTarget[2] = BotWork[2] + 8f * Mathf.Sin(t * 7f);
                _botTarget[3] = BotWork[3] + 12f * Mathf.Sin(t * 9f);
                PoseToward(a, _botTarget, 160f, dt);
                if (a.Servo != null)
                    a.Servo.Level = 1f;
            }
            else if (t < BotWorkTime + BotFoldTime)
            {
                PoseToward(a, BotStow, 200f, dt);
                if (a.Servo != null)
                    a.Servo.Level = 1f;
            }
            else
            {
                PoseToward(a, BotStow, 200f, dt);
                if (a.Servo != null)
                    a.Servo.Level = 0f;
                SetConsoleRise(a, a.ConsoleRise - dt / ConsoleRiseTime);
            }
            ApplyBotPose(a);
        }
    }
}
