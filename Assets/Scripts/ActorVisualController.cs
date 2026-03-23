using System;
using UnityEngine;
using UnityEngine.UI;

namespace Scripts
{
    public class ActorVisualController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform moveMount;
        [SerializeField] private CanvasGroup uiCanvasGroup;

        [Header("Debug")]
        [SerializeField] private Move currentMoveDebug;

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

            if (!instance.name.EndsWith("__DYING", StringComparison.Ordinal))
            {
                instance.name += "__DYING";
            }

            if (_currentMoveInstance == instance)
            {
                _currentMoveInstance = null;
                currentMoveDebug = null;
            }

            Destroy(instance.gameObject);
        }

        internal void RefreshMoveVisualState(bool hasCurrent, float moveProgress)
        {
            if (_currentMoveInstance == null)
            {
                if (uiCanvasGroup != null)
                {
                    uiCanvasGroup.alpha = 1f;
                }

                return;
            }

            if (!hasCurrent)
            {
                if (uiCanvasGroup != null)
                {
                    uiCanvasGroup.alpha = 1f;
                }

                return;
            }

            Transform root = _currentMoveInstance.VisualRoot;
            if (root == null)
            {
                if (uiCanvasGroup != null)
                {
                    uiCanvasGroup.alpha = 1f;
                }

                return;
            }

            bool visible = !_currentMoveInstance.DelayVisualReveal
                || moveProgress >= _currentMoveInstance.VisualRevealProgress;

            SetVisualVisible(root, visible);

            if (uiCanvasGroup != null)
            {
                uiCanvasGroup.alpha = visible ? 1f : 0f;
            }
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