using System;
using System.Collections.Generic;
using UnityEngine;

namespace Scripts.TurnMotion
{
    [Serializable]
    public class TurnMotionPoint
    {
        public string label;
        [Min(0f)] public float sourceTime;
    }

    [Serializable]
    public class TurnMotionSegment
    {
        public string label;
        public float sourceStartTime;
        public float sourceEndTime;
        public float turnStartTime;
        public float turnEndTime;

        public float SourceDuration => Mathf.Max(0f, sourceEndTime - sourceStartTime);
        public float TurnDuration => Mathf.Max(0f, turnEndTime - turnStartTime);
        public float PlaybackSpeed => TurnDuration > 0f ? SourceDuration / TurnDuration : 0f;
    }

    [CreateAssetMenu(menuName = "CrossBlade/Turn Motion Definition", fileName = "TurnMotionDefinition")]
    public class TurnMotionDefinition : ScriptableObject
    {
        [SerializeField] private AnimationClip sourceClip;
        [SerializeField, Min(0.01f)] private float turnDuration = 0.5f;
        [SerializeField, Min(1f)] private float outputFps = 30f;
        [SerializeField] private List<TurnMotionPoint> points = new List<TurnMotionPoint>();
        [SerializeField] private List<TurnMotionSegment> segments = new List<TurnMotionSegment>();
        [SerializeField] private AnimationClip generatedClip;
        [SerializeField] private GameObject actionPrefab;

        public AnimationClip SourceClip => sourceClip;
        public float TurnDuration => turnDuration;
        public float OutputFps => outputFps;
        public IReadOnlyList<TurnMotionPoint> Points => points;
        public IReadOnlyList<TurnMotionSegment> Segments => segments;
        public AnimationClip GeneratedClip => generatedClip;
        public GameObject ActionPrefab => actionPrefab;

        public void Configure(
            AnimationClip source, float duration, float fps, IEnumerable<TurnMotionPoint> selectedPoints,
            IEnumerable<TurnMotionSegment> generatedSegments, AnimationClip outputClip, GameObject prefab)
        {
            sourceClip = source;
            turnDuration = Mathf.Max(0.01f, duration);
            outputFps = Mathf.Max(1f, fps);
            points = new List<TurnMotionPoint>(selectedPoints);
            segments = new List<TurnMotionSegment>(generatedSegments);
            generatedClip = outputClip;
            actionPrefab = prefab;
        }
    }
}
