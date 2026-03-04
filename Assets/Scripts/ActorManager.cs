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
        [Header("Actors")]
        public Actor actorA;
        public Actor actorB;

        [Header("Simulation")]
        public bool autoSimulate = true;

        [Header("Defaults")]
        [Range(1, 5)] public int defaultForce = 3;

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

            if (actorA.IsMoveRunning && actorB.IsMoveRunning)
            {
                ExchangeResult result = ResolveExchange(actorA, actorB);
                ApplyExchange(result);
            }
        }

        public void TryStartActors()
        {
            if (!actorA.IsMoveRunning)
            {
                actorA.TryStartNextMove(SelectForce);
            }

            if (!actorB.IsMoveRunning)
            {
                actorB.TryStartNextMove(SelectForce);
            }
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

            bool aWeaponBGuard = !b.IsGuardBroken && Touching(a.weaponCollider, b.guardCollider);
            bool bWeaponAGuard = !a.IsGuardBroken && Touching(b.weaponCollider, a.guardCollider);

            if (aWeaponBGuard && bWeaponAGuard)
            {
                return ExchangeResult.Clash;
            }

            if (aWeaponBGuard)
            {
                return ExchangeResult.ABlocksB;
            }

            if (bWeaponAGuard)
            {
                return ExchangeResult.BBlocksA;
            }

            bool aWeaponBHurt = Touching(a.weaponCollider, b.hurtCollider);
            bool bWeaponAHurt = Touching(b.weaponCollider, a.hurtCollider);

            if (aWeaponBHurt && bWeaponAHurt)
            {
                return ExchangeResult.Clash;
            }

            if (aWeaponBHurt)
            {
                return ExchangeResult.AHitsB;
            }

            if (bWeaponAHurt)
            {
                return ExchangeResult.BHitsA;
            }

            return ExchangeResult.None;
        }

        private void ApplyExchange(ExchangeResult result)
        {
            if (result == ExchangeResult.None)
            {
                return;
            }

            MoveRuntime aState = actorA.Current;
            MoveRuntime bState = actorB.Current;

            int aStanceDamage = aState.move != null ? aState.move.baseStanceDamage : 0;
            int bStanceDamage = bState.move != null ? bState.move.baseStanceDamage : 0;

            int aDamage = aState.move != null ? Mathf.RoundToInt(aState.move.baseDamage * actorA.ChainMultiplier) : 0;
            int bDamage = bState.move != null ? Mathf.RoundToInt(bState.move.baseDamage * actorB.ChainMultiplier) : 0;

            switch (result)
            {
                case ExchangeResult.Clash:
                    actorA.ApplyStanceDamage(bStanceDamage);
                    actorB.ApplyStanceDamage(aStanceDamage);
                    break;

                case ExchangeResult.ABlocksB:
                    actorB.Interrupt(MoveEventType.Guard, InterruptReason.Guard);
                    actorB.ApplyStanceDamage(aStanceDamage);
                    actorA.ApplyStanceDamage(Mathf.Max(1, bStanceDamage));
                    break;

                case ExchangeResult.BBlocksA:
                    actorA.Interrupt(MoveEventType.Guard, InterruptReason.Guard);
                    actorA.ApplyStanceDamage(bStanceDamage);
                    actorB.ApplyStanceDamage(Mathf.Max(1, aStanceDamage));
                    break;

                case ExchangeResult.AHitsB:
                    actorB.Interrupt(MoveEventType.Hit, InterruptReason.Hit);
                    actorB.ApplyHpDamage(aDamage);
                    break;

                case ExchangeResult.BHitsA:
                    actorA.Interrupt(MoveEventType.Hit, InterruptReason.Hit);
                    actorA.ApplyHpDamage(bDamage);
                    break;
            }

            // TODO: 충돌 결과별 밀려남 거리 계산/적용.
        }

        private static bool Touching(Collider2D lhs, Collider2D rhs)
        {
            if (lhs == null || rhs == null || !lhs.enabled || !rhs.enabled)
            {
                return false;
            }

            return lhs.IsTouching(rhs);
        }
    }
}
