using System;
using UnityEngine;

namespace Scripts
{
    public enum SkillTargetSide
    {
        User,
        Target
    }

    public abstract class SpecialSkillEffect : MonoBehaviour
    {
        public virtual bool CanApply(CombatContext context)
        {
            return true;
        }

        public abstract void Apply(CombatContext context);
    }

    public class SpecialSkill : MonoBehaviour
    {
        [Header("Skill")]
        public string skillId = "speciqal_skill";
        [Min(0)] public int forceCost = 1;

        public virtual bool CanUse(CombatContext context)
        {
            return context.user != null
                && context.manager != null
                && context.user.CanSpendSpecialForce(forceCost)
                && context.manager.CanUseSpecialSkill(context.user)
                && AllEffectsCanApply(context);
        }

        public bool TryUse(CombatContext context)
        {
            if (!CanUse(context))
            {
                return false;
            }

            if (!context.user.SpendSpecialForce(forceCost))
            {
                return false;
            }

            SpecialSkillEffect[] effects = GetComponents<SpecialSkillEffect>();
            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i] == null)
                {
                    continue;
                }

                effects[i].Apply(context);
            }

            return true;
        }

        private bool AllEffectsCanApply(CombatContext context)
        {
            SpecialSkillEffect[] effects = GetComponents<SpecialSkillEffect>();
            if (effects.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i] == null)
                {
                    continue;
                }

                if (!effects[i].CanApply(context))
                {
                    return false;
                }
            }

            return true;
        }
    }

    [AddComponentMenu("CrossBlade/Special Skill Effects/Multiply Next Attack Damage")]
    public class MultiplyNextAttackDamageEffect : SpecialSkillEffect
    {
        public SkillTargetSide targetSide = SkillTargetSide.User;
        [Min(1f)] public float damageMultiplier = 2f;

        public override bool CanApply(CombatContext context)
        {
            return context.Resolve(targetSide) != null;
        }

        public override void Apply(CombatContext context)
        {
            Actor actor = context.Resolve(targetSide);
            if (actor == null)
            {
                return;
            }

            actor.SetNextAttackDamageMultiplier(damageMultiplier);
        }
    }

    [AddComponentMenu("CrossBlade/Special Skill Effects/Stop Turns")]
    public class StopTurnsEffect : SpecialSkillEffect
    {
        public SkillTargetSide targetSide = SkillTargetSide.Target;
        [Min(1)] public int turns = 1;

        public override bool CanApply(CombatContext context)
        {
            return context.manager != null && context.Resolve(targetSide) != null;
        }

        public override void Apply(CombatContext context)
        {
            Actor actor = context.Resolve(targetSide);
            if (actor == null)
            {
                return;
            }

            context.manager.StopActorForTurns(actor, turns);
        }
    }

    [AddComponentMenu("CrossBlade/Special Skill Effects/Force Interrupt")]
    public class ForceInterruptEffect : SpecialSkillEffect
    {
        public SkillTargetSide targetSide = SkillTargetSide.Target;
        public MoveEventType trigger = MoveEventType.Hit;
        public InterruptReason reason = InterruptReason.Forced;
        public bool requireRunningMove = true;

        public override bool CanApply(CombatContext context)
        {
            Actor actor = context.Resolve(targetSide);
            if (actor == null || context.manager == null)
            {
                return false;
            }

            return !requireRunningMove || actor.IsMoveRunning;
        }

        public override void Apply(CombatContext context)
        {
            Actor actor = context.Resolve(targetSide);
            if (actor == null)
            {
                return;
            }

            context.manager.ForceActorInterrupt(actor, trigger, reason);
        }
    }

    [AddComponentMenu("CrossBlade/Special Skill Effects/Change Stance")]
    public class ChangeStanceEffect : SpecialSkillEffect
    {
        public SkillTargetSide targetSide = SkillTargetSide.User;
        public int amount;

        public override bool CanApply(CombatContext context)
        {
            return context.Resolve(targetSide) != null;
        }

        public override void Apply(CombatContext context)
        {
            Actor actor = context.Resolve(targetSide);
            if (actor == null)
            {
                return;
            }

            if (amount >= 0)
            {
                actor.RecoverStance(amount);
                return;
            }

            actor.ApplyStanceDamage(-amount);
        }
    }

    [AddComponentMenu("CrossBlade/Special Skill Effects/Change Special Force")]
    public class ChangeSpecialForceEffect : SpecialSkillEffect
    {
        public SkillTargetSide targetSide = SkillTargetSide.User;
        public int amount;

        public override bool CanApply(CombatContext context)
        {
            return context.Resolve(targetSide) != null;
        }

        public override void Apply(CombatContext context)
        {
            Actor actor = context.Resolve(targetSide);
            if (actor == null)
            {
                return;
            }

            if (amount >= 0)
            {
                actor.GainSpecialForce(amount);
                return;
            }

            actor.SpendSpecialForce(Mathf.Min(actor.specialForce, -amount));
        }
    }

    [AddComponentMenu("CrossBlade/Special Skill Effects/Direct Damage")]
    public class DirectDamageEffect : SpecialSkillEffect
    {
        public SkillTargetSide targetSide = SkillTargetSide.Target;
        [Min(0)] public int hpDamage;
        [Min(0)] public int stanceDamage;

        public override bool CanApply(CombatContext context)
        {
            return context.Resolve(targetSide) != null;
        }

        public override void Apply(CombatContext context)
        {
            Actor actor = context.Resolve(targetSide);
            if (actor == null)
            {
                return;
            }

            actor.ApplyHpDamage(hpDamage);
            actor.ApplyStanceDamage(stanceDamage);
        }
    }
}
