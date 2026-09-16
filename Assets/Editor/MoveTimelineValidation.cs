using System;
using System.Reflection;
using Scripts;
using UnityEditor;
using UnityEngine;

internal static class MoveTimelineValidation
{
    public static void RunAll()
    {
        MoveHitboxValidation.Run();
        Run();
    }

    public static void Run()
    {
        const string aPath = "Assets/__MoveXYA.prefab", bPath = "Assets/__MoveXYB.prefab", sequencePath = "Assets/__MoveXYSequence.asset";
        GameObject actorObject = null;
        try
        {
            var a = MoveAssetCreation.CreateAtPath(aPath);
            var b = MoveAssetCreation.CreateAtPath(bPath);
            Configure(a, .9f, 2, true);
            Configure(b, 1.9f, 3, false);
            Require(Vector2.Distance(a.EvaluateMovementOffset(.5f, 1), new Vector2(1, 1)) < .001f, "XY midpoint");
            Require(Vector2.Distance(a.EvaluateMovementOffset(.5f, -1), new Vector2(-1, 1)) < .001f, "Facing mirrors X only");
            var sequence = ScriptableObject.CreateInstance<MovePreviewSequence>();
            sequence.startupSeconds = .1f; sequence.moves.Add(a); sequence.moves.Add(null); sequence.moves.Add(b);
            AssetDatabase.CreateAsset(sequence, sequencePath); AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(sequencePath, ImportAssetOptions.ForceUpdate);
            sequence = AssetDatabase.LoadAssetAtPath<MovePreviewSequence>(sequencePath);
            Require(sequence != null && sequence.moves[0] == a && sequence.moves[2] == b, "Sequence survives save/reimport");
            Require(Mathf.Abs(MoveTimelineSampling.Length(sequence) - 3) < .001f, "Sequence includes startup delay");
            MoveTimelineSampling.Sample(sequence, 1, 1, out var selected, out var progress, out var origin);
            Require(selected == b && progress == 0 && origin == new Vector2(2, 0), "Boundary starts next Move at previous endpoint");
            MoveTimelineSampling.Sample(sequence, 2, 1, out selected, out progress, out origin);
            Require(selected == b && Mathf.Abs(progress - .5f) < .001f && origin == new Vector2(2, 0), "Second Move timing");
            Require(Vector2.Distance(origin + MoveTimelineSampling.Offset(b, progress, .1f, 1), new Vector2(3.5f, 0)) < .001f, "Offsets accumulate");
            MoveTimelineSampling.Sample(sequence, 3, -1, out selected, out progress, out origin);
            Require(Vector2.Distance(origin + MoveTimelineSampling.Offset(b, progress, .1f, -1), new Vector2(-5, 0)) < .001f, "Final endpoint and reverse facing");

            actorObject = new GameObject("Curve runtime validation");
            var actor = actorObject.AddComponent<Actor>();
            var controller = actorObject.AddComponent<ActorActionController>();
            var actorSettings = new SerializedObject(actor);
            actorSettings.FindProperty("moveStartDelay").floatValue = .1f; actorSettings.ApplyModifiedPropertiesWithoutUndo();
            Set(controller, "_owner", actor); Set(controller, "_hasCurrent", true);
            Set(controller, "_current", new MoveRuntime(a, 1) { elapsed = .4f });
            Set(controller, "_moveStartPosition", new Vector2(10, 2)); Set(controller, "_moveStartFacingSign", -1);
            Apply(controller, false);
            Require(Vector2.Distance(actorObject.transform.position, new Vector2(9, 3)) < .001f, "Runtime position matches preview at mid Move");
            Set(controller, "_current", new MoveRuntime(a, 1) { elapsed = .9f }); Apply(controller, false);
            Require(Vector2.Distance(actorObject.transform.position, new Vector2(8, 2)) < .001f, "Runtime reaches exact endpoint");
            var setting = new SerializedObject(a);
            setting.FindProperty("movementPhase").enumValueIndex = (int)MovementPhase.ActiveOnly; setting.ApplyModifiedPropertiesWithoutUndo();
            Set(controller, "_current", new MoveRuntime(a, 1) { elapsed = .45f }); Apply(controller, false);
            Require(Vector2.Distance(actorObject.transform.position, new Vector2(9, 3)) < .001f, "Active-only phase timing");
            setting.FindProperty("movementPhase").enumValueIndex = (int)MovementPhase.StartupOnly; setting.ApplyModifiedPropertiesWithoutUndo();
            Set(controller, "_moveStartupRemaining", .05f); Apply(controller, true);
            Require(Vector2.Distance(actorObject.transform.position, new Vector2(9, 3)) < .001f, "Startup-only phase timing");
            Debug.Log("MOVE_TIMELINE_VALIDATION_PASS persistence, boundaries, accumulation, facing, runtime midpoint/endpoint, phases");
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally
        {
            if (actorObject != null) UnityEngine.Object.DestroyImmediate(actorObject);
            AssetDatabase.DeleteAsset(sequencePath); AssetDatabase.DeleteAsset(aPath); AssetDatabase.DeleteAsset(bPath);
        }
    }

    private static void Configure(Move move, float duration, float x, bool jump)
    {
        var so = new SerializedObject(move);
        so.FindProperty("duration").floatValue = duration;
        so.FindProperty("movementMode").enumValueIndex = (int)MovementMode.CurveXY;
        so.FindProperty("movementPhase").enumValueIndex = (int)MovementPhase.StartupAndActive;
        so.FindProperty("movementX").animationCurveValue = AnimationCurve.Linear(0, 0, 1, x);
        so.FindProperty("movementY").animationCurveValue = jump ? new AnimationCurve(new Keyframe(0, 0), new Keyframe(.5f, 1), new Keyframe(1, 0)) : AnimationCurve.Linear(0, 0, 1, 0);
        so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SavePrefabAsset(move.gameObject);
    }
    private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value);
    private static void Apply(ActorActionController controller, bool startup) => typeof(ActorActionController).GetMethod("ApplyCurveMovement", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { startup });
    private static void Require(bool test, string message) { if (!test) throw new InvalidOperationException(message); }
}
