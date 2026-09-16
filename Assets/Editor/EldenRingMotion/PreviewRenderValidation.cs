using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MotionPrototype
{
    internal static class PreviewRenderValidation
    {
        public static void Run()
        {
            // Include the user's gameplay scene: the preview must not render it.
            EditorSceneManager.OpenScene("Assets/Scenes/CombatScene.unity");
            EldenRingMotionImporter.EnsurePreviewPrefab();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EldenRingMotionImporter.PrefabPath);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(EldenRingMotionImporter.GeneratedRoot + "/a023_003003.anim");
            if (clip == null) throw new Exception("Missing screenshot reproduction clip");
            using var session = new MotionPreviewSession();
            session.Load(prefab, Color.gray, Color.white);
            session.SetSilhouette(false, Color.gray, Color.white);
            session.FitCameraToClip(clip, false);
            var output = Path.Combine(ProjectPaths.RepositoryRoot, "output/unity_integration");
            Directory.CreateDirectory(output);
            Color32[] previous = null;
            for (var i = 0; i < 3; i++)
            {
                session.Sample(clip, clip.length * i / 3f, false);
                var texture = session.Capture(960, 600);
                var pixels = texture.GetPixels32();
                File.WriteAllBytes(Path.Combine(output, $"preview_fixed_{i}.png"), texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                var opaque = pixels.Count(p => p.a > 200 && (p.r > 20 || p.g > 20 || p.b > 20));
                if (opaque < 500) throw new Exception($"Blank preview: {opaque} visible pixels");
                if (previous != null && pixels.Zip(previous, (a,b) => !a.Equals(b)).Count(x => x) < 100)
                    throw new Exception("Preview did not move");
                previous = pixels;
                Debug.Log($"PREVIEW_FRAME_PASS {i} pixels={opaque}");
            }
        }
    }
}
