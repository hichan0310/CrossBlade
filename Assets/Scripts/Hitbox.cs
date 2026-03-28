using UnityEngine;

namespace Scripts
{
    public class Hitbox : MonoBehaviour
    {
        [SerializeField] private Collider2D hitboxCollider;
        [SerializeField] private float damageCoef=1;
        [SerializeField] private float stanceCoef=1;

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
