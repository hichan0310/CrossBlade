using System;
using System.Reflection;
using Scripts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CrossBlade.EditorTools
{
    public enum ReviewView { Gameplay, Front, Back, Left, Right, FrontThreeQuarter, RearThreeQuarter }

    /// <summary>Disposable editor scene. Uses the production sampler through a temporary Move.</summary>
    public sealed class AnimationReviewSession : IDisposable
    {
        private static readonly MethodInfo Evaluate = typeof(CharacterAnimationPlayer).GetMethod("Evaluate", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly CharacterAnimationProfile profile;
        private readonly PreviewRenderUtility renderer;
        private readonly Scene scene;
        private readonly Move move;
        private AnimationClip prepared;
        public GameObject Instance { get; }
        public CharacterAnimationPlayer Player { get; }
        public AnimationClip Source { get; private set; }
        public string[] Issues { get; private set; } = Array.Empty<string>();
        public float Time { get; private set; }
        public float Speed { get; set; } = 1;
        public bool Loop { get; set; }
        public bool Playing { get; private set; }
        public int Facing { get; private set; } = 1;
        public float Rate => Source != null && Source.frameRate > 0 ? Source.frameRate : 30;
        public int LastFrame => Source == null ? 0 : Mathf.RoundToInt(Source.length * Rate);
        public int Frame => Mathf.Clamp(Mathf.RoundToInt(Time * Rate), 0, LastFrame);
        public float Normalized => Source != null && Source.length > 0 ? Time / Source.length : 0;
        public Vector2 Orbit { get; set; } = new Vector2(180, 0);
        public float Zoom { get; set; } = 1.15f;
        public Color Background { get; set; } = new Color(.45f, .48f, .52f);
        public Vector3 Target { get; set; } = new Vector3(0, .7f, 0);
        public ReviewView View { get; private set; }

        public AnimationReviewSession(CharacterAnimationProfile profile, bool graphics = true)
        {
            this.profile = profile;
            if (profile == null || profile.previewPrefab == null) throw new InvalidOperationException("Choose a profile with a preview prefab.");
            if (profile.previewPrefab.GetComponentInChildren<CharacterAnimationPlayer>(true) == null)
                throw new InvalidOperationException("Preview prefab needs the existing CharacterAnimationPlayer.");
            try
            {
                renderer = graphics ? new PreviewRenderUtility() : null;
                scene = renderer != null ? renderer.camera.scene : EditorSceneManager.NewPreviewScene();
                Instance = (GameObject)PrefabUtility.InstantiatePrefab(profile.previewPrefab, scene);
                Instance.hideFlags = HideFlags.HideAndDontSave;
                Instance.transform.position = Vector3.zero;
                Player = Instance.GetComponentInChildren<CharacterAnimationPlayer>(true);
                foreach (var skin in Instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skin.updateWhenOffscreen = true;
                    skin.forceMatrixRecalculationPerRender = true;
                }
                var go = new GameObject("Review Move (temporary)") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(go, scene);
                move = go.AddComponent<Move>();
                Evaluate.Invoke(Player, new object[] { null, 0f, Facing });
                if (Player.AnimationRoot == null) throw new InvalidOperationException("Preview player has no animation root.");
                foreach (string path in profile.fixedAttachmentPaths)
                    if (Player.AnimationRoot.transform.Find(path) == null)
                        throw new InvalidOperationException("Fixed attachment path does not exist: " + path);
                if (renderer != null)
                {
                    renderer.camera.clearFlags = CameraClearFlags.SolidColor;
                    renderer.camera.backgroundColor = new Color(.12f, .15f, .20f);
                    renderer.camera.nearClipPlane = .01f;
                    renderer.camera.farClipPlane = 100;
                    renderer.lights[0].intensity = 1.2f;
                    renderer.lights[0].transform.rotation = Quaternion.Euler(40, 30, 0);
                    renderer.lights[1].intensity = .8f;
                    renderer.ambientColor = Color.gray;
                }
            }
            catch { Dispose(); throw; }
        }
        public void SetClip(AnimationClip clip)
        {
            Pause();
            // Reset cached channels through the real runtime transition path.
            Evaluate.Invoke(Player, new object[] { null, 0f, Facing });
            if (prepared != null) Object.DestroyImmediate(prepared);
            Source = clip;
            Issues = clip == null ? Array.Empty<string>() : AnimationReviewAssets.Incompatibilities(clip, Player.AnimationRoot, profile);
            prepared = clip == null ? null : AnimationReviewAssets.PrepareClip(clip, profile);
            var data = new SerializedObject(move);
            data.FindProperty("characterAnimation").objectReferenceValue = prepared;
            data.ApplyModifiedPropertiesWithoutUndo();
            Seek(0);
        }
        public void Seek(float seconds)
        {
            Time = Source == null ? 0 : Mathf.Clamp(seconds, 0, Source.length);
            Evaluate.Invoke(Player, new object[] { Source != null ? move : null, Normalized, Facing });
            EditorApplication.QueuePlayerLoopUpdate();
        }
        public void Step(int direction) { Pause(); Seek(Mathf.Clamp(Frame + direction, 0, LastFrame) / Rate); }
        public void Play() { if (Source == null || Source.length <= 0) return; if (Time >= Source.length) Seek(0); Playing = true; }
        public void Pause() => Playing = false;
        public void Stop() { Pause(); Seek(0); }
        public void Advance(double elapsed)
        {
            if (!Playing || Source == null) return;
            float next = Time + (float)Math.Max(0, elapsed) * Mathf.Max(0, Speed);
            if (next >= Source.length)
            {
                if (Loop && Source.length > 0) next %= Source.length;
                else { next = Source.length; Pause(); }
            }
            Seek(next);
        }
        public void SetFacing(int facing) { Facing = facing < 0 ? -1 : 1; Seek(Time); SetView(View); }
        public void SetView(ReviewView view)
        {
            View = view;
            float yaw = Player.transform.eulerAngles.y;
            switch (view)
            {
                case ReviewView.Gameplay: Orbit = new Vector2(180, 0); break;
                case ReviewView.Front: Orbit = new Vector2(yaw, 0); break;
                case ReviewView.Back: Orbit = new Vector2(yaw + 180, 0); break;
                case ReviewView.Left: Orbit = new Vector2(yaw - 90, 0); break;
                case ReviewView.Right: Orbit = new Vector2(yaw + 90, 0); break;
                case ReviewView.FrontThreeQuarter: Orbit = new Vector2(yaw + 45, 12); break;
                case ReviewView.RearThreeQuarter: Orbit = new Vector2(yaw + 135, 12); break;
            }
        }
        public Texture Render(Rect rect)
        {
            if (renderer == null) throw new InvalidOperationException("This session has no graphics viewport.");
            renderer.BeginPreview(rect, GUIStyle.none);
            Texture texture = null;
            try
            {
                var camera = renderer.camera;
                camera.backgroundColor = Background;
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Clamp(Zoom, .1f, 10);
                Vector3 direction = Quaternion.Euler(-Orbit.y, Orbit.x, 0) * Vector3.forward;
                camera.transform.position = Target + direction * 8;
                camera.transform.LookAt(Target);
                renderer.Render(true);
            }
            finally { texture = renderer.EndPreview(); }
            return texture;
        }
        public void Dispose()
        {
            if (prepared != null) Object.DestroyImmediate(prepared);
            if (renderer != null) renderer.Cleanup();
            else if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
