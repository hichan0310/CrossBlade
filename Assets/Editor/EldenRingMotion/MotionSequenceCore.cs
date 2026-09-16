using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MotionPrototype
{
    internal static class MotionSequenceCore
    {
        internal readonly struct Placement
        {
            public readonly MotionSegment segment;
            public readonly float start;
            public readonly float duration;
            public readonly float blend;
            public Placement(MotionSegment segment, float start, float duration, float blend)
            {
                this.segment = segment; this.start = start; this.duration = duration; this.blend = blend;
            }
        }

        public static List<Placement> Layout(MotionSequenceAsset sequence)
        {
            var result = new List<Placement>();
            var cursor = 0f;
            foreach (var segment in sequence.segments.Where(item => item?.clip != null))
            {
                segment.firstFrame = Mathf.Clamp(segment.firstFrame, 0, segment.MaximumFrame);
                segment.lastFrame = Mathf.Clamp(segment.lastFrame <= 0 ? segment.MaximumFrame : segment.lastFrame,
                    segment.firstFrame, segment.MaximumFrame);
                var duration = segment.Duration;
                var blend = result.Count == 0 ? 0f : Mathf.Min(duration, segment.blendFrames / Mathf.Max(1f, sequence.outputFps));
                cursor = Mathf.Max(0f, cursor - blend);
                result.Add(new Placement(segment, cursor, duration, blend));
                cursor += duration;
            }
            return result;
        }

        public static float Duration(MotionSequenceAsset sequence)
        {
            var layout = Layout(sequence);
            return layout.Count == 0 ? 0 : layout[^1].start + layout[^1].duration;
        }

        public static void Sample(MotionSequenceAsset sequence, GameObject instance, float time)
        {
            var layout = Layout(sequence);
            if (layout.Count == 0 || instance == null) return;
            ResetToPreview(sequence, instance);
            var currentIndex = Mathf.Clamp(layout.FindLastIndex(item => time >= item.start), 0, layout.Count - 1);
            var current = layout[currentIndex];
            var currentLocal = Mathf.Clamp(time - current.start, 0f, current.duration);
            if (currentIndex > 0 && current.blend > 0f && currentLocal < current.blend)
            {
                var previous = layout[currentIndex - 1];
                var transforms = instance.GetComponentsInChildren<Transform>(true);
                SampleSegment(previous.segment, instance, Mathf.Clamp(time - previous.start, 0f, previous.duration));
                var positions = transforms.Select(item => item.localPosition).ToArray();
                var rotations = transforms.Select(item => item.localRotation).ToArray();
                var scales = transforms.Select(item => item.localScale).ToArray();
                ResetToPreview(sequence, instance);
                SampleSegment(current.segment, instance, currentLocal);
                var alpha = Mathf.Clamp01(currentLocal / current.blend);
                for (var index = 0; index < transforms.Length; index++)
                {
                    transforms[index].localPosition = Vector3.Lerp(positions[index], transforms[index].localPosition, alpha);
                    transforms[index].localRotation = Quaternion.Slerp(rotations[index], transforms[index].localRotation, alpha);
                    transforms[index].localScale = Vector3.Lerp(scales[index], transforms[index].localScale, alpha);
                }
            }
            else SampleSegment(current.segment, instance, currentLocal);
        }

        private static void ResetToPreview(MotionSequenceAsset sequence, GameObject instance)
        {
            if (sequence.previewCharacter == null) return;
            var source = sequence.previewCharacter.GetComponentsInChildren<Transform>(true)
                .ToDictionary(item => AnimationUtility.CalculateTransformPath(item, sequence.previewCharacter.transform));
            foreach (var target in instance.GetComponentsInChildren<Transform>(true))
            {
                var path = AnimationUtility.CalculateTransformPath(target, instance.transform);
                if (!source.TryGetValue(path, out var rest)) continue;
                target.localPosition = rest.localPosition;
                target.localRotation = rest.localRotation;
                target.localScale = rest.localScale;
            }
        }

        private static void SampleSegment(MotionSegment segment, GameObject instance, float localTime)
        {
            var source = segment.firstFrame / segment.Rate + localTime * Mathf.Max(0.01f, segment.speed);
            segment.clip.SampleAnimation(instance, Mathf.Clamp(source, segment.firstFrame / segment.Rate,
                segment.lastFrame / segment.Rate));
        }

        public static AnimationClip Bake(MotionSequenceAsset sequence, string assetPath)
        {
            if (sequence == null || sequence.previewCharacter == null) throw new InvalidOperationException("Assign a preview character.");
            if (Layout(sequence).Count == 0) throw new InvalidOperationException("Add at least one valid segment.");
            var instance = UnityEngine.Object.Instantiate(sequence.previewCharacter);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var transforms = instance.GetComponentsInChildren<Transform>(true);
                var paths = transforms.Select(item => AnimationUtility.CalculateTransformPath(item, instance.transform)).ToArray();
                var duration = Duration(sequence);
                var frames = Mathf.Max(1, Mathf.CeilToInt(duration * sequence.outputFps));
                var curves = new Dictionary<(string, string), List<Keyframe>>();
                var animated = sequence.segments.Where(item => item?.clip != null)
                    .SelectMany(item => AnimationUtility.GetCurveBindings(item.clip))
                    .Where(binding => binding.type == typeof(Transform))
                    .Select(binding => (binding.path, binding.propertyName)).ToHashSet();
                var previous = new Quaternion[transforms.Length];
                var rootPosition = instance.transform.localPosition;
                var rootRotation = instance.transform.localRotation;
                for (var frame = 0; frame <= frames; frame++)
                {
                    var time = Mathf.Min(duration, frame / (float)sequence.outputFps);
                    Sample(sequence, instance, time);
                    if (!sequence.keepRootMotion)
                    {
                        instance.transform.localPosition = rootPosition;
                        instance.transform.localRotation = rootRotation;
                    }
                    for (var index = 0; index < transforms.Length; index++)
                    {
                        var transform = transforms[index];
                        var rotation = transform.localRotation;
                        if (frame > 0 && Quaternion.Dot(previous[index], rotation) < 0)
                            rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                        previous[index] = rotation;
                        AddIf(animated, curves, paths[index], "m_LocalPosition.x", time, transform.localPosition.x);
                        AddIf(animated, curves, paths[index], "m_LocalPosition.y", time, transform.localPosition.y);
                        AddIf(animated, curves, paths[index], "m_LocalPosition.z", time, transform.localPosition.z);
                        AddIf(animated, curves, paths[index], "m_LocalRotation.x", time, rotation.x);
                        AddIf(animated, curves, paths[index], "m_LocalRotation.y", time, rotation.y);
                        AddIf(animated, curves, paths[index], "m_LocalRotation.z", time, rotation.z);
                        AddIf(animated, curves, paths[index], "m_LocalRotation.w", time, rotation.w);
                        AddIf(animated, curves, paths[index], "m_LocalScale.x", time, transform.localScale.x);
                        AddIf(animated, curves, paths[index], "m_LocalScale.y", time, transform.localScale.y);
                        AddIf(animated, curves, paths[index], "m_LocalScale.z", time, transform.localScale.z);
                    }
                }
                var clip = new AnimationClip { name = System.IO.Path.GetFileNameWithoutExtension(assetPath), frameRate = sequence.outputFps };
                foreach (var pair in curves)
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(pair.Key.Item1, typeof(Transform), pair.Key.Item2),
                        new AnimationCurve(pair.Value.ToArray()));
                clip.EnsureQuaternionContinuity();
                AssetDatabase.CreateAsset(clip, assetPath);
                AssetDatabase.SaveAssets();
                return clip;
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static void Add(Dictionary<(string, string), List<Keyframe>> curves, string path, string property, float time, float value)
        {
            var key = (path, property);
            if (!curves.TryGetValue(key, out var list)) curves[key] = list = new List<Keyframe>();
            list.Add(new Keyframe(time, value));
        }

        private static void AddIf(HashSet<(string, string)> animated,
            Dictionary<(string, string), List<Keyframe>> curves, string path, string property, float time, float value)
        {
            if (animated.Contains((path, property))) Add(curves, path, property, time, value);
        }
    }
}
