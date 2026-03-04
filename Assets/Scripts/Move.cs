using System;
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

    [Serializable]
    public class MoveTransition
    {
        public MoveEventType trigger = MoveEventType.NormalEnd;
        public Move next;
    }

    public abstract class Move : ScriptableObject
    {
        [Header("Identity")]
        public string moveId = "move";
        public MoveCategory category = MoveCategory.Neutral;

        [Header("Timing")]
        [Min(0.01f)] public float duration = 0.30f;
        public AnimationClip clip;

        [Header("Stance")]
        [Min(0f)] public float stanceCostScale = 1f;
        public int stanceRecoveryOnFinish = 0;

        [Header("Combat")]
        public int baseDamage = 1;
        public int baseStanceDamage = 1;

        [Header("Graph")]
        public Move defaultNext;
        public List<MoveTransition> transitions = new List<MoveTransition>();

        public int GetStanceCost(int force)
        {
            int clamped = ClampForce(force);
            return Mathf.CeilToInt(clamped * stanceCostScale);
        }

        public virtual float GetTravelDistance(int force)
        {
            return 0f;
        }

        public Move GetNext(MoveEventType trigger)
        {
            for (int i = 0; i < transitions.Count; i++)
            {
                if (transitions[i] != null && transitions[i].trigger == trigger)
                {
                    return transitions[i].next;
                }
            }

            if (trigger == MoveEventType.NormalEnd)
            {
                return defaultNext;
            }

            return null;
        }

        protected static int ClampForce(int force)
        {
            return Mathf.Clamp(force, 1, 5);
        }
    }

    [CreateAssetMenu(menuName = "CrossBlade/Move/Base Move", fileName = "Move_Base")]
    public class BaseMove : Move
    {
    }

    [CreateAssetMenu(menuName = "CrossBlade/Move/Attack Move", fileName = "Move_Attack")]
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

            int idx = ClampForce(force) - 1;
            idx = Mathf.Clamp(idx, 0, values.Length - 1);
            return values[idx];
        }
    }

    [CreateAssetMenu(menuName = "CrossBlade/Move/Dash Move", fileName = "Move_Dash")]
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

            int idx = ClampForce(force) - 1;
            idx = Mathf.Clamp(idx, 0, values.Length - 1);
            return values[idx];
        }
    }
}
