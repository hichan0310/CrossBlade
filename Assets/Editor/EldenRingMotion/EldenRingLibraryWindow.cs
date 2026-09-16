using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MotionPrototype
{
    [Serializable] internal sealed class IndexedMotion
    {
        public string id;
        public string group;
        public string weapon;
        public string category;
        public string provenance;
        public int byte_size;
    }

    [Serializable] internal sealed class IndexedMotionCatalog
    {
        public int format_version;
        public IndexedMotion[] entries;
    }

    [Serializable] internal sealed class MotionShortlist
    {
        public List<string> ids = new();
    }

    internal sealed class EldenRingLibraryWindow : EditorWindow
    {
        private static readonly Color BodyColor = new(0.055f, 0.065f, 0.08f, 1f);
        private static readonly Color WeaponColor = new(0.72f, 0.8f, 0.88f, 1f);

        private IndexedMotionCatalog _catalog;
        private string _search = "";
        private string[] _weapons = { "All" };
        private string[] _weaponLabels = { "전체" };
        private int _weapon;
        private string[] _groups = { "All" };
        private int _group;
        private string[] _categories = { "All" };
        private int _category;
        private Vector2 _scroll;
        private Vector2 _windowScroll;
        private Vector2 _reviewScroll;
        private MotionPreviewSession _session;
        private GameObject _character;
        private AnimationClip _clip;
        private string _currentId;
        private readonly HashSet<string> _shortlist = new(StringComparer.Ordinal);
        private bool _playing;
        private bool _autoNext;
        private bool _shortlistOnly;
        private bool _rootMotion;
        private bool _showTrail;
        private float _time;
        private float _speed = 1f;
        private int _firstFrame, _lastFrame;
        private bool _rangeOnly = true;
        private readonly Dictionary<string, (int first, int last, float speed)> _edits = new();
        private float ClipRate => _clip == null ? 30f : Mathf.Max(1f, _clip.frameRate);
        private int MaximumFrame => _clip == null ? 0 : Mathf.RoundToInt(_clip.length * ClipRate);
        private float RangeStart => _rangeOnly ? _firstFrame / ClipRate : 0f;
        private float RangeEnd => _rangeOnly ? _lastFrame / ClipRate : _clip.length;
        private float PreviewDuration => Mathf.Max(0f, RangeEnd - RangeStart) / _speed;
        private float PreviewTime => Mathf.Clamp((_time - RangeStart) / _speed, 0f, PreviewDuration);
        private int OutputFps => Mathf.Clamp(Mathf.RoundToInt(ClipRate), 1, 120);

        private void SetPreviewTime(float seconds)
        {
            _time = RangeStart + Mathf.Clamp(seconds, 0f, PreviewDuration) * _speed;
        }
        private double _lastEditorTime;
        private CameraPreset _camera = CameraPreset.SIDE_3Q;

        private static string ShortlistPath => Path.Combine(ProjectPaths.RepositoryRoot, "catalog", "motion_shortlist.json");

        [MenuItem("CrossBlade/Elden Ring Motion Library")]
        public static void Open()
        {
            var window = GetWindow<EldenRingLibraryWindow>("ER Visual Motion Library");
            window.minSize = new Vector2(900f, 650f);
            window.Show();
        }

        private void OnEnable()
        {
            _session = new MotionPreviewSession();
            EldenRingMotionImporter.EnsurePreviewPrefab();
            _character = AssetDatabase.LoadAssetAtPath<GameObject>(EldenRingMotionImporter.PrefabPath);
            EditorApplication.update += EditorUpdate;
            Reload();
            LoadShortlist();
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
            _session?.Dispose();
            _session = null;
        }

        private void Reload()
        {
            var path = Path.Combine(ProjectPaths.RepositoryRoot, "catalog", "elden_ring_browser_catalog.json");
            _catalog = File.Exists(path) ? JsonUtility.FromJson<IndexedMotionCatalog>(File.ReadAllText(path)) : null;
            var entries = _catalog?.entries ?? Array.Empty<IndexedMotion>();
            var selectedWeapon = _weapons[Mathf.Clamp(_weapon, 0, _weapons.Length - 1)];
            _weapons = new[] { "All" }.Concat(entries.Select(item => item.weapon ?? "")
                .Distinct().OrderBy(value => string.IsNullOrEmpty(value) ? 1 : 0).ThenBy(value => value)).ToArray();
            _weaponLabels = _weapons.Select(value => $"{WeaponLabel(value)} ({(value == "All" ? entries.Length : entries.Count(item => (item.weapon ?? "") == value)):N0})").ToArray();
            _weapon = Mathf.Max(0, Array.IndexOf(_weapons, selectedWeapon));
            _groups = new[] { "All" }.Concat(entries.Select(item => item.group).Distinct().OrderBy(item => item)).ToArray();
            _categories = new[] { "All" }.Concat(entries.Select(item => item.category).Distinct().OrderBy(item => item)).ToArray();
        }

        private IndexedMotion[] VisibleMotions()
        {
            var group = _groups[Mathf.Clamp(_group, 0, _groups.Length - 1)];
            var category = _categories[Mathf.Clamp(_category, 0, _categories.Length - 1)];
            var weapon = _weapons[Mathf.Clamp(_weapon, 0, _weapons.Length - 1)];
            return (_catalog?.entries ?? Array.Empty<IndexedMotion>())
                .Where(item => weapon == "All" || (item.weapon ?? "") == weapon)
                .Where(item => group == "All" || item.group == group)
                .Where(item => category == "All" || item.category == category)
                .Where(item => !_shortlistOnly || _shortlist.Contains(item.id))
                .Where(item => string.IsNullOrWhiteSpace(_search) ||
                    item.id.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.weapon.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.category.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
        }

        private void OnGUI()
        {
            HandleKeyboard();
            _windowScroll = EditorGUILayout.BeginScrollView(_windowScroll);
            EditorGUILayout.LabelField("Elden Ring Visual Motion Library", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "목록의 이름만 고르는 창이 아닙니다. 후보를 선택하면 오른쪽에서 즉시 반복 재생됩니다. 처음 연 모션만 한 번 변환하며, 이후에는 캐시된 클립을 바로 재생합니다.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            _search = EditorGUILayout.TextField("Search", _search);
            if (GUILayout.Button("Reload", GUILayout.Width(70f))) Reload();
            EditorGUILayout.EndHorizontal();
            var nextWeapon = EditorGUILayout.Popup("Category / 무기 타입", _weapon, _weaponLabels);
            if (nextWeapon != _weapon)
            {
                _weapon = nextWeapon;
                _group = 0;
                _search = "";
                _autoNext = false;
            }
            _category = EditorGUILayout.Popup("동작 종류", Mathf.Clamp(_category, 0, _categories.Length - 1), _categories);
            _group = EditorGUILayout.Popup("원본 그룹", Mathf.Clamp(_group, 0, _groups.Length - 1), _groups);
            _shortlistOnly = EditorGUILayout.Toggle("Shortlist only", _shortlistOnly);
            if (EditorGUI.EndChangeCheck()) _scroll = Vector2.zero;

            var visible = VisibleMotions();
            EditorGUILayout.LabelField($"Indexed: {_catalog?.entries?.Length ?? 0:N0} · Filtered: {visible.Length:N0} · Shortlist: {_shortlist.Count:N0}");

            EditorGUILayout.BeginHorizontal(GUILayout.Height(Mathf.Max(320f, position.height - 245f)));
            DrawCandidateList(visible);
            DrawVisualReview(visible);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "단축키: Space 재생/정지 · ←/→ 이전/다음 모션 · ,/. 이전/다음 프레임 · F 후보 저장 · I 시작 지정 · O 끝 지정 · C .anim 내보내기",
                EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        private void DrawCandidateList(IndexedMotion[] visible)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(Mathf.Clamp(position.width * 0.34f, 280f, 420f)));
            EditorGUILayout.LabelField("Candidates", EditorStyles.boldLabel);
            // Reserve the complete list height but draw only the rows in the viewport.
            // This keeps every result reachable without loading thousands of assets per repaint.
            var viewport = GUILayoutUtility.GetRect(100f, 100f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            const float rowHeight = 25f;
            var content = new Rect(0f, 0f, Mathf.Max(100f, viewport.width - 18f), visible.Length * rowHeight);
            _scroll = GUI.BeginScrollView(viewport, _scroll, content);
            var first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / rowHeight));
            var end = Mathf.Min(visible.Length, first + Mathf.CeilToInt(viewport.height / rowHeight) + 1);
            for (var index = first; index < end; index++)
            {
                var item = visible[index];
                var prepared = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(item.id)) != null;
                var marker = _shortlist.Contains(item.id) ? "★" : prepared ? "▶" : "·";
                var style = item.id == _currentId ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
                var label = $"{marker} {item.id}   {item.weapon}   {item.category}";
                if (GUI.Button(new Rect(0f, index * rowHeight, content.width, rowHeight - 2f), new GUIContent(label, label), style))
                {
                    var selected = item;
                    EditorApplication.delayCall += () => SelectMotion(selected, true);
                }
            }
            GUI.EndScrollView();
            EditorGUILayout.LabelField($"전체 {visible.Length:N0}개 · 스크롤로 모두 탐색", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawVisualReview(IndexedMotion[] visible)
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            _reviewScroll = EditorGUILayout.BeginScrollView(_reviewScroll);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(visible.Length == 0))
            {
                if (GUILayout.Button("◀ Previous", GUILayout.Height(26f))) ScheduleMove(-1);
                if (GUILayout.Button(_playing ? "Pause" : "Play", GUILayout.Height(26f))) TogglePlay();
                if (GUILayout.Button("Next ▶", GUILayout.Height(26f))) ScheduleMove(1);
            }
            _autoNext = GUILayout.Toggle(_autoNext, "Auto next", GUILayout.Width(82f));
            EditorGUILayout.EndHorizontal();

            var previewHeight = Mathf.Clamp((position.height - 245f) * .55f, 180f, 600f);
            var previewRect = GUILayoutUtility.GetRect(320f, previewHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.125f, 0.14f));
            if (_session?.Instance != null && _clip != null)
            {
                var render = _session.RenderPreview(
                    Mathf.Max(16, (int)previewRect.width), Mathf.Max(16, (int)previewRect.height), _showTrail);
                if (render != null) GUI.DrawTexture(previewRect, render, ScaleMode.ScaleToFit, true);
            }
            else
            {
                GUI.Label(previewRect, visible.Length == 0 ? "조건에 맞는 모션이 없습니다." : "왼쪽 후보를 선택하거나 Next를 누르세요.", EditorStyles.centeredGreyMiniLabel);
            }

            if (_clip != null)
            {
                EditorGUILayout.LabelField($"{_currentId} · {(_rangeOnly ? "편집 결과" : "원본 전체 확인")} {PreviewDuration:0.000}초 @ {OutputFps} FPS", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var nextTime = EditorGUILayout.Slider("재생 시간", PreviewTime, 0f, PreviewDuration);
                if (EditorGUI.EndChangeCheck())
                {
                    SetPreviewTime(nextTime);
                    _playing = false;
                    UpdatePose();
                }
                var frame = Mathf.Min(Mathf.RoundToInt(PreviewTime * OutputFps), Mathf.CeilToInt(PreviewDuration * OutputFps));
                EditorGUILayout.LabelField($"출력 프레임 {frame} / {Mathf.CeilToInt(PreviewDuration * OutputFps)} · {PreviewTime:0.000} / {PreviewDuration:0.000}초", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"원본 프레임 {Mathf.RoundToInt(_time * ClipRate)} · 원본 전체 {_clip.length:0.000}초", EditorStyles.miniLabel);
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_clip == null))
            {
                if (GUILayout.Button(_shortlist.Contains(_currentId) ? "★ Remove shortlist" : "☆ Keep in shortlist")) ToggleShortlist();
                if (GUILayout.Button("Open detailed preview")) EditorApplication.delayCall += () => MotionBrowserWindow.OpenClip(_clip);
            }
            EditorGUILayout.EndHorizontal();

            if (_clip != null) DrawClipEditing();
            EditorGUI.BeginChangeCheck();
            _rootMotion = EditorGUILayout.Toggle("Root motion", _rootMotion);
            _showTrail = EditorGUILayout.Toggle("Weapon trail", _showTrail);
            _camera = (CameraPreset)EditorGUILayout.EnumPopup("Camera", _camera);
            if (EditorGUI.EndChangeCheck()) UpdatePose();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void SelectMotion(IndexedMotion item, bool autoplay)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(item.id)) ?? Prepare(item.id);
            if (clip == null) return;
            RememberEdit();
            _currentId = item.id;
            _clip = clip;
            if (_edits.TryGetValue(item.id, out var edit))
            { _firstFrame = edit.first; _lastFrame = edit.last; _speed = edit.speed; }
            else { _firstFrame = 0; _lastFrame = MaximumFrame; _speed = 1f; }
            _firstFrame = Mathf.Clamp(_firstFrame, 0, Mathf.Max(0, MaximumFrame - 1));
            _lastFrame = Mathf.Clamp(_lastFrame, _firstFrame, MaximumFrame);
            _time = RangeStart;
            _playing = autoplay;
            _lastEditorTime = EditorApplication.timeSinceStartup;
            try
            {
                _session ??= new MotionPreviewSession();
                _session.Load(_character, BodyColor, WeaponColor);
                _session.SetSilhouette(false, BodyColor, WeaponColor);
                _session.FitCameraToClip(_clip, _rootMotion);
                UpdatePose();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Motion preview failed", exception.Message, "OK");
            }
        }

        private void MoveCandidate(IndexedMotion[] visible, int direction)
        {
            if (visible.Length == 0) return;
            var current = Array.FindIndex(visible, item => item.id == _currentId);
            var next = current < 0 ? (direction > 0 ? 0 : visible.Length - 1) : (current + direction + visible.Length) % visible.Length;
            SelectMotion(visible[next], true);
        }

        private void ScheduleMove(int direction)
        {
            EditorApplication.delayCall += () => MoveCandidate(VisibleMotions(), direction);
        }

        private void TogglePlay()
        {
            if (_clip == null) return;
            _playing = !_playing;
            if (_playing && (_time < RangeStart || _time >= RangeEnd)) _time = RangeStart;
            _lastEditorTime = EditorApplication.timeSinceStartup;
        }

        private void StepFrame(int direction)
        {
            if (_clip == null) return;
            _playing = false;
            var framePosition = PreviewTime * OutputFps;
            var nextFrame = direction > 0 ? Mathf.FloorToInt(framePosition + .0001f) + 1 : Mathf.CeilToInt(framePosition - .0001f) - 1;
            SetPreviewTime(nextFrame / (float)OutputFps);
            UpdatePose();
        }

        private void EditorUpdate()
        {
            if (!_playing || _clip == null) return;
            var now = EditorApplication.timeSinceStartup;
            if (_time < RangeStart || _time > RangeEnd) _time = RangeStart;
            _time += (float)(now - _lastEditorTime) * _speed;
            _lastEditorTime = now;
            if (_time >= RangeEnd)
            {
                if (_autoNext)
                {
                    MoveCandidate(VisibleMotions(), 1);
                    return;
                }
                var length = RangeEnd - RangeStart;
                _time = length > 0f ? RangeStart + (_time - RangeStart) % length : RangeStart;
                if (length <= 0f) _playing = false;
            }
            UpdatePose();
            Repaint();
        }

        private void UpdatePose()
        {
            if (_session == null || _clip == null) return;
            _session.Sample(_clip, _time, _rootMotion);
            _session.UpdateTrail(_clip, _time, _rootMotion, _showTrail);
            _session.SetCamera(_camera, 0f, 0f, 6f, 1.2f, true);
            Repaint();
        }

        private void HandleKeyboard()
        {
            var current = Event.current;
            if (focusedWindow != this || current.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;
            var handled = true;
            if (current.keyCode == KeyCode.Space) TogglePlay();
            else if (current.keyCode == KeyCode.LeftArrow) ScheduleMove(-1);
            else if (current.keyCode == KeyCode.RightArrow) ScheduleMove(1);
            else if (current.keyCode == KeyCode.Comma) StepFrame(-1);
            else if (current.keyCode == KeyCode.Period) StepFrame(1);
            else if (current.keyCode == KeyCode.F) ToggleShortlist();
            else if (current.keyCode == KeyCode.C && _clip != null)
                ExportEditedClip();
            else if (current.keyCode == KeyCode.I && _clip != null) SetBoundary(true);
            else if (current.keyCode == KeyCode.O && _clip != null) SetBoundary(false);
            else handled = false;
            if (handled) current.Use();
        }

        private void RememberEdit()
        {
            if (!string.IsNullOrEmpty(_currentId)) _edits[_currentId] = (_firstFrame, _lastFrame, _speed);
        }

        private void SetBoundary(bool first)
        {
            var frame = Mathf.Clamp(Mathf.RoundToInt(_time * ClipRate), 0, MaximumFrame);
            if (first) _firstFrame = Mathf.Min(frame, Mathf.Max(0, _lastFrame - 1));
            else _lastFrame = Mathf.Max(frame, Mathf.Min(MaximumFrame, _firstFrame + 1));
            _rangeOnly = true;
            EditingChanged();
        }

        private void EditingChanged()
        {
            _autoNext = false;
            _playing = false;
            _time = Mathf.Clamp(_time, RangeStart, RangeEnd);
            RememberEdit();
            UpdatePose();
        }

        private void DrawClipEditing()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("자르기 · 배속 · .anim 내보내기", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var previousFirst = _firstFrame;
            var previousLast = _lastFrame;
            _firstFrame = EditorGUILayout.IntSlider("시작 프레임", _firstFrame, 0, Mathf.Max(0, _lastFrame - 1));
            _lastFrame = EditorGUILayout.IntSlider("끝 프레임", _lastFrame, Mathf.Min(MaximumFrame, _firstFrame + 1), MaximumFrame);
            if (_firstFrame != previousFirst || _lastFrame != previousLast) _rangeOnly = true;
            _speed = EditorGUILayout.Slider("재생 / 내보내기 배속", _speed, .1f, 4f);
            _rangeOnly = !EditorGUILayout.Toggle("원본 전체 확인", !_rangeOnly);
            if (EditorGUI.EndChangeCheck()) EditingChanged();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("현재 프레임을 시작으로")) SetBoundary(true);
            if (GUILayout.Button("현재 프레임을 끝으로")) SetBoundary(false);
            if (GUILayout.Button("전체 구간 / 1배속"))
            { _firstFrame = 0; _lastFrame = MaximumFrame; _speed = 1f; EditingChanged(); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField($"원본 {_firstFrame / ClipRate:0.000}–{_lastFrame / ClipRate:0.000}초 → 저장 길이 {(_lastFrame - _firstFrame) / ClipRate / _speed:0.000}초");
            using (new EditorGUI.DisabledScope(_lastFrame <= _firstFrame || _character == null))
                if (GUILayout.Button("선택 구간을 .anim으로 내보내기", GUILayout.Height(30))) ExportEditedClip();
            EditorGUILayout.EndVertical();
        }

        private void ExportEditedClip()
        {
            if (_clip == null || _character == null || _lastFrame <= _firstFrame) return;
            _playing = false;
            var path = EditorUtility.SaveFilePanelInProject("편집한 애니메이션 내보내기", _currentId + "_edited", "anim", "선택 구간과 배속을 적용한 .anim을 저장합니다.");
            if (string.IsNullOrEmpty(path)) return;
            if (File.Exists(path))
            { EditorUtility.DisplayDialog("이미 존재하는 파일", "원본과 기존 결과를 보존하려면 새 파일 이름을 지정하세요.", "확인"); return; }
            var recipe = CreateInstance<MotionSequenceAsset>();
            try
            {
                recipe.previewCharacter = _character;
                recipe.outputFps = OutputFps;
                recipe.keepRootMotion = _rootMotion;
                recipe.segments.Add(new MotionSegment { clip = _clip, firstFrame = _firstFrame, lastFrame = _lastFrame, speed = _speed });
                var result = MotionSequenceCore.Bake(recipe, path);
                Selection.activeObject = result;
                EditorGUIUtility.PingObject(result);
                ShowNotification(new GUIContent(".anim 저장 완료: " + result.name));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("내보내기 실패", exception.Message, "확인");
            }
            finally { DestroyImmediate(recipe); }
        }

        private void ToggleShortlist()
        {
            if (string.IsNullOrEmpty(_currentId)) return;
            if (!_shortlist.Add(_currentId)) _shortlist.Remove(_currentId);
            SaveShortlist();
            Repaint();
        }

        private void LoadShortlist()
        {
            _shortlist.Clear();
            if (!File.Exists(ShortlistPath)) return;
            var data = JsonUtility.FromJson<MotionShortlist>(File.ReadAllText(ShortlistPath));
            if (data?.ids == null) return;
            foreach (var id in data.ids) _shortlist.Add(id);
        }

        private void SaveShortlist()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ShortlistPath));
            var data = new MotionShortlist { ids = _shortlist.OrderBy(id => id, StringComparer.Ordinal).ToList() };
            File.WriteAllText(ShortlistPath, JsonUtility.ToJson(data, true));
        }

        private static string ClipPath(string id) => $"{EldenRingMotionImporter.GeneratedRoot}/{id}.anim";

        private static string WeaponLabel(string weapon) => weapon switch
        {
            "All" => "전체", "" => "무기 미분류",
            "Straight Sword" => "직검", "Greatsword" => "대검", "Colossal Sword" => "특대검",
            "Thrusting Sword" => "자검", "Curved Sword" => "곡검", "Katana" => "도",
            "Dagger" => "단검", "Axe" => "도끼", "Greataxe" => "대형 도끼",
            "Hammer" => "망치", "Great Hammer" => "대형 망치", "Flail" => "철퇴",
            "Spear" => "창", "Great Spear" => "대형 창", "Halberd" => "도끼창",
            "Scythe" => "낫", "Whip" => "채찍", "Fist" => "주먹", "Claw" => "손톱",
            "Light Bow" => "소형 활", "Torch" => "횃불", "Catalyst" => "촉매",
            "Thrusting Shield" => "자돌 방패", "Smithscript Dagger" => "투척검",
            "Hand-to-Hand" => "격투", _ => weapon
        };

        internal static AnimationClip Prepare(string id)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Elden Ring Motion", $"Decoding {id}", .25f);
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"\"{Path.Combine(ProjectPaths.RepositoryRoot, "scripts", "prepare_elden_ring_motion.py")}\" {id}",
                    WorkingDirectory = ProjectPaths.RepositoryRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException($"Decoder failed with exit code {process.ExitCode}.");
                EditorUtility.DisplayProgressBar("Elden Ring Motion", $"Creating Unity clip {id}", .75f);
                return EldenRingMotionImporter.ImportSingle(Path.Combine(ProjectPaths.RepositoryRoot, "converted", "elden_ring", id + ".motion.json"));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Motion preparation failed", exception.Message, "OK");
                return null;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }
    }
}
