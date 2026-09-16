using UnityEngine;

namespace Scripts.TurnMotion
{
    public class TurnMotionPart : MonoBehaviour
    {
        [SerializeField] private int partIndex;
        [SerializeField] private string partLabel;
        [SerializeField] private float sourceStartTime;
        [SerializeField] private float sourceEndTime;
        [SerializeField] private float turnStartTime;
        [SerializeField] private float turnEndTime;
        [SerializeField] private Transform vfxAnchor;

        private bool _wasActive;

        public int PartIndex => partIndex;
        public string PartLabel => partLabel;
        public float TurnStartTime => turnStartTime;
        public float TurnEndTime => turnEndTime;
        public Transform VfxAnchor => vfxAnchor;

        public void Configure(int index, string label, TurnMotionSegment segment, Transform anchor)
        {
            partIndex = index;
            partLabel = label;
            sourceStartTime = segment.sourceStartTime;
            sourceEndTime = segment.sourceEndTime;
            turnStartTime = segment.turnStartTime;
            turnEndTime = segment.turnEndTime;
            vfxAnchor = anchor;
        }

        public void Evaluate(float turnTime, bool simulateVfx)
        {
            // Half-open ranges prevent adjacent VFX parts from firing together on a boundary.
            bool active = turnTime >= turnStartTime && turnTime < turnEndTime;
            if (vfxAnchor == null)
            {
                _wasActive = active;
                return;
            }

            if (vfxAnchor.gameObject.activeSelf != active)
                vfxAnchor.gameObject.SetActive(active);

            if (active && !_wasActive && Application.isPlaying)
            {
                foreach (ParticleSystem particle in vfxAnchor.GetComponentsInChildren<ParticleSystem>(true))
                    particle.Play(true);
            }

            if (active && simulateVfx && !Application.isPlaying)
            {
                float localTime = Mathf.Max(0f, turnTime - turnStartTime);
                foreach (ParticleSystem particle in vfxAnchor.GetComponentsInChildren<ParticleSystem>(true))
                    particle.Simulate(localTime, true, true, true);
            }
            _wasActive = active;
        }

        public void ResetPart()
        {
            _wasActive = false;
            if (vfxAnchor == null) return;
            foreach (ParticleSystem particle in vfxAnchor.GetComponentsInChildren<ParticleSystem>(true))
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            vfxAnchor.gameObject.SetActive(false);
        }
    }
}
