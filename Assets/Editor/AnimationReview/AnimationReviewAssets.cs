using System;
using System.IO;
using System.Linq;
using Scripts;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CrossBlade.EditorTools
{
    public static class AnimationReviewAssets
    {
        [Serializable] public sealed class ApprovalSource
        {
            public string sourceGuid;
            public long sourceLocalId;
            public string takeName;
        }
        public static ApprovalSource SourceOf(AnimationClip clip)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
            if (importer == null || string.IsNullOrEmpty(importer.userData)) return null;
            try { return JsonUtility.FromJson<ApprovalSource>(importer.userData); }
            catch (ArgumentException) { return null; }
        }
        public static AnimationClip[] ClipsAt(string path) => AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();

        public static AnimationClip PrepareClip(AnimationClip source, CharacterAnimationProfile profile)
        {
            var copy = Object.Instantiate(source);
            copy.name = source.name;
            copy.hideFlags = HideFlags.HideAndDontSave;
            foreach (var binding in AnimationUtility.GetCurveBindings(copy))
                if (profile.IsFixed(binding.path)) AnimationUtility.SetEditorCurve(copy, binding, null);
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(copy))
                if (profile.IsFixed(binding.path)) AnimationUtility.SetObjectReferenceCurve(copy, binding, null);
            return copy;
        }
        public static string[] Incompatibilities(AnimationClip clip, GameObject root, CharacterAnimationProfile profile)
        {
            if (clip.humanMotion) return new[] { "Humanoid muscle clips are unsupported. Export animation on this character's Generic skeleton." };
            var bindings = AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip));
            return bindings.Where(b => !profile.IsFixed(b.path)).Where(b =>
            {
                Transform t = string.IsNullOrEmpty(b.path) ? root.transform : root.transform.Find(b.path);
                return t == null || (b.type != typeof(Transform) && t.GetComponent(b.type) == null);
            }).Select(b => b.path + " : " + b.propertyName).Distinct().ToArray();
        }
        public static bool IsApproved(AnimationClip clip, CharacterAnimationProfile profile) => clip != null
            && CharacterAnimationProfile.IsInside(AssetDatabase.GetAssetPath(clip), profile.ApprovedPath)
            && AssetDatabase.GetAssetPath(clip).EndsWith(".anim", StringComparison.OrdinalIgnoreCase);

        // Called only by the explicit human approval button (or isolated validation fixtures).
        public static AnimationClip Approve(AnimationClip source, CharacterAnimationProfile profile)
        {
            if (!AssetDatabase.IsValidFolder(profile.ApprovedPath)) throw new InvalidOperationException("Configure an Approved folder.");
            using (var session = new AnimationReviewSession(profile, false))
            {
                session.SetClip(source);
                if (session.Issues.Length != 0) throw new InvalidOperationException("Resolve incompatible bindings before approval.");
            }
            var copy = PrepareClip(source, profile);
            // Same bounded reduction used by the existing Danjin setup for constant helper channels.
            foreach (var binding in AnimationUtility.GetCurveBindings(copy))
            {
                var curve = AnimationUtility.GetEditorCurve(copy, binding);
                if (curve == null || curve.length <= 2) continue;
                float value = curve.keys[0].value;
                if (curve.keys.All(k => Mathf.Abs(k.value - value) <= 1e-6f))
                    AnimationUtility.SetEditorCurve(copy, binding, AnimationCurve.Linear(0, value, source.length, value));
            }
            copy.hideFlags = HideFlags.None;
            string safe = string.Concat(source.name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' || c == '\\' ? '_' : c));
            string path = AssetDatabase.GenerateUniqueAssetPath(profile.ApprovedPath + "/" + safe + ".anim");
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId);
            var importer = AssetImporter.GetAtPath(path);
            importer.userData = JsonUtility.ToJson(new ApprovalSource { sourceGuid = guid, sourceLocalId = localId, takeName = source.name });
            AssetDatabase.SaveAssets();
            AssetDatabase.WriteImportSettingsIfDirty(path);
            return copy;
        }
        public static void Assign(AnimationClip clip, CharacterAnimationProfile profile, GameObject movePrefab)
        {
            if (!IsApproved(clip, profile)) throw new InvalidOperationException("Approve a snapshot before assigning it.");
            string path = AssetDatabase.GetAssetPath(movePrefab);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || movePrefab.GetComponent<Move>() == null)
                throw new InvalidOperationException("Choose a prefab with a root Move component.");
            var instance = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var data = new SerializedObject(instance.GetComponent<Move>());
                data.FindProperty("characterAnimation").objectReferenceValue = clip;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(instance); }
        }
    }
}
