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

    [Serializable]
    public class QueuedMove
    {
        private int carryIn = 0;
        private int carryOut = 0;
        public Move move;
        public int forceCarryOut => carryOut;
        public int forceCarryIn { set { carryIn = value; } }

        public void Play(int inputForce, CombatContext combatContext)
        {
            this.move.Play(combatContext, carryIn + inputForce, out carryOut);
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
        [SerializeField] private Transform moveMount;

        [Header("Startup")]
        [SerializeField] private Move initialMove;

        private event Action<Actor, MoveRuntime> MoveStarted;
        private event Action<Actor, MoveRuntime> MoveFinished;
        private event Action<Actor, MoveRuntime, InterruptReason> MoveInterrupted;

        private readonly Queue<QueuedMove> _queue = new Queue<QueuedMove>();
        private MoveRuntime _current;
        private QueuedMove _currentQueuedMove;
        private bool _hasCurrent;
        private int _chainCount;
        private Vector2 _recoilVelocity;
        private float _recoilFriction;
        private float _nextAttackDamageMultiplier = 1f;
        private int _carriedForce;
        private MoveEventType? _forcedFollowUpTrigger;
        private int _forcedFollowUpRemaining;
        private Move _currentMoveInstance;

        internal bool IsMoveRunning => _hasCurrent;
        internal MoveRuntime Current => _current;
        internal int QueueCount => _queue.Count;
        internal bool IsGuardBroken => stance <= 0;
        internal bool CanGuard => !IsGuardBroken && _currentMoveInstance != null && _currentMoveInstance.Guardable;
        internal Vector2 Position => body != null ? body.position : (Vector2)transform.position;
        internal float ChainMultiplier => Mathf.Min(1f + (_chainCount * chainStepBonus), chainMaxMultiplier);
        internal float KnockbackResistance => knockbackResistance + (_currentMoveInstance != null ? _currentMoveInstance.KnockbackResistance : 0f);
        internal int SpecialForce => specialForce;
        internal Collider2D weaponCollider => _currentMoveInstance != null ? _currentMoveInstance.WeaponCollider : null;
        internal Collider2D bodyCollider => _currentMoveInstance != null ? _currentMoveInstance.BodyCollider : null;

        private void Awake()
        {
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

        private void ClearQueue()
        {
            _queue.Clear();
            _carriedForce = 0;
        }

        internal bool TryStartNextMove(Func<Actor, Move, int> forceSelector)
        {
            if (_hasCurrent || _queue.Count == 0)
            {
                return false;
            }

            QueuedMove queued = _queue.Dequeue();
            if (queued.move == null)
            {
                return false;
            }

            int inputForce = forceSelector != null ? forceSelector(this, queued.move) : 3;
            return StartMove(queued, inputForce);
        }

        internal void Tick(float deltaTime)
        {
            if (!_hasCurrent)
            {
                ApplyRecoil(deltaTime);
                return;
            }

            _current.elapsed += deltaTime;
            ApplyRecoil(deltaTime);

            if (_current.IsDone)
            {
                FinishCurrentMove();
            }
        }

        internal void Interrupt(MoveEventType trigger, InterruptReason reason)
        {
            if (!_hasCurrent)
            {
                return;
            }

            MoveRuntime interrupted = _current;
            QueuedMove interruptedQueuedMove = _currentQueuedMove;
            Move next = null;
            if (interrupted.move != null)
            {
                switch (trigger)
                {
                    case MoveEventType.Hit:
                        next = interrupted.move.HitMove;
                        break;
                    case MoveEventType.Guard:
                        next = interrupted.move.GuardMove;
                        break;
                    case MoveEventType.NormalEnd:
                        if (interrupted.move.After.Count > 0)
                        {
                            next = interrupted.move.After[0];
                        }
                        break;
                }
            }

            _hasCurrent = false;
            _currentQueuedMove = null;
            _carriedForce = 0;
            ReleaseMoveInstance(_currentMoveInstance);
            _currentMoveInstance = null;
            MoveInterrupted?.Invoke(this, interrupted, reason);

            _chainCount = 0;
            _forcedFollowUpTrigger = null;
            _forcedFollowUpRemaining = 0;

            if (reason == InterruptReason.Hit)
            {
                ClearQueue();
                _forcedFollowUpTrigger = MoveEventType.Hit;
                _forcedFollowUpRemaining = 1;
            }
            else if (reason == InterruptReason.Guard)
            {
                _forcedFollowUpTrigger = MoveEventType.Guard;
                _forcedFollowUpRemaining = 2;
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

            StartMove(queued, interrupted.selectedForce);
        }

        internal void ApplyHpDamage(int amount)
        {
            hp = Mathf.Max(0, hp - Mathf.Max(0, amount));
        }

        internal void ApplyStanceDamage(int amount)
        {
            int reduced = Mathf.Max(0, amount - (_currentMoveInstance != null ? _currentMoveInstance.StanceDamageResistance : 0));
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

        private bool StartMove(QueuedMove queued, int inputForce)
        {
            if (queued == null || queued.move == null)
            {
                return false;
            }

            int selectedForce = Mathf.Clamp(inputForce, 1, 5);
            Move runtimeMove = CreateMoveInstance(queued.move);
            if (runtimeMove == null)
            {
                return false;
            }

            int carriedForce = _carriedForce;
            _carriedForce = 0;

            queued.forceCarryIn = carriedForce;
            queued.move = runtimeMove;

            CombatContext context = new CombatContext
            {
                user = this
            };

            _currentMoveInstance = runtimeMove;
            _currentQueuedMove = queued;
            _current = new MoveRuntime(runtimeMove, selectedForce, selectedForce + carriedForce);
            _hasCurrent = true;

            queued.Play(selectedForce, context);
            stance = Mathf.Max(0, stance - Mathf.Max(0, runtimeMove.StanceCost));
            GainSpecialForce(selectedForce);
            MoveStarted?.Invoke(this, _current);
            return true;
        }

        private void FinishCurrentMove()
        {
            MoveRuntime finished = _current;
            QueuedMove finishedQueuedMove = _currentQueuedMove;

            _hasCurrent = false;
            _currentQueuedMove = null;

            if (finished.move != null)
            {
                RecoverStance(finished.move.StanceRecovery);
            }

            _carriedForce = finishedQueuedMove != null ? finishedQueuedMove.forceCarryOut : 0;

            MoveFinished?.Invoke(this, finished);
            ReleaseMoveInstance(_currentMoveInstance);
            _currentMoveInstance = null;

            if (_forcedFollowUpRemaining > 0)
            {
                if (finished.move != null && finished.move.SkipAdditionalInterruptFollowUp)
                {
                    _forcedFollowUpTrigger = null;
                    _forcedFollowUpRemaining = 0;
                }
                else if (_forcedFollowUpTrigger.HasValue && finished.move != null)
                {
                    Move forced = null;
                    switch (_forcedFollowUpTrigger.Value)
                    {
                        case MoveEventType.Hit:
                            forced = finished.move.HitMove;
                            break;
                        case MoveEventType.Guard:
                            forced = finished.move.GuardMove;
                            break;
                    }

                    _forcedFollowUpRemaining--;
                    if (_forcedFollowUpRemaining <= 0)
                    {
                        _forcedFollowUpTrigger = null;
                    }

                    if (forced != null)
                    {
                        QueuedMove forcedQueuedMove = new QueuedMove { move = forced };
                        forcedQueuedMove.forceCarryIn = _carriedForce;
                        StartMove(forcedQueuedMove, finished.selectedForce);
                        return;
                    }
                }
            }

            if (_queue.Count > 0)
            {
                _chainCount++;
                _queue.Peek().forceCarryIn = _carriedForce;
            }
            else
            {
                _chainCount = 0;
            }

            if (finished.move == null || finished.move.After.Count <= 0)
            {
                return;
            }

            QueuedMove autoQueuedMove = new QueuedMove { move = finished.move.After[0] };
            autoQueuedMove.forceCarryIn = _carriedForce;
            StartMove(autoQueuedMove, finished.selectedForce);
        }

        internal void MoveBy(Vector2 delta)
        {
            SetActorPosition(Position + delta);
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

        private Move CreateMoveInstance(Move template)
        {
            Transform parent = moveMount != null ? moveMount : transform;
            Move instance = Instantiate(template, parent);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            return instance;
        }

        private void ReleaseMoveInstance(Move instance)
        {
            if (instance == null)
            {
                return;
            }

            instance.gameObject.SetActive(false);
            Destroy(instance.gameObject);
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
