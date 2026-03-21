using System;
using UnityEditor;
using UnityEngine;

namespace Scripts
{
    public class MoveEffects:MonoBehaviour
    {
        [SerializeField] public bool noParents;
        [SerializeField] public float destroyTimer;

        private void OnEnable()
        {
            if (noParents)
                this.transform.SetParent(null);
            var root = this.transform;
            var visible = true;
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
            Destroy(gameObject, destroyTimer);
        }
    }
}
