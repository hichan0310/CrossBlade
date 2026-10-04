using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Scripts;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class OnlineDuelSetup : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) => RefreshContent();

    [InitializeOnLoadMethod]
    private static void BeforePlay()
    {
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.ExitingEditMode && SceneManager.GetActiveScene().name == "OnlineCombat") RefreshContent();
        };
    }

    [MenuItem("Tools/CrossBlade/Prepare Online Duel")]
    public static void Prepare()
    {
        RefreshContent();
        const string scene = "Assets/Scenes/OnlineCombat.unity";
        if (!File.Exists(scene) && !AssetDatabase.CopyAsset("Assets/Scenes/CombatScene.unity", scene))
            throw new InvalidOperationException("Could not create online scene");
        var loaded = SceneManager.GetSceneByPath(scene);
        bool wasLoaded = loaded.IsValid() && loaded.isLoaded;
        if (!wasLoaded) loaded = EditorSceneManager.OpenScene(scene, OpenSceneMode.Additive);
        try
        {
            var manager = loaded.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ActorManager>(true)).Single();
            manager.autoSimulate = false;
            EditorSceneManager.MarkSceneDirty(loaded);
            EditorSceneManager.SaveScene(loaded);
        }
        finally { if (!wasLoaded) EditorSceneManager.CloseScene(loaded, true); }
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == scene)) scenes.Add(new EditorBuildSettingsScene(scene, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("ONLINE_DUEL_PREPARED");
    }

    public static void RefreshContent()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:CombatMoveGraphAsset"))
        {
            var graph = AssetDatabase.LoadAssetAtPath<CombatMoveGraphAsset>(AssetDatabase.GUIDToAssetPath(guid));
            var moves = graph.nodes.Where(n => n.move != null).Select(n => n.move).Distinct().ToArray();
            if (graph.startMove == null || !moves.Contains(graph.startMove)) throw new BuildFailedException(graph.name + ": 시작 행동이 그래프에 없습니다.");
            int idleCount = moves.Count(move => new SerializedObject(move).FindProperty("isIdle").boolValue);
            if (idleCount != 1) throw new BuildFailedException(graph.name + ": 시간 초과 복귀용 Idle 동작을 정확히 하나 체크하세요.");
            var content = new StringBuilder("CrossBlade-protocol-1\n");
            foreach (var source in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                content.Append(File.ReadAllText(source));
            content.Append(guid).Append('|').Append(Array.IndexOf(moves, graph.startMove)).Append('|')
                .Append(Array.IndexOf(moves, graph.defaultGuardMove)).Append('|').Append(Array.IndexOf(moves, graph.defaultHitMove));
            foreach (var move in moves)
            {
                var path = AssetDatabase.GetAssetPath(move);
                content.Append('|').Append(AssetDatabase.AssetPathToGUID(path)).Append(':').Append(AssetDatabase.GetAssetDependencyHash(path));
                var data = new SerializedObject(move);
                foreach (var field in new[] { "after", "guardMove", "hitMove" })
                {
                    var property = data.FindProperty(field);
                    if (field == "after")
                    {
                        for (int i = 0; i < property.arraySize; i++) ValidateTarget(property.GetArrayElementAtIndex(i).objectReferenceValue as Move);
                    }
                    else ValidateTarget(property.objectReferenceValue as Move);
                }
                void ValidateTarget(Move target)
                { if (target != null && !moves.Contains(target)) throw new BuildFailedException(move.name + ": 그래프 밖으로 연결된 Move " + target.name); }
            }
            using var hash = SHA256.Create();
            string value = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(content.ToString()))).Replace("-", "").ToLowerInvariant();
            if (graph.networkContentHash == value) continue;
            graph.networkContentHash = value; EditorUtility.SetDirty(graph);
        }
        AssetDatabase.SaveAssets();
    }

    public static void BuildLinuxTest()
    {
        Prepare();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/OnlineCombat.unity" },
            locationPathName = "/tmp/crossblade-online-player/CrossBlade.x86_64",
            target = BuildTarget.StandaloneLinux64, options = BuildOptions.Development });
        if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Online test player build failed");
    }
}
