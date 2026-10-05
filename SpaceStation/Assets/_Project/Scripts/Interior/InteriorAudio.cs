using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-9 내부 방문 소리: 지금 있는 방의 환경음(성격별 6묶음 루프, 방을 옮기면 교차 페이드) + 발소리.
    /// 루프는 처음 들어갈 때 한 번 만들어 두고(AudioService 루프는 지울 수 없음), 나오면 모두 0으로 내린다.
    /// <see cref="InteriorMode"/>가 방이 바뀔 때 <see cref="SetRoom"/>, 나올 때 <see cref="Stop"/>를 부른다.
    /// </summary>
    public sealed class InteriorAudio
    {
        private const float FadeSpeed = 0.7f;   // 초당 (1.4초에 0 → 1)
        private const float WalkVolume = 0.75f;

        private readonly Dictionary<InteriorAmbience, AudioService.Loop> _loops = new Dictionary<InteriorAmbience, AudioService.Loop>();
        private FirstPersonController _player;

        public void Attach(FirstPersonController player)
        {
            Detach();
            _player = player;
            if (_player != null)
                _player.Footstep += HandleFootstep;
        }

        public void Detach()
        {
            if (_player != null)
                _player.Footstep -= HandleFootstep;
            _player = null;
        }

        /// <summary>이 묶음만 올리고 나머지는 내림.</summary>
        public void SetRoom(InteriorAmbience ambience)
        {
            if (!EnsureLoops())
                return;
            foreach (var pair in _loops)
                pair.Value.Target = pair.Key == ambience ? 1f : 0f;
        }

        public void Stop()
        {
            foreach (var loop in _loops.Values)
                loop.Target = 0f;
            Detach();
        }

        private bool EnsureLoops()
        {
            if (_loops.Count > 0)
                return true;
            var service = AudioService.Instance;
            if (service == null || service.Library == null)
                return false;
            var lib = service.Library;
            Add(service, InteriorAmbience.Life, lib.RoomLife);
            Add(service, InteriorAmbience.Water, lib.RoomWater);
            Add(service, InteriorAmbience.Machine, lib.RoomMachine);
            Add(service, InteriorAmbience.Heat, lib.RoomHeat);
            Add(service, InteriorAmbience.Electric, lib.RoomElectric);
            Add(service, InteriorAmbience.Hall, lib.RoomHall);
            return true;
        }

        private void Add(AudioService service, InteriorAmbience kind, SoundCue cue)
        {
            var loop = service.CreateLoop(cue);
            loop.FadeSpeed = FadeSpeed;
            loop.Target = 0f;
            _loops[kind] = loop;
        }

        private static void HandleFootstep(bool run)
        {
            AudioService.TryPlay(l => l.Footstep, run ? 1f : WalkVolume);
        }
    }
}
