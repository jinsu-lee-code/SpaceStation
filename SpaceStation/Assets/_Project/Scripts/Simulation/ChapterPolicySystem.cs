using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 13-6a 서사판 장 정책 (사용자 결정 2026-10-18). 공통 정책 틀에 판 고리(<see cref="IPolicyGate"/>, M-22)로 얹는다.
    /// - 정책 규칙(StationRule.Policies)이 열린 장부터: 슬롯 = 정책을 정한 마지막 장의 값 + 그 장 등급 목표를 채우면 더(3장 1칸 → 대형 때 2칸)
    /// - 고를 수 있는 정책 = 지금 장까지 장 데이터에 넣은 것 (3장 4종), 적용 대기 · 교체 대기도 장 값(30초 · 3분)
    /// - 종족 반응(<see cref="SocietyConfig"/>): 통제형 감점 · 자율형 효과가 무리 비율로 커지거나 작아짐. 무리 구성이 바뀌면 효과를 다시 계산
    /// - 정책이 하나라도 시행되면 장 표시 'policy' (3장 필수 목표)
    /// </summary>
    public sealed class ChapterPolicySystem : StationSystem, IPolicyGate
    {
        public const string SystemKey = "chapterPolicy";
        /// <summary>정책이 하나라도 시행됨 (장 목표 표시).</summary>
        public const string PolicyFlag = "policy";

        private readonly SocietyConfig _society;
        private float _appliedReaction;

        public ChapterPolicySystem(SocietyConfig society)
        {
            _society = society;
        }

        public override string Key => SystemKey;

        private ChapterSystem Chapters => Sim.Chapters;

        /// <summary>무리 비율로 섞은 정책 반응 (+ 순응 · − 반발, 종족이 없으면 0).</summary>
        public float Reaction { get; private set; }

        /// <summary>반발하는 무리의 크기 (Σ 비율 × 반발 크기, 0 ~ 1) — 긴장도 '정책 반발'.</summary>
        public float Opposition { get; private set; }

        protected override void OnAttached()
        {
            Measure();
            _appliedReaction = Reaction;
            if (Sim.Policies != null)
                Sim.Policies.Gate = this;
            if (Chapters != null)
                Chapters.ChapterStarted += HandleChapterStarted;
        }

        private void HandleChapterStarted(int index)
        {
            var data = Chapters.Current;
            if (data.PolicySlots <= 0 || SlotCount(0) <= 0)
                return;
            Sim.Report($"정책이 열렸습니다 · [관리] → 정책에서 {data.PolicySlots}칸에 고를 수 있음", true);
            Sim.Policies?.Refresh();
        }

        public override void Tick(float dt)
        {
            var policies = Sim.Policies;
            if (policies == null)
                return;
            Measure();
            if (Math.Abs(Reaction - _appliedReaction) > 0.005f)
            {
                _appliedReaction = Reaction;
                policies.Refresh(); // 무리 구성이 바뀌면 실제 효과 값도
            }
            if (Chapters != null && !Chapters.HasFlag(PolicyFlag))
                foreach (var s in policies.Slots)
                    if (s.Active != null)
                    {
                        Chapters.SetFlag(PolicyFlag);
                        break;
                    }
        }

        /// <summary>지금 시행 중인 통제형 정책 수.</summary>
        public int ActiveControl
        {
            get
            {
                int n = 0;
                if (Sim.Policies != null)
                    foreach (var s in Sim.Policies.Slots)
                        if (s.Active != null && s.Active.Stance == PolicyStance.Control)
                            n++;
                return n;
            }
        }

        // ---------------- 종족 반응 ----------------

        private void Measure()
        {
            float reaction = 0f, opposition = 0f;
            var ships = Sim.GetSystem<ArkShipSystem>();
            var roster = Sim.Residents;
            if (ships != null && roster != null && roster.Residents.Count > 0)
            {
                foreach (var s in ships.Config.Species)
                {
                    if (s == null)
                        continue;
                    float ratio = (float)roster.CountKind(s.Id) / roster.Residents.Count;
                    reaction += ratio * s.ControlReaction;
                    if (s.ControlReaction < 0f)
                        opposition += ratio * -s.ControlReaction;
                }
            }
            Reaction = Mathf.Clamp(reaction, -1f, 1f);
            Opposition = Mathf.Clamp01(opposition);
        }

        /// <summary>통제형 만족도 감점 배율 · 자율형 좋은 효과 배율.</summary>
        private float ControlFactor => _society != null ? Mathf.Max(0f, 1f - Reaction * _society.ControlScale) : 1f;
        private float AutonomyFactor => _society != null ? Mathf.Max(0f, 1f - Reaction * _society.AutonomyScale) : 1f;

        /// <summary>반응 쪽으로 가장 크게 기여하는 종족 이름 (없으면 null).</summary>
        private string Leader(bool complying)
        {
            var ships = Sim.GetSystem<ArkShipSystem>();
            var roster = Sim.Residents;
            if (ships == null || roster == null || roster.Residents.Count == 0)
                return null;
            string best = null;
            float bestWeight = 0f;
            foreach (var s in ships.Config.Species)
            {
                if (s == null || (complying ? s.ControlReaction <= 0f : s.ControlReaction >= 0f))
                    continue;
                float weight = roster.CountKind(s.Id) * Mathf.Abs(s.ControlReaction);
                if (weight > bestWeight)
                {
                    bestWeight = weight;
                    best = s.DisplayName;
                }
            }
            return best;
        }

        // ---------------- 고리 (IPolicyGate) ----------------

        /// <summary>정책 설정을 정한 마지막 장 (지금 장까지, 없으면 null).</summary>
        private ChapterData Defining(out int index)
        {
            index = -1;
            if (Chapters == null)
                return null;
            for (int i = Math.Min(Chapters.Index, Chapters.Count - 1); i >= 0; i--)
            {
                var c = Chapters.GetChapter(i);
                if (c != null && c.PolicySlots > 0)
                {
                    index = i;
                    return c;
                }
            }
            return null;
        }

        /// <summary>등급 목표를 채우면 더 열릴 슬롯이 남았는지.</summary>
        private bool GradeUpPending(ChapterData def, int index) => def != null && def.GradeUpPolicySlots > 0 && index == Chapters.Index && !Chapters.GradeUpApplied;

        public int SlotCount(int byDefault)
        {
            if (Chapters == null)
                return byDefault;
            if (Sim.Rules != null && !Sim.Rules.Allows(StationRule.Policies))
                return 0;
            var def = Defining(out int index);
            if (def == null)
                return byDefault;
            return def.PolicySlots + (GradeUpPending(def, index) || def.GradeUpPolicySlots <= 0 ? 0 : def.GradeUpPolicySlots);
        }

        public bool Offers(PolicyData policy)
        {
            if (policy == null || Chapters == null)
                return policy != null;
            for (int i = 0; i <= Chapters.Index && i < Chapters.Count; i++)
            {
                var c = Chapters.GetChapter(i);
                if (c == null)
                    continue;
                foreach (var p in c.Policies)
                    if (p == policy)
                        return true;
            }
            return false;
        }

        public float ApplyDelay(float byDefault)
        {
            var def = Defining(out _);
            return def != null ? def.PolicyApplyDelay : byDefault;
        }

        public float SwapCooldown(float byDefault)
        {
            var def = Defining(out _);
            return def != null ? def.PolicySwapCooldown : byDefault;
        }

        public float Value(PolicyData policy, PolicyModifier modifier)
        {
            if (policy == null || _society == null)
                return modifier.Value;
            switch (policy.Stance)
            {
                case PolicyStance.Control:
                    // 반발 = 만족도 상한 감점만 커지거나 작아짐 (좋은 효과는 그대로)
                    return modifier.Stat == PolicyStat.SatisfactionCap && modifier.Value < 0f ? modifier.Value * ControlFactor : modifier.Value;
                case PolicyStance.Autonomy:
                    PolicyStats.Describe(modifier.Stat, modifier.Value, out bool good);
                    if (!good)
                        return modifier.Value;
                    float neutral = PolicyStats.Neutral(modifier.Stat);
                    return neutral + (modifier.Value - neutral) * AutonomyFactor;
                default:
                    return modifier.Value;
            }
        }

        public string Note(PolicyData policy)
        {
            if (policy == null)
                return null;
            switch (policy.Stance)
            {
                case PolicyStance.Control:
                    return "통제형" + ReactionNote(ControlFactor, "감점", complying: Reaction > 0f, Reaction > 0f ? "순응" : "반발");
                case PolicyStance.Autonomy:
                    return "자율형" + ReactionNote(AutonomyFactor, "효과", complying: Reaction > 0f, Reaction > 0f ? "덤덤" : "반김");
                default:
                    return null;
            }
        }

        private string ReactionNote(float factor, string what, bool complying, string verb)
        {
            if (_society == null || Mathf.Abs(factor - 1f) < 0.01f)
                return "";
            string who = Leader(complying);
            return who != null ? $" · {who} {verb} ({what} ×{factor:0.##})" : $" · {what} ×{factor:0.##}";
        }

        public string Summary
        {
            get
            {
                var def = Defining(out int index);
                if (def == null)
                    return null;
                if (!GradeUpPending(def, index))
                    return $"{Chapters.GetChapter(index).DisplayName}부터";
                return $"{GradeGoalName(def)} 도달 때 +{def.GradeUpPolicySlots}";
            }
        }

        public string LockedSlot(int index)
        {
            var def = Defining(out int chapter);
            if (def != null && GradeUpPending(def, chapter) && index < def.PolicySlots + def.GradeUpPolicySlots)
                return $"{GradeGoalName(def)} 도달 때";
            return "뒤 장에서";
        }

        private string GradeGoalName(ChapterData chapter)
        {
            foreach (var g in chapter.Goals)
                if (g.Kind == ChapterGoalKind.ReachGrade && g.Grade < Sim.Progression.GradeCount)
                    return Sim.Progression.GetGrade(g.Grade).DisplayName;
            return "등급 목표";
        }
    }
}
