using System.Collections.Generic;
using UnityEngine;

namespace MotionPrototype
{
    internal sealed class MotionGraphAsset : ScriptableObject
    {
        public List<MotionGraphNode> nodes = new();
        public List<MotionGraphEdge> edges = new();
    }
}
