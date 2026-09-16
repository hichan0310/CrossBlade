using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Scripts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CrossBlade.EditorTools
{
    [InitializeOnLoad]
    public static class DanjinIntegrationValidation
    {
        private const string Pending = "CrossBlade.DanjinValidation";
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly List<string> Checks = new List<string>();
        private static string Output => Path.GetFullPath("../../output/unity_integration");
        static DanjinIntegrationValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                    ValidatePlaying();
            };
        }
        public static void Run()
        {
            EditorSceneManager.OpenScene(DanjinIntegrationSetup.ScenePath);
            // Disable automatic stepping for this test session only; never save these changes.
            foreach (var manager in Object.FindObjectsByType<ActorManager>(FindObjectsSortMode.None))
                manager.enabled = false;
            Debug.Log("DANJIN_VALIDATION_ENTER_PLAY");
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }
        private static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Flags).Invoke(target, args);
        private static object Get(object target, string name) => target.GetType().GetProperty(name, Flags).GetValue(target);
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Checks.Add(message);
        }
        private static Transform Find(GameObject root, string name) =>
            root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
        private static void SetDuration(Move move, float duration) =>
            typeof(Move).GetField("duration", Flags).SetValue(move, duration);
        private static void ValidatePlaying()
        {
            try
            {
                Directory.CreateDirectory(Output);
                var actors = Object.FindObjectsByType<Actor>(FindObjectsSortMode.None);
                Actor actor = actors.Single(a => a.name == "Player");
                Actor enemy = actors.Single(a => a.name == "Enemy");
                foreach (var manager in Object.FindObjectsByType<ActorManager>(FindObjectsSortMode.None)) manager.enabled = false;
                var player = actor.GetComponentInChildren<CharacterAnimationPlayer>();
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DanjinIntegrationSetup.ClipPath);
                var attack = AssetDatabase.LoadAssetAtPath<GameObject>(DanjinIntegrationSetup.AttackPath).GetComponent<Move>();
                var context = new CombatContext { user = actor, target = enemy };
                var visual = actor.GetComponent<ActorVisualController>();
                var controller = actor.GetComponent<ActorActionController>();
                Require(player != null && clip != null, "Real persistent character and clip are wired");
                Require(Mathf.Abs(clip.length - 2.1f) < .001f, "Imported clip duration is 2.1 seconds");
                Require((AnimationClip)Get(attack, "CharacterAnimation") == clip, "Attack1_1 references the real imported clip");
                int characterId = player.gameObject.GetInstanceID();
                Transform sword = Find(player.AnimationRoot, "Existing_MagicSword_Preview");
                Vector3 swordPosition = sword.localPosition;
                Quaternion swordRotation = sword.localRotation;
                Require(!AnimationUtility.GetCurveBindings(clip).Any(b => b.path.Contains("Existing_MagicSword_Preview")),
                    "Sword correction remains a fixed prefab transform with no prop animation keys");
                Require(player.GetComponentsInChildren<Collider>(true).Length == 0 && player.GetComponentsInChildren<Collider2D>(true).Length == 0,
                    "Persistent visual adds no combat colliders");
                Require(player.GetComponentsInChildren<Animator>(true).All(a => !a.enabled && !a.applyRootMotion),
                    "No Animator clock or Animator root motion runs independently");

                Action start = () =>
                {
                    if ((bool)Get(actor, "IsMoveRunning")) Call(actor, "Interrupt", MoveEventType.Clash, InterruptReason.Clash, context);
                    Call(actor, "ClearQueuedMovesForInterrupt");
                    Call(actor, "Enqueue", attack);
                    Require((bool)Call(actor, "TryStartNextMove", new Func<Actor, Move, int>((a, m) => 1), context), "Attack starts through ActorActionController");
                };
                start();
                Move runtime = (Move)Get(visual, "CurrentMoveInstance");
                Require(runtime != attack && runtime.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0,
                    "Runtime Move is instantiated without cloning the character");
                Require(runtime.GetComponent<Collider2D>() != null && runtime.GetComponent<Collider2D>().enabled,
                    "Existing 2D body collider remains enabled");
                Require(!runtime.GetComponent<SpriteRenderer>().enabled, "Replaced root sprite is hidden");
                float delay = (float)Get(actor, "MoveStartDelay");
                Call(actor, "Tick", delay * .5f);
                Require(Mathf.Abs(player.SampleTime - (float)Get(actor, "MoveProgress") * clip.length) < 1e-5f,
                    "Animation matches gameplay progress during startup");
                Call(actor, "Tick", delay * .5f);
                Call(actor, "Tick", .15f);
                Require(Mathf.Abs(player.SampleTime - (float)Get(actor, "MoveProgress") * clip.length) < 1e-5f,
                    "Animation matches gameplay progress during active time");
                SetDuration(runtime, .9f);
                Call(actor, "Tick", 0f);
                Require(Mathf.Abs(player.SampleTime - (float)Get(actor, "MoveProgress") * clip.length) < 1e-5f,
                    "Changing Move duration automatically retimes the same clip");

                Transform hand = Find(player.AnimationRoot, "右手首");
                var poses = new List<Vector3>();
                Vector3 actorPosition = actor.transform.position;
                Transform rootMotion = Find(player.AnimationRoot, "全ての親");
                Vector3 lockedRoot = player.AnimationRoot.transform.InverseTransformPoint(rootMotion.position);
                foreach (float progress in new[] {0f, .25f, .5f, .75f, 1f})
                {
                    Call(player, "Evaluate", runtime, progress, 1);
                    poses.Add(hand.position - Find(player.AnimationRoot, "下半身").position);
                    Require(Mathf.Abs(player.SampleTime - progress * clip.length) < 1e-5f, "Explicit sample time " + progress);
                    Require(Vector3.Distance(sword.localPosition, swordPosition) < 1e-5f && Quaternion.Angle(sword.localRotation, swordRotation) < .01f,
                        "Sword attachment stays constant at progress " + progress);
                    Require(Mathf.Abs(player.AnimationRoot.transform.InverseTransformPoint(rootMotion.position).x - lockedRoot.x) < 1e-5f && Mathf.Abs(player.AnimationRoot.transform.InverseTransformPoint(rootMotion.position).z - lockedRoot.z) < 1e-5f,
                        "Visual horizontal root motion is held at progress " + progress);
                }
                Require(poses.Any(p => Vector3.Distance(p, poses[0]) > .1f), "Actual hand motion relative to pelvis is non-rigid");
                Require(Vector3.Distance(actor.transform.position, actorPosition) < 1e-5f, "Sampling does not move the 2D Actor");
                foreach (int sign in new[] {1, -1})
                {
                    Call(actor, "FaceTowards", (Vector2)actor.transform.position + Vector2.right * sign * 10);
                    Call(visual, "LateUpdate");
                    Require(Vector3.Dot(player.transform.forward, Vector3.right * sign) > .99f, "3D facing follows Actor sign " + sign);
                    Require(player.GetComponentsInChildren<Transform>(true).All(t => t.localToWorldMatrix.determinant > 0),
                        "Skinned hierarchy has no reflected world transform when facing " + sign);
                }
                foreach (MoveEventType trigger in new[] {MoveEventType.Hit, MoveEventType.Guard, MoveEventType.Clash})
                {
                    start();
                    Call(actor, "Tick", delay);
                    Call(actor, "Tick", .12f);
                    var reason = trigger == MoveEventType.Hit ? InterruptReason.Hit : trigger == MoveEventType.Guard ? InterruptReason.Guard : InterruptReason.Clash;
                    Call(actor, "Interrupt", trigger, reason, context);
                    Require(player.SampleTime == 0f, trigger + " interrupt immediately clears the attack pose");
                    Require(player.gameObject.GetInstanceID() == characterId, trigger + " interrupt reuses the same character");
                    if (trigger != MoveEventType.Clash)
                        Require((bool)Get(actor, "IsMoveRunning"), trigger + " still starts the existing reaction Move");
                }
                start();
                Call(actor, "Tick", delay);
                Call(actor, "Tick", .31f);
                Require(!(bool)Get(actor, "IsMoveRunning") && player.SampleTime == 0, "Completion returns immediately to default pose");
                Require(actor.GetComponentsInChildren<CharacterAnimationPlayer>(true).Length == 1, "Exactly one persistent character survives transitions");
                Require(enemy.GetComponentInChildren<CharacterAnimationPlayer>() == null, "Sprite-only enemy stays on the existing path");

                // Structural collision regression: the tested Attack already has no weapon hitboxes.
                // Do not invent them just to manufacture a successful damage test.
                Require(attack.GetComponentsInChildren<Hitbox>(true).Length == 0, "Pre-existing Attack1_1 has no weapon hitboxes (documented limitation)");
                var data = new Result { passed = true, checks = Checks.ToArray(), clip = clip.name, clipSeconds = clip.length,
                    characterCount = actor.GetComponentsInChildren<CharacterAnimationPlayer>(true).Length,
                    swordLocalRotation = swordRotation, skinBounds = player.GetComponentInChildren<SkinnedMeshRenderer>().bounds.size };
                File.WriteAllText(Path.Combine(Output, "playmode_validation.json"), JsonUtility.ToJson(data, true));
                Debug.Log("DANJIN_PLAYMODE_VALIDATION_PASSED " + Checks.Count + " checks");
                SessionState.SetBool(Pending, false);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(Output, "playmode_failure.txt"), e.ToString());
                Debug.LogException(e);
                SessionState.SetBool(Pending, false);
                EditorApplication.Exit(1);
            }
        }
        [Serializable] private class Result
        {
            public bool passed;
            public string[] checks;
            public string clip;
            public float clipSeconds;
            public int characterCount;
            public Quaternion swordLocalRotation;
            public Vector3 skinBounds;
        }
    }
}
