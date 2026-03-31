using UnityEngine;

namespace Scripts
{
    public enum PlanQueryState
    {
        Running,
        Ready,
        Failed
    }
    public abstract class PlanMaker:ScriptableObject
    {
        [Header("Optional UI")]
        [SerializeField] protected GameObject planInputUIPrefab;
        [SerializeField] protected GameObject forceInputUIPrefab;
        public GameObject PlanInputUIPrefab => planInputUIPrefab;
        public GameObject ForceInputUIPrefab => forceInputUIPrefab;

        // plan, force가 필요한 상황에서 계속 호출됨
        public abstract PlanQueryState GetPlan(Actor actor);
        public abstract PlanQueryState GetForce(Actor actor);
    }
}
