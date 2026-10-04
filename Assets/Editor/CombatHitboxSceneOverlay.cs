using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Scripts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Read-only Scene view overlay for the authored and live combat colliders.</summary>
[InitializeOnLoad]
internal static class CombatHitboxSceneOverlay
{
    private const string PrefKey = "CrossBlade.ShowCombatHitboxesInScene";
    private static readonly PropertyInfo IsMoveRunning = typeof(Actor).GetProperty("IsMoveRunning", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo MoveProgress = typeof(Actor).GetProperty("MoveProgress", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo EvaluatePose = typeof(CharacterAnimationPlayer).GetMethod("Evaluate", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Color BodyColor = new(.2f, .84f, 1f, 1f);
    private static readonly Color ActiveWeaponColor = new(1f, .43f, .12f, 1f);
    private static readonly Color InactiveColor = new(.62f, .66f, .73f, .8f);
    private static readonly Color ConsumedColor = new(1f, .25f, .3f, .85f);
    private static bool visible;
    private static Move selectedMove;
    private static float previewProgress;
    private static double nextRepaint;
    private static readonly Dictionary<CharacterAnimationPlayer, int> PreviewPlayers = new();
    private static readonly Dictionary<SceneView, ulong> OriginalSceneMasks = new();

    static CombatHitboxSceneOverlay()
    {
        visible = EditorPrefs.GetBool(PrefKey, true);
        SceneView.duringSceneGui += Draw;
        EditorApplication.update += RepaintDuringPlay;
        EditorSceneManager.sceneSaving += (scene, path) => RestoreEditPoses();
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) RestoreEditPoses();
        };
        AssemblyReloadEvents.beforeAssemblyReload += RestoreEditPoses;
        AssemblyReloadEvents.beforeAssemblyReload += RestoreSceneMasks;
    }

    [MenuItem("Tools/CrossBlade/Show Combat Hitboxes In Scene")]
    private static void Toggle()
    {
        visible = !visible;
        EditorPrefs.SetBool(PrefKey, visible);
        if (!visible) RestoreEditPoses();
        SceneView.RepaintAll();
    }

    [MenuItem("Tools/CrossBlade/Show Combat Hitboxes In Scene", true)]
    private static bool ValidateToggle()
    {
        Menu.SetChecked("Tools/CrossBlade/Show Combat Hitboxes In Scene", visible);
        return true;
    }

    private static void RepaintDuringPlay()
    {
        if (!visible || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextRepaint) return;
        nextRepaint = EditorApplication.timeSinceStartup + .05;
        SceneView.RepaintAll();
    }

    private static void Draw(SceneView sceneView)
    {
        EnsureCombatSceneVisible(sceneView);
        if (!visible) return;
        var actors = UnityEngine.Object.FindObjectsByType<Actor>(FindObjectsSortMode.None)
            .Where(actor => actor != null && actor.gameObject.scene.IsValid() && actor.gameObject.scene.isLoaded)
            .OrderBy(actor => actor.transform.position.x).ToArray();
        if (actors.Length == 0) return;

        bool playing = EditorApplication.isPlaying;
        Move[] choices = playing ? Array.Empty<Move>() : FindMoves(actors);
        DrawControls(playing, choices, actors);
        if (Event.current.type != EventType.Repaint) return;
        if (!playing && selectedMove != null && !AnimationMode.InAnimationMode())
            SampleEditPoses(actors);

        foreach (var actor in actors)
        {
            var visual = actor.GetComponent<ActorVisualController>();
            if (visual == null) continue;
            var visualData = new SerializedObject(visual);
            var authoring = playing ? null : actor.GetComponentInChildren<SceneMoveAuthoring>(true);
            Move move = playing
                ? visualData.FindProperty("currentMoveDebug").objectReferenceValue as Move
                : authoring != null ? authoring.SceneMove : null;
            if (move == null) continue;
            bool running = playing && IsMoveRunning != null && (bool)IsMoveRunning.GetValue(actor);
            float progress = playing ? running && MoveProgress != null ? (float)MoveProgress.GetValue(actor) : 1f : previewProgress;

            var data = new SerializedObject(move);
            var body = data.FindProperty("bodyCollider").objectReferenceValue as Collider2D;
            if (body != null) DrawCollider(body, BodyColor, actor.name + " 몸");
            var weapons = data.FindProperty("weaponHitboxes");
            for (int i = 0; i < weapons.arraySize; i++)
            {
                var hit = weapons.GetArrayElementAtIndex(i).objectReferenceValue as Hitbox;
                if (hit == null) continue;
                var box = hit.GetComponent<Collider2D>();
                if (box == null) continue;
                bool enabled = !playing || running && box.enabled && box.gameObject.activeInHierarchy;
                bool active = hit.IsActiveAt(progress);
                Color color = !enabled ? ConsumedColor : active ? ActiveWeaponColor : InactiveColor;
                string state = !enabled ? "꺼짐" : active ? "활성" : "대기";
                DrawCollider(box, color, actor.name + " 무기 " + (i + 1) + " " + state);
            }
            Handles.color = Color.white;
        }
    }

    private static void EnsureCombatSceneVisible(SceneView sceneView)
    {
        if (sceneView == null || sceneView.camera == null) return;
        bool combatLoaded = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            string path = SceneManager.GetSceneAt(i).path;
            if (path == "Assets/Scenes/CombatScene.unity" || path == "Assets/Scenes/OnlineCombat.unity")
            {
                combatLoaded = true;
                break;
            }
        }

        if (combatLoaded)
        {
            if (!OriginalSceneMasks.ContainsKey(sceneView))
            {
                OriginalSceneMasks.Add(sceneView, sceneView.camera.overrideSceneCullingMask);
                sceneView.Repaint();
            }
            sceneView.camera.overrideSceneCullingMask = ulong.MaxValue;
        }
        else if (OriginalSceneMasks.TryGetValue(sceneView, out ulong originalMask))
        {
            sceneView.camera.overrideSceneCullingMask = originalMask;
            OriginalSceneMasks.Remove(sceneView);
            sceneView.Repaint();
        }
    }

    private static void RestoreSceneMasks()
    {
        foreach (var entry in OriginalSceneMasks)
            if (entry.Key != null && entry.Key.camera != null)
                entry.Key.camera.overrideSceneCullingMask = entry.Value;
        OriginalSceneMasks.Clear();
    }

    private static Move[] FindMoves(Actor[] actors)
    {
        var graph = new SerializedObject(actors[0]).FindProperty("combatMoveGraph").objectReferenceValue as CombatMoveGraphAsset;
        if (graph == null) return Array.Empty<Move>();
        var moves = graph.nodes.Where(node => node != null && node.move != null).Select(node => node.move).Distinct().ToArray();
        if (selectedMove == null || !moves.Contains(selectedMove))
        {
            var authoring = actors[0].GetComponentInChildren<SceneMoveAuthoring>(true);
            selectedMove = authoring != null && moves.Contains(authoring.SourceMove) ? authoring.SourceMove
                : moves.FirstOrDefault(move => move.GetComponentsInChildren<Hitbox>(true).Length > 0) ?? graph.startMove;
        }
        return moves;
    }

    internal static void ValidateSceneMapping()
    {
        foreach (string path in new[] { "Assets/Scenes/CombatScene.unity", "Assets/Scenes/OnlineCombat.unity" })
            ValidateSceneMapping(path);
    }

    private static void ValidateSceneMapping(string path)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        var actors = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Actor>(true))
            .OrderBy(actor => actor.transform.position.x).ToArray();
        if (actors.Length != 2) throw new InvalidOperationException("Scene authoring needs two actors");
        var centers = new Vector3[2];
        for (int i = 0; i < 2; i++)
        {
            var visual = actors[i].GetComponent<ActorVisualController>();
            var mount = new SerializedObject(visual).FindProperty("moveMount").objectReferenceValue as Transform;
            if (mount == null || mount.localScale.x >= 0f)
                throw new InvalidOperationException("Move attachment X axis is not converted for " + actors[i].name);
            var player = new SerializedObject(visual).FindProperty("characterPlayer").objectReferenceValue as CharacterAnimationPlayer;
            if (player == null || player.AnimationRoot == null || player.AnimationRoot.GetComponentsInChildren<Renderer>(true).Length == 0)
                throw new InvalidOperationException("The 3D model is not in the combat scene for " + actors[i].name);
            var authoring = actors[i].GetComponentInChildren<SceneMoveAuthoring>(true);
            var box = authoring != null && authoring.SceneMove != null
                ? authoring.SceneMove.GetComponentInChildren<Hitbox>(true)?.GetComponent<BoxCollider2D>() : null;
            if (box == null || !PrefabUtility.IsPartOfPrefabInstance(box))
                throw new InvalidOperationException("The editable Move collider is not in the scene for " + actors[i].name);
            centers[i] = box.transform.TransformPoint(box.offset);
        }
        float left = centers[0].x - actors[0].transform.position.x;
        float right = centers[1].x - actors[1].transform.position.x;
        if (Mathf.Abs(left + right) > .001f || left <= .01f || right >= -.01f ||
            Mathf.Abs(centers[0].y - centers[1].y) > .001f)
            throw new InvalidOperationException("Weapon hitboxes must face inward toward the opponent");
        Debug.Log($"COMBAT_SCENE_AUTHORING_PASS {path} weapon X from actor: Player +{left:F3}, Enemy {right:F3}");
    }

    private static void DrawControls(bool playing, Move[] choices, Actor[] actors)
    {
        Handles.BeginGUI();
        try
        {
            var rect = new Rect(75, 10, 390, playing ? 72 : 151);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, 18), "전투 히트박스 · 몸 하늘색 / 활성 무기 주황색 / 비활성 회색");
            if (playing)
            {
                GUI.Label(new Rect(rect.x + 8, rect.y + 30, rect.width - 16, 36),
                    "Play: 실제 콜라이더 위치와 판정 프레임 표시\n꺼짐(빨강): 충돌 후 소모 또는 행동 종료");
                return;
            }
            if (choices.Length == 0)
            {
                GUI.Label(new Rect(rect.x + 8, rect.y + 30, rect.width - 16, 18), "씬 Actor에 연결된 Move 그래프가 없습니다.");
                return;
            }
            int index = Mathf.Max(0, Array.IndexOf(choices, selectedMove));
            int chosen = EditorGUI.Popup(new Rect(rect.x + 8, rect.y + 30, rect.width - 16, 18), "Move", index,
                choices.Select(move => move.name).ToArray());
            if (chosen != index)
            {
                selectedMove = choices[chosen];
                foreach (var actor in actors)
                {
                    var authoring = actor.GetComponentInChildren<SceneMoveAuthoring>(true)
                        ?? CombatSceneAuthoringSetup.EnsureForActor(actor);
                    CombatSceneAuthoringSetup.SetMove(authoring, selectedMove);
                }
            }
            previewProgress = EditorGUI.Slider(new Rect(rect.x + 8, rect.y + 54, rect.width - 16, 18),
                "프레임 진행", previewProgress, 0f, 1f);
            var clip = new SerializedObject(selectedMove).FindProperty("characterAnimation").objectReferenceValue as AnimationClip;
            int lastFrame = clip == null ? 0 : Mathf.RoundToInt(clip.length * clip.frameRate);
            GUI.Label(new Rect(rect.x + 8, rect.y + 78, rect.width - 16, 18),
                $"프레임 {Mathf.RoundToInt(previewProgress * lastFrame)}/{lastFrame} · 판정은 씬의 Move 자식 Collider2D에서 편집");
            if (GUI.Button(new Rect(rect.x + 8, rect.y + 99, 160, 22), "씬의 3D 모델 선택·확대"))
            {
                var visual = actors[0].GetComponent<ActorVisualController>();
                var player = visual == null ? null : new SerializedObject(visual).FindProperty("characterPlayer").objectReferenceValue as CharacterAnimationPlayer;
                if (player != null)
                {
                    Selection.activeGameObject = player.gameObject;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            }
            if (GUI.Button(new Rect(rect.x + 174, rect.y + 99, rect.width - 182, 22), "씬 히트박스 선택"))
            {
                var authoring = actors[0].GetComponentInChildren<SceneMoveAuthoring>(true);
                var hit = authoring != null && authoring.SceneMove != null
                    ? authoring.SceneMove.GetComponentInChildren<Hitbox>(true) : null;
                var body = authoring != null && authoring.SceneMove != null
                    ? new SerializedObject(authoring.SceneMove).FindProperty("bodyCollider").objectReferenceValue as Collider2D : null;
                var target = hit != null ? hit.gameObject : body != null ? body.gameObject : null;
                if (target != null)
                {
                    Selection.activeGameObject = target;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            }
            if (GUI.Button(new Rect(rect.x + 8, rect.y + 124, rect.width - 16, 22), "씬에서 편집한 판정을 Move 프리팹에 저장"))
            {
                foreach (var actor in actors)
                    CombatSceneAuthoringSetup.ApplyEdits(actor.GetComponentInChildren<SceneMoveAuthoring>(true));
            }
        }
        finally { Handles.EndGUI(); }
    }

    private static void SampleEditPoses(Actor[] actors)
    {
        if (EvaluatePose == null) return;
        foreach (var actor in actors)
        {
            var visual = actor.GetComponent<ActorVisualController>();
            var player = visual == null ? null : new SerializedObject(visual).FindProperty("characterPlayer").objectReferenceValue as CharacterAnimationPlayer;
            if (player == null || player.AnimationRoot == null) continue;
            int sign = actors.Length > 1 && actors.First(other => other != actor).transform.position.x < actor.transform.position.x ? -1 : 1;
            EvaluatePose.Invoke(player, new object[] { selectedMove, previewProgress, sign });
            PreviewPlayers[player] = sign;
        }
    }

    private static void RestoreEditPoses()
    {
        if (EvaluatePose == null) return;
        foreach (var entry in PreviewPlayers)
            if (entry.Key != null && entry.Key.AnimationRoot != null)
                EvaluatePose.Invoke(entry.Key, new object[] { null, 0f, entry.Value });
        PreviewPlayers.Clear();
    }

    private static void DrawCollider(Collider2D collider, Color color, string label)
    {
        Matrix4x4 matrix = collider.transform.localToWorldMatrix;
        Handles.color = color;
        if (collider is BoxCollider2D box)
        {
            Vector2 half = box.size * .5f;
            var points = new[]
            {
                matrix.MultiplyPoint3x4(box.offset + new Vector2(-half.x, -half.y)),
                matrix.MultiplyPoint3x4(box.offset + new Vector2(-half.x, half.y)),
                matrix.MultiplyPoint3x4(box.offset + new Vector2(half.x, half.y)),
                matrix.MultiplyPoint3x4(box.offset + new Vector2(half.x, -half.y))
            };
            Handles.DrawSolidRectangleWithOutline(points,
                new Color(color.r, color.g, color.b, .13f), color);
            if (Selection.activeGameObject == collider.gameObject)
                Handles.Label(points[1] + Vector3.up * .05f, label);
        }
        else
        {
            Bounds bounds = collider.bounds;
            if (bounds.size.sqrMagnitude <= 0f) return;
            Handles.DrawWireCube(bounds.center, bounds.size);
            if (Selection.activeGameObject == collider.gameObject)
                Handles.Label(bounds.max, label);
        }
    }
}
