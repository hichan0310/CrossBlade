using System.Collections.Generic;
using UnityEngine;

namespace Scripts.CharAIs
{
    [CreateAssetMenu(fileName = "SampleAI", menuName = "AI/SampleAI")]
    public class SampleAI : PlanMaker
    {
        public override PlanQueryState GetPlan(Actor actor)
        {
            if (actor == null || actor.PlanningMove == null)
            {
                return PlanQueryState.Failed;
            }

            if (actor.HasPlannedMove) return PlanQueryState.Ready;

            var after = actor.PlanningMove.After;
            var candidates = new List<Move>();
            if (after != null)
                foreach (var move in after)
                    if (move != null) candidates.Add(move);

            if (candidates.Count == 0)
            {
                if (actor.IdleMove != null) { actor.SubmitPlannedMove(actor.IdleMove); return PlanQueryState.Ready; }
                actor.FailPlannedMove(); return PlanQueryState.Failed;
            }

            actor.SubmitPlannedMove(candidates[Random.Range(0, candidates.Count)]);
            return PlanQueryState.Ready;
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

            return PlanQueryState.Ready;
        }
    }
}
