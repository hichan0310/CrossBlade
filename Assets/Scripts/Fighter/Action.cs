using UnityEngine;

namespace Fighter
{
    public class Action:MonoBehaviour
    {
        [Tooltip("body colliders")]
        public Collider2D head;
        public Collider2D body;
        public Collider2D leg;

        public Collider2D defenceCollider;
        
    }
}