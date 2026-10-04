using System;
using UnityEngine;
namespace Scripts.CharAIs
{
    [CreateAssetMenu(fileName = "PlayerInputManager", menuName = "AI/PlayerInputManager")]
    public class PlayerInputManager : PlanMaker
    {
        [SerializeField] public float inputDuration = 0.5f;

        public override PlanQueryState GetPlan(Actor actor)
        {
            if (actor == null || actor.PlanningMove == null)
            {
                return PlanQueryState.Failed;
            }

            var after = actor.PlanningMove.After;
            if (after == null || after.Count == 0)
            {
                if (actor.IdleMove != null) { actor.SubmitPlannedMove(actor.IdleMove); return PlanQueryState.Ready; }
                actor.FailPlannedMove(); return PlanQueryState.Failed;
            }

            if (actor.HasPlannedMove)
            {
                return PlanQueryState.Ready;
            }

            if (!actor.GettingPlan && !actor.GettingPlanFinished)
            {
                actor.StartGettingPlan();
                return PlanQueryState.Running;
            }

            if (actor.GettingPlanFinished)
            {
                return actor.HasPlannedMove ? PlanQueryState.Ready : PlanQueryState.Failed;
            }

            return PlanQueryState.Running;
        }

        public override PlanQueryState GetForce(Actor actor)
        {
            return actor == null ? PlanQueryState.Failed : PlanQueryState.Ready;
        }
    }
}
