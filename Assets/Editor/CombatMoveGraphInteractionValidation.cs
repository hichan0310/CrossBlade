using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Scripts;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

internal static class CombatMoveGraphInteractionValidation
{
    static MoveGraphWindow window;
    static CombatMoveGraphAsset asset;
    static int ticks, phase, errors;
    static Vector2 before, press;
    static VisualElement pressed;
    const string Folder = "Assets/__NativeGraphCheck";
    public static void Run()
    {
        if (AssetDatabase.IsValidFolder(Folder)) throw new InvalidOperationException("Test folder already exists");
        AssetDatabase.CreateFolder("Assets", "__NativeGraphCheck");
        asset=ScriptableObject.CreateInstance<CombatMoveGraphAsset>();
        for(int i=0;i<100;i++)
        {
            var move=MoveAssetCreation.CreateAtPath(Folder+"/Move"+i.ToString("000")+".prefab");
            asset.nodes.Add(new CombatMoveGraphNode {move=move, position=new Vector2(i%10*300,i/10*220)});
        }
        asset.startMove=asset.nodes[0].move;
        MoveGraphWindow.Connect(asset.nodes[0].move,asset.nodes[1].move,"after");
        AssetDatabase.CreateAsset(asset,Folder+"/Graph.asset");
        window=EditorWindow.GetWindow<MoveGraphWindow>();window.OpenGraph(asset);window.position=new Rect(50,50,1400,1000);window.Show();window.Focus(); Undo.ClearAll();
        Application.logMessageReceived += Log; EditorApplication.update += Step;
    }
    static void Log(string text,string stack,LogType type) { if(type==LogType.Error||type==LogType.Exception) errors++; }
    static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    static bool Within(VisualElement child,VisualElement ancestor) { for(var e=child;e!=null;e=e.parent) if(e==ancestor)return true;return false; }
    static void Mouse(VisualElement target, EventType type,Vector2 position,int button=0)
    {
        var raw=new Event { type=type, mousePosition=position, button=button, clickCount=1 };
        if(type==EventType.MouseDown) {using(var e=MouseDownEvent.GetPooled(raw))target.SendEvent(e);}
        if(type==EventType.MouseDrag) {using(var e=MouseMoveEvent.GetPooled(raw))target.SendEvent(e);}
        if(type==EventType.MouseUp) {using(var e=MouseUpEvent.GetPooled(raw))target.SendEvent(e);}
    }
    static void Step()
    {
        if(++ticks%15!=0)return;
        try
        {
            var view=window.canvasView;
            var node=view.Views[asset.nodes[0].move];
            switch(phase++)
            {
                case 0:
                    Require(view.Views.Count==100,"100 nodes retained");view.UpdateViewTransform(new Vector3(80,70,0),Vector3.one*.4f);break;
                case 1:
                    press=node.titleContainer.worldBound.center;
                    Require(view.worldBound.Contains(press),"Node visible after zoom");
                    pressed=view.panel.Pick(press);
                    Require(Within(pressed,node),"Zoomed title picks correct node");
                    var outside=new Vector2(view.worldBound.x+10,view.worldBound.y-5);
                    Require(!Within(view.panel.Pick(outside),view),"Graph cannot intercept toolbar clicks");
                    before=node.GetPosition().position;
                    Mouse(pressed,EventType.MouseDown,press);break;
                case 2:
                    Mouse(view,EventType.MouseDrag,press+new Vector2(80,40));break;
                case 3:
                    Mouse(view,EventType.MouseUp,press+new Vector2(80,40));break;
                case 4:
                    Require(Vector2.Distance(node.GetPosition().position,before)>100,"Actual scaled mouse drag moves node");
                    Require(Vector2.Distance(node.GetPosition().position,asset.nodes[0].position)<.1f,"Drag persisted graph coordinates");
                    view.UpdateViewTransform(new Vector3(40,30,0),Vector3.one*1.5f);
                    node.SetPosition(new Rect(Vector2.zero,node.GetPosition().size));break;
                case 5:
                    Require(Within(view.panel.Pick(node.titleContainer.worldBound.center),node),"150 percent zoom picks correct node");
                    Undo.IncrementCurrentGroup(); var edge=view.edges.First();view.DeleteElements(new[]{edge});
                    Require(!MoveGraphWindow.Targets(asset.nodes[0].move,"after").Any(),"Native edge deletion updates Move");
                    Undo.FlushUndoRecordObjects(); Undo.PerformUndo();break;
                case 6:
                    Require(MoveGraphWindow.Targets(asset.nodes[0].move,"after").Contains(asset.nodes[1].move),"Connection Undo restores actual reference");
                    Require(errors==0,"No GUI exceptions");
                    Debug.Log("NATIVE_GRAPH_INTERACTION_PASS 100 nodes, 40/150 percent picking, toolbar clipping, actual mouse drag, saved coordinates, edge deletion and Undo");
                    EditorApplication.update-=Step;window.Close();AssetDatabase.DeleteAsset(Folder);EditorApplication.Exit(0);break;
            }
            window.Repaint();
        }
        catch(Exception e){EditorApplication.update-=Step;Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
