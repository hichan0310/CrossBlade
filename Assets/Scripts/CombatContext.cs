using UnityEngine;

namespace Scripts
{
    public class CombatContext
    {
        public Actor user;
        public Actor target;
        public ActorManager manager;
        public ExchangeResult exchangeResult;
        public MoveEventType trigger;
        public InterruptReason reason;
        public int userHpDamage;
        public int targetHpDamage;
        public int userStanceDamage;
        public int targetStanceDamage;

        public Actor Resolve(SkillTargetSide side)
        {
            switch (side)
            {
                case SkillTargetSide.User:
                    return user;
                case SkillTargetSide.Target:
                    return target;
                default:
                    return null;
            }
        }
    }
}
