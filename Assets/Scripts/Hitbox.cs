using UnityEngine;

namespace Scripts
{
    public class Hitbox : MonoBehaviour
    {
        [SerializeField] private Collider2D hitboxCollider;
        [SerializeField] private float damageCoef=1;
        [SerializeField] private float stanceCoef=1;
        [SerializeField, Range(0f, 1f)] private float activeStart;
        [SerializeField, Range(0f, 1f)] private float activeEnd = 1f;

        // Use the same total Move progress as CharacterAnimationPlayer. Do not
        // re-enable colliders here: combat disables a consumed hitbox after contact.
        public bool IsActiveAt(float moveProgress) =>
            moveProgress >= activeStart && moveProgress <= activeEnd;

        internal Collider2D Collider => hitboxCollider;
        public virtual float DamageCoef => damageCoef;
        public virtual float StanceCoef => stanceCoef;

        private void Reset()
        {
            CacheCollider();
        }

        private void OnValidate()
        {
            CacheCollider();
        }

        private void CacheCollider()
        {
            if (hitboxCollider == null)
            {
                hitboxCollider = GetComponent<Collider2D>();
            }
        }
    }
}
