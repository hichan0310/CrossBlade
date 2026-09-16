using System;
using System.Collections.Generic;
using UnityEngine;

namespace MotionPrototype
{
    [Serializable]
    internal sealed class MotionSegment
    {
        public string label;
        public AnimationClip clip;
        [Min(0)] public int firstFrame;
        [Min(0)] public int lastFrame;
        [Min(0.01f)] public float speed = 1f;
        [Min(0)] public int blendFrames;

        public float Rate => clip != null ? Mathf.Max(1f, clip.frameRate) : 30f;
        public int MaximumFrame => clip != null ? Mathf.Max(0, Mathf.RoundToInt(clip.length * Rate)) : 0;
        public float SourceDuration => clip == null ? 0f : Mathf.Max(0, lastFrame - firstFrame) / Rate;
        public float Duration => SourceDuration / Mathf.Max(0.01f, speed);
    }


    [Serializable]
    internal sealed class MotionGraphNode
    {
        public string id = Guid.NewGuid().ToString("N");
        public string label;
        public MotionSequenceAsset sequence;
        public Vector2 position;
    }

    [Serializable]
    internal sealed class MotionGraphEdge
    {
        public string from;
        public string to;
        public string trigger = "Next";
    }

}
