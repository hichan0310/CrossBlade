using System;
using System.IO;
using System.Linq;
using Scripts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CrossBlade.EditorTools
{
    /// <summary>Repeatable setup with real imported assets; never fabricates GUIDs.</summary>
    public static class DanjinIntegrationSetup
    {
        public const string Folder = "Assets/Characters/Danjin";
        public const string ModelPath = Folder + "/Danjin_ER_SwordSlash_Test01.fbx";
        public const string ClipPath = Folder + "/Danjin_ER_SwordSlash_Test01.anim";
        public const string PrefabPath = Folder + "/DanjinCharacter3D.prefab";
        public const string AttackPath = "Assets/Scripts/Fighter/SamplePlayer/Attack1_1.prefab";
        public const string ScenePath = "Assets/Scenes/CombatScene.unity";

        [Serializable] private class MaterialEntry { public string name; public string texture; public float[] color; }
        [Serializable] private class Manifest { public MaterialEntry[] materials; }

        [MenuItem("Tools/CrossBlade/Set Up Danjin 3D Test")]
        public static void Run()
        {
            try
            {
                Setup();
                Debug.Log("DANJIN_SETUP_PASSED");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static void Setup()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new FileNotFoundException("Export the approved Danjin Blender file first.", ModelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.optimizeGameObjects = false;
            importer.preserveHierarchy = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.importCameras = false;
            importer.importLights = false;
            importer.motionNodeName = "DanjinRig";
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "/source_manifest.json"));
            Directory.CreateDirectory(Folder + "/Materials");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (MaterialEntry entry in manifest.materials)
            {
                string path = Folder + "/Materials/" + entry.name + ".mat";
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    AssetDatabase.CreateAsset(mat, path);
                }
                Texture texture = string.IsNullOrEmpty(entry.texture) ? null : AssetDatabase.LoadAssetAtPath<Texture>(entry.texture);
                mat.SetTexture("_BaseMap", texture);
                mat.SetColor("_BaseColor", texture != null ? Color.white : new Color(entry.color[0], entry.color[1], entry.color[2], entry.color[3]));
                mat.SetFloat("_Cull", 0f);
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.4f);
                mat.EnableKeyword("_ALPHATEST_ON");
                EditorUtility.SetDirty(mat);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.name), mat);
            }
            importer.SaveAndReimport();
            var takes = importer.defaultClipAnimations;
            foreach (var take in takes) { take.loopTime = false; take.loopPose = false; }
            importer.clipAnimations = takes;
            importer.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var source = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .First(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, ClipPath); }
            EditorUtility.CopySerialized(source, clip);
            clip.name = "Danjin_ER_SwordSlash_Test01";
            clip.hideFlags = HideFlags.None;
            clip.legacy = false;
            // FBX scene baking can emit constant prop curves. The approved grip belongs to the prefab.
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.path.Contains("Existing_MagicSword_Preview"))
                    AnimationUtility.SetEditorCurve(clip, binding, null);
            // Collapse only near-constant channels (imported hair/helper transforms and scale).
            // Keep every varying curve and the clip endpoints; error is bounded to 1e-6 per component.
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length <= 2) continue;
                var keys = curve.keys;
                float first = keys[0].value;
                if (keys.All(k => Mathf.Abs(k.value - first) <= 1e-6f))
                    AnimationUtility.SetEditorCurve(clip, binding,
                        AnimationCurve.Linear(0f, first, source.length, first));
            }
            clip.EnsureQuaternionContinuity();
            EditorUtility.SetDirty(clip);

            var character = new GameObject("Character3D");
            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.transform.SetParent(character.transform, false);
            modelInstance.name = "DanjinModel";
            var player = character.AddComponent<CharacterAnimationPlayer>();
            SetReference(player, "animationRoot", modelInstance);
            SetReference(player, "defaultPose", clip);
            Transform motionRoot = Find(modelInstance.transform, "全ての親");
            if (motionRoot == null) throw new InvalidOperationException("Imported root-motion bone missing.");
            SetReference(player, "motionRoot", motionRoot);
            foreach (Animator animator in modelInstance.GetComponentsInChildren<Animator>(true))
            {
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.enabled = false;
            }
            foreach (SkinnedMeshRenderer renderer in modelInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                renderer.updateWhenOffscreen = true;
            // Save the ready pose for immediate scene visibility, without modifying the source clip.
            clip.SampleAnimation(modelInstance, 0f);
            character.transform.localScale = Vector3.one * 0.65f;
            character.transform.localRotation = Quaternion.Euler(0, 90, 0);
            var prefab = PrefabUtility.SaveAsPrefabAsset(character, PrefabPath);
            UnityEngine.Object.DestroyImmediate(character);

            var attack = PrefabUtility.LoadPrefabContents(AttackPath);
            try
            {
                SetReference(attack.GetComponent<Move>(), "characterAnimation", clip);
                PrefabUtility.SaveAsPrefabAsset(attack, AttackPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(attack); }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            // The scene's actorType is not unique: both existing Actors serialize as Player.
            // Select the actual named Player and leave Enemy/combat configuration untouched.
            var actor = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Actor>(true))
                .Single(a => a.gameObject.name == "Player");
            var visual = actor.GetComponent<ActorVisualController>();
            Transform old = actor.transform.Find("Character3D");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, actor.transform);
            instance.name = "Character3D";
            instance.transform.localPosition = new Vector3(0f, 0.018f, 0f);
            SetReference(visual, "characterPlayer", instance.GetComponent<CharacterAnimationPlayer>());
            var actorData = new SerializedObject(actor);
            Transform facing = actorData.FindProperty("facingRoot").objectReferenceValue as Transform;
            if (facing == null || facing == actor.transform || instance.transform.IsChildOf(facing))
                throw new InvalidOperationException("Persistent model must be outside the negatively scaled 2D FacingRoot.");
            // Match the initial facing already serialized on the Actor.
            bool positiveRight = actorData.FindProperty("positiveScaleFacesRight").boolValue;
            bool right = (facing.localScale.x >= 0f) == positiveRight;
            instance.transform.localRotation = Quaternion.Euler(0, right ? 90 : 270, 0);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        private static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        private static void SetReference(UnityEngine.Object target, string property, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
