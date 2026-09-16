using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MotionPrototype;
using Scripts;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class MoveHitboxWindow : EditorWindow
{
    internal Func<Move, string, System.Collections.Generic.IEnumerable<Move>> ReactionTargets;
    private System.Collections.Generic.IEnumerable<Move> PreviewTargets(Move source, string field)
        => ReactionTargets != null ? ReactionTargets(source, field) : MoveGraphWindow.EffectiveTargets(source, field);
    [SerializeField] private Move move;
    [SerializeField] private GameObject character;
    [SerializeField] private string motionRootPath = "Model/skeleton/Pelvis";
    [SerializeField] private float yaw = 90f;
    private MotionPreviewSession preview;
    private CharacterAnimationPlayer player;
    private Hitbox selected;
    private bool editBody;
    private Collider2D Body => move == null ? null : new SerializedObject(move).FindProperty("bodyCollider").objectReferenceValue as Collider2D;
    private BoxCollider2D EditingBox => editBody ? Body as BoxCollider2D : selected == null ? null : selected.GetComponent<BoxCollider2D>();
    private float time, zoom = 2.2f, height = 1f;
    private bool playing, leftFacing;
    [SerializeField] private float previewSpeed = 1f;
    private readonly List<Move> incoming = new();
    private Move incomingFor, pairSource, pairTarget;
    private int incomingIndex;
    private bool pairActive;
    private double lastUpdate;
    private Vector2 scroll;
    private int dragMode;
    private float startupSeconds = .1f;
    private Vector2 graphOrigin;
    private bool followGraph = true;
    private bool waitingForBranch;
    internal Action HostRepaint;
    internal Action<Move> ActiveMoveChanged;
    internal Move CurrentMove => move;
    private float emptyClipProgress;
    private bool showSetup;
    private bool editMovement;
    private Vector2 keyPosition;
    private static readonly MethodInfo Evaluate = typeof(CharacterAnimationPlayer).GetMethod("Evaluate", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Open()
    {
        var window = GetWindow<MoveHitboxWindow>("Move Motion Editor");
        window.minSize = new Vector2(740, 650);
        var source = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Move>() : null;
        if (source != null && EditorUtility.IsPersistent(source)) window.SetMove(source);
    }

    public static void OpenWithMove(Move source)
    {
        var window = GetWindow<MoveHitboxWindow>("Move Motion Editor");
        window.minSize = new Vector2(740, 650);
        window.SetMove(source);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.update += Tick;
        Undo.undoRedoPerformed += RefreshView;
        lastUpdate = EditorApplication.timeSinceStartup;
        if (character == null) character = AssetDatabase.LoadAssetAtPath<GameObject>(EldenRingMotionImporter.PrefabPath);
        ReloadPreview();
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        Undo.undoRedoPerformed -= RefreshView;
        preview?.Dispose();
    }

    private void SetMove(Move source) { pairActive = false; graphOrigin = Vector2.zero; move = source; selected = null; time = 0; emptyClipProgress = 0; playing = false; waitingForBranch = false; RefreshView(); }
    internal void SelectGraphMove(Move source) { if (move != source) SetMove(source); }
    internal void DrawEmbedded() => OnGUI();
    internal void PausePlayback() => playing = false;
    private void RefreshView() { Repaint(); HostRepaint?.Invoke(); }
    private AnimationClip Clip => move == null ? null : new SerializedObject(move).FindProperty("characterAnimation").objectReferenceValue as AnimationClip;
    private float Progress => Clip == null || Clip.length <= 0 ? emptyClipProgress : Mathf.Clamp01(time / Clip.length);
    private int LastFrame => Clip == null ? 0 : Mathf.RoundToInt(Clip.length * Clip.frameRate);

    internal void StoreGraphSettings(CombatMoveGraphAsset asset)
    {
        asset.previewCharacter = character; asset.previewYaw = yaw; asset.motionRootPath = motionRootPath;
        asset.previewSpeed = previewSpeed; asset.previewZoom = zoom; asset.previewHeight = height; asset.startupSeconds = startupSeconds;
    }

    internal void LoadGraphSettings(CombatMoveGraphAsset asset)
    {
        if (asset.previewCharacter != null) character = asset.previewCharacter;
        yaw = asset.previewYaw; motionRootPath = asset.motionRootPath;
        previewSpeed = Mathf.Clamp(asset.previewSpeed, .05f, 2); zoom = asset.previewZoom; height = asset.previewHeight; startupSeconds = asset.startupSeconds;
        ReloadPreview();
    }

    private void ReloadPreview()
    {
        preview?.Dispose(); player = null;
        if (character == null) return;
        preview = new MotionPreviewSession();
        preview.Load(character, Color.gray, Color.white);
        preview.SetSilhouette(false, Color.gray, Color.white);
        player = preview.Instance.GetComponentInChildren<CharacterAnimationPlayer>(true);
        if (player != null) return; // A runtime prefab already carries its own yaw/root configuration.
        var wrapper = new GameObject("Hitbox preview facing root");
        SceneManager.MoveGameObjectToScene(wrapper, preview.Instance.scene);
        preview.Instance.transform.SetParent(wrapper.transform, false);
        player = wrapper.AddComponent<CharacterAnimationPlayer>();
        var settings = new SerializedObject(player);
        settings.FindProperty("animationRoot").objectReferenceValue = preview.Instance;
        settings.FindProperty("rightFacingYaw").floatValue = yaw;
        settings.FindProperty("motionRoot").objectReferenceValue = string.IsNullOrEmpty(motionRootPath) ? null : preview.Instance.transform.Find(motionRootPath);
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    private void Tick()
    {
        var now = EditorApplication.timeSinceStartup;
        if (now - lastUpdate < 1d / 30d) return;
        if (playing)
        {
            AdvancePreviewTime((float)(now - lastUpdate));
            RefreshView();
        }
        lastUpdate = now;
    }

    private void OnGUI()
    {
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawGraphNavigation();
            if (HostRepaint == null)
            {
                var next = (Move)EditorGUILayout.ObjectField("Move 프리팹", move, typeof(Move), false);
                if (next != move) SetMove(next);
            }
            if (move == null) { EditorGUILayout.EndScrollView(); return; }
            showSetup = EditorGUILayout.Foldout(showSetup || Clip == null, "애니메이션 · 캐릭터 설정", true);
            if (showSetup)
            {
                EditorGUI.BeginChangeCheck();
                character = (GameObject)EditorGUILayout.ObjectField("미리보기 캐릭터", character, typeof(GameObject), false);
                yaw = EditorGUILayout.FloatField("모델 방향 Y (기본 모델)", yaw);
                motionRootPath = EditorGUILayout.TextField("수평 고정 본 경로 (선택)", motionRootPath);
                if (EditorGUI.EndChangeCheck()) ReloadPreview();
                var so = new SerializedObject(move);
                EditorGUILayout.PropertyField(so.FindProperty("characterAnimation"), new GUIContent("애니메이션 (.anim)"));
                EditorGUILayout.PropertyField(so.FindProperty("duration"), new GUIContent("실행 시간 (초)"));
                EditorGUILayout.PropertyField(so.FindProperty("category"));
                EditorGUILayout.PropertyField(so.FindProperty("damageBase"), new GUIContent("기본 데미지"));
                EditorGUILayout.PropertyField(so.FindProperty("stanceDamageBase"), new GUIContent("기본 자세 데미지"));
                so.ApplyModifiedProperties();
            }
            previewSpeed = EditorGUILayout.Slider("미리보기 속도", previewSpeed, .05f, 2f);
            EditorGUILayout.BeginHorizontal();
            foreach (float speed in new[] { .1f, .25f, .5f, 1f })
                if (GUILayout.Button(speed.ToString("0.##") + "×")) previewSpeed = speed;
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(Clip == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(playing ? "일시정지" : "재생")) playing = !playing;
                if (GUILayout.Button("이전 프레임")) { playing = false; time = Mathf.Max(0, time - 1 / Mathf.Max(1, Clip.frameRate)); }
                if (GUILayout.Button("다음 프레임")) { playing = false; time = Mathf.Min(Clip.length, time + 1 / Mathf.Max(1, Clip.frameRate)); }
                var nextFacing = GUILayout.Toggle(leftFacing, "왼쪽 방향", "Button");
                if (nextFacing != leftFacing) { leftFacing = nextFacing; graphOrigin.x = -graphOrigin.x; }
                EditorGUILayout.EndHorizontal();
                EditorGUI.BeginChangeCheck();
                time = EditorGUILayout.Slider("클립 시간", time, 0, Clip != null ? Clip.length : 0);
                EditorGUI.EndChangeCheck();
                EditorGUILayout.LabelField($"프레임 {(Clip == null ? 0 : Mathf.RoundToInt(time * Clip.frameRate))} / {LastFrame}");
            }
            zoom = EditorGUILayout.Slider("화면 범위", zoom, .5f, 8f);
            height = EditorGUILayout.Slider("화면 높이", height, -2f, 4f);
            var mode = GUILayout.Toolbar(editBody ? 1 : 0, new[] { "무기 공격 판정", "몸 피격 판정" });
            if (editBody != (mode == 1)) { editBody = mode == 1; dragMode = 0; editMovement = false; }
            var rect = GUILayoutUtility.GetRect(200, 360, GUILayout.ExpandWidth(true));
            DrawPreview(rect);
            DrawMovement();
            if (editBody) DrawBody(); else DrawBoxes();
            EditorGUILayout.BeginHorizontal();
            if (!editBody && GUILayout.Button("무기 박스 추가")) EditorApplication.delayCall += () => { if (this != null) { Save(); AddBox(); } };
            if (GUILayout.Button("Move 저장")) Save();
            using (new EditorGUI.DisabledScope(selected == null))
                if (!editBody && GUILayout.Button("선택 무기 박스 삭제")) EditorApplication.delayCall += () => { if (this != null) RemoveBox(); };
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawPreview(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(.08f, .09f, .1f));
        if (preview == null || player == null) return;
        var texture = RenderModel(Mathf.Max(1, (int)rect.width), Mathf.Max(1, (int)rect.height));
        if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill);
        GUI.BeginGroup(rect);
        var local = new Rect(0, 0, rect.width, rect.height);
        var zero = ToScreen(Vector2.zero, local);
        Handles.BeginGUI(); Handles.color = new Color(.5f, .5f, .5f, .5f);
        Handles.DrawLine(new Vector3(0, zero.y), new Vector3(rect.width, zero.y));
        Handles.DrawLine(new Vector3(zero.x, 0), new Vector3(zero.x, rect.height));
        var actorOffset = ActorOffset;
        var lastPoint = ToScreen(graphOrigin, local);
        Handles.color = Color.cyan;
        for (int i = 0; i <= 40; i++)
        {
            var point = ToScreen(graphOrigin + MoveTimelineSampling.Offset(move, i / 40f, Startup, leftFacing ? -1 : 1), local);
            if (i > 0) Handles.DrawLine(lastPoint, point);
            lastPoint = point;
        }
        var marker = ToScreen(actorOffset, local);
        EditorGUI.DrawRect(new Rect(marker - Vector2.one * 5, Vector2.one * 10), Color.cyan);
        if (editMovement && Event.current.type == EventType.MouseDown && Event.current.button == 0 && Vector2.Distance(marker, Event.current.mousePosition) < 14)
        { dragMode = 3; Event.current.Use(); }
        var boxes = move.GetComponentsInChildren<Hitbox>(true)
            .Select(hit => (hit, box: hit.GetComponent<BoxCollider2D>()))
            .Append((hit: (Hitbox)null, box: Body as BoxCollider2D));
        foreach (var entry in boxes)
        {
            var hit = entry.hit;
            var box = entry.box;
            if (box == null) continue;
            var isBody = hit == null;
            var editable = editBody == isBody;
            var isSelected = editable && (isBody || hit == selected);
            var bounds = BoxBounds(move.transform, box, leftFacing);
            bounds.center += (Vector3)actorOffset;
            var a = ToScreen(bounds.min, local); var b = ToScreen(bounds.max, local);
            var screen = Rect.MinMaxRect(a.x, b.y, b.x, a.y);
            var color = isBody ? new Color(.2f, .8f, 1f) : hit.IsActiveAt(Progress) ? new Color(1, .55f, .1f) : Color.gray;
            Handles.DrawSolidRectangleWithOutline(screen, new Color(color.r, color.g, color.b, .12f), isSelected ? Color.yellow : color);
            if (isBody) GUI.Label(new Rect(screen.x, screen.y - 18, 100, 18), "몸 피격 판정");
            if (isSelected)
            {
                var handle = new Rect(screen.xMax - 6, screen.yMin - 6, 12, 12);
                EditorGUI.DrawRect(handle, Color.yellow);
                if (!editMovement && Event.current.type == EventType.MouseDown && Event.current.button == 0 && handle.Contains(Event.current.mousePosition))
                { dragMode = 2; Undo.RecordObject(box, "Resize hitbox"); Event.current.Use(); }
            }
            if (editable && !editMovement && Event.current.type == EventType.MouseDown && Event.current.button == 0 && screen.Contains(Event.current.mousePosition))
            { if (!isBody) selected = hit; dragMode = 1; Undo.RecordObject(box, "Move hitbox"); Event.current.Use(); }
        }
        Handles.EndGUI();
        if (Event.current.type == EventType.MouseDrag && dragMode == 3)
        {
            var delta = new Vector2(Event.current.delta.x, -Event.current.delta.y) * (2 * zoom / rect.height);
            if (leftFacing) delta.x = -delta.x;
            keyPosition = move.EvaluateMovementOffset(MoveTimelineSampling.PhaseProgress(move, Progress, Startup), 1) + delta;
            SetMovementKey(); Event.current.Use(); RefreshView();
        }
        if (Event.current.type == EventType.MouseDrag && (dragMode == 1 || dragMode == 2) && EditingBox != null)
        {
            var box = EditingBox;
            Undo.RecordObject(box, dragMode == 1 ? "Move hitbox" : "Resize hitbox");
            var delta = new Vector3(Event.current.delta.x, -Event.current.delta.y, 0) * (2 * zoom / rect.height);
            if (leftFacing && dragMode == 1) delta.x = -delta.x;
            // Convert Move-local drag into the collider's own coordinates.
            delta = box.transform.InverseTransformVector(move.transform.TransformVector(delta));
            if (dragMode == 1) box.offset += (Vector2)delta;
            else box.size = new Vector2(Mathf.Max(.01f, box.size.x + 2 * delta.x), Mathf.Max(.01f, box.size.y + 2 * delta.y));
            EditorUtility.SetDirty(box); Event.current.Use(); RefreshView();
        }
        if (Event.current.type == EventType.MouseUp) dragMode = 0;
        GUI.EndGroup();
    }

    internal RenderTexture RenderModel(int width, int pixelHeight)
    {
        if (preview == null || player == null) return null;
        Evaluate.Invoke(player, new object[] { move, Progress, leftFacing ? -1 : 1 });
        preview.Instance.transform.root.position = ActorOffset;
        preview.SetCombatCamera(zoom, height);
        return preview.RenderPreview(width, pixelHeight, false);
    }

    private Vector2 ToScreen(Vector2 point, Rect rect) => new Vector2(rect.width * .5f + point.x * rect.height / (2 * zoom), rect.height * .5f - (point.y - height) * rect.height / (2 * zoom));

    internal static Bounds BoxBounds(Transform root, BoxCollider2D box, bool flip)
    {
        var bounds = new Bounds();
        for (int i = 0; i < 4; i++)
        {
            var p = root.InverseTransformPoint(box.transform.TransformPoint(box.offset + Vector2.Scale(box.size * .5f, new Vector2((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1))));
            if (flip) p.x = -p.x;
            if (i == 0) bounds = new Bounds(p, Vector3.zero); else bounds.Encapsulate(p);
        }
        return bounds;
    }

    private float Startup => Mathf.Max(0, startupSeconds);
    private Vector2 ActorOffset => graphOrigin + MoveTimelineSampling.Offset(move, Progress, Startup, leftFacing ? -1 : 1);

    internal void AdvancePreviewTime(float wallSeconds) => AdvancePlayback(wallSeconds * previewSpeed);

    internal void AdvancePlayback(float seconds)
    {
        if (move == null || !playing) return;
        float span = MoveTimelineSampling.Duration(move) + Startup;
        float progress = Mathf.Clamp01(Progress + seconds / span);
        emptyClipProgress = progress;
        time = Clip == null ? 0 : Clip.length * progress;
        if (progress < 1) return;
        if (pairActive)
        {
            if (move == pairSource && move != pairTarget)
            {
                if (MoveGraphWindow.Targets(move, "after").Contains(pairTarget)) FollowEdge(pairTarget, "after");
                else { pairActive = false; playing = false; }
            }
            else { pairActive = false; playing = false; }
            return;
        }
        if (!followGraph) { emptyClipProgress = 0; time = 0; return; }
        var candidates = MoveGraphWindow.Targets(move, "after").Where(target => target != null).Distinct().ToArray();
        if (candidates.Length == 1) FollowEdge(candidates[0], "after");
        else { playing = false; waitingForBranch = candidates.Length > 1; }
    }

    internal void FollowEdge(Move target, string field)
    {
        if (move == null || target == null || !PreviewTargets(move, field).Contains(target)) return;
        float departure = field == "after" ? 1 : Progress;
        graphOrigin += MoveTimelineSampling.Offset(move, departure, Startup, leftFacing ? -1 : 1);
        move = target; time = 0; emptyClipProgress = 0; selected = null;
        waitingForBranch = false; playing = true;
        ActiveMoveChanged?.Invoke(target);
        RefreshView();
    }

    private void DrawGraphNavigation()
    {
        EditorGUILayout.LabelField("그래프 연결 미리보기", EditorStyles.boldLabel);
        followGraph = EditorGUILayout.Toggle("연결된 다음 행동 이어 재생", followGraph);
        if (move == null) return;
        DrawPredecessors();
        if (waitingForBranch) EditorGUILayout.HelpBox("다음 행동 후보가 여러 개입니다. 이어 볼 분기를 선택하세요.", MessageType.Info);
        foreach (var field in new[] { "after", "guardMove", "hitMove" })
        {
            var label = field == "after" ? "다음" : field == "guardMove" ? "현재 시점에서 방어" : "현재 시점에서 피격";
            foreach (var target in PreviewTargets(move, field).Where(target => target != null).Distinct().ToArray())
                if (GUILayout.Button(label + " → " + target.name))
                {
                    var destination = target;
                    var port = field;
                    EditorApplication.delayCall += () => { if (this != null) FollowEdge(destination, port); };
                }
        }
        if (GUILayout.Button("선택 노드 처음부터 · 위치 초기화")) SetMove(move);
        EditorGUILayout.HelpBox("실제 Move의 연결을 따라 봅니다. 다음 후보가 하나면 이어 재생하고, 여러 개면 선택을 기다립니다. 방어·피격 버튼은 현재 시점에서 해당 분기를 미리봅니다. 전투의 자동 실행 규칙을 변경하지 않습니다.", MessageType.None);
    }

    internal static List<Move> FindPredecessors(Move target)
    {
        var found = new List<Move>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null) continue;
            foreach (var candidate in prefab.GetComponentsInChildren<Move>(true))
                if (candidate != target && MoveGraphWindow.Targets(candidate, "after").Contains(target) && !found.Contains(candidate)) found.Add(candidate);
        }
        return found.OrderBy(candidate => candidate.name).ToList();
    }

    internal bool PlayPredecessor(Move source, Move target)
    {
        if (source == null || target == null || source == target || !MoveGraphWindow.Targets(source, "after").Contains(target)) return false;
        SetMove(source);
        pairSource = source; pairTarget = target; pairActive = true; playing = true;
        lastUpdate = EditorApplication.timeSinceStartup;
        ActiveMoveChanged?.Invoke(source);
        return true;
    }

    private void DrawPredecessors()
    {
        if (GUILayout.Button("이 노드로 연결된 이전 행동 찾기"))
        {
            var target = move;
            EditorApplication.delayCall += () =>
            {
                if (this == null || target == null) return;
                incoming.Clear(); incoming.AddRange(FindPredecessors(target)); incomingFor = target; incomingIndex = 0;
                RefreshView();
            };
        }
        if (incomingFor == move)
        {
            if (incoming.Count == 0) EditorGUILayout.LabelField("이 노드를 다음 행동 후보로 연결한 Move가 없습니다.");
            else
            {
                incomingIndex = EditorGUILayout.Popup("이전 행동", Mathf.Clamp(incomingIndex, 0, incoming.Count - 1), incoming.Select(entry => entry != null ? entry.name + " — " + AssetDatabase.GetAssetPath(entry) : "삭제된 Move").ToArray());
                if (GUILayout.Button("선택한 이전 행동 → 현재 행동 재생"))
                {
                    var source = incoming[incomingIndex]; var target = move;
                    EditorApplication.delayCall += () => { if (this != null && !PlayPredecessor(source, target)) ShowNotification(new GUIContent("연결이 변경됐습니다. 이전 행동을 다시 찾으세요.")); };
                }
            }
        }
        if (pairSource != null && pairTarget != null)
        {
            EditorGUILayout.LabelField($"두 동작 미리보기: {pairSource.name} → {pairTarget.name}");
            if (GUILayout.Button("이 두 동작 다시 재생"))
            {
                var source = pairSource; var target = pairTarget;
                EditorApplication.delayCall += () => { if (this != null) PlayPredecessor(source, target); };
            }
        }
    }

    private void DrawMovement()
    {
        EditorGUILayout.LabelField("X / Y 이동", EditorStyles.boldLabel);
        startupSeconds = Mathf.Max(0, EditorGUILayout.FloatField("미리보기 준비 지연 (초)", startupSeconds));
        var so = new SerializedObject(move);
        var mode = so.FindProperty("movementMode");
        if ((MovementMode)mode.enumValueIndex != MovementMode.CurveXY)
        {
            editMovement = false;
            if (GUILayout.Button("이 Move에 X/Y 곡선 이동 사용"))
            {
                mode.enumValueIndex = (int)MovementMode.CurveXY;
                so.FindProperty("movementPhase").enumValueIndex = (int)MovementPhase.StartupAndActive;
                so.ApplyModifiedProperties();
            }
            if ((MovementMode)mode.enumValueIndex == MovementMode.StopAtRange || (MovementMode)mode.enumValueIndex == MovementMode.PassThroughTarget)
                EditorGUILayout.HelpBox("대상 위치에 따라 달라지는 기존 이동은 이 창에서 경로를 예측하지 않습니다. X/Y 곡선 이동을 사용하면 지정한 경로를 표시합니다.", MessageType.Info);
            return;
        }
        EditorGUILayout.PropertyField(so.FindProperty("movementPhase"), new GUIContent("이동 적용 구간"));
        EditorGUILayout.PropertyField(so.FindProperty("movementX"), new GUIContent("X 위치 곡선 (전방 +)"));
        EditorGUILayout.PropertyField(so.FindProperty("movementY"), new GUIContent("Y 위치 곡선 (위쪽 +)"));
        so.ApplyModifiedProperties();
        editMovement = EditorGUILayout.Toggle("이동 경로 편집 (하늘색 점 드래그)", editMovement);
        keyPosition = EditorGUILayout.Vector2Field("현재 시점에 넣을 위치", keyPosition);
        using (new EditorGUI.DisabledScope(MoveTimelineSampling.PhaseProgress(move, Progress, Startup) < .00001f))
            if (GUILayout.Button("현재 시점에 X/Y 위치 키 저장")) SetMovementKey();
        EditorGUILayout.LabelField("각 Move의 시작 위치는 (0, 0)입니다. 곡선 가로축: 진행률 0~1, 세로축: 이동 거리.");
        EditorGUILayout.LabelField($"현재 이동: {ActorOffset.x:0.00}, {ActorOffset.y:0.00}");
    }

    private void SetMovementKey()
    {
        var so = new SerializedObject(move);
        if ((MovementMode)so.FindProperty("movementMode").enumValueIndex != MovementMode.CurveXY) return;
        float progress = MoveTimelineSampling.PhaseProgress(move, Progress, Startup);
        if (progress < .00001f) return; // Each Move starts at zero displacement.
        Undo.RecordObject(move, "Edit Move XY key");
        for (int axis = 0; axis < 2; axis++)
        {
            var property = so.FindProperty(axis == 0 ? "movementX" : "movementY");
            var curve = property.animationCurveValue;
            float value = (axis == 0 ? keyPosition.x : keyPosition.y) + curve.Evaluate(0);
            int index = -1;
            for (int i = 0; i < curve.length; i++) if (Mathf.Abs(curve.keys[i].time - progress) < .0001f) { index = i; break; }
            var key = new Keyframe(progress, value);
            if (index >= 0) index = curve.MoveKey(index, key); else index = curve.AddKey(key);
            AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
            property.animationCurveValue = curve;
        }
        so.ApplyModifiedProperties();
    }

    private void DrawBody()
    {
        EditorGUILayout.LabelField("몸 피격 판정", EditorStyles.boldLabel);
        var body = Body;
        if (body == null)
        {
            EditorGUILayout.HelpBox("이 Move에는 몸 피격 판정이 없습니다.", MessageType.Info);
            if (GUILayout.Button("몸 피격 박스 만들기"))
                EditorApplication.delayCall += () => { if (this != null) { Save(); AddBody(); } };
            return;
        }
        EditorGUILayout.ObjectField("사용 중인 몸 콜라이더", body, typeof(Collider2D), false);
        if (body is BoxCollider2D box)
        {
            var data = new SerializedObject(box);
            EditorGUILayout.PropertyField(data.FindProperty("m_Offset"), new GUIContent("몸 중심 (로컬)"));
            EditorGUILayout.PropertyField(data.FindProperty("m_Size"), new GUIContent("몸 크기"));
            var size = data.FindProperty("m_Size").vector2Value;
            data.FindProperty("m_Size").vector2Value = new Vector2(Mathf.Max(.01f, size.x), Mathf.Max(.01f, size.y));
            if (data.ApplyModifiedProperties()) RefreshView();
            EditorGUILayout.HelpBox("몸 박스 내부를 드래그해 이동하고, 모서리의 노란 점으로 크기를 조절하세요. 이 Move 전체에 적용되는 피격 판정입니다. 무기 판정과 별도로 저장됩니다.", MessageType.None);
        }
        else
        {
            EditorGUILayout.HelpBox("기존 몸 판정은 " + body.GetType().Name + "입니다. 이 형태는 Collider Inspector에서 편집할 수 있습니다.", MessageType.Info);
            if (GUILayout.Button("몸 콜라이더 Inspector 열기")) Selection.activeObject = body;
        }
    }

    private void AddBody()
    {
        if (move == null || Body != null) return;
        var path = AssetDatabase.GetAssetPath(move);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var source = root.GetComponent<Move>();
            var data = new SerializedObject(source);
            if (data.FindProperty("bodyCollider").objectReferenceValue != null) return;
            var go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(root.transform, "BodyHitbox"));
            go.transform.SetParent(root.transform, false);
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.offset = new Vector2(0f, .9f);
            box.size = new Vector2(.6f, 1.8f);
            data.FindProperty("bodyCollider").objectReferenceValue = box;
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        move = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Move>();
        editBody = true;
        RefreshView();
    }

    private void DrawBoxes()
    {
        var boxes = move.GetComponentsInChildren<Hitbox>(true);
        if (boxes.Length == 0) { EditorGUILayout.LabelField("히트박스가 없습니다. 박스 추가를 누르세요."); return; }
        int index = Array.IndexOf(boxes, selected);
        selected = boxes[EditorGUILayout.Popup("편집할 히트박스", Mathf.Max(0, index), boxes.Select(b => b.name).ToArray())];
        var box = selected.GetComponent<BoxCollider2D>();
        if (box == null) { EditorGUILayout.HelpBox("이 창은 BoxCollider2D를 편집합니다. 다른 모양은 해당 Collider Inspector에서 편집하세요.", MessageType.Info); return; }
        var collider = new SerializedObject(box);
        EditorGUILayout.PropertyField(collider.FindProperty("m_Offset"), new GUIContent("중심 (로컬)"));
        EditorGUILayout.PropertyField(collider.FindProperty("m_Size"), new GUIContent("크기"));
        collider.ApplyModifiedProperties();
        var hit = new SerializedObject(selected);
        var start = hit.FindProperty("activeStart"); var end = hit.FindProperty("activeEnd");
        int first = Mathf.RoundToInt(start.floatValue * LastFrame), last = Mathf.RoundToInt(end.floatValue * LastFrame);
        EditorGUI.BeginChangeCheck();
        first = EditorGUILayout.IntSlider("판정 시작 프레임", first, 0, LastFrame);
        last = EditorGUILayout.IntSlider("판정 종료 프레임", last, first, LastFrame);
        if (EditorGUI.EndChangeCheck() && LastFrame > 0) { start.floatValue = (float)first / LastFrame; end.floatValue = (float)last / LastFrame; }
        EditorGUILayout.PropertyField(hit.FindProperty("damageCoef"), new GUIContent("데미지 계수"));
        EditorGUILayout.PropertyField(hit.FindProperty("stanceCoef"), new GUIContent("자세 데미지 계수"));
        hit.ApplyModifiedProperties();
        EditorGUILayout.LabelField(selected.IsActiveAt(Progress) ? "현재 프레임: 판정 활성" : "현재 프레임: 판정 비활성");
    }

    private void Save()
    {
        if (move != null) PrefabUtility.SavePrefabAsset(move.transform.root.gameObject);
        AssetDatabase.SaveAssets();
    }

    private void AddBox()
    {
        if (move == null) return;
        var path = AssetDatabase.GetAssetPath(move);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int number = 0;
            while (root.transform.Find("Hitbox_" + number) != null) number++;
            var go = new GameObject("Hitbox_" + number);
            go.transform.SetParent(root.transform, false);
            var box = go.AddComponent<BoxCollider2D>(); box.isTrigger = true; box.offset = new Vector2(.8f, 1f); box.size = new Vector2(1, .6f);
            var hitbox = go.AddComponent<Hitbox>();
            var so = new SerializedObject(root.GetComponent<Move>());
            var list = so.FindProperty("weaponHitboxes");
            var hitboxes = root.GetComponentsInChildren<Hitbox>(true);
            list.arraySize = hitboxes.Length;
            for (int i = 0; i < hitboxes.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = hitboxes[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        move = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Move>();
        selected = move.GetComponentsInChildren<Hitbox>(true).Last();
        RefreshView();
    }

    private void RemoveBox()
    {
        if (move == null || selected == null) return;
        if (selected.transform == move.transform || selected.transform.childCount > 0)
        {
            EditorUtility.DisplayDialog("삭제할 수 없는 오브젝트", "Move 루트나 자식이 있는 오브젝트는 Prefab Inspector에서 직접 편집하세요.", "확인");
            return;
        }
        Save();
        var relative = AnimationUtility.CalculateTransformPath(selected.transform, move.transform);
        var path = AssetDatabase.GetAssetPath(move);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var target = root.transform.Find(relative);
            if (target == null) return;
            UnityEngine.Object.DestroyImmediate(target.gameObject);
            var so = new SerializedObject(root.GetComponent<Move>());
            var list = so.FindProperty("weaponHitboxes");
            var hits = root.GetComponentsInChildren<Hitbox>(true);
            list.arraySize = hits.Length;
            for (int i = 0; i < hits.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = hits[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        selected = null;
        move = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Move>();
        RefreshView();
    }
}
