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

        [Header("Actors")]
        public Actor actorA;
        public Actor actorB;

        [Header("Simulation")]
        public bool autoSimulate = true;

        [Header("Defaults")]
        [Range(1, 5)] public int defaultForce = 3;

        [Header("Knockback")]
        [Min(0f)] public float baseKnockbackSpeed = 0.5f;
        [Min(0f)] public float stanceDamageToKnockback = 0.15f;
        [Min(0f)] public float hpDamageToKnockback = 0.10f;
        [Min(0f)] public float maxKnockbackSpeed = 8f;
        [Min(0f)] public float knockbackFriction = 10f;

        [Header("Turn Stop (Debug)")]
        [SerializeField, Min(0)] private int stopTurnsA;
        [SerializeField, Min(0)] private int stopTurnsB;

        // 프레임마다 stop 턴이 줄어드는 것을 막기 위한 게이트.
        private bool _consumedStopAInCurrentWindow;
        private bool _consumedStopBInCurrentWindow;

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
            TryStartActors();

            actorA.Tick(deltaTime);
            actorB.Tick(deltaTime);

            if (actorA.IsMoveRunning && actorB.IsMoveRunning
                && actorA.IsReadyForExchange && actorB.IsReadyForExchange)
            {
                ExchangeInfo exchange = ResolveExchange(actorA, actorB);
                if (exchange.result == ExchangeResult.ABlocksB || exchange.result == ExchangeResult.BBlocksA || exchange.result == ExchangeResult.AHitsB || exchange.result == ExchangeResult.BHitsA || exchange.result == ExchangeResult.Clash)
                {
                    Debug.Log(exchange.result);
                }
                ApplyExchange(exchange);
            }
        }

        public void TryStartActors()
        {
            if (actorA.IsMoveRunning || actorB.IsMoveRunning)
            {
                return;
            }

            bool startedA = false;
            bool startedB = false;

            if (ShouldBlockStartA())
            {
                ConsumeStopTurnA();
            }
            else
            {
                startedA = actorA.TryStartNextMove(SelectForce);
            }

            if (ShouldBlockStartB())
            {
                ConsumeStopTurnB();
            }
            else
            {
                startedB = actorB.TryStartNextMove(SelectForce);
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

            actor.Interrupt(trigger, reason);
            return true;
        }

        public int SelectForce(Actor actor, Move move)
        {
            return defaultForce;
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

            CombatContext context = new CombatContext
            {
                user = actorA,
                target = actorB,
                manager = this,
                exchangeResult = exchange.result,
                userStanceDamage = aState.move != null ? aState.move.StanceDamage : 0,
                targetStanceDamage = bState.move != null ? bState.move.StanceDamage : 0,
                userHpDamage = exchange.hitboxA != null ? Mathf.RoundToInt(exchange.hitboxA.trueDamage * actorA.ChainMultiplier) : 0,
                targetHpDamage = exchange.hitboxB != null ? Mathf.RoundToInt(exchange.hitboxB.trueDamage * actorB.ChainMultiplier) : 0
            };

            switch (exchange.result)
            {
                case ExchangeResult.Clash:
                    DisableHitbox(exchange.hitboxA);
                    DisableHitbox(exchange.hitboxB);
                    actorA.ApplyStanceDamage(context.targetStanceDamage);
                    actorB.ApplyStanceDamage(context.userStanceDamage);
                    break;

                case ExchangeResult.ABlocksB:
                    DisableHitbox(exchange.hitboxA);
                    actorB.Interrupt(MoveEventType.Guard, InterruptReason.Guard);
                    actorB.ApplyStanceDamage(context.userStanceDamage);
                    actorA.ApplyStanceDamage(Mathf.Max(1, context.targetStanceDamage));
                    break;

                case ExchangeResult.BBlocksA:
                    DisableHitbox(exchange.hitboxB);
                    actorA.Interrupt(MoveEventType.Guard, InterruptReason.Guard);
                    actorA.ApplyStanceDamage(context.targetStanceDamage);
                    actorB.ApplyStanceDamage(Mathf.Max(1, context.userStanceDamage));
                    break;

                case ExchangeResult.AHitsB:
                    DisableHitbox(exchange.hitboxA);
                    context.userHpDamage = Mathf.RoundToInt(context.userHpDamage * actorA.ConsumeNextAttackDamageMultiplier());
                    actorB.Interrupt(MoveEventType.Hit, InterruptReason.Hit);
                    actorB.ApplyHpDamage(context.userHpDamage);
                    break;

                case ExchangeResult.BHitsA:
                    DisableHitbox(exchange.hitboxB);
                    context.targetHpDamage = Mathf.RoundToInt(context.targetHpDamage * actorB.ConsumeNextAttackDamageMultiplier());
                    actorA.Interrupt(MoveEventType.Hit, InterruptReason.Hit);
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

        private static bool TryGetWeaponBodyTouch(System.Collections.Generic.IList<Hitbox> hitboxes, Collider2D body, out Hitbox touchingHitbox)
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
            float speedA = baseKnockbackSpeed
                + (context.targetHpDamage * hpDamageToKnockback)
                + (context.targetStanceDamage * stanceDamageToKnockback)
                - context.user.KnockbackResistance;
            float speedB = baseKnockbackSpeed
                + (context.userHpDamage * hpDamageToKnockback)
                + (context.userStanceDamage * stanceDamageToKnockback)
                - context.target.KnockbackResistance;

            KnockbackSpeeds result;
            result.speedA = Mathf.Clamp(speedA, 0f, maxKnockbackSpeed);
            result.speedB = Mathf.Clamp(speedB, 0f, maxKnockbackSpeed);
            return result;
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
