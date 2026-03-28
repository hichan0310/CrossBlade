using Unity.VisualScripting;
using UnityEngine;

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
        private struct ExchangeInfo
        {
            public ExchangeResult result;
            public Hitbox hitboxA;
            public Hitbox hitboxB;
        }

        private struct KnockbackSpeeds
        {
            public float speedA;
            public float speedB;
        }

        [Header("Actors")] public Actor actorA;
        public Actor actorB;
        public CombatContext combatContext;

        [Header("Simulation")] public bool autoSimulate = true;

        [Header("Defaults")] [Header("Knockback")] [SerializeField]
        private float c;

        [SerializeField] private float d;
        [SerializeField] private float l;
        [Min(0f)] public float knockbackFriction = 10f;
        [SerializeField] private float clashDecrease;

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
        }

        private void Update()
        {
            if (!autoSimulate || actorA == null || actorB == null)
            {
                return;
            }

            Simulate(Time.deltaTime);
        }

        public void Simulate(float deltaTime)
        {
            this.actorA.ForceUpdate(deltaTime);
            this.actorB.ForceUpdate(deltaTime);
            TryStartActors();
            UpdateFacing();
            ApplyMovement(deltaTime);

            if (actorA.IsMoveRunning && actorB.IsMoveRunning
                                     && actorA.IsReadyForExchange && actorB.IsReadyForExchange)
            {
                ExchangeInfo exchange = ResolveExchange(actorA, actorB);
                if (exchange.result == ExchangeResult.ABlocksB || exchange.result == ExchangeResult.BBlocksA ||
                    exchange.result == ExchangeResult.AHitsB || exchange.result == ExchangeResult.BHitsA ||
                    exchange.result == ExchangeResult.Clash)
                {
                    Debug.Log(exchange.result);
                }

                ApplyExchange(exchange);
            }

            actorA.Tick(deltaTime);
            actorB.Tick(deltaTime);
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
                    if (!actor.IsMoveRunning && actor.HasMoveVisual)
                    {
                        return;
                    }

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
            if (actor == null || target == null || !actor.IsMoveRunning)
            {
                return;
            }

            Move move = actor.Current.move;
            if (move == null)
            {
                return;
            }

            if (!ShouldMoveNow(actor, move))
            {
                return;
            }

            switch (move.MovementMode)
            {
                case MovementMode.None:
                    return;

                case MovementMode.StopAtRange:
                    MoveTowardRange(actor, target, move.StopDistance, move.Speed, deltaTime);
                    return;

                case MovementMode.PassThroughTarget:
                {
                    float targetX = target.Position.x + (actor.MoveStartFacingSign * move.PassThroughOffset);
                    MoveToward(actor, targetX, GetMovementProgress(actor, move));
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

                    actor.MoveBy(new Vector2(direction * moveAmount, 0f));
                    return;
                }

                case MovementMode.FixedDistanceForward:
                {
                    float targetX = actor.MoveStartPosition.x + (actor.MoveStartFacingSign * move.FixedTravelDistance);
                    MoveToward(actor, targetX, GetMovementProgress(actor, move));
                    return;
                }
            }
        }

        private static void MoveTowardRange(Actor actor, Actor target, float stopDistance, float speed, float deltaTime)
        {
            float deltaX = target.Position.x - actor.Position.x;
            float distanceX = Mathf.Abs(deltaX);
            float remaining = distanceX - stopDistance;

            if (remaining <= 0f)
            {
                return;
            }

            float moveAmount = Mathf.Min(speed * deltaTime, remaining);
            actor.MoveBy(new Vector2(Mathf.Sign(deltaX) * moveAmount, 0f));
        }

        private static void MoveToward(Actor actor, float targetX, float progress)
        {
            float startX = actor.MoveStartPosition.x;
            float x = Mathf.Lerp(startX, targetX, Mathf.Clamp01(progress));
            actor.MoveTo(new Vector2(x, actor.Position.y));
        }

        public void TryStartActors()
        {
            if (actorA.IsMoveRunning || actorB.IsMoveRunning)
            {
                return;
            }

            bool startedA = false;
            bool startedB = false;
            bool AcanStartNow = false;
            bool BcanStartNow = false;

            if (actorA.QueueCount > 0)
            {
                if (actorA.ActionController.nextMove.UsesForce && !actorA.GettingForce && !actorA.GettingForceFinished)
                {
                    actorA.StartGettingForce();
                }

                if ((!actorA.GettingForce && actorA.GettingForceFinished) || !actorA.ActionController.nextMove.UsesForce)
                {
                    AcanStartNow = true;
                }
            }
            else
            {
                actorA.ActionController.FillQueue(actorA.Current.move);
            }

            if (actorB.QueueCount > 0)
            {
                BcanStartNow = true;
            }
            else
            {
                actorB.ActionController.FillQueue(actorB.Current.move);
            }

            if (AcanStartNow && BcanStartNow)
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

            // 상대가 새 Move를 시작하면 다음 정지 턴을 소비할 수 있게 윈도우를 리셋한다.
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
            return !actorA.IsMoveRunning && !actorB.IsMoveRunning;
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

        public int SelectForce(Actor actor, Move move)
        {
            if (actor != null && move != null && move.UsesForce)
            {
                return actor.ConsumePendingForce();
            }

            return 0;
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

            bool aWeaponBBody = TryGetWeaponBodyTouch(a.weaponHitboxes, b.bodyCollider, out Hitbox aBodyHitbox);
            bool bWeaponABody = TryGetWeaponBodyTouch(b.weaponHitboxes, a.bodyCollider, out Hitbox bBodyHitbox);

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
            Debug.Log(bState.move);
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

                    actorA.ApplyStanceDamage((int)(context.targetStanceDamage * clashDecrease));
                    actorB.ApplyStanceDamage((int)(context.userStanceDamage * clashDecrease));
                    break;

                case ExchangeResult.ABlocksB:
                    aState.move.OnAttack(actorA, context);
                    DisableHitbox(exchange.hitboxA);
                    actorB.Interrupt(MoveEventType.Guard, InterruptReason.Guard, context);
                    actorB.ApplyStanceDamage(context.userStanceDamage);
                    actorA.ApplyStanceDamage(Mathf.Max(1, context.targetStanceDamage));
                    break;

                case ExchangeResult.BBlocksA:
                    bState.move.OnAttack(actorB, context);
                    DisableHitbox(exchange.hitboxB);
                    actorA.Interrupt(MoveEventType.Guard, InterruptReason.Guard, context);
                    actorA.ApplyStanceDamage(context.targetStanceDamage);
                    actorB.ApplyStanceDamage(Mathf.Max(1, context.userStanceDamage));
                    break;

                case ExchangeResult.AHitsB:
                    aState.move.OnAttack(actorA, context);
                    DisableHitbox(exchange.hitboxA);
                    context.userHpDamage =
                        Mathf.RoundToInt(context.userHpDamage * actorA.ConsumeNextAttackDamageMultiplier());
                    actorB.Interrupt(MoveEventType.Hit, InterruptReason.Hit, context);
                    actorB.ApplyHpDamage(context.userHpDamage);
                    break;

                case ExchangeResult.BHitsA:
                    bState.move.OnAttack(actorB, context);
                    DisableHitbox(exchange.hitboxB);
                    context.targetHpDamage =
                        Mathf.RoundToInt(context.targetHpDamage * actorB.ConsumeNextAttackDamageMultiplier());
                    actorA.Interrupt(MoveEventType.Hit, InterruptReason.Hit, context);
                    actorA.ApplyHpDamage(context.targetHpDamage);
                    break;
            }

            KnockbackSpeeds knockback = CalculateKnockbackSpeeds(context);
            ApplyKnockback(knockback);
        }

        private static bool Touching(Collider2D lhs, Collider2D rhs)
        {
            if (lhs == null || rhs == null || !lhs.enabled || !rhs.enabled)
            {
                return false;
            }

            return lhs.IsTouching(rhs);
        }

        private static bool TryGetWeaponBodyTouch(System.Collections.Generic.IList<Hitbox> hitboxes, Collider2D body,
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
                if (hitbox == null || !Touching(hitbox.Collider, body))
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
                if (left == null || left.Collider == null || !left.Collider.enabled)
                {
                    continue;
                }

                for (int j = 0; j < b.weaponHitboxes.Count; j++)
                {
                    Hitbox right = b.weaponHitboxes[j];
                    if (right == null || !Touching(left.Collider, right.Collider))
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

        private KnockbackSpeeds CalculateKnockbackSpeeds(CombatContext context)
        {
            var aforce = context.user.Current.force;
            var bforce = context.target.Current.force;

            var apower = context.user.Current.move.getPower(aforce);
            var bpower = context.target.Current.move.getPower(bforce);

            var diff = Mathf.Abs(apower - bpower);
            var coef = l * (diff + c) / (diff + d);

            return new KnockbackSpeeds()
            {
                speedA = bpower * coef,
                speedB = apower * coef,
            };
        }

        private void ApplyKnockback(KnockbackSpeeds speeds)
        {
            Vector2 delta = actorA.Position - actorB.Position;
            float sign = delta.x >= 0f ? 1f : -1f;

            Vector2 dirA = new Vector2(sign, 0f);
            Vector2 dirB = -dirA;

            actorA.ResetAndApplyKnockback(dirA, speeds.speedA, knockbackFriction);
            actorB.ResetAndApplyKnockback(dirB, speeds.speedB, knockbackFriction);
        }
    }
}
