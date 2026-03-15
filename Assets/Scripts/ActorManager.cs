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
                && actorA.IsReadyForExchange && actorB.IsReadyForExchange
                && !actorA.HasResolvedExchange && !actorB.HasResolvedExchange)
            {
                ExchangeResult result = ResolveExchange(actorA, actorB);
                if(result==ExchangeResult.ABlocksB || result == ExchangeResult.BBlocksA || actorA.Current.move.hasWeaponCollider || actorB.Current.move.hasWeaponCollider)
                {
                    Debug.Log(result);
                }
                ApplyExchange(result);
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

        private ExchangeResult ResolveExchange(Actor a, Actor b)
        {
            bool weaponWeapon = Touching(a.weaponCollider, b.weaponCollider);
            if (weaponWeapon)
            {
                return ExchangeResult.Clash;
            }

            bool aWeaponBBody = Touching(a.weaponCollider, b.bodyCollider);
            bool bWeaponABody = Touching(b.weaponCollider, a.bodyCollider);

            if (aWeaponBBody && bWeaponABody)
            {
                return ExchangeResult.Clash;
            }

            if (aWeaponBBody)
            {
                return b.CanGuard ? ExchangeResult.ABlocksB : ExchangeResult.AHitsB;
            }

            if (bWeaponABody)
            {
                return a.CanGuard ? ExchangeResult.BBlocksA : ExchangeResult.BHitsA;
            }

            return ExchangeResult.None;
        }

        private void ApplyExchange(ExchangeResult result)
        {
            if (result == ExchangeResult.None)
            {
                return;
            }

            actorA.MarkCurrentMoveExchanged();
            actorB.MarkCurrentMoveExchanged();

            MoveRuntime aState = actorA.Current;
            MoveRuntime bState = actorB.Current;

            CombatContext context = new CombatContext
            {
                user = actorA,
                target = actorB,
                manager = this,
                exchangeResult = result,
                userStanceDamage = aState.move != null ? aState.move.StanceDamage : 0,
                targetStanceDamage = bState.move != null ? bState.move.StanceDamage : 0,
                userHpDamage = aState.move != null ? Mathf.RoundToInt(aState.move.Damage * actorA.ChainMultiplier) : 0,
                targetHpDamage = bState.move != null ? Mathf.RoundToInt(bState.move.Damage * actorB.ChainMultiplier) : 0
            };

            switch (result)
            {
                case ExchangeResult.Clash:
                    actorA.ApplyStanceDamage(context.targetStanceDamage);
                    actorB.ApplyStanceDamage(context.userStanceDamage);
                    break;

                case ExchangeResult.ABlocksB:
                    actorB.Interrupt(MoveEventType.Guard, InterruptReason.Guard);
                    actorB.ApplyStanceDamage(context.userStanceDamage);
                    actorA.ApplyStanceDamage(Mathf.Max(1, context.targetStanceDamage));
                    break;

                case ExchangeResult.BBlocksA:
                    actorA.Interrupt(MoveEventType.Guard, InterruptReason.Guard);
                    actorA.ApplyStanceDamage(context.targetStanceDamage);
                    actorB.ApplyStanceDamage(Mathf.Max(1, context.userStanceDamage));
                    break;

                case ExchangeResult.AHitsB:
                    context.userHpDamage = Mathf.RoundToInt(context.userHpDamage * actorA.ConsumeNextAttackDamageMultiplier());
                    actorB.Interrupt(MoveEventType.Hit, InterruptReason.Hit);
                    actorB.ApplyHpDamage(context.userHpDamage);
                    break;

                case ExchangeResult.BHitsA:
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
