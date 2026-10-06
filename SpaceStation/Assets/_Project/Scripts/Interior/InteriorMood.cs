using System;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>11-10 방 하나의 시뮬레이션 상태 (연출 입력).</summary>
    public struct RoomCondition
    {
        /// <summary>코어와 연결됨.</summary>
        public bool Active;
        /// <summary>운석·확산 파손 중 (수리 대기·수리 중 포함).</summary>
        public bool Damaged;
        /// <summary>정거장 전력 효율 (0~1, 전역).</summary>
        public float Power;
        /// <summary>그 모듈의 노후 효율 (내구도 효율, 0~1). 1 = 노후 없음.</summary>
        public float Wear;
        /// <summary>태양 폭풍 진행 중.</summary>
        public bool Storm;

        public static RoomCondition Normal => new RoomCondition { Active = true, Power = 1f, Wear = 1f };
    }

    public enum RoomLightMode
    {
        /// <summary>평소 조명 (밝기·색·깜빡임만 바뀜).</summary>
        Normal,
        /// <summary>파손: 붉은 비상 조명 + 경광등·연기·스파크·경보음.</summary>
        Alarm,
        /// <summary>비활성(코어와 끊김): 방 조명 꺼짐 + 바닥 호박색 비상등.</summary>
        Emergency,
    }

    /// <summary>11-10 방 조명 연출 값 (<see cref="InteriorMoodRules.Evaluate"/>의 결과).</summary>
    public struct RoomMood
    {
        public RoomLightMode Mode;
        /// <summary>방 조명 세기 배율 (0~1). 깜빡임은 이 값 위에서 순간적으로 더 낮춘다.</summary>
        public float Brightness;
        /// <summary>조명 색에 곱하는 색 (노후 = 누런 색, 파손 = 붉은 색).</summary>
        public Color Tint;
        /// <summary>전력 부족 깜빡임 횟수 (초당 평균).</summary>
        public float FlickerRate;
        /// <summary>노후 형광등 떨림 (초당 평균, 짧게 여러 번 튐).</summary>
        public float StutterRate;
        /// <summary>태양 폭풍 지직거림 세기 (0이면 없음).</summary>
        public float Jitter;
    }

    /// <summary>11-10 연출 수치 (InteriorMode 인스펙터에서 조정).</summary>
    [Serializable]
    public sealed class InteriorMoodTuning
    {
        [Header("전력 부족")]
        [Tooltip("이 전력 효율(최소 효율)에서 가장 어둡고 가장 자주 깜빡임")]
        public float PowerFloor = 0.25f;
        [Tooltip("최소 효율일 때 조명 세기 배율")]
        public float PowerMinBrightness = 0.45f;
        [Tooltip("최소 효율일 때 초당 깜빡임 수 (효율 100%면 0, 사이는 비례)")]
        public float PowerMaxFlickerRate = 2.4f;
        [Tooltip("깜빡일 때 세기 배율 범위")]
        public Vector2 FlickerLevel = new Vector2(0.08f, 0.35f);
        [Tooltip("깜빡임 한 번 길이 (초)")]
        public Vector2 FlickerDuration = new Vector2(0.04f, 0.13f);

        [Header("노후")]
        [Tooltip("노후 효율 0일 때 조명 색 (사이는 비례)")]
        public Color WearTint = new Color(1f, 0.78f, 0.52f);
        [Tooltip("노후 효율 0일 때 줄어드는 세기 비율")]
        public float WearDim = 0.35f;
        [Tooltip("노후 효율이 이 값보다 낮으면 형광등처럼 가끔 떨림")]
        public float WearStutterBelow = 0.35f;
        public float WearStutterRate = 0.3f;

        [Header("파손 (경보)")]
        public float AlarmBrightness = 0.3f;
        public Color AlarmTint = new Color(1f, 0.22f, 0.16f);
        public Color BeaconColor = new Color(1f, 0.12f, 0.08f);
        public float BeaconIntensity = 14f;
        public float BeaconRange = 10f;
        [Tooltip("경광등 회전 (도/초)")]
        public float BeaconSpeed = 220f;
        [Tooltip("칸 하나당 초당 연기 입자 수")]
        public float SmokePerCell = 5f;
        [Tooltip("스파크 간격 (초)")]
        public Vector2 SparkInterval = new Vector2(0.6f, 2.2f);

        [Header("비활성 (비상등)")]
        public Color EmergencyColor = new Color(1f, 0.55f, 0.15f);
        [Tooltip("1.8은 8m 방에서 거의 안 보였음")]
        public float EmergencyIntensity = 4f;
        public float EmergencyRange = 7f;

        [Header("태양 폭풍")]
        [Tooltip("지직거림 세기 (세기 배율의 ±)")]
        public float StormJitter = 0.12f;
        [Tooltip("초당 번쩍임 수")]
        public float StormSpikeRate = 0.5f;
        public Color StormSpikeTint = new Color(0.75f, 0.85f, 1.25f);
    }

    /// <summary>11-10 방 상태 → 조명 연출 (순수 계산, 테스트 대상).</summary>
    public static class InteriorMoodRules
    {
        public static RoomMood Evaluate(RoomCondition c, InteriorMoodTuning t)
        {
            if (!c.Active)
                return new RoomMood { Mode = RoomLightMode.Emergency, Brightness = 0f, Tint = Color.white };

            var mood = new RoomMood { Mode = RoomLightMode.Normal, Brightness = 1f, Tint = Color.white };

            // 전력 부족: 효율 1 → 바닥값 사이를 0~1로
            float shortage = c.Power >= 1f ? 0f : Mathf.InverseLerp(1f, t.PowerFloor, c.Power);
            if (shortage > 0f)
            {
                mood.Brightness *= Mathf.Lerp(1f, t.PowerMinBrightness, shortage);
                mood.FlickerRate = t.PowerMaxFlickerRate * shortage;
            }

            // 노후: 효율이 낮을수록 누렇고 어둡게, 아주 낮으면 떨림
            float wear = 1f - Mathf.Clamp01(c.Wear);
            if (wear > 0f)
            {
                mood.Brightness *= 1f - t.WearDim * wear;
                mood.Tint = Color.Lerp(Color.white, t.WearTint, wear);
                if (c.Wear < t.WearStutterBelow)
                    mood.StutterRate = t.WearStutterRate;
            }

            if (c.Storm)
                mood.Jitter = t.StormJitter;

            if (c.Damaged)
            {
                mood.Mode = RoomLightMode.Alarm;
                mood.Brightness *= t.AlarmBrightness;
                mood.Tint = t.AlarmTint;
            }
            return mood;
        }
    }
}
