using System;
using UnityEngine;
using UnityEngine.UI;

namespace Scripts
{
    public class ActorForceUI : MonoBehaviour
    {
        [SerializeField] private Actor actor;
        [SerializeField] private SpriteRenderer[] pips = new SpriteRenderer[5];
        [SerializeField] private Color activeColor = Color.white;
        [SerializeField] private Color inactiveColor = new Color(1f, 1f, 1f, 0.2f);
        

        private void Reset()
        {
            if (actor == null)
            {
                actor = GetComponentInParent<Actor>();
            }
        }

        private void LateUpdate()
        {
            if (actor == null || pips == null)
            {
                return;
            }

            int force = Mathf.Clamp(actor.PendingForce, 1, 5);

            for (int i = 0; i < pips.Length; i++)
            {
                if (pips[i] == null)
                {
                    continue;
                }

                pips[i].color = i < force ? activeColor : inactiveColor;
            }
        }
    }
}
