using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Scripts
{
    public class ActorVisualController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform moveMount;

        [Header("Persistent 3D Character (optional)")]
        [SerializeField] private CharacterAnimationPlayer characterPlayer;
        [Tooltip("Hide only the Move root's legacy character sprite; child sprites and effects remain available.")]
        [SerializeField] private bool replaceMoveRootSprite = true;
        private Actor _actor;
        private bool graphCharacterCreated;

        private void EnsureGraphCharacter()
        {
            if (graphCharacterCreated) return;
            _actor = GetComponent<Actor>();
            var graph = _actor != null ? _actor.CombatMoveGraph : null;
            if (graph == null || graph.previewCharacter == null) return;
            // A scene-authored graph model is visible in edit mode and also serves at runtime.
            if (characterPlayer != null && characterPlayer.gameObject.name == "GraphCharacter" &&
                characterPlayer.AnimationRoot != null)
            {
                characterPlayer.Configure(characterPlayer.AnimationRoot, graph.motionRootPath, graph.previewYaw,
                    graph.startMove != null ? graph.startMove.CharacterAnimation : null);
                graphCharacterCreated = true;
                return;
            }
            if (characterPlayer != null) characterPlayer.gameObject.SetActive(false);
            var facing = new GameObject("GraphCharacter");
            facing.transform.SetParent(transform, false);
            var model = Instantiate(graph.previewCharacter, facing.transform);
            model.transform.localPosition = Vector3.zero;
            foreach (var previous in model.GetComponentsInChildren<CharacterAnimationPlayer>(true)) previous.enabled = false;
            characterPlayer = facing.AddComponent<CharacterAnimationPlayer>();
            characterPlayer.Configure(model, graph.motionRootPath, graph.previewYaw,
                graph.startMove != null ? graph.startMove.CharacterAnimation : null);
            graphCharacterCreated = true;
        }

        private void Awake()
        {
            _actor = GetComponent<Actor>();
            EnsureGraphCharacter();
            if (_actor != null && _actor.CombatMoveGraph != null && _actor.CombatMoveGraph.previewCharacter != null)
                MoveAttachmentAxes.ApplyTo(moveMount);
            characterPlayer?.Evaluate(null, 0f, _actor != null ? _actor.FacingSign : 1);
        }

        private void LateUpdate()
        {
            if (characterPlayer != null && _actor != null)
                characterPlayer.UpdateFacing(_actor.FacingSign);
        }

        private void HideReplacedSprite()
        {
            if (characterPlayer == null || !replaceMoveRootSprite || _currentMoveInstance == null) return;
            SpriteRenderer sprite = _currentMoveInstance.GetComponent<SpriteRenderer>();
            if (sprite != null) sprite.enabled = false;
        }

        [Header("Visual Reveal")]
        [SerializeField] private Transform previousVisualFallbackRoot;


        [Header("Debug")]
        [SerializeField] private Move currentMoveDebug;

        private readonly List<SpriteRenderer> _fallbackRenderers = new List<SpriteRenderer>();
        private bool _showPreviousVisual;

        private Move _currentMoveInstance;

        internal Move CurrentMoveInstance => _currentMoveInstance;
        internal bool HasMoveVisual => _currentMoveInstance != null;

        internal Move CreateMoveInstance(Move template)
        {
            EnsureGraphCharacter();
            if (template == null)
            {
                return null;
            }

            Transform parent = moveMount != null ? moveMount : transform;
            Move instance = Instantiate(template, parent);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            _currentMoveInstance = instance;
            currentMoveDebug = instance;
            HideReplacedSprite();

            return instance;
        }

        internal void ReleaseMoveInstance(Move instance)
        {
            if (instance == null)
            {
                return;
            }

            if (!instance.name.Contains("__DYING"))
            {
                instance.name += "__DYING";
                // debugging
            }

            if (_currentMoveInstance == instance)
            {
                _currentMoveInstance = null;
                currentMoveDebug = null;
            }

            Destroy(instance.gameObject);
        }

        internal void CapturePreviousVisualSnapshot()
        {
            if (_currentMoveInstance == null || previousVisualFallbackRoot == null)
            {
                ClearPreviousVisualSnapshot();
                return;
            }

            Transform sourceRoot = _currentMoveInstance.VisualRoot;
            if (sourceRoot == null)
            {
                ClearPreviousVisualSnapshot();
                return;
            }

            SpriteRenderer[] sourceRenderers = sourceRoot.GetComponentsInChildren<SpriteRenderer>(true);
            if (characterPlayer != null && replaceMoveRootSprite)
            {
                SpriteRenderer replaced = _currentMoveInstance.GetComponent<SpriteRenderer>();
                sourceRenderers = Array.FindAll(sourceRenderers, renderer => renderer != replaced);
            }
            if (sourceRenderers == null || sourceRenderers.Length == 0)
            {
                ClearPreviousVisualSnapshot();
                return;
            }

            EnsureFallbackPoolSize(sourceRenderers.Length);

            for (int i = 0; i < sourceRenderers.Length; i++)
            {
                SpriteRenderer source = sourceRenderers[i];
                SpriteRenderer target = _fallbackRenderers[i];

                if (source == null || target == null)
                {
                    continue;
                }

                target.gameObject.SetActive(true);
                target.sprite = source.sprite;
                target.color = source.color;
                target.flipX = source.flipX;
                target.flipY = source.flipY;
                target.sortingLayerID = source.sortingLayerID;
                target.sortingOrder = source.sortingOrder;
                target.sharedMaterial = source.sharedMaterial;
                target.transform.localPosition = previousVisualFallbackRoot.InverseTransformPoint(source.transform.position);
                target.transform.localRotation = Quaternion.Inverse(previousVisualFallbackRoot.rotation) * source.transform.rotation;

                Vector3 sourceScale = source.transform.lossyScale;
                target.transform.localScale = new Vector3(
                    Mathf.Abs(sourceScale.x),
                    Mathf.Abs(sourceScale.y),
                    Mathf.Abs(sourceScale.z)
                );
            }

            for (int i = sourceRenderers.Length; i < _fallbackRenderers.Count; i++)
            {
                if (_fallbackRenderers[i] != null)
                {
                    _fallbackRenderers[i].gameObject.SetActive(false);
                }
            }

            previousVisualFallbackRoot.gameObject.SetActive(true);
        }

        internal void BeginPreviousVisual(bool enabled)
        {
            _showPreviousVisual = enabled && HasFallbackVisual();

            if (!_showPreviousVisual)
            {
                SetPreviousVisualVisible(false);
            }
        }

        internal void ClearPreviousVisualSnapshot()
        {
            _showPreviousVisual = false;
            SetPreviousVisualVisible(false);
        }

        private bool HasFallbackVisual()
        {
            if (previousVisualFallbackRoot == null)
            {
                return false;
            }

            for (int i = 0; i < _fallbackRenderers.Count; i++)
            {
                if (_fallbackRenderers[i] != null &&
                    _fallbackRenderers[i].gameObject.activeSelf &&
                    _fallbackRenderers[i].sprite != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetPreviousVisualVisible(bool visible)
        {
            if (previousVisualFallbackRoot == null)
            {
                return;
            }

            previousVisualFallbackRoot.gameObject.SetActive(visible);
        }

        private void EnsureFallbackPoolSize(int count)
        {
            if (previousVisualFallbackRoot == null)
            {
                return;
            }

            while (_fallbackRenderers.Count < count)
            {
                GameObject go = new GameObject($"PreviousVisual_{_fallbackRenderers.Count}");
                go.transform.SetParent(previousVisualFallbackRoot, false);
                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                _fallbackRenderers.Add(sr);
            }
        }

        internal void RefreshMoveVisualState(bool hasCurrent, float moveProgress)
        {
            // Between actions, keep the last sampled pose until the next Move starts.
            if (hasCurrent && _currentMoveInstance != null)
                characterPlayer?.Evaluate(_currentMoveInstance, moveProgress,
                    _actor != null ? _actor.FacingSign : 1);
            HideReplacedSprite();
            if (_currentMoveInstance == null)
            {
                ClearPreviousVisualSnapshot();
                return;
            }

            if (!hasCurrent)
            {
                ClearPreviousVisualSnapshot();
                return;
            }

            Transform root = _currentMoveInstance.VisualRoot;
            if (root == null)
            {
                ClearPreviousVisualSnapshot();
                return;
            }

            if (_currentMoveInstance is TurnMotionMove turnMotionMove)
                turnMotionMove.EvaluateVisual(moveProgress);

            bool revealBlocked = _currentMoveInstance.DelayVisualReveal
                && moveProgress < _currentMoveInstance.VisualRevealProgress;

            bool showFallback = _showPreviousVisual
                && revealBlocked
                && HasFallbackVisual();

            if (!revealBlocked)
            {
                _showPreviousVisual = false;
            }

            bool currentVisible = !revealBlocked;

            SetVisualVisible(root, currentVisible);
            HideReplacedSprite();
            SetPreviousVisualVisible(showFallback);

        }

        private static void SetVisualVisible(Transform root, bool visible)
        {
            if (root == null)
            {
                return;
            }

            SpriteRenderer[] spriteRenderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] != null)
                {
                    spriteRenderers[i].enabled = visible;
                }
            }

            Animator[] animators = root.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] != null)
                {
                    animators[i].enabled = visible;
                }
            }

            ParticleSystem[] particleSystems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                if (particleSystems[i] == null)
                {
                    continue;
                }

                var emission = particleSystems[i].emission;
                emission.enabled = visible;
            }
        }
    }

    // The authored Move-local X axis is opposite the 3D mannequin's combat-facing X.
    // Keep the conversion on the scene/runtime mount, never on the source prefab.
    public static class MoveAttachmentAxes
    {
        public static void ApplyTo(Transform mount)
        {
            if (mount == null) return;
            Vector3 scale = mount.localScale;
            scale.x = -Mathf.Abs(scale.x);
            mount.localScale = scale;
        }
    }
}
