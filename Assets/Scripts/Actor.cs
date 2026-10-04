using System;
using System.Collections.Generic;
using Scripts.CharAIs;
using UnityEngine;

namespace Scripts
{
    public enum InterruptReason
    {
        Hit,
        Guard,
        Clash,
        Forced
    }

    public enum ActorType
    {
        Player,
        Enemy
    }

    [Serializable]
    public class QueuedMove
    {
        private int carryIn = 0;
        private int carryOut = 0;
        public Move move;
        public int queueRecoveryBonus;
        public int forceCarryOut => carryOut;
        public int forceCarryIn { set { carryIn = value; } }
        public void SetForceCarryOut(int value) { carryOut = Mathf.Clamp(value, 0, 4); }

        public void Play(int inputForce, CombatContext combatContext, ActorType actorType)
        {
            move.Play(actorType, combatContext, carryIn + inputForce, out carryOut);
        }
    }

    public struct MoveRuntime
    {
        public Move move;
        public int force;
        public int chaseForce;
        public float elapsed;

        public MoveRuntime(Move move, int force)
        {
            this.move = move;
            this.force = force;
            chaseForce = 0;
            elapsed = 0f;
        }

        public float Normalized
        {
            get
            {
                if (move == null || move.Duration <= 0f)
                {
                    return 1f;
                }

                return Mathf.Clamp01(elapsed / move.Duration);
            }
        }

        public bool IsDone
        {
            get
            {
                if (move == null)
                {
                    return true;
                }

                return elapsed >= move.Duration;
            }
        }
    }

    public class Actor : MonoBehaviour
    {
        [SerializeField, Tooltip("이 캐릭터가 사용하는 전투 그래프. 공통 방어·피격은 이 그래프에서 가져옵니다.")]
        private CombatMoveGraphAsset combatMoveGraph;
        public CombatMoveGraphAsset CombatMoveGraph => combatMoveGraph;
        [SerializeField] private ActorType actorType;

        [Header("Identity")]
        [SerializeField] private string actorId = "actor";

        [Header("Stats")]
        [SerializeField] private int maxHp = 100;
        [SerializeField] private int hp = 100;
        [SerializeField] private int maxStance = 100;
        [SerializeField] private int stance = 100;
        [SerializeField, Min(0)] private int baseStanceRecoveryPerMove = 10;
        [SerializeField] private int maxSpecialForce = 20;
        [SerializeField] private int specialForce;
        [SerializeField, Range(1, 5)] public int pendingForce = 1;

        [Header("Chain")]
        [SerializeField, Min(0f)] private float chainStepBonus = 0.05f;
        [SerializeField, Min(1f)] private float chainMaxMultiplier = 1.5f;

        [Header("References")]
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private ActorVisualController visualController;
        [SerializeField] private ActorActionController actionController;
        [SerializeField] private Transform facingRoot;
        [SerializeField] private bool positiveScaleFacesRight = true;

        [Header("Startup")]
        [SerializeField] private Move initialMove;

        [Header("Debug")]
        [SerializeField] private float moveStartDelay = 0.1f;
        
        [Header("Input")]
        [SerializeField] private PlanMaker planMaker;
        public PlanMaker PlanMaker => planMaker;
        internal bool OnlineControlled { get; private set; }
        internal void SetOnlinePlanner(PlanMaker planner)
        {
            OnlineControlled = true;
            planMaker = planner;
            DespawnPlanInputUI();
        }
        internal void ApplyOnlineState(int health, int posture, int special, Vector2 position, int facing)
        {
            hp = Mathf.Clamp(health, 0, maxHp);
            stance = Mathf.Clamp(posture, 0, maxStance);
            specialForce = Mathf.Clamp(special, 0, maxSpecialForce);
            SetActorPosition(position);
            SetFacing(facing);
        }
        
        [SerializeField] private bool gettingForce = false;
        public bool GettingForce => gettingForce;
        [SerializeField] private bool gettingForceFinished=false;
        public bool GettingForceFinished => gettingForceFinished;
        [SerializeField] private float gettingForceTimer = 0f;
        [SerializeField] private Transform planUIRoot;
        [SerializeField] private bool gettingPlan = false;
        public bool GettingPlan => gettingPlan;
        [SerializeField, Min(0.1f)] private float planDecisionSeconds = 3f;
        private float planTimeRemaining;

        [SerializeField] private bool gettingPlanFinished = false;
        public bool GettingPlanFinished => gettingPlanFinished;

        [SerializeField] private Move plannedMove;
        internal Move PlannedMove => plannedMove;
        internal bool HasPlannedMove => plannedMove != null;
        internal Move PlanningMove => actionController != null && actionController.PlanningSourceMove != null
            ? actionController.PlanningSourceMove : IdleMove;
        internal Move IdleMove => combatMoveGraph != null ? combatMoveGraph.ResolveIdleMove() :
            (initialMove != null && initialMove.IsIdle ? initialMove : null);

        private GameObject _spawnedPlanInputUI;
        
        public void StartGettingPlan()
        {
            if (gettingPlan || gettingPlanFinished)
            {
                return;
            }

            gettingPlan = true;
            planTimeRemaining = planDecisionSeconds;
            SpawnPlanInputUI();
        }

        public void SubmitPlannedMove(Move move)
        {
            plannedMove = move;
            gettingPlan = false;
            gettingPlanFinished = true;
            DespawnPlanInputUI();
        }

        public void FailPlannedMove()
        {
            plannedMove = null;
            gettingPlan = false;
            gettingPlanFinished = true;
            DespawnPlanInputUI();
        }

        internal void CancelPlanning()
        {
            plannedMove = null;
            gettingPlan = false;
            gettingPlanFinished = false;
            planTimeRemaining = 0f;
            DespawnPlanInputUI();
        }

        internal bool TryConsumePlannedMove(out Move move)
        {
            move = plannedMove;
            plannedMove = null;
            gettingPlanFinished = false;
            return move != null;
        }

        private void SpawnPlanInputUI()
        {
            if (planMaker == null || planMaker.PlanInputUIPrefab == null || _spawnedPlanInputUI != null)
            {
                return;
            }

            Transform parent = planUIRoot != null ? planUIRoot : transform;
            _spawnedPlanInputUI = Instantiate(planMaker.PlanInputUIPrefab, parent);

            PlanInputUIBinder binder = _spawnedPlanInputUI.GetComponent<PlanInputUIBinder>();
            if (binder != null)
            {
                binder.Bind(this, planMaker);
            }
        }

        private void DespawnPlanInputUI()
        {
            if (_spawnedPlanInputUI == null)
            {
                return;
            }

            Destroy(_spawnedPlanInputUI);
            _spawnedPlanInputUI = null;
        }
        public void StartGettingForce()
        {
            gettingForce = true;
            gettingForceTimer = planMaker is PlayerInputManager pim ? pim.inputDuration : 0.1f;
        }

        internal int PendingForce => pendingForce;
        internal bool CanIncreasePendingForce()
        {
            return false;
        }

        internal bool TryIncreasePendingForce()
        {
            return false;
        }

        internal int ConsumePendingForce()
        {
            int value = Mathf.Clamp(pendingForce, 1, 5);
            pendingForce = 1;
            return value;
        }

        public void ForceUpdate(float deltaTime)
        {
            if (gettingPlan && !gettingPlanFinished)
            {
                if (QueueCount > 0) planTimeRemaining = planDecisionSeconds;
                else planTimeRemaining = Mathf.Max(0f, planTimeRemaining - deltaTime);
                // An empty queue holds the completed Move's pose until a real next Move is chosen.
                // Do not silently inject Idle when the planning timer reaches zero.
            }
            if (gettingForce)
            {
                gettingForceTimer -= deltaTime;
                if (gettingForceTimer <= 0f)
                {
                    gettingForceTimer = 0;
                    gettingForce = false;
                    gettingForceFinished = true;
                }
            }
        }

        [SerializeField] internal Vector2 _recoilVelocity;
        private Vector2 _recoilDirection;
        private float _recoilDistance;
        private float _recoilDuration;
        private float _recoilElapsed;
        [Header("Directional movement input")]
        [SerializeField, Min(0f)] private float minimumInputSpeedChange = .4f;
        [SerializeField, Min(0f)] private float maximumInputSpeedChange = 2f;
        private int directionalInput;
        private float _nextAttackDamageMultiplier = 1f;

        private Move CurrentMoveInstance => visualController != null ? visualController.CurrentMoveInstance : null;

        internal bool IsMoveRunning => actionController != null && actionController.IsMoveRunning;
        internal bool IsReadyForExchange => actionController != null && actionController.IsReadyForExchange;
        internal bool HasResolvedExchange => actionController != null && actionController.HasResolvedExchange;
        internal MoveRuntime Current => actionController != null ? actionController.Current : default;
        internal int QueueCount => actionController != null ? actionController.QueueCount : 0;
        internal int MaxQueuedMoves => actionController != null ? actionController.MaxQueuedMoves : 5;
        internal int NextQueueRecoveryBonus => actionController != null ? actionController.NextQueueRecoveryBonus : 0;
        internal bool HasQueueSpace => QueueCount < MaxQueuedMoves;
        internal bool IsGuardBroken => stance <= 0;
        internal bool CanGuard => IsMoveRunning && !IsGuardBroken && CurrentMoveInstance != null &&
                                  CurrentMoveInstance.Category != MoveCategory.HitReaction && CurrentMoveInstance.Guardable;
        internal Vector2 Position => body != null ? body.position : (Vector2)transform.position;
        internal float ChainMultiplier => Mathf.Min(1f + ((actionController != null ? actionController.ChainCount : 0) * chainStepBonus), chainMaxMultiplier);
        internal int SpecialForce => specialForce;
        internal IList<Hitbox> weaponHitboxes => CurrentMoveInstance != null ? CurrentMoveInstance.WeaponHitboxes : Array.Empty<Hitbox>();
        internal Collider2D bodyCollider => CurrentMoveInstance != null ? CurrentMoveInstance.BodyCollider : null;
        internal ActorActionController  ActionController => actionController;
        
        internal string ActorId => actorId;
        internal int Hp => hp;
        internal int MaxHp => maxHp;
        internal int Stance => stance;
        internal int MaxStance => maxStance;
        internal int BaseStanceRecoveryPerMove => baseStanceRecoveryPerMove;
        internal int MaxSpecialForce => maxSpecialForce;
        internal bool IsInStartup => actionController != null && actionController.IsMoveRunning && actionController.StartupRemaining > 0f;
        internal float StartupRemaining => actionController != null ? actionController.StartupRemaining : 0f;
        internal string CurrentMoveId => Current.move != null ? Current.move.MoveId : "-";
        internal Vector2 MoveStartPosition => actionController != null ? actionController.MoveStartPosition : Position;
        internal int MoveStartFacingSign => actionController != null ? actionController.MoveStartFacingSign : FacingSign;
        internal bool HasMoveVisual => visualController != null && visualController.HasMoveVisual;
        internal float MoveStartDelay => moveStartDelay;
        internal ActorType Kind => actorType;
        internal bool HasVisualController => visualController != null;
        internal Move CurrentMoveVisual => CurrentMoveInstance;

        internal int FacingSign
        {
            get
            {
                Transform root = facingRoot != null ? facingRoot : transform;
                float sign = Mathf.Sign(root.localScale.x);

                if (Mathf.Abs(sign) <= 0.001f)
                {
                    sign = 1f;
                }

                if (!positiveScaleFacesRight)
                {
                    sign *= -1f;
                }

                return sign >= 0f ? 1 : -1;
            }
        }

        internal float StartupProgress => actionController != null ? actionController.StartupProgress : 1f;
        internal float ActiveProgress => actionController != null ? actionController.ActiveProgress : 1f;
        internal float MoveProgress => actionController != null ? actionController.MoveProgress : 1f;

        internal bool TryConsumeStartFacing()
        {
            return actionController != null && actionController.TryConsumeStartFacing();
        }

        internal void SyncMoveStartFacing()
        {
            actionController?.SyncMoveStartFacing();
        }

        internal void FaceTowards(Vector2 targetPosition)
        {
            float deltaX = targetPosition.x - Position.x;
            if (Mathf.Abs(deltaX) <= 0.1f)
            {
                return;
            }

            SetFacing(deltaX > 0f ? 1 : -1);
        }

        private void SetFacing(int direction)
        {
            Transform root = facingRoot != null ? facingRoot : transform;

            Vector3 scale = root.localScale;
            float absX = Mathf.Abs(scale.x);
            float sign = direction > 0 ? 1f : -1f;

            if (!positiveScaleFacesRight)
            {
                sign *= -1f;
            }

            scale.x = absX * sign;
            root.localScale = scale;
        }

        private void Awake()
        {
            if (visualController == null)
            {
                visualController = GetComponent<ActorVisualController>();
            }

            if (actionController == null)
            {
                actionController = GetComponent<ActorActionController>();
            }

            if (actionController != null)
            {
                actionController.Initialize(this);
            }

            var startingMove = combatMoveGraph != null ? combatMoveGraph.startMove : initialMove;
            if (startingMove != null)
            {
                Enqueue(startingMove);
            }
        }

        internal bool Enqueue(Move move)
        {
            return actionController != null && actionController.Enqueue(move);
        }

        internal void ClearQueuedMovesForInterrupt()
        {
            actionController?.ClearQueuedMovesForInterrupt();
        }

        internal void EnqueueInterruptFollowUps(Move move, int count)
        {
            actionController?.EnqueueInterruptFollowUps(move, count);
        }

        internal bool TryStartNextMove(Func<Actor, Move, int> forceSelector, CombatContext combatContext)
        {
            gettingForceFinished = false;
            return actionController != null && actionController.TryStartNextMove(forceSelector, combatContext);
        }

        internal void Tick(float deltaTime)
        {
            actionController?.Tick(deltaTime);
        }

        internal void Interrupt(MoveEventType trigger, InterruptReason reason, CombatContext combatContext)
        {
            actionController?.Interrupt(trigger, reason, combatContext);
        }

        internal void InterruptWithFollowUps(MoveEventType trigger, InterruptReason reason,
            CombatContext combatContext, int followUpCount)
        {
            actionController?.InterruptWithFollowUps(trigger, reason, combatContext, followUpCount);
        }

        internal void ApplyHpDamage(int amount)
        {
            hp = Mathf.Max(0, hp - Mathf.Max(0, amount));
        }

        internal void ApplyStanceDamage(int amount)
        {
            stance = Mathf.Max(0, stance - amount);
        }

        internal bool TrySpendStance(int amount)
        {
            if (amount < 0 || stance < amount) return false;
            stance -= amount;
            return true;
        }

        internal void RecoverStance(int amount)
        {
            stance = Mathf.Clamp(stance + Mathf.Max(0, amount), 0, maxStance);
        }

        internal void GainSpecialForce(int amount)
        {
            if (amount > 0)
            {
                specialForce = Mathf.Clamp(specialForce + amount, 0, maxSpecialForce);
            }
        }

        internal bool CanSpendSpecialForce(int amount)
        {
            return amount >= 0 && specialForce >= amount;
        }

        internal bool SpendSpecialForce(int amount)
        {
            if (!CanSpendSpecialForce(amount))
            {
                return false;
            }

            specialForce -= amount;
            return true;
        }

        internal void SetNextAttackDamageMultiplier(float multiplier)
        {
            _nextAttackDamageMultiplier = Mathf.Max(1f, multiplier);
        }

        internal float ConsumeNextAttackDamageMultiplier()
        {
            float value = _nextAttackDamageMultiplier;
            _nextAttackDamageMultiplier = 1f;
            return value;
        }

        internal void ResetAndApplyKnockbackDistance(Vector2 direction, float distance, float duration)
        {
            if (direction.sqrMagnitude <= 0f || distance <= 0f)
            {
                _recoilVelocity = Vector2.zero;
                _recoilDistance = 0f;
                _recoilElapsed = 0f;
                return;
            }

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
            }

            _recoilDirection = direction.normalized;
            _recoilDistance = distance;
            _recoilDuration = Mathf.Max(.05f, duration);
            _recoilElapsed = 0f;
            _recoilVelocity = _recoilDirection * (2f * distance / _recoilDuration);
        }

        internal void SetDirectionalInput(int direction)
        {
            directionalInput = Mathf.Clamp(direction, -1, 1);
        }

        internal int DirectionalInput => directionalInput;

        internal float AdjustHorizontalMovement(float distance, float deltaTime)
        {
            if (Mathf.Abs(distance) < 0.000001f || deltaTime <= 0f || directionalInput == 0)
                return distance;
            float baseSpeed = Mathf.Abs(distance) / deltaTime;
            float minimum = Mathf.Max(0f, minimumInputSpeedChange);
            float maximum = Mathf.Max(minimum, maximumInputSpeedChange);
            float speedChange = Mathf.Clamp(baseSpeed * 0.2f, minimum, maximum);
            bool sameDirection = Mathf.Sign(distance) == directionalInput;
            // Opposing input can stop a very slow motion, but must not reverse it.
            float adjustedSpeed = sameDirection ? baseSpeed + speedChange : Mathf.Max(0f, baseSpeed - speedChange);
            return Mathf.Sign(distance) * adjustedSpeed * deltaTime;
        }

        internal void MoveBy(Vector2 delta)
        {
            // Contact is resolved by the combat sweep, before either fighter is ticked.
            // Commit now so the second fighter and this frame's exchange see the result.
            var position = Position + delta;
            if (body != null) body.position = position;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            Physics2D.SyncTransforms();
        }

        internal void MarkCurrentMoveExchanged()
        {
            actionController?.MarkCurrentMoveExchanged();
        }

        internal void MoveTo(Vector2 position)
        {
            SetActorPosition(position);
        }

        private void SetActorPosition(Vector2 position)
        {
            if (body != null) body.position = position;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            Physics2D.SyncTransforms();
        }

        private Vector2 ApplyRecoil(float deltaTime, bool applyPosition)
        {
            if (_recoilVelocity.sqrMagnitude <= 0f || deltaTime <= 0f)
            {
                return Vector2.zero;
            }

            float previous = Mathf.Clamp01(_recoilElapsed / _recoilDuration);
            _recoilElapsed = Mathf.Min(_recoilDuration, _recoilElapsed + deltaTime);
            float current = Mathf.Clamp01(_recoilElapsed / _recoilDuration);
            float step = _recoilDistance * ((1f - previous) * (1f - previous) -
                                            (1f - current) * (1f - current));
            Vector2 delta = _recoilDirection * step;
            delta.x = AdjustHorizontalMovement(delta.x, deltaTime);
            if (applyPosition) SetActorPosition(Position + delta);

            if (_recoilElapsed >= _recoilDuration)
            {
                _recoilVelocity = Vector2.zero;

                if (body != null)
                {
                    body.linearVelocity = Vector2.zero;
                }

                return delta;
            }

            _recoilVelocity = _recoilDirection * (2f * _recoilDistance * (1f - current) / _recoilDuration);
            return delta;
        }

        internal Vector2 ApplyRecoilFromActionController(float deltaTime, bool applyPosition = true)
        {
            return ApplyRecoil(deltaTime, applyPosition);
        }

        internal Move CreateMoveInstanceFromAction(Move template)
        {
            return visualController != null ? visualController.CreateMoveInstance(template) : null;
        }

        internal void ReleaseMoveInstanceFromAction(Move instance)
        {
            visualController?.ReleaseMoveInstance(instance);
        }

        internal void RefreshMoveVisualStateFromAction(bool hasCurrent, float moveProgress)
        {
            visualController?.RefreshMoveVisualState(hasCurrent, moveProgress);
        }

        internal void CapturePreviousVisualSnapshotFromAction()
        {
            visualController?.CapturePreviousVisualSnapshot();
        }

        internal void BeginPreviousVisualFromAction(bool enabled)
        {
            visualController?.BeginPreviousVisual(enabled);
        }

        internal void ClearPreviousVisualSnapshotFromAction()
        {
            visualController?.ClearPreviousVisualSnapshot();
        }
    }
}
