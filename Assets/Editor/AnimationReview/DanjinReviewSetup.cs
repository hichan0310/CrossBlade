using System.Linq;
using Scripts;
using UnityEditor;
using UnityEngine;

namespace CrossBlade.EditorTools
{
    public static class DanjinReviewSetup
    {
        public const string ProfilePath = DanjinIntegrationSetup.Folder + "/DanjinAnimationReview.asset";
        public static void Run()
        {
            Folder(DanjinIntegrationSetup.Folder + "/Animations");
            string incoming = DanjinIntegrationSetup.Folder + "/Animations/Incoming";
            string approved = DanjinIntegrationSetup.Folder + "/Animations/Approved";
            Folder(incoming); Folder(approved);
            var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<CharacterAnimationProfile>();
                profile.previewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DanjinIntegrationSetup.PrefabPath);
                profile.importBaseline = AssetDatabase.LoadAssetAtPath<GameObject>(DanjinIntegrationSetup.ModelPath);
                profile.referenceClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DanjinIntegrationSetup.ClipPath);
                profile.incomingFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(incoming);
                profile.approvedFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(approved);
                var player = profile.previewPrefab.GetComponent<CharacterAnimationPlayer>();
                var root = (GameObject)new SerializedObject(player).FindProperty("animationRoot").objectReferenceValue;
                var sword = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Existing_MagicSword_Preview");
                profile.fixedAttachmentPaths = new[] { AnimationUtility.CalculateTransformPath(sword, root.transform) };
                AssetDatabase.CreateAsset(profile, ProfilePath);
                AssetDatabase.SaveAssets();
            }
            Debug.Log("ANIMATION_REVIEW_SETUP_PASSED");
        }
        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
