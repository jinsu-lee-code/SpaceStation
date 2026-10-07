using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Interior;

namespace SpaceStation.Tests
{
    /// <summary>11-11 주민 배치 규칙 (낮 = 일터 · 여가, 밤 = 집).</summary>
    public class ResidentPlacementTests
    {
        private const int Home = 0, Lab = 1, Lounge = 2;

        private static List<PlacementRoom> Rooms(int labWork = 2, bool labUsable = true, int homeRest = 6)
        {
            return new List<PlacementRoom>
            {
                new PlacementRoom { RestSpots = homeRest, Usable = true },                                                      // 거주
                new PlacementRoom { WorkSpots = labWork, RestSpots = 0, WorkTraits = new[] { ResidentTrait.Scientist }, Usable = labUsable }, // 연구소
                new PlacementRoom { RestSpots = 4, Leisure = true, Usable = true },                                             // 휴게실
            };
        }

        private static PlacementPerson Person(int id, params ResidentTrait[] traits)
            => new PlacementPerson { Id = id, HomeRoom = Home, Traits = traits };

        private static Dictionary<int, Placement> Run(List<PlacementPerson> people, List<PlacementRoom> rooms, bool day, float share)
        {
            var result = new List<Placement>();
            ResidentPlacementRules.Plan(people, rooms, day, share, result);
            var byPerson = new Dictionary<int, Placement>();
            foreach (var p in result)
                byPerson[p.Person] = p;
            return byPerson;
        }

        [Test]
        public void Night_EveryoneHome()
        {
            var people = new List<PlacementPerson> { Person(1, ResidentTrait.Scientist), Person(2, ResidentTrait.Optimist) };
            var r = Run(people, Rooms(), day: false, share: 1f);
            Assert.AreEqual(2, r.Count);
            Assert.AreEqual(Home, r[0].Room);
            Assert.AreEqual(Home, r[1].Room);
            Assert.IsFalse(r[0].Work);
        }

        [Test]
        public void Day_WorkTraitGoesToWorkplace_OthersByShare()
        {
            var people = new List<PlacementPerson> { Person(1, ResidentTrait.Scientist), Person(2, ResidentTrait.Optimist) };
            var allLeisure = Run(people, Rooms(), day: true, share: 1f);
            Assert.AreEqual(Lab, allLeisure[0].Room);
            Assert.IsTrue(allLeisure[0].Work);
            Assert.AreEqual(Lounge, allLeisure[1].Room, "일터가 없으면 여가 방");

            var noLeisure = Run(people, Rooms(), day: true, share: 0f);
            Assert.AreEqual(Home, noLeisure[1].Room, "여가 비율 0이면 집");
        }

        [Test]
        public void Day_WorkplaceFull_OrUnusable_FallsBack()
        {
            var people = new List<PlacementPerson>
            {
                Person(1, ResidentTrait.Scientist), Person(2, ResidentTrait.Scientist), Person(3, ResidentTrait.Scientist),
            };
            var full = Run(people, Rooms(labWork: 2), day: true, share: 0f);
            int atLab = 0;
            foreach (var p in full.Values)
                if (p.Room == Lab)
                    atLab++;
            Assert.AreEqual(2, atLab, "자리 수만큼만");
            Assert.AreEqual(Home, full[2].Room, "늦게 온 주민은 집으로");

            var damaged = Run(people, Rooms(labUsable: false), day: true, share: 0f);
            foreach (var p in damaged.Values)
                Assert.AreEqual(Home, p.Room, "쓸 수 없는 일터(파손 · 비활성)에는 아무도 없음");
        }

        [Test]
        public void HomeFull_ExtraResidentsNotShown()
        {
            var people = new List<PlacementPerson> { Person(1), Person(2), Person(3) };
            var r = Run(people, Rooms(homeRest: 2), day: false, share: 0f);
            Assert.AreEqual(2, r.Count);
        }

        [Test]
        public void Homeless_OnlyAppearsAtWorkOrLeisure()
        {
            var people = new List<PlacementPerson> { new PlacementPerson { Id = 1, HomeRoom = -1, Traits = new[] { ResidentTrait.Optimist } } };
            Assert.AreEqual(0, Run(people, Rooms(), day: false, share: 1f).Count);
            Assert.AreEqual(Lounge, Run(people, Rooms(), day: true, share: 1f)[0].Room);
        }

        [Test]
        public void SameInput_SameResult()
        {
            var people = new List<PlacementPerson>();
            for (int i = 1; i <= 8; i++)
                people.Add(Person(i, i % 2 == 0 ? ResidentTrait.Scientist : ResidentTrait.Optimist));
            var a = Run(people, Rooms(), day: true, share: 0.5f);
            var b = Run(people, Rooms(), day: true, share: 0.5f);
            Assert.AreEqual(a.Count, b.Count);
            foreach (var pair in a)
                Assert.AreEqual(pair.Value.Room, b[pair.Key].Room);
        }
    }
}
