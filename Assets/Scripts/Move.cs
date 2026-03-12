using System.Collections.Generic;
using UnityEngine;

namespace Scripts
{
    public enum MoveEventType
    {
        NormalEnd,
        Hit,
        Guard,
        Clash
    }

    public enum MoveCategory
    {
        Neutral,
        Attack,
        Dash,
        Guard,
        HitReaction
    }

    public abstract class Move : MonoBehaviour
    {
        [Header("Identity")]
        public string moveId = "move";
        public MoveCategory category = MoveCategory.Neutral;

        [Header("Timing")]
        [Min(0.01f)] public float duration = 0.30f;

        [Header("Visual")]
        public Sprite frameSprite;

        [Header("Motion")]
        public bool useStepMovement;
        public Vector2 stepOffset;
        public bool useTeleport;
        public Vector2 teleportOffset;

        [Header("Stance")]
        [Min(0f)] public float stanceCostScale = 1f;
        public int stanceRecoveryOnFinish = 1;

        [Header("Combat")]
        public int baseDamage = 0;
        public int baseStanceDamage = 0;

        [Header("Graph")]
        public Move hitMove;
        public Move guardMove;
        public List<Move> after = new List<Move>();
        public bool skipAdditionalInterruptFollowUp;

        public int GetStanceCost(int force)
        {
            return Mathf.CeilToInt(force * stanceCostScale);
        }

        public virtual float GetTravelDistance(int force)
        {
            return 0f;
        }

        public virtual int forceCarryIn
        {
            get
            {
                return 0;
            }
        }

        public virtual int forceCarryOut
        {
            get
            {
                return 0;
            }
        }

        public Move GetNext(MoveEventType trigger)
        {
            switch (trigger)
            {
                case MoveEventType.Hit:
                    return hitMove;
                case MoveEventType.Guard:
                    return guardMove;
                case MoveEventType.Clash:
                case MoveEventType.NormalEnd:
                    return (after != null && after.Count > 0) ? after[0] : null;
                default:
                    return null;
            }
        }

        protected static int ClampInputForce(int force)
        {
            return Mathf.Clamp(force, 1, 5);
        }
    }

    [AddComponentMenu("CrossBlade/Move/Base Move")]
    public class BaseMove : Move
    {
    }

    [AddComponentMenu("CrossBlade/Move/Attack Move")]
    public class AttackMove : Move
    {
        [Tooltip("Force 1~5 each slot. Missing slots reuse nearest valid value.")]
        public float[] travelDistanceByForce = new float[5];

        public override float GetTravelDistance(int force)
        {
            return ReadByForce(travelDistanceByForce, force);
        }

        private static float ReadByForce(float[] values, int force)
        {
            if (values == null || values.Length == 0)
            {
                return 0f;
            }

            int idx = ClampInputForce(force) - 1;
            idx = Mathf.Clamp(idx, 0, values.Length - 1);
            return values[idx];
        }
    }

    [AddComponentMenu("CrossBlade/Move/Dash Move")]
    public class DashMove : Move
    {
        [Tooltip("Force 1~5 each slot. Missing slots reuse nearest valid value.")]
        public float[] travelDistanceByForce = new float[5];

        public override float GetTravelDistance(int force)
        {
            return ReadByForce(travelDistanceByForce, force);
        }

        private static float ReadByForce(float[] values, int force)
        {
            if (values == null || values.Length == 0)
            {
                return 0f;
            }

            int idx = ClampInputForce(force) - 1;
            idx = Mathf.Clamp(idx, 0, values.Length - 1);
            return values[idx];
        }
    }
}
