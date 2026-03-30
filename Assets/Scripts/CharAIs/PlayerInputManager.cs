using System;
using UnityEngine;

namespace Scripts.CharAIs
{
    
    [CreateAssetMenu(fileName = "PlayerInputManager", menuName = "AI/PlayerInputManager")]
    public class PlayerInputManager:PlanMaker
    {
        [SerializeField] public float inputDuration = 0.5f;
        
        public override bool GetPlan(Actor actor)
        {
            var after = actor.Current.move.After;
            int nextIndex = UnityEngine.Random.Range(0, after.Count);
            actor.ActionController.Enqueue(after[nextIndex]);
            return true;
        }

        public override bool GetForce(Actor actor)
        {
            if (actor.ActionController.nextMove.UsesForce && !actor.GettingForce && !actor.GettingForceFinished)
            {
                actor.StartGettingForce();
            }
            return (!actor.GettingForce && actor.GettingForceFinished) || !actor.ActionController.nextMove.UsesForce;
        }
    }
}
