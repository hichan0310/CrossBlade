using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MotionPrototype
{
    internal sealed class MotionGraphWindow : EditorWindow
    {
        private MotionGraphAsset _graph;
        private MotionSequenceAsset _sequenceToAdd;
        private string _from;
        private string _to;
        private string _trigger = "Next";
        private Vector2 _pan = new(280, 180);

        public static void Open() => GetWindow<MotionGraphWindow>("Motion Graph");

        public static void OpenWithSequence(MotionSequenceAsset sequence)
        {
            var window = GetWindow<MotionGraphWindow>("Motion Graph");
            window._sequenceToAdd = sequence;
            window.Show(); window.Focus();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _graph = (MotionGraphAsset)EditorGUILayout.ObjectField(_graph, typeof(MotionGraphAsset), false, GUILayout.Width(240));
            if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(45))) NewGraph();
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(45))) SaveGraph();
            GUILayout.Space(10);
            _sequenceToAdd = (MotionSequenceAsset)EditorGUILayout.ObjectField(_sequenceToAdd, typeof(MotionSequenceAsset), false, GUILayout.Width(220));
            using (new EditorGUI.DisabledScope(_sequenceToAdd == null))
                if (GUILayout.Button("Add node", EditorStyles.toolbarButton, GUILayout.Width(70))) AddNode(_sequenceToAdd);
            EditorGUILayout.EndHorizontal();
            if (_graph == null)
            {
                EditorGUILayout.HelpBox("Create or select a graph. Nodes reference non-destructive Motion Sequence assets; edges record named transitions.", MessageType.Info);
                return;
            }
            DrawConnectionsPanel();
            var canvas = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(canvas, new Color(.12f, .13f, .15f));
            HandlePan(canvas);
            DrawGrid(canvas, 24, new Color(1, 1, 1, .04f));
            Handles.BeginGUI();
            foreach (var edge in _graph.edges)
            {
                var from = _graph.nodes.FirstOrDefault(node => node.id == edge.from);
                var to = _graph.nodes.FirstOrDefault(node => node.id == edge.to);
                if (from == null || to == null) continue;
                var a = canvas.position + _pan + from.position + new Vector2(170, 30);
                var b = canvas.position + _pan + to.position + new Vector2(0, 30);
                Handles.DrawBezier(a, b, a + Vector2.right * 60, b + Vector2.left * 60, new Color(.35f, .8f, 1), null, 3);
                GUI.Label(new Rect((a + b) * .5f - new Vector2(35, 10), new Vector2(100, 20)), edge.trigger, EditorStyles.miniLabel);
            }
            Handles.EndGUI();
            BeginWindows();
            for (var index = 0; index < _graph.nodes.Count; index++)
            {
                var node = _graph.nodes[index];
                var rect = new Rect(canvas.position + _pan + node.position, new Vector2(170, 62));
                var next = GUI.Window(index + 1000, rect, _ => DrawNode(node), string.IsNullOrEmpty(node.label) ? node.sequence?.name : node.label);
                node.position = next.position - canvas.position - _pan;
            }
            EndWindows();
            if (GUI.changed) EditorUtility.SetDirty(_graph);
        }

        private void DrawConnectionsPanel()
        {
            EditorGUILayout.BeginHorizontal("box");
            var labels = _graph.nodes.Select(node => string.IsNullOrEmpty(node.label) ? node.sequence?.name ?? "Missing" : node.label).ToArray();
            var fromIndex = Mathf.Max(0, Array.FindIndex(_graph.nodes.ToArray(), node => node.id == _from));
            var toIndex = Mathf.Max(0, Array.FindIndex(_graph.nodes.ToArray(), node => node.id == _to));
            using (new EditorGUI.DisabledScope(labels.Length == 0))
            {
                fromIndex = EditorGUILayout.Popup("From", fromIndex, labels);
                toIndex = EditorGUILayout.Popup("To", toIndex, labels);
                _trigger = EditorGUILayout.TextField("Trigger", _trigger);
                if (labels.Length > 0) { _from = _graph.nodes[fromIndex].id; _to = _graph.nodes[toIndex].id; }
                if (GUILayout.Button("Connect", GUILayout.Width(70)) && _from != _to)
                {
                    Undo.RecordObject(_graph, "Connect motion nodes");
                    _graph.edges.Add(new MotionGraphEdge { from = _from, to = _to, trigger = _trigger });
                }
            }
            EditorGUILayout.EndHorizontal();
            for (var index = 0; index < _graph.edges.Count; index++)
            {
                var edge = _graph.edges[index];
                var from = _graph.nodes.FirstOrDefault(node => node.id == edge.from);
                var to = _graph.nodes.FirstOrDefault(node => node.id == edge.to);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{from?.label ?? from?.sequence?.name} —[{edge.trigger}]→ {to?.label ?? to?.sequence?.name}");
                if (GUILayout.Button("Remove", GUILayout.Width(65))) { Undo.RecordObject(_graph, "Remove edge"); _graph.edges.RemoveAt(index--); }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawNode(MotionGraphNode node)
        {
            node.label = EditorGUILayout.TextField(node.label);
            node.sequence = (MotionSequenceAsset)EditorGUILayout.ObjectField(node.sequence, typeof(MotionSequenceAsset), false);
            GUI.DragWindow();
        }

        private void HandlePan(Rect canvas)
        {
            var e = Event.current;
            if (canvas.Contains(e.mousePosition) && e.type == EventType.MouseDrag && e.button == 2)
            {
                _pan += e.delta; e.Use(); Repaint();
            }
        }

        private static void DrawGrid(Rect rect, float spacing, Color color)
        {
            Handles.BeginGUI(); Handles.color = color;
            for (var x = rect.x; x < rect.xMax; x += spacing) Handles.DrawLine(new Vector3(x, rect.y), new Vector3(x, rect.yMax));
            for (var y = rect.y; y < rect.yMax; y += spacing) Handles.DrawLine(new Vector3(rect.x, y), new Vector3(rect.xMax, y));
            Handles.EndGUI();
        }

        private void AddNode(MotionSequenceAsset sequence)
        {
            EnsureGraph(); Undo.RecordObject(_graph, "Add motion node");
            var node = new MotionGraphNode { label = sequence.name, sequence = sequence, position = new Vector2(_graph.nodes.Count * 190, 0) };
            _graph.nodes.Add(node); _from ??= node.id; _to ??= node.id; EditorUtility.SetDirty(_graph);
        }

        private void EnsureGraph() { if (_graph == null) { _graph = CreateInstance<MotionGraphAsset>(); _graph.name = "Unsaved Motion Graph"; } }
        private void NewGraph() { _graph = CreateInstance<MotionGraphAsset>(); _graph.name = "Unsaved Motion Graph"; }
        private void SaveGraph()
        {
            EnsureGraph();
            if (AssetDatabase.Contains(_graph)) { AssetDatabase.SaveAssets(); return; }
            var path = EditorUtility.SaveFilePanelInProject("Save Motion Graph", "MotionGraph", "asset", "Choose an asset path.");
            if (string.IsNullOrEmpty(path)) return;
            AssetDatabase.CreateAsset(_graph, path); AssetDatabase.SaveAssets();
        }
    }
}
