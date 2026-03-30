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
}
