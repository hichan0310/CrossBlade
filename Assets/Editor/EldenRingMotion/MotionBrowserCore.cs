using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MotionPrototype
{
    [Serializable]
    internal sealed class SelectedFrame
    {
        public int frame;
        public float time;
        public string file;
    }

    [Serializable]
    internal sealed class MotionMetadata
    {
        public string character;
        public string animation;
        public float duration;
        public float source_fps;
        public float turn_duration;
        public List<SelectedFrame> selected_frames = new();
    }

    [Serializable]
    internal sealed class SelectionRecord
    {
        public string character;
        public string animation;
        public List<SelectedFrame> selected_frames = new();
    }

    [Serializable]
    internal sealed class SelectionDatabase
    {
        public List<SelectionRecord> records = new();
    }

    internal enum CameraPreset
    {
        SIDE,
        SIDE_3Q,
        FRONT_3Q
    }

    internal static class ProjectPaths
    {
        // CrossBlade is nested at workspace/gitrepo/CrossBlade. Motion archives and
        // conversion tools stay outside Assets so Unity never imports the raw database.
        public static string RepositoryRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        public static string SelectionFile => Path.Combine(RepositoryRoot, "catalog", "frame_selections.json");

        public static string SafeName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(safe) ? "Unknown" : safe;
        }
    }

    internal sealed class MotionPreviewSession : IDisposable
    {
        private static readonly HashSet<string> FramingBoneNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Pelvis", "Spine", "Spine1", "Spine2", "Head",
            "L_UpperArm", "L_Forearm", "L_Hand", "R_UpperArm", "R_Forearm", "R_Hand",
            "L_Thigh", "L_Calf", "L_Foot", "L_Toe0", "R_Thigh", "R_Calf", "R_Foot", "R_Toe0"
        };

        private Scene _scene;
        private GameObject _instance;
        private Camera _camera;
        private RenderTexture _previewTexture;
        private Material _bodyMaterial;
        private Material _weaponMaterial;
        private Material _trailMaterial;
        private readonly Dictionary<Renderer, Material[]> _originalMaterials = new();
        private LineRenderer _trail;
        private Transform _weapon;
        private Vector3 _initialPosition;
        private Quaternion _initialRotation;
        private Transform[] _poseTransforms = Array.Empty<Transform>();
        private Vector3[] _posePositions = Array.Empty<Vector3>();
        private Quaternion[] _poseRotations = Array.Empty<Quaternion>();
        private Vector3[] _poseScales = Array.Empty<Vector3>();
        private Bounds _framingBounds;
        private bool _hasFramingBounds;
        private CameraPreset _lastPreset = CameraPreset.SIDE_3Q;
        private float _lastYaw;
        private float _lastPitch;
        private float _lastDistance = 6f;
        private float _lastHeight = 1.2f;
        private bool _lastOrthographic = true;

        public GameObject Instance => _instance;
        public RenderTexture PreviewTexture => _previewTexture;

        public Camera PreviewCamera => _camera;

        public void SetCombatCamera(float size, float height)
        {
            _camera.transform.SetPositionAndRotation(new Vector3(0f, height, -20f), Quaternion.identity);
            _camera.orthographic = true;
            _camera.orthographicSize = size;
        }

        public void Load(GameObject source, Color bodyColor, Color weaponColor)
        {
            Dispose();
            if (source == null) return;

            _scene = EditorSceneManager.NewPreviewScene();
            _instance = UnityEngine.Object.Instantiate(source);
            _instance.name = source.name;
            _instance.hideFlags = HideFlags.None;
            SceneManager.MoveGameObjectToScene(_instance, _scene);
            _initialPosition = _instance.transform.localPosition;
            _initialRotation = _instance.transform.localRotation;
            _poseTransforms = _instance.GetComponentsInChildren<Transform>(true);
            _posePositions = _poseTransforms.Select(item => item.localPosition).ToArray();
            _poseRotations = _poseTransforms.Select(item => item.localRotation).ToArray();
            _poseScales = _poseTransforms.Select(item => item.localScale).ToArray();
            foreach (var skinned in _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skinned.updateWhenOffscreen = true;

            var cameraObject = new GameObject("Motion Preview Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, _scene);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.scene = _scene;
            _camera.cameraType = CameraType.Preview;
            _camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(_scene);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.orthographic = true;
            _camera.orthographicSize = 2.4f;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 100f;

            var lightObject = new GameObject("Motion Preview Key Light");
            SceneManager.MoveGameObjectToScene(lightObject, _scene);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.color = new Color(1f, 0.96f, 0.9f);
            lightObject.transform.rotation = Quaternion.Euler(35f, -35f, 0f);

            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            _bodyMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, color = bodyColor };
            _weaponMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, color = weaponColor };
            _trailMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, color = new Color(1f, 0.35f, 0.1f, 0.8f) };
            foreach (var renderer in _instance.GetComponentsInChildren<Renderer>(true))
                _originalMaterials[renderer] = renderer.sharedMaterials;
            SetSilhouette(true, bodyColor, weaponColor);
            FindWeaponAndCreateTrail();
            SetCamera(CameraPreset.SIDE_3Q, 0f, 0f, 6f, 1.2f, true);
        }

        public void SetSilhouette(bool enabled, Color bodyColor, Color weaponColor)
        {
            if (_instance == null) return;
            _bodyMaterial.color = bodyColor;
            _weaponMaterial.color = weaponColor;
            foreach (var renderer in _instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is LineRenderer) continue;
                if (!enabled)
                {
                    if (_originalMaterials.TryGetValue(renderer, out var original)) renderer.sharedMaterials = original;
                    continue;
                }
                var lower = renderer.name.ToLowerInvariant();
                var weapon = lower.Contains("weapon") || lower.Contains("sword") || lower.Contains("blade");
                var materials = new Material[Math.Max(1, renderer.sharedMaterials.Length)];
                for (var index = 0; index < materials.Length; index++) materials[index] = weapon ? _weaponMaterial : _bodyMaterial;
                renderer.sharedMaterials = materials;
            }
        }

        public void ForceSilhouette()
        {
            if (_bodyMaterial == null || _weaponMaterial == null) return;
            SetSilhouette(true, _bodyMaterial.color, _weaponMaterial.color);
        }

        public void Sample(AnimationClip clip, float time, bool rootMotion)
        {
            if (_instance == null || clip == null) return;
            ResetPose();
            clip.SampleAnimation(_instance, Mathf.Clamp(time, 0f, clip.length));
            if (!rootMotion)
            {
                _instance.transform.localPosition = _initialPosition;
                _instance.transform.localRotation = _initialRotation;
            }
        }

        public void ResetPose()
        {
            for (var index = 0; index < _poseTransforms.Length; index++)
            {
                if (_poseTransforms[index] == null) continue;
                _poseTransforms[index].localPosition = _posePositions[index];
                _poseTransforms[index].localRotation = _poseRotations[index];
                _poseTransforms[index].localScale = _poseScales[index];
            }
        }

        public void AttachWeapon(GameObject weaponPrefab, string boneName, Vector3 localPosition, Vector3 localEuler, Vector3 localScale)
        {
            if (_instance == null || weaponPrefab == null) return;
            var bone = _instance.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => transform.name.Equals(boneName, StringComparison.OrdinalIgnoreCase));
            if (bone == null) throw new InvalidOperationException($"Weapon bone not found: {boneName}");
            var weaponObject = UnityEngine.Object.Instantiate(weaponPrefab);
            weaponObject.name = "PrototypeSword";
            weaponObject.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(weaponObject, _scene);
            weaponObject.transform.SetParent(bone, false);
            weaponObject.transform.localPosition = localPosition;
            weaponObject.transform.localRotation = Quaternion.Euler(localEuler);
            weaponObject.transform.localScale = localScale;
            foreach (var renderer in weaponObject.GetComponentsInChildren<Renderer>(true))
                _originalMaterials[renderer] = renderer.sharedMaterials;
            SetSilhouette(true, _bodyMaterial.color, _weaponMaterial.color);
            FindWeaponAndCreateTrail();
        }

        public void UpdateTrail(AnimationClip clip, float currentTime, bool rootMotion, bool visible)
        {
            if (_trail == null || _weapon == null || clip == null)
                return;
            _trail.enabled = visible;
            if (!visible) return;

            const int samples = 18;
            var start = Mathf.Max(0f, currentTime - 0.28f);
            _trail.positionCount = samples;
            for (var i = 0; i < samples; i++)
            {
                var time = Mathf.Lerp(start, currentTime, i / (samples - 1f));
                Sample(clip, time, rootMotion);
                _trail.SetPosition(i, WeaponTip());
            }
            Sample(clip, currentTime, rootMotion);
        }

        public void FitCameraToClip(AnimationClip clip, bool rootMotion, int sampleCount = 25)
        {
            if (_instance == null || clip == null) return;
            _hasFramingBounds = false;
            for (var index = 0; index < Mathf.Max(2, sampleCount); index++)
            {
                var time = clip.length * index / (Mathf.Max(2, sampleCount) - 1f);
                Sample(clip, time, rootMotion);
                if (!TryGetVisualBounds(out var current)) continue;
                if (!_hasFramingBounds)
                {
                    _framingBounds = current;
                    _hasFramingBounds = true;
                }
                else
                {
                    _framingBounds.Encapsulate(current);
                }
            }
            Sample(clip, 0f, rootMotion);
            SetCamera(_lastPreset, _lastYaw, _lastPitch, _lastDistance, _lastHeight, _lastOrthographic);
        }

        public void SetCamera(CameraPreset preset, float yawOffset, float pitchOffset, float distance, float height, bool orthographic)
        {
            if (_camera == null) return;
            _lastPreset = preset;
            _lastYaw = yawOffset;
            _lastPitch = pitchOffset;
            _lastDistance = distance;
            _lastHeight = height;
            _lastOrthographic = orthographic;
            var yaw = preset switch
            {
                CameraPreset.SIDE => -90f,
                CameraPreset.FRONT_3Q => -35f,
                _ => -55f
            };
            var pitch = 6f + pitchOffset;
            var bounds = _hasFramingBounds ? _framingBounds : default;
            if (!_hasFramingBounds) TryGetVisualBounds(out bounds);
            var target = _hasFramingBounds || bounds.size.sqrMagnitude > 0f
                ? bounds.center + Vector3.up * (height - 1.2f)
                : (_instance != null ? _instance.transform.position : Vector3.zero) + Vector3.up * height;
            var forward = Quaternion.Euler(pitch, yaw + yawOffset, 0f) * Vector3.forward;
            var radius = bounds.size.sqrMagnitude > 0f ? Mathf.Max(1f, bounds.extents.magnitude) : 1f;
            _camera.transform.position = target - forward * Mathf.Max(distance, radius * 3f);
            _camera.transform.LookAt(target);
            _camera.orthographic = orthographic;
            _camera.orthographicSize = Mathf.Max(0.5f, radius * 1.18f * (distance / 6f));
            _camera.fieldOfView = 35f;
        }

        public RenderTexture RenderPreview(int width, int height, bool includeTrail)
        {
            if (_camera == null) return null;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return _previewTexture;
            EnsurePreviewTexture(width, height);
            var oldTrail = _trail != null && _trail.enabled;
            if (_trail != null) _trail.enabled = includeTrail && oldTrail;
            _camera.targetTexture = _previewTexture;
            var graphicsPipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            var qualityPipeline = QualitySettings.renderPipeline;
            // CrossBlade's quality override selects a 2D renderer, which skips these
            // Standard 3D materials. Render only this preview with the built-in path;
            // restore both settings even if rendering fails.
            try
            {
                QualitySettings.renderPipeline = null;
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = null;
                _camera.Render();
            }
            finally
            {
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = graphicsPipeline;
                QualitySettings.renderPipeline = qualityPipeline;
                _camera.targetTexture = null;
                if (_trail != null) _trail.enabled = oldTrail;
            }
            _camera.targetTexture = null;
            if (_trail != null) _trail.enabled = oldTrail;
            return _previewTexture;
        }

        public Texture2D Capture(int width, int height)
        {
            var texture = RenderPreview(width, height, false);
            if (texture == null) return null;
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var output = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            output.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            output.Apply();
            RenderTexture.active = previous;
            return output;
        }

        private void FindWeaponAndCreateTrail()
        {
            _weapon = _instance.GetComponentsInChildren<Transform>(true).FirstOrDefault(transform =>
            {
                var lower = transform.name.ToLowerInvariant();
                return lower.Contains("weapon") || lower.Contains("sword") || lower.Contains("blade");
            });
            if (_weapon == null) return;
            if (_trail != null) UnityEngine.Object.DestroyImmediate(_trail.gameObject);
            var trailObject = new GameObject("Weapon Tip Trail") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(trailObject, _scene);
            _trail = trailObject.AddComponent<LineRenderer>();
            _trail.sharedMaterial = _trailMaterial;
            _trail.startWidth = 0.035f;
            _trail.endWidth = 0.008f;
            _trail.useWorldSpace = true;
            _trail.enabled = false;
        }

        private static void EnsureSavedHostScene()
        {
            var active = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(active.path)) return;
            if (active.isDirty && active.rootCount > 0)
                throw new InvalidOperationException("Save the current scene before opening Motion Browser so an isolated preview scene can be added safely.");
            const string folder = "Assets/PrototypeValidation";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "PrototypeValidation");
            if (!EditorSceneManager.SaveScene(active, folder + "/MotionPreviewHost.unity"))
                throw new InvalidOperationException("Could not create the Motion Browser host scene.");
        }

        private Vector3 WeaponTip()
        {
            var renderer = _weapon.GetComponentInChildren<Renderer>();
            if (renderer != null)
                return renderer.bounds.center + _weapon.up * renderer.bounds.extents.magnitude;
            return _weapon.position + _weapon.up;
        }

        private bool TryGetVisualBounds(out Bounds bounds)
        {
            bounds = default;
            if (_instance == null) return false;

            // Mesh parts on the Danjin review skin can be driven by optional Elden Ring
            // face/accessory bones and briefly jump far away. Framing from those renderer
            // bounds makes the body unreadably small. Stable body bones describe the pose
            // we are reviewing and keep the camera useful through the whole motion.
            var framingBones = _poseTransforms.Where(transform =>
                    transform != null && FramingBoneNames.Contains(transform.name))
                .ToArray();
            if (framingBones.Length >= 8)
            {
                bounds = new Bounds(framingBones[0].position, Vector3.zero);
                for (var index = 1; index < framingBones.Length; index++)
                    bounds.Encapsulate(framingBones[index].position);
                bounds.Expand(Mathf.Max(0.18f, bounds.extents.magnitude * 0.18f));
                return true;
            }

            var renderers = _instance.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled && renderer is not LineRenderer).ToArray();
            if (renderers.Length == 0) return false;
            bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return true;
        }

        private void EnsurePreviewTexture(int width, int height)
        {
            if (_previewTexture != null && _previewTexture.width == width && _previewTexture.height == height) return;
            if (_previewTexture != null)
            {
                _previewTexture.Release();
                UnityEngine.Object.DestroyImmediate(_previewTexture);
            }
            _previewTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 4,
                name = "Motion Preview"
            };
            _previewTexture.Create();
        }

        public void Dispose()
        {
            if (_previewTexture != null)
            {
                _previewTexture.Release();
                UnityEngine.Object.DestroyImmediate(_previewTexture);
            }
            if (_bodyMaterial != null) UnityEngine.Object.DestroyImmediate(_bodyMaterial);
            if (_weaponMaterial != null) UnityEngine.Object.DestroyImmediate(_weaponMaterial);
            if (_trailMaterial != null) UnityEngine.Object.DestroyImmediate(_trailMaterial);
            if (_scene.IsValid() && _scene.isLoaded) EditorSceneManager.ClosePreviewScene(_scene);
            _previewTexture = null;
            _bodyMaterial = null;
            _weaponMaterial = null;
            _trailMaterial = null;
            _trail = null;
            _weapon = null;
            _camera = null;
            _instance = null;
            _poseTransforms = Array.Empty<Transform>();
            _posePositions = Array.Empty<Vector3>();
            _poseRotations = Array.Empty<Quaternion>();
            _poseScales = Array.Empty<Vector3>();
            _originalMaterials.Clear();
            _hasFramingBounds = false;
        }
    }

    internal static class MotionExporter
    {
        public static string Export(
            MotionPreviewSession session, GameObject character, AnimationClip clip,
            IEnumerable<int> frames, int resolution, float turnDuration, bool rootMotion)
        {
            var fps = Mathf.Max(1f, clip.frameRate);
            var characterName = ProjectPaths.SafeName(character.name.Replace("(Clone)", "").Trim());
            var clipName = ProjectPaths.SafeName(clip.name);
            var directory = Path.Combine(ProjectPaths.RepositoryRoot, "output", "silhouettes", characterName, clipName);
            Directory.CreateDirectory(directory);

            var metadata = new MotionMetadata
            {
                character = characterName,
                animation = clip.name,
                duration = clip.length,
                source_fps = fps,
                turn_duration = turnDuration
            };

            session.FitCameraToClip(clip, rootMotion);

            foreach (var frame in frames.Distinct().OrderBy(value => value))
            {
                var time = Mathf.Min(frame / fps, clip.length);
                session.Sample(clip, time, rootMotion);
                session.ForceSilhouette();
                var image = session.Capture(resolution, resolution);
                if (image == null) throw new InvalidOperationException("Preview capture failed.");
                var fileName = $"frame_{frame:0000}.png";
                File.WriteAllBytes(Path.Combine(directory, fileName), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                metadata.selected_frames.Add(new SelectedFrame { frame = frame, time = time, file = fileName });
            }

            File.WriteAllText(Path.Combine(directory, "motion.json"), JsonUtility.ToJson(metadata, true));
            AssetDatabase.Refresh();
            return directory;
        }

        public static List<SelectedFrame> LoadSelection(string character, string animation)
        {
            if (!File.Exists(ProjectPaths.SelectionFile)) return new List<SelectedFrame>();
            try
            {
                var database = JsonUtility.FromJson<SelectionDatabase>(File.ReadAllText(ProjectPaths.SelectionFile));
                var record = database?.records?.FirstOrDefault(item => item.character == character && item.animation == animation);
                return record?.selected_frames ?? new List<SelectedFrame>();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not read frame selections: {exception.Message}");
                return new List<SelectedFrame>();
            }
        }

        public static void SaveSelection(string character, string animation, List<SelectedFrame> frames)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProjectPaths.SelectionFile)!);
            var database = File.Exists(ProjectPaths.SelectionFile)
                ? JsonUtility.FromJson<SelectionDatabase>(File.ReadAllText(ProjectPaths.SelectionFile))
                : new SelectionDatabase();
            database ??= new SelectionDatabase();
            database.records ??= new List<SelectionRecord>();
            var record = database.records.FirstOrDefault(item => item.character == character && item.animation == animation);
            if (record == null)
            {
                record = new SelectionRecord { character = character, animation = animation };
                database.records.Add(record);
            }
            record.selected_frames = frames;
            File.WriteAllText(ProjectPaths.SelectionFile, JsonUtility.ToJson(database, true));
        }
    }
}
