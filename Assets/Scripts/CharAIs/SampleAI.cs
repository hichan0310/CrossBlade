using UnityEngine;

namespace Scripts.CharAIs
{
    [CreateAssetMenu(fileName = "SampleAI", menuName = "AI/SampleAI")]
    public class SampleAI : PlanMaker
    {
        public override PlanQueryState GetPlan(Actor actor)
        {
            if (actor == null || actor.Current.move == null)
            {
                return PlanQueryState.Failed;
            }

            var after = actor.Current.move.After;
            if (after == null || after.Count == 0)
            {
                actor.FailPlannedMove();
                return PlanQueryState.Failed;
            }

            int nextIndex = UnityEngine.Random.Range(0, after.Count);
            actor.SubmitPlannedMove(after[nextIndex]);
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

            actor.pendingForce = UnityEngine.Random.Range(1, 6);
            return PlanQueryState.Ready;
        }
    }
}