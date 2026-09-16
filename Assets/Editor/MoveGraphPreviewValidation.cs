using System;
using System.Reflection;
using Scripts;
using UnityEditor;
using UnityEngine;

internal static class MoveGraphPreviewValidation
{
    public static void Run()
    {
        const string folder = "Assets/__GraphPreviewCheck";
        MoveHitboxWindow editor = null;
        try
        {
            if (AssetDatabase.IsValidFolder(folder)) throw new InvalidOperationException("Test folder already exists");
            AssetDatabase.CreateFolder("Assets", "__GraphPreviewCheck");
            var a = MoveAssetCreation.CreateAtPath(folder + "/A.prefab");
            var b = MoveAssetCreation.CreateAtPath(folder + "/B.prefab");
            var hit = MoveAssetCreation.CreateAtPath(folder + "/Hit.prefab");
            var clip = new AnimationClip();
            clip.SetCurve("", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 1, 0));
            AssetDatabase.CreateAsset(clip, folder + "/Clip.anim");
            var so = new SerializedObject(a);
            so.FindProperty("characterAnimation").objectReferenceValue = clip;
            so.FindProperty("movementMode").enumValueIndex = (int)MovementMode.CurveXY;
            so.FindProperty("movementPhase").enumValueIndex = (int)MovementPhase.StartupAndActive;
            so.FindProperty("movementX").animationCurveValue = AnimationCurve.Linear(0, 0, 1, 2);
            so.ApplyModifiedPropertiesWithoutUndo();
            MoveGraphWindow.Connect(a, b, "after");
            MoveGraphWindow.Connect(a, hit, "hitMove");
            MoveGraphWindow.Connect(a, hit, "guardMove");
            MoveGraphWindow.Connect(b, a, "after");
            editor = ScriptableObject.CreateInstance<MoveHitboxWindow>();
            Move observed = null;
            editor.ActiveMoveChanged = current => observed = current;
            editor.SelectGraphMove(a);
            Set(editor, "playing", true);
            editor.AdvancePlayback(2);
            Require(editor.CurrentMove == b && observed == b, "Single actual edge advances and updates graph selection");
            Require(Vector2.Distance(Origin(editor), new Vector2(2, 0)) < .001f, "Normal edge uses source endpoint");
            editor.AdvancePlayback(2);
            Require(editor.CurrentMove == a, "Cycles advance one node at a time");
            editor.SelectGraphMove(b); editor.SelectGraphMove(a);
            Set(editor, "time", .5f);
            editor.FollowEdge(hit, "hitMove");
            Require(editor.CurrentMove == hit && Vector2.Distance(Origin(editor), new Vector2(1, 0)) < .001f, "Hit branch preserves interrupted position");
            editor.SelectGraphMove(a); Set(editor, "time", .25f);
            editor.FollowEdge(hit, "guardMove");
            Require(Vector2.Distance(Origin(editor), new Vector2(.5f, 0)) < .001f, "Guard branch preserves current position");
            MoveGraphWindow.Connect(a, hit, "after");
            editor.SelectGraphMove(a); Set(editor, "playing", true); editor.AdvancePlayback(2);
            Require(editor.CurrentMove == a && !(bool)Get(editor, "playing") && (bool)Get(editor, "waitingForBranch"), "Multiple branches pause for explicit choice");
            editor.FollowEdge(b, "after");
            Require(editor.CurrentMove == b, "Chosen existing branch is followed");
            var previous = editor.CurrentMove; editor.FollowEdge(hit, "after");
            Require(editor.CurrentMove == previous, "Unconnected target is rejected");
            Require(new SerializedObject(a).FindProperty("after").arraySize == 2, "Preview leaves combat graph intact");
            Require(MoveHitboxWindow.FindPredecessors(b).Contains(a), "Incoming candidates found from actual prefab references");
            Require(editor.PlayPredecessor(a, b), "Predecessor pair starts");
            Set(editor, "previewSpeed", .25f);
            editor.AdvancePreviewTime(.4f);
            Require(Mathf.Abs((float)Get(editor, "time") - .25f) < .001f, "Quarter speed uses wall time without changing clip duration");
            editor.AdvancePlayback(2);
            Require(editor.CurrentMove == b && Vector2.Distance(Origin(editor), new Vector2(2, 0)) < .001f, "Chosen successor overrides multiple candidates and accumulates position");
            editor.AdvancePlayback(2);
            Require(editor.CurrentMove == b && !(bool)Get(editor, "playing"), "Pair stops after target despite outgoing edges");
            Require(editor.PlayPredecessor(a, b) && Origin(editor) == Vector2.zero, "Pair replay resets position");
            Require(!editor.PlayPredecessor(b, hit), "Unconnected predecessor rejected");
            Require(new SerializedObject(a).FindProperty("after").arraySize == 2, "Pair preview does not rewrite graph");
            Debug.Log("MOVE_PREDECESSOR_SPEED_PASS discovery, slow playback, explicit pair, accumulated position, stop, replay, no graph mutation");
            Debug.Log("MOVE_GRAPH_PREVIEW_PASS actual edges, host selection, endpoint, hit/guard interruption, branch choice, cycles, no graph mutation");
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally
        {
            if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
            AssetDatabase.DeleteAsset(folder);
        }
    }
    private static Vector2 Origin(object obj) => (Vector2)Get(obj, "graphOrigin");
    private static object Get(object obj, string field) => obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj);
    private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value);
    private static void Require(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
}
