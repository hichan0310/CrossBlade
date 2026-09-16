using System;
using System.Collections.Generic;
using System.Reflection;
using Scripts;
using UnityEditor;
using UnityEngine;

internal static class MoveHitboxValidation
{
    public static void Run()
    {
        const string path = "Assets/__BlankMoveValidation.prefab";
        MoveHitboxWindow window = null;
        GameObject runtime = null, target = null;
        try
        {
            var move = MoveAssetCreation.CreateAtPath(path);
            var so = new SerializedObject(move);
            Require(so.FindProperty("weaponHitboxes").arraySize == 0 && so.FindProperty("characterAnimation").objectReferenceValue == null, "Blank Move has no inherited animation or hitboxes");
            Require(so.FindProperty("after").arraySize == 0 && so.FindProperty("guardMove").objectReferenceValue == null && so.FindProperty("hitMove").objectReferenceValue == null, "Blank Move has no branches");
            window = ScriptableObject.CreateInstance<MoveHitboxWindow>();
            Invoke(window, "SetMove", move);
            Invoke(window, "AddBox");
            move = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Move>();
            so = new SerializedObject(move);
            Require(so.FindProperty("weaponHitboxes").arraySize == 1, "Added hitbox registered exactly once");
            var hit = move.GetComponentInChildren<Hitbox>();
            var hs = new SerializedObject(hit);
            hs.FindProperty("activeStart").floatValue = .25f;
            hs.FindProperty("activeEnd").floatValue = .75f;
            hs.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(move.gameObject);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            move = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Move>();
            hit = move.GetComponentInChildren<Hitbox>();
            Require(!hit.IsActiveAt(.24f) && hit.IsActiveAt(.25f) && hit.IsActiveAt(.5f) && hit.IsActiveAt(.75f) && !hit.IsActiveAt(.76f), "Frame window persists and includes endpoints");
            var bounds = MoveHitboxWindow.BoxBounds(move.transform, hit.GetComponent<BoxCollider2D>(), false);
            var mirrored = MoveHitboxWindow.BoxBounds(move.transform, hit.GetComponent<BoxCollider2D>(), true);
            Require(Mathf.Abs(bounds.center.x + mirrored.center.x) < .0001f && bounds.size == mirrored.size, "Left facing mirrors the combat box");
            runtime = UnityEngine.Object.Instantiate(move.gameObject);
            var runtimeHit = runtime.GetComponentInChildren<Hitbox>();
            target = new GameObject("Hitbox validation target");
            var body = target.AddComponent<BoxCollider2D>(); body.offset = new Vector2(.8f, 1f);
            Physics2D.SyncTransforms();
            var touch = typeof(ActorManager).GetMethod("TryGetWeaponBodyTouch", BindingFlags.Static | BindingFlags.NonPublic);
            bool Contact(float progress)
            {
                var args = new object[] { new List<Hitbox> { runtimeHit }, body, progress, null };
                return (bool)touch.Invoke(null, args);
            }
            Require(!Contact(.1f) && Contact(.5f) && !Contact(.9f), "Runtime body collision respects time window");
            runtimeHit.GetComponent<BoxCollider2D>().enabled = false;
            Require(!Contact(.5f), "Consumed hitbox stays disabled");
            Invoke(window, "SetMove", move);
            typeof(MoveHitboxWindow).GetField("selected", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, hit);
            Invoke(window, "RemoveBox");
            move = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Move>();
            Require(move.GetComponentsInChildren<Hitbox>().Length == 0 && new SerializedObject(move).FindProperty("weaponHitboxes").arraySize == 0, "Removing box clears combat reference");
            Debug.Log("MOVE_HITBOX_VALIDATION_PASS blank, add, save/reimport, time window, mirror, collision, consumed, remove");
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally
        {
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
            if (runtime != null) UnityEngine.Object.DestroyImmediate(runtime);
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
            AssetDatabase.DeleteAsset(path);
        }
    }

    public static void RenderCheck()
    {
        const string path = "Assets/__HitboxRenderValidation.prefab";
        MoveHitboxWindow window = null;
        try
        {
            var move = MoveAssetCreation.CreateAtPath(path);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/PrototypeGenerated/EldenRing/a025_034000.anim");
            Require(clip != null, "Real greatsword clip available");
            var so = new SerializedObject(move);
            so.FindProperty("characterAnimation").objectReferenceValue = clip;
            so.FindProperty("movementMode").enumValueIndex = (int)MovementMode.CurveXY;
            so.FindProperty("movementPhase").enumValueIndex = (int)MovementPhase.StartupAndActive;
            so.FindProperty("movementX").animationCurveValue = AnimationCurve.Linear(0, 0, 1, 1.5f);
            so.FindProperty("movementY").animationCurveValue = new AnimationCurve(new Keyframe(0, 0), new Keyframe(.5f, .5f), new Keyframe(1, 0));
            so.ApplyModifiedPropertiesWithoutUndo();
            window = ScriptableObject.CreateInstance<MoveHitboxWindow>();
            Invoke(window, "SetMove", move);
            Color32[] previous = null;
            for (int i = 0; i < 2; i++)
            {
                typeof(MoveHitboxWindow).GetField("time", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, clip.length * (i == 0 ? .1f : .6f));
                var rendered = window.RenderModel(900, 600);
                Require(rendered != null, "Hitbox preview renders");
                var old = RenderTexture.active;
                var image = new Texture2D(900, 600, TextureFormat.RGBA32, false);
                try
                {
                    RenderTexture.active = rendered;
                    image.ReadPixels(new Rect(0, 0, 900, 600), 0, 0); image.Apply();
                    var pixels = image.GetPixels32();
                    int visible = 0, changed = 0;
                    for (int p = 0; p < pixels.Length; p++)
                    {
                        if (pixels[p].a > 20 && (pixels[p].r > 25 || pixels[p].g > 25 || pixels[p].b > 25)) visible++;
                        if (previous != null && !pixels[p].Equals(previous[p])) changed++;
                    }
                    Require(visible > 1000, "Character is visible");
                    if (previous != null) Require(changed > 1000, "Scrubbing changes actual rendered pose");
                    previous = pixels;
                    System.IO.File.WriteAllBytes("/tmp/hitbox_preview_" + i + ".png", image.EncodeToPNG());
                }
                finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(image); }
            }
            Debug.Log("MOVE_HITBOX_RENDER_PASS visible greatsword model and distinct scrubbed poses");
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally
        {
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
            AssetDatabase.DeleteAsset(path);
        }
    }

    private static void Invoke(object source, string method, params object[] args) => source.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source, args);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
