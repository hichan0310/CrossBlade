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

    public enum MoveCategory
    {
        Neutral,
        Attack,
        Dash,
        Guard,
        HitReaction
    }

    // 추가한거
    public enum FacingMode
    {
        UseActorDefault,   
        AutoFaceTarget,    
        LockCurrentFacing, 
        ForceFaceRight,    
        ForceFaceLeft      
    }
    // 추가한거


    //돌진 만들어본거 (안써도됨)
    public enum ApproachPhase
    {
        None,
        StartupOnly,
        ActiveOnly,
        StartupAndActive
    }

    public enum MovementMode
    {
        None,
        StopAtRange,
        PassThroughTarget,
        FixedDistanceForward
    }
    // 돌진 만들어본거 (안써도됨)

    public class Move : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string moveId = "move";
        [SerializeField] private MoveCategory category = MoveCategory.Neutral;

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

        [Header("Graph")]
        [SerializeField] private Move hitMove;
        [SerializeField] private Move guardMove;
        [SerializeField] private List<Move> after = new List<Move>();
        [SerializeField] private bool guardable = true;
        [SerializeField] private bool skipAdditionalInterruptFollowUp;
        // 추가한거
        [Header("Facing")]
        [SerializeField] private FacingMode facingMode = FacingMode.UseActorDefault;

        internal FacingMode FacingMode => facingMode;
        // 추가한거

        //돌진 만들어본거 (안써도됨)
        [Header("Approach")]
        [SerializeField, Min(0f)] private float startupApproachSpeed = 0f;
        [SerializeField, Min(0f)] private float activeApproachSpeed = 0f;
        [SerializeField, Min(0f)] private float approachStopDistance = 1.0f;

        internal float StartupApproachSpeed => startupApproachSpeed;
        internal float ActiveApproachSpeed => activeApproachSpeed;
        internal float ApproachStopDistance => approachStopDistance;

        [Header("Movement")]
        [SerializeField] private MovementMode movementMode = MovementMode.None;
        [SerializeField, Min(0f)] private float startupMoveSpeed = 0f;
        [SerializeField, Min(0f)] private float activeMoveSpeed = 0f;
        [SerializeField, Min(0f)] private float stopDistance = 1.0f;
        [SerializeField, Min(0f)] private float passThroughOffset = 0.5f;
        [SerializeField, Min(0f)] private float fixedTravelDistance = 1.5f;

        internal MovementMode MovementMode => movementMode;
        internal float StartupMoveSpeed => startupMoveSpeed;
        internal float ActiveMoveSpeed => activeMoveSpeed;
        internal float StopDistance => stopDistance;
        internal float PassThroughOffset => passThroughOffset;
        internal float FixedTravelDistance => fixedTravelDistance;
        // 돌진 만들어본거 (안써도됨)
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
