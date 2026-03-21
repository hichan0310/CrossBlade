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
    
    public enum FacingMode
    {
        UseActorDefault,   
        AutoFaceTarget,    
        LockCurrentFacing, 
        FaceTargetOnStartOnly  
    }
    
    public enum MovementMode
    {
        None,
        StopAtRange,
        PassThroughTarget,
        FixedDistanceForward,
        FixedSpeedForward
    }

    public enum MovementPhase
    {
        None,
        StartupOnly,
        ActiveOnly,
        StartupAndActive
    }

    public class Move : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string moveId = "move";
        [SerializeField] private MoveCategory category = MoveCategory.Neutral;

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
        
        [Header("Facing")]
        [SerializeField] private FacingMode facingMode = FacingMode.UseActorDefault;

        internal FacingMode FacingMode => facingMode;
        

        [Header("Movement")]
        [SerializeField] private MovementMode movementMode = MovementMode.None;
        [SerializeField] private MovementPhase movementPhase = MovementPhase.None;
        [SerializeField] private float speed = 0f;
        [SerializeField, Min(0f)] private float stopDistance = 0f;
        [SerializeField, Min(0f)] private float passThroughOffset = 0f;
        [SerializeField,] private float fixedTravelDistance = 0f;

        [Header("Visual Timing")]
        [SerializeField] private bool delayVisualReveal = false;
        [SerializeField, Range(0f, 1f)] private float visualRevealProgress = 0f;
        [SerializeField] private Transform visualRoot;

        internal bool DelayVisualReveal => delayVisualReveal;
        internal float VisualRevealProgress => visualRevealProgress;
        internal Transform VisualRoot => visualRoot;

        internal MovementMode MovementMode => movementMode;
        internal MovementPhase MovementPhase => movementPhase;
        internal float Speed => speed;
        internal float StopDistance => stopDistance;
        internal float PassThroughOffset => passThroughOffset;
        internal float FixedTravelDistance => fixedTravelDistance;
       
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
        
        [SerializeField] private List<MoveEffects> onAttackEffects;


        internal virtual void Play(ActorType actorType, CombatContext combatContext, int force, out int carryOut)
        {
            Debug.Log(this.gameObject.name);
            // foreach (Hitbox weaponHitbox in this.weaponHitboxes)
            // {
            //     weaponHitbox.Collider.enabled = true;
            //     weaponHitbox.Collider.gameObject.SetActive(true);
            // }

            carryOut = 0;
        }

        internal virtual Move OnHit(Actor actor, CombatContext combatContext)
        {
            actor.ClearQueuedMovesForInterrupt();
            actor.EnqueueInterruptFollowUps(hitMove, 1);
            return hitMove;
        }

        internal virtual Move OnGuard(Actor actor, CombatContext combatContext)
        {
            actor.ClearQueuedMovesForInterrupt();
            actor.EnqueueInterruptFollowUps(guardMove, 2);
            return guardMove;
        }

        internal virtual void OnClash(Actor actor, CombatContext combatContext)
        {
            
        }

        internal virtual void OnAttack(Actor actor, CombatContext combatContext)
        {
            onAttackEffects.ForEach(effect => effect.gameObject.SetActive(true));
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
