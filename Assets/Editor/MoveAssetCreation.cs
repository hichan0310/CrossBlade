using System.IO;
using Scripts;
using UnityEditor;
using UnityEngine;

internal static class MoveAssetCreation
{
    [MenuItem("Assets/Create/CrossBlade/Empty Move", false, 220)]
    private static void CreateFromMenu() => CreateWithDialog();

    internal static Move CreateWithDialog()
    {
        var path = EditorUtility.SaveFilePanelInProject("빈 Move 만들기", "NewMove", "prefab", "새 Move 프리팹을 저장할 위치를 선택하세요.");
        if (string.IsNullOrEmpty(path)) return null;
        if (File.Exists(path))
        {
            EditorUtility.DisplayDialog("이미 존재하는 파일", "새 파일 이름을 지정하세요.", "확인");
            return null;
        }
        var move = CreateAtPath(path);
        Selection.activeObject = move.gameObject;
        EditorGUIUtility.PingObject(move.gameObject);
        return move;
    }

    internal static Move CreateAtPath(string path, AnimationClip clip = null)
    {
        if (File.Exists(path)) throw new IOException("Move already exists: " + path);
        var instance = new GameObject(Path.GetFileNameWithoutExtension(path));
        try
        {
            var move = instance.AddComponent<Move>();
            var serialized = new SerializedObject(move);
            serialized.FindProperty("moveId").stringValue = instance.name;
            if (clip != null)
            {
                serialized.FindProperty("characterAnimation").objectReferenceValue = clip;
                serialized.FindProperty("duration").floatValue = Mathf.Max(.01f, clip.length);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            if (prefab == null) throw new IOException("Could not save Move: " + path);
            return prefab.GetComponent<Move>();
        }
        finally { Object.DestroyImmediate(instance); }
    }

    internal static Move CreateFromMotion(MotionPrototype.MotionSequenceAsset motion, string path)
    {
        if (File.Exists(path)) throw new IOException("Move already exists: " + path);
        if (motion == null || MotionPrototype.MotionSequenceCore.Duration(motion) <= 0)
            throw new System.InvalidOperationException("시작·끝 프레임을 지정한 애니메이션이 필요합니다.");
        var clipPath = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(path, ".anim"));
        try
        {
            var clip = MotionPrototype.MotionSequenceCore.Bake(motion, clipPath);
            if (AnimationUtility.GetCurveBindings(clip).Length == 0)
                throw new System.InvalidOperationException("미리보기 캐릭터와 애니메이션의 본 경로가 맞지 않습니다.");
            return CreateAtPath(path, clip);
        }
        catch
        {
            AssetDatabase.DeleteAsset(clipPath);
            throw;
        }
    }
}
