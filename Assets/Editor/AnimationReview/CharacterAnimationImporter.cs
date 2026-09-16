using System;
using System.Linq;
using UnityEditor;

namespace CrossBlade.EditorTools
{
    // Only FBXs under an explicitly configured Incoming folder are affected.
    public sealed class CharacterAnimationImporter : AssetPostprocessor
    {
        internal static CharacterAnimationProfile ProfileFor(string path) =>
            AssetDatabase.FindAssets("t:CharacterAnimationProfile").Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>)
                .FirstOrDefault(p => p != null && CharacterAnimationProfile.IsInside(path, p.IncomingPath));
        private ModelImporter Baseline()
        {
            if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return null;
            var profile = ProfileFor(assetPath);
            if (profile == null || profile.importBaseline == null) return null;
            var baseline = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(profile.importBaseline)) as ModelImporter;
            // This workflow samples explicit bone paths, not Humanoid muscle retargeting.
            return baseline != null && baseline.animationType == ModelImporterAnimationType.Generic ? baseline : null;
        }
        private void OnPreprocessModel()
        {
            var baseline = Baseline();
            if (baseline == null) return;
            var importer = (ModelImporter)assetImporter;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.preserveHierarchy = baseline.preserveHierarchy;
            importer.motionNodeName = baseline.motionNodeName;
            importer.animationCompression = baseline.animationCompression;
            importer.resampleCurves = baseline.resampleCurves;
            importer.globalScale = baseline.globalScale;
            importer.useFileScale = baseline.useFileScale;
            importer.useFileUnits = baseline.useFileUnits;
            importer.bakeAxisConversion = baseline.bakeAxisConversion;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            // Retain mesh/hierarchy data: stripping it could affect bone paths or bind poses.
        }
        private void OnPreprocessAnimation()
        {
            var baseline = Baseline();
            if (baseline == null) return;
            var importer = (ModelImporter)assetImporter;
            // Establish defaults only. Subsequent manual splits, loops and root options survive reimport.
            if (importer.clipAnimations.Length != 0) return;
            var clips = importer.defaultClipAnimations;
            var source = baseline.clipAnimations.FirstOrDefault() ?? baseline.defaultClipAnimations.FirstOrDefault();
            foreach (var clip in clips)
            {
                clip.loopTime = false;
                clip.loopPose = false;
                if (source == null) continue;
                clip.lockRootRotation = source.lockRootRotation;
                clip.lockRootHeightY = source.lockRootHeightY;
                clip.lockRootPositionXZ = source.lockRootPositionXZ;
                clip.keepOriginalOrientation = source.keepOriginalOrientation;
                clip.keepOriginalPositionY = source.keepOriginalPositionY;
                clip.keepOriginalPositionXZ = source.keepOriginalPositionXZ;
                clip.heightFromFeet = source.heightFromFeet;
                clip.rotationOffset = source.rotationOffset;
                clip.heightOffset = source.heightOffset;
            }
            importer.clipAnimations = clips;
        }
    }
}
