using System;
using System.Collections.Generic;
using UnityEngine;

namespace Scripts
{
    public class ActorActionController : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxQueuedMoves = 5;
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
        private float _moveGroundY;
        private float _lastPathSample;
        private Vector2 _lastCurveSample;
        private int _moveStartFacingSign = 1;
        private bool _startFacingConsumed;
        private int selectedForce = 0;
        private Move _currentSourceMove;
        internal Move NetworkSourceMove { get; private set; }
        internal int NetworkActionSerial { get; private set; }
        internal int CurrentForce => _hasCurrent ? _current.force : _carriedForce;
        internal bool ChaseStopped { get; set; }
        internal float ConsumePathSample(float sample)
        {
            float delta = sample - _lastPathSample;
            _lastPathSample = sample;
            return delta;
        }

        internal void ApplyReplica(Move source, int serial, bool running, float progress)
        {
            EnsureOwner();
            if (source != NetworkSourceMove || serial != NetworkActionSerial)
            {
                if (_owner.CurrentMoveVisual != null) _owner.ReleaseMoveInstanceFromAction(_owner.CurrentMoveVisual);
                var instance = source != null ? _owner.CreateMoveInstanceFromAction(source) : null;
                if (instance != null) instance.BindGraphFromSource(source);
                _current = new MoveRuntime(instance, 0);
                NetworkSourceMove = source; NetworkActionSerial = serial;
            }
            _hasCurrent = running;
            float elapsed = Mathf.Clamp01(progress) * ((source != null ? source.Duration : 0f) + _owner.MoveStartDelay);
            _moveStartupRemaining = Mathf.Max(0, _owner.MoveStartDelay - elapsed);
            _current.elapsed = Mathf.Max(0, elapsed - _owner.MoveStartDelay);
            _owner.RefreshMoveVisualStateFromAction(running, progress);
        }

        internal bool IsMoveRunning => _hasCurrent;
        internal bool IsReadyForExchange => _hasCurrent && _moveStartupRemaining <= 0f;
        internal bool HasResolvedExchange => _currentMoveExchanged;
        internal MoveRuntime Current => _current;
        internal int QueueCount => _queue.Count;
        internal int MaxQueuedMoves => Mathf.Max(1, maxQueuedMoves);
        internal int NextQueueRecoveryBonus => _queue.Count * 5;
        internal int ChainCount => _chainCount;
        internal float StartupRemaining => _moveStartupRemaining;
        internal Vector2 MoveStartPosition => _moveStartPosition;
        internal int MoveStartFacingSign => _moveStartFacingSign;
        internal Move nextMove => _queue.Count > 0 ? _queue.Peek().move : null;
        internal Move PlanningSourceMove
        {
            get
            {
                Move tail = null;
                foreach (var queued in _queue) tail = queued.move;
                if (tail != null) return tail;
                if (_currentSourceMove != null) return _currentSourceMove;
                return _current.move != null ? _current.move : null;
            }
        }

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

        internal bool Enqueue(Move move)
        {
            if (move == null || _queue.Count >= MaxQueuedMoves)
            {
                return false;
            }

            _queue.Enqueue(new QueuedMove { move = move, queueRecoveryBonus = NextQueueRecoveryBonus });
            return true;
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

            int inputForce = forceSelector != null ? forceSelector(_owner, queued.move) : (queued.move.UsesForce ? 1 : 0);
            return StartMove(queued, inputForce, combatContext);
        }

        internal bool TryChargeCurrent()
        {
            EnsureOwner();
            if (_owner == null || !_hasCurrent || _currentSourceMove == null || !_currentSourceMove.CanCharge ||
                _queue.Count > 0 || _owner.HasPlannedMove ||
                _current.force >= 4 || !_owner.TrySpendStance(_currentSourceMove.ChargeStanceCost)) return false;
            _current.force++;
            _currentQueuedMove?.SetForceCarryOut(_current.force);
            return true;
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
                Vector2 recoil = _owner.ApplyRecoilFromActionController(deltaTime, !UsesCurveMovementAt(true));
                _moveStartPosition += recoil;
                ApplyCurveMovement(true, recoil, deltaTime);
                _owner.RefreshMoveVisualStateFromAction(_hasCurrent, MoveProgress);
                return;
            }

            _current.elapsed += deltaTime;
            Vector2 activeRecoil = _owner.ApplyRecoilFromActionController(deltaTime, !UsesCurveMovementAt(false));
            _moveStartPosition += activeRecoil;
            ApplyCurveMovement(false, activeRecoil, deltaTime);

            if (_current.IsDone)
            {
                // Sample the exact final frame before the action becomes idle/waiting.
                _owner.RefreshMoveVisualStateFromAction(true, 1f);
                FinishCurrentMove();
            }

            _owner.RefreshMoveVisualStateFromAction(_hasCurrent, MoveProgress);
        }

        private bool UsesCurveMovementAt(bool startup)
        {
            var move = _current.move;
            return move != null && move.MovementMode == MovementMode.CurveXY &&
                   (move.MovementPhase == MovementPhase.StartupAndActive ||
                    (startup ? move.MovementPhase == MovementPhase.StartupOnly : move.MovementPhase == MovementPhase.ActiveOnly));
        }

        private void ApplyCurveMovement(bool startup, Vector2 recoil, float deltaTime)
        {
            var move = _current.move;
            if (move == null || move.MovementMode != MovementMode.CurveXY) return;
            float progress;
            switch (move.MovementPhase)
            {
                case MovementPhase.StartupOnly:
                    if (!startup) return;
                    progress = StartupProgress;
                    break;
                case MovementPhase.ActiveOnly:
                    if (startup) return;
                    progress = ActiveProgress;
                    break;
                case MovementPhase.StartupAndActive:
                    progress = MoveProgress;
                    break;
                default: return;
            }
            // Authored X curves use the mannequin's local horizontal axis, which is
            // opposite the combat world's facing axis. Convert only at runtime;
            // keep the user's curve keys and editor preview coordinates intact.
            Vector2 sample = move.EvaluateMovementOffset(progress, -_moveStartFacingSign);
            Vector2 delta = sample - _lastCurveSample;
            _lastCurveSample = sample;
            delta.x = _owner.AdjustHorizontalMovement(delta.x, deltaTime);
            _owner.MoveTo(_owner.Position + recoil + delta);
        }

        internal void Interrupt(MoveEventType trigger, InterruptReason reason, CombatContext combatContext)
        {
            InterruptWithFollowUps(trigger, reason, combatContext, 1);
        }

        internal void InterruptWithFollowUps(MoveEventType trigger, InterruptReason reason,
            CombatContext combatContext, int followUpCount)
        {
            EnsureOwner();

            if (_owner == null || !_hasCurrent)
            {
                return;
            }

            MoveRuntime interrupted = _current;
            Move interruptedSourceMove = _currentSourceMove;
            Move next = null;

            _hasCurrent = false;
            _currentQueuedMove = null;
            _currentSourceMove = null;
            _carriedForce = 0;
            _owner.CancelPlanning();
            _currentMoveExchanged = false;
            _moveStartupRemaining = 0f;
            RestoreGroundHeightAfterCurve();

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
            if (StartMove(queued, 0, combatContext) && followUpCount > 0 &&
                (followUpCount > 1 || interruptedSourceMove == null || !interruptedSourceMove.SkipAdditionalInterruptFollowUp))
                EnqueueInterruptFollowUps(next, followUpCount);
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

            // The legacy force selector cannot bypass charge. Attacks always start at one.
            selectedForce = queued.move.UsesForce ? 1 : 0;
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
            NetworkSourceMove = sourceMove;
            NetworkActionSerial++;
            runtimeMove.BindGraphFromSource(sourceMove);
            //_owner.ApplyMoveStartStanceCostFromAction(runtimeMove);
            _owner.BeginPreviousVisualFromAction(runtimeMove.DelayVisualReveal && runtimeMove.ShowPreviousVisual);
            int carriedForce = sourceMove.AcceptsCarriedForce ? _carriedForce : 0;
            _carriedForce = 0;

            queued.forceCarryIn = carriedForce;
            _currentQueuedMove = queued;
            int chaseForce = sourceMove.UsesForce && sourceMove.MovementMode == MovementMode.StopAtRange
                ? Mathf.Min(carriedForce, sourceMove.ChaseForceAllocation) : 0;
            _current = new MoveRuntime(runtimeMove, selectedForce + carriedForce - chaseForce);
            _current.chaseForce = chaseForce;
            _hasCurrent = true;
            _currentMoveExchanged = false;
            ChaseStopped = false;
            _moveStartupRemaining = _owner.MoveStartDelay;

            _moveStartPosition = _owner.Position;
            _moveGroundY = _owner.Position.y;
            _lastPathSample = 0f;
            _lastCurveSample = Vector2.zero;
            _moveStartFacingSign = _owner.FacingSign;
            _startFacingConsumed = false;

            queued.move = runtimeMove;
            queued.Play(selectedForce, combatContext, _owner.Kind);
            queued.move = sourceMove;

            _owner.RefreshMoveVisualStateFromAction(_hasCurrent, MoveProgress);
            return true;
        }

        private void FinishCurrentMove()
        {
            MoveRuntime finished = _current;
            RestoreGroundHeightAfterCurve();
            if (finished.move != null && finished.move.UsesForce)
            {
                _owner.GainSpecialForce(finished.force);
            }
            QueuedMove finishedQueuedMove = _currentQueuedMove;
            Move finishedSourceMove = _currentSourceMove;

            _hasCurrent = false;
            _currentQueuedMove = null;
            _currentSourceMove = finishedSourceMove;
            _currentMoveExchanged = false;
            _moveStartupRemaining = 0f;

            if (finished.move != null)
            {
                int recovery = _owner.BaseStanceRecoveryPerMove + finished.move.StanceRecovery +
                               (finishedQueuedMove != null ? finishedQueuedMove.queueRecoveryBonus : 0);
                if (finished.move.Category == MoveCategory.HitReaction)
                    recovery = Mathf.FloorToInt(recovery * .5f);
                _owner.RecoverStance(recovery);
            }

            _carriedForce = _queue.Count > 0 && finishedQueuedMove != null ? finishedQueuedMove.forceCarryOut : 0;

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

        private void RestoreGroundHeightAfterCurve()
        {
            if (_owner == null || _current.move == null || _current.move.MovementMode != MovementMode.CurveXY)
                return;

            // Authored Y curves may end above or below zero. Keep their motion
            // during the action, but do not carry that offset into later moves.
            Vector2 position = _owner.Position;
            if (!Mathf.Approximately(position.y, _moveGroundY))
                _owner.MoveTo(new Vector2(position.x, _moveGroundY));
        }
    }
}
