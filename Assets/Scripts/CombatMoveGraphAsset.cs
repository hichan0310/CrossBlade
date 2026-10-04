using System;
using System.Collections.Generic;
using Scripts;
using UnityEngine;

[Serializable]
public sealed class CombatMoveGraphNode
{
    public Move move;
    public Vector2 position;
}

[CreateAssetMenu(menuName = "CrossBlade/Combat Move Graph", fileName = "CombatMoveGraph")]
public sealed class CombatMoveGraphAsset : ScriptableObject
{
    public Move startMove;
    [HideInInspector] public string networkContentHash;
    [HideInInspector] public MoveReactionDefaults reactionDefaults;
    public Move defaultGuardMove;
    public Move defaultHitMove;
    [SerializeField, HideInInspector] private bool reactionsMigrated;
    private void OnEnable()
    {
        if (reactionsMigrated) return;
        if (reactionDefaults != null)
        {
            defaultGuardMove = reactionDefaults.guardMove;
            defaultHitMove = reactionDefaults.hitMove;
        }
        reactionsMigrated = true;
    }
    public Move ResolveReaction(Move move, bool guard)
    {
        if (move == null) return null;
        var direct = guard ? move.ResolvedGuardMove : move.ResolvedHitMove;
        if (direct != null) return direct;
        var common = guard ? defaultGuardMove : defaultHitMove;
        return common != null && nodes.Exists(n => n.move == common) ? common : null;
    }
    public Move ResolveIdleMove()
    {
        foreach (var node in nodes)
            if (node.move != null && node.move.IsIdle) return node.move;
        return null;
    }
    public List<CombatMoveGraphNode> nodes = new();
    public Vector2 pan = new(30, 30);
    public float zoom = 1f;
    public GameObject previewCharacter;
    public string motionRootPath = "Model/skeleton/Pelvis";
    public float previewYaw = 90, previewSpeed = 1, previewZoom = 2.2f, previewHeight = 1, startupSeconds = .1f;
}
