using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Scripts;
using Scripts.TurnMotion;
using UnityEditor;
using UnityEngine;

namespace CrossBlade.EditorTools
{
    public static class TurnMotionBuilder
    {
        public static List<TurnMotionPoint> NormalizePoints(AnimationClip source, IEnumerable<TurnMotionPoint> input)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return input
                .Where(point => point != null)
                .Select(point => new TurnMotionPoint
                {
                    label = point.label,
                    sourceTime = Mathf.Clamp(point.sourceTime, 0f, source.length)
                })
                .OrderBy(point => point.sourceTime)
                .GroupBy(point => Mathf.RoundToInt(point.sourceTime * 100000f))
                .Select(group => group.First())
                .ToList();
        }

        public static List<TurnMotionSegment> BuildSegments(
            AnimationClip source, IEnumerable<TurnMotionPoint> input, float turnDuration)
        {
            List<TurnMotionPoint> points = NormalizePoints(source, input);
            if (points.Count < 2)
                throw new InvalidOperationException("Choose at least two distinct source points.");
            float equalDuration = Mathf.Max(0.01f, turnDuration) / (points.Count - 1);
            var segments = new List<TurnMotionSegment>();
            for (int index = 0; index < points.Count - 1; index++)
            {
                segments.Add(new TurnMotionSegment
                {
                    label = string.IsNullOrWhiteSpace(points[index].label)
                        ? $"Part {index + 1:00}"
                        : points[index].label,
                    sourceStartTime = points[index].sourceTime,
                    sourceEndTime = points[index + 1].sourceTime,
                    turnStartTime = equalDuration * index,
                    turnEndTime = index == points.Count - 2
                        ? Mathf.Max(0.01f, turnDuration)
                        : equalDuration * (index + 1),
                });
            }
            return segments;
        }

        public static float MapTurnToSourceTime(IReadOnlyList<TurnMotionSegment> segments, float turnTime)
        {
            if (segments == null || segments.Count == 0) return 0f;
            TurnMotionSegment selected = segments[segments.Count - 1];
            foreach (TurnMotionSegment segment in segments)
            {
                if (turnTime <= segment.turnEndTime + 0.000001f)
                {
                    selected = segment;
                    break;
                }
            }
            float alpha = selected.TurnDuration <= 0f
                ? 0f
                : Mathf.InverseLerp(selected.turnStartTime, selected.turnEndTime, turnTime);
            return Mathf.Lerp(selected.sourceStartTime, selected.sourceEndTime, alpha);
        }

        public static AnimationClip GenerateClip(
            AnimationClip source, IEnumerable<TurnMotionPoint> inputPoints,
            float turnDuration, float outputFps, string assetPath)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            List<TurnMotionSegment> segments = BuildSegments(source, inputPoints, turnDuration);
            float duration = Mathf.Max(0.01f, turnDuration);
            float fps = Mathf.Max(1f, outputFps);
            int frameCount = Mathf.Max(2, Mathf.CeilToInt(duration * fps) + 1);
            var output = new AnimationClip
            {
                name = Path.GetFileNameWithoutExtension(assetPath),
                frameRate = fps,
                legacy = source.legacy,
                wrapMode = WrapMode.ClampForever,
            };

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(source, binding);
                if (sourceCurve == null) continue;
                var keys = new Keyframe[frameCount];
                for (int frame = 0; frame < frameCount; frame++)
                {
                    float targetTime = frame == frameCount - 1 ? duration : Mathf.Min(duration, frame / fps);
                    float sourceTime = MapTurnToSourceTime(segments, targetTime);
                    keys[frame] = new Keyframe(targetTime, sourceCurve.Evaluate(sourceTime));
                }
                var curve = new AnimationCurve(keys);
                for (int index = 0; index < curve.length; index++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(output, binding, curve);
            }

            foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                ObjectReferenceKeyframe[] sourceKeys = AnimationUtility.GetObjectReferenceCurve(source, binding);
                var targetKeys = new List<ObjectReferenceKeyframe>();
                UnityEngine.Object previous = null;
                bool hasPrevious = false;
                for (int frame = 0; frame < frameCount; frame++)
                {
                    float targetTime = frame == frameCount - 1 ? duration : Mathf.Min(duration, frame / fps);
                    float sourceTime = MapTurnToSourceTime(segments, targetTime);
                    UnityEngine.Object value = ValueAt(sourceKeys, sourceTime);
                    if (hasPrevious && value == previous) continue;
                    targetKeys.Add(new ObjectReferenceKeyframe { time = targetTime, value = value });
                    previous = value;
                    hasPrevious = true;
                }
                AnimationUtility.SetObjectReferenceCurve(output, binding, targetKeys.ToArray());
            }

            AnimationEvent[] events = AnimationUtility.GetAnimationEvents(source)
                .Select(sourceEvent => RemapEvent(sourceEvent, segments))
                .Where(remapped => remapped != null)
                .ToArray();
            AnimationUtility.SetAnimationEvents(output, events);
            output.EnsureQuaternionContinuity();

            EnsureAssetFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(output, assetPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
        }

        public static TurnMotionDefinition SaveDefinition(
            string assetPath, AnimationClip source, float turnDuration, float outputFps,
            IReadOnlyList<TurnMotionPoint> points, AnimationClip outputClip, GameObject actionPrefab)
        {
            EnsureAssetFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            TurnMotionDefinition definition = AssetDatabase.LoadAssetAtPath<TurnMotionDefinition>(assetPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<TurnMotionDefinition>();
                AssetDatabase.CreateAsset(definition, assetPath);
            }
            definition.Configure(source, turnDuration, outputFps, NormalizePoints(source, points),
                BuildSegments(source, points, turnDuration), outputClip, actionPrefab);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            return definition;
        }

        public static GameObject CreateActionPrefab(
            string outputFolder, string actionName, GameObject characterPrefab,
            GameObject defaultVfxPrefab, TurnMotionDefinition definition, AnimationClip generatedClip)
        {
            if (definition == null || generatedClip == null)
                throw new InvalidOperationException("Save a definition and generate its clip first.");
            string safeName = SanitizeName(actionName);
            string partsFolder = $"{outputFolder}/{safeName}_Parts";
            EnsureAssetFolder(outputFolder);
            EnsureAssetFolder(partsFolder);

            var partPrefabs = new List<GameObject>();
            for (int index = 0; index < definition.Segments.Count; index++)
            {
                TurnMotionSegment segment = definition.Segments[index];
                string partPath = $"{partsFolder}/{safeName}_Part_{index + 1:00}.prefab";
                partPrefabs.Add(CreateOrUpdatePartPrefab(partPath, index, segment, defaultVfxPrefab));
            }

            string actionPath = $"{outputFolder}/{safeName}.prefab";
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(actionPath) == null
                ? new GameObject(safeName)
                : PrefabUtility.LoadPrefabContents(actionPath);
            bool loadedContents = AssetDatabase.LoadAssetAtPath<GameObject>(actionPath) != null;
            try
            {
                TurnMotionAction action = root.GetComponent<TurnMotionAction>() ?? root.AddComponent<TurnMotionAction>();
                TurnMotionMove move = root.GetComponent<TurnMotionMove>() ?? root.AddComponent<TurnMotionMove>();
                Transform character = root.transform.Find("Character");
                if (character == null && characterPrefab != null)
                {
                    GameObject characterObject = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab);
                    characterObject.name = "Character";
                    characterObject.transform.SetParent(root.transform, false);
                    character = characterObject.transform;
                }

                Transform partsRoot = root.transform.Find("Parts");
                if (partsRoot == null)
                {
                    var partsObject = new GameObject("Parts");
                    partsObject.transform.SetParent(root.transform, false);
                    partsRoot = partsObject.transform;
                }
                for (int child = partsRoot.childCount - 1; child >= 0; child--)
                    UnityEngine.Object.DestroyImmediate(partsRoot.GetChild(child).gameObject);

                var partInstances = new List<TurnMotionPart>();
                foreach (GameObject partPrefab in partPrefabs)
                {
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(partPrefab);
                    instance.transform.SetParent(partsRoot, false);
                    partInstances.Add(instance.GetComponent<TurnMotionPart>());
                }
                action.Configure(definition, generatedClip, definition.TurnDuration,
                    character != null ? character.gameObject : root, partInstances);
                move.Configure(action);
                var serializedMove = new SerializedObject(move);
                serializedMove.FindProperty("moveId").stringValue = safeName;
                serializedMove.FindProperty("category").enumValueIndex = (int)MoveCategory.Attack;
                serializedMove.FindProperty("duration").floatValue = definition.TurnDuration;
                serializedMove.FindProperty("visualRoot").objectReferenceValue = root.transform;
                serializedMove.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, actionPath);
            }
            finally
            {
                if (loadedContents) PrefabUtility.UnloadPrefabContents(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<GameObject>(actionPath);
        }

        private static GameObject CreateOrUpdatePartPrefab(
            string path, int index, TurnMotionSegment segment, GameObject defaultVfxPrefab)
        {
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(path) : new GameObject($"Part_{index + 1:00}");
            try
            {
                TurnMotionPart part = root.GetComponent<TurnMotionPart>() ?? root.AddComponent<TurnMotionPart>();
                Transform anchor = root.transform.Find("VFXAnchor");
                if (anchor == null)
                {
                    var anchorObject = new GameObject("VFXAnchor");
                    anchorObject.transform.SetParent(root.transform, false);
                    anchor = anchorObject.transform;
                }
                if (defaultVfxPrefab != null && anchor.childCount == 0)
                {
                    GameObject vfx = (GameObject)PrefabUtility.InstantiatePrefab(defaultVfxPrefab);
                    vfx.transform.SetParent(anchor, false);
                }
                part.Configure(index, segment.label, segment, anchor);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static UnityEngine.Object ValueAt(ObjectReferenceKeyframe[] keys, float time)
        {
            UnityEngine.Object value = null;
            foreach (ObjectReferenceKeyframe key in keys)
            {
                if (key.time > time + 0.000001f) break;
                value = key.value;
            }
            return value;
        }

        private static AnimationEvent RemapEvent(AnimationEvent sourceEvent, IReadOnlyList<TurnMotionSegment> segments)
        {
            TurnMotionSegment segment = segments.FirstOrDefault(item =>
                sourceEvent.time >= item.sourceStartTime && sourceEvent.time <= item.sourceEndTime);
            if (segment == null) return null;
            float alpha = segment.SourceDuration <= 0f
                ? 0f
                : Mathf.InverseLerp(segment.sourceStartTime, segment.sourceEndTime, sourceEvent.time);
            var output = new AnimationEvent
            {
                functionName = sourceEvent.functionName,
                floatParameter = sourceEvent.floatParameter,
                intParameter = sourceEvent.intParameter,
                stringParameter = sourceEvent.stringParameter,
                objectReferenceParameter = sourceEvent.objectReferenceParameter,
                messageOptions = sourceEvent.messageOptions,
                time = Mathf.Lerp(segment.turnStartTime, segment.turnEndTime, alpha),
            };
            return output;
        }

        public static string SanitizeName(string value)
        {
            string result = string.IsNullOrWhiteSpace(value) ? "TurnMotionAction" : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
            return result.Replace(' ', '_');
        }

        public static void EnsureAssetFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(parent)) EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
