using UnityEngine;

namespace Scripts
{
    public abstract class PlanInputUIBinder : MonoBehaviour
    {
        public abstract void Bind(Actor actor, PlanMaker owner);
    }
}
