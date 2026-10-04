using System;
using System.Reflection;
using Scripts;
using UnityEngine;

public static class CombatBalanceValidation
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    private static T Read<T>(object target, string name) => (T)target.GetType().GetProperty(name, Fields).GetValue(target);
    private static object Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Fields | BindingFlags.Static).Invoke(target, args);
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); }

    public static void Run()
    {
        var root = new GameObject("Combat balance validation");
        var left = new GameObject("Left");
        var right = new GameObject("Right");
        var attacking = new GameObject("Attacking Move");
        var passive = new GameObject("Passive Move");
        var reactionObject = new GameObject("Reaction Move");
        var cameraObject = new GameObject("Combat camera validation");
        try
        {
            var manager = root.AddComponent<ActorManager>();
            var aController = left.AddComponent<ActorActionController>();
            var bController = right.AddComponent<ActorActionController>();
            var a = left.AddComponent<Actor>();
            var b = right.AddComponent<Actor>();
            Set(b, "actorType", ActorType.Enemy);
            Set(b, "moveStartDelay", 0f);
            Set(a, "actionController", aController); Set(b, "actionController", bController);
            typeof(ActorActionController).GetMethod("Initialize", Fields).Invoke(aController, new object[] { a });
            typeof(ActorActionController).GetMethod("Initialize", Fields).Invoke(bController, new object[] { b });
            manager.actorA = a; manager.actorB = b;

            var attack = attacking.AddComponent<Move>();
            Set(attack, "category", MoveCategory.Attack);
            Set(attack, "damageBase", 8f); Set(attack, "damagePerPower", 2f);
            Set(attack, "stanceDamageBase", 14f); Set(attack, "stanceDamagePerPower", 4f);
            Set(attack, "impactPowerBase", 8f); Set(attack, "impactPowerPerForce", 2f);
            var idle = passive.AddComponent<Move>();
            var reaction = reactionObject.AddComponent<Move>();
            Set(reaction, "category", MoveCategory.HitReaction);
            Set(reaction, "guardable", true); // Category must block guard even if an old prefab still enables it.
            Set(reaction, "stanceUsageBase", 0f);
            var hitboxCollider = attacking.AddComponent<BoxCollider2D>();
            var hitbox = attacking.AddComponent<Hitbox>();
            Set(hitbox, "hitboxCollider", hitboxCollider);

            var infoType = typeof(ActorManager).GetNestedType("ExchangeInfo", BindingFlags.NonPublic);
            var exchange = Activator.CreateInstance(infoType);
            infoType.GetField("result").SetValue(exchange, ExchangeResult.AHitsB);
            infoType.GetField("hitboxA").SetValue(exchange, hitbox);
            Set(aController, "_current", new MoveRuntime(attack, 1));
            Set(bController, "_current", new MoveRuntime(idle, 0));
            Set(aController, "_hasCurrent", true); Set(bController, "_hasCurrent", true);
            typeof(ActorManager).GetMethod("ApplyExchange", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, new[] { exchange });
            Check(Read<int>(b, "Hp") == 90, "Direct hit HP damage changed unexpectedly");
            Check(Read<int>(b, "Stance") == 82, "Direct hit did not apply its 18 posture damage");
            Set(idle, "hitMove", reaction);
            Set(idle, "guardMove", reaction);
            var enqueue = typeof(ActorActionController).GetMethod("Enqueue", Fields);
            enqueue.Invoke(bController, new object[] { idle });
            var onHit = typeof(Move).GetMethod("OnHit", Fields);
            Check((Move)onHit.Invoke(idle, new object[] { b, null }) == reaction, "Hit must select the reaction");
            Check(Read<int>(bController, "QueueCount") == 0, "Hit must not queue a duplicate reaction");
            enqueue.Invoke(bController, new object[] { idle });
            var onGuard = typeof(Move).GetMethod("OnGuard", Fields);
            Check((Move)onGuard.Invoke(idle, new object[] { b, null }) == reaction, "Guard must select the reaction");
            Check(Read<int>(bController, "QueueCount") == 0, "Guard must not queue duplicate reactions");

            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 2.7f; camera.aspect = 2f;
            camera.transform.position = new Vector3(0f, 1.25f, -10f);
            Set(manager, "combatCamera", camera);
            Set(manager, "cameraHeightOffset", 1.25f);
            Set(manager, "cameraBaseOrthographicSize", 2.7f);
            left.transform.position = new Vector3(-8f, 0f);
            right.transform.position = new Vector3(8f, 2f);
            typeof(ActorManager).GetMethod("LateUpdate", Fields).Invoke(manager, null);
            Check(Mathf.Abs(camera.transform.position.x) < .001f &&
                  Mathf.Abs(camera.transform.position.y - 2.25f) < .001f &&
                  Mathf.Abs(camera.orthographicSize - 4.5f) < .001f,
                "Camera must center and fit both fighters after knockback");

            var visual = right.AddComponent<ActorVisualController>();
            Set(b, "visualController", visual);
            Set(bController, "_current", new MoveRuntime(idle, 0));
            Set(bController, "_currentSourceMove", idle);
            Set(bController, "_hasCurrent", true);
            var context = new CombatContext { user = a, target = b, manager = manager };
            Call(bController, "Interrupt", MoveEventType.Hit, InterruptReason.Hit, context);
            Check(Read<Move>(bController, "NetworkSourceMove") == reaction && Read<int>(bController, "QueueCount") == 1,
                "Hit must play a reaction now and lock exactly one following reaction");
            Check(!Read<bool>(b, "CanGuard"), "Hit reaction must not guard even with guardable enabled");
            Call(bController, "Tick", .2f);
            Call(bController, "Tick", 1f);
            Check(!Read<bool>(bController, "IsMoveRunning"), "First hit reaction did not finish");
            Check((bool)Call(bController, "TryStartNextMove", null, context) &&
                  Read<Move>(bController, "NetworkSourceMove") == reaction &&
                  Read<int>(bController, "QueueCount") == 0, "The locked second hit reaction did not play next");

            Set(bController, "_current", new MoveRuntime(idle, 0));
            Set(bController, "_currentSourceMove", idle);
            Set(bController, "_hasCurrent", true);
            Call(bController, "Interrupt", MoveEventType.Guard, InterruptReason.Guard, context);
            Check(Read<Move>(bController, "NetworkSourceMove") == reaction && Read<int>(bController, "QueueCount") == 1,
                "Guard must play a reaction now and lock exactly one following reaction");

            Set(b, "stance", 5);
            Set(bController, "_current", new MoveRuntime(idle, 0));
            Set(bController, "_currentSourceMove", idle);
            Set(bController, "_hasCurrent", true);
            Call(manager, "ApplyStanceDamageAndReaction", b, 5, (MoveEventType?)MoveEventType.Guard, context);
            Check(Read<int>(b, "Stance") == 0 &&
                  Read<Move>(bController, "NetworkSourceMove") == reaction &&
                  Read<int>(bController, "QueueCount") == 2 && !Read<bool>(b, "CanGuard"),
                "Guard stance break must switch to hit and lock two following hit reactions");
            Call(bController, "Tick", 1f);
            Check(Read<int>(b, "Stance") == 5, "Hit reaction must recover half the normal 10 stance");
            Check((bool)Call(bController, "TryStartNextMove", null, context), "First locked hit follow-up missing");
            Call(bController, "Tick", 1f);
            Check(Read<int>(b, "Stance") == 10 && Read<int>(bController, "QueueCount") == 1,
                "First hit follow-up must also recover half and leave one lock");
            Check((bool)Call(bController, "TryStartNextMove", null, context), "Second locked hit follow-up missing");
            Call(bController, "Tick", 1f);
            Check(Read<int>(b, "Stance") == 15 && Read<int>(bController, "QueueCount") == 0,
                "Second hit follow-up must finish with half recovery and unlock actions");

            Set(b, "stance", 5);
            Set(bController, "_current", new MoveRuntime(idle, 0));
            Set(bController, "_currentSourceMove", idle);
            Set(bController, "_hasCurrent", true);
            Call(manager, "ApplyExchange", exchange);
            Check(Read<int>(b, "Stance") == 0 &&
                  Read<Move>(bController, "NetworkSourceMove") == reaction &&
                  Read<int>(bController, "QueueCount") == 2,
                "A direct hit that breaks stance must play hit now and reserve two more hit actions");
            Debug.Log("COMBAT_BALANCE_VALIDATION_PASS");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(passive);
            UnityEngine.Object.DestroyImmediate(reactionObject);
            UnityEngine.Object.DestroyImmediate(attacking);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(right);
            UnityEngine.Object.DestroyImmediate(left);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
