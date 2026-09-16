using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CrossBlade.EditorTools
{
    public sealed class AnimationReviewWindow : EditorWindow
    {
        [SerializeField] private CharacterAnimationProfile profile;
        [SerializeField] private AnimationClip clip;
        [SerializeField] private GameObject movePrefab;
        private AnimationReviewSession session;
        private string[] sources = Array.Empty<string>();
        private GameObject[] moves = Array.Empty<GameObject>();
        private Vector2 scroll;
        private string message;
        private double lastUpdate;
        private bool refreshPending;
        public AnimationReviewSession Session => session;
        internal int GuiPasses { get; private set; }

        public static void Open() => GetWindow<AnimationReviewWindow>("Animation Review");
        private void OnEnable()
        {
            minSize = new Vector2(800, 580);
            if (profile == null) profile = AssetDatabase.FindAssets("t:CharacterAnimationProfile")
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>).FirstOrDefault();
            if (clip == null && profile != null) clip = profile.referenceClip;
            lastUpdate = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            RefreshAssets();
            Rebuild();
        }
        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            session?.Dispose(); session = null;
        }
        private void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) { session?.Dispose(); session = null; }
            if (state == PlayModeStateChange.EnteredEditMode) Rebuild();
        }
        private void OnProjectChange() => refreshPending = true;
        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (refreshPending && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                refreshPending = false;
                RefreshAssets(); Rebuild(); Repaint();
            }
            if (session != null && session.Playing) { session.Advance(now - lastUpdate); Repaint(); }
            lastUpdate = now;
        }
        private void RefreshAssets()
        {
            if (profile == null) return;
            var folders = new[] { profile.IncomingPath, profile.ApprovedPath }.Where(AssetDatabase.IsValidFolder).ToArray();
            sources = (folders.Length == 0 ? Array.Empty<string>() : AssetDatabase.FindAssets("", folders)
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)))
                .Concat(new[] {AssetDatabase.GetAssetPath(profile.referenceClip), AssetDatabase.GetAssetPath(profile.importBaseline)})
                .Where(p => !string.IsNullOrEmpty(p)).Distinct().OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
            moves = AssetDatabase.FindAssets("t:Prefab").Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(p => p != null && p.GetComponent<Scripts.Move>() != null)
                .OrderBy(p => p.name).ToArray();
        }
        private void Rebuild()
        {
            session?.Dispose(); session = null;
            if (profile == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            try { session = new AnimationReviewSession(profile); session.SetClip(clip); }
            catch (Exception e) { message = e.Message; session?.Dispose(); session = null; }
        }
        public void SelectClip(AnimationClip selected)
        {
            clip = selected; message = null;
            if (session == null) Rebuild(); else session.SetClip(clip);
            Repaint();
        }
        private void OnGUI()
        {
            GuiPasses++;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Exit Play mode to review animations in the isolated editor preview.", MessageType.Info); return;
            }
            EditorGUILayout.BeginHorizontal();
            DrawSidebar();
            EditorGUILayout.BeginVertical();
            DrawViewport();
            DrawControls();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }
        private void DrawSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(295));
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();
            var selected = (CharacterAnimationProfile)EditorGUILayout.ObjectField("Character", profile, typeof(CharacterAnimationProfile), false);
            if (EditorGUI.EndChangeCheck()) { profile = selected; clip = profile != null ? profile.referenceClip : null; RefreshAssets(); Rebuild(); }
            if (profile == null)
            {
                EditorGUILayout.HelpBox("Create an Animation Review Profile and select a prefab with CharacterAnimationPlayer.", MessageType.Info);
                EditorGUILayout.EndScrollView(); EditorGUILayout.EndVertical(); return;
            }
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("Preview prefab", profile.previewPrefab, typeof(GameObject), false);
            EditorGUILayout.LabelField("Incoming folder", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(profile.IncomingPath, EditorStyles.wordWrappedLabel, GUILayout.Height(38));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Show Incoming")) { Selection.activeObject = profile.incomingFolder; EditorGUIUtility.PingObject(profile.incomingFolder); }
            if (GUILayout.Button("Refresh")) { RefreshAssets(); Rebuild(); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Recent imports / approved / reference", EditorStyles.boldLabel);
            int index = Array.IndexOf(sources, AssetDatabase.GetAssetPath(clip));
            int newIndex = EditorGUILayout.Popup(index, sources.Select(p => Path.GetFileName(p) + (CharacterAnimationProfile.IsInside(p, profile.ApprovedPath) ? " [Approved]" : "")).ToArray());
            if (newIndex != index && newIndex >= 0) SelectClip(AnimationReviewAssets.ClipsAt(sources[newIndex]).FirstOrDefault());
            var source = EditorGUILayout.ObjectField("FBX or clip", clip, typeof(Object), false);
            if (source != clip) SelectClip(source is AnimationClip c ? c : AnimationReviewAssets.ClipsAt(AssetDatabase.GetAssetPath(source)).FirstOrDefault());
            if (session != null) session.Background = EditorGUILayout.ColorField("Background", session.Background);
            if (clip != null)
            {
                var takes = AnimationReviewAssets.ClipsAt(AssetDatabase.GetAssetPath(clip));
                int take = Array.IndexOf(takes, clip);
                int next = EditorGUILayout.Popup("Take", take, takes.Select(c => c.name).ToArray());
                if (next != take && next >= 0) SelectClip(takes[next]);
                DrawMetadata();
                bool approved = AnimationReviewAssets.IsApproved(clip, profile);
                EditorGUILayout.HelpBox(approved ? "Approved snapshot" : "Unreviewed — approval is your decision. Leave it here if rejected.", MessageType.Info);
                using (new EditorGUI.DisabledScope(approved || session == null || session.Issues.Length > 0))
                    if (GUILayout.Button("Approve snapshot…"))
                    {
                        if (EditorUtility.DisplayDialog("Approve animation", "Save the reviewed pose data as a new approved .anim snapshot? The source FBX remains unchanged.", "Approve", "Cancel"))
                            Try(() => { SelectClip(AnimationReviewAssets.Approve(clip, profile)); message = "Approved snapshot saved. Choose a Move to assign it."; });
                    }
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Assign to existing Move", EditorStyles.boldLabel);
                int current = Array.IndexOf(moves, movePrefab);
                int choice = EditorGUILayout.Popup(current, moves.Select(p => p.name + " — " + Path.GetDirectoryName(AssetDatabase.GetAssetPath(p))).ToArray());
                if (choice >= 0 && choice != current) movePrefab = moves[choice];
                movePrefab = (GameObject)EditorGUILayout.ObjectField(movePrefab, typeof(GameObject), false);
                using (new EditorGUI.DisabledScope(!approved || movePrefab == null))
                    if (GUILayout.Button("Assign approved clip"))
                        Try(() => { AnimationReviewAssets.Assign(clip, profile, movePrefab); message = "Assigned " + clip.name + " to " + movePrefab.name + "."; });
            }
            if (session != null && session.Issues.Length > 0)
                EditorGUILayout.HelpBox($"{session.Issues.Length} incompatible bindings. Export the same Generic bone hierarchy.\n" + string.Join("\n", session.Issues.Take(4)), MessageType.Error);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            EditorGUILayout.EndScrollView(); EditorGUILayout.EndVertical();
        }
        private void DrawMetadata()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(clip.name, EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(AssetDatabase.GetAssetPath(clip), EditorStyles.wordWrappedLabel, GUILayout.Height(50));
            EditorGUILayout.LabelField($"{clip.length:F3} s · {clip.frameRate:g} fps · {(session?.LastFrame ?? 0) + 1} samples (incl. endpoints)");
            EditorGUILayout.LabelField($"Asset loop: {clip.isLooping} · Root curves: {clip.hasRootCurves}");
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as ModelImporter;
            if (importer != null)
            {
                EditorGUILayout.LabelField($"Rig: {importer.animationType} · Root: {importer.motionNodeName}", EditorStyles.wordWrappedLabel);
                var take = importer.clipAnimations.FirstOrDefault(t => t.name == clip.name);
                if (take != null) EditorGUILayout.LabelField($"Bake root rotation/Y/XZ: {take.lockRootRotation}/{take.lockRootHeightY}/{take.lockRootPositionXZ}", EditorStyles.wordWrappedLabel);
            }
            var origin = AnimationReviewAssets.SourceOf(clip);
            if (origin != null && !string.IsNullOrEmpty(origin.sourceGuid))
                EditorGUILayout.LabelField("Snapshot source: " + AssetDatabase.GUIDToAssetPath(origin.sourceGuid) + " / " + origin.takeName, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Preview uses runtime horizontal root lock and fixed weapon grip.", EditorStyles.wordWrappedLabel);
        }
        private void DrawViewport()
        {
            EditorGUILayout.BeginHorizontal();
            if (session != null)
            {
                var view = (ReviewView)EditorGUILayout.EnumPopup(session.View);
                if (view != session.View) session.SetView(view);
                int facing = GUILayout.Toolbar(session.Facing > 0 ? 0 : 1, new[] {"Facing Right", "Facing Left"});
                if ((facing == 0 ? 1 : -1) != session.Facing) session.SetFacing(facing == 0 ? 1 : -1);
                if (GUILayout.Button("Reset view")) { session.Zoom = 1.15f; session.Target = new Vector3(0, .7f, 0); session.SetView(ReviewView.Gameplay); }
            }
            EditorGUILayout.EndHorizontal();
            Rect rect = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (session == null) return;
            var e = Event.current;
            if (rect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDrag && e.button == 0) { var orbit = session.Orbit + new Vector2(-e.delta.x, e.delta.y) * .5f; orbit.y = Mathf.Clamp(orbit.y, -85, 85); session.Orbit = orbit; e.Use(); Repaint(); }
                if (e.type == EventType.ScrollWheel) { session.Zoom = Mathf.Clamp(session.Zoom * Mathf.Exp(e.delta.y * .05f), .1f, 10); e.Use(); Repaint(); }
            }
            if (e.type == EventType.Repaint)
            {
                try { GUI.DrawTexture(rect, session.Render(rect), ScaleMode.StretchToFill, false); }
                catch (Exception ex) { message = ex.Message; }
            }
            EditorGUILayout.LabelField("Drag to orbit · Scroll to zoom · Views follow character facing", EditorStyles.miniLabel);
        }
        private void DrawControls()
        {
            if (session == null || clip == null) return;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(session.Playing ? "Pause" : "Play")) { if (session.Playing) session.Pause(); else session.Play(); lastUpdate = EditorApplication.timeSinceStartup; }
            if (GUILayout.Button("Stop / Reset")) session.Stop();
            if (GUILayout.Button("◀ Frame")) session.Step(-1);
            if (GUILayout.Button("Frame ▶")) session.Step(1);
            session.Loop = GUILayout.Toggle(session.Loop, "Loop");
            EditorGUILayout.EndHorizontal();
            session.Speed = EditorGUILayout.Slider("Playback speed", session.Speed, .05f, 3f);
            EditorGUI.BeginChangeCheck();
            float time = EditorGUILayout.Slider("Time (seconds)", session.Time, 0, clip.length);
            if (EditorGUI.EndChangeCheck()) { session.Pause(); session.Seek(time); Repaint(); }
            EditorGUILayout.LabelField($"Frame {session.Frame} / {session.LastFrame} · {session.Time:F3} / {clip.length:F3} s · Normalized {session.Normalized:F4}");
        }
        private void Try(Action action) { try { action(); } catch (Exception e) { message = e.Message; Debug.LogException(e); } }
    }
}
