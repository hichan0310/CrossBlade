using UnityEngine;

namespace Scripts
{
    public class Hitbox : MonoBehaviour
    {
        [SerializeField] private Collider2D hitboxCollider;
        [SerializeField] private int baseTrueDamage;

        internal Collider2D Collider => hitboxCollider;
        public virtual int trueDamage => baseTrueDamage;

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
