using UnityEngine;

namespace Scripts
{
    public abstract class PlanMaker:ScriptableObject
    {
        // plan, force가 필요한 상황에서 계속 호출될거임
        public abstract bool GetPlan(Actor actor);
        public abstract bool GetForce(Actor actor);
    }
}
