using System;
using System.Reflection;
using Scripts;
using UnityEngine;

public static class MoveMovementValidation
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Flags).SetValue(obj, value);
    private static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Flags).Invoke(obj, args);
    private static void Near(float actual, float expected, string reason)
    {
        if (Mathf.Abs(actual - expected) > .0001f) throw new Exception(reason + $": {actual} != {expected}");
    }

    public static void Run()
    {
        var a = new GameObject("Movement test actor");
        var b = new GameObject("Movement test target");
        var m = new GameObject("Movement test Move");
        try
        {
            var actor = a.AddComponent<Actor>();
            var target = b.AddComponent<Actor>();
            actor.StartGettingPlan();
            actor.ForceUpdate(4f);
            if (!actor.GettingPlan || actor.GettingPlanFinished)
                throw new Exception("Planning timeout must hold the finished pose instead of queuing Idle");
            Call(actor, "CancelPlanning");
            // Exercise simulation positions without deferred Rigidbody2D.MovePosition.
            Set(actor, "body", null); Set(target, "body", null);
            Near((float)Call(actor, "AdjustHorizontalMovement", 1f, 1f), 1, "Neutral input must preserve motion");
            Call(actor, "SetDirectionalInput", 1);
            Near((float)Call(actor, "AdjustHorizontalMovement", 1f, 1f), 1.4f, "Minimum change adds to baseline");
            Near((float)Call(actor, "AdjustHorizontalMovement", -1f, 1f), -.6f, "Opposing input reduces motion without reversing it");
            Near((float)Call(actor, "AdjustHorizontalMovement", 10f, 1f), 12f, "Unclamped 20 percent change");
            Near((float)Call(actor, "AdjustHorizontalMovement", 100f, 1f), 102f, "Maximum clamps change, not total speed");
            Call(actor, "SetDirectionalInput", -1);
            Near((float)Call(actor, "AdjustHorizontalMovement", 100f, 1f), 98f, "Maximum subtraction keeps baseline");
            Near((float)Call(actor, "AdjustHorizontalMovement", .01f, 1f), 0f, "Opposing input cannot reverse slow motion");
            Near((float)Call(actor, "AdjustHorizontalMovement", 0f, 1f), 0f, "Zero motion stays zero");
            Call(actor, "SetDirectionalInput", 0);
            var chase = typeof(ActorManager).GetMethod("MoveTowardRange", BindingFlags.NonPublic | BindingFlags.Static);
            b.transform.position = new Vector3(5, 0);
            chase.Invoke(null, new object[] { actor, target, 1f, 100f, 1f });
            Near(a.transform.position.x, 4, "Chase must stop before the target");
            chase.Invoke(null, new object[] { actor, target, 1f, 100f, 1f });
            Near(a.transform.position.x, 4, "Chase must hold inside range");
            b.transform.position = new Vector3(-5, 0);
            chase.Invoke(null, new object[] { actor, target, 1f, 2f, .5f });
            Near(a.transform.position.x, 3, "Chase must follow a target on the left at configured speed");
            Call(actor, "SetDirectionalInput", -1);
            chase.Invoke(null, new object[] { actor, target, 1f, 2f, .5f });
            Near(a.transform.position.x, 1.8f, "Left input must speed up leftward chase");
            Call(actor, "SetDirectionalInput", 0);

            var move = m.AddComponent<Move>();
            Set(move, "impactPowerBase", 8f); Set(move, "impactPowerPerForce", 2f);
            Near((float)Call(move, "GetImpactPower", 1), 10, "Base plus force must determine impact power");
            Near((float)Call(move, "GetImpactPower", 3), 14, "Impact power must scale with actual force");
            var distance = typeof(ActorManager).GetMethod("CalculateKnockbackDistance", BindingFlags.NonPublic | BindingFlags.Static);
            Near((float)distance.Invoke(null, new object[] { 10f, 10f }), .6f, "Equal power clash distance");
            Near((float)distance.Invoke(null, new object[] { 14f, 8f }), 1.02f, "Strong attack must push farther");
            Near((float)distance.Invoke(null, new object[] { 8f, 14f }), .3f, "Weak attack must push less");
            Near((float)distance.Invoke(null, new object[] { 0f, 14f }), 0f, "Passive move must not push");

            var controller = a.GetComponent<ActorActionController>() ?? a.AddComponent<ActorActionController>();
            Set(controller, "_owner", actor);
            Set(controller, "_hasCurrent", true);
            Set(move, "duration", 1f);
            Set(move, "movementMode", MovementMode.CurveXY);
            Set(move, "movementPhase", MovementPhase.ActiveOnly);
            Set(move, "movementX", AnimationCurve.Linear(0, 0, 1, -1));
            Set(controller, "_current", new MoveRuntime(move, 1));
            Set(controller, "_moveStartPosition", Vector2.zero);
            a.transform.position = Vector3.zero;
            Call(actor, "ResetAndApplyKnockbackDistance", Vector2.left, .4f, .4f);
            Call(controller, "Tick", .1f);
            Near(a.transform.position.x, -.075f, "Curve and distance-based recoil must add together");
            Call(controller, "Tick", .1f);
            Near(a.transform.position.x, -.1f, "Curve must retain previous recoil displacement");
            Call(actor, "SetDirectionalInput", 1);
            Call(actor, "ResetAndApplyKnockbackDistance", Vector2.left, .4f, .4f);
            Call(controller, "Tick", .1f);
            Near(a.transform.position.x, -.095f, "Right input must boost rightward curve and resist leftward recoil");
            Call(actor, "SetDirectionalInput", 0);
            a.transform.position = Vector3.zero;
            Call(actor, "ResetAndApplyKnockbackDistance", Vector2.right, .9f, .33f);
            for (int i = 0; i < 4; i++) Call(actor, "ApplyRecoilFromActionController", .1f, true);
            Near(a.transform.position.x, .9f, "Neutral input must travel the full computed knockback distance");

            // The same authored curve must follow the actor's facing in either direction.
            Set(actor, "_recoilVelocity", Vector2.zero);
            Set(controller, "_current", new MoveRuntime(move, 1));
            Set(controller, "_lastCurveSample", Vector2.zero);
            Set(controller, "_moveStartFacingSign", -1);
            Set(controller, "_moveStartupRemaining", 0f);
            a.transform.position = Vector3.zero;
            Call(controller, "Tick", .1f);
            Near(a.transform.position.x, -.1f, "A left-facing actor must mirror the same authored curve");
            Set(controller, "_moveStartFacingSign", 1);

            // A Move can contain a vertical arc without changing the actor's
            // standing height for every later action.
            Set(move, "movementY", AnimationCurve.Linear(0f, 0f, 1f, .7f));
            Set(move, "duration", .1f);
            Set(controller, "_current", new MoveRuntime(move, 1));
            Set(controller, "_lastCurveSample", Vector2.zero);
            Set(controller, "_moveGroundY", 0f);
            Set(controller, "_hasCurrent", true);
            a.transform.position = Vector3.zero;
            Call(controller, "Tick", .05f);
            Near(a.transform.position.y, .35f, "Authored Y arc must be visible during the Move");
            Call(controller, "Tick", .05f);
            Near(a.transform.position.y, 0f, "Finished Y arc must return to standing height");
            if ((bool)typeof(Actor).GetProperty("IsMoveRunning", Flags).GetValue(actor))
                throw new Exception("Finished Move must stop accepting movement input");

            Set(actor, "actionController", controller);
            var targetController = b.GetComponent<ActorActionController>() ?? b.AddComponent<ActorActionController>();
            Set(target, "actionController", targetController);
            Set(targetController, "_owner", target);
            var targetMove = b.AddComponent<Move>();
            Set(targetController, "_current", new MoveRuntime(targetMove, 1));
            var ownBox = a.AddComponent<BoxCollider2D>();
            var targetBox = b.AddComponent<BoxCollider2D>();
            ownBox.size = targetBox.size = Vector2.one;
            var ownWeapon = a.AddComponent<Hitbox>();
            var targetWeapon = b.AddComponent<Hitbox>();
            Set(ownWeapon, "hitboxCollider", ownBox); Set(targetWeapon, "hitboxCollider", targetBox);
            foreach (bool ownIsWeapon in new[] { false, true })
            foreach (bool targetIsWeapon in new[] { false, true })
            {
                Set(move, "bodyCollider", ownIsWeapon ? null : ownBox);
                Set(targetMove, "bodyCollider", targetIsWeapon ? null : targetBox);
                Set(move, "weaponHitboxes", ownIsWeapon ? new System.Collections.Generic.List<Hitbox> { ownWeapon } : new System.Collections.Generic.List<Hitbox>());
                Set(targetMove, "weaponHitboxes", targetIsWeapon ? new System.Collections.Generic.List<Hitbox> { targetWeapon } : new System.Collections.Generic.List<Hitbox>());
                controller.GetType().GetProperty("ChaseStopped", Flags).SetValue(controller, false);
                a.transform.position = Vector3.zero; b.transform.position = new Vector3(5, 0);
                chase.Invoke(null, new object[] { actor, target, 0f, 100f, 1f });
                Near(a.transform.position.x, 4, $"Swept contact {ownIsWeapon}/{targetIsWeapon}");
                b.transform.position = new Vector3(10, 0);
                chase.Invoke(null, new object[] { actor, target, 0f, 100f, 1f });
                Near(a.transform.position.x, 4, "Contact must permanently stop this action's chase");
            }
            Set(move, "allowManualMovement", true);
            Set(move, "manualMovementSpeed", 1f);
            Set(move, "movementMode", MovementMode.None);
            Set(move, "bodyCollider", ownBox);
            Set(targetMove, "bodyCollider", targetBox);
            Set(move, "weaponHitboxes", new System.Collections.Generic.List<Hitbox>());
            Set(targetMove, "weaponHitboxes", new System.Collections.Generic.List<Hitbox>());
            Set(actor, "_recoilVelocity", Vector2.zero);
            Set(controller, "_hasCurrent", false); // Last visual remains while waiting for the next planned action.
            a.transform.position = Vector3.zero; b.transform.position = new Vector3(5, 0);
            var manual = typeof(ActorManager).GetMethod("ApplyMovement", BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(Actor), typeof(Actor), typeof(float) }, null);
            Call(actor, "SetDirectionalInput", 1);
            manual.Invoke(null, new object[] { actor, target, .1f });
            Near(a.transform.position.x, 0f, "Waiting must ignore directional input");
            Set(controller, "_hasCurrent", true);
            manual.Invoke(null, new object[] { actor, target, .1f });
            Near(a.transform.position.x, .1f, "Idle input moves at its configured speed");
            Call(actor, "SetDirectionalInput", -1);
            manual.Invoke(null, new object[] { actor, target, .1f });
            Near(a.transform.position.x, 0f, "Direction changes during the same idle action");
            b.transform.position = new Vector3(1.1f, 0);
            Call(actor, "SetDirectionalInput", 1);
            manual.Invoke(null, new object[] { actor, target, .1f });
            Near(a.transform.position.x, .1f, "Idle movement stops at body contact");
            Call(actor, "SetDirectionalInput", -1);
            manual.Invoke(null, new object[] { actor, target, .1f });
            Near(a.transform.position.x, 0f, "Idle movement can retreat after contact");
            Call(actor, "SetDirectionalInput", 0);
            var rigidbody = a.AddComponent<Rigidbody2D>();
            rigidbody.bodyType = RigidbodyType2D.Kinematic;
            Set(actor, "body", rigidbody);
            controller.GetType().GetProperty("ChaseStopped", Flags).SetValue(controller, false);
            rigidbody.position = Vector2.zero; a.transform.position = Vector3.zero;
            b.transform.position = new Vector3(5, 0);
            chase.Invoke(null, new object[] { actor, target, 0f, 100f, 1f });
            Near(rigidbody.position.x, 4, "Chase must commit physics position before the other fighter's sweep");
            Near(ownBox.bounds.max.x, targetBox.bounds.min.x, "Contact bounds must be current in the same simulation step");
            Call(actor, "MoveTo", new Vector2(2f, 0f));
            Near(rigidbody.position.x, 2f, "Curve movement must update the Rigidbody2D immediately");
            Near(a.transform.position.x, 2f, "Curve movement must update the visible character immediately");
            Debug.Log("MOVE_MOVEMENT_VALIDATION_PASS: chase, active directional movement, waiting input blocked, vertical arc reset, collision and retreat, recoil plus curve, immediate physics position");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(a);
            UnityEngine.Object.DestroyImmediate(b);
            UnityEngine.Object.DestroyImmediate(m);
        }
    }
}
