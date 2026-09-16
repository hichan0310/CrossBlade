using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MotionPrototype
{
    internal static class MotionAssetPersistenceValidation
    {
        public static void Run()
        {
            const string sequencePath = "Assets/__SequencePersistenceTest.asset";
            const string graphPath = "Assets/__GraphPersistenceTest.asset";
            try
            {
                var paths = Directory.GetFiles("Assets", "*.asset", SearchOption.AllDirectories)
                    .Where(path => File.ReadAllText(path).Contains("Assembly-CSharp-Editor:MotionPrototype:MotionSequenceAsset"))
                    .ToArray();
                foreach (var path in paths)
                {
                    var sequence = AssetDatabase.LoadAssetAtPath<MotionSequenceAsset>(path);
                    Require(sequence != null, "Load saved sequence: " + path);
                    Require(MonoScript.FromScriptableObject(sequence)?.GetClass() == typeof(MotionSequenceAsset), "Sequence script resolves");
                    Require(sequence.segments.Count > 0 && sequence.segments.All(segment => segment.clip != null), "Saved clips resolve: " + path);
                    Require(AssetDatabase.FindAssets("t:MotionSequenceAsset").Contains(AssetDatabase.AssetPathToGUID(path)), "Object picker indexes: " + path);
                }

                Require(!File.Exists(sequencePath) && !File.Exists(graphPath), "Test paths are unused");
                var saved = ScriptableObject.CreateInstance<MotionSequenceAsset>();
                saved.outputFps = 60;
                saved.segments.Add(new MotionSegment { firstFrame = 12, lastFrame = 24, speed = 1.5f });
                AssetDatabase.CreateAsset(saved, sequencePath);
                var graph = ScriptableObject.CreateInstance<MotionGraphAsset>();
                graph.nodes.Add(new MotionGraphNode { sequence = saved, label = "Persistence" });
                AssetDatabase.CreateAsset(graph, graphPath);
                AssetDatabase.SaveAssets();
                foreach (var path in new[] { sequencePath, graphPath })
                {
                    Require(!File.ReadAllText(path).Contains("m_Script: {fileID: 0}"), "New asset has script: " + path);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
                var loaded = AssetDatabase.LoadAssetAtPath<MotionSequenceAsset>(sequencePath);
                Require(loaded != null && loaded.outputFps == 60 && loaded.segments[0].firstFrame == 12 && loaded.segments[0].lastFrame == 24 && loaded.segments[0].speed == 1.5f, "Reimport preserves edits");
                Require(AssetDatabase.LoadAssetAtPath<MotionGraphAsset>(graphPath).nodes[0].sequence == loaded, "Graph sequence reference survives");
                Debug.Log("MOTION_ASSET_PERSISTENCE_PASS recovered=" + paths.Length + " picker, clips, new save, reimport, graph reference");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
            finally
            {
                AssetDatabase.DeleteAsset(graphPath);
                AssetDatabase.DeleteAsset(sequencePath);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
