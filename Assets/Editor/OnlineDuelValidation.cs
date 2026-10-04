using System;
using System.Linq;
using System.Reflection;
using Scripts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class OnlineDuelValidation
{
    // Run in a disposable project; no UGS calls or saved user asset changes.
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/OnlineCombat.unity");
        var battle = UnityEngine.Object.FindFirstObjectByType<ActorManager>();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var actor in new[] { battle.actorA, battle.actorB }) typeof(Actor).GetMethod("Awake", flags).Invoke(actor, null);
        var duel = new GameObject("ValidationDuel").AddComponent<OnlineDuel>();
        var type = typeof(OnlineDuel);
        type.GetMethod("Awake", flags).Invoke(duel, null);
        type.GetField("running", flags).SetValue(duel, true);
        var moves = (Move[])type.GetField("moves", flags).GetValue(duel);
        var source = moves.First(m => MoveGraphWindow.Targets(m, "after").Any(t => t != null));
        var actorB = battle.actorB;
        typeof(Actor).GetMethod("ClearQueuedMovesForInterrupt", flags).Invoke(actorB, null);
        var controller = actorB.GetComponent<ActorActionController>();
        typeof(ActorActionController).GetField("_current", flags).SetValue(controller, new MoveRuntime(source, 1));
        var plan = type.GetMethod("Plan", flags);
        plan.Invoke(duel, new object[] { actorB });
        var windows = (Array)type.GetField("windows", flags).GetValue(duel);
        var window = windows.GetValue(1); var wt = window.GetType();
        int revision = (int)wt.GetField("revision").GetValue(window);
        int candidate = ((int[])wt.GetField("choices").GetValue(window))[0];
        var choiceType = type.GetNestedType("Choice", BindingFlags.NonPublic);
        bool Accept(int rev, int move, int force)
        {
            var choice = Activator.CreateInstance(choiceType);
            choiceType.GetField("window").SetValue(choice, rev);
            choiceType.GetField("move").SetValue(choice, move);
            choiceType.GetField("force").SetValue(choice, force);
            return (bool)type.GetMethod("Accept", flags).Invoke(duel, new[] { (object)1, choice });
        }
        void Check(bool result, string reason) { if (!result) throw new Exception(reason); }
        Check(!Accept(revision - 1, candidate, 1), "Stale window accepted");
        Check(!Accept(revision, -1, 1), "Invalid Move accepted");
        Check(!Accept(revision, candidate, 6), "Invalid force accepted");
        Check(!Accept(revision, candidate, 3), "Legacy force selection bypassed charging");
        Check(Accept(revision, candidate, 1), "Valid choice rejected");
        Check(!Accept(revision, candidate, 1), "Duplicate choice accepted");
        wt.GetField("open").SetValue(window, true);
        wt.GetField("deadline").SetValue(window, Time.realtimeSinceStartupAsDouble - 1);
        Check(!Accept(revision, candidate, 1), "Expired choice accepted");
        typeof(Actor).GetMethod("TryConsumePlannedMove", flags).Invoke(actorB, new object[] { null });
        Check((PlanQueryState)plan.Invoke(duel, new object[] { actorB }) == PlanQueryState.Ready,
            "Expired plan did not resolve");
        Check((Move)typeof(Actor).GetProperty("PlannedMove", flags).GetValue(actorB) == actorB.CombatMoveGraph.ResolveIdleMove(),
            "Plan timeout did not return to the checked idle Move");
        typeof(Actor).GetMethod("TryConsumePlannedMove", flags).Invoke(actorB, new object[] { null });
        typeof(Actor).GetMethod("Enqueue", flags).Invoke(actorB, new object[] { moves[candidate] });
        wt.GetField("open").SetValue(window, true);
        wt.GetField("sourceMove").SetValue(window, moves[candidate]);
        wt.GetField("deadline").SetValue(window, Time.realtimeSinceStartupAsDouble - 1);
        Check((PlanQueryState)plan.Invoke(duel, new object[] { actorB }) == PlanQueryState.Running,
            "Nonempty queue should continue without forced idle");
        Check((Move)typeof(Actor).GetProperty("PlannedMove", flags).GetValue(actorB) == null,
            "Nonempty queue appended idle on timeout");
        Debug.Log("ONLINE_INPUT_VALIDATION_PASS: stale, invalid, duplicate, idle timeout, nonempty queue");
        UnityEngine.Object.DestroyImmediate(duel.gameObject);
    }
}
