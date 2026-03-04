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
        public int force;
        public float elapsed;

        public MoveRuntime(Move move, int force)
        {
            this.move = move;
            this.force = Mathf.Clamp(force, 1, 5);
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

        [Header("Chain")]
        [Min(0f)] public float chainStepBonus = 0.05f;
        [Min(1f)] public float chainMaxMultiplier = 1.5f;

        [Header("References")]
        public Animator animator;
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

        public bool IsMoveRunning => _hasCurrent;
        public MoveRuntime Current => _current;
        public int QueueCount => _queue.Count;
        public bool IsGuardBroken => stance <= 0;

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

            _queue.Enqueue(new QueuedMove { move = move });
        }

        public void ClearQueue()
        {
            _queue.Clear();
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

            int clampedForce = Mathf.Clamp(force, 1, 5);
            int cost = move.GetStanceCost(clampedForce);
            stance = Mathf.Max(0, stance - cost);

            _current = new MoveRuntime(move, clampedForce);
            _hasCurrent = true;

            PlayAnimation(move);
            MoveStarted?.Invoke(this, _current);
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (!_hasCurrent)
            {
                return;
            }

            _current.elapsed += deltaTime;
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
            Move next = interrupted.move.GetNext(trigger);
            _hasCurrent = false;
            MoveInterrupted?.Invoke(this, interrupted, reason);

            _chainCount = 0;

            if (next != null)
            {
                StartMove(next, interrupted.force);
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

        private void FinishCurrentMove()
        {
            MoveRuntime finished = _current;
            _hasCurrent = false;

            if (finished.move != null)
            {
                RecoverStance(finished.move.stanceRecoveryOnFinish);
            }

            MoveFinished?.Invoke(this, finished);

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
                StartMove(autoNext, finished.force);
            }
        }

        private void PlayAnimation(Move move)
        {
            if (animator == null || move == null || move.clip == null)
            {
                return;
            }

            animator.Play(move.clip.name, 0, 0f);
        }
    }
}
