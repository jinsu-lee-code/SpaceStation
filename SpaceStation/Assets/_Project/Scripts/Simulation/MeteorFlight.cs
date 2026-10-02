using SpaceStation.Core;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 5-7 운석 한 발의 경로 (연출용, 판정 결과는 바꾸지 않음).
    /// 노림 → (포탑 격추) 또는 (실드 빗겨냄 → 우주로 / 튕겨서 다른 모듈로 → 격추 또는 명중) 또는 명중.
    /// </summary>
    public sealed class MeteorFlight
    {
        /// <summary>처음 노린 모듈.</summary>
        public ModuleInstance Target;
        /// <summary>빗겨낸 실드 (없으면 null).</summary>
        public ModuleInstance DeflectedBy;
        /// <summary>튕겨서 새로 노린 모듈 (없으면 null = 우주로 날아감).</summary>
        public ModuleInstance RicochetTarget;
        /// <summary>포탑이 격추한 지점의 대상 모듈 (없으면 null).</summary>
        public ModuleInstance InterceptedAt;
        /// <summary>최종 명중 모듈 (없으면 null).</summary>
        public ModuleInstance Hit;
        /// <summary>명중했지만 방어 연구로 파손을 면함 (내구도만 깎임, Phase 6).</summary>
        public bool Immune;

        public bool Intercepted => InterceptedAt != null;
        public bool Deflected => DeflectedBy != null;
    }
}
