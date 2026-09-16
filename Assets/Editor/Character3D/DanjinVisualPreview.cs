using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Scripts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CrossBlade.EditorTools
{
    /// <summary>Capture real GPU-skinned poses on separate play-mode frames.</summary>
    [InitializeOnLoad]
    public static class DanjinVisualPreview
    {
        private const string Pending = "CrossBlade.DanjinPreview";
        private static CharacterAnimationPlayer player;
        private static Actor actor;
        private static Move attack;
        private static int sample, sampledFrame;
        private static bool ready;
        private static readonly float[] Progress = { 0f, 15f / 63f, 23f / 63f };
        static DanjinVisualPreview()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                {
                    actor = Object.FindObjectsByType<Actor>(FindObjectsSortMode.None).Single(a => a.name == "Player");
                    player = actor.GetComponentInChildren<CharacterAnimationPlayer>();
                    attack = AssetDatabase.LoadAssetAtPath<GameObject>(DanjinIntegrationSetup.AttackPath).GetComponent<Move>();
                    foreach (var manager in Object.FindObjectsByType<ActorManager>(FindObjectsSortMode.None)) manager.enabled = false;
                    foreach (var body in Object.FindObjectsByType<Rigidbody2D>(FindObjectsSortMode.None)) body.simulated = false;
                    // Freeze combat for diagnostic poses only. The saved scene remains untouched.
                    actor.GetComponent<ActorVisualController>().enabled = false;
                    sample = 0; ready = false;
                    EditorApplication.update += Capture;
                }
            };
        }
        public static void Run()
        {
            EditorSceneManager.OpenScene(DanjinIntegrationSetup.ScenePath);
            foreach (var manager in Object.FindObjectsByType<ActorManager>(FindObjectsSortMode.None)) manager.enabled = false;
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }
        private static void Capture()
        {
            try
            {
                if (!ready)
                {
                    typeof(CharacterAnimationPlayer).GetMethod("Evaluate", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(player, new object[] { attack, Progress[sample % 3], sample < 3 ? 1 : -1 });
                    sampledFrame = Time.frameCount;
                    ready = true;
                    return;
                }
                if (Time.frameCount < sampledFrame + 2) return;
                var camera = Camera.main;
                camera.transform.position = actor.transform.position + new Vector3(0, .7f, -10);
                camera.orthographicSize = 1.25f;
                camera.cullingMask &= ~(1 << 5);
                var target = new RenderTexture(1024, 768, 24);
                target.Create();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                var old = RenderTexture.active;
                RenderTexture.active = target;
                var image = new Texture2D(1024, 768, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1024, 768), 0, 0);
                image.Apply();
                string output = Path.GetFullPath("../../output/unity_integration");
                File.WriteAllBytes(Path.Combine(output, $"unity_{(sample < 3 ? "right" : "left")}_{Mathf.RoundToInt(Progress[sample % 3] * 63):00}.png"), image.EncodeToPNG());
                RenderTexture.active = old;
                Object.DestroyImmediate(image);
                target.Release(); Object.DestroyImmediate(target);
                sample++; ready = false;
                if (sample == 6)
                {
                    EditorApplication.update -= Capture;
                    SessionState.SetBool(Pending, false);
                    Debug.Log("DANJIN_VISUAL_PREVIEW_COMPLETE");
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception e)
            {
                EditorApplication.update -= Capture;
                SessionState.SetBool(Pending, false);
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }
    }
}
