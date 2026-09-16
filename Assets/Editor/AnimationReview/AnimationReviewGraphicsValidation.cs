using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CrossBlade.EditorTools
{
    public static class AnimationReviewGraphicsValidation
    {
        public static void Run()
        {
            var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(DanjinReviewSetup.ProfilePath);
            using (var session = new AnimationReviewSession(profile))
            {
                session.SetClip(profile.referenceClip);
                string output = Path.GetFullPath("../../output/unity_integration");
                foreach (int facing in new[] {1, -1})
                {
                    session.SetFacing(facing);
                    foreach (ReviewView view in Enum.GetValues(typeof(ReviewView)))
                    {
                        session.SetView(view);
                        session.Seek(.5f);
                        Save(session, Path.Combine(output, $"review_{facing}_{view}.png"));
                    }
                }
                session.SetFacing(1); session.SetView(ReviewView.Gameplay);
                session.Stop(); Save(session, Path.Combine(output, "review_default.png"));
                session.Seek(.8f); Save(session, Path.Combine(output, "review_scrub.png"));
            }
            var window = EditorWindow.GetWindow<AnimationReviewWindow>();
            try
            {
                window.position = new Rect(40, 40, 1100, 800);
                window.SelectClip(profile.referenceClip);
                window.SendEvent(new Event { type = EventType.Layout });
                window.SendEvent(new Event { type = EventType.Repaint });
                if (window.GuiPasses == 0 || window.Session == null)
                    throw new InvalidOperationException("Review window did not draw its controls.");
                window.Session.Step(1);
                if (window.Session.Frame != 1) throw new InvalidOperationException("Window session frame step failed.");
            }
            finally { window.Close(); }
            Debug.Log("ANIMATION_REVIEW_GRAPHICS_PASSED");
        }
        private static void Save(AnimationReviewSession session, string path)
        {
            var texture = session.Render(new Rect(0, 0, 960, 720));
            var old = RenderTexture.active;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = (RenderTexture)texture;
                image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { RenderTexture.active = old; Object.DestroyImmediate(image); }
        }
    }
}
