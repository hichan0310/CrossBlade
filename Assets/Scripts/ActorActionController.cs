using System;
using System.Collections.Generic;
using UnityEngine;

namespace Scripts
{
    public class ActorActionController : MonoBehaviour
    {
        private Actor _owner;

        private readonly Queue<QueuedMove> _queue = new Queue<QueuedMove>();
        private MoveRuntime _current;
        private QueuedMove _currentQueuedMove;
        private bool _hasCurrent;
        private int _chainCount;
        private int _carriedForce;
        private bool _currentMoveExchanged;
        private float _moveStartupRemaining;
        private Vector2 _moveStartPosition;
        private int _moveStartFacingSign = 1;
        private bool _startFacingConsumed;
        private int selectedForce = 0;
        private Move _currentSourceMove;

        internal bool IsMoveRunning => _hasCurrent;
        internal bool IsReadyForExchange => _hasCurrent && _moveStartupRemaining <= 0f;
        internal bool HasResolvedExchange => _currentMoveExchanged;
        internal MoveRuntime Current => _current;
        internal int QueueCount => _queue.Count;
        internal int ChainCount => _chainCount;
        internal float StartupRemaining => _moveStartupRemaining;
        internal Vector2 MoveStartPosition => _moveStartPosition;
        internal int MoveStartFacingSign => _moveStartFacingSign;
        internal Move nextMove => _queue.Count > 0 ? _queue.Peek().move : null;

        internal float StartupProgress
        {
            get
            {
                if (_owner == null || _owner.MoveStartDelay <= 0f)
                {
                    return 1f;
                }

                return Mathf.Clamp01((_owner.MoveStartDelay - _moveStartupRemaining) / _owner.MoveStartDelay);
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

                float startupElapsed = Mathf.Max(0f, (_owner != null ? _owner.MoveStartDelay : 0f) - _moveStartupRemaining);
                float activeElapsed = _current.elapsed;
                float moveDuration = _current.move != null ? _current.move.Duration : 0f;
                float totalDuration = (_owner != null ? _owner.MoveStartDelay : 0f) + moveDuration;

                if (totalDuration <= 0f)
                {
                    return 1f;
                }

                return Mathf.Clamp01((startupElapsed + activeElapsed) / totalDuration);
            }
        }
        

        internal void Initialize(Actor owner)
        {
            _owner = owner;
        }

        private void EnsureOwner()
        {
            if (_owner == null)
            {
                _owner = GetComponent<Actor>();
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
            EnsureOwner();

            if (_owner == null)
            {
                return;
            }

            _moveStartFacingSign = _owner.FacingSign;
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
            EnsureOwner();

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

            int inputForce = forceSelector != null ? forceSelector(_owner, queued.move) : 3;
            return StartMove(queued, inputForce, combatContext);
        }

        internal void Tick(float deltaTime)
        {
            EnsureOwner();

            if (_owner == null)
            {
                return;
            }

            if (!_hasCurrent)
            {
                _owner.ApplyRecoilFromActionController(deltaTime);
                _owner.RefreshMoveVisualStateFromAction(false, 1f);
                return;
            }

            if (_moveStartupRemaining > 0f)
            {
                _moveStartupRemaining = Mathf.Max(0f, _moveStartupRemaining - deltaTime);
                _owner.ApplyRecoilFromActionController(deltaTime);
                _owner.RefreshMoveVisualStateFromAction(_hasCurrent, MoveProgress);
                return;
            }

            _current.elapsed += deltaTime;
            _owner.ApplyRecoilFromActionController(deltaTime);

            if (_current.IsDone)
            {
                FinishCurrentMove();
            }

            _owner.RefreshMoveVisualStateFromAction(_hasCurrent, MoveProgress);
        }

        internal void Interrupt(MoveEventType trigger, InterruptReason reason, CombatContext combatContext)
        {
            EnsureOwner();

            if (_owner == null || !_hasCurrent)
            {
                return;
            }

            MoveRuntime interrupted = _current;
            QueuedMove interruptedQueuedMove = _currentQueuedMove;
            Move interruptedSourceMove = _currentSourceMove;
            Move next = null;

            _hasCurrent = false;
            _currentQueuedMove = null;
            _currentSourceMove = null;
            _carriedForce = 0;
            _currentMoveExchanged = false;
            _moveStartupRemaining = 0f;

            if (_owner.HasVisualController)
            {
                _owner.CapturePreviousVisualSnapshotFromAction();
                _owner.ReleaseMoveInstanceFromAction(_owner.CurrentMoveVisual);
            }

            _chainCount = 0;

            switch (trigger)
            {
                case MoveEventType.Hit:
                    next = interruptedSourceMove != null ? interruptedSourceMove.OnHit(_owner, combatContext) : null;
                    break;

                case MoveEventType.Guard:
                    next = interruptedSourceMove != null ? interruptedSourceMove.OnGuard(_owner, combatContext) : null;
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

            StartMove(queued, 0, combatContext);
        }

        internal void MarkCurrentMoveExchanged()
        {
            _currentMoveExchanged = true;
        }

        private bool StartMove(QueuedMove queued, int inputForce, CombatContext combatContext)
        {
            EnsureOwner();

            if (_owner == null || queued == null || queued.move == null)
            {
                return false;
            }

            if (!_hasCurrent && _owner.CurrentMoveVisual != null && _owner.HasVisualController)
            {
                _owner.CapturePreviousVisualSnapshotFromAction();
                _owner.ReleaseMoveInstanceFromAction(_owner.CurrentMoveVisual);
            }

            selectedForce = Mathf.Clamp(inputForce, 1, 5);
            Move sourceMove = queued.move;
            if (sourceMove != null && (sourceMove.name.Contains("(Clone)") || sourceMove.name.Contains("__DYING")))
            {
                Debug.LogWarning($"[BAD MOVE SOURCE] {sourceMove.name}", sourceMove);
            }
            Move runtimeMove = _owner.CreateMoveInstanceFromAction(sourceMove);
            if (runtimeMove == null)
            {
                return false;
            }
            _currentSourceMove = sourceMove;
            runtimeMove.BindGraphFromSource(sourceMove);
            _owner.BeginPreviousVisualFromAction(runtimeMove.DelayVisualReveal && runtimeMove.ShowPreviousVisual);
            int carriedForce = _carriedForce;
            _carriedForce = 0;

            queued.forceCarryIn = carriedForce;
            _currentQueuedMove = queued;
            _current = new MoveRuntime(runtimeMove, selectedForce + carriedForce);
            _hasCurrent = true;
            _currentMoveExchanged = false;
            _moveStartupRemaining = _owner.MoveStartDelay;

            _moveStartPosition = _owner.Position;
            _moveStartFacingSign = _owner.FacingSign;
            _startFacingConsumed = false;

            queued.move = runtimeMove;
            queued.Play(selectedForce, combatContext, _owner.Kind);
            queued.move = sourceMove;

            _owner.ApplyMoveStartStanceCostFromAction(runtimeMove);
            _owner.RefreshMoveVisualStateFromAction(_hasCurrent, MoveProgress);
            return true;
        }

        private void FinishCurrentMove()
        {
            MoveRuntime finished = _current;
            if (finished.move != null && finished.move.UsesForce)
            {
                _owner.GainSpecialForce(selectedForce);
            }
            QueuedMove finishedQueuedMove = _currentQueuedMove;
            Move finishedSourceMove = _currentSourceMove;

            _hasCurrent = false;
            _currentQueuedMove = null;
            _currentSourceMove = null;
            _currentMoveExchanged = false;
            _moveStartupRemaining = 0f;

            if (finished.move != null)
            {
                _owner.RecoverStance(finished.move.StanceRecovery);
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
        }
    }
}
