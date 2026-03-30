using UnityEngine;

namespace Scripts.CharAIs
{
    
    [CreateAssetMenu(fileName = "SampleAI", menuName = "AI/SampleAI")]
    public class SampleAI:PlanMaker
    {
        public override bool GetPlan(Actor actor)
        {
            var after = actor.Current.move.After;
            int nextIndex = UnityEngine.Random.Range(0, after.Count);
            actor.ActionController.Enqueue(after[nextIndex]);
            return true;
        }

        public override bool GetForce(Actor actor)
        {
            actor.pendingForce=UnityEngine.Random.Range(1, 5);
            return true;
        }
    }
}
