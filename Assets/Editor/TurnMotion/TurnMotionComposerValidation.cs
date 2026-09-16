using System;
using System.Collections.Generic;
using Scripts;
using Scripts.TurnMotion;
using UnityEditor;
using UnityEngine;

namespace CrossBlade.EditorTools
{
    public static class TurnMotionComposerValidation
    {
        private const string Root = "Assets/__TurnMotionValidation";

        public static void Validate()
        {
            try
            {
                TurnMotionBuilder.EnsureAssetFolder(Root);
                AnimationClip source = CreateSourceClip();
                GameObject characterPrefab = CreateCharacterPrefab();
                GameObject vfxPrefab = CreateVfxPrefab();
                var points = new List<TurnMotionPoint>
                {
                    new TurnMotionPoint { label = "Windup", sourceTime = 0f },
                    new TurnMotionPoint { label = "Contact", sourceTime = 0.2f },
                    new TurnMotionPoint { label = "Recovery", sourceTime = 1f },
                };
                List<TurnMotionSegment> segments = TurnMotionBuilder.BuildSegments(source, points, 0.6f);
                Require(segments.Count == 2, "Expected two equal-time segments.");
                Require(Mathf.Abs(segments[0].TurnDuration - 0.3f) < 0.0001f, "Segment 1 is not 0.3 seconds.");
                Require(Mathf.Abs(segments[1].TurnDuration - 0.3f) < 0.0001f, "Segment 2 is not 0.3 seconds.");
                Require(Mathf.Abs(segments[0].PlaybackSpeed - (0.2f / 0.3f)) < 0.0001f,
                    "Windup playback speed is wrong.");
                Require(Mathf.Abs(segments[1].PlaybackSpeed - (0.8f / 0.3f)) < 0.0001f,
                    "Recovery playback speed is wrong.");

                AnimationClip generated = TurnMotionBuilder.GenerateClip(
                    source, points, 0.6f, 30f, Root + "/ValidationAction.anim");
                EditorCurveBinding binding = EditorCurveBinding.FloatCurve("Bone", typeof(Transform), "m_LocalPosition.x");
                AnimationCurve curve = AnimationUtility.GetEditorCurve(generated, binding);
                Require(curve != null, "Generated transform curve is missing.");
                Require(Mathf.Abs(curve.Evaluate(0.3f) - 2f) < 0.001f,
                    "First selected source point was not mapped to the equal segment boundary.");
                Require(Mathf.Abs(curve.Evaluate(0.45f) - 6f) < 0.02f,
                    "Second segment time warp produced the wrong source sample.");

                TurnMotionDefinition definition = TurnMotionBuilder.SaveDefinition(
                    Root + "/ValidationAction.asset", source, 0.6f, 30f, points, generated, null);
                GameObject actionPrefab = TurnMotionBuilder.CreateActionPrefab(
                    Root, "ValidationAction", characterPrefab, vfxPrefab, definition, generated);
                TurnMotionBuilder.SaveDefinition(
                    Root + "/ValidationAction.asset", source, 0.6f, 30f, points, generated, actionPrefab);
                Require(actionPrefab.GetComponent<TurnMotionMove>() != null, "Generated prefab is not a Move subclass.");
                TurnMotionAction action = actionPrefab.GetComponent<TurnMotionAction>();
                Require(action != null, "Generated action component is missing.");
                Require(action.Parts.Count == 2, "Generated action does not reference two Part prefabs.");
                Require(action.Parts[0] != null && action.Parts[0].VfxAnchor != null,
                    "Part VFXAnchor is missing from the action prefab.");
                Require(action.Parts[0].transform.parent != null && action.Parts[0].transform.parent.name == "Parts",
                    "Part prefab instance is not nested below the action Parts root.");

                GameObject instance = UnityEngine.Object.Instantiate(actionPrefab);
                try
                {
                    TurnMotionAction runtime = instance.GetComponent<TurnMotionAction>();
                    runtime.SetTime(0.3f, false);
                    Transform bone = instance.transform.Find("Character/Bone");
                    Require(bone != null && Mathf.Abs(bone.localPosition.x - 2f) < 0.001f,
                        "Generated action prefab does not sample the time-warped clip correctly.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }

                Debug.Log("TURN_MOTION_VALIDATION: passed=True, sourcePoints=3, segments=2, " +
                          "turnDuration=0.6, segmentDuration=0.3, moveSubclass=True, partPrefabs=2, " +
                          "nestedParts=True, vfxAnchor=True");
            }
            finally
            {
                AssetDatabase.DeleteAsset(Root);
                AssetDatabase.Refresh();
            }
        }

        private static AnimationClip CreateSourceClip()
        {
            var clip = new AnimationClip { name = "ValidationSource", frameRate = 30f };
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.2f, 2f),
                new Keyframe(1f, 10f));
            AnimationUtility.SetEditorCurve(
                clip, EditorCurveBinding.FloatCurve("Bone", typeof(Transform), "m_LocalPosition.x"), curve);
            AssetDatabase.CreateAsset(clip, Root + "/ValidationSource.anim");
            return clip;
        }

        private static GameObject CreateCharacterPrefab()
        {
            var root = new GameObject("ValidationCharacter");
            var bone = new GameObject("Bone");
            bone.transform.SetParent(root.transform, false);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/ValidationCharacter.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject CreateVfxPrefab()
        {
            var root = new GameObject("ValidationSlashVFX");
            root.AddComponent<ParticleSystem>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/ValidationSlashVFX.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
