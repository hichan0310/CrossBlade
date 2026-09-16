using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MotionPrototype
{
    [Serializable]
    internal sealed class ShadowCatalogEntry
    {
        public string id;
        public string group;
        public string canonical_name;
        public string source_animation;
        public string clip_name;
        public string category;
    }

    [Serializable]
    internal sealed class ShadowBrowserCatalog
    {
        public int format_version;
        public ShadowCatalogEntry[] entries;
    }

    internal enum MotionBrowserFilter
    {
        All,
        Attack,
        Dodge,
        Guard,
        Parry,
        Hit,
        Movement,
        Skill,
        Unknown
    }

    internal sealed class MotionBrowserWindow : EditorWindow
    {
        private GameObject _character;
        private AnimationClip _clip;
        private GameObject _weapon;
        private string _weaponBone = "Palm.R";
        private Vector3 _weaponPosition;
        private Vector3 _weaponEuler;
        private Vector3 _weaponScale = Vector3.one;
        private MotionPreviewSession _session;
        private readonly List<SelectedFrame> _selected = new();
        private Vector2 _selectionScroll;
        private Vector2 _animationScroll;
        private Vector2 _shadowScroll;
        private string _animationSearch = "";
        private ShadowBrowserCatalog _shadowCatalog;
        private string[] _shadowGroups = { "a023" };
        private int _shadowGroupIndex;
        private MotionBrowserFilter _animationFilter;
        private bool _playing;
        private bool _rootMotion;
        private bool _silhouette;
        private bool _showTrail;
        private bool _orthographic = true;
        private float _time;
        private float _turnDuration = 0.5f;
        private double _lastEditorTime;
        private int _resolution = 512;
        private CameraPreset _preset = CameraPreset.SIDE_3Q;
        private float _yaw;
        private float _pitch;
        private float _distance = 6f;
        private float _height = 1.2f;
        private Color _bodyColor = new(0.055f, 0.065f, 0.08f, 1f);
        private Color _weaponColor = new(0.72f, 0.8f, 0.88f, 1f);

        private bool IsEldenRingMotion =>
            _character != null && AssetDatabase.GetAssetPath(_character) == EldenRingMotionImporter.PrefabPath;

        public static void Open()
        {
            var window = GetWindow<MotionBrowserWindow>();
            window.titleContent = new GUIContent("Motion Browser");
            window.minSize = new Vector2(620f, 680f);
            window.Show();
        }

        public static void OpenClip(AnimationClip clip)
        {
            var window = GetWindow<MotionBrowserWindow>();
            window.titleContent = new GUIContent("Elden Ring Motion Browser");
            window.SelectEldenRing();
            window._clip = clip;
            window.ReloadPreview();
            window.Show();
            window.Focus();
        }

        public static void OpenEldenRingMotion()
        {
            var window = GetWindow<MotionBrowserWindow>();
            window.titleContent = new GUIContent("Elden Ring Motion Browser");
            window.minSize = new Vector2(720f, 760f);
            window.SelectEldenRing();
            window._clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                EldenRingMotionImporter.GeneratedRoot + "/shadow_a023_030040.anim") ?? window._clip;
            window._silhouette = false;
            window.ReloadPreview();
            window._playing = true;
            window._lastEditorTime = EditorApplication.timeSinceStartup;
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            _session = new MotionPreviewSession();
            EditorApplication.update += EditorUpdate;
            LoadShadowCatalog();
            TryLoadFixture();
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
            SaveSelection();
            _session?.Dispose();
            _session = null;
        }

        private void TryLoadFixture()
        {
            EldenRingMotionImporter.EnsurePreviewPrefab();
            _character ??= AssetDatabase.LoadAssetAtPath<GameObject>(EldenRingMotionImporter.PrefabPath);
            _clip ??= AssetDatabase.LoadAssetAtPath<AnimationClip>(EldenRingMotionImporter.ClipPath);
            ReloadPreview();
        }

        private void ReloadPreview()
        {
            _playing = false;
            _time = 0f;
            _session?.Load(_character, _bodyColor, _weaponColor);
            if (_weapon != null && _session?.Instance != null)
                _session.AttachWeapon(_weapon, _weaponBone, _weaponPosition, _weaponEuler, _weaponScale);
            if (_clip != null && _session?.Instance != null)
                _session.FitCameraToClip(_clip, _rootMotion);
            LoadSelection();
            UpdatePose();
        }

        private void OnGUI()
        {
            HandleKeyboard();
            EditorGUILayout.LabelField("Prototype Motion Browser", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Source assets are never modified. Preview materials exist only in an isolated preview scene.", MessageType.Info);

            DrawMotionCatalog();

            EditorGUI.BeginChangeCheck();
            var character = (GameObject)EditorGUILayout.ObjectField("Character / Prefab", _character, typeof(GameObject), true);
            var clip = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", _clip, typeof(AnimationClip), false);
            var weapon = (GameObject)EditorGUILayout.ObjectField("Weapon Prefab", _weapon, typeof(GameObject), false);
            var weaponBone = EditorGUILayout.TextField("Weapon Bone", _weaponBone);
            var weaponPosition = EditorGUILayout.Vector3Field("Weapon Local Position", _weaponPosition);
            var weaponEuler = EditorGUILayout.Vector3Field("Weapon Local Euler", _weaponEuler);
            var weaponScale = EditorGUILayout.Vector3Field("Weapon Local Scale", _weaponScale);
            if (EditorGUI.EndChangeCheck())
            {
                SaveSelection();
                _character = character;
                _clip = clip;
                _weapon = weapon;
                _weaponBone = weaponBone;
                _weaponPosition = weaponPosition;
                _weaponEuler = weaponEuler;
                _weaponScale = weaponScale;
                ReloadPreview();
            }

            var previewHeight = Mathf.Clamp(position.height * 0.38f, 220f, 390f);
            var previewRect = GUILayoutUtility.GetRect(100f, previewHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(previewRect, new Color(0.17f, 0.18f, 0.2f));
            if (_session?.Instance != null)
            {
                var render = _session.RenderPreview(Mathf.Max(16, (int)previewRect.width), Mathf.Max(16, (int)previewRect.height), _showTrail);
                if (render != null) GUI.DrawTexture(previewRect, render, ScaleMode.ScaleToFit, true);
            }
            else
            {
                GUI.Label(previewRect, "Assign a character prefab and AnimationClip.", EditorStyles.centeredGreyMiniLabel);
            }

            DrawTimeline();
            DrawControls();
            DrawOptions();
            DrawSelectedFrames();
        }

        private void DrawMotionCatalog()
        {
            EditorGUILayout.LabelField("Characters / Animations", EditorStyles.boldLabel);
            if (GUILayout.Button("Elden Ring")) SelectEldenRing();

            if (_character == null) return;
            var assetPath = AssetDatabase.GetAssetPath(_character);
            var clips = (assetPath == EldenRingMotionImporter.PrefabPath
                    ? AssetDatabase.FindAssets("t:AnimationClip", new[] { EldenRingMotionImporter.GeneratedRoot })
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Select(path => AssetDatabase.LoadAssetAtPath<AnimationClip>(path))
                    : AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>())
                .Where(item => item != null)
                .Where(item => !item.name.StartsWith("__preview__", StringComparison.Ordinal))
                .OrderBy(item => item.name)
                .ToArray();
            if (clips.Length == 0) return;

            EditorGUILayout.BeginHorizontal();
            _animationSearch = EditorGUILayout.TextField("Search", _animationSearch);
            _animationFilter = (MotionBrowserFilter)EditorGUILayout.EnumPopup(_animationFilter, GUILayout.Width(105f));
            EditorGUILayout.EndHorizontal();
            _animationScroll = EditorGUILayout.BeginScrollView(_animationScroll, GUILayout.Height(105f));
            foreach (var item in clips.Where(ClipMatchesFilter))
            {
                var selected = item == _clip;
                using (new EditorGUI.DisabledScope(selected))
                    if (GUILayout.Button($"{DescribeClip(item)}    {item.length:0.000}s @ {item.frameRate:0.##} FPS", EditorStyles.miniButton))
                    {
                        SaveSelection();
                        _clip = item;
                        ReloadPreview();
                    }
            }
            EditorGUILayout.EndScrollView();

            if (assetPath == EldenRingMotionImporter.PrefabPath)
                DrawShadowCatalog();
        }

        private void LoadShadowCatalog()
        {
            var path = Path.Combine(ProjectPaths.RepositoryRoot, "catalog", "shadow_browser_catalog.json");
            if (!File.Exists(path)) return;
            _shadowCatalog = JsonUtility.FromJson<ShadowBrowserCatalog>(File.ReadAllText(path));
            if (_shadowCatalog?.entries == null) return;
            _shadowGroups = new[] { "All" }.Concat(_shadowCatalog.entries
                    .Select(entry => entry.group).Distinct().OrderBy(group => group, StringComparer.Ordinal))
                .ToArray();
            _shadowGroupIndex = Array.IndexOf(_shadowGroups, "a023");
            if (_shadowGroupIndex < 0) _shadowGroupIndex = 0;
        }

        private void DrawShadowCatalog()
        {
            if (_shadowCatalog?.entries == null) return;
            var weaponCount = _shadowCatalog.entries.Count(entry =>
                int.TryParse(entry.group.Substring(1), out var group) && group >= 20 && group <= 62);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                $"Shadow Repository: {_shadowCatalog.entries.Length:N0} motions " +
                $"({weaponCount:N0} weapon / {_shadowCatalog.entries.Length - weaponCount:N0} Ash)",
                EditorStyles.boldLabel);
            _shadowGroupIndex = EditorGUILayout.Popup("Motion Group", _shadowGroupIndex, _shadowGroups);
            var selectedGroup = _shadowGroups[Mathf.Clamp(_shadowGroupIndex, 0, _shadowGroups.Length - 1)];
            var visible = _shadowCatalog.entries.Where(entry =>
                    selectedGroup == "All" || entry.group == selectedGroup)
                .Where(entry => string.IsNullOrWhiteSpace(_animationSearch) ||
                                entry.id.IndexOf(_animationSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                entry.source_animation.IndexOf(_animationSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(250)
                .ToArray();
            _shadowScroll = EditorGUILayout.BeginScrollView(_shadowScroll, GUILayout.Height(150f));
            foreach (var entry in visible)
            {
                var clipPath = $"{EldenRingMotionImporter.GeneratedRoot}/{entry.clip_name}.anim";
                var prepared = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                var label = prepared == null
                    ? $"Load  {entry.id}   [{entry.source_animation}]"
                    : $"Play   {entry.id}   {prepared.length:0.000}s @ {prepared.frameRate:0.##} FPS";
                if (!GUILayout.Button(label, EditorStyles.miniButton)) continue;
                if (prepared == null)
                    prepared = PrepareShadowEntry(entry);
                if (prepared == null) continue;
                SaveSelection();
                _clip = prepared;
                ReloadPreview();
                _playing = true;
                _lastEditorTime = EditorApplication.timeSinceStartup;
            }
            EditorGUILayout.EndScrollView();
            if (selectedGroup == "All" && visible.Length == 250)
                EditorGUILayout.LabelField("Showing first 250. Choose a group or enter Search to narrow the list.", EditorStyles.miniLabel);
        }

        private AnimationClip PrepareShadowEntry(ShadowCatalogEntry entry)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Shadow Motion", $"Decoding {entry.id}...", 0.25f);
                var script = Path.Combine(ProjectPaths.RepositoryRoot, "motion_pipeline.py");
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = $"\"{script}\" prepare-shadow \"{entry.id}\"",
                        WorkingDirectory = ProjectPaths.RepositoryRoot,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };
                process.Start();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"Decoder exited with code {process.ExitCode}. See the Unity console.");
                EditorUtility.DisplayProgressBar("Shadow Motion", $"Importing {entry.id} into Unity...", 0.7f);
                EldenRingMotionImporter.Import();
                return AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    $"{EldenRingMotionImporter.GeneratedRoot}/{entry.clip_name}.anim");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Shadow Motion", $"Failed to prepare {entry.id}.\n\n{exception.Message}", "OK");
                return null;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void SelectEldenRing()
        {
            SaveSelection();
            EldenRingMotionImporter.EnsurePreviewPrefab();
            _character = AssetDatabase.LoadAssetAtPath<GameObject>(EldenRingMotionImporter.PrefabPath);
            _clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(EldenRingMotionImporter.ClipPath);
            _weapon = null;
            _rootMotion = false;
            _silhouette = false;
            ReloadPreview();
        }

        private bool ClipMatchesFilter(AnimationClip item)
        {
            var description = DescribeClip(item);
            if (!string.IsNullOrWhiteSpace(_animationSearch) &&
                description.IndexOf(_animationSearch, StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (_animationFilter == MotionBrowserFilter.All) return true;
            var value = item.name.ToLowerInvariant();
            if (value.StartsWith("a", StringComparison.Ordinal) && value.Contains("_"))
            {
                var suffix = value[(value.IndexOf('_') + 1)..];
                if (suffix.Length == 6 && int.TryParse(suffix[^3..], out var action))
                {
                    if (_animationFilter == MotionBrowserFilter.Attack)
                        return action < 100 || action is >= 300 and < 400 || action is >= 500 and < 700 || action >= 900;
                    if (_animationFilter == MotionBrowserFilter.Skill &&
                        int.TryParse(value.Substring(1, 3), out var category))
                        return category is >= 600 and < 900;
                }
            }
            return _animationFilter switch
            {
                MotionBrowserFilter.Attack => ContainsAny(value, "attack", "melee", "slash", "stab", "chop"),
                MotionBrowserFilter.Dodge => ContainsAny(value, "dodge", "evade"),
                MotionBrowserFilter.Guard => ContainsAny(value, "guard", "block"),
                MotionBrowserFilter.Parry => ContainsAny(value, "parry", "counter"),
                MotionBrowserFilter.Hit => ContainsAny(value, "hit", "damage", "stagger"),
                MotionBrowserFilter.Movement => ContainsAny(value, "walk", "run", "strafe", "jump"),
                MotionBrowserFilter.Skill => ContainsAny(value, "skill", "spell", "cast"),
                MotionBrowserFilter.Unknown => !ContainsAny(value, "attack", "melee", "slash", "stab", "chop", "dodge", "evade", "guard", "block", "parry", "counter", "hit", "damage", "stagger", "walk", "run", "strafe", "jump", "skill", "spell", "cast"),
                _ => true
            };
        }

        private static bool ContainsAny(string value, params string[] terms) => terms.Any(value.Contains);

        private static string DescribeClip(AnimationClip clip)
        {
            var name = clip.name;
            if (!name.StartsWith("a", StringComparison.Ordinal) || name.Length < 11 || name[4] != '_') return name;
            if (!int.TryParse(name.Substring(1, 3), out var category) ||
                !int.TryParse(name[^3..], out var action)) return name;
            var weapon = category switch
            {
                20 => "Dagger", 21 => "Torch", 22 => "Claw", 23 => "Straight Sword", 24 => "Twinblade",
                25 => "Greatsword", 26 => "Colossal Sword", 27 => "Thrusting Sword", 28 => "Curved Sword",
                29 => "Katana", 30 => "Axe", 31 => "Colossal Weapon", 32 => "Greataxe", 33 => "Hammer",
                34 => "Flail", 35 => "Great Hammer", 36 => "Spear", 37 => "Great Spear", 38 => "Halberd",
                39 => "Heavy Thrusting Sword", 40 => "Curved Greatsword", 41 => "Catalyst", 42 => "Fist",
                43 => "Whip", 44 => "Bow", 45 => "Greatbow", 46 => "Crossbow", 47 => "Greatshield",
                48 => "Small Shield", 49 => "Medium Shield", 50 => "Scythe", 51 => "Light Bow", 52 => "Ballista",
                53 => "Smithscript Dagger", 55 => "Hand-to-Hand", 56 => "Perfume Bottle", 57 => "Thrusting Shield",
                58 => "Backhand Blade", 60 => "Light Greatsword", 61 => "Great Katana", 62 => "Beast Claw",
                >= 600 and < 900 => "Ash of War",
                _ => $"a{category:000}"
            };
            var actionName = action switch
            {
                < 100 => "Light/Normal",
                >= 300 and < 400 => "Heavy/Charged",
                >= 500 and < 600 => "Running",
                >= 600 and < 700 => "Jumping",
                >= 900 => "Evasion Attack",
                _ => "Motion"
            };
            return $"{weapon} · {actionName} · {name}";
        }

        private void DrawTimeline()
        {
            if (_clip == null) return;
            var fps = Mathf.Max(1f, _clip.frameRate);
            var maxFrame = Mathf.Max(1, Mathf.CeilToInt(_clip.length * fps));
            var currentFrame = Mathf.Clamp(Mathf.RoundToInt(_time * fps), 0, maxFrame);
            EditorGUILayout.Space(4f);
            var timelineRect = GUILayoutUtility.GetRect(10f, 34f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(timelineRect, new Color(0.1f, 0.105f, 0.12f));
            Handles.BeginGUI();
            for (var boundary = 0f; boundary <= _clip.length + 0.0001f; boundary += Mathf.Max(0.01f, _turnDuration))
            {
                var x = timelineRect.x + boundary / Mathf.Max(0.001f, _clip.length) * timelineRect.width;
                Handles.color = new Color(0.3f, 0.75f, 1f, 0.85f);
                Handles.DrawLine(new Vector3(x, timelineRect.y), new Vector3(x, timelineRect.yMax));
                GUI.Label(new Rect(x + 3f, timelineRect.y + 1f, 75f, 17f), $"{boundary:0.0}s", EditorStyles.miniLabel);
            }
            Handles.EndGUI();

            EditorGUI.BeginChangeCheck();
            var nextTime = EditorGUILayout.Slider(_time, 0f, _clip.length);
            if (EditorGUI.EndChangeCheck())
            {
                _time = nextTime;
                _playing = false;
                UpdatePose();
            }
            EditorGUILayout.LabelField($"Frame: {currentFrame} / {maxFrame}    Time: {_time:0.0000} / {_clip.length:0.0000} sec    FPS: {fps:0.##}");
        }

        private void DrawControls()
        {
            using (new EditorGUI.DisabledScope(_clip == null || _character == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Restart")) SetFrame(0);
                if (GUILayout.Button("Previous Frame")) Step(-1);
                if (GUILayout.Button(_playing ? "Pause" : "Play")) TogglePlay();
                if (GUILayout.Button("Next Frame")) Step(1);
                if (GUILayout.Button("Add Frame")) AddCurrentFrame();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawOptions()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            if (!IsEldenRingMotion)
                _silhouette = EditorGUILayout.Toggle("Silhouette Preview", _silhouette);
            else
                _silhouette = false;
            _rootMotion = EditorGUILayout.Toggle("Root Motion", _rootMotion);
            _showTrail = EditorGUILayout.Toggle("Show Weapon Tip Trail", _showTrail);
            if (!IsEldenRingMotion)
            {
                _bodyColor = EditorGUILayout.ColorField("Body", _bodyColor);
                _weaponColor = EditorGUILayout.ColorField("Weapon", _weaponColor);
            }
            _preset = (CameraPreset)EditorGUILayout.EnumPopup("Camera Preset", _preset);
            _orthographic = EditorGUILayout.Toggle("Orthographic", _orthographic);
            _yaw = EditorGUILayout.Slider("Yaw Offset", _yaw, -90f, 90f);
            _pitch = EditorGUILayout.Slider("Pitch Offset", _pitch, -30f, 45f);
            _distance = EditorGUILayout.Slider("Distance", _distance, 2f, 12f);
            _height = EditorGUILayout.Slider("Height", _height, 0f, 3f);
            _turnDuration = Mathf.Max(0.01f, EditorGUILayout.FloatField("Turn Duration", _turnDuration));
            if (!IsEldenRingMotion)
                _resolution = EditorGUILayout.IntPopup("Resolution", _resolution, new[] { "512", "1024" }, new[] { 512, 1024 });
            if (EditorGUI.EndChangeCheck()) UpdatePose();
            if (!_silhouette)
                EditorGUILayout.HelpBox("Original materials are visible only on the disposable preview clone. PNG export always forces silhouette materials.", MessageType.Info);
        }

        private void DrawSelectedFrames()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Selected Frames", EditorStyles.boldLabel);
            _selectionScroll = EditorGUILayout.BeginScrollView(_selectionScroll, GUILayout.Height(76f));
            var remove = -1;
            for (var i = 0; i < _selected.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Frame {_selected[i].frame}    {_selected[i].time:0.000} sec");
                if (GUILayout.Button("Remove", GUILayout.Width(70f))) remove = i;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            if (remove >= 0)
            {
                _selected.RemoveAt(remove);
                SaveSelection();
            }

            if (!IsEldenRingMotion)
            {
                using (new EditorGUI.DisabledScope(_selected.Count == 0 || _session?.Instance == null || _clip == null))
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Export Selected Frames")) ExportSelected();
                    if (GUILayout.Button("Export Full Sequence")) ExportFull();
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.LabelField("Shortcuts (window focused): Space Play/Pause, Left/Right Step, A Add Frame", EditorStyles.miniLabel);
        }

        private void EditorUpdate()
        {
            if (!_playing || _clip == null) return;
            var now = EditorApplication.timeSinceStartup;
            _time += (float)(now - _lastEditorTime);
            _lastEditorTime = now;
            if (_time > _clip.length) _time = 0f;
            UpdatePose();
            Repaint();
        }

        private void HandleKeyboard()
        {
            var current = Event.current;
            if (focusedWindow != this || current.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;
            if (current.keyCode == KeyCode.Space) TogglePlay();
            else if (current.keyCode == KeyCode.LeftArrow) Step(-1);
            else if (current.keyCode == KeyCode.RightArrow) Step(1);
            else if (current.keyCode == KeyCode.A) AddCurrentFrame();
            else return;
            current.Use();
        }

        private void TogglePlay()
        {
            if (_clip == null) return;
            _playing = !_playing;
            _lastEditorTime = EditorApplication.timeSinceStartup;
        }

        private void Step(int direction)
        {
            if (_clip == null) return;
            _playing = false;
            var frame = Mathf.RoundToInt(_time * Mathf.Max(1f, _clip.frameRate)) + direction;
            SetFrame(frame);
        }

        private void SetFrame(int frame)
        {
            if (_clip == null) return;
            var fps = Mathf.Max(1f, _clip.frameRate);
            var maxFrame = Mathf.CeilToInt(_clip.length * fps);
            _time = Mathf.Clamp(frame, 0, maxFrame) / fps;
            _time = Mathf.Min(_time, _clip.length);
            _playing = false;
            UpdatePose();
            Repaint();
        }

        private void AddCurrentFrame()
        {
            if (_clip == null || _character == null) return;
            var fps = Mathf.Max(1f, _clip.frameRate);
            var frame = Mathf.RoundToInt(_time * fps);
            if (_selected.All(item => item.frame != frame))
                _selected.Add(new SelectedFrame { frame = frame, time = Mathf.Min(frame / fps, _clip.length) });
            _selected.Sort((left, right) => left.frame.CompareTo(right.frame));
            SaveSelection();
        }

        private void UpdatePose()
        {
            if (_session == null || _clip == null) return;
            if (IsEldenRingMotion) _silhouette = false;
            _session.SetSilhouette(_silhouette, _bodyColor, _weaponColor);
            _session.Sample(_clip, _time, _rootMotion);
            _session.UpdateTrail(_clip, _time, _rootMotion, _showTrail);
            _session.SetCamera(_preset, _yaw, _pitch, _distance, _height, _orthographic);
            Repaint();
        }

        private void ExportSelected()
        {
            var directory = MotionExporter.Export(_session, _character, _clip, _selected.Select(item => item.frame), _resolution, _turnDuration, _rootMotion);
            UpdatePose();
            Debug.Log($"Exported {_selected.Count} silhouette frames to {directory}");
            EditorUtility.RevealInFinder(directory);
        }

        private void ExportFull()
        {
            var maxFrame = Mathf.CeilToInt(_clip.length * Mathf.Max(1f, _clip.frameRate));
            var directory = MotionExporter.Export(_session, _character, _clip, Enumerable.Range(0, maxFrame + 1), _resolution, _turnDuration, _rootMotion);
            UpdatePose();
            Debug.Log($"Exported full sequence to {directory}");
            EditorUtility.RevealInFinder(directory);
        }

        private void LoadSelection()
        {
            _selected.Clear();
            if (_character == null || _clip == null) return;
            _selected.AddRange(MotionExporter.LoadSelection(_character.name, _clip.name));
        }

        private void SaveSelection()
        {
            if (_character == null || _clip == null) return;
            MotionExporter.SaveSelection(_character.name, _clip.name, _selected);
        }
    }
}
