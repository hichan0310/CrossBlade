using System.Collections.Generic;
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

    public enum MoveCategory
    {
        Neutral,
        Attack,
        Dash,
        Guard,
        HitReaction
    }

    public class Move : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string moveId = "move";
        [SerializeField] private MoveCategory category = MoveCategory.Neutral;

        [Header("Runtime View")]
        [SerializeField] private Collider2D weaponCollider;
        [SerializeField] private Collider2D bodyCollider;

        [Header("Combat")]
        [SerializeField, Min(0f)] private float knockbackResistance;
        [SerializeField, Min(0)] private int stanceDamageResistance;

        [Header("Graph")]
        [SerializeField] private Move hitMove;
        [SerializeField] private Move guardMove;
        [SerializeField] private List<Move> after = new List<Move>();
        [SerializeField] private bool guardable;
        [SerializeField] private bool skipAdditionalInterruptFollowUp;

        internal string MoveId => moveId;
        internal MoveCategory Category => category;
        internal Collider2D WeaponCollider => weaponCollider;
        internal Collider2D BodyCollider => bodyCollider;
        internal float KnockbackResistance => knockbackResistance;
        internal int StanceDamageResistance => stanceDamageResistance;
        internal Move HitMove => hitMove;
        internal Move GuardMove => guardMove;
        internal IList<Move> After => after;
        internal bool Guardable => guardable;
        internal bool SkipAdditionalInterruptFollowUp => skipAdditionalInterruptFollowUp;
        internal virtual float Duration => 0.30f;
        internal virtual int Damage => 0;
        internal virtual int StanceDamage => 0;
        internal virtual int StanceCost => 0;
        internal virtual int StanceRecovery => 0;

        internal virtual void Play(CombatContext combatContext, int force, out int carryOut)
        {
            carryOut = 0;
        }
    }
}
