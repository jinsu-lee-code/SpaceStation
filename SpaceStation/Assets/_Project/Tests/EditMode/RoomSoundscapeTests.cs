using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Audio;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 모듈 소리 풍경 (2026-11-26 2단계 소리 풍경, SOUND_LIST_NEW D-7): 가끔 소리가 나도 되는 때 (낮 · 밤 · 쇼) · 쇼 · 탈것 배율 · 등록된 소리 풍경 에셋이 다 채워졌는지
    /// (모듈 · 바탕 · 빈 클립 없음 · 음 있는 소리는 음계 칸 · 따라 붙는 물체 이름이 그 방 템플릿에 있는지).
    /// </summary>
    public class RoomSoundscapeTests
    {
        [Test]
        public void ScatterAllowed_DayNightAndShow()
        {
            Assert.IsTrue(RoomSoundscape.ScatterAllowed(RoomSoundscape.DayPart.Always, RoomSoundscape.ShowPart.Any, true, false));
            Assert.IsFalse(RoomSoundscape.ScatterAllowed(RoomSoundscape.DayPart.Day, RoomSoundscape.ShowPart.Any, false, false), "낮 새는 밤에 쉼");
            Assert.IsFalse(RoomSoundscape.ScatterAllowed(RoomSoundscape.DayPart.Night, RoomSoundscape.ShowPart.Any, true, false), "밤 개구리는 낮에 쉼");
            Assert.IsTrue(RoomSoundscape.ScatterAllowed(RoomSoundscape.DayPart.Night, RoomSoundscape.ShowPart.Any, false, true));
            Assert.IsFalse(RoomSoundscape.ScatterAllowed(RoomSoundscape.DayPart.Always, RoomSoundscape.ShowPart.During, true, false), "함성은 쇼 동안만");
            Assert.IsTrue(RoomSoundscape.ScatterAllowed(RoomSoundscape.DayPart.Always, RoomSoundscape.ShowPart.During, false, true));
            Assert.IsFalse(RoomSoundscape.ScatterAllowed(RoomSoundscape.DayPart.Always, RoomSoundscape.ShowPart.NotDuring, true, true), "상영 중 좌석 끄응 쉼");
            Assert.IsFalse(RoomSoundscape.ScatterAllowed(RoomSoundscape.DayPart.Day, RoomSoundscape.ShowPart.During, false, true), "낮 · 쇼 둘 다 맞아야");
        }

        [Test]
        public void StateGain_ShowAndRideBlendToTheirGains()
        {
            Assert.AreEqual(1f, RoomSoundscape.StateGain(0f, 0f, 0f, 0f), 1e-5f, "쇼 · 탈것이 아니면 그대로");
            Assert.AreEqual(0.4f, RoomSoundscape.StateGain(0.4f, 1f, 1f, 0f), 1e-5f, "상영 중 홀 바탕 0.4배");
            Assert.AreEqual(0.7f, RoomSoundscape.StateGain(0.4f, 1f, 0.5f, 0f), 1e-5f, "넘어가는 중간");
            Assert.AreEqual(0f, RoomSoundscape.StateGain(1f, 0f, 0f, 1f), 1e-5f, "탄 동안 열차 3D 달림은 끔 (탄 쪽 2D 루프가 따로)");
            Assert.AreEqual(0.12f, RoomSoundscape.StateGain(0.3f, 0.4f, 1f, 1f), 1e-5f, "둘이 겹치면 곱");
        }

        [Test]
        public void MotionSpeed_VerticalCountsOnlyUpDown()
        {
            // 2026-11-26 S-31 하역 체인: 크레인 집게가 옆으로 갈 땐 0, 오르내릴 때만
            var a = new Vector3(0f, 1f, 0f);
            Assert.AreEqual(0f, RoomSoundscape.MotionSpeed(a, a + new Vector3(0.6f, 0f, 0f), 1f, true), 1e-5f, "옆으로만 = 0");
            Assert.AreEqual(0.3f, RoomSoundscape.MotionSpeed(a, a + new Vector3(0.6f, -0.3f, 0f), 1f, true), 1e-5f, "위아래만 셈");
            Assert.AreEqual(0.5f, RoomSoundscape.MotionSpeed(a, a + new Vector3(0.3f, 0.4f, 0f), 1f, false), 1e-5f, "기본 = 모든 방향");
            Assert.AreEqual(0f, RoomSoundscape.MotionSpeed(a, a + Vector3.up, 0f, false), 1e-5f, "dt 0");
        }

        [Test]
        public void RegisteredSoundscapes_AreFilled()
        {
            var lib = AssetDatabase.LoadAssetAtPath<SoundLibrary>("Assets/_Project/Data/Audio/SoundLibrary.asset");
            Assert.IsNotNull(lib);
            var modules = new HashSet<Data.ModuleData>();
            int n = 0;
            foreach (var s in lib.Soundscapes)
            {
                Assert.IsNotNull(s, "빈 칸");
                Assert.IsNotNull(s.Module, s.name + " 모듈");
                Assert.IsTrue(modules.Add(s.Module), s.name + " 같은 모듈 소리 풍경이 둘");
                Assert.IsTrue(s.Bed != null && s.Bed.HasClip, s.name + " 바탕");
                foreach (var c in s.Bed.Clips) Assert.IsNotNull(c, s.name + " 바탕 빈 클립");
                var prefab = TemplatePrefab(s.Module);
                foreach (var e in s.Emitters)
                {
                    Assert.IsTrue(e.Cue != null && e.Cue.HasClip, s.name + " · " + e.Name);
                    foreach (var c in e.Cue.Clips) Assert.IsNotNull(c, s.name + " · " + e.Name + " 빈 클립");
                    Assert.LessOrEqual(e.MinDistance, e.MaxDistance, s.name + " · " + e.Name + " 거리");
                    if (!string.IsNullOrEmpty(e.Follow) && prefab != null)
                        Assert.IsTrue(Has(prefab.transform, e.Follow), s.name + " · " + e.Name + " 따라 붙을 물체 " + e.Follow + "가 템플릿에 없음");
                }
                foreach (var sc in s.Scatters)
                {
                    Assert.IsTrue(sc.Cue != null && sc.Cue.HasClip, s.name + " · " + sc.Name);
                    foreach (var c in sc.Cue.Clips) Assert.IsNotNull(c, s.name + " · " + sc.Name + " 빈 클립");
                    Assert.Greater(sc.Interval.x, 0f, s.name + " · " + sc.Name + " 간격");
                    Assert.LessOrEqual(sc.Interval.x, sc.Interval.y, s.name + " · " + sc.Name + " 간격");
                    if (sc.Tonal)
                    {
                        Assert.Greater(sc.Notes.Length, 0, s.name + " · " + sc.Name + " 음계 칸");
                        foreach (int m in sc.Notes)
                            Assert.LessOrEqual(Mathf.Abs(m - sc.BaseNote), 24, s.name + " · " + sc.Name + " 두 옥타브 넘게 옮김");
                    }
                }
                n++;
            }
            Assert.GreaterOrEqual(n, 8);
        }

        private static GameObject TemplatePrefab(Data.ModuleData module)
        {
            string name = module.name.Replace("MD_Evo_", "PF_Interior_Evo");
            return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Interior/" + name + ".prefab");
        }

        private static bool Has(Transform root, string name)
        {
            if (root.name == name)
                return true;
            foreach (Transform c in root)
                if (Has(c, name))
                    return true;
            return false;
        }
    }
}
