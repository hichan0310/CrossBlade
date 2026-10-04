using System;
using System.IO;
using System.Linq;
using Scripts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class CombatSceneVisualSetup
{
    [MenuItem("Tools/CrossBlade/Refresh Combat Scene Characters")]
    public static void Refresh()
    {
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit == null) throw new InvalidOperationException("URP Unlit shader is missing");
        var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/PrototypeGenerated/EldenRing/BodyPreview.mat");
        var weaponMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/PrototypeGenerated/EldenRing/WeaponPreview.mat");
        if (bodyMaterial == null || weaponMaterial == null) throw new InvalidOperationException("Mannequin materials are missing");
        bodyMaterial.shader = unlit;
        bodyMaterial.SetColor("_BaseColor", new Color(.65f, .68f, .74f));
        weaponMaterial.shader = unlit;
        weaponMaterial.SetColor("_BaseColor", new Color(.86f, .87f, .9f));
        EditorUtility.SetDirty(bodyMaterial);
        EditorUtility.SetDirty(weaponMaterial);
        const string mannequinPath = "Assets/PrototypeGenerated/EldenRing/ER_Base_Male_Animated.prefab";
        var mannequin = PrefabUtility.LoadPrefabContents(mannequinPath);
        try
        {
            foreach (var renderer in mannequin.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterial = renderer is SkinnedMeshRenderer ? bodyMaterial : weaponMaterial;
            PrefabUtility.SaveAsPrefabAsset(mannequin, mannequinPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(mannequin); }

        const string rendererPath = "Assets/Settings/CombatForwardRenderer.asset";
        var forward = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (forward == null)
        {
            forward = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(forward, rendererPath);
        }
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/UniversalRP.asset");
        var pipelineData = new SerializedObject(pipeline);
        var renderers = pipelineData.FindProperty("m_RendererDataList");
        if (renderers.arraySize < 2) renderers.arraySize = 2;
        renderers.GetArrayElementAtIndex(1).objectReferenceValue = forward;
        // Scene view cameras use the pipeline default rather than the combat
        // Main Camera's explicit renderer index.
        pipelineData.FindProperty("m_DefaultRendererIndex").intValue = 1;
        pipelineData.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pipeline);
        AssetDatabase.SaveAssets();

        foreach (string path in new[] { "Assets/Scenes/CombatScene.unity", "Assets/Scenes/OnlineCombat.unity" })
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) continue;
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var actors = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Actor>(true)).ToArray();
            if (actors.Length != 2) throw new InvalidOperationException(path + ": expected two actors, found " +
                actors.Length + " roots=" + string.Join(",", scene.GetRootGameObjects().Select(root => root.name)));
            foreach (var actor in actors)
            {
                var graph = new SerializedObject(actor).FindProperty("combatMoveGraph").objectReferenceValue as CombatMoveGraphAsset;
                if (graph == null || graph.previewCharacter == null) throw new InvalidOperationException(path + ": missing graph mannequin");

                var visual = actor.GetComponent<ActorVisualController>();
                var visualData = new SerializedObject(visual);
                var currentPlayer = visualData.FindProperty("characterPlayer").objectReferenceValue as CharacterAnimationPlayer;
                if (currentPlayer != null && currentPlayer.gameObject.name == "GraphCharacter" &&
                    currentPlayer.AnimationRoot != null &&
                    PrefabUtility.GetCorrespondingObjectFromSource(currentPlayer.AnimationRoot) == graph.previewCharacter)
                {
                    currentPlayer.transform.localRotation = Quaternion.Euler(0f,
                        graph.previewYaw + (actor.name == "Enemy" ? 180f : 0f), 0f);
                    var existingPlayerData = new SerializedObject(currentPlayer);
                    existingPlayerData.FindProperty("rightFacingYaw").floatValue = graph.previewYaw;
                    existingPlayerData.FindProperty("defaultPose").objectReferenceValue = graph.startMove != null
                        ? new SerializedObject(graph.startMove).FindProperty("characterAnimation").objectReferenceValue : null;
                    existingPlayerData.ApplyModifiedPropertiesWithoutUndo();
                    continue;
                }

                if (currentPlayer != null && currentPlayer.transform.IsChildOf(actor.transform))
                    UnityEngine.Object.DestroyImmediate(currentPlayer.gameObject);
                var oldCharacter = actor.transform.Find("Character3D");
                if (oldCharacter != null) UnityEngine.Object.DestroyImmediate(oldCharacter.gameObject);
                var oldGraphCharacter = actor.transform.Find("GraphCharacter");
                if (oldGraphCharacter != null) UnityEngine.Object.DestroyImmediate(oldGraphCharacter.gameObject);

                var facing = new GameObject("GraphCharacter");
                facing.transform.SetParent(actor.transform, false);
                facing.transform.localRotation = Quaternion.Euler(0, graph.previewYaw + (actor.name == "Enemy" ? 180f : 0f), 0);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(graph.previewCharacter, scene);
                model.transform.SetParent(facing.transform, false);
                model.transform.localPosition = Vector3.zero;
                var player = facing.AddComponent<CharacterAnimationPlayer>();
                var playerData = new SerializedObject(player);
                playerData.FindProperty("animationRoot").objectReferenceValue = model;
                playerData.FindProperty("defaultPose").objectReferenceValue = graph.startMove != null
                    ? new SerializedObject(graph.startMove).FindProperty("characterAnimation").objectReferenceValue : null;
                playerData.FindProperty("motionRoot").objectReferenceValue = string.IsNullOrEmpty(graph.motionRootPath)
                    ? null : model.transform.Find(graph.motionRootPath);
                playerData.FindProperty("rightFacingYaw").floatValue = graph.previewYaw;
                playerData.ApplyModifiedPropertiesWithoutUndo();
                visualData.Update();
                visualData.FindProperty("characterPlayer").objectReferenceValue = player;
                visualData.ApplyModifiedPropertiesWithoutUndo();
            }

            foreach (var actor in actors) CombatSceneAuthoringSetup.EnsureForActor(actor);

            var camera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c => c.CompareTag("MainCamera"));
            if (camera == null) throw new InvalidOperationException(path + ": missing main camera");
            var cameraData = new SerializedObject(camera.GetComponent<UniversalAdditionalCameraData>());
            cameraData.FindProperty("m_RendererIndex").intValue = 1;
            cameraData.ApplyModifiedPropertiesWithoutUndo();
            camera.orthographicSize = 2.7f;
            camera.transform.position = new Vector3(0f, 1.25f, -10f);
            camera.backgroundColor = new Color(.07f, .08f, .11f);

            var center = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(child => child.name == "CenterUI");
            if (center != null) center.gameObject.SetActive(false);

            if (!scene.GetRootGameObjects().Any(root => root.name == "Character Light"))
            {
                var lightObject = new GameObject("Character Light");
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                lightObject.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("COMBAT_SCENE_VISUALS_READY " + path + " actors=" + actors.Length);
        }
    }

    public static void Validate()
    {
        var graph = AssetDatabase.LoadAssetAtPath<CombatMoveGraphAsset>("Assets/UserCombatGraph.asset");
        if (graph == null || graph.startMove == null ||
            !new SerializedObject(graph.startMove).FindProperty("isIdle").boolValue)
            throw new InvalidOperationException("Combat graph must start in an idle move");
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/UniversalRP.asset");
        var pipelineData = new SerializedObject(pipeline);
        var renderersProperty = pipelineData.FindProperty("m_RendererDataList");
        if (renderersProperty.arraySize < 2 ||
            renderersProperty.GetArrayElementAtIndex(1).objectReferenceValue is not UniversalRendererData)
            throw new InvalidOperationException("Combat forward renderer is not registered");
        if (pipelineData.FindProperty("m_DefaultRendererIndex").intValue != 1)
            throw new InvalidOperationException("Scene view must default to the 3D combat renderer");
        foreach (string path in new[] { "Assets/Scenes/CombatScene.unity", "Assets/Scenes/OnlineCombat.unity" })
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var actors = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Actor>(true)).ToArray();
            var camera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .Single(c => c.CompareTag("MainCamera"));
            var cameraData = new SerializedObject(camera.GetComponent<UniversalAdditionalCameraData>());
            if (cameraData.FindProperty("m_RendererIndex").intValue != 1)
                throw new InvalidOperationException(path + ": camera does not use combat forward renderer");
            if (actors.Length != 2) throw new InvalidOperationException(path + ": missing actors");
            var center = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(child => child.name == "CenterUI");
            if (center == null || center.gameObject.activeSelf)
                throw new InvalidOperationException(path + ": result overlay is visible before battle ends");
            foreach (var actor in actors)
            {
                var graphRoot = actor.transform.Find("GraphCharacter");
                if (graphRoot == null || actor.transform.Find("Character3D") != null)
                    throw new InvalidOperationException(path + ": old or missing character on " + actor.name);
                var model = graphRoot.GetComponentInChildren<CharacterAnimationPlayer>(true);
                var renderers = graphRoot.GetComponentsInChildren<Renderer>(true);
                if (model == null || model.AnimationRoot == null || renderers.Length == 0)
                    throw new InvalidOperationException(path + ": missing mannequin renderer on " + actor.name);
                var authoring = actor.GetComponentInChildren<SceneMoveAuthoring>(true);
                if (authoring == null || authoring.SceneMove == null || !PrefabUtility.IsPartOfPrefabInstance(authoring.SceneMove))
                    throw new InvalidOperationException(path + ": missing editable scene Move prefab instance on " + actor.name);
                foreach (var renderer in renderers)
                    if (renderer.sharedMaterial == null ||
                        renderer.sharedMaterial.shader.name != "Universal Render Pipeline/Unlit")
                        throw new InvalidOperationException(path + ": mannequin has an incompatible material on " + actor.name);
                typeof(CharacterAnimationPlayer).GetMethod("Evaluate", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).Invoke(model, new object[] { null, 0f, actor.name == "Enemy" ? -1 : 1 });
                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                var point = camera.WorldToViewportPoint(bounds.center);
                if (point.z <= 0 || point.x < .05f || point.x > .95f || point.y < .05f || point.y > .95f)
                    throw new InvalidOperationException(path + ": mannequin outside camera: " + actor.name + " " + point);
                Debug.Log("COMBAT_SCENE_CHARACTER_VISIBLE " + path + " " + actor.name + " renderers=" + renderers.Length +
                          " bounds=" + bounds.size + " viewport=" + point);
                ValidateHeldPose(actor, model);
            }
        }
    }

    private static void ValidateHeldPose(Actor actor, CharacterAnimationPlayer player)
    {
        var attackPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NewMove1.prefab");
        var attack = attackPrefab != null ? attackPrefab.GetComponent<Move>() : null;
        if (attack == null) throw new InvalidOperationException("Missing attack Move for held-pose validation");
        var evaluate = typeof(CharacterAnimationPlayer).GetMethod("Evaluate", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        var refresh = typeof(ActorVisualController).GetMethod("RefreshMoveVisualState",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        int facing = actor.name == "Enemy" ? -1 : 1;
        evaluate.Invoke(player, new object[] { attack, 1f, facing });
        var clip = player.SampledClip;
        float time = player.SampleTime;
        if (clip == null || Mathf.Abs(time - clip.length) > .001f)
            throw new InvalidOperationException("Attack did not sample its final animation frame");
        var bone = player.AnimationRoot.transform.Find("Model/skeleton/Pelvis");
        Quaternion rotation = bone != null ? bone.localRotation : Quaternion.identity;
        refresh.Invoke(actor.GetComponent<ActorVisualController>(), new object[] { false, 1f });
        if (player.SampledClip != clip || Mathf.Abs(player.SampleTime - time) > .001f ||
            (bone != null && Quaternion.Angle(rotation, bone.localRotation) > .001f))
            throw new InvalidOperationException("Waiting for the next Move replaced the completed pose with Idle");
        evaluate.Invoke(player, new object[] { null, 0f, facing });
        Debug.Log("COMBAT_SCENE_FINAL_POSE_HELD " + actor.name + " clip=" + clip.name + " time=" + time);
    }

    public static void RenderDiagnostic()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/CombatScene.unity", OpenSceneMode.Single);
        var camera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true))
            .Single(c => c.CompareTag("MainCamera"));
        var target = new RenderTexture(1280, 720, 24);
        var output = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            output.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            output.Apply();
            File.WriteAllBytes("/tmp/crossblade-game-diagnostic.png", output.EncodeToPNG());
            Debug.Log("COMBAT_SCENE_RENDER_SAVED /tmp/crossblade-game-diagnostic.png");
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(output);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    public static void ValidateCombat()
    {
        Validate();
        CombatHitboxSceneOverlay.ValidateSceneMapping();
        MoveMovementValidation.Run();
        CombatBalanceValidation.Run();
        OnlineDuelSetup.RefreshContent();
        Debug.Log("COMBAT_SCENE_RUNTIME_VALIDATION_PASS");
    }
}
