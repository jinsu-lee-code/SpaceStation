using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 사운드 개편 4-4 · D-8 · D-10 (2026-11-26, SOUND_LIST_NEW): 방 장치 소리 · 바깥 일이 안에서 들리는 소리를 고르는 규칙 — 순수 함수 (EditMode InteriorObjectSoundTests).
    /// "보이는 움직임엔 소리" — 지금 있는 방의 것만, 돌 때 · 집을 때 · 불꽃이 보일 때만 (D-8). 물건마다 소리 X → 루프 + 가끔 "덜컹" (D-2).
    /// </summary>
    public static class InteriorObjectSound
    {
        /// <summary>궤도 부품 (<see cref="InteriorOrbit"/>) 소리 갈래 — 템플릿 프리팹 부품 이름으로 고름 (템플릿을 다시 지어도 따라감, D-8).</summary>
        public enum OrbitSound { None, Drone, Hologram, Roller, Carousel }

        /// <summary>
        /// 부품 이름 → 소리 갈래. 날아다니는 드론 (비행장 · 순찰 · 착륙장 · 비상 대응 · 보안청) = 날개 웅 · 홀로그램 · 플라즈마 · 학습 코어 고리 · 데이터 흐름 = 작은 전자 웅 ·
        /// 판 롤러 = 벨트 · 회전 선반 = 모터. 없음 = 드론 모빌 (매달린 장식) · 조선소 받침대 드론 (전시) · 그리드 허브 흐름 (번개 정원 체험 소리) · 구름 · 물고기 · 거품 · 풍선 ·
        /// 혼천의 · 떠도는 돌 · 쓸기 빛 (레이더 핑이 따로) · 등대 렌즈 · 시민 회관 별 궤도 (상영 소리).
        /// </summary>
        public static OrbitSound ForOrbitPart(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Contains("_Swarm") || name.Contains("Shipyard_Drone"))
                return OrbitSound.None;
            if (name.Contains("Drone") || name.Contains("_Fly") || name.Contains("SecurityHQ_Orbit"))
                return OrbitSound.Drone;
            if (name.Contains("_Holo") || name.Contains("_Plasma") || name.Contains("SecurityHQ_Show") || name.Contains("Automation_Ring")
                || name.Contains("Automation_Flow") || name.Contains("Automation_Logs"))
                return OrbitSound.Hologram;
            if (name.Contains("_Roll0"))   // 롤러 셋은 함께 돎 — 소리는 하나 (D-2)
                return OrbitSound.Roller;
            if (name.Contains("_Carousel0"))   // 회전 선반 탑 여럿 — 소리는 하나
                return OrbitSound.Carousel;
            return OrbitSound.None;
        }

        /// <summary>
        /// 움직이는 부품의 모터 크기 (0 ~ 1): 빠르기 (m/s) · 도는 빠르기 (°/s)를 기준 빠르기로 나눈 것 중 큰 쪽. gate 아래는 0 —
        /// 해를 따라 아주 천천히 도는 가대처럼 보이지 않는 움직임에는 소리를 내지 않음.
        /// </summary>
        public static float MotionLevel(float metersPerSecond, float degreesPerSecond, float fullMeters, float fullDegrees, float gate = 0.12f)
        {
            float a = fullMeters > 0f ? metersPerSecond / fullMeters : 0f;
            float b = fullDegrees > 0f ? degreesPerSecond / fullDegrees : 0f;
            float v = Mathf.Clamp01(Mathf.Max(a, b));
            return v < gate ? 0f : v;
        }

        /// <summary>
        /// 화분 식물 · 나무 곁을 지나감 (바스락): 식물 줄기에서 수평 reach m 안 · 플레이어가 걷는 중 (minSpeed m/s 넘게) · 그 식물을 막 스친 게 아님 (cooldown 초).
        /// </summary>
        public static bool Brushes(float horizontalDistance, float playerSpeed, float sinceLast, float reach = 0.85f, float minSpeed = 0.6f, float cooldown = 1.5f)
            => horizontalDistance <= reach && playerSpeed >= minSpeed && sinceLast >= cooldown;

        /// <summary>
        /// 유성우 몰아침 (D-2 "한 파도에 많으면 몰아치는 소리 하나"): 최근 window초 안 충돌 수가 count 이상이고 지난 울림에서 gap초가 지났으면 낮은 울림 한 번.
        /// </summary>
        public static bool Barrage(int recentImpacts, float sinceRumble, int count = 3, float gap = 4f) => recentImpacts >= count && sinceRumble >= gap;

        /// <summary>
        /// 바깥에서 모듈 가까이 줌 (D-4): 카메라 ~ 가장 가까운 모듈 칸 거리 (바깥 m)로 그 방 바탕 크기 — near m 안 1, far m 밖 0.
        /// 카메라 가장 가까운 줌 (3 m)에서 한 칸 모듈 = 약 2.5 m.
        /// </summary>
        public static float ZoomLevel(float distance, float near = 3f, float far = 7f)
            => 1f - Mathf.Clamp01((distance - near) / Mathf.Max(0.01f, far - near));
    }
}
