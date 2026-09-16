using System.Collections.Generic;
using Scripts;
using UnityEngine;

internal sealed class MovePreviewSequence : ScriptableObject
{
    public List<Move> moves = new();
    [Min(0)] public float startupSeconds = .1f;
}
