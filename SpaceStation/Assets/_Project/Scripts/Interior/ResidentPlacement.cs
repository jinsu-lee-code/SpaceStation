using System;
using System.Collections.Generic;
using SpaceStation.Data;

namespace SpaceStation.Interior
{
    /// <summary>11-11 배치 계산에 쓰는 방 하나 (내부에 있는 방만).</summary>
    public struct PlacementRoom
    {
        /// <summary>일하는 사람 자리 수.</summary>
        public int WorkSpots;
        /// <summary>사는 사람 · 쉬러 온 사람 자리 수.</summary>
        public int RestSpots;
        /// <summary>낮에 여기서 일하는 특성 (null이면 일터 아님).</summary>
        public IReadOnlyList<ResidentTrait> WorkTraits;
        /// <summary>낮에 일터가 없는 주민이 쉬러 오는 방.</summary>
        public bool Leisure;
        /// <summary>사람이 있을 수 있는 방 (코어와 연결 + 파손 아님). 아니면 아무도 두지 않는다.</summary>
        public bool Usable;
    }

    /// <summary>11-11 배치 계산에 쓰는 주민 하나.</summary>
    public struct PlacementPerson
    {
        public int Id;
        /// <summary>집인 방 번호 (내부에 없거나 집이 없으면 -1).</summary>
        public int HomeRoom;
        public IReadOnlyList<ResidentTrait> Traits;
    }

    /// <summary>배치 결과 하나: 사람(번호)이 방(번호)에 일하는 자리 / 쉬는 자리로.</summary>
    public struct Placement
    {
        public int Person;
        public int Room;
        public bool Work;
    }

    /// <summary>
    /// 11-11 주민 배치 규칙 (순수 계산, 테스트 대상). 걸어 다니는 AI 없이 방문할 때의 "그 순간" 위치만 정한다.
    /// - 밤: 모두 집.
    /// - 낮: 일터 특성이 맞는 방(빈 일하는 자리)이 있으면 일터. 없으면 일부(<paramref name="leisureShare"/>)는 여가 방, 나머지는 집.
    /// - 방이 쓸 수 없거나(비활성 · 파손) 자리가 없으면 다음 후보, 끝까지 없으면 보이지 않는다.
    /// - 같은 조건이면 언제 방문해도 같은 결과 (주민 번호로 고르는 결정적 규칙).
    /// </summary>
    public static class ResidentPlacementRules
    {
        public static void Plan(IReadOnlyList<PlacementPerson> people, IReadOnlyList<PlacementRoom> rooms, bool day, float leisureShare,
            List<Placement> result)
        {
            result.Clear();
            var workLeft = new int[rooms.Count];
            var restLeft = new int[rooms.Count];
            for (int i = 0; i < rooms.Count; i++)
            {
                workLeft[i] = rooms[i].Usable ? Math.Max(0, rooms[i].WorkSpots) : 0;
                restLeft[i] = rooms[i].Usable ? Math.Max(0, rooms[i].RestSpots) : 0;
            }

            // 번호 순서로 (먼저 온 주민이 먼저 자리를 잡음)
            var order = new List<int>(people.Count);
            for (int i = 0; i < people.Count; i++)
                order.Add(i);
            order.Sort((a, b) => people[a].Id.CompareTo(people[b].Id));

            var candidates = new List<int>();
            foreach (int p in order)
            {
                var person = people[p];
                if (day)
                {
                    // 일터
                    candidates.Clear();
                    for (int r = 0; r < rooms.Count; r++)
                    {
                        if (workLeft[r] > 0 && WorksIn(person, rooms[r]))
                            candidates.Add(r);
                    }
                    if (TryPick(person.Id, candidates, workLeft, out int work))
                    {
                        result.Add(new Placement { Person = p, Room = work, Work = true });
                        continue;
                    }
                    // 여가 방 (일터가 없는 사람 중 일부)
                    if (Share(person.Id) < leisureShare)
                    {
                        candidates.Clear();
                        for (int r = 0; r < rooms.Count; r++)
                        {
                            if (rooms[r].Leisure && restLeft[r] > 0 && r != person.HomeRoom)
                                candidates.Add(r);
                        }
                        if (TryPick(person.Id, candidates, restLeft, out int leisure))
                        {
                            result.Add(new Placement { Person = p, Room = leisure, Work = false });
                            continue;
                        }
                    }
                }
                // 집
                int home = person.HomeRoom;
                if (home >= 0 && home < rooms.Count && restLeft[home] > 0)
                {
                    restLeft[home]--;
                    result.Add(new Placement { Person = p, Room = home, Work = false });
                }
            }
        }

        public static bool WorksIn(PlacementPerson person, PlacementRoom room)
        {
            if (room.WorkTraits == null || person.Traits == null)
                return false;
            foreach (var t in person.Traits)
            {
                foreach (var w in room.WorkTraits)
                {
                    if (t == w)
                        return true;
                }
            }
            return false;
        }

        /// <summary>주민 번호로 정하는 0~1 값 (여가 방에 갈지).</summary>
        public static float Share(int id) => (uint)(id * 2654435761u) % 1000u / 1000f;

        /// <summary>후보 중 주민 번호로 하나 고름 (자리를 하나 씀).</summary>
        private static bool TryPick(int id, List<int> candidates, int[] left, out int room)
        {
            room = -1;
            if (candidates.Count == 0)
                return false;
            room = candidates[(int)((uint)id % (uint)candidates.Count)];
            left[room]--;
            return true;
        }
    }
}
