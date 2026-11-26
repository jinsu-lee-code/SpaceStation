using NUnit.Framework;
using SpaceStation.Audio;
using SpaceStation.Data;
using SpaceStation.Interior;

namespace SpaceStation.Tests
{
    /// <summary>주민 소리 (2026-11-26 사운드 개편 4-3 · 4-4 · D-2 · D-5 ④ · D-11): 종족 칸 · 반응 → 기분 · 웅성 단계 · 활동 갈래 · 목소리 반복 막기.</summary>
    public class ResidentSoundTests
    {
        [Test]
        public void SpeciesIndex_MatchesAnimalModelSetOrder()
        {
            Assert.AreEqual(8, ResidentSound.Species.Length);
            Assert.AreEqual(0, ResidentSound.SpeciesIndex("rabbit"));
            Assert.AreEqual(1, ResidentSound.SpeciesIndex("cat"));
            Assert.AreEqual(7, ResidentSound.SpeciesIndex("penguin"));
            Assert.AreEqual(-1, ResidentSound.SpeciesIndex("human"));
            Assert.AreEqual(-1, ResidentSound.SpeciesIndex(null));
            // 잠 숨: 큰 몸 · 중간 · 작은 몸
            Assert.AreEqual(0, ResidentSound.SleepSize("bear"));
            Assert.AreEqual(1, ResidentSound.SleepSize("dog"));
            Assert.AreEqual(2, ResidentSound.SleepSize("rabbit"));
        }

        [Test]
        public void MoodForReaction_PendingRequestAsks()
        {
            Assert.AreEqual(ResidentVoiceMood.Happy, ResidentSound.MoodForReaction(ResidentReaction.HopWave, false));
            Assert.AreEqual(ResidentVoiceMood.Happy, ResidentSound.MoodForReaction(ResidentReaction.Wave, false));
            Assert.AreEqual(ResidentVoiceMood.Upset, ResidentSound.MoodForReaction(ResidentReaction.TurnAway, false));
            Assert.IsNull(ResidentSound.MoodForReaction(ResidentReaction.Look, false));   // 바라보기만 = 소리 없음
            Assert.AreEqual(ResidentVoiceMood.Ask, ResidentSound.MoodForReaction(ResidentReaction.TurnAway, true));
            Assert.IsNull(ResidentSound.MoodForReaction(ResidentReaction.Look, true));
        }

        [Test]
        public void CrowdLevel_RisesWithAwakeResidents()
        {
            Assert.AreEqual(0f, ResidentSound.CrowdLevel(0), 1e-6f);
            Assert.AreEqual(0f, ResidentSound.CrowdLevel(1), 1e-6f);
            Assert.AreEqual(1f, ResidentSound.CrowdLevel(3), 1e-6f);
            Assert.AreEqual(2f, ResidentSound.CrowdLevel(8), 1e-6f);
            Assert.AreEqual(3f, ResidentSound.CrowdLevel(16), 1e-6f);
            Assert.AreEqual(3f, ResidentSound.CrowdLevel(40), 1e-6f);
            for (int n = 1; n < 20; n++)
                Assert.GreaterOrEqual(ResidentSound.CrowdLevel(n + 1), ResidentSound.CrowdLevel(n));
        }

        [Test]
        public void CrowdMix_EqualPowerBetweenNeighbours()
        {
            var w = new float[3];
            ResidentSound.CrowdMix(0f, w);
            Assert.AreEqual(0f, w[0] + w[1] + w[2], 1e-6f);
            ResidentSound.CrowdMix(1f, w);
            Assert.AreEqual(1f, w[0], 1e-5f);
            Assert.AreEqual(0f, w[1], 1e-5f);
            ResidentSound.CrowdMix(2f, w);
            Assert.AreEqual(1f, w[1], 1e-5f);
            Assert.AreEqual(0f, w[0], 1e-5f);
            ResidentSound.CrowdMix(3f, w);
            Assert.AreEqual(1f, w[2], 1e-5f);
            for (float l = 1.05f; l < 3f; l += 0.1f)
            {
                ResidentSound.CrowdMix(l, w);
                Assert.AreEqual(1f, w[0] * w[0] + w[1] * w[1] + w[2] * w[2], 1e-4f, $"level {l}");   // 같은 힘
                Assert.IsTrue(w[0] == 0f || w[2] == 0f, $"level {l}: 이웃 단계 둘만");
            }
        }

        [Test]
        public void ActivityOf_RoomPoseAndNight()
        {
            // 밤 · 집 = 잠 (앉거나 누운 주민만)
            Assert.AreEqual(ResidentActivity.Sleep, ResidentSound.ActivityOf("MD_Habitat", InteriorAmbience.Life, false, true, true, true));
            Assert.AreEqual(ResidentActivity.None, ResidentSound.ActivityOf("MD_Habitat", InteriorAmbience.Life, false, false, true, true));
            // 일하는 자리 = 키보드, 기계 · 고열 · 물 방 = 공구
            Assert.AreEqual(ResidentActivity.Work, ResidentSound.ActivityOf("MD_DataCenter", InteriorAmbience.Electric, true, false, false, false));
            Assert.AreEqual(ResidentActivity.WorkTool, ResidentSound.ActivityOf("MD_Refinery", InteriorAmbience.Heat, true, false, false, false));
            Assert.AreEqual(ResidentActivity.WorkTool, ResidentSound.ActivityOf("MD_Farm", InteriorAmbience.Water, true, false, false, false));
            // 오락실 = 놀이 · 식탁 있는 방에 앉음 (낮) = 그릇 · 밤에는 그릇 없음
            Assert.AreEqual(ResidentActivity.Play, ResidentSound.ActivityOf("MD_Recreation", InteriorAmbience.Life, false, true, false, false));
            Assert.AreEqual(ResidentActivity.Eat, ResidentSound.ActivityOf("MD_Commerce", InteriorAmbience.Life, false, true, false, false));
            Assert.AreEqual(ResidentActivity.None, ResidentSound.ActivityOf("MD_Commerce", InteriorAmbience.Life, false, true, false, true));
            Assert.AreEqual(ResidentActivity.None, ResidentSound.ActivityOf("MD_Commerce", InteriorAmbience.Life, false, false, false, false));
            Assert.AreEqual(ResidentActivity.None, ResidentSound.ActivityOf("MD_Core", InteriorAmbience.Hall, false, true, false, false));
            Assert.IsTrue(ResidentSound.IsPlayRoom("MD_Evo_CultureCenter"));
            Assert.IsFalse(ResidentSound.IsPlayRoom(null));
        }

        [Test]
        public void VoiceGate_PerResident20sRoomTwoAndSpacing()
        {
            var g = new ResidentVoiceGate();
            Assert.IsTrue(g.TryAdmit(1, 10f, 1.5f));
            Assert.IsFalse(g.TryAdmit(2, 10.2f, 1.5f));      // 시작끼리 0.45초 안
            Assert.IsTrue(g.TryAdmit(2, 10.5f, 1.5f));
            Assert.IsFalse(g.TryAdmit(3, 11.0f, 0.5f));      // 방 동시 2 (1 · 2가 울리는 중)
            Assert.IsTrue(g.TryAdmit(3, 11.6f, 0.5f));       // 1이 끝남 (11.5초)
            Assert.IsFalse(g.TryAdmit(1, 25f, 0.5f));        // 같은 주민 20초 안
            Assert.IsTrue(g.TryAdmit(1, 30.1f, 0.5f));
            Assert.IsTrue(g.TryAdmit(1, 31f, 0.5f, reply: true));   // 부탁 · 완료 대답은 주민 간격을 건너뜀
            Assert.IsFalse(g.TryAdmit(2, 31.2f, 0.5f, reply: true)); // 시작 간격은 지킴
            g.ClearRoom();
            Assert.IsFalse(g.TryAdmit(1, 32f, 0.5f));        // 방이 바뀌어도 같은 주민 간격은 남음
            Assert.IsTrue(g.TryAdmit(4, 32f, 0.5f));
        }
    }
}
