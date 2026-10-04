using UnityEngine;

namespace Scripts
{
    // This planner has no local UI or AI: the host accepts validated network choices.
    public sealed class OnlineDuelPlanner : PlanMaker
    {
        public OnlineDuel duel;
        public override PlanQueryState GetPlan(Actor actor) => duel != null ? duel.Plan(actor) : PlanQueryState.Failed;
        public override PlanQueryState GetForce(Actor actor) => PlanQueryState.Ready;
    }
}
