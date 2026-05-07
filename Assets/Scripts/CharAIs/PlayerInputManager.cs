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
            if (actor == null)
            {
                return PlanQueryState.Failed;
            }

            Move baseMove = actor.PlanningBaseMove;
            if (baseMove == null)
            {
                return PlanQueryState.Failed;
            }

            var after = baseMove.After;
            if (after == null || after.Count == 0)
            {
                actor.FailPlannedMove();
                return PlanQueryState.Failed;
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
            if (actor == null)
            {
                return PlanQueryState.Failed;
            }

            if (actor.ActionController.nextMove == null || !actor.ActionController.nextMove.UsesForce)
            {
                return PlanQueryState.Ready;
            }

            if (!actor.GettingForce && !actor.GettingForceFinished)
            {
                actor.StartGettingForce();
                return PlanQueryState.Running;
            }

            return (!actor.GettingForce && actor.GettingForceFinished)
                ? PlanQueryState.Ready
                : PlanQueryState.Running;
        }
    }
}