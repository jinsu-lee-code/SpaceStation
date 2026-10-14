using SpaceStation.Core;

namespace SpaceStation.UI
{
    /// <summary>
    /// 12-3c-opt 로딩 화면 팁 (<see cref="SceneFader"/>). 키는 보여 줄 때 지금 설정된 키로 바꿔 넣는다.
    /// 새 시스템이 생기면 여기에 한 줄 더한다.
    /// </summary>
    public static class LoadingTips
    {
        private static readonly string[] Lines =
        {
            "면이 맞닿은 모듈은 저절로 연결됩니다. 코어와 이어지지 않은 모듈은 작동하지 않아요.",
            "{Pause} 키로 언제든 시간을 멈추고 천천히 계획을 세울 수 있어요.",
            "모듈을 선택하고 {EnterInterior} 키를 누르면 안으로 들어가 직접 걸어 볼 수 있어요.",
            "내부에서 {Pad} 키로 휴대 패드를 들면 정거장 지도와 빠른 이동을 쓸 수 있어요.",
            "파손된 모듈 안에 들어가면 손상 지점을 직접 고칠 수 있어요.",
            "선택한 모듈은 {Move} 키로 다른 자리에 옮길 수 있어요.",
            "합성 레시피 창({Recipes})에서 모듈을 합쳐 더 강한 진화 모듈을 만드는 법을 볼 수 있어요.",
            "주민 명단({Roster})에서 주민마다 특성과 사는 집을 확인할 수 있어요.",
            "도시급부터 유성우가 예보됩니다. 예보된 방향의 앞면을 장갑 격벽으로 막아 두세요.",
            "국가급부터 정책 창({Policies})에서 상시 정책을 고를 수 있어요. 바꾼 정책은 잠시 뒤에 적용돼요.",
            "만족도가 계속 낮으면 주민이 파업을 경고해요. 정책과 여가 시설로 만족도를 챙기세요.",
            "대륙급부터 융합로 · 정제소 · 연료 전지가 주변을 데웁니다. 열 지도({HeatMap})로 과열된 곳을 찾으세요.",
            "방열판은 정거장 바깥에 드러나 있어야 맞닿은 모듈의 열을 빼낼 수 있어요.",
            "{SpeedCycle} 키로 배속을 바꿀 수 있어요. 큰 사건 전에는 1배속으로 돌려 두면 안전해요.",
        };

        public static int Count => Lines.Length;

        public static string Get(int index)
        {
            string line = Lines[((index % Lines.Length) + Lines.Length) % Lines.Length];
            int start = line.IndexOf('{');
            while (start >= 0)
            {
                int end = line.IndexOf('}', start);
                if (end < 0)
                    break;
                string name = line.Substring(start + 1, end - start - 1);
                string key = System.Enum.TryParse<GameAction>(name, out var action) ? KeyBindings.Label(action) : name;
                line = line.Substring(0, start) + $"<color={HudTheme.AccentHex}><b>{key}</b></color>" + line.Substring(end + 1);
                start = line.IndexOf('{', start + 1);
            }
            return line;
        }
    }
}
