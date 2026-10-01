using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>5-9 메인 메뉴 소리: 메뉴 음악 + 조용한 정거장 험.</summary>
    public sealed class MenuAudio : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float _humLevel = 0.5f;

        private void Start()
        {
            var audio = AudioService.Instance;
            if (audio == null || audio.Library == null)
                return;
            audio.PlayMusic(audio.Library.MusicMenu);
            var hum = audio.CreateLoop(audio.Library.StationHum);
            hum.FadeSpeed = 0.3f;
            hum.Target = _humLevel;
        }
    }
}
