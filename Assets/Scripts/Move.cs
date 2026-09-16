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
        FixedSpeedForward,
        CurveXY
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

        [Header("Persistent Character Animation")]
        [Tooltip("Optional clip on the Actor's persistent rig. Sampled using total MoveProgress, including startup.")]
        [SerializeField] private AnimationClip characterAnimation;
        internal AnimationClip CharacterAnimation => characterAnimation;

        [Header("Combat")] 
        [SerializeField, Min(0f)] private float damageBase;
        [SerializeField, Min(0f)] private float damagePerPower;
        [SerializeField, Min(0f)] private float stanceDamageBase;
        [SerializeField, Min(0f)] private float stanceDamagePerPower;
        [SerializeField, Min(0f)] private int stanceRecovery;
        [SerializeField, Min(0f)] private float stanceUsageBase;
        [SerializeField, Min(0f)] private float stanceUsagePerPower;

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
        [SerializeField] private AnimationCurve movementX = AnimationCurve.Linear(0, 0, 1, 0);
        [SerializeField] private AnimationCurve movementY = AnimationCurve.Linear(0, 0, 1, 0);

        public Vector2 EvaluateMovementOffset(float progress, int facingSign)
        {
            progress = Mathf.Clamp01(progress);
            return new Vector2(
                (movementX == null ? 0 : movementX.Evaluate(progress) - movementX.Evaluate(0)) * (facingSign < 0 ? -1 : 1),
                movementY == null ? 0 : movementY.Evaluate(progress) - movementY.Evaluate(0));
        }

        [Header("Visual Reveal")]
        [SerializeField] private bool delayVisualReveal = false;
        [SerializeField, Range(0f, 1f)] private float visualRevealProgress = 0f;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private bool showPreviousVisual = false;

        internal MoveCategory Category => category;
        internal bool UsesForce => category == MoveCategory.Attack;

        internal bool DelayVisualReveal => delayVisualReveal;
        internal float VisualRevealProgress => visualRevealProgress;
        internal Transform VisualRoot => visualRoot;
        internal bool ShowPreviousVisual => showPreviousVisual;


        internal MovementMode MovementMode => movementMode;
        internal MovementPhase MovementPhase => movementPhase;
        internal float Speed => speed;
        internal float StopDistance => stopDistance;
        internal float PassThroughOffset => passThroughOffset;
        internal float FixedTravelDistance => fixedTravelDistance;
       
        internal string MoveId => moveId;
        internal IList<Hitbox> WeaponHitboxes => weaponHitboxes;
        internal Collider2D BodyCollider => bodyCollider;
        [SerializeField, HideInInspector] private MoveReactionDefaults reactionDefaults;
        public Move ResolvedHitMove => hitMove;
        public Move ResolvedGuardMove => guardMove;
        internal Move HitMove => ResolvedHitMove;
        internal Move GuardMove => ResolvedGuardMove;
        internal IList<Move> After => after;
        internal bool Guardable => guardable;
        internal bool SkipAdditionalInterruptFollowUp => skipAdditionalInterruptFollowUp;
        internal virtual float Duration => duration;
        internal virtual int StanceRecovery => stanceRecovery;
        
        [SerializeField] private List<MoveEffects> onAttackEffects = new List<MoveEffects>();

        internal void BindGraphFromSource(Move source)
        {
            if (source == null)
            {
                return;
            }

            hitMove = source.hitMove;
            guardMove = source.guardMove;
            reactionDefaults = source.reactionDefaults;
            after = new List<Move>(source.after);
            guardable = source.guardable;
            skipAdditionalInterruptFollowUp = source.skipAdditionalInterruptFollowUp;
        }

        internal int getPower(int force)
        {
            return force;
        }

        internal float getDamage(int power)
        {
            return damageBase + damagePerPower*power;
        }

        internal float getStanceDamage(int power)
        {
            return stanceDamageBase + stanceDamagePerPower*power;
        }


        internal virtual void Play(ActorType actorType, CombatContext combatContext, int force, out int carryOut)
        {
            Actor actor = actorType == ActorType.Player ? combatContext.user : combatContext.target;
            actor.ApplyStanceDamage((int)(force*stanceUsagePerPower+stanceUsageBase));
            foreach (Hitbox weaponHitbox in this.weaponHitboxes)
            {
                weaponHitbox.Collider.enabled = true;
                weaponHitbox.Collider.gameObject.SetActive(true);
            }

            carryOut = 0;
        }

        internal virtual Move OnHit(Actor actor, CombatContext combatContext)
        {
            actor.ClearQueuedMovesForInterrupt();
            var target = actor.CombatMoveGraph != null ? actor.CombatMoveGraph.ResolveReaction(this, false) : hitMove;
            actor.EnqueueInterruptFollowUps(target, 1);
            return target;
        }

        internal virtual Move OnGuard(Actor actor, CombatContext combatContext)
        {
            actor.ClearQueuedMovesForInterrupt();
            var target = actor.CombatMoveGraph != null ? actor.CombatMoveGraph.ResolveReaction(this, true) : guardMove;
            actor.EnqueueInterruptFollowUps(target, 2);
            return target;
        }

        internal virtual void OnClash(Actor actor, CombatContext combatContext)
        {
            
        }

        internal virtual void OnAttack(Actor actor, CombatContext combatContext)
        {
            onAttackEffects?.ForEach(effect => { if (effect != null) effect.gameObject.SetActive(true); });
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
