using System;
using System.Linq;
using Scripts;
using UnityEditor;
using UnityEngine;

internal static class MoveGraphValidation
{
    public static void Run()
    {
        const string folder = "Assets/__MoveGraphValidation";
        if (AssetDatabase.IsValidFolder(folder)) throw new Exception("Validation folder exists");
        AssetDatabase.CreateFolder("Assets", "__MoveGraphValidation");
        try
        {
            Move Make(string name)
            {
                var go = new GameObject(name); go.AddComponent<Move>();
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, folder+"/"+name+".prefab");
                UnityEngine.Object.DestroyImmediate(go);
                return prefab.GetComponent<Move>();
            }
            var a = Make("A"); var b = Make("B"); var c = Make("C");
            MoveGraphWindow.Connect(a,b,"after"); MoveGraphWindow.Connect(a,c,"after");
            MoveGraphWindow.Connect(a,b,"after");
            if (!MoveGraphWindow.Targets(a,"after").SequenceEqual(new[]{b,c})) throw new Exception("Order/duplicates");
            MoveGraphWindow.Connect(a,b,"guardMove"); MoveGraphWindow.Connect(a,c,"hitMove");
            if (MoveGraphWindow.Targets(a,"guardMove").Single()!=b || MoveGraphWindow.Targets(a,"hitMove").Single()!=c)
                throw new Exception("Branch mapping");
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            MoveGraphWindow.Connect(a,c,"guardMove"); Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            if (MoveGraphWindow.Targets(a,"guardMove").Single()!=b) throw new Exception("Undo");
            PrefabUtility.SavePrefabAsset(a.gameObject); AssetDatabase.SaveAssets();
            var loaded = PrefabUtility.LoadPrefabContents(folder+"/A.prefab");
            try
            {
                var move = loaded.GetComponent<Move>();
                if (!MoveGraphWindow.Targets(move,"after").SequenceEqual(new[]{b,c}) ||
                    MoveGraphWindow.Targets(move,"hitMove").Single()!=c) throw new Exception("Persistence");
            }
            finally { PrefabUtility.UnloadPrefabContents(loaded); }
            Debug.Log("MOVE_GRAPH_VALIDATION_PASS order duplicate branches undo prefab persistence");
        }
        finally { AssetDatabase.DeleteAsset(folder); }
    }
}
