using System.Collections.Generic;
using UnityEngine;

namespace MotionPrototype
{
    internal sealed class MotionSequenceAsset : ScriptableObject
    {
        public GameObject previewCharacter;
        [Range(1, 120)] public int outputFps = 30;
        public bool keepRootMotion;
        public List<MotionSegment> segments = new();
    }
}
