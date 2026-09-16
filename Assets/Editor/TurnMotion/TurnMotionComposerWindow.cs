using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Scripts.TurnMotion;
using UnityEditor;
using UnityEngine;

namespace CrossBlade.EditorTools
{
    public class TurnMotionComposerWindow : EditorWindow
    {
        private AnimationClip _sourceClip;
        private GameObject _previewPrefab;
        private GameObject _defaultVfxPrefab;
        private TurnMotionDefinition _definition;
        private AnimationClip _generatedClip;
        private GameObject _generatedActionPrefab;
        private readonly List<TurnMotionPoint> _points = new List<TurnMotionPoint>();
        private string _actionName = "SwordAttack_Turn";
        private string _outputFolder = "Assets/Generated/TurnMotions";
        private float _turnDuration = 0.5f;
        private float _outputFps = 30f;
        private float _time;
        private bool _playing;
        private bool _previewGenerated;
        private bool _simulateVfx = true;
        private double _lastEditorTime;
        private Vector2 _pointsScroll;
        private PreviewRenderUtility _preview;
        private GameObject _previewInstance;
        private GameObject _previewPrefabLoaded;

        [MenuItem("Tools/CrossBlade/Turn Motion Composer")]
        public static void Open()
        {
            var window = GetWindow<TurnMotionComposerWindow>();
            window.titleContent = new GUIContent("Turn Motion Composer");
            window.minSize = new Vector2(760f, 720f);
            window.Show();
        }

        private AnimationClip ActivePreviewClip => _previewGenerated && _generatedClip != null
            ? _generatedClip
            : _sourceClip;

        private GameObject DesiredPreviewPrefab => _previewGenerated && _generatedActionPrefab != null
            ? _generatedActionPrefab
            : _previewPrefab;

        private void OnEnable()
        {
            EditorApplication.update += EditorUpdate;
            EnsurePreview();
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
            CleanupPreview();
        }

        private void EditorUpdate()
        {
            if (!_playing || ActivePreviewClip == null) return;
            double now = EditorApplication.timeSinceStartup;
            float delta = _lastEditorTime <= 0d ? 0f : (float)(now - _lastEditorTime);
            _lastEditorTime = now;
            _time += delta;
            if (_time > ActivePreviewClip.length)
                _time = 0f;
            SamplePreview();
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Turn Motion Composer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Select source-time points. Every interval between adjacent points is time-warped to the same " +
                "duration, and all intervals are packed into one turn. Generated Part prefabs keep VFX editable.",
                MessageType.Info);

            DrawInputs();
            DrawPreview();
            DrawTransport();
            DrawTimeline();
            DrawPointEditor();
            DrawSegments();
            DrawGeneration();
        }

        private void DrawInputs()
        {
            EditorGUI.BeginChangeCheck();
            AnimationClip source = (AnimationClip)EditorGUILayout.ObjectField(
                "Source Animation", _sourceClip, typeof(AnimationClip), false);
            GameObject previewPrefab = (GameObject)EditorGUILayout.ObjectField(
                "Character Prefab", _previewPrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                bool sourceChanged = source != _sourceClip;
                _sourceClip = source;
                _previewPrefab = previewPrefab;
                if (sourceChanged)
                {
                    _generatedClip = null;
                    _previewGenerated = false;
                    _points.Clear();
                    _time = 0f;
                    if (_sourceClip != null)
                    {
                        _actionName = TurnMotionBuilder.SanitizeName(_sourceClip.name + "_Turn");
                        _points.Add(new TurnMotionPoint { label = "Start", sourceTime = 0f });
                        _points.Add(new TurnMotionPoint { label = "End", sourceTime = _sourceClip.length });
                    }
                }
                RebuildPreviewInstance();
            }

            _definition = (TurnMotionDefinition)EditorGUILayout.ObjectField(
                "Definition", _definition, typeof(TurnMotionDefinition), false);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Load Definition", GUILayout.Width(120f))) LoadDefinition();
            if (GUILayout.Button("Use Full Clip", GUILayout.Width(110f))) UseFullClip();
            if (GUILayout.Button("Clear Points", GUILayout.Width(100f))) _points.Clear();
            EditorGUILayout.EndHorizontal();
            _turnDuration = Mathf.Max(0.01f, EditorGUILayout.FloatField("Turn Duration", _turnDuration));
            _outputFps = Mathf.Max(1f, EditorGUILayout.FloatField("Output FPS", _outputFps));
            _defaultVfxPrefab = (GameObject)EditorGUILayout.ObjectField(
                "Default Part VFX", _defaultVfxPrefab, typeof(GameObject), false);
        }

        private void DrawPreview()
        {
            Rect rect = GUILayoutUtility.GetRect(100f, 310f, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            if (_preview == null || _previewInstance == null)
            {
                EditorGUI.DrawRect(rect, new Color(0.07f, 0.075f, 0.085f));
                GUI.Label(rect, "Assign a Character Prefab to preview the motion.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            _preview.BeginPreview(rect, GUIStyle.none);
            _preview.camera.Render();
            Texture result = _preview.EndPreview();
            GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
        }

        private void DrawTransport()
        {
            AnimationClip clip = ActivePreviewClip;
            using (new EditorGUI.DisabledScope(clip == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Previous Frame")) StepFrame(-1);
                if (GUILayout.Button(_playing ? "Pause" : "Play"))
                {
                    _playing = !_playing;
                    _lastEditorTime = EditorApplication.timeSinceStartup;
                }
                if (GUILayout.Button("Next Frame")) StepFrame(1);
                if (GUILayout.Button("Add Point") && !_previewGenerated) AddCurrentPoint();
                EditorGUILayout.EndHorizontal();
                bool generated = EditorGUILayout.ToggleLeft("Preview generated one-turn clip", _previewGenerated);
                if (generated != _previewGenerated)
                {
                    _previewGenerated = generated && _generatedClip != null;
                    _time = 0f;
                    RebuildPreviewInstance();
                }
                _simulateVfx = EditorGUILayout.ToggleLeft("Simulate Part VFX while scrubbing action prefab", _simulateVfx);
                float fps = clip != null && clip.frameRate > 0f ? clip.frameRate : _outputFps;
                int frame = clip != null ? Mathf.RoundToInt(_time * fps) : 0;
                int total = clip != null ? Mathf.RoundToInt(clip.length * fps) : 0;
                EditorGUILayout.LabelField($"Frame: {frame} / {total}    Time: {_time:0.0000} / {(clip != null ? clip.length : 0f):0.0000}s");
            }
        }

        private void DrawTimeline()
        {
            AnimationClip clip = ActivePreviewClip;
            if (clip == null) return;
            Rect rect = GUILayoutUtility.GetRect(40f, 36f, GUILayout.ExpandWidth(true));
            Rect sliderRect = new Rect(rect.x, rect.y + 8f, rect.width, 18f);
            float next = GUI.HorizontalSlider(sliderRect, _time, 0f, clip.length);
            if (!Mathf.Approximately(next, _time))
            {
                _playing = false;
                _time = next;
                SamplePreview();
            }
            if (!_previewGenerated && _sourceClip != null)
            {
                foreach (TurnMotionPoint point in _points)
                {
                    float x = rect.x + (point.sourceTime / Mathf.Max(0.0001f, _sourceClip.length)) * rect.width;
                    EditorGUI.DrawRect(new Rect(x - 1f, rect.y + 2f, 2f, 28f), new Color(1f, 0.55f, 0.1f));
                }
            }
        }

        private void DrawPointEditor()
        {
            EditorGUILayout.LabelField("Selected Source Points", EditorStyles.boldLabel);
            _pointsScroll = EditorGUILayout.BeginScrollView(_pointsScroll, GUILayout.Height(125f));
            List<TurnMotionPoint> ordered = _points.OrderBy(point => point.sourceTime).ToList();
            foreach (TurnMotionPoint point in ordered)
            {
                EditorGUILayout.BeginHorizontal();
                point.label = EditorGUILayout.TextField(point.label, GUILayout.Width(150f));
                point.sourceTime = Mathf.Clamp(EditorGUILayout.FloatField(point.sourceTime, GUILayout.Width(85f)),
                    0f, _sourceClip != null ? _sourceClip.length : float.MaxValue);
                GUILayout.Label("sec", GUILayout.Width(28f));
                if (GUILayout.Button("Go", GUILayout.Width(42f)))
                {
                    _previewGenerated = false;
                    _time = point.sourceTime;
                    SamplePreview();
                }
                if (GUILayout.Button("Remove", GUILayout.Width(65f)))
                {
                    _points.Remove(point);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawSegments()
        {
            EditorGUILayout.LabelField("Equal Turn Segments", EditorStyles.boldLabel);
            if (_sourceClip == null || _points.Count < 2)
            {
                EditorGUILayout.LabelField("Add at least two distinct source points.", EditorStyles.miniLabel);
                return;
            }
            try
            {
                List<TurnMotionSegment> segments = TurnMotionBuilder.BuildSegments(_sourceClip, _points, _turnDuration);
                foreach (TurnMotionSegment segment in segments)
                {
                    EditorGUILayout.LabelField(
                        $"{segment.label}: source {segment.sourceStartTime:0.000}–{segment.sourceEndTime:0.000}s  →  " +
                        $"turn {segment.turnStartTime:0.000}–{segment.turnEndTime:0.000}s  " +
                        $"({segment.PlaybackSpeed:0.00}x)", EditorStyles.miniLabel);
                }
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Warning);
            }
        }

        private void DrawGeneration()
        {
            EditorGUILayout.Space(5f);
            _actionName = EditorGUILayout.TextField("Action Name", _actionName);
            _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);
            using (new EditorGUI.DisabledScope(_sourceClip == null || _points.Count < 2))
            {
                if (GUILayout.Button("Generate Clip + Definition + Action/Part Prefabs", GUILayout.Height(34f)))
                    GenerateAll();
            }
            if (_definition != null)
                EditorGUILayout.ObjectField("Generated Definition", _definition, typeof(TurnMotionDefinition), false);
            if (_generatedClip != null)
                EditorGUILayout.ObjectField("Generated Clip", _generatedClip, typeof(AnimationClip), false);
            if (_generatedActionPrefab != null)
            {
                EditorGUILayout.ObjectField(
                    "Generated Action", _generatedActionPrefab, typeof(GameObject), false);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select Action Prefab"))
                {
                    Selection.activeObject = _generatedActionPrefab;
                    EditorGUIUtility.PingObject(_generatedActionPrefab);
                }
                if (GUILayout.Button("Open Part Prefab Folder"))
                {
                    string actionPath = AssetDatabase.GetAssetPath(_generatedActionPrefab);
                    string directory = Path.GetDirectoryName(actionPath)?.Replace('\\', '/');
                    string partFolder = $"{directory}/{_generatedActionPrefab.name}_Parts";
                    UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(partFolder);
                    if (folder != null)
                    {
                        Selection.activeObject = folder;
                        EditorGUIUtility.PingObject(folder);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void GenerateAll()
        {
            try
            {
                string safeName = TurnMotionBuilder.SanitizeName(_actionName);
                string clipPath = $"{_outputFolder}/{safeName}.anim";
                string definitionPath = $"{_outputFolder}/{safeName}.asset";
                _generatedClip = TurnMotionBuilder.GenerateClip(
                    _sourceClip, _points, _turnDuration, _outputFps, clipPath);
                _definition = TurnMotionBuilder.SaveDefinition(
                    definitionPath, _sourceClip, _turnDuration, _outputFps, _points, _generatedClip, null);
                GameObject actionPrefab = TurnMotionBuilder.CreateActionPrefab(
                    _outputFolder, safeName, _previewPrefab, _defaultVfxPrefab, _definition, _generatedClip);
                _definition = TurnMotionBuilder.SaveDefinition(
                    definitionPath, _sourceClip, _turnDuration, _outputFps, _points, _generatedClip, actionPrefab);
                _generatedActionPrefab = actionPrefab;
                _previewGenerated = true;
                _time = 0f;
                RebuildPreviewInstance();
                Selection.activeObject = actionPrefab;
                EditorGUIUtility.PingObject(actionPrefab);
                Debug.Log($"TURN_MOTION_GENERATED: action={actionPrefab.name}, segments={_definition.Segments.Count}, " +
                          $"duration={_turnDuration:0.###}, output={_outputFolder}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Turn Motion Composer", exception.Message, "OK");
            }
        }

        private void LoadDefinition()
        {
            if (_definition == null) return;
            _sourceClip = _definition.SourceClip;
            _generatedClip = _definition.GeneratedClip;
            _generatedActionPrefab = _definition.ActionPrefab;
            _turnDuration = _definition.TurnDuration;
            _outputFps = _definition.OutputFps;
            _points.Clear();
            foreach (TurnMotionPoint point in _definition.Points)
                _points.Add(new TurnMotionPoint { label = point.label, sourceTime = point.sourceTime });
            _time = 0f;
            RebuildPreviewInstance();
        }

        private void UseFullClip()
        {
            if (_sourceClip == null) return;
            _points.Clear();
            _points.Add(new TurnMotionPoint { label = "Start", sourceTime = 0f });
            _points.Add(new TurnMotionPoint { label = "End", sourceTime = _sourceClip.length });
        }

        private void AddCurrentPoint()
        {
            if (_sourceClip == null) return;
            float snapped = Mathf.Round(_time * _sourceClip.frameRate) / Mathf.Max(1f, _sourceClip.frameRate);
            if (_points.Any(point => Mathf.Abs(point.sourceTime - snapped) < 0.0001f)) return;
            _points.Add(new TurnMotionPoint { label = $"Point {_points.Count + 1}", sourceTime = snapped });
            _points.Sort((a, b) => a.sourceTime.CompareTo(b.sourceTime));
        }

        private void StepFrame(int direction)
        {
            AnimationClip clip = ActivePreviewClip;
            if (clip == null) return;
            _playing = false;
            float fps = clip.frameRate > 0f ? clip.frameRate : _outputFps;
            _time = Mathf.Clamp(_time + direction / fps, 0f, clip.length);
            SamplePreview();
        }

        private void EnsurePreview()
        {
            if (_preview != null) return;
            _preview = new PreviewRenderUtility();
            _preview.cameraFieldOfView = 30f;
            _preview.lights[0].intensity = 1.2f;
            _preview.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
            _preview.lights[1].intensity = 0.6f;
        }

        private void RebuildPreviewInstance()
        {
            EnsurePreview();
            if (_previewInstance != null)
                UnityEngine.Object.DestroyImmediate(_previewInstance);
            _previewInstance = null;
            _previewPrefabLoaded = DesiredPreviewPrefab;
            if (DesiredPreviewPrefab == null) return;
            _previewInstance = UnityEngine.Object.Instantiate(DesiredPreviewPrefab);
            _previewInstance.hideFlags = HideFlags.HideAndDontSave;
            _preview.AddSingleGO(_previewInstance);
            FitCamera();
            SamplePreview();
        }

        private void SamplePreview()
        {
            if (_previewPrefabLoaded != DesiredPreviewPrefab) RebuildPreviewInstance();
            AnimationClip clip = ActivePreviewClip;
            if (_previewInstance == null || clip == null) return;
            TurnMotionAction action = _previewGenerated
                ? _previewInstance.GetComponent<TurnMotionAction>()
                : null;
            if (action != null)
                action.SetTime(_time, _simulateVfx);
            else
                clip.SampleAnimation(_previewInstance, Mathf.Clamp(_time, 0f, clip.length));
        }

        private void FitCamera()
        {
            if (_previewInstance == null) return;
            Renderer[] renderers = _previewInstance.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => !(renderer is ParticleSystemRenderer)).ToArray();
            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.up, Vector3.one * 2f);
            foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            float radius = Mathf.Max(0.5f, bounds.extents.magnitude);
            Vector3 direction = new Vector3(1.5f, 0.65f, 2.4f).normalized;
            _preview.camera.transform.position = bounds.center + direction * radius * 3.1f;
            _preview.camera.transform.LookAt(bounds.center + Vector3.up * bounds.extents.y * 0.05f);
            _preview.camera.nearClipPlane = Mathf.Max(0.01f, radius * 0.02f);
            _preview.camera.farClipPlane = radius * 10f;
        }

        private void CleanupPreview()
        {
            if (_previewInstance != null) UnityEngine.Object.DestroyImmediate(_previewInstance);
            _previewInstance = null;
            _preview?.Cleanup();
            _preview = null;
        }
    }
}
