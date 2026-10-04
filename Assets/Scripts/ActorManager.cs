using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using Scripts.CharAIs;

namespace Scripts
{
    public enum ExchangeResult
    {
        None,
        Clash,
        ABlocksB,
        BBlocksA,
        AHitsB,
        BHitsA
    }

    public class ActorManager : MonoBehaviour
    {
        private int lastAiChargeSerial = -1;
        private int _observedActionA;
        private int _observedActionB;
        public int TurnIndex { get; private set; }
        public ExchangeResult LastExchange { get; private set; }
        private struct ExchangeInfo
        {
            public ExchangeResult result;
            public Hitbox hitboxA;
            public Hitbox hitboxB;
        }

        private struct KnockbackDistances
        {
            public float actorA;
            public float actorB;
        }

        [Header("Actors")] public Actor actorA;
        public Actor actorB;
        public CombatContext combatContext;

        [Header("Simulation")] public bool autoSimulate = true;

        [Header("Knockback")]
        [SerializeField, Min(0.05f)] private float knockbackDuration = .33f;
        [SerializeField] private float clashDecrease;
        private Camera combatCamera;
        private float cameraHeightOffset;
        private float cameraBaseOrthographicSize;

        [Header("Turn Stop (Debug)")] [SerializeField, Min(0)]
        private int stopTurnsA;

        [SerializeField, Min(0)] private int stopTurnsB;

        // 프레임마다 stop 턴이 줄어드는 것을 막기 위한 게이트.
        private bool _consumedStopAInCurrentWindow;
        private bool _consumedStopBInCurrentWindow;

        private void Start()
        {
            this.combatContext = new CombatContext()
            {
                user = actorA, target = actorB
            };
            combatCamera = Camera.main;
            if (combatCamera != null && actorA != null && actorB != null)
            {
                cameraHeightOffset = combatCamera.transform.position.y - (actorA.Position.y + actorB.Position.y) * .5f;
                cameraBaseOrthographicSize = combatCamera.orthographicSize;
            }
        }

        private void LateUpdate()
        {
            if (actorA == null || actorB == null) return;
            if (combatCamera == null)
            {
                combatCamera = Camera.main;
                if (combatCamera == null) return;
                cameraHeightOffset = combatCamera.transform.position.y - (actorA.Position.y + actorB.Position.y) * .5f;
                cameraBaseOrthographicSize = combatCamera.orthographicSize;
            }

            Vector2 midpoint = (actorA.Position + actorB.Position) * .5f;
            Vector3 position = combatCamera.transform.position;
            combatCamera.transform.position = new Vector3(midpoint.x, midpoint.y + cameraHeightOffset, position.z);
            if (combatCamera.orthographic && combatCamera.aspect > 0f)
            {
                float halfWidth = Mathf.Abs(actorA.Position.x - actorB.Position.x) * .5f + 1f;
                combatCamera.orthographicSize = Mathf.Max(cameraBaseOrthographicSize, halfWidth / combatCamera.aspect);
            }
        }

        private void Update()
        {
            if (!autoSimulate || actorA == null || actorB == null)
            {
                return;
            }

            if (!actorA.OnlineControlled)
                actorA.SetDirectionalInput(actorA.IsMoveRunning ? ReadArrowDirection() : 0);
            if (!actorA.OnlineControlled && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                actorA.ActionController.TryChargeCurrent();
            if (!actorB.OnlineControlled && actorB.PlanMaker is SampleAI && actorB.IsMoveRunning &&
                actorB.MoveProgress >= .5f && actorB.ActionController.NetworkActionSerial != lastAiChargeSerial)
            {
                lastAiChargeSerial = actorB.ActionController.NetworkActionSerial;
                actorB.ActionController.TryChargeCurrent();
            }
            Simulate(Time.deltaTime);
        }

        internal static int ReadArrowDirection()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return 0;
            return (keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.leftArrowKey.isPressed ? 1 : 0);
        }

        public void Simulate(float deltaTime)
        {
            this.actorA.ForceUpdate(deltaTime);
            this.actorB.ForceUpdate(deltaTime);
            PollNextMove(actorA);
            PollNextMove(actorB);
            TryStartActors();
            ObserveActionStarts();
            UpdateFacing();
            ApplyMovement(deltaTime);

            if (actorA.IsMoveRunning && actorB.IsMoveRunning
                                     && actorA.IsReadyForExchange && actorB.IsReadyForExchange)
            {
                ExchangeInfo exchange = ResolveExchange(actorA, actorB);
                ApplyExchange(exchange);
                if (exchange.result != ExchangeResult.None) LastExchange = exchange.result;
                ObserveActionStarts();
            }

            actorA.Tick(deltaTime);
            actorB.Tick(deltaTime);
        }

        private void ObserveActionStarts()
        {
            int serialA = actorA.ActionController.NetworkActionSerial;
            int serialB = actorB.ActionController.NetworkActionSerial;
            if (serialA == _observedActionA && serialB == _observedActionB) return;
            _observedActionA = serialA;
            _observedActionB = serialB;
            TurnIndex++;
        }

        private static void PollNextMove(Actor actor)
        {
            if (actor == null || !actor.IsMoveRunning || actor.PlanMaker == null || !actor.HasQueueSpace ||
                actor.QueueCount > 0 && !(actor.PlanMaker is OnlineDuelPlanner)) return;
            if (actor.PlanMaker is SampleAI && actor.PlanningMove != null && actor.PlanningMove.CanCharge &&
                actor.MoveProgress < .5f) return;
            if (actor.PlanMaker.GetPlan(actor) == PlanQueryState.Ready &&
                actor.TryConsumePlannedMove(out Move planned)) actor.Enqueue(planned);
        }

        // 방향 전환
        private void UpdateFacing()
        {
            if (actorA == null || actorB == null)
            {
                return;
            }

            UpdateFacing(actorA, actorB);
            UpdateFacing(actorB, actorA);
        }

        private static void UpdateFacing(Actor actor, Actor target)
        {
            if (actor == null || target == null)
            {
                return;
            }

            Move currentMove = actor.IsMoveRunning ? actor.Current.move : null;
            FacingMode mode = currentMove != null ? currentMove.FacingMode : FacingMode.UseActorDefault;

            switch (mode)
            {
                case FacingMode.AutoFaceTarget:
                    actor.FaceTowards(target.Position);
                    if (actor.TryConsumeStartFacing())
                    {
                        actor.SyncMoveStartFacing();
                    }

                    return;

                case FacingMode.LockCurrentFacing:
                    return;

                case FacingMode.FaceTargetOnStartOnly:
                    if (actor.TryConsumeStartFacing())
                    {
                        actor.FaceTowards(target.Position);
                        actor.SyncMoveStartFacing();
                    }

                    return;

                case FacingMode.UseActorDefault:
                default:
                    if (actor.IsMoveRunning && actor.IsReadyForExchange)
                    {
                        return;
                    }

                    actor.FaceTowards(target.Position);
                    return;
            }
        }

        private static bool ShouldMoveNow(Actor actor, Move move)
        {
            bool isStartup = actor.IsMoveRunning && !actor.IsReadyForExchange;
            bool isActive = actor.IsMoveRunning && actor.IsReadyForExchange;

            switch (move.MovementPhase)
            {
                case MovementPhase.None:
                    return false;

                case MovementPhase.StartupOnly:
                    return isStartup;

                case MovementPhase.ActiveOnly:
                    return isActive;

                case MovementPhase.StartupAndActive:
                    return isStartup || isActive;

                default:
                    return false;
            }
        }

        private static float GetMovementProgress(Actor actor, Move move)
        {
            switch (move.MovementPhase)
            {
                case MovementPhase.StartupOnly:
                    return actor.StartupProgress;

                case MovementPhase.ActiveOnly:
                    return actor.ActiveProgress;

                case MovementPhase.StartupAndActive:
                    return actor.MoveProgress;

                case MovementPhase.None:
                default:
                    return 0f;
            }
        }

        private void ApplyMovement(float deltaTime)
        {
            if (actorA == null || actorB == null || deltaTime <= 0f)
            {
                return;
            }

            ApplyMovement(actorA, actorB, deltaTime);
            ApplyMovement(actorB, actorA, deltaTime);
        }

        private static void ApplyMovement(Actor actor, Actor target, float deltaTime)
        {
            if (actor == null || target == null)
            {
                return;
            }

            // Current retains the last Move while its final pose is held. Input must
            // only move an actor while that Move is actually playing.
            if (!actor.IsMoveRunning) return;

            Move move = actor.Current.move;
            if (move == null)
            {
                return;
            }

            if (move.AllowManualMovement && move.MovementMode == MovementMode.None)
            {
                MoveManually(actor, target, move.ManualMovementSpeed, deltaTime);
                return;
            }

            if (!ShouldMoveNow(actor, move))
            {
                return;
            }

            switch (move.MovementMode)
            {
                case MovementMode.CurveXY:
                    // ActorActionController samples curves with the visual clock,
                    // including the final sample before a Move finishes.
                    return;
                case MovementMode.None:
                    return;

                case MovementMode.StopAtRange:
                    MoveTowardRange(actor, target, move.StopDistance,
                        Mathf.Max(0f, move.Speed) + actor.Current.chaseForce * move.ChaseSpeedPerForce, deltaTime);
                    return;

                case MovementMode.PassThroughTarget:
                {
                    float targetX = target.Position.x + (actor.MoveStartFacingSign * move.PassThroughOffset);
                    MoveToward(actor, targetX, GetMovementProgress(actor, move), deltaTime);
                    return;
                }

                case MovementMode.FixedSpeedForward:
                {
                    if (Mathf.Abs(move.Speed) <= 0.001f)
                    {
                        return;
                    }

                    float direction = actor.MoveStartFacingSign * Mathf.Sign(move.Speed);
                    float moveAmount = Mathf.Abs(move.Speed) * deltaTime;

                    actor.MoveBy(new Vector2(actor.AdjustHorizontalMovement(direction * moveAmount, deltaTime), 0f));
                    return;
                }

                case MovementMode.FixedDistanceForward:
                {
                    float targetX = actor.MoveStartPosition.x + (actor.MoveStartFacingSign * move.FixedTravelDistance);
                    MoveToward(actor, targetX, GetMovementProgress(actor, move), deltaTime);
                    return;
                }
            }
        }

        private static void MoveManually(Actor actor, Actor target, float speed, float deltaTime)
        {
            int direction = actor.DirectionalInput;
            if (direction == 0 || speed <= 0f || actor._recoilVelocity.sqrMagnitude > 0f) return;
            // This is direct idle movement, not an existing motion to accelerate.
            float step = direction * speed * deltaTime;
            float distance = Mathf.Abs(step);
            Physics2D.SyncTransforms();
            foreach (var own in ChaseColliders(actor))
            foreach (var other in ChaseColliders(target))
                distance = Mathf.Min(distance, ChaseContactDistance(own.bounds, other.bounds, direction));
            if (distance > 0f) actor.MoveBy(new Vector2(direction * distance, 0f));
        }

        private static void MoveTowardRange(Actor actor, Actor target, float stopDistance, float speed, float deltaTime)
        {
            var controller = actor.ActionController;
            if (controller != null && controller.ChaseStopped) return;
            float deltaX = target.Position.x - actor.Position.x;
            float distanceX = Mathf.Abs(deltaX);
            float remaining = distanceX - stopDistance;

            float signedStep = Mathf.Sign(deltaX) * Mathf.Max(0, speed) * deltaTime;
            float moveAmount = Mathf.Min(Mathf.Abs(actor.AdjustHorizontalMovement(signedStep, deltaTime)), Mathf.Max(0, remaining));
            // Use the same world-space bounds as combat contact, swept over this step.
            // Disabled/consumed weapons and inactive weapon frames do not block pursuit.
            Physics2D.SyncTransforms();
            foreach (var own in ChaseColliders(actor))
            foreach (var other in ChaseColliders(target))
            {
                float contact = ChaseContactDistance(own.bounds, other.bounds, Mathf.Sign(deltaX));
                if (contact > moveAmount) continue;
                moveAmount = contact;
                if (controller != null) controller.ChaseStopped = true;
            }
            if (moveAmount <= 0f) return;
            actor.MoveBy(new Vector2(Mathf.Sign(deltaX) * moveAmount, 0f));
        }

        private static System.Collections.Generic.IEnumerable<Collider2D> ChaseColliders(Actor actor)
        {
            var move = actor.Current.move;
            if (move == null) yield break;
            var body = move.BodyCollider;
            if (body != null && body.enabled && body.gameObject.activeInHierarchy) yield return body;
            foreach (var weapon in move.WeaponHitboxes)
            {
                if (weapon == null || !weapon.IsActiveAt(actor.MoveProgress)) continue;
                var collider = weapon.Collider;
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy) yield return collider;
            }
        }

        private static float ChaseContactDistance(Bounds own, Bounds other, float direction)
        {
            if (own.max.y < other.min.y || own.min.y > other.max.y) return float.PositiveInfinity;
            if (own.max.x >= other.min.x && own.min.x <= other.max.x)
                return direction * (other.center.x - own.center.x) < 0f ? float.PositiveInfinity : 0f;
            float distance = direction >= 0 ? other.min.x - own.max.x : own.min.x - other.max.x;
            return distance >= 0 ? distance : float.PositiveInfinity;
        }

        private static void MoveToward(Actor actor, float targetX, float progress, float deltaTime)
        {
            float startX = actor.MoveStartPosition.x;
            float x = Mathf.Lerp(startX, targetX, Mathf.Clamp01(progress));
            float delta = actor.ActionController.ConsumePathSample(x - startX);
            actor.MoveBy(new Vector2(actor.AdjustHorizontalMovement(delta, deltaTime), 0f));
        }

        public void TryStartActors()
        {
            if (actorA.IsMoveRunning || actorB.IsMoveRunning)
            {
                return;
            }

            if (actorA._recoilVelocity.sqrMagnitude > 0f || actorB._recoilVelocity.sqrMagnitude > 0f)
            {
                return;
            }

            bool startedA = false;
            bool startedB = false;

            bool aCanStartNow = PrepareActor(actorA);
            bool bCanStartNow = PrepareActor(actorB);

            if (aCanStartNow && bCanStartNow)
            {
                if (ShouldBlockStartA())
                {
                    ConsumeStopTurnA();
                }
                else
                {
                    startedA = actorA.TryStartNextMove(SelectForce, this.combatContext);
                }

                if (ShouldBlockStartB())
                {
                    ConsumeStopTurnB();
                }
                else
                {
                    startedB = actorB.TryStartNextMove(SelectForce, this.combatContext);
                }
            }

            if (startedA)
            {
                _consumedStopBInCurrentWindow = false;
            }

            if (startedB)
            {
                _consumedStopAInCurrentWindow = false;
            }
        }

        public void StopActorAForTurns(int turns)
        {
            if (turns <= 0)
            {
                return;
            }

            stopTurnsA += turns;
            _consumedStopAInCurrentWindow = false;
        }

        public void StopActorBForTurns(int turns)
        {
            if (turns <= 0)
            {
                return;
            }

            stopTurnsB += turns;
            _consumedStopBInCurrentWindow = false;
        }

        public int GetRemainingStopTurnsA()
        {
            return stopTurnsA;
        }

        public int GetRemainingStopTurnsB()
        {
            return stopTurnsB;
        }

        public bool CanUseSpecialSkill(Actor actor)
        {
            if (actor == null)
            {
                return false;
            }

            // 턴 사이사이 개입만 허용한다.
            return !actorA.IsMoveRunning
                && !actorB.IsMoveRunning
                && actorA._recoilVelocity.sqrMagnitude <= 0f
                && actorB._recoilVelocity.sqrMagnitude <= 0f;
        }

        public bool TryUseSpecialSkill(SpecialSkill skill, Actor user, Actor target)
        {
            if (skill == null || user == null)
            {
                return false;
            }

            CombatContext context = new CombatContext
            {
                user = user,
                target = target,
                manager = this
            };

            return skill.TryUse(context);
        }

        public void StopActorForTurns(Actor actor, int turns)
        {
            if (actor == null || turns <= 0)
            {
                return;
            }

            if (actor == actorA)
            {
                StopActorAForTurns(turns);
                return;
            }

            if (actor == actorB)
            {
                StopActorBForTurns(turns);
            }
        }

        public bool ForceActorInterrupt(Actor actor, MoveEventType trigger, InterruptReason reason)
        {
            if (actor == null || !actor.IsMoveRunning)
            {
                return false;
            }

            actor.Interrupt(trigger, reason, this.combatContext);
            return true;
        }

        private bool PrepareActor(Actor actor)
        {
            if (actor == null || actor.PlanMaker == null)
            {
                return false;
            }

            if (actor.QueueCount == 0)
            {
                PlanQueryState planState = actor.PlanMaker.GetPlan(actor);

                if (planState == PlanQueryState.Ready)
                {
                    if (actor.TryConsumePlannedMove(out Move plannedMove))
                    {
                        actor.ActionController.Enqueue(plannedMove);
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            PlanQueryState forceState = actor.PlanMaker.GetForce(actor);
            return forceState == PlanQueryState.Ready;
        }

        public int SelectForce(Actor actor, Move move)
        {
            return move != null && move.UsesForce ? 1 : 0;
        }

        private ExchangeInfo ResolveExchange(Actor a, Actor b)
        {
            if (TryGetWeaponWeaponTouch(a, b, out Hitbox aClashHitbox, out Hitbox bClashHitbox))
            {
                return new ExchangeInfo
                {
                    result = ExchangeResult.Clash,
                    hitboxA = aClashHitbox,
                    hitboxB = bClashHitbox
                };
            }

            bool aWeaponBBody = TryGetWeaponBodyTouch(a.weaponHitboxes, b.bodyCollider, a.MoveProgress, out Hitbox aBodyHitbox);
            bool bWeaponABody = TryGetWeaponBodyTouch(b.weaponHitboxes, a.bodyCollider, b.MoveProgress, out Hitbox bBodyHitbox);

            if (aWeaponBBody && bWeaponABody)
            {
                return new ExchangeInfo
                {
                    result = ExchangeResult.Clash,
                    hitboxA = aBodyHitbox,
                    hitboxB = bBodyHitbox
                };
            }

            if (aWeaponBBody)
            {
                return new ExchangeInfo
                {
                    result = b.CanGuard ? ExchangeResult.ABlocksB : ExchangeResult.AHitsB,
                    hitboxA = aBodyHitbox
                };
            }

            if (bWeaponABody)
            {
                return new ExchangeInfo
                {
                    result = a.CanGuard ? ExchangeResult.BBlocksA : ExchangeResult.BHitsA,
                    hitboxB = bBodyHitbox
                };
            }

            return new ExchangeInfo
            {
                result = ExchangeResult.None
            };
        }

        private void ApplyExchange(ExchangeInfo exchange)
        {
            if (exchange.result == ExchangeResult.None)
            {
                return;
            }

            MoveRuntime aState = actorA.Current;
            MoveRuntime bState = actorB.Current;

            var aStance = aState.move != null && exchange.hitboxA != null
                ? (int)(aState.move.getStanceDamage(aState.force) * exchange.hitboxA.StanceCoef)
                : 0;
            var bStance = bState.move != null && exchange.hitboxB != null
                ? (int)(bState.move.getStanceDamage(bState.force) * exchange.hitboxB.StanceCoef)
                : 0;
            var aDamage = aState.move != null && exchange.hitboxA != null
                ? (int)(aState.move.getDamage(aState.force) * exchange.hitboxA.DamageCoef)
                : 0;
            var bDamage = bState.move != null && exchange.hitboxB != null
                ? (int)(bState.move.getDamage(bState.force) * exchange.hitboxB.DamageCoef)
                : 0;

            CombatContext context = new CombatContext
            {
                user = actorA,
                target = actorB,
                manager = this,
                exchangeResult = exchange.result,
                userStanceDamage = aStance,
                targetStanceDamage = bStance,
                userHpDamage = aDamage,
                targetHpDamage = bDamage,
            };

            // Capture the attacking Moves before an interrupt replaces the current action.
            KnockbackDistances knockback = CalculateKnockbackDistances(context);
            switch (exchange.result)
            {
                case ExchangeResult.Clash:
                    if (aState.move != null)
                    {
                        aState.move.OnClash(actorA, context);
                    }

                    if (bState.move != null)
                    {
                        bState.move.OnClash(actorB, context);
                    }

                    aState.move.OnAttack(actorA, context);
                    bState.move.OnAttack(actorB, context);
                    DisableHitbox(exchange.hitboxA);
                    DisableHitbox(exchange.hitboxB);

                    ApplyStanceDamageAndReaction(actorA, (int)(context.targetStanceDamage * clashDecrease),
                        null, context);
                    ApplyStanceDamageAndReaction(actorB, (int)(context.userStanceDamage * clashDecrease),
                        null, context);
                    break;

                case ExchangeResult.ABlocksB:
                    aState.move.OnAttack(actorA, context);
                    DisableHitbox(exchange.hitboxA);
                    ApplyStanceDamageAndReaction(actorB, context.userStanceDamage, MoveEventType.Guard, context);
                    ApplyStanceDamageAndReaction(actorA, Mathf.Max(1, context.targetStanceDamage), null, context);
                    break;

                case ExchangeResult.BBlocksA:
                    bState.move.OnAttack(actorB, context);
                    DisableHitbox(exchange.hitboxB);
                    ApplyStanceDamageAndReaction(actorA, context.targetStanceDamage, MoveEventType.Guard, context);
                    ApplyStanceDamageAndReaction(actorB, Mathf.Max(1, context.userStanceDamage), null, context);
                    break;

                case ExchangeResult.AHitsB:
                    aState.move.OnAttack(actorA, context);
                    DisableHitbox(exchange.hitboxA);
                    context.userHpDamage =
                        Mathf.RoundToInt(context.userHpDamage * actorA.ConsumeNextAttackDamageMultiplier());
                    actorB.ApplyHpDamage(context.userHpDamage);
                    ApplyStanceDamageAndReaction(actorB, context.userStanceDamage, MoveEventType.Hit, context);
                    break;

                case ExchangeResult.BHitsA:
                    bState.move.OnAttack(actorB, context);
                    DisableHitbox(exchange.hitboxB);
                    context.targetHpDamage =
                        Mathf.RoundToInt(context.targetHpDamage * actorB.ConsumeNextAttackDamageMultiplier());
                    actorA.ApplyHpDamage(context.targetHpDamage);
                    ApplyStanceDamageAndReaction(actorA, context.targetStanceDamage, MoveEventType.Hit, context);
                    break;
            }

            ApplyKnockback(knockback);
        }

        private static void ApplyStanceDamageAndReaction(Actor actor, int damage, MoveEventType? normalReaction,
            CombatContext context)
        {
            bool hadStance = actor.Stance > 0;
            actor.ApplyStanceDamage(damage);
            bool stanceBroken = hadStance && actor.IsGuardBroken;
            if (stanceBroken)
            {
                actor.InterruptWithFollowUps(MoveEventType.Hit, InterruptReason.Hit, context, 2);
            }
            else if (normalReaction.HasValue)
            {
                MoveEventType trigger = normalReaction.Value;
                actor.Interrupt(trigger, trigger == MoveEventType.Guard ? InterruptReason.Guard : InterruptReason.Hit,
                    context);
            }
        }

        private static bool Touching(Collider2D lhs, Collider2D rhs)
        {
            if (lhs == null || rhs == null || !lhs.enabled || !rhs.enabled)
            {
                return false;
            }
            return lhs.bounds.Intersects(rhs.bounds);
        }

        private static bool TryGetWeaponBodyTouch(System.Collections.Generic.IList<Hitbox> hitboxes, Collider2D body, float progress,
            out Hitbox touchingHitbox)
        {
            touchingHitbox = null;
            if (body == null || hitboxes == null)
            {
                return false;
            }

            for (int i = 0; i < hitboxes.Count; i++)
            {
                Hitbox hitbox = hitboxes[i];
                if (hitbox == null || !hitbox.IsActiveAt(progress) || !Touching(hitbox.Collider, body))
                {
                    continue;
                }

                touchingHitbox = hitbox;
                return true;
            }

            return false;
        }

        private static bool TryGetWeaponWeaponTouch(Actor a, Actor b, out Hitbox aHitbox, out Hitbox bHitbox)
        {
            aHitbox = null;
            bHitbox = null;

            for (int i = 0; i < a.weaponHitboxes.Count; i++)
            {
                Hitbox left = a.weaponHitboxes[i];
                if (left == null || !left.IsActiveAt(a.MoveProgress) || left.Collider == null || !left.Collider.enabled)
                {
                    continue;
                }

                for (int j = 0; j < b.weaponHitboxes.Count; j++)
                {
                    Hitbox right = b.weaponHitboxes[j];
                    if (right == null || !right.IsActiveAt(b.MoveProgress) || !Touching(left.Collider, right.Collider))
                    {
                        continue;
                    }

                    aHitbox = left;
                    bHitbox = right;
                    return true;
                }
            }

            return false;
        }

        private static void DisableHitbox(Hitbox hitbox)
        {
            if (hitbox == null || hitbox.Collider == null)
            {
                return;
            }

            hitbox.Collider.enabled = false;
        }

        private bool ShouldBlockStartA()
        {
            return stopTurnsA > 0 && actorA.QueueCount > 0;
        }

        private bool ShouldBlockStartB()
        {
            return stopTurnsB > 0 && actorB.QueueCount > 0;
        }

        private void ConsumeStopTurnA()
        {
            if (_consumedStopAInCurrentWindow || stopTurnsA <= 0)
            {
                return;
            }

            stopTurnsA--;
            _consumedStopAInCurrentWindow = true;
        }

        private void ConsumeStopTurnB()
        {
            if (_consumedStopBInCurrentWindow || stopTurnsB <= 0)
            {
                return;
            }

            stopTurnsB--;
            _consumedStopBInCurrentWindow = true;
        }

        private KnockbackDistances CalculateKnockbackDistances(CombatContext context)
        {
            float aPower = context.user.Current.move.GetImpactPower(context.user.Current.force);
            float bPower = context.target.Current.move.GetImpactPower(context.target.Current.force);
            return new KnockbackDistances()
            {
                actorA = CalculateKnockbackDistance(bPower, aPower),
                actorB = CalculateKnockbackDistance(aPower, bPower),
            };
        }

        // Equal powers trade space; the power difference shifts the exchange toward the weaker fighter.
        private static float CalculateKnockbackDistance(float incomingPower, float ownPower) =>
            Mathf.Max(0f, .06f * incomingPower + .03f * (incomingPower - ownPower));

        private void ApplyKnockback(KnockbackDistances distances)
        {
            Vector2 delta = actorA.Position - actorB.Position;
            float sign = delta.x >= 0f ? 1f : -1f;

            Vector2 dirA = new Vector2(sign, 0f);
            Vector2 dirB = -dirA;

            actorA.ResetAndApplyKnockbackDistance(dirA, distances.actorA, knockbackDuration);
            actorB.ResetAndApplyKnockbackDistance(dirB, distances.actorB, knockbackDuration);
        }
    }
}
