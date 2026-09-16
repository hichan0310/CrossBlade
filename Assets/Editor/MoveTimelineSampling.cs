using System.Reflection;
using Scripts;
using UnityEditor;
using UnityEngine;

internal static class MoveTimelineSampling
{
    private static readonly PropertyInfo DurationProperty = typeof(Move).GetProperty("Duration", BindingFlags.Instance | BindingFlags.NonPublic);
    internal static float Duration(Move move) => move == null ? 0 : Mathf.Max(.01f, (float)DurationProperty.GetValue(move));
    internal static float Length(MovePreviewSequence sequence)
    {
        float result = 0;
        foreach (var move in sequence.moves) if (move != null) result += Duration(move) + Mathf.Max(0, sequence.startupSeconds);
        return result;
    }

    internal static float PhaseProgress(Move move, float progress, float startup)
    {
        var so = new SerializedObject(move);
        var phase = (MovementPhase)so.FindProperty("movementPhase").enumValueIndex;
        float elapsed = Mathf.Clamp01(progress) * (Duration(move) + startup);
        return phase switch
        {
            MovementPhase.StartupOnly => startup > 0 ? Mathf.Clamp01(elapsed / startup) : 1,
            MovementPhase.ActiveOnly => Mathf.Clamp01((elapsed - startup) / Duration(move)),
            MovementPhase.StartupAndActive => Mathf.Clamp01(progress),
            _ => 0
        };
    }

    internal static Vector2 Offset(Move move, float progress, float startup, int facing)
    {
        if (move == null) return Vector2.zero;
        var so = new SerializedObject(move);
        var mode = (MovementMode)so.FindProperty("movementMode").enumValueIndex;
        float phase = PhaseProgress(move, progress, startup);
        if (mode == MovementMode.CurveXY) return move.EvaluateMovementOffset(phase, facing);
        if (mode == MovementMode.FixedDistanceForward) return new Vector2(so.FindProperty("fixedTravelDistance").floatValue * phase * facing, 0);
        if (mode == MovementMode.FixedSpeedForward)
        {
            var timing = (MovementPhase)so.FindProperty("movementPhase").enumValueIndex;
            float span = timing == MovementPhase.StartupOnly ? startup : timing == MovementPhase.ActiveOnly ? Duration(move) : timing == MovementPhase.StartupAndActive ? Duration(move) + startup : 0;
            return new Vector2(so.FindProperty("speed").floatValue * span * phase * facing, 0);
        }
        return Vector2.zero;
    }

    internal static void Sample(MovePreviewSequence sequence, float seconds, int facing, out Move selected, out float progress, out Vector2 origin)
    {
        selected = null; progress = 0; origin = Vector2.zero;
        float remaining = Mathf.Clamp(seconds, 0, Length(sequence));
        Move last = null;
        Vector2 lastOrigin = Vector2.zero;
        foreach (var move in sequence.moves)
        {
            if (move == null) continue;
            float span = Duration(move) + Mathf.Max(0, sequence.startupSeconds);
            if (remaining < span) { selected = move; progress = remaining / span; return; }
            last = move; lastOrigin = origin;
            origin += Offset(move, 1, sequence.startupSeconds, facing);
            remaining -= span;
        }
        selected = last; progress = 1; origin = lastOrigin;
    }
}
