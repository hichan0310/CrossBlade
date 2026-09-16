using System;
using System.Collections.Generic;
using System.Linq;
using Scripts;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

// Edits the real Move prefab references consumed by PlayerPlanUI and ActorActionController.
public sealed class MoveGraphWindow : EditorWindow
{
    private readonly List<Move> nodes = new();
    private readonly Dictionary<Move, Vector2> positions = new();
    private Move root, selected;
    private Vector2 pan = new(30, 30), inspectorScroll;
    private MoveHitboxWindow motionEditor;
    private int inspectorTab;
    [SerializeField] private CombatMoveGraphAsset graph;
    private float graphZoom = 1f;
    internal CombatMoveGraphView canvasView;
    private IMGUIContainer inspectorView;
    private ScrollView nodeList;
    private ObjectField graphField, rootField;
    private string uiSearch = "";
    private static readonly string[] Ports = { "after", "guardMove", "hitMove" };

    [MenuItem("CrossBlade/Combat Move Graph")]
    public static void Open()
    {
        var window = GetWindow<MoveGraphWindow>("Combat Move Graph");
        window.minSize = new Vector2(1150, 720);
        if (Selection.activeObject is CombatMoveGraphAsset saved) { window.OpenGraph(saved); return; }
        var move = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Move>() : null;
        if (move != null && EditorUtility.IsPersistent(move)) window.Load(move);
    }

    private void OnEnable()
    {
        Undo.undoRedoPerformed += OnUndo;
        if (graph != null) EditorApplication.delayCall += () => { if (this != null && graph != null) OpenGraph(graph); };
    }

    [UnityEditor.Callbacks.OnOpenAsset]
    private static bool OpenGraphFile(int instanceId, int line)
    {
        var asset = EditorUtility.InstanceIDToObject(instanceId) as CombatMoveGraphAsset;
        if (asset == null) return false;
        EditorApplication.delayCall += () => GetWindow<MoveGraphWindow>("Combat Move Graph").OpenGraph(asset);
        return true;
    }

    internal void OpenGraph(CombatMoveGraphAsset asset)
    {
        if (graph != asset) StoreGraph();
        graph = asset;
        nodes.Clear(); positions.Clear();
        foreach (var entry in graph.nodes)
            if (entry.move != null && !nodes.Contains(entry.move)) { nodes.Add(entry.move); positions[entry.move] = entry.position; }
        root = graph.startMove; selected = root != null ? root : nodes.FirstOrDefault();
        pan = graph.pan; graphZoom = Mathf.Clamp(graph.zoom, .1f, 2f);
        if (motionEditor != null) { DestroyImmediate(motionEditor); motionEditor = null; }
        minSize = new Vector2(950, 620);
        RefreshGraphUI();
        Repaint();
    }

    internal void StoreGraph()
    {
        if (graph == null) return;
        graph.startMove = root; graph.pan = pan; graph.zoom = graphZoom;
        graph.nodes = nodes.Where(n => n != null).Select(n => new CombatMoveGraphNode { move = n, position = positions[n] }).ToList();
        if (motionEditor != null) motionEditor.StoreGraphSettings(graph);
        EditorUtility.SetDirty(graph);
    }

    internal void AddMove(Move move)
    {
        if (move == null || nodes.Contains(move)) return;
        positions[move] = new Vector2((nodes.Count % 4) * 245, (nodes.Count / 4) * 180);
        nodes.Add(move);
        if (root == null) root = move;
    }

    internal void AddFolderMoves(string folder)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetDirectoryName(path).Replace('\\', '/') != folder) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var move in prefab.GetComponentsInChildren<Move>(true)) AddMove(move);
        }
        StoreGraph(); RefreshGraphUI(); canvasView?.schedule.Execute(() => canvasView.FrameAll()).ExecuteLater(100);
    }

    private void SaveGraphFile()
    {
        if (graph == null)
        {
            var path = EditorUtility.SaveFilePanelInProject("그래프 전체 저장", "CombatMoveGraph", "asset", "노드 전체와 배치를 저장합니다.");
            if (string.IsNullOrEmpty(path) || System.IO.File.Exists(path)) return;
            graph = CreateInstance<CombatMoveGraphAsset>();
            AssetDatabase.CreateAsset(graph, path);
        }
        StoreGraph();
        foreach (var node in nodes.Where(n => n != null)) PrefabUtility.SavePrefabAsset(node.transform.root.gameObject);
        AssetDatabase.SaveAssets();
        graphField?.SetValueWithoutNotify(graph);
    }
    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndo;
        StoreGraph();
        if (graph != null && AssetDatabase.Contains(graph)) AssetDatabase.SaveAssets();
        if (motionEditor != null) DestroyImmediate(motionEditor);
        motionEditor = null;
    }

    private void Load(Move move)
    {
        root = move; selected = move;
        if (move == null) return;
        var visited = new HashSet<Move>();
        var queue = new Queue<Move>(nodes); queue.Enqueue(move);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == null || !visited.Add(current)) continue;
            AddMove(current);
            foreach (var field in Ports)
                foreach (var target in Targets(current, field))
                    if (target != null && !visited.Contains(target)) queue.Enqueue(target);
        }
        StoreGraph(); RefreshGraphUI();
    }

    internal static IEnumerable<Move> Targets(Move move, string field)
    {
        var property = new SerializedObject(move).FindProperty(field);
        if (field == "after")
        {
            for (var i = 0; i < property.arraySize; i++)
                yield return property.GetArrayElementAtIndex(i).objectReferenceValue as Move;
        }
        else yield return property.objectReferenceValue as Move;
    }

    internal static void Connect(Move from, Move to, string field)
    {
        if (from == null || to == null || !EditorUtility.IsPersistent(from) || !EditorUtility.IsPersistent(to))
            throw new InvalidOperationException("프로젝트에 저장된 Move 프리팹끼리 연결하세요.");
        if (!Ports.Contains(field)) throw new ArgumentException("Unknown port");
        var so = new SerializedObject(from);
        var property = so.FindProperty(field);
        if (field == "after")
        {
            if (Targets(from, field).Contains(to)) return;
            property.InsertArrayElementAtIndex(property.arraySize);
            property.GetArrayElementAtIndex(property.arraySize - 1).objectReferenceValue = to;
        }
        else property.objectReferenceValue = to;
        so.ApplyModifiedProperties();
    }

    private void OnUndo()
    {
        if (graph != null) OpenGraph(graph);
        else RefreshGraphUI();
    }

    public void CreateGUI()
    {
        rootVisualElement.Clear();
        rootVisualElement.style.flexDirection = FlexDirection.Column;
        var toolbar = new Toolbar();
        graphField = new ObjectField("그래프") { objectType = typeof(CombatMoveGraphAsset), allowSceneObjects = false };
        graphField.style.flexGrow = 1;
        graphField.SetValueWithoutNotify(graph);
        graphField.RegisterValueChangedCallback(evt => { if (evt.newValue is CombatMoveGraphAsset asset) OpenGraph(asset); });
        toolbar.Add(graphField);
        toolbar.Add(new ToolbarButton(SaveGraphFile) { text = "저장" });
        toolbar.Add(new ToolbarButton(MotionPrototype.MotionComposerWindow.OpenForMove) { text = "애니메이션 → Move" });
        toolbar.Add(new ToolbarButton(() => { StoreGraph(); graph = null; nodes.Clear(); positions.Clear(); root = selected = null; RefreshGraphUI(); }) { text = "새 그래프" });
        toolbar.Add(new ToolbarButton(() =>
        {
            var created = MoveAssetCreation.CreateWithDialog();
            if (created == null) return;
            AddMove(created); selected = created; StoreGraph(); RefreshGraphUI();
        }) { text = "빈 Move 만들기" });
        rootVisualElement.Add(toolbar);
        var row = new Toolbar();
        rootField = new ObjectField("시작 Move") { objectType = typeof(Move), allowSceneObjects = false };
        rootField.style.flexGrow = 1; rootField.SetValueWithoutNotify(root);
        rootField.RegisterValueChangedCallback(evt => Load(evt.newValue as Move));
        row.Add(rootField);
        row.Add(new ToolbarButton(() => AddFolderMoves(root == null ? "Assets" : System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(root)).Replace('\\', '/'))) { text = "같은 폴더 추가" });
        row.Add(new ToolbarButton(() => Load(root)) { text = "연결 새로고침" });
        rootVisualElement.Add(row);
        var split = new TwoPaneSplitView(1, 480, TwoPaneSplitViewOrientation.Horizontal);
        split.style.flexGrow = 1; split.style.minHeight = 0;
        var left = new VisualElement(); left.style.minWidth = 250; left.style.flexGrow = 1; left.style.overflow = Overflow.Hidden;
        var controls = new Toolbar();
        var search = new ToolbarSearchField(); search.style.flexGrow = 1;
        search.RegisterValueChangedCallback(evt => { uiSearch = evt.newValue; RefreshNodeList(); }); controls.Add(search);
        controls.Add(new ToolbarButton(() => canvasView.FrameAll()) { text = "전체 보기" });
        controls.Add(new ToolbarButton(() =>
        {
            if (graph != null) Undo.RecordObject(graph, "Arrange graph");
            for (int i = 0; i < nodes.Count; i++) positions[nodes[i]] = new Vector2(i % 4 * 300, i / 4 * 220);
            StoreGraph(); RefreshGraphUI(); canvasView.schedule.Execute(() => canvasView.FrameAll()).ExecuteLater(100);
        }) { text = "자동 배치" });
        left.Add(controls);
        var listFoldout = new Foldout { text = "전체 노드 검색 목록", value = false };
        nodeList = new ScrollView(); nodeList.style.height = 120; nodeList.style.flexShrink = 0;
        listFoldout.Add(nodeList); left.Add(listFoldout);
        canvasView = new CombatMoveGraphView(this);
        canvasView.style.flexGrow = 1; canvasView.style.minHeight = 100; canvasView.style.overflow = Overflow.Hidden;
        left.Add(canvasView); split.Add(left);
        inspectorView = new IMGUIContainer(DrawInspector); inspectorView.style.flexGrow = 1; inspectorView.style.minWidth = 320; inspectorView.style.overflow = Overflow.Hidden;
        split.Add(inspectorView); rootVisualElement.Add(split);
        rootVisualElement.Add(new Label("포트 드래그로 연결 · 선 선택 후 Delete로 연결 삭제 · 휠 확대 · 중간 버튼 이동 · 노드 제목 드래그 · 경계선으로 편집 영역 너비 조절"));
        RefreshGraphUI();
    }

    private void RefreshNodeList()
    {
        if (nodeList == null) return;
        nodeList.Clear();
        foreach (var move in nodes.Where(n => n != null && n.name.IndexOf(uiSearch, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            var target = move;
            nodeList.Add(new Button(() => { SelectNode(target); canvasView.FocusMove(target); }) { text = move.name + " · " + AssetDatabase.GetAssetPath(move) });
        }
    }

    internal void RefreshGraphUI()
    {
        graphField?.SetValueWithoutNotify(graph); rootField?.SetValueWithoutNotify(root);
        canvasView?.Reload(nodes.Where(n => n != null).ToArray(), positions, pan * graphZoom, graphZoom);
        RefreshNodeList(); inspectorView?.MarkDirtyRepaint();
    }
    internal void SelectNode(Move move) { selected = move; inspectorView?.MarkDirtyRepaint(); Repaint(); }
    internal void MoveNode(Move move, Vector2 position)
    {
        if (graph != null) Undo.RecordObject(graph, "Move graph node");
        positions[move] = position; StoreGraph();
    }
    internal void SetView(Vector2 position, float scale) { graphZoom = scale; pan = position / scale; }
    internal void DropMove(Move move) { AddMove(move); StoreGraph(); RefreshGraphUI(); }

    internal static void OpenWithCreatedMove(Move move)
    {
        if (move == null) return;
        var window = GetWindow<MoveGraphWindow>("Combat Move Graph");
        window.AddMove(move);
        window.selected = move;
        window.StoreGraph();
        window.RefreshGraphUI();
        window.Show();
        window.Focus();
        window.canvasView?.schedule.Execute(() => window.canvasView.FocusMove(move)).ExecuteLater(100);
    }
    internal void RemoveNode(Move move)
    {
        if (graph != null) Undo.RecordObject(graph, "Remove graph node");
        nodes.Remove(move); positions.Remove(move);
        if (root == move) root = nodes.FirstOrDefault();
        if (selected == move) selected = root;
        StoreGraph(); RefreshNodeList();
    }
    internal static void Disconnect(Move from, Move to, string field)
    {
        var so = new SerializedObject(from); var property = so.FindProperty(field);
        if (field == "after")
        {
            for (int i = property.arraySize - 1; i >= 0; i--)
                if (property.GetArrayElementAtIndex(i).objectReferenceValue == to)
                { property.GetArrayElementAtIndex(i).objectReferenceValue = null; property.DeleteArrayElementAtIndex(i); }
        }
        else if (property.objectReferenceValue == to) property.objectReferenceValue = null;
        so.ApplyModifiedProperties();
    }

    internal IEnumerable<Move> GraphTargets(Move move, string field) => EffectiveTargets(move, field, graph);

    internal static IEnumerable<Move> EffectiveTargets(Move move, string field, CombatMoveGraphAsset context = null)
    {
        if (move == null) return Array.Empty<Move>();
        if (field == "after") return Targets(move, field);
        var target = context != null ? context.ResolveReaction(move, field == "guardMove")
            : field == "guardMove" ? move.ResolvedGuardMove : move.ResolvedHitMove;
        return target == null ? Array.Empty<Move>() : new[] { target };
    }

    private Move DrawCommonChoice(string label, Move current)
    {
        var choices = nodes.Where(n => n != null).Distinct().ToList();
        var labels = new[] { "없음" }.Concat(choices.Select(n => n.name + " · " + AssetDatabase.GetAssetPath(n))).ToArray();
        int index = current == null ? 0 : choices.IndexOf(current) + 1;
        if (current != null && index == 0)
            EditorGUILayout.HelpBox(label + " 대상이 현재 그래프에 없습니다. 다시 선택하세요.", MessageType.Warning);
        int next = EditorGUILayout.Popup(label, index, labels);
        return next == index ? current : next == 0 ? null : choices[next - 1];
    }

    private void DrawReactionDefaults()
    {
        EditorGUILayout.LabelField("이 그래프의 공통 방어 · 피격", EditorStyles.boldLabel);
        if (graph == null)
        {
            if (GUILayout.Button("그래프 저장 후 공통 설정")) SaveGraphFile();
            return;
        }
        EditorGUI.BeginChangeCheck();
        var guard = DrawCommonChoice("공통 방어", graph.defaultGuardMove);
        var hit = DrawCommonChoice("공통 피격", graph.defaultHitMove);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(graph, "Edit graph reactions");
            graph.defaultGuardMove = guard; graph.defaultHitMove = hit;
            StoreGraph(); AssetDatabase.SaveAssets(); RefreshGraphUI();
        }
        EditorGUILayout.LabelField("현재 그래프 노드에서 선택 · 개별 연결 우선", EditorStyles.miniLabel);
        if (GUILayout.Button("공통값과 같은 개별 연결 정리"))
        {
            foreach (var node in nodes.Where(n => n != null))
            {
                var data = new SerializedObject(node);
                foreach (var field in new[] { "guardMove", "hitMove" })
                {
                    var common = field == "guardMove" ? graph.defaultGuardMove : graph.defaultHitMove;
                    if (common != null && data.FindProperty(field).objectReferenceValue == common)
                        data.FindProperty(field).objectReferenceValue = null;
                }
                data.ApplyModifiedProperties();
            }
            SaveGraphFile(); RefreshGraphUI();
        }
    }

    private void DrawInspector()
    {
        EditorGUILayout.BeginVertical();
        DrawReactionDefaults();
        inspectorTab = GUILayout.Toolbar(inspectorTab, new[] { "모션 · 이동 · 히트박스", "전투 분기 설정" });
        if (inspectorTab == 0)
        {
            if (selected == null) EditorGUILayout.HelpBox("왼쪽 그래프에서 행동 노드를 선택하세요.", MessageType.Info);
            else
            {
                if (motionEditor == null)
                {
                    motionEditor = CreateInstance<MoveHitboxWindow>();
                    if (graph != null) motionEditor.LoadGraphSettings(graph);
                    motionEditor.HostRepaint = () => { inspectorView?.MarkDirtyRepaint(); Repaint(); };
                    motionEditor.ActiveMoveChanged = target =>
                    {
                        selected = target;
                        if (!nodes.Contains(target))
                        {
                            positions[target] = new Vector2((nodes.Count % 4) * 245, (nodes.Count / 4) * 180);
                            nodes.Add(target);
                        }
                        canvasView?.HighlightMove(target);
                        inspectorView?.MarkDirtyRepaint(); Repaint();
                    };
                }
                motionEditor.ReactionTargets = GraphTargets;
                motionEditor.SelectGraphMove(selected);
                motionEditor.DrawEmbedded();
            }
            EditorGUILayout.EndVertical();
            return;
        }
        motionEditor?.PausePlayback();
        inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
        if (selected != null)
        {
            EditorGUILayout.LabelField(selected.name, EditorStyles.boldLabel);
            var so = new SerializedObject(selected); so.Update();
            foreach (var field in new[] { "moveId", "category", "duration", "characterAnimation" })
                EditorGUILayout.PropertyField(so.FindProperty(field));
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(so.FindProperty("after"),new GUIContent("다음 행동 후보 (순서 유지)"),true);
            EditorGUILayout.PropertyField(so.FindProperty("guardMove"),new GUIContent("방어 개별 연결 (없으면 공통)"));
            EditorGUILayout.PropertyField(so.FindProperty("hitMove"),new GUIContent("피격 개별 연결 (없으면 공통)"));
            EditorGUILayout.PropertyField(so.FindProperty("guardable"),new GUIContent("방어 가능"));
            EditorGUILayout.PropertyField(so.FindProperty("skipAdditionalInterruptFollowUp"),new GUIContent("추가 인터럽트 후속 생략"));
            if (so.ApplyModifiedProperties()) RefreshGraphUI();
            EditorGUILayout.HelpBox("다음 행동은 플레이어/AI의 선택 후보입니다. 방어·피격의 개별 연결을 None으로 지우면 공통 설정을 따릅니다. 공통 설정도 없으면 분기 대상은 없습니다.",MessageType.Info);
            if (GUILayout.Button("Project에서 찾기")) EditorGUIUtility.PingObject(selected);
            if (GUILayout.Button("모션 보면서 히트박스 편집"))
            {
                inspectorTab = 0;
            }
        }
        else EditorGUILayout.LabelField("노드를 선택하세요.");
        EditorGUILayout.EndScrollView(); EditorGUILayout.EndVertical();
    }
}
