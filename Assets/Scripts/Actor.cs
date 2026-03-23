using System;
using System.Collections.Generic;
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
        public int forceCarryOut => carryOut;
        public int forceCarryIn { set { carryIn = value; } }

        public void Play(int inputForce, CombatContext combatContext, ActorType actorType)
        {
            move.Play(actorType, combatContext, carryIn + inputForce, out carryOut);
        }
    }

    public struct MoveRuntime
    {
        public Move move;
        public int selectedForce;
        public int force;
        public float elapsed;

        public MoveRuntime(Move move, int selectedForce, int force)
        {
            this.move = move;
            this.selectedForce = Mathf.Clamp(selectedForce, 1, 5);
            this.force = force;
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
        [SerializeField] private ActorType actorType;

        [Header("Identity")]
        [SerializeField] private string actorId = "actor";

        [Header("Stats")]
        [SerializeField] private int maxHp = 100;
        [SerializeField] private int hp = 100;
        [SerializeField] private int maxStance = 100;
        [SerializeField] private int stance = 100;
        [SerializeField] private int maxSpecialForce = 20;
        [SerializeField] private int specialForce;
        [SerializeField, Min(0f)] private float knockbackResistance = 0f;

        [Header("Chain")]
        [SerializeField, Min(0f)] private float chainStepBonus = 0.05f;
        [SerializeField, Min(1f)] private float chainMaxMultiplier = 1.5f;

        [Header("References")]
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private ActorVisualController visualController;
        [SerializeField] private Transform facingRoot;
        [SerializeField] private bool positiveScaleFacesRight = true;

        [Header("Startup")]
        [SerializeField] private Move initialMove;

        [Header("Debug")]
        [SerializeField] private float moveStartDelay = 0.1f;

        private readonly Queue<QueuedMove> _queue = new Queue<QueuedMove>();
        private MoveRuntime _current;
        private QueuedMove _currentQueuedMove;
        private bool _hasCurrent;
        private int _chainCount;
        private Vector2 _recoilVelocity;
        private float _recoilFriction;
        private float _nextAttackDamageMultiplier = 1f;
        private int _carriedForce;
        private bool _currentMoveExchanged;
        private float _moveStartupRemaining;
        private Vector2 _moveStartPosition;
        private int _moveStartFacingSign = 1;
        private bool _startFacingConsumed;

        private Move CurrentMoveInstance => visualController != null ? visualController.CurrentMoveInstance : null;

        internal bool IsMoveRunning => _hasCurrent;
        internal bool IsReadyForExchange => _hasCurrent && _moveStartupRemaining <= 0f;
        internal bool HasResolvedExchange => _currentMoveExchanged;
        internal MoveRuntime Current => _current;
        internal int QueueCount => _queue.Count;
        internal bool IsGuardBroken => stance <= 0;
        internal bool CanGuard => _hasCurrent && !IsGuardBroken && CurrentMoveInstance != null && CurrentMoveInstance.Guardable;
        internal Vector2 Position => body != null ? body.position : (Vector2)transform.position;
        internal float ChainMultiplier => Mathf.Min(1f + (_chainCount * chainStepBonus), chainMaxMultiplier);
        internal float KnockbackResistance => knockbackResistance + (CurrentMoveInstance != null ? CurrentMoveInstance.KnockbackResistance : 0f);
        internal int SpecialForce => specialForce;
        internal IList<Hitbox> weaponHitboxes => CurrentMoveInstance != null ? CurrentMoveInstance.WeaponHitboxes : Array.Empty<Hitbox>();
        internal Collider2D bodyCollider => CurrentMoveInstance != null ? CurrentMoveInstance.BodyCollider : null;

        internal string ActorId => actorId;
        internal int Hp => hp;
        internal int MaxHp => maxHp;
        internal int Stance => stance;
        internal int MaxStance => maxStance;
        internal int MaxSpecialForce => maxSpecialForce;
        internal bool IsInStartup => _hasCurrent && _moveStartupRemaining > 0f;
        internal float StartupRemaining => _moveStartupRemaining;
        internal string CurrentMoveId => _current.move != null ? _current.move.MoveId : "-";
        internal Vector2 MoveStartPosition => _moveStartPosition;
        internal int MoveStartFacingSign => _moveStartFacingSign;
        internal bool HasMoveVisual => visualController != null && visualController.HasMoveVisual;

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

        internal float StartupProgress
        {
            get
            {
                if (moveStartDelay <= 0f)
                {
                    return 1f;
                }

                return Mathf.Clamp01((moveStartDelay - _moveStartupRemaining) / moveStartDelay);
            }
        }

        internal float ActiveProgress
        {
            get
            {
                if (!_hasCurrent || _current.move == null || _current.move.Duration <= 0f)
                {
                    return 1f;
                }

                return Mathf.Clamp01(_current.elapsed / _current.move.Duration);
            }
        }

        internal float MoveProgress
        {
            get
            {
                if (!_hasCurrent)
                {
                    return 1f;
                }

                float startupElapsed = Mathf.Max(0f, moveStartDelay - _moveStartupRemaining);
                float activeElapsed = _current.elapsed;
                float moveDuration = _current.move != null ? _current.move.Duration : 0f;
                float totalDuration = moveStartDelay + moveDuration;

                if (totalDuration <= 0f)
                {
                    return 1f;
                }

                return Mathf.Clamp01((startupElapsed + activeElapsed) / totalDuration);
            }
        }

        internal bool TryConsumeStartFacing()
        {
            if (!_hasCurrent || _startFacingConsumed)
            {
                return false;
            }

            _startFacingConsumed = true;
            return true;
        }

        internal void SyncMoveStartFacing()
        {
            _moveStartFacingSign = FacingSign;
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

            if (initialMove != null)
            {
                Enqueue(initialMove);
            }
        }

        internal void Enqueue(Move move)
        {
            if (move == null)
            {
                return;
            }

            if (!_hasCurrent && _queue.Count == 0)
            {
                _carriedForce = 0;
            }

            _queue.Enqueue(new QueuedMove { move = move });
        }

        internal void ClearQueuedMovesForInterrupt()
        {
            ClearQueue();
        }

        internal void EnqueueInterruptFollowUps(Move move, int count)
        {
            if (move == null || count <= 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                _queue.Enqueue(new QueuedMove { move = move });
            }
        }

        private void ClearQueue()
        {
            _queue.Clear();
            _carriedForce = 0;
        }

        internal bool TryStartNextMove(Func<Actor, Move, int> forceSelector, CombatContext combatContext)
        {
            if (_hasCurrent)
            {
                return false;
            }

            if (_queue.Count == 0)
            {
                return false;
            }

            QueuedMove queued = _queue.Dequeue();
            if (queued.move == null)
            {
                return false;
            }

            int inputForce = forceSelector != null ? forceSelector(this, queued.move) : 3;
            return StartMove(queued, inputForce, combatContext);
        }

        internal void Tick(float deltaTime)
        {
            if (!_hasCurrent)
            {
                ApplyRecoil(deltaTime);
                visualController?.RefreshMoveVisualState(false, 1f);
                return;
            }

            if (_moveStartupRemaining > 0f)
            {
                _moveStartupRemaining = Mathf.Max(0f, _moveStartupRemaining - deltaTime);
                ApplyRecoil(deltaTime);
                visualController?.RefreshMoveVisualState(_hasCurrent, MoveProgress);
                return;
            }

            _current.elapsed += deltaTime;
            ApplyRecoil(deltaTime);

            if (_current.IsDone)
            {
                FinishCurrentMove();
            }

            visualController?.RefreshMoveVisualState(_hasCurrent, MoveProgress);
        }

        internal void Interrupt(MoveEventType trigger, InterruptReason reason, CombatContext combatContext)
        {
            if (!_hasCurrent)
            {
                return;
            }

            MoveRuntime interrupted = _current;
            QueuedMove interruptedQueuedMove = _currentQueuedMove;
            Move interruptedSourceMove = interruptedQueuedMove != null ? interruptedQueuedMove.move : interrupted.move;
            Move next = null;

            _hasCurrent = false;
            _currentQueuedMove = null;
            _carriedForce = 0;
            _currentMoveExchanged = false;
            _moveStartupRemaining = 0f;

            if (visualController != null)
            {
                visualController.ReleaseMoveInstance(CurrentMoveInstance);
            }

            _chainCount = 0;

            switch (trigger)
            {
                case MoveEventType.Hit:
                    next = interruptedSourceMove != null ? interruptedSourceMove.OnHit(this, combatContext) : null;
                    break;

                case MoveEventType.Guard:
                    next = interruptedSourceMove != null ? interruptedSourceMove.OnGuard(this, combatContext) : null;
                    break;
            }

            if (next == null)
            {
                return;
            }

            QueuedMove queued = new QueuedMove { move = next };
            if (interruptedQueuedMove != null)
            {
                queued.forceCarryIn = interruptedQueuedMove.forceCarryOut;
            }

            StartMove(queued, interrupted.selectedForce, combatContext);
        }

        internal void ApplyHpDamage(int amount)
        {
            hp = Mathf.Max(0, hp - Mathf.Max(0, amount));
        }

        internal void ApplyStanceDamage(int amount)
        {
            int reduced = Mathf.Max(0, amount - (CurrentMoveInstance != null ? CurrentMoveInstance.StanceDamageResistance : 0));
            stance = Mathf.Max(0, stance - reduced);
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

        internal void ResetAndApplyKnockback(Vector2 direction, float initialSpeed, float friction)
        {
            if (direction.sqrMagnitude <= 0f || initialSpeed <= 0f)
            {
                _recoilVelocity = Vector2.zero;
                _recoilFriction = Mathf.Max(0f, friction);
                return;
            }

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
            }

            _recoilVelocity = direction.normalized * initialSpeed;
            _recoilFriction = Mathf.Max(0f, friction);
        }

        private bool StartMove(QueuedMove queued, int inputForce, CombatContext combatContext)
        {
            if (queued == null || queued.move == null)
            {
                return false;
            }

            if (visualController == null)
            {
                visualController = GetComponent<ActorVisualController>();
            }

            if (!_hasCurrent && CurrentMoveInstance != null && visualController != null)
            {
                visualController.ReleaseMoveInstance(CurrentMoveInstance);
            }

            int selectedForce = Mathf.Clamp(inputForce, 1, 5);
            Move sourceMove = queued.move;
            Move runtimeMove = visualController != null ? visualController.CreateMoveInstance(sourceMove) : null;
            if (runtimeMove == null)
            {
                return false;
            }

            int carriedForce = _carriedForce;
            _carriedForce = 0;

            queued.forceCarryIn = carriedForce;
            _currentQueuedMove = queued;
            _current = new MoveRuntime(runtimeMove, selectedForce, selectedForce + carriedForce);
            _hasCurrent = true;
            _currentMoveExchanged = false;
            _moveStartupRemaining = moveStartDelay;

            _moveStartPosition = Position;
            _moveStartFacingSign = FacingSign;
            _startFacingConsumed = false;

            queued.move = runtimeMove;
            queued.Play(selectedForce, combatContext, actorType);
            queued.move = sourceMove;

            stance = Mathf.Max(0, stance - Mathf.Max(0, runtimeMove.StanceCost));
            visualController?.RefreshMoveVisualState(_hasCurrent, MoveProgress);
            GainSpecialForce(selectedForce);

            return true;
        }

        private void FinishCurrentMove()
        {
            MoveRuntime finished = _current;
            QueuedMove finishedQueuedMove = _currentQueuedMove;
            Move finishedSourceMove = finishedQueuedMove != null ? finishedQueuedMove.move : finished.move;

            _hasCurrent = false;
            _currentQueuedMove = null;
            _currentMoveExchanged = false;
            _moveStartupRemaining = 0f;

            if (finished.move != null)
            {
                RecoverStance(finished.move.StanceRecovery);
            }

            _carriedForce = finishedQueuedMove != null ? finishedQueuedMove.forceCarryOut : 0;

            if (_queue.Count > 0)
            {
                _chainCount++;
                _queue.Peek().forceCarryIn = _carriedForce;
            }
            else
            {
                _chainCount = 0;
            }

            if (_queue.Count > 0 || finishedSourceMove == null || finishedSourceMove.After.Count <= 0)
            {
                return;
            }

            int nextIndex = UnityEngine.Random.Range(0, finishedSourceMove.After.Count);
            QueuedMove autoQueuedMove = new QueuedMove { move = finishedSourceMove.After[nextIndex] };
            autoQueuedMove.forceCarryIn = _carriedForce;
            _queue.Enqueue(autoQueuedMove);
        }

        internal void MoveBy(Vector2 delta)
        {
            SetActorPosition(Position + delta);
        }

        internal void MarkCurrentMoveExchanged()
        {
            _currentMoveExchanged = true;
        }

        internal void MoveTo(Vector2 position)
        {
            SetActorPosition(position);
        }

        private void SetActorPosition(Vector2 position)
        {
            if (body != null)
            {
                body.MovePosition(position);
                return;
            }

            transform.position = position;
        }

        private void ApplyRecoil(float deltaTime)
        {
            if (_recoilVelocity.sqrMagnitude <= 0f || deltaTime <= 0f)
            {
                return;
            }

            Vector2 delta = _recoilVelocity * deltaTime;
            SetActorPosition(Position + delta);

            float speed = Mathf.MoveTowards(_recoilVelocity.magnitude, 0f, _recoilFriction * deltaTime);
            _recoilVelocity = speed > 0f ? _recoilVelocity.normalized * speed : Vector2.zero;
        }
    }
}