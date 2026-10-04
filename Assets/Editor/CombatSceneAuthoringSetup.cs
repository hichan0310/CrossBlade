using System;
using System.Linq;
using Scripts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CombatSceneAuthoringSetup
{
    private const string RootName = "Scene Move (edit-only)";

    [MenuItem("Tools/CrossBlade/Build Scene Move Authoring")]
    public static void RefreshScenes()
    {
        foreach (string path in new[] { "Assets/Scenes/CombatScene.unity", "Assets/Scenes/OnlineCombat.unity" })
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) continue;
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var actors = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Actor>(true)).ToArray();
            foreach (var actor in actors) EnsureForActor(actor);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("COMBAT_SCENE_AUTHORING_READY " + path);
        }
    }

    internal static SceneMoveAuthoring EnsureForActor(Actor actor)
    {
        if (actor == null) throw new ArgumentNullException(nameof(actor));
        var visual = actor.GetComponent<ActorVisualController>();
        if (visual == null) throw new InvalidOperationException(actor.name + " has no visual controller");
        var visualData = new SerializedObject(visual);
        var mount = visualData.FindProperty("moveMount").objectReferenceValue as Transform;
        if (mount == null) throw new InvalidOperationException(actor.name + " has no MoveMount");
        MoveAttachmentAxes.ApplyTo(mount);

        var facing = new SerializedObject(actor).FindProperty("facingRoot").objectReferenceValue as Transform;
        if (facing != null)
        {
            Vector3 scale = facing.localScale;
            scale.x = Mathf.Abs(scale.x) * (actor.name == "Enemy" ? -1f : 1f);
            facing.localScale = scale;
        }

        var existing = mount.Find(RootName);
        GameObject root;
        if (existing == null)
        {
            root = new GameObject(RootName);
            root.transform.SetParent(mount, false);
        }
        else root = existing.gameObject;
        root.tag = "EditorOnly";
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        var authoring = root.GetComponent<SceneMoveAuthoring>() ?? root.AddComponent<SceneMoveAuthoring>();

        var graph = new SerializedObject(actor).FindProperty("combatMoveGraph").objectReferenceValue as CombatMoveGraphAsset;
        if (graph == null) return authoring;
        Move preferred = authoring.SourceMove;
        if (preferred == null || !graph.nodes.Any(node => node.move == preferred))
            preferred = graph.nodes.Select(node => node.move)
                .FirstOrDefault(move => move != null && move.GetComponentsInChildren<Hitbox>(true).Length > 0)
                ?? graph.startMove;
        SetMove(authoring, preferred);
        return authoring;
    }

    internal static void SetMove(SceneMoveAuthoring authoring, Move source)
    {
        if (authoring == null || source == null || !EditorUtility.IsPersistent(source)) return;
        if (authoring.SourceMove == source && authoring.SceneMove != null &&
            PrefabUtility.GetCorrespondingObjectFromSource(authoring.SceneMove) == source) return;

        if (authoring.SceneMove != null)
        {
            ApplyEdits(authoring);
            UnityEngine.Object.DestroyImmediate(authoring.SceneMove.gameObject);
        }
        var instance = PrefabUtility.InstantiatePrefab(source.gameObject, authoring.gameObject.scene) as GameObject;
        if (instance == null) throw new InvalidOperationException("Could not create scene Move prefab instance: " + source.name);
        instance.transform.SetParent(authoring.transform, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var data = new SerializedObject(authoring);
        data.FindProperty("sourceMove").objectReferenceValue = source;
        data.FindProperty("sceneMove").objectReferenceValue = instance.GetComponent<Move>();
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(authoring.gameObject.scene);
    }

    internal static void ApplyEdits(SceneMoveAuthoring authoring)
    {
        var instance = authoring != null ? authoring.SceneMove : null;
        if (instance == null || !PrefabUtility.IsPartOfPrefabInstance(instance)) return;
        if (!PrefabUtility.HasPrefabInstanceAnyOverrides(instance.gameObject, false)) return;
        PrefabUtility.ApplyPrefabInstance(instance.gameObject, InteractionMode.AutomatedAction);
        AssetDatabase.SaveAssets();
    }
}
