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
    public struct QueuedMove
    {
        public Move move;
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
                if (move == null || move.duration <= 0f)
                {
                    return 1f;
                }

                return Mathf.Clamp01(elapsed / move.duration);
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

                return elapsed >= move.duration;
            }
        }
    }

    public class Actor : MonoBehaviour
    {
        [Header("Identity")]
        public string actorId = "actor";

        [Header("Stats")]
        public int maxHp = 100;
        public int hp = 100;
        public int maxStance = 100;
        public int stance = 100;
        public int maxSpecialForce = 20;
        public int specialForce;
        [Min(0f)] public float knockbackResistance = 0f;

        [Header("Chain")]
        [Min(0f)] public float chainStepBonus = 0.05f;
        [Min(1f)] public float chainMaxMultiplier = 1.5f;

        [Header("References")]
        public Rigidbody2D body;
        public SpriteRenderer spriteRenderer;
        public Collider2D weaponCollider;
        public Collider2D guardCollider;
        public Collider2D hurtCollider;

        public event Action<Actor, MoveRuntime> MoveStarted;
        public event Action<Actor, MoveRuntime> MoveFinished;
        public event Action<Actor, MoveRuntime, InterruptReason> MoveInterrupted;

        private readonly Queue<QueuedMove> _queue = new Queue<QueuedMove>();
        private MoveRuntime _current;
        private bool _hasCurrent;
        private int _chainCount;
        private Vector2 _moveStartPosition;
        private Vector2 _moveEndPosition;
        private Vector2 _recoilVelocity;
        private float _recoilFriction;
        private float _nextAttackDamageMultiplier = 1f;
        private int _carriedForce;
        private MoveEventType? _forcedFollowUpTrigger;
        private int _forcedFollowUpRemaining;

        public bool IsMoveRunning => _hasCurrent;
        public MoveRuntime Current => _current;
        public int QueueCount => _queue.Count;
        public bool IsGuardBroken => stance <= 0;
        public Vector2 Position => body != null ? body.position : (Vector2)transform.position;
        public float NextAttackDamageMultiplier => _nextAttackDamageMultiplier;
        public int CarriedForce => _carriedForce;

        public float ChainMultiplier
        {
            get
            {
                float value = 1f + (_chainCount * chainStepBonus);
                return Mathf.Min(value, chainMaxMultiplier);
            }
        }

        public void Enqueue(Move move)
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

        public void ClearQueue()
        {
            _queue.Clear();
            _carriedForce = 0;
        }

        public bool TryStartNextMove(Func<Actor, Move, int> forceSelector)
        {
            if (_hasCurrent || _queue.Count == 0)
            {
                return false;
            }

            QueuedMove queued = _queue.Dequeue();
            Move move = queued.move;
            if (move == null)
            {
                return false;
            }

            int force = forceSelector != null ? forceSelector(this, move) : 3;
            return StartMove(move, force);
        }

        public bool StartMove(Move move, int force)
        {
            if (move == null)
            {
                return false;
            }

            int inputForce = Mathf.Clamp(force, 1, 5);
            int carryIn = Mathf.Min(_carriedForce, move.forceCarryIn);
            int effectiveForce = inputForce + carryIn;
            _carriedForce = 0;

            int cost = move.GetStanceCost(effectiveForce);
            stance = Mathf.Max(0, stance - cost);
            GainSpecialForce(effectiveForce);

            _current = new MoveRuntime(move, inputForce, effectiveForce);
            _hasCurrent = true;

            ApplyMovePresentation(move);
            InitializeMoveMotion(move);
            MoveStarted?.Invoke(this, _current);
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (!_hasCurrent)
            {
                ApplyRecoil(deltaTime);
                return;
            }

            _current.elapsed += deltaTime;
            ApplyMoveMotion(_current);
            ApplyRecoil(deltaTime);

            if (!_current.IsDone)
            {
                return;
            }

            FinishCurrentMove();
        }

        public void Interrupt(MoveEventType trigger, InterruptReason reason)
        {
            if (!_hasCurrent)
            {
                return;
            }

            MoveRuntime interrupted = _current;
            Move next = interrupted.move != null ? interrupted.move.GetNext(trigger) : null;
            _hasCurrent = false;
            _carriedForce = 0;
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

            if (next != null)
            {
                StartMove(next, interrupted.force);
                return;
            }
        }

        public void ApplyHpDamage(int amount)
        {
            hp = Mathf.Max(0, hp - Mathf.Max(0, amount));
        }

        public void ApplyStanceDamage(int amount)
        {
            stance = Mathf.Max(0, stance - Mathf.Max(0, amount));
        }

        public void RecoverStance(int amount)
        {
            stance = Mathf.Clamp(stance + Mathf.Max(0, amount), 0, maxStance);
        }

        public void GainSpecialForce(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            specialForce = Mathf.Clamp(specialForce + amount, 0, maxSpecialForce);
        }

        public bool CanSpendSpecialForce(int amount)
        {
            return amount >= 0 && specialForce >= amount;
        }

        public bool SpendSpecialForce(int amount)
        {
            if (!CanSpendSpecialForce(amount))
            {
                return false;
            }

            specialForce -= amount;
            return true;
        }

        public void SetNextAttackDamageMultiplier(float multiplier)
        {
            _nextAttackDamageMultiplier = Mathf.Max(1f, multiplier);
        }

        public float ConsumeNextAttackDamageMultiplier()
        {
            float value = _nextAttackDamageMultiplier;
            _nextAttackDamageMultiplier = 1f;
            return value;
        }

        public void ResetAndApplyKnockback(Vector2 direction, float initialSpeed, float friction)
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

        private void FinishCurrentMove()
        {
            MoveRuntime finished = _current;
            _hasCurrent = false;

            if (finished.move != null)
            {
                RecoverStance(finished.move.stanceRecoveryOnFinish);
                _carriedForce = finished.move.forceCarryOut;
            }
            else
            {
                _carriedForce = 0;
            }

            MoveFinished?.Invoke(this, finished);

            if (_forcedFollowUpRemaining > 0)
            {
                if (finished.move != null && finished.move.skipAdditionalInterruptFollowUp)
                {
                    _forcedFollowUpTrigger = null;
                    _forcedFollowUpRemaining = 0;
                }
                else if (_forcedFollowUpTrigger.HasValue && finished.move != null)
                {
                    Move forced = finished.move.GetNext(_forcedFollowUpTrigger.Value);
                    _forcedFollowUpRemaining--;

                    if (_forcedFollowUpRemaining <= 0)
                    {
                        _forcedFollowUpTrigger = null;
                    }

                    if (forced != null)
                    {
                        StartMove(forced, finished.selectedForce);
                        return;
                    }
                }
            }

            if (_queue.Count > 0)
            {
                _chainCount++;
            }
            else
            {
                _chainCount = 0;
            }

            Move autoNext = finished.move != null ? finished.move.GetNext(MoveEventType.NormalEnd) : null;
            if (autoNext != null)
            {
                StartMove(autoNext, finished.selectedForce);
            }
        }

        private void ApplyMovePresentation(Move move)
        {
            if (spriteRenderer != null && move != null && move.frameSprite != null)
            {
                spriteRenderer.sprite = move.frameSprite;
            }
        }

        private void InitializeMoveMotion(Move move)
        {
            if (move == null)
            {
                return;
            }

            Vector2 current = GetActorPosition();

            if (move.useTeleport)
            {
                current += move.teleportOffset;
                SetActorPosition(current);
            }

            _moveStartPosition = current;
            _moveEndPosition = move.useStepMovement ? current + move.stepOffset : current;

            if (!move.useStepMovement)
            {
                return;
            }

            if (move.duration <= 0f)
            {
                SetActorPosition(_moveEndPosition);
            }
        }

        private void ApplyMoveMotion(MoveRuntime runtime)
        {
            if (runtime.move == null || !runtime.move.useStepMovement)
            {
                return;
            }

            float t = runtime.Normalized;
            Vector2 target = Vector2.Lerp(_moveStartPosition, _moveEndPosition, t);
            SetActorPosition(target);
        }

        private Vector2 GetActorPosition()
        {
            return body != null ? body.position : (Vector2)transform.position;
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

            // 반동 중에도 기존 Move 진행 경로가 같이 밀려나도록 보정.
            _moveStartPosition += delta;
            _moveEndPosition += delta;

            float speed = _recoilVelocity.magnitude;
            speed = Mathf.MoveTowards(speed, 0f, _recoilFriction * deltaTime);
            _recoilVelocity = speed > 0f ? _recoilVelocity.normalized * speed : Vector2.zero;
        }
    }
}
