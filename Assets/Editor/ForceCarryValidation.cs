using System;
using System.Reflection;
using Scripts;
using UnityEngine;

public static class ForceCarryValidation
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    private static T Read<T>(object target, string name) => (T)target.GetType().GetProperty(name, Flags).GetValue(target);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }

    public static void Run()
    {
        var own = new GameObject("Charge validation actor");
        var other = new GameObject("Charge validation target");
        var readyObject = new GameObject("Charge validation ready");
        var attackObject = new GameObject("Charge validation attack");
        var idleObject = new GameObject("Charge validation idle");
        var graph = ScriptableObject.CreateInstance<CombatMoveGraphAsset>();
        try
        {
            var visual = own.AddComponent<ActorVisualController>();
            var controller = own.AddComponent<ActorActionController>();
            var actor = own.AddComponent<Actor>();
            var target = other.AddComponent<Actor>();
            Set(actor, "visualController", visual);
            Set(actor, "actionController", controller);
            Call(controller, "Initialize", actor);
            Set(actor, "actorType", ActorType.Player);
            var context = new CombatContext { user = actor, target = target };
            var ready = readyObject.AddComponent<Move>();
            Set(ready, "duration", .3f);
            Set(ready, "canCharge", true);
            Set(ready, "acceptsCarriedForce", true);
            Set(ready, "chargeStanceCost", 0);
            Set(ready, "stanceUsageBase", 13f);
            var attack = attackObject.AddComponent<Move>();
            Set(attack, "category", MoveCategory.Attack);
            Set(attack, "acceptsCarriedForce", true);
            Set(attack, "duration", .3f);
            Set(attack, "stanceUsageBase", 17f);
            Set(attack, "stanceUsagePerPower", 5f);
            var idle = idleObject.AddComponent<Move>();
            Set(idle, "stanceUsageBase", 0f);
            Set(idle, "stanceRecovery", 5);
            Set(idle, "isIdle", true);
            graph.nodes.Add(new CombatMoveGraphNode { move = idle });
            Set(actor, "combatMoveGraph", graph);

            Call(actor, "Enqueue", ready);
            Check((bool)Call(actor, "TryStartNextMove", (Func<Actor, Move, int>)((a, m) => 0), context), "Ready did not start");
            Check((bool)Call(controller, "TryChargeCurrent"), "First charge failed");
            Check((bool)Call(controller, "TryChargeCurrent"), "Second charge failed");
            Check(Read<int>(actor, "Stance") == 87 && Read<MoveRuntime>(actor, "Current").force == 2, "Preparation cost or pre-choice charge incorrect");
            Call(actor, "Enqueue", attack);
            Check(!(bool)Call(controller, "TryChargeCurrent"), "Charge remained available after the next action was queued");
            Call(actor, "Tick", .1f);
            Call(actor, "Tick", .31f);
            Check(Read<int>(actor, "Stance") == 97, "Base posture recovery missing");
            Check((bool)Call(actor, "TryStartNextMove", (Func<Actor, Move, int>)((a, m) => 5), context), "Attack did not start");
            Check(Read<MoveRuntime>(actor, "Current").force == 3, "Charged force was lost or legacy force bypassed charge");
            Check(Read<int>(actor, "Stance") == 65, "Attack must spend base 17 + 5 per used force exactly once");
            Check(!(bool)Call(controller, "TryChargeCurrent"), "Attack charged outside preparation");
            Call(actor, "Tick", .1f);
            Call(actor, "Tick", .31f);
            Check(Read<int>(actor, "Stance") == 75, "Attack completion did not restore 10 posture");
            Call(actor, "Enqueue", ready);
            Check((bool)Call(actor, "TryStartNextMove", (Func<Actor, Move, int>)((a, m) => 0), context), "Second ready did not start");
            Check((bool)Call(controller, "TryChargeCurrent"), "Second ready did not charge");
            Call(actor, "Tick", .1f);
            Call(actor, "Tick", .31f);
            Call(actor, "Enqueue", attack);
            Check((bool)Call(actor, "TryStartNextMove", (Func<Actor, Move, int>)((a, m) => 1), context), "Second attack did not start");
            Check(Read<MoveRuntime>(actor, "Current").force == 1, "Empty queue must discard carry out/in");
            Check(Read<int>(actor, "Stance") == 50, "Uncharged attack cost must be 17 + 5");
            Call(actor, "Tick", .1f);
            Call(actor, "Tick", .31f);
            Call(actor, "Enqueue", idle);
            Check((bool)Call(actor, "TryStartNextMove", (Func<Actor, Move, int>)((a, m) => 0), context), "Idle did not start");
            Call(actor, "Tick", .1f);
            Call(actor, "Tick", .31f);
            Check(Read<int>(actor, "Stance") == 75, "Idle must recover 10 + 5 posture");
            Set(actor, "planDecisionSeconds", .1f);
            actor.StartGettingPlan();
            actor.ForceUpdate(.2f);
            Check(Read<Move>(actor, "PlannedMove") == idle, "Plan timeout must choose the graph's checked idle Move");
            Call(actor, "CancelPlanning");
            Call(actor, "Enqueue", ready);
            Call(actor, "Enqueue", attack);
            Check(Read<Move>(actor, "PlanningMove") == attack, "Further choices must follow the queue tail");
            actor.StartGettingPlan();
            actor.ForceUpdate(3.1f);
            Check(Read<Move>(actor, "PlannedMove") == null, "Nonempty queue must not append idle on timeout");
            Call(actor, "CancelPlanning");
            Call(actor, "ClearQueuedMovesForInterrupt");
            Set(actor, "stance", 30);
            Check((bool)Call(actor, "Enqueue", ready), "First queue entry failed");
            for (int i = 1; i < 5; i++)
                Check((bool)Call(actor, "Enqueue", idle), "Queue entry within capacity failed");
            Check(Read<int>(actor, "QueueCount") == 5 && Read<int>(actor, "NextQueueRecoveryBonus") == 25,
                "Queue size or next recovery bonus is wrong");
            Check(!(bool)Call(actor, "Enqueue", idle) && Read<int>(actor, "QueueCount") == 5,
                "Queue accepted a sixth pending action");
            Check((bool)Call(actor, "TryStartNextMove", (Func<Actor, Move, int>)((a, m) => 0), context), "Queued ready did not start");
            Call(actor, "Tick", .1f);
            Call(actor, "Tick", .31f);
            Check(Read<int>(actor, "Stance") == 27, "First queue entry must recover only base posture");
            Check((bool)Call(actor, "TryStartNextMove", (Func<Actor, Move, int>)((a, m) => 0), context), "Queued idle did not start");
            Call(actor, "Tick", .1f);
            Call(actor, "Tick", .31f);
            Check(Read<int>(actor, "Stance") == 47, "Second entry must retain its enqueue-time +5 recovery bonus");
            Debug.Log("FORCE_CARRY_VALIDATION_PASS");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(attackObject);
            UnityEngine.Object.DestroyImmediate(idleObject);
            UnityEngine.Object.DestroyImmediate(readyObject);
            UnityEngine.Object.DestroyImmediate(other);
            UnityEngine.Object.DestroyImmediate(own);
            UnityEngine.Object.DestroyImmediate(graph);
        }
    }
}
