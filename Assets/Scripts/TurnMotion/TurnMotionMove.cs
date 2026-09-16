using Scripts.TurnMotion;
using UnityEngine;

namespace Scripts
{
    /// <summary>
    /// A normal CrossBlade Move whose visual timeline is driven by a generated one-turn motion.
    /// It can be queued by ActorActionController exactly like every existing Move prefab.
    /// </summary>
    public class TurnMotionMove : Move
    {
        [SerializeField] private TurnMotionAction turnMotionAction;

        public TurnMotionAction TurnMotionAction => turnMotionAction;
        internal override float Duration => turnMotionAction != null
            ? turnMotionAction.TurnDuration
            : base.Duration;

        public void Configure(TurnMotionAction action)
        {
            turnMotionAction = action;
        }

        internal override void Play(ActorType actorType, CombatContext combatContext, int force, out int carryOut)
        {
            base.Play(actorType, combatContext, force, out carryOut);
            if (turnMotionAction == null) return;
            turnMotionAction.Pause();
            turnMotionAction.SetTime(0f, true);
        }

        internal void EvaluateVisual(float normalizedTime)
        {
            if (turnMotionAction != null)
                turnMotionAction.SetNormalizedTime(normalizedTime, true);
        }
    }
}
