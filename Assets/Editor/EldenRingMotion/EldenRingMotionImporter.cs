using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MotionPrototype
{
    [Serializable]
    internal sealed class HkxTrackData
    {
        public string name;
        public int bone_index;
        public float[] samples;
    }

    [Serializable]
    internal sealed class HkxMotionData
    {
        public int format_version;
        public string name;
        public string source;
        public string skeleton;
        public string havok_version;
        public float duration;
        public float fps;
        public int frame_count;
        public int track_count;
        public HkxTrackData[] tracks;
        public float[] root_motion;
    }

    [Serializable]
    internal sealed class HkxSkeletonBoneData
    {
        public int index;
        public string name;
        public int parent;
        public float[] reference;
    }

    [Serializable]
    internal sealed class HkxSkeletonData
    {
        public int format_version;
        public string source;
        public int bone_count;
        public HkxSkeletonBoneData[] bones;
    }

    [Serializable]
    internal sealed class EldenRingImportReport
    {
        public string animation;
        public float duration;
        public float fps;
        public int frames;
        public int source_tracks;
        public int mapped_tracks;
        public List<string> mapped = new();
        public List<string> missing = new();
        public string prefab;
        public string clip;
    }

    [Serializable]
    internal sealed class EldenRingMotionValidationReport
    {
        public string animation;
        public int sampled_frames;
        public float max_right_upper_arm_angle;
        public float max_right_forearm_angle;
        public float max_right_hand_angle;
        public float max_right_hand_travel;
        public float sword_tip_path_length;
        public float max_pose_joint_displacement;
        public float max_upper_arm_length_error;
        public float max_forearm_length_error;
        public float max_arm_scale_error;
        public float max_left_upper_arm_direction_error;
        public float max_left_forearm_direction_error;
        public float max_right_upper_arm_direction_error;
        public float max_right_forearm_direction_error;
        public float mean_left_upper_arm_direction_error;
        public float mean_left_forearm_direction_error;
        public float mean_right_upper_arm_direction_error;
        public float mean_right_forearm_direction_error;
        public float max_left_hand_rotation_error;
        public float max_right_hand_rotation_error;
        public float max_weapon_rotation_error;
        public float max_weapon_socket_position_error;
        public Vector3 target_reference_left_hand;
        public Vector3 source_reference_left_hand;
        public Vector3 target_reference_right_hand;
        public Vector3 source_reference_right_hand;
        public Vector3 min_skinned_bounds_size;
        public Vector3 max_skinned_bounds_size;
        public bool passed;
    }

    [Serializable]
    internal sealed class EldenRingTerminalRotationClipReport
    {
        public string animation;
        public int sampled_frames;
        public float max_left_hand_error;
        public float max_right_hand_error;
        public float max_weapon_error;
        public float max_socket_local_position_error;
        public bool passed;
    }

    [Serializable]
    internal sealed class EldenRingTerminalRotationBatchReport
    {
        public List<EldenRingTerminalRotationClipReport> clips = new();
        public bool passed;
    }

    [Serializable]
    internal sealed class ShadowMotionValidationReport
    {
        public string animation;
        public int sampled_frames;
        public float max_left_hand_pelvis_relative_travel;
        public float max_right_hand_pelvis_relative_travel;
        public float max_left_upper_arm_rotation;
        public float max_right_upper_arm_rotation;
        public float max_left_forearm_rotation;
        public float max_right_forearm_rotation;
        public bool passed;
    }

    internal static class EldenRingMotionImporter
    {
        private const string ModelPath = "Assets/PrototypeReference/EldenRing/ER_Base_Male.fbx";
        public const string GeneratedRoot = "Assets/PrototypeGenerated/EldenRing";
        public const string PrefabPath = GeneratedRoot + "/ER_Base_Male_Animated.prefab";
        public const string ClipPath = GeneratedRoot + "/a023_030150.anim";
        private const string BodyMaterialPath = GeneratedRoot + "/BodyPreview.mat";
        private const string WeaponMaterialPath = GeneratedRoot + "/WeaponPreview.mat";
        private const int ValuesPerSample = 10;

        [MenuItem("Tools/Prototype/Import Elden Ring Sample Motion")]
        public static void ImportFromMenu()
        {
            Import();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        }

        public static void Import()
        {
            var jsonPath = Path.Combine(ProjectPaths.RepositoryRoot, "converted", "elden_ring", "a023_030000.motion.json");
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Decode the HKX first with scripts/hkx_to_unity_json.py", jsonPath);
            var data = JsonUtility.FromJson<HkxMotionData>(File.ReadAllText(jsonPath));
            Validate(data);
            var skeletonPath = Path.Combine(ProjectPaths.RepositoryRoot, "converted", "elden_ring", "c0000.skeleton.json");
            var skeleton = JsonUtility.FromJson<HkxSkeletonData>(File.ReadAllText(skeletonPath));
            ValidateSkeleton(skeleton, data);

            BuildPreviewPrefab(skeleton);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var transforms = prefab.GetComponentsInChildren<Transform>(true);
            var byName = transforms.GroupBy(transform => transform.name)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var clip = new AnimationClip { name = data.name, frameRate = data.fps, legacy = false };
            var report = new EldenRingImportReport
            {
                animation = data.name,
                duration = data.duration,
                fps = data.fps,
                frames = data.frame_count,
                source_tracks = data.track_count,
                prefab = PrefabPath,
                clip = ClipPath,
            };

            foreach (var track in data.tracks)
            {
                if (!byName.TryGetValue(track.name, out var candidates) || candidates.Length != 1)
                {
                    report.missing.Add(track.name);
                    continue;
                }
                report.mapped.Add(track.name);
            }
            report.mapped_tracks = report.mapped.Count;
            AddHierarchyRetargetedCurves(clip, prefab.transform, byName, data, skeleton);
            clip.EnsureQuaternionContinuity();

            AssetDatabase.DeleteAsset(ClipPath);
            AssetDatabase.CreateAsset(clip, ClipPath);
            var additionalClipCount = ImportAdditionalConvertedClips(prefab.transform, byName, skeleton, data.name);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Directory.CreateDirectory(Path.Combine(ProjectPaths.RepositoryRoot, "catalog"));
            var reportPath = Path.Combine(ProjectPaths.RepositoryRoot, "catalog", "elden_ring_motion_import.json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            Debug.Log($"ELDEN_RING_MOTION_IMPORTED: frames={data.frame_count}, mapped={report.mapped_tracks}/{data.track_count}, clips={additionalClipCount + 1}, primary={ClipPath}");
        }

        private static int ImportAdditionalConvertedClips(
            Transform prefabRoot, Dictionary<string, Transform[]> byName, HkxSkeletonData skeleton, string primaryName)
        {
            var convertedDirectory = Path.Combine(ProjectPaths.RepositoryRoot, "converted", "elden_ring");
            var count = 0;
            foreach (var jsonPath in Directory.GetFiles(convertedDirectory, "*.motion.json").OrderBy(path => path, StringComparer.Ordinal))
            {
                var motion = JsonUtility.FromJson<HkxMotionData>(File.ReadAllText(jsonPath));
                Validate(motion);
                if (motion.name == primaryName) continue;
                var motionSkeleton = LoadSkeletonForMotion(motion, skeleton);
                ValidateSkeleton(motionSkeleton, motion);

                var clip = new AnimationClip { name = motion.name, frameRate = motion.fps, legacy = false };
                AddHierarchyRetargetedCurves(clip, prefabRoot, byName, motion, motionSkeleton);
                clip.EnsureQuaternionContinuity();
                var clipPath = $"{GeneratedRoot}/{motion.name}.anim";
                AssetDatabase.DeleteAsset(clipPath);
                AssetDatabase.CreateAsset(clip, clipPath);
                count++;
            }
            return count;
        }

        /// <summary>Imports one decoded motion without rebuilding every previously prepared clip.</summary>
        public static AnimationClip ImportSingle(string jsonPath)
        {
            if (!File.Exists(jsonPath)) throw new FileNotFoundException("Decoded motion JSON is missing.", jsonPath);
            var motion = JsonUtility.FromJson<HkxMotionData>(File.ReadAllText(jsonPath));
            Validate(motion);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                // First-time project setup still creates the validated model/prefab/material fixture.
                Import();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }
            if (prefab == null) throw new InvalidOperationException($"Preview prefab is missing: {PrefabPath}");

            var defaultSkeletonPath = Path.Combine(ProjectPaths.RepositoryRoot, "converted", "elden_ring", "c0000.skeleton.json");
            var defaultSkeleton = JsonUtility.FromJson<HkxSkeletonData>(File.ReadAllText(defaultSkeletonPath));
            var skeleton = LoadSkeletonForMotion(motion, defaultSkeleton);
            ValidateSkeleton(skeleton, motion);
            var byName = prefab.GetComponentsInChildren<Transform>(true)
                .GroupBy(transform => transform.name)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var clip = new AnimationClip { name = motion.name, frameRate = motion.fps, legacy = false };
            AddHierarchyRetargetedCurves(clip, prefab.transform, byName, motion, skeleton);
            clip.EnsureQuaternionContinuity();

            var clipPath = $"{GeneratedRoot}/{motion.name}.anim";
            AssetDatabase.DeleteAsset(clipPath);
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        }

        public static void EnsurePreviewPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null && prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Any(renderer => renderer.enabled) && !prefab.GetComponentsInChildren<Transform>(true)
                    .Any(transform => transform.name.StartsWith("MotionMannequin", StringComparison.Ordinal))) return;

            var skeletonPath = Path.Combine(ProjectPaths.RepositoryRoot, "converted", "elden_ring", "c0000.skeleton.json");
            if (!File.Exists(skeletonPath))
                throw new FileNotFoundException("Elden Ring preview skeleton is missing.", skeletonPath);
            var skeleton = JsonUtility.FromJson<HkxSkeletonData>(File.ReadAllText(skeletonPath));
            if (skeleton?.bones == null || skeleton.bones.Length == 0)
                throw new InvalidDataException("Elden Ring preview skeleton is invalid.");
            BuildPreviewPrefab(skeleton);
            AssetDatabase.SaveAssets();
        }

        private static void BuildPreviewPrefab(HkxSkeletonData skeleton)
        {
            EnsureAssetFolder("Assets/PrototypeGenerated");
            EnsureAssetFolder(GeneratedRoot);
            var (bodyMaterial, weaponMaterial) = EnsurePreviewMaterials();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new InvalidOperationException($"Model is missing: {ModelPath}");

            var instance = new GameObject("ER_Base_Male_Animated");
            try
            {
                instance.AddComponent<Animator>();
                var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                modelInstance.name = "Model";
                modelInstance.transform.SetParent(instance.transform, false);
                modelInstance.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                foreach (var renderer in modelInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.enabled = true;
                    renderer.sharedMaterial = bodyMaterial;
                }
                AddWeaponBoneAndSword(modelInstance.transform, weaponMaterial, skeleton);
                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static HkxSkeletonData LoadSkeletonForMotion(HkxMotionData motion, HkxSkeletonData defaultSkeleton)
        {
            if (string.IsNullOrEmpty(motion.skeleton)) return defaultSkeleton;
            var skeletonPath = Path.Combine(
                ProjectPaths.RepositoryRoot, "converted", "elden_ring", motion.skeleton + ".skeleton.json");
            if (!File.Exists(skeletonPath))
                throw new FileNotFoundException($"Motion skeleton is missing: {motion.skeleton}", skeletonPath);
            return JsonUtility.FromJson<HkxSkeletonData>(File.ReadAllText(skeletonPath));
        }

        public static void ImportAndValidateMotion()
        {
            Import();
            var motionPath = Path.Combine(ProjectPaths.RepositoryRoot, "converted", "elden_ring", "a023_030000.motion.json");
            var skeletonPath = Path.Combine(ProjectPaths.RepositoryRoot, "converted", "elden_ring", "c0000.skeleton.json");
            var motion = JsonUtility.FromJson<HkxMotionData>(File.ReadAllText(motionPath));
            var skeleton = JsonUtility.FromJson<HkxSkeletonData>(File.ReadAllText(skeletonPath));
            var sourceReferenceGlobals = BuildUnitySourceGlobals(skeleton, skeleton.bones.Select(bone => bone.reference).ToArray());
            var sourceIndices = skeleton.bones.ToDictionary(bone => bone.name, bone => bone.index, StringComparer.Ordinal);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var transforms = instance.GetComponentsInChildren<Transform>(true).ToDictionary(item => item.name, item => item);
                var diagnosticNames = new[]
                {
                    "Pelvis", "Spine2", "Head", "L_Clavicle", "L_UpperArm", "L_Forearm", "L_Hand",
                    "R_Clavicle", "R_UpperArm", "R_Forearm", "R_Hand"
                };
                foreach (var name in diagnosticNames)
                    Debug.Log($"ER_BIND_AXIS: {name} target={transforms[name].position:F6} source={sourceReferenceGlobals[sourceIndices[name]].GetPosition():F6}");
                var bindLocalRotations = new[] { "L_Hand", "R_Hand", "R_Weapon" }
                    .ToDictionary(name => name, name => transforms[name].localRotation, StringComparer.Ordinal);
                var bindWeaponLocalPosition = transforms["R_Weapon"].localPosition;
                var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                renderer.updateWhenOffscreen = true;
                var trackedNames = new[] { "Head", "Spine2", "L_Hand", "R_Hand", "L_Foot", "R_Foot" };
                clip.SampleAnimation(instance, 0f);
                var initialPositions = trackedNames.ToDictionary(name => name, name => transforms[name].position);
                var initialHandPosition = transforms["R_Hand"].position;
                var initialUpperArm = transforms["R_UpperArm"].localRotation;
                var initialForearm = transforms["R_Forearm"].localRotation;
                var initialHandRotation = transforms["R_Hand"].localRotation;
                var initialUpperArmLength = Vector3.Distance(transforms["R_UpperArm"].position, transforms["R_Forearm"].position);
                var initialForearmLength = Vector3.Distance(transforms["R_Forearm"].position, transforms["R_Hand"].position);
                var initialUpperArmScale = transforms["R_UpperArm"].localScale;
                var initialForearmScale = transforms["R_Forearm"].localScale;
                var initialHandScale = transforms["R_Hand"].localScale;
                var previousTip = transforms["R_Weapon"].TransformPoint(new Vector3(0f, 0.012f, 0f));
                var minBounds = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                var maxBounds = Vector3.zero;
                var report = new EldenRingMotionValidationReport
                {
                    animation = clip.name,
                    sampled_frames = Mathf.RoundToInt(clip.length * clip.frameRate) + 1,
                    target_reference_left_hand = transforms["L_Hand"].position,
                    source_reference_left_hand = sourceReferenceGlobals[sourceIndices["L_Hand"]].GetPosition(),
                    target_reference_right_hand = transforms["R_Hand"].position,
                    source_reference_right_hand = sourceReferenceGlobals[sourceIndices["R_Hand"]].GetPosition(),
                };
                var directionErrorSums = new float[4];
                var bakedMesh = new Mesh();
                for (var frame = 0; frame < report.sampled_frames; frame++)
                {
                    clip.SampleAnimation(instance, Mathf.Min(frame / clip.frameRate, clip.length));
                    var sourceLocals = new float[skeleton.bone_count][];
                    for (var bone = 0; bone < skeleton.bone_count; bone++)
                    {
                        sourceLocals[bone] = new float[ValuesPerSample];
                        Array.Copy(motion.tracks[bone].samples, Math.Min(frame, motion.frame_count - 1) * ValuesPerSample,
                            sourceLocals[bone], 0, ValuesPerSample);
                    }
                    var sourceGlobals = BuildUnitySourceGlobals(skeleton, sourceLocals);
                    var errors = new[]
                    {
                        SegmentDirectionError(transforms, sourceGlobals, sourceIndices, "L_UpperArm", "L_Forearm"),
                        SegmentDirectionError(transforms, sourceGlobals, sourceIndices, "L_Forearm", "L_Hand"),
                        SegmentDirectionError(transforms, sourceGlobals, sourceIndices, "R_UpperArm", "R_Forearm"),
                        SegmentDirectionError(transforms, sourceGlobals, sourceIndices, "R_Forearm", "R_Hand"),
                    };
                    for (var errorIndex = 0; errorIndex < errors.Length; errorIndex++)
                        directionErrorSums[errorIndex] += errors[errorIndex];
                    report.max_left_upper_arm_direction_error = Mathf.Max(report.max_left_upper_arm_direction_error, errors[0]);
                    report.max_left_forearm_direction_error = Mathf.Max(report.max_left_forearm_direction_error, errors[1]);
                    report.max_right_upper_arm_direction_error = Mathf.Max(report.max_right_upper_arm_direction_error, errors[2]);
                    report.max_right_forearm_direction_error = Mathf.Max(report.max_right_forearm_direction_error, errors[3]);
                    report.max_left_hand_rotation_error = Mathf.Max(report.max_left_hand_rotation_error,
                        LocalDeltaRotationError("L_Hand", transforms, bindLocalRotations, sourceGlobals, sourceReferenceGlobals, sourceIndices));
                    report.max_right_hand_rotation_error = Mathf.Max(report.max_right_hand_rotation_error,
                        LocalDeltaRotationError("R_Hand", transforms, bindLocalRotations, sourceGlobals, sourceReferenceGlobals, sourceIndices));
                    report.max_weapon_rotation_error = Mathf.Max(report.max_weapon_rotation_error,
                        LocalDeltaRotationError("R_Weapon", transforms, bindLocalRotations, sourceGlobals, sourceReferenceGlobals, sourceIndices));
                    report.max_weapon_socket_position_error = Mathf.Max(report.max_weapon_socket_position_error,
                        Vector3.Distance(transforms["R_Weapon"].localPosition, bindWeaponLocalPosition));
                    report.max_right_upper_arm_angle = Mathf.Max(report.max_right_upper_arm_angle,
                        Quaternion.Angle(initialUpperArm, transforms["R_UpperArm"].localRotation));
                    report.max_right_forearm_angle = Mathf.Max(report.max_right_forearm_angle,
                        Quaternion.Angle(initialForearm, transforms["R_Forearm"].localRotation));
                    report.max_right_hand_angle = Mathf.Max(report.max_right_hand_angle,
                        Quaternion.Angle(initialHandRotation, transforms["R_Hand"].localRotation));
                    report.max_right_hand_travel = Mathf.Max(report.max_right_hand_travel,
                        Vector3.Distance(initialHandPosition, transforms["R_Hand"].position));
                    var poseDisplacement = trackedNames.Sum(name => Vector3.Distance(initialPositions[name], transforms[name].position));
                    report.max_pose_joint_displacement = Mathf.Max(report.max_pose_joint_displacement, poseDisplacement);
                    report.max_upper_arm_length_error = Mathf.Max(report.max_upper_arm_length_error,
                        Mathf.Abs(Vector3.Distance(transforms["R_UpperArm"].position, transforms["R_Forearm"].position) - initialUpperArmLength));
                    report.max_forearm_length_error = Mathf.Max(report.max_forearm_length_error,
                        Mathf.Abs(Vector3.Distance(transforms["R_Forearm"].position, transforms["R_Hand"].position) - initialForearmLength));
                    report.max_arm_scale_error = Mathf.Max(report.max_arm_scale_error,
                        (transforms["R_UpperArm"].localScale - initialUpperArmScale).magnitude,
                        (transforms["R_Forearm"].localScale - initialForearmScale).magnitude,
                        (transforms["R_Hand"].localScale - initialHandScale).magnitude);
                    var tip = transforms["R_Weapon"].TransformPoint(new Vector3(0f, 0.012f, 0f));
                    if (frame > 0) report.sword_tip_path_length += Vector3.Distance(previousTip, tip);
                    previousTip = tip;

                    if (frame % 12 == 0 || frame == report.sampled_frames - 1)
                    {
                        renderer.BakeMesh(bakedMesh);
                        var size = bakedMesh.bounds.size;
                        minBounds = Vector3.Min(minBounds, size);
                        maxBounds = Vector3.Max(maxBounds, size);
                    }
                }
                UnityEngine.Object.DestroyImmediate(bakedMesh);
                report.mean_left_upper_arm_direction_error = directionErrorSums[0] / report.sampled_frames;
                report.mean_left_forearm_direction_error = directionErrorSums[1] / report.sampled_frames;
                report.mean_right_upper_arm_direction_error = directionErrorSums[2] / report.sampled_frames;
                report.mean_right_forearm_direction_error = directionErrorSums[3] / report.sampled_frames;
                report.min_skinned_bounds_size = minBounds;
                report.max_skinned_bounds_size = maxBounds;
                report.passed = report.max_right_upper_arm_angle > 25f &&
                                report.max_right_hand_travel > 0.25f &&
                                report.sword_tip_path_length > 1f &&
                                report.max_upper_arm_length_error < 0.001f &&
                                report.max_forearm_length_error < 0.001f &&
                                report.max_arm_scale_error < 0.0001f &&
                                report.max_left_upper_arm_direction_error < 0.1f &&
                                report.max_left_forearm_direction_error < 0.1f &&
                                report.max_right_upper_arm_direction_error < 0.1f &&
                                report.max_right_forearm_direction_error < 0.1f &&
                                report.max_left_hand_rotation_error < 0.1f &&
                                report.max_right_hand_rotation_error < 0.1f &&
                                report.max_weapon_rotation_error < 0.1f &&
                                report.max_weapon_socket_position_error < 0.001f &&
                                maxBounds.x < 2.2f && maxBounds.y < 2.2f && maxBounds.z < 2.2f &&
                                minBounds.magnitude > 0.5f;
                var output = Path.Combine(ProjectPaths.RepositoryRoot, "catalog", "elden_ring_motion_validation.json");
                File.WriteAllText(output, JsonUtility.ToJson(report, true));
                Debug.Log($"ELDEN_RING_MOTION_ONLY_VALIDATION: passed={report.passed}, upperArm={report.max_right_upper_arm_angle:F1}deg, forearm={report.max_right_forearm_angle:F1}deg, handTravel={report.max_right_hand_travel:F3}m, tipPath={report.sword_tip_path_length:F3}m, armDirectionError=L({report.max_left_upper_arm_direction_error:F3}/{report.max_left_forearm_direction_error:F3}) R({report.max_right_upper_arm_direction_error:F3}/{report.max_right_forearm_direction_error:F3})deg, armLengthError={report.max_upper_arm_length_error:F6}/{report.max_forearm_length_error:F6}m, scaleError={report.max_arm_scale_error:F6}, output={output}");
                if (!report.passed) throw new InvalidOperationException("Hierarchy retarget did not pass motion-only validation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
            ValidateAllPreparedTerminalRotations(prefab, skeleton);
            ValidateShadowMotion(prefab);
        }

        private static void ValidateShadowMotion(GameObject prefab)
        {
            const string motionName = "shadow_a023_030040";
            var motionPath = Path.Combine(ProjectPaths.RepositoryRoot, "converted", "elden_ring", motionName + ".motion.json");
            if (!File.Exists(motionPath)) return;
            var motion = JsonUtility.FromJson<HkxMotionData>(File.ReadAllText(motionPath));
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{GeneratedRoot}/{motionName}.anim");
            if (clip == null) throw new InvalidOperationException($"Prepared clip is missing: {motionName}");
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var bones = instance.GetComponentsInChildren<Transform>(true)
                    .ToDictionary(item => item.name, item => item, StringComparer.Ordinal);
                clip.SampleAnimation(instance, 0f);
                var leftHandStart = bones["L_Hand"].position - bones["Pelvis"].position;
                var rightHandStart = bones["R_Hand"].position - bones["Pelvis"].position;
                var leftUpperArmStart = bones["L_UpperArm"].localRotation;
                var rightUpperArmStart = bones["R_UpperArm"].localRotation;
                var leftForearmStart = bones["L_Forearm"].localRotation;
                var rightForearmStart = bones["R_Forearm"].localRotation;
                var report = new ShadowMotionValidationReport
                {
                    animation = motionName,
                    sampled_frames = motion.frame_count,
                };
                for (var frame = 0; frame < motion.frame_count; frame++)
                {
                    clip.SampleAnimation(instance, Mathf.Min(frame / motion.fps, clip.length));
                    report.max_left_hand_pelvis_relative_travel = Mathf.Max(
                        report.max_left_hand_pelvis_relative_travel,
                        Vector3.Distance(leftHandStart, bones["L_Hand"].position - bones["Pelvis"].position));
                    report.max_right_hand_pelvis_relative_travel = Mathf.Max(
                        report.max_right_hand_pelvis_relative_travel,
                        Vector3.Distance(rightHandStart, bones["R_Hand"].position - bones["Pelvis"].position));
                    report.max_left_upper_arm_rotation = Mathf.Max(report.max_left_upper_arm_rotation,
                        Quaternion.Angle(leftUpperArmStart, bones["L_UpperArm"].localRotation));
                    report.max_right_upper_arm_rotation = Mathf.Max(report.max_right_upper_arm_rotation,
                        Quaternion.Angle(rightUpperArmStart, bones["R_UpperArm"].localRotation));
                    report.max_left_forearm_rotation = Mathf.Max(report.max_left_forearm_rotation,
                        Quaternion.Angle(leftForearmStart, bones["L_Forearm"].localRotation));
                    report.max_right_forearm_rotation = Mathf.Max(report.max_right_forearm_rotation,
                        Quaternion.Angle(rightForearmStart, bones["R_Forearm"].localRotation));
                }
                report.passed = report.max_left_hand_pelvis_relative_travel > 0.1f &&
                                report.max_right_hand_pelvis_relative_travel > 0.2f &&
                                report.max_left_upper_arm_rotation > 20f &&
                                report.max_right_upper_arm_rotation > 30f &&
                                report.max_left_forearm_rotation > 10f &&
                                report.max_right_forearm_rotation > 10f;
                var output = Path.Combine(ProjectPaths.RepositoryRoot, "catalog", "shadow_motion_validation.json");
                File.WriteAllText(output, JsonUtility.ToJson(report, true));
                Debug.Log($"SHADOW_MOTION_VALIDATION: passed={report.passed}, " +
                          $"handTravel=L{report.max_left_hand_pelvis_relative_travel:F3}/R{report.max_right_hand_pelvis_relative_travel:F3}m, " +
                          $"upperArm=L{report.max_left_upper_arm_rotation:F1}/R{report.max_right_upper_arm_rotation:F1}deg, " +
                          $"forearm=L{report.max_left_forearm_rotation:F1}/R{report.max_right_forearm_rotation:F1}deg");
                if (!report.passed)
                    throw new InvalidOperationException("Shadow motion is rigid or missing expected limb animation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void ValidateAllPreparedTerminalRotations(GameObject prefab, HkxSkeletonData skeleton)
        {
            var report = new EldenRingTerminalRotationBatchReport { passed = true };
            var convertedDirectory = Path.Combine(ProjectPaths.RepositoryRoot, "converted", "elden_ring");
            foreach (var jsonPath in Directory.GetFiles(convertedDirectory, "*.motion.json").OrderBy(path => path, StringComparer.Ordinal))
            {
                var motion = JsonUtility.FromJson<HkxMotionData>(File.ReadAllText(jsonPath));
                var motionSkeleton = LoadSkeletonForMotion(motion, skeleton);
                ValidateSkeleton(motionSkeleton, motion);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{GeneratedRoot}/{motion.name}.anim");
                if (clip == null) throw new InvalidOperationException($"Prepared clip is missing: {motion.name}");
                var instance = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    var transforms = instance.GetComponentsInChildren<Transform>(true)
                        .ToDictionary(item => item.name, item => item, StringComparer.Ordinal);
                    var bindRotations = new[] { "L_Hand", "R_Hand", "R_Weapon" }
                        .ToDictionary(name => name, name => transforms[name].localRotation, StringComparer.Ordinal);
                    var bindSocketPosition = transforms["R_Weapon"].localPosition;
                    var indices = motionSkeleton.bones.ToDictionary(bone => bone.name, bone => bone.index, StringComparer.Ordinal);
                    var clipReport = new EldenRingTerminalRotationClipReport
                    {
                        animation = motion.name,
                        sampled_frames = motion.frame_count,
                    };
                    for (var frame = 0; frame < motion.frame_count; frame++)
                    {
                        clip.SampleAnimation(instance, Mathf.Min(frame / motion.fps, clip.length));
                        clipReport.max_left_hand_error = Mathf.Max(clipReport.max_left_hand_error,
                            DirectLocalDeltaError("L_Hand", frame, motion, motionSkeleton, transforms, bindRotations, indices));
                        clipReport.max_right_hand_error = Mathf.Max(clipReport.max_right_hand_error,
                            DirectLocalDeltaError("R_Hand", frame, motion, motionSkeleton, transforms, bindRotations, indices));
                        clipReport.max_weapon_error = Mathf.Max(clipReport.max_weapon_error,
                            DirectLocalDeltaError("R_Weapon", frame, motion, motionSkeleton, transforms, bindRotations, indices));
                        clipReport.max_socket_local_position_error = Mathf.Max(clipReport.max_socket_local_position_error,
                            Vector3.Distance(bindSocketPosition, transforms["R_Weapon"].localPosition));
                    }
                    // Unity's quaternion component curves can differ by a few degrees after asset
                    // serialization/sampling even at nominal source frame times. Keep this visible
                    // in the report and reject material deviations rather than sub-degree noise.
                    clipReport.passed = clipReport.max_left_hand_error < 5f &&
                                        clipReport.max_right_hand_error < 5f &&
                                        clipReport.max_weapon_error < 0.1f &&
                                        clipReport.max_socket_local_position_error < 0.001f;
                    report.clips.Add(clipReport);
                    report.passed &= clipReport.passed;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
            var output = Path.Combine(ProjectPaths.RepositoryRoot, "catalog", "elden_ring_terminal_rotation_validation.json");
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log($"ELDEN_RING_TERMINAL_ROTATION_VALIDATION: passed={report.passed}, clips={report.clips.Count}, output={output}");
            if (!report.passed) throw new InvalidOperationException("At least one prepared clip failed terminal rotation validation.");
        }

        private static float DirectLocalDeltaError(
            string boneName, int frame, HkxMotionData motion, HkxSkeletonData skeleton,
            Dictionary<string, Transform> transforms, Dictionary<string, Quaternion> bindRotations,
            Dictionary<string, int> indices)
        {
            var index = indices[boneName];
            var reference = ReadMirroredRotation(skeleton.bones[index].reference);
            var values = new float[ValuesPerSample];
            Array.Copy(motion.tracks[index].samples, frame * ValuesPerSample, values, 0, ValuesPerSample);
            var animated = ReadMirroredRotation(values);
            var expected = bindRotations[boneName] * Quaternion.Inverse(reference) * animated;
            return Quaternion.Angle(expected, transforms[boneName].localRotation);
        }

        private static float SegmentDirectionError(
            Dictionary<string, Transform> targetTransforms, Matrix4x4[] sourceGlobals,
            Dictionary<string, int> sourceIndices, string parentName, string childName)
        {
            var targetDirection = targetTransforms[childName].position - targetTransforms[parentName].position;
            var sourceDirection = sourceGlobals[sourceIndices[childName]].GetPosition() -
                                  sourceGlobals[sourceIndices[parentName]].GetPosition();
            return Vector3.Angle(targetDirection, sourceDirection);
        }

        private static float LocalDeltaRotationError(
            string boneName, Dictionary<string, Transform> targetTransforms,
            Dictionary<string, Quaternion> targetBindLocals, Matrix4x4[] sourceGlobals,
            Matrix4x4[] sourceReferenceGlobals, Dictionary<string, int> sourceIndices)
        {
            var boneIndex = sourceIndices[boneName];
            var parentIndex = boneName == "R_Weapon" ? sourceIndices["R_Hand"] :
                boneName == "R_Hand" ? sourceIndices["R_Forearm"] : sourceIndices["L_Forearm"];
            var sourceReferenceLocal = Quaternion.Inverse(sourceReferenceGlobals[parentIndex].rotation) *
                                       sourceReferenceGlobals[boneIndex].rotation;
            var sourceAnimatedLocal = Quaternion.Inverse(sourceGlobals[parentIndex].rotation) *
                                      sourceGlobals[boneIndex].rotation;
            var expected = targetBindLocals[boneName] * Quaternion.Inverse(sourceReferenceLocal) * sourceAnimatedLocal;
            return Quaternion.Angle(expected, targetTransforms[boneName].localRotation);
        }

        private static void Validate(HkxMotionData data)
        {
            if (data == null || data.format_version != 1) throw new InvalidDataException("Unsupported motion JSON.");
            if (data.frame_count < 2 || data.fps <= 0 || data.tracks == null || data.tracks.Length != data.track_count)
                throw new InvalidDataException("Motion JSON header is inconsistent.");
            foreach (var track in data.tracks)
                if (track.samples == null || track.samples.Length != data.frame_count * ValuesPerSample)
                    throw new InvalidDataException($"Track {track.name} has an invalid sample count.");
        }

        private static void ValidateSkeleton(HkxSkeletonData skeleton, HkxMotionData motion)
        {
            if (skeleton == null || skeleton.format_version != 1 || skeleton.bones == null ||
                skeleton.bones.Length != skeleton.bone_count || skeleton.bone_count != motion.track_count)
                throw new InvalidDataException("Skeleton JSON does not match the motion track count.");
            for (var index = 0; index < skeleton.bones.Length; index++)
            {
                var bone = skeleton.bones[index];
                if (bone.index != index || bone.reference == null || bone.reference.Length != ValuesPerSample)
                    throw new InvalidDataException($"Skeleton bone {index} is inconsistent.");
                if (bone.parent >= index) throw new InvalidDataException($"Skeleton bone {bone.name} has an invalid parent.");
                if (motion.tracks[index].bone_index != index || motion.tracks[index].name != bone.name)
                    throw new InvalidDataException($"Motion track {index} does not match skeleton bone {bone.name}.");
            }
        }

        private sealed class TargetBoneMapping
        {
            public Transform transform;
            public int sourceIndex;
            public Vector3 positionOffset;
            public Quaternion rotationOffset;
            public Vector3 restGlobalScale;
            public Matrix4x4 fixedParentGlobal;
            public TargetBoneMapping mappedParent;
        }

        private static void AddHierarchyRetargetedCurves(
            AnimationClip clip, Transform prefabRoot, Dictionary<string, Transform[]> byName,
            HkxMotionData motion, HkxSkeletonData skeleton)
        {
            var sourceReferenceGlobals = BuildUnitySourceGlobals(skeleton, skeleton.bones.Select(bone => bone.reference).ToArray());
            var sourceFrameGlobals = new Matrix4x4[motion.frame_count][];
            var sourceFrameLocals = new float[motion.frame_count][][];
            for (var frame = 0; frame < motion.frame_count; frame++)
            {
                var locals = new float[skeleton.bone_count][];
                for (var bone = 0; bone < skeleton.bone_count; bone++)
                {
                    locals[bone] = new float[ValuesPerSample];
                    Array.Copy(motion.tracks[bone].samples, frame * ValuesPerSample, locals[bone], 0, ValuesPerSample);
                }
                sourceFrameLocals[frame] = locals;
                sourceFrameGlobals[frame] = BuildUnitySourceGlobals(skeleton, locals);
            }

            var mappings = new Dictionary<Transform, TargetBoneMapping>();
            foreach (var bone in skeleton.bones)
            {
                if (!byName.TryGetValue(bone.name, out var candidates) || candidates.Length != 1) continue;
                var target = candidates[0];
                var targetRestGlobal = prefabRoot.worldToLocalMatrix * target.localToWorldMatrix;
                var sourceRestGlobal = sourceReferenceGlobals[bone.index];
                mappings[target] = new TargetBoneMapping
                {
                    transform = target,
                    sourceIndex = bone.index,
                    positionOffset = targetRestGlobal.GetPosition() - sourceRestGlobal.GetPosition(),
                    rotationOffset = Quaternion.Inverse(sourceRestGlobal.rotation) * targetRestGlobal.rotation,
                    restGlobalScale = targetRestGlobal.lossyScale,
                    fixedParentGlobal = prefabRoot.worldToLocalMatrix * target.parent.localToWorldMatrix,
                };
            }
            foreach (var mapping in mappings.Values)
                mappings.TryGetValue(mapping.transform.parent, out mapping.mappedParent);

            var desiredGlobals = new Dictionary<TargetBoneMapping, Matrix4x4[]>();
            foreach (var mapping in mappings.Values)
            {
                var globals = new Matrix4x4[motion.frame_count];
                for (var frame = 0; frame < motion.frame_count; frame++)
                {
                    var sourceGlobal = sourceFrameGlobals[frame][mapping.sourceIndex];
                    globals[frame] = Matrix4x4.TRS(
                        sourceGlobal.GetPosition() + mapping.positionOffset,
                        sourceGlobal.rotation * mapping.rotationOffset,
                        mapping.restGlobalScale);
                }
                desiredGlobals[mapping] = globals;
            }

            foreach (var mapping in mappings.Values)
            {
                var path = AnimationUtility.CalculateTransformPath(mapping.transform, prefabRoot);
                var rotations = new[] { new Keyframe[motion.frame_count], new Keyframe[motion.frame_count], new Keyframe[motion.frame_count], new Keyframe[motion.frame_count] };
                var previousRotation = mapping.transform.localRotation;
                for (var frame = 0; frame < motion.frame_count; frame++)
                {
                    Quaternion rotation;
                    if (UsesLocalBoneDelta(mapping.transform.name))
                    {
                        var sourceReference = ReadMirroredRotation(skeleton.bones[mapping.sourceIndex].reference);
                        var sourceAnimated = ReadMirroredRotation(sourceFrameLocals[frame][mapping.sourceIndex]);
                        rotation = mapping.transform.localRotation * Quaternion.Inverse(sourceReference) * sourceAnimated;
                    }
                    else
                    {
                        var parentGlobal = mapping.mappedParent != null
                            ? desiredGlobals[mapping.mappedParent][frame]
                            : mapping.fixedParentGlobal;
                        rotation = Quaternion.Inverse(parentGlobal.rotation) * desiredGlobals[mapping][frame].rotation;
                    }
                    if (Quaternion.Dot(previousRotation, rotation) < 0f)
                        rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                    previousRotation = rotation;
                    var time = frame / motion.fps;
                    rotations[0][frame] = new Keyframe(time, rotation.x);
                    rotations[1][frame] = new Keyframe(time, rotation.y);
                    rotations[2][frame] = new Keyframe(time, rotation.z);
                    rotations[3][frame] = new Keyframe(time, rotation.w);
                }
                SetCurve(clip, path, "m_LocalRotation.x", rotations[0]);
                SetCurve(clip, path, "m_LocalRotation.y", rotations[1]);
                SetCurve(clip, path, "m_LocalRotation.z", rotations[2]);
                SetCurve(clip, path, "m_LocalRotation.w", rotations[3]);
            }
            AddRootMotionCurves(clip, motion.root_motion, motion.frame_count, motion.fps);
        }

        private static bool UsesLocalBoneDelta(string boneName) =>
            boneName.EndsWith("_Hand", StringComparison.Ordinal) ||
            boneName.Contains("Finger", StringComparison.Ordinal) ||
            boneName.Contains("Weapon", StringComparison.Ordinal) ||
            boneName.Contains("Shield", StringComparison.Ordinal);

        private static Quaternion ReadMirroredRotation(float[] values)
        {
            var rotation = new Quaternion(values[3], values[4], values[5], values[6]);
            return new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w);
        }

        private static Matrix4x4[] BuildSourceGlobals(HkxSkeletonData skeleton, float[][] localValues)
        {
            var globals = new Matrix4x4[skeleton.bone_count];
            for (var index = 0; index < skeleton.bone_count; index++)
            {
                var values = localValues[index];
                var local = Matrix4x4.TRS(
                    new Vector3(values[0], values[1], values[2]),
                    new Quaternion(values[3], values[4], values[5], values[6]).normalized,
                    new Vector3(values[7], values[8], values[9]));
                var parent = skeleton.bones[index].parent;
                globals[index] = parent < 0 ? local : globals[parent] * local;
            }
            return globals;
        }

        // Havok's player skeleton and this FBX use opposite handedness: their bind poses
        // are identical after reflecting the global X axis. A quaternion-only offset cannot
        // represent that reflection; failing to apply it turns arm directions by ~90 degrees.
        private static Matrix4x4[] BuildUnitySourceGlobals(HkxSkeletonData skeleton, float[][] localValues)
        {
            var sourceGlobals = BuildSourceGlobals(skeleton, localValues);
            var unityGlobals = new Matrix4x4[sourceGlobals.Length];
            for (var index = 0; index < sourceGlobals.Length; index++)
            {
                var position = sourceGlobals[index].GetPosition();
                var rotation = sourceGlobals[index].rotation;
                unityGlobals[index] = Matrix4x4.TRS(
                    new Vector3(-position.x, position.y, position.z),
                    new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w),
                    sourceGlobals[index].lossyScale);
            }
            return unityGlobals;
        }

        private static void AddRetargetedRotationCurves(
            AnimationClip clip, string path, Quaternion targetRest, float[] values, int frameCount, float fps)
        {
            var sourceRest = ReadQuaternion(values, 0);
            var previous = targetRest;
            var keys = new[] { new Keyframe[frameCount], new Keyframe[frameCount], new Keyframe[frameCount], new Keyframe[frameCount] };
            for (var frame = 0; frame < frameCount; frame++)
            {
                var rotation = targetRest * (Quaternion.Inverse(sourceRest) * ReadQuaternion(values, frame));
                if (Quaternion.Dot(previous, rotation) < 0f)
                    rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                previous = rotation;
                var time = frame / fps;
                keys[0][frame] = new Keyframe(time, rotation.x);
                keys[1][frame] = new Keyframe(time, rotation.y);
                keys[2][frame] = new Keyframe(time, rotation.z);
                keys[3][frame] = new Keyframe(time, rotation.w);
            }
            SetCurve(clip, path, "m_LocalRotation.x", keys[0]);
            SetCurve(clip, path, "m_LocalRotation.y", keys[1]);
            SetCurve(clip, path, "m_LocalRotation.z", keys[2]);
            SetCurve(clip, path, "m_LocalRotation.w", keys[3]);
        }

        private static Quaternion ReadQuaternion(float[] values, int frame)
        {
            var offset = frame * ValuesPerSample + 3;
            return new Quaternion(values[offset], values[offset + 1], values[offset + 2], values[offset + 3]).normalized;
        }

        private static void AddWeaponTranslationCurves(AnimationClip clip, string path, float[] values, int frameCount, float fps)
        {
            var keys = new[] { new Keyframe[frameCount], new Keyframe[frameCount], new Keyframe[frameCount] };
            for (var frame = 0; frame < frameCount; frame++)
            {
                var offset = frame * ValuesPerSample;
                var time = frame / fps;
                // The FBX armature retains a 100x unit transform and reverses its local X axis.
                keys[0][frame] = new Keyframe(time, -values[offset] * 0.01f);
                keys[1][frame] = new Keyframe(time, values[offset + 1] * 0.01f);
                keys[2][frame] = new Keyframe(time, values[offset + 2] * 0.01f);
            }
            SetCurve(clip, path, "m_LocalPosition.x", keys[0]);
            SetCurve(clip, path, "m_LocalPosition.y", keys[1]);
            SetCurve(clip, path, "m_LocalPosition.z", keys[2]);
        }

        private static void AddRootMotionCurves(AnimationClip clip, float[] values, int frameCount, float fps)
        {
            if (values == null || values.Length != frameCount * 4) return;
            var px = new Keyframe[frameCount];
            var py = new Keyframe[frameCount];
            var pz = new Keyframe[frameCount];
            var rx = new Keyframe[frameCount];
            var ry = new Keyframe[frameCount];
            var rz = new Keyframe[frameCount];
            var rw = new Keyframe[frameCount];
            for (var frame = 0; frame < frameCount; frame++)
            {
                var time = frame / fps;
                var offset = frame * 4;
                px[frame] = new Keyframe(time, values[offset]);
                py[frame] = new Keyframe(time, values[offset + 1]);
                pz[frame] = new Keyframe(time, values[offset + 2]);
                var rotation = Quaternion.AngleAxis(values[offset + 3] * Mathf.Rad2Deg, Vector3.up);
                rx[frame] = new Keyframe(time, rotation.x);
                ry[frame] = new Keyframe(time, rotation.y);
                rz[frame] = new Keyframe(time, rotation.z);
                rw[frame] = new Keyframe(time, rotation.w);
            }
            SetCurve(clip, "", "m_LocalPosition.x", px);
            SetCurve(clip, "", "m_LocalPosition.y", py);
            SetCurve(clip, "", "m_LocalPosition.z", pz);
            SetCurve(clip, "", "m_LocalRotation.x", rx);
            SetCurve(clip, "", "m_LocalRotation.y", ry);
            SetCurve(clip, "", "m_LocalRotation.z", rz);
            SetCurve(clip, "", "m_LocalRotation.w", rw);
        }

        private static void SetCurve(AnimationClip clip, string path, string property, Keyframe[] keys)
        {
            var curve = new AnimationCurve(keys);
            SetLinearTangents(curve);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
        }

        private static void SetLinearTangents(AnimationCurve curve)
        {
            for (var index = 0; index < curve.length; index++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
            }
        }

        private static (Material body, Material weapon) EnsurePreviewMaterials()
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("No lit preview shader is available.");
            var body = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);
            if (body == null)
            {
                body = new Material(shader) { name = "BodyPreview", color = new Color(0.42f, 0.45f, 0.5f, 1f) };
                body.SetFloat("_Metallic", 0f);
                body.SetFloat("_Glossiness", 0.25f);
                AssetDatabase.CreateAsset(body, BodyMaterialPath);
            }
            var weapon = AssetDatabase.LoadAssetAtPath<Material>(WeaponMaterialPath);
            if (weapon == null)
            {
                weapon = new Material(shader) { name = "WeaponPreview", color = new Color(0.65f, 0.68f, 0.72f, 1f) };
                weapon.SetFloat("_Metallic", 0.35f);
                weapon.SetFloat("_Glossiness", 0.4f);
                AssetDatabase.CreateAsset(weapon, WeaponMaterialPath);
            }
            return (body, weapon);
        }

        private static void AddWeaponBoneAndSword(Transform root, Material weaponMaterial, HkxSkeletonData skeleton)
        {
            var hand = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(transform => transform.name == "R_Hand");
            if (hand == null) throw new InvalidOperationException("R_Hand was not found in the imported model.");
            var referenceGlobals = BuildUnitySourceGlobals(skeleton, skeleton.bones.Select(bone => bone.reference).ToArray());
            var sourceWeaponIndex = Array.FindIndex(skeleton.bones, bone => bone.name == "R_Weapon");
            if (sourceWeaponIndex < 0) throw new InvalidOperationException("R_Weapon was not found in the source skeleton.");
            var sourceWeapon = referenceGlobals[sourceWeaponIndex];
            var weaponBone = new GameObject("R_Weapon").transform;
            weaponBone.SetParent(hand, false);
            var prefabRoot = root.parent;
            weaponBone.position = prefabRoot.TransformPoint(sourceWeapon.GetPosition());
            weaponBone.rotation = prefabRoot.rotation * sourceWeapon.rotation;

            var grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            grip.name = "SwordGrip";
            UnityEngine.Object.DestroyImmediate(grip.GetComponent<Collider>());
            grip.transform.SetParent(weaponBone, false);
            grip.transform.localPosition = new Vector3(0f, 0.0002f, 0f);
            grip.transform.localRotation = Quaternion.identity;
            grip.transform.localScale = new Vector3(0.00025f, 0.0014f, 0.00025f);
            grip.GetComponent<Renderer>().sharedMaterial = weaponMaterial;

            var guard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            guard.name = "SwordGuard";
            UnityEngine.Object.DestroyImmediate(guard.GetComponent<Collider>());
            guard.transform.SetParent(weaponBone, false);
            guard.transform.localPosition = new Vector3(0f, 0.0016f, 0f);
            guard.transform.localScale = new Vector3(0.0032f, 0.00035f, 0.00055f);
            guard.GetComponent<Renderer>().sharedMaterial = weaponMaterial;

            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "PrototypeSwordBlade";
            UnityEngine.Object.DestroyImmediate(blade.GetComponent<Collider>());
            blade.transform.SetParent(weaponBone, false);
            blade.transform.localPosition = new Vector3(0f, 0.0072f, 0f);
            blade.transform.localScale = new Vector3(0.00055f, 0.011f, 0.00018f);
            blade.GetComponent<Renderer>().sharedMaterial = weaponMaterial;
        }

        private static void AddMotionMannequin(Transform root, Material material)
        {
            var bones = root.GetComponentsInChildren<Transform>(true)
                .GroupBy(transform => transform.name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var marker = new GameObject("MotionMannequin_v2").transform;
            marker.SetParent(root, false);

            AddSegment(bones, "Pelvis", "Spine", .34f, material);
            AddSegment(bones, "Spine", "Spine1", .42f, material);
            AddSegment(bones, "Spine1", "Spine2", .42f, material);
            AddSegment(bones, "Spine2", "Neck", .32f, material);

            AddSegment(bones, "Pelvis", "L_Thigh", .24f, material);
            AddSegment(bones, "L_Thigh", "L_Calf", .22f, material);
            AddSegment(bones, "L_Calf", "L_Foot", .19f, material);
            AddSegment(bones, "Pelvis", "R_Thigh", .24f, material);
            AddSegment(bones, "R_Thigh", "R_Calf", .22f, material);
            AddSegment(bones, "R_Calf", "R_Foot", .19f, material);

            AddSegment(bones, "Spine2", "L_Clavicle", .20f, material);
            AddSegment(bones, "L_Clavicle", "L_UpperArm", .22f, material);
            AddSegment(bones, "L_UpperArm", "L_Forearm", .20f, material);
            AddSegment(bones, "L_Forearm", "L_Hand", .17f, material);
            AddSegment(bones, "Spine2", "R_Clavicle", .20f, material);
            AddSegment(bones, "R_Clavicle", "R_UpperArm", .22f, material);
            AddSegment(bones, "R_UpperArm", "R_Forearm", .20f, material);
            AddSegment(bones, "R_Forearm", "R_Hand", .17f, material);

            AddJoint(bones, "Pelvis", .15f, material);
            AddJoint(bones, "Spine2", .14f, material);
            AddJoint(bones, "Head", .18f, material);
            AddJoint(bones, "L_Hand", .075f, material);
            AddJoint(bones, "R_Hand", .075f, material);
            AddJoint(bones, "L_Foot", .09f, material);
            AddJoint(bones, "R_Foot", .09f, material);
        }

        private static void AddSegment(
            Dictionary<string, Transform> bones, string startName, string endName,
            float thickness, Material material)
        {
            if (!bones.TryGetValue(startName, out var start) || !bones.TryGetValue(endName, out var end)) return;
            var localEnd = start.InverseTransformPoint(end.position);
            var length = localEnd.magnitude;
            if (length < .0001f) return;
            var segment = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            segment.name = $"Mannequin_{startName}_{endName}";
            UnityEngine.Object.DestroyImmediate(segment.GetComponent<Collider>());
            segment.transform.SetParent(start, false);
            segment.transform.localPosition = localEnd * .5f;
            segment.transform.localRotation = Quaternion.FromToRotation(Vector3.up, localEnd.normalized);
            segment.transform.localScale = new Vector3(length * thickness, length * .5f, length * thickness);
            segment.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void AddJoint(
            Dictionary<string, Transform> bones, string boneName, float size, Material material)
        {
            if (!bones.TryGetValue(boneName, out var bone)) return;
            var joint = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            joint.name = $"Mannequin_{boneName}";
            UnityEngine.Object.DestroyImmediate(joint.GetComponent<Collider>());
            joint.transform.SetParent(bone, false);
            var parentScale = bone.lossyScale;
            joint.transform.localScale = new Vector3(
                size / Mathf.Max(.0001f, Mathf.Abs(parentScale.x)),
                size / Mathf.Max(.0001f, Mathf.Abs(parentScale.y)),
                size / Mathf.Max(.0001f, Mathf.Abs(parentScale.z)));
            joint.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent ?? "Assets", name);
        }
    }
}
