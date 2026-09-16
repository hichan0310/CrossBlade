using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MotionPrototype
{
    internal sealed class MotionComposerWindow : EditorWindow
    {
        [SerializeField] private MotionSequenceAsset _sequence;
        private MotionPreviewSession _session;
        private AnimationClip _clipToAdd;
        private Vector2 _segmentsScroll;
        private float _time;
        private bool _playing;
        private bool _loop = true;
        private float _previewSpeed = 1f;
        private double _lastTime;
        private Vector3 _rootPosition;
        private Quaternion _rootRotation;
        private Vector2 _windowScroll;

        [MenuItem("CrossBlade/Motion Composer")]
        public static void Open() => GetWindow<MotionComposerWindow>("Motion Composer");

        public static void OpenForMove()
        {
            if (Selection.activeObject is MotionSequenceAsset recipe)
            {
                var window = GetWindow<MotionComposerWindow>("Motion Composer");
                window._sequence = recipe;
                window.ReloadPreview();
                window.Show();
            }
            else OpenWithClip(Selection.activeObject as AnimationClip);
        }

        [UnityEditor.Callbacks.OnOpenAsset]
        private static bool OpenSequenceAsset(int instanceId, int line)
        {
            var sequence = EditorUtility.InstanceIDToObject(instanceId) as MotionSequenceAsset;
            if (sequence == null) return false;
            EditorApplication.delayCall += () =>
            {
                if (sequence == null) return;
                var window = GetWindow<MotionComposerWindow>("Motion Composer");
                window._sequence = sequence;
                window.ReloadPreview();
                window.Show();
                window.Focus();
            };
            return true;
        }

        public static void OpenWithClip(AnimationClip clip)
        {
            var window = GetWindow<MotionComposerWindow>("Motion Composer");
            window.NewSequence();
            window.AddClip(clip);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            _session = new MotionPreviewSession();
            EditorApplication.update += UpdatePlayback;
            _lastTime = EditorApplication.timeSinceStartup;
            if (_sequence != null) ReloadPreview();
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdatePlayback;
            _session?.Dispose();
        }

        private void EnsureSequence()
        {
            if (_sequence != null) return;
            _sequence = CreateInstance<MotionSequenceAsset>();
            _sequence.name = "저장하지 않은 모션 편집";
            _sequence.previewCharacter = AssetDatabase.LoadAssetAtPath<GameObject>(EldenRingMotionImporter.PrefabPath);
            ReloadPreview();
        }

        private void AddClip(AnimationClip clip)
        {
            if (clip == null) return;
            EnsureSequence();
            Undo.RecordObject(_sequence, "Add motion segment");
            _sequence.segments.Add(new MotionSegment
            {
                label = clip.name,
                clip = clip,
                firstFrame = 0,
                lastFrame = Mathf.RoundToInt(clip.length * Mathf.Max(1, clip.frameRate)),
                speed = 1f,
            });
            EditorUtility.SetDirty(_sequence);
            ReloadPreview();
        }

        private void ReloadPreview()
        {
            _playing = false;
            _time = 0;
            if (_session == null || _sequence == null || _sequence.previewCharacter == null) return;
            _session.Load(_sequence.previewCharacter, new Color(.06f, .07f, .09f), new Color(.75f, .8f, .9f));
            _rootPosition = _session.Instance.transform.localPosition;
            _rootRotation = _session.Instance.transform.localRotation;
            _session.SetSilhouette(false, Color.black, Color.white);
            UpdatePose();
        }

        private void OnGUI()
        {
            EnsureSequence();
            EditorGUILayout.LabelField("Motion Composer · 자르기 / 배속 / .anim 내보내기", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("원본 클립의 시작·끝 프레임과 저장할 재생 속도를 조절한 뒤 .anim 내보내기를 누르세요. Move 생성 없이 애니메이션만 저장할 수 있습니다. 기존 편집 .asset도 불러올 수 있습니다.", MessageType.Info);
            using (new EditorGUI.DisabledScope(MotionSequenceCore.Duration(_sequence) <= 0 || _sequence.previewCharacter == null))
            {
                if (GUILayout.Button(".anim 내보내기 · 자르기 / 배속 적용", GUILayout.Height(32))) Bake();
                if (GUILayout.Button("Move 만들기 · 전투 그래프에 추가", GUILayout.Height(32))) CreateMove();
            }
            _windowScroll = EditorGUILayout.BeginScrollView(_windowScroll);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var sequence = (MotionSequenceAsset)EditorGUILayout.ObjectField("저장한 편집 (.asset)", _sequence, typeof(MotionSequenceAsset), false);
            if (EditorGUI.EndChangeCheck()) { _sequence = sequence; ReloadPreview(); }
            if (GUILayout.Button("새 편집", GUILayout.Width(65))) NewSequence();
            if (GUILayout.Button("편집 저장", GUILayout.Width(75))) SaveSequence();
            EditorGUILayout.EndHorizontal();
            EnsureSequence();
            EditorGUI.BeginChangeCheck();
            _sequence.previewCharacter = (GameObject)EditorGUILayout.ObjectField("Preview character", _sequence.previewCharacter, typeof(GameObject), false);
            _sequence.outputFps = EditorGUILayout.IntSlider("Output FPS", _sequence.outputFps, 1, 120);
            _sequence.keepRootMotion = EditorGUILayout.Toggle("Keep root motion", _sequence.keepRootMotion);
            if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(_sequence); ReloadPreview(); }

            EditorGUILayout.BeginHorizontal();
            _clipToAdd = (AnimationClip)EditorGUILayout.ObjectField("원본 애니메이션", _clipToAdd, typeof(AnimationClip), false);
            using (new EditorGUI.DisabledScope(_clipToAdd == null))
                if (GUILayout.Button("추가", GUILayout.Width(55))) { AddClip(_clipToAdd); _clipToAdd = null; }
            EditorGUILayout.EndHorizontal();
            DrawSegments();
            DrawPreview();
            DrawTimeline();
            EditorGUILayout.EndScrollView();
        }

        private void CreateMove()
        {
            var name = _sequence.segments.FirstOrDefault(item => item?.clip != null)?.clip.name ?? "NewMove";
            var path = EditorUtility.SaveFilePanelInProject("Move 만들기", name, "prefab", "같은 폴더에 애니메이션과 Move를 저장합니다.");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var move = MoveAssetCreation.CreateFromMotion(_sequence, path);
                Selection.activeObject = move.gameObject;
                EditorApplication.delayCall += () => MoveGraphWindow.OpenWithCreatedMove(move);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Move 생성 실패", exception.Message, "확인");
            }
        }

        private void DrawSegments()
        {
            EditorGUILayout.LabelField($"애니메이션 구간 · 총 {MotionSequenceCore.Duration(_sequence):0.000}초", EditorStyles.boldLabel);
            _segmentsScroll = EditorGUILayout.BeginScrollView(_segmentsScroll, GUILayout.Height(Mathf.Min(230, 72 + _sequence.segments.Count * 72)));
            for (var index = 0; index < _sequence.segments.Count; index++)
            {
                var segment = _sequence.segments[index];
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                segment.label = EditorGUILayout.TextField(segment.label);
                segment.clip = (AnimationClip)EditorGUILayout.ObjectField(segment.clip, typeof(AnimationClip), false);
                if (GUILayout.Button("↑", GUILayout.Width(25)) && index > 0) Move(index, index - 1);
                if (GUILayout.Button("↓", GUILayout.Width(25)) && index + 1 < _sequence.segments.Count) Move(index, index + 1);
                if (GUILayout.Button("×", GUILayout.Width(25))) { Undo.RecordObject(_sequence, "Remove segment"); _sequence.segments.RemoveAt(index--); EditorUtility.SetDirty(_sequence); }
                EditorGUILayout.EndHorizontal();
                if (segment.clip != null)
                {
                    segment.firstFrame = EditorGUILayout.IntSlider("시작 프레임", segment.firstFrame, 0, segment.MaximumFrame);
                    segment.lastFrame = EditorGUILayout.IntSlider("끝 프레임", segment.lastFrame <= 0 ? segment.MaximumFrame : segment.lastFrame, segment.firstFrame, segment.MaximumFrame);
                    EditorGUILayout.BeginHorizontal();
                    segment.speed = Mathf.Max(.01f, EditorGUILayout.FloatField("저장할 재생 속도", segment.speed));
                    segment.blendFrames = EditorGUILayout.IntSlider("Blend frames", segment.blendFrames, 0,
                        Mathf.Min(segment.lastFrame - segment.firstFrame, _sequence.outputFps));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.LabelField($"Source {segment.firstFrame / segment.Rate:0.000}–{segment.lastFrame / segment.Rate:0.000}s → {segment.Duration:0.000}s", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
            if (GUI.changed) { EditorUtility.SetDirty(_sequence); _time = Mathf.Min(_time, MotionSequenceCore.Duration(_sequence)); UpdatePose(); }
        }

        private void DrawPreview()
        {
            var rect = GUILayoutUtility.GetRect(100, Mathf.Clamp(position.height * .32f, 180, 340), GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(.18f, .19f, .21f));
            var texture = _session?.RenderPreview(Mathf.Max(16, (int)rect.width), Mathf.Max(16, (int)rect.height), false);
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
        }

        private void DrawTimeline()
        {
            var duration = MotionSequenceCore.Duration(_sequence);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_playing ? "Pause" : "Play", GUILayout.Width(70))) { _playing = !_playing; _lastTime = EditorApplication.timeSinceStartup; }
            if (GUILayout.Button("Stop", GUILayout.Width(55))) { _playing = false; _time = 0; UpdatePose(); }
            _loop = GUILayout.Toggle(_loop, "Loop", GUILayout.Width(55));
            _previewSpeed = EditorGUILayout.Slider(_previewSpeed, .05f, 3f);
            EditorGUILayout.EndHorizontal();
            EditorGUI.BeginChangeCheck();
            var time = EditorGUILayout.Slider("미리보기 시간", _time, 0, Mathf.Max(.001f, duration));
            if (EditorGUI.EndChangeCheck()) { _playing = false; _time = time; UpdatePose(); }
            var frame = Mathf.RoundToInt(_time * _sequence.outputFps);
            EditorGUILayout.LabelField($"Frame {frame} · {_time:0.000}/{duration:0.000}s · {_sequence.outputFps} FPS", EditorStyles.miniLabel);
        }

        private void UpdatePose()
        {
            if (_session?.Instance == null || _sequence == null) return;
            _session.ResetPose();
            MotionSequenceCore.Sample(_sequence, _session.Instance, _time);
            if (!_sequence.keepRootMotion)
            {
                _session.Instance.transform.localPosition = _rootPosition;
                _session.Instance.transform.localRotation = _rootRotation;
            }
            Repaint();
        }

        private void UpdatePlayback()
        {
            var now = EditorApplication.timeSinceStartup;
            if (_playing)
            {
                var duration = MotionSequenceCore.Duration(_sequence);
                _time += (float)(now - _lastTime) * _previewSpeed;
                if (_time > duration) { if (_loop && duration > 0) _time %= duration; else { _time = duration; _playing = false; } }
                UpdatePose();
            }
            _lastTime = now;
        }

        private void Move(int from, int to)
        {
            Undo.RecordObject(_sequence, "Reorder segment");
            var item = _sequence.segments[from]; _sequence.segments.RemoveAt(from); _sequence.segments.Insert(to, item);
            EditorUtility.SetDirty(_sequence);
        }

        private void NewSequence()
        {
            _sequence = CreateInstance<MotionSequenceAsset>();
            _sequence.name = "저장하지 않은 모션 편집";
            _sequence.previewCharacter = AssetDatabase.LoadAssetAtPath<GameObject>(EldenRingMotionImporter.PrefabPath);
            ReloadPreview();
        }

        private void SaveSequence()
        {
            if (AssetDatabase.Contains(_sequence)) { AssetDatabase.SaveAssets(); return; }
            var path = EditorUtility.SaveFilePanelInProject("모션 편집 저장", "MotionEdit", "asset", "나중에 다시 편집할 프레임 범위와 속도를 저장합니다.");
            if (string.IsNullOrEmpty(path)) return;
            AssetDatabase.CreateAsset(_sequence, path); AssetDatabase.SaveAssets();
        }

        private void Bake()
        {
            var path = EditorUtility.SaveFilePanelInProject("Bake combined animation", _sequence.name, "anim", "Choose an output clip path.");
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null) { EditorUtility.DisplayDialog("Output exists", "Choose a new asset path.", "OK"); return; }
            var clip = MotionSequenceCore.Bake(_sequence, path);
            Selection.activeObject = clip; EditorGUIUtility.PingObject(clip);
        }
    }
}
