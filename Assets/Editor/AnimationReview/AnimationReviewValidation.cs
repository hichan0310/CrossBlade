using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Scripts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CrossBlade.EditorTools
{
    public static class AnimationReviewValidation
    {
        private static readonly List<string> Checks = new List<string>();
        private static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            Checks.Add(message);
        }
        public static void Run()
        {
            string temp = AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/AnimationReview/__Validation");
            string imported = null;
            CharacterAnimationProfile testProfile = null;
            try
            {
                Checks.Clear();
                var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(DanjinReviewSetup.ProfilePath);
                Require(profile != null, "Real shared character profile exists");
                string prefabHash = AssetDatabase.GetAssetDependencyHash(DanjinIntegrationSetup.PrefabPath).ToString();
                string sceneHash = AssetDatabase.GetAssetDependencyHash(DanjinIntegrationSetup.ScenePath).ToString();
                string moveText = File.ReadAllText(DanjinIntegrationSetup.AttackPath);
                var embedded = AnimationReviewAssets.ClipsAt(DanjinIntegrationSetup.ModelPath).First();
                var native = profile.referenceClip;
                Require(embedded != null && native != null, "Existing slash is discoverable as both embedded FBX and native clip");
                using (var session = new AnimationReviewSession(profile, false))
                {
                    Require(EditorSceneManager.IsPreviewScene(session.Instance.scene), "Character lives in an isolated editor preview scene");
                    session.SetClip(embedded);
                    Require(session.Issues.Length == 0, "Embedded slash binds to the actual Generic rig");
                    Require(session.Instance.GetComponentsInChildren<SkinnedMeshRenderer>().Any(), "Real Danjin skinned mesh is instantiated");
                    var sword = session.Player.AnimationRoot.transform.Find(profile.fixedAttachmentPaths.Single());
                    Require(sword != null && sword.GetComponent<Renderer>() != null, "Actual attached sword is present");
                    var rotation = sword.localRotation;
                    var position = sword.localPosition;
                    var hand = session.Instance.GetComponentsInChildren<Transform>().Single(t => t.name == "右手首");
                    session.Seek(.8f); var pose = hand.position;
                    session.Seek(.2f); session.Seek(.8f);
                    Require(Vector3.Distance(hand.position, pose) < 1e-5f, "Out-of-order scrubbing deterministically returns the same hand pose");
                    session.Stop(); var start = hand.position;
                    session.Play(); session.Advance(.3);
                    Require(session.Playing && session.Time > .29f && Vector3.Distance(hand.position, start) > .05f, "Play advances time and actual hand motion");
                    session.Pause(); session.Advance(1);
                    Require(Mathf.Abs(session.Time - .3f) < 1e-5f, "Pause stops time advancement");
                    session.Stop(); Require(session.Time == 0 && !session.Playing, "Stop resets to frame zero");
                    session.Step(1); Require(Mathf.Abs(session.Time - 1f / embedded.frameRate) < 1e-6f, "Next frame uses the clip's actual sample rate");
                    session.Step(-1); Require(session.Frame == 0, "Previous frame returns to frame zero");
                    session.Speed = 2; session.Play(); session.Advance(.2);
                    Require(Mathf.Abs(session.Time - .4f) < 1e-5f, "Playback speed doubles sampled time");
                    session.Speed = 1; session.Loop = true; session.Seek(embedded.length - .1f); session.Advance(.2);
                    Require(session.Playing && Mathf.Abs(session.Time - .1f) < 1e-5f, "Loop wraps with remainder preserved");
                    session.Loop = false; session.Seek(embedded.length - .1f); session.Advance(.2);
                    Require(!session.Playing && Mathf.Abs(session.Time - embedded.length) < 1e-5f, "Non-loop playback holds the final frame");
                    Require(session.LastFrame == 63 && Mathf.Abs(session.Rate - 30) < .001f, "Metadata reports 64 endpoint samples at 30 FPS over 2.1 seconds");
                    foreach (int facing in new[] {1, -1})
                    {
                        session.SetFacing(facing);
                        Require(Vector3.Dot(session.Player.transform.forward, Vector3.right * facing) > .99f, "Runtime Y-rotation facing " + facing);
                        Require(session.Instance.GetComponentsInChildren<Transform>().All(t => t.localToWorldMatrix.determinant > 0), "Positive skinned hierarchy determinant facing " + facing);
                        foreach (ReviewView view in Enum.GetValues(typeof(ReviewView)))
                        {
                            session.SetView(view);
                            Require(session.View == view && !float.IsNaN(session.Orbit.x), "Camera preset " + view + " facing " + facing);
                        }
                        for (int frame = 0; frame <= session.LastFrame; frame++)
                        {
                            session.Seek(frame / session.Rate);
                            if (Quaternion.Angle(sword.localRotation, rotation) > .01f || Vector3.Distance(sword.localPosition, position) > 1e-5f)
                                throw new InvalidOperationException("Sword attachment changed at frame " + frame);
                        }
                        Require(true, "Fixed sword grip survives every source frame facing " + facing);
                    }
                    session.SetClip(native); session.Seek(.6f);
                    Require(session.Issues.Length == 0, "Native .anim also samples without missing bindings");
                    var bad = new AnimationClip();
                    AnimationUtility.SetEditorCurve(bad, EditorCurveBinding.FloatCurve("MissingRig/Bone", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Linear(0, 0, 1, 1));
                    session.SetClip(bad); Require(session.Issues.Length == 1, "Incompatible skeleton is explicitly diagnosed");
                    session.SetClip(native); Object.DestroyImmediate(bad);
                }
                AssetDatabase.CreateFolder("Assets/Editor/AnimationReview", Path.GetFileName(temp));
                testProfile = Object.Instantiate(profile);
                testProfile.approvedFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(temp);
                imported = AssetDatabase.GenerateUniqueAssetPath(profile.IncomingPath + "/__ReviewValidationSecond.fbx");
                if (File.Exists(imported)) throw new InvalidOperationException("Validation FBX path is already occupied.");
                File.Copy(DanjinIntegrationSetup.ModelPath, imported);
                AssetDatabase.ImportAsset(imported, ImportAssetOptions.ForceSynchronousImport);
                var importer = (ModelImporter)AssetImporter.GetAtPath(imported);
                var baseline = (ModelImporter)AssetImporter.GetAtPath(DanjinIntegrationSetup.ModelPath);
                Require(importer.animationType == ModelImporterAnimationType.Generic && importer.importAnimation && !importer.optimizeGameObjects,
                    "Incoming FBX automatically receives compatible Generic import settings");
                Require(importer.motionNodeName == baseline.motionNodeName && importer.animationCompression == baseline.animationCompression
                    && importer.bakeAxisConversion == baseline.bakeAxisConversion && importer.globalScale == baseline.globalScale, "Incoming root, compression, axes and scale match the baseline");
                Require(!importer.importCameras && !importer.importLights && importer.materialImportMode == ModelImporterMaterialImportMode.None,
                    "Animation import excludes extra cameras, lights and materials");
                Require(importer.clipAnimations.All(c => !c.loopTime), "New imported takes default to non-looping");
                var takes = importer.clipAnimations;
                takes[0].name = "Second_Animation_Review_Test";
                takes[0].loopTime = true;
                importer.clipAnimations = takes; importer.SaveAndReimport();
                Require(importer.clipAnimations[0].loopTime, "Explicit user loop setting survives reimport");
                var second = AnimationReviewAssets.ClipsAt(imported).Single();
                Require(second.name == "Second_Animation_Review_Test", "Second FBX take is selectable without a hard-coded clip name");
                using (var session = new AnimationReviewSession(profile, false))
                {
                    session.SetClip(second); session.Seek(.5f);
                    Require(session.Issues.Length == 0 && Mathf.Abs(session.Player.SampleTime - .5f) < 1e-5f, "Newly imported second asset samples on actual Danjin");
                }
                var approved = AnimationReviewAssets.Approve(second, testProfile);
                Require(AnimationReviewAssets.IsApproved(approved, testProfile), "Explicit test approval creates a native snapshot in the chosen folder");
                Require(!AnimationUtility.GetCurveBindings(approved).Any(b => profile.IsFixed(b.path)), "Approved snapshot excludes fixed attachment keys");
                Require(File.Exists(imported), "Approval preserves the incoming source FBX");
                Require(AssetDatabase.GUIDToAssetPath(AnimationReviewAssets.SourceOf(approved).sourceGuid) == imported,
                    "Approved snapshot records its source FBX GUID and take in its meta file");
                string copyPath = temp + "/Attack1_1.prefab";
                AssetDatabase.CopyAsset(DanjinIntegrationSetup.AttackPath, copyPath);
                var target = AssetDatabase.LoadAssetAtPath<GameObject>(copyPath);
                AnimationReviewAssets.Assign(approved, testProfile, target);
                Require(new SerializedObject(target.GetComponent<Move>()).FindProperty("characterAnimation").objectReferenceValue == approved,
                    "Approved snapshot assigns through the existing Attack1_1 Move field on an isolated prefab copy");
                bool denied = false;
                try { AnimationReviewAssets.Assign(second, testProfile, target); } catch (InvalidOperationException) { denied = true; }
                Require(denied, "Unreviewed FBX clips cannot be assigned through the approval UI service");
                Require(File.ReadAllText(DanjinIntegrationSetup.AttackPath) == moveText, "Production Attack1_1 remains unchanged by validation");
                Require(AssetDatabase.GetAssetDependencyHash(DanjinIntegrationSetup.PrefabPath).ToString() == prefabHash
                    && AssetDatabase.GetAssetDependencyHash(DanjinIntegrationSetup.ScenePath).ToString() == sceneHash, "Production character prefab and CombatScene remain unchanged");
                Require(CharacterAnimationImporter.ProfileFor(DanjinIntegrationSetup.ModelPath) == null, "Importer does not touch the validated model outside Incoming");
                string output = Path.GetFullPath("../../output/unity_integration");
                File.WriteAllText(Path.Combine(output, "animation_review_validation.json"), JsonUtility.ToJson(new Result { passed = true, checks = Checks.ToArray() }, true));
                Debug.Log("ANIMATION_REVIEW_VALIDATION_PASSED " + Checks.Count + " checks");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
                throw;
            }
            finally
            {
                if (testProfile != null) Object.DestroyImmediate(testProfile);
                if (imported != null && AssetDatabase.LoadMainAssetAtPath(imported) != null) AssetDatabase.DeleteAsset(imported);
                if (AssetDatabase.IsValidFolder(temp)) AssetDatabase.DeleteAsset(temp);
            }
        }
        [Serializable] private class Result { public bool passed; public string[] checks; }
    }
}
