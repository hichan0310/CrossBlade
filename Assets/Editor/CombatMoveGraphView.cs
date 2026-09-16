using System.Collections.Generic;
using System.Linq;
using Scripts;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class CombatMoveGraphView : GraphView
{
    internal sealed class MoveNode : Node
    {
        internal Move Move;
        internal Port Input;
        internal readonly Dictionary<string, Port> Outputs = new();
        private readonly MoveGraphWindow owner;
        internal MoveNode(Move move, MoveGraphWindow window)
        {
            Move = move; owner = window; title = move.name;
            style.width = 240;
            Input = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(Move));
            Input.portName = "이전 행동"; inputContainer.Add(Input);
            var names = new[] { "after", "guardMove", "hitMove" };
            var labels = new[] { "다음 행동", "방어", "피격" };
            var colors = new[] { Color.cyan, Color.green, new Color(1, .3f, .3f) };
            for (int i = 0; i < names.Length; i++)
            {
                var port = InstantiatePort(Orientation.Horizontal, Direction.Output, i == 0 ? Port.Capacity.Multi : Port.Capacity.Single, typeof(Move));
                port.portName = labels[i]; port.userData = names[i]; port.portColor = colors[i];
                if (i > 0 && !MoveGraphWindow.Targets(move, names[i]).Any(target => target != null))
                {
                    var fallback = owner.GraphTargets(move, names[i]).FirstOrDefault();
                    port.portName += fallback != null ? " · 공통: " + fallback.name : " · 공통 미설정";
                }
                Outputs[names[i]] = port; outputContainer.Add(port);
            }
            RefreshExpandedState(); RefreshPorts();
        }
        public override void OnSelected() { base.OnSelected(); owner.SelectNode(Move); }
    }
    private readonly MoveGraphWindow owner;
    internal readonly Dictionary<Move, MoveNode> Views = new();
    private bool rebuilding;
    internal CombatMoveGraphView(MoveGraphWindow window)
    {
        owner = window;
        SetupZoom(.1f, 2f);
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());
        this.AddManipulator(new ClickSelector());
        Insert(0, new GridBackground());
        graphViewChanged = Changed;
        viewTransformChanged = _ => { if (!rebuilding) owner.SetView(viewTransform.position, viewTransform.scale.x); };
        RegisterCallback<DragUpdatedEvent>(evt => { DragAndDrop.visualMode = DragAndDropVisualMode.Link; evt.StopPropagation(); });
        RegisterCallback<DragPerformEvent>(evt =>
        {
            DragAndDrop.AcceptDrag();
            foreach (var obj in DragAndDrop.objectReferences)
            {
                var move = obj as Move ?? (obj as GameObject)?.GetComponent<Move>();
                if (move != null && EditorUtility.IsPersistent(move)) owner.DropMove(move);
            }
            evt.StopPropagation();
        });
    }
    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter adapter) => ports.Where(p => p.direction != startPort.direction && p.portType == startPort.portType).ToList();
    internal void Reload(Move[] moves, Dictionary<Move, Vector2> positions, Vector2 pan, float zoom)
    {
        rebuilding = true;
        foreach (var edge in edges.ToList()) { edge.input?.Disconnect(edge); edge.output?.Disconnect(edge); RemoveElement(edge); }
        foreach (var node in nodes.ToList()) RemoveElement(node);
        Views.Clear();
        foreach (var move in moves)
        {
            var node = new MoveNode(move, owner); node.SetPosition(new Rect(positions[move], new Vector2(240, 180)));
            Views[move] = node; AddElement(node);
        }
        foreach (var node in Views.Values)
            foreach (var pair in node.Outputs)
                foreach (var target in MoveGraphWindow.Targets(node.Move, pair.Key))
                    if (target != null && Views.TryGetValue(target, out var other)) AddElement(pair.Value.ConnectTo(other.Input));
        UpdateViewTransform(pan, Vector3.one * zoom);
        rebuilding = false;
    }
    private GraphViewChange Changed(GraphViewChange change)
    {
        if (rebuilding) return change;
        if (change.elementsToRemove != null)
            foreach (var element in change.elementsToRemove)
            {
                if (element is Edge edge) MoveGraphWindow.Disconnect(((MoveNode)edge.output.node).Move, ((MoveNode)edge.input.node).Move, (string)edge.output.userData);
                if (element is MoveNode node) { Views.Remove(node.Move); owner.RemoveNode(node.Move); }
            }
        if (change.edgesToCreate != null)
            foreach (var edge in change.edgesToCreate)
                MoveGraphWindow.Connect(((MoveNode)edge.output.node).Move, ((MoveNode)edge.input.node).Move, (string)edge.output.userData);
        if (change.movedElements != null)
            foreach (var node in change.movedElements.OfType<MoveNode>()) owner.MoveNode(node.Move, node.GetPosition().position);
        owner.StoreGraph();
        if (change.edgesToCreate?.Count > 0 || change.elementsToRemove?.Count > 0)
            schedule.Execute(() => owner.RefreshGraphUI());
        return change;
    }
    internal void HighlightMove(Move move)
    {
        if (!Views.TryGetValue(move, out var node)) { owner.RefreshGraphUI(); if (!Views.TryGetValue(move, out node)) return; }
        ClearSelection(); AddToSelection(node);
    }
    internal void FocusMove(Move move) { HighlightMove(move); FrameSelection(); }
}
