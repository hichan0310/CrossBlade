using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MotionPrototype
{
    internal static class MotionEditingValidation
    {
        private static readonly List<string> Checks = new();
        private const string SequencePath = "Assets/PrototypeGenerated/__MotionSequenceValidation.asset";
        private const string GraphPath = "Assets/PrototypeGenerated/__MotionGraphValidation.asset";
        private const string ClipPath = "Assets/PrototypeGenerated/__MotionBakeValidation.anim";

        public static void Run()
        {
            try
            {
                Checks.Clear();
                var catalogPath = Path.Combine(ProjectPaths.RepositoryRoot, "catalog", "elden_ring_browser_catalog.json");
                var catalog = JsonUtility.FromJson<IndexedMotionCatalog>(File.ReadAllText(catalogPath));
                Require(catalog.entries.Length == 16512, "All 16,512 unique indexed motions are visible to Unity");
                Require(catalog.entries.Select(item => item.id).Distinct().Count() == catalog.entries.Length, "Library IDs are unique");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EldenRingMotionImporter.PrefabPath);
                var first = AssetDatabase.LoadAssetAtPath<AnimationClip>(EldenRingMotionImporter.GeneratedRoot + "/a023_030150.anim");
                var second = first;
                Require(prefab != null && first != null, "Real ER preview character and prepared source motion are available in CrossBlade");
                var sequence = ScriptableObject.CreateInstance<MotionSequenceAsset>();
                sequence.name = "Validation Sequence";
                sequence.previewCharacter = prefab;
                sequence.outputFps = 30;
                sequence.segments.Add(new MotionSegment { label = "cut-fast", clip = first, firstFrame = 3, lastFrame = 18, speed = 2f });
                sequence.segments.Add(new MotionSegment { label = "cut-slow", clip = second, firstFrame = 0, lastFrame = 12, speed = .5f, blendFrames = 3 });
                AssetDatabase.CreateAsset(sequence, SequencePath);
                var expected = (15f / first.frameRate) / 2f + (12f / second.frameRate) / .5f - 3f / 30f;
                Require(Mathf.Abs(MotionSequenceCore.Duration(sequence) - expected) < 1e-5f, "Cuts, speed and transition overlap determine sequence duration");

                var instance = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    var hand = instance.GetComponentsInChildren<Transform>(true).Single(item => item.name == "R_Hand");
                    MotionSequenceCore.Sample(sequence, instance, .1f);
                    var pose = hand.position;
                    MotionSequenceCore.Sample(sequence, instance, .01f);
                    MotionSequenceCore.Sample(sequence, instance, .1f);
                    Require(Vector3.Distance(pose, hand.position) < 1e-5f, "Sequence scrubbing is deterministic out of order");
                    MotionSequenceCore.Sample(sequence, instance, MotionSequenceCore.Layout(sequence)[1].start + .05f);
                Require(Vector3.Distance(pose, hand.position) > .001f, "Second cut contributes during its blended segment");
                }
                finally { UnityEngine.Object.DestroyImmediate(instance); }

                var baked = MotionSequenceCore.Bake(sequence, ClipPath);
                Require(Mathf.Abs(baked.length - MotionSequenceCore.Duration(sequence)) <= 1f / sequence.outputFps + 1e-5f,
                    "Baked .anim preserves composed duration");
                Require(AnimationUtility.GetCurveBindings(baked).Length > 100, "Baked .anim contains transform animation curves");
                Require(baked.frameRate == sequence.outputFps, "Baked .anim uses selected output FPS");

                var graph = ScriptableObject.CreateInstance<MotionGraphAsset>();
                var one = new MotionGraphNode { label = "Attack A", sequence = sequence, position = Vector2.zero };
                var two = new MotionGraphNode { label = "Attack B", sequence = sequence, position = new Vector2(200, 0) };
                graph.nodes.Add(one); graph.nodes.Add(two);
                graph.edges.Add(new MotionGraphEdge { from = one.id, to = two.id, trigger = "Next" });
                AssetDatabase.CreateAsset(graph, GraphPath);
                AssetDatabase.SaveAssets();
                var reloaded = AssetDatabase.LoadAssetAtPath<MotionGraphAsset>(GraphPath);
                Require(reloaded.nodes.Count == 2 && reloaded.edges.Count == 1, "Motion graph nodes and named transition serialize");
                Require(reloaded.edges[0].trigger == "Next" && reloaded.edges[0].from == reloaded.nodes[0].id,
                    "Graph edge retains endpoints and trigger");

                var output = Path.Combine(ProjectPaths.RepositoryRoot, "catalog", "motion_editing_validation.json");
                File.WriteAllText(output, JsonUtility.ToJson(new Result { passed = true, checks = Checks.ToArray() }, true));
                Debug.Log($"MOTION_EDITING_VALIDATION_PASSED {Checks.Count} checks");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                throw;
            }
            finally
            {
                AssetDatabase.DeleteAsset(ClipPath);
                AssetDatabase.DeleteAsset(GraphPath);
                AssetDatabase.DeleteAsset(SequencePath);
            }
        }

        public static void ValidateOnDemand()
        {
            const string id = "a023_030200";
            var clip = EldenRingLibraryWindow.Prepare(id);
            if (clip == null || clip.name != id || clip.length <= 0 || clip.frameRate <= 0)
                throw new InvalidOperationException("On-demand HKX preparation did not create a playable Unity clip.");
            if (!AnimationUtility.GetCurveBindings(clip).Any(binding => binding.path == "" &&
                    binding.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal)))
                throw new InvalidOperationException("Prepared clip did not retain its root transform curves.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EldenRingMotionImporter.PrefabPath);
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var hand = instance.GetComponentsInChildren<Transform>(true).Single(item => item.name == "R_Hand");
                clip.SampleAnimation(instance, 0f);
                var first = hand.position;
                clip.SampleAnimation(instance, clip.length * .5f);
                if (Vector3.Distance(first, hand.position) < .001f)
                    throw new InvalidOperationException("Prepared clip did not animate the preview character.");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
            Debug.Log($"MOTION_ON_DEMAND_VALIDATION_PASSED {id} {clip.length:0.000}s {clip.frameRate:0.##}fps");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Checks.Add(message);
        }

        [Serializable] private sealed class Result { public bool passed; public string[] checks; }
    }
}
