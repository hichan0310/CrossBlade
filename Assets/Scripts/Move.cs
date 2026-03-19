using System.Collections.Generic;
using Unity.VisualScripting.Dependencies.Sqlite;
using UnityEngine;

namespace Scripts
{
    public enum MoveEventType
    {
        NormalEnd,
        Hit,
        Guard,
        Clash
    }

    public class Move : MonoBehaviour
    {
        [Header("Identity")] [SerializeField] private string moveId = "move";

        [Header("Runtime View")] [SerializeField, HideInInspector]
        private Collider2D weaponCollider;

        [SerializeField] private List<Hitbox> weaponHitboxes = new List<Hitbox>();
        [SerializeField] private Collider2D bodyCollider;
        public bool hasWeaponCollider => weaponHitboxes.Count > 0;

        [Header("Timing")] 
        [SerializeField, Min(0.01f)] private float duration = 0.30f;

        [Header("Combat")] 
        [SerializeField, Min(0f)] private float knockbackResistance;
        [SerializeField, Min(0)] private int stanceDamageResistance;
        [SerializeField, Min(0)] private int chaseMaxLength;
        [SerializeField, ] private int chaseDuration;

        [Header("Graph")] [SerializeField] private Move hitMove;
        [SerializeField] private Move guardMove;
        [SerializeField] private List<Move> after = new List<Move>();
        [SerializeField] private bool guardable = true;
        [SerializeField] private bool skipAdditionalInterruptFollowUp;

        internal string MoveId => moveId;
        internal IList<Hitbox> WeaponHitboxes => weaponHitboxes;
        internal Collider2D BodyCollider => bodyCollider;
        internal float KnockbackResistance => knockbackResistance;
        internal int StanceDamageResistance => stanceDamageResistance;
        internal Move HitMove => hitMove;
        internal Move GuardMove => guardMove;
        internal IList<Move> After => after;
        internal bool Guardable => guardable;
        internal bool SkipAdditionalInterruptFollowUp => skipAdditionalInterruptFollowUp;
        internal virtual float Duration => duration;
        internal virtual int Damage => 0;
        internal virtual int StanceDamage => 0;
        internal virtual int StanceCost => 0;
        internal virtual int StanceRecovery => 0;


        internal virtual void Play(ActorType actorType, CombatContext combatContext, int force, out int carryOut)
        {
            var length = combatContext.user.transform.position.x - combatContext.target.transform.position.x;
            var scale = length < 0 ? 1 : -1;
            if (actorType == ActorType.Enemy)
            {
                combatContext.target.transform.localScale = new Vector3(-scale, 1, 1);
            }
            else if (actorType == ActorType.Player)
            {
                combatContext.user.transform.position = new Vector3(scale, 1, 1);
            }

            carryOut = 0;
        }

        private void Reset()
        {
            CacheHitboxes();
        }

        private void OnValidate()
        {
            CacheHitboxes();
        }

        private void CacheHitboxes()
        {
            weaponHitboxes.RemoveAll(hitbox => hitbox == null);

            if (weaponHitboxes.Count == 0 && weaponCollider != null)
            {
                Hitbox legacyHitbox = weaponCollider.GetComponent<Hitbox>();
                if (legacyHitbox != null)
                {
                    weaponHitboxes.Add(legacyHitbox);
                }
            }

            if (weaponHitboxes.Count > 0)
            {
                return;
            }

            Hitbox[] hitboxes = GetComponentsInChildren<Hitbox>(true);
            for (int i = 0; i < hitboxes.Length; i++)
            {
                if (hitboxes[i] == null)
                {
                    continue;
                }

                weaponHitboxes.Add(hitboxes[i]);
            }
        }
    }
}
