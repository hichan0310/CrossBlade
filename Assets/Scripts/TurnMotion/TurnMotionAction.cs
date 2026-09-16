using System.Collections.Generic;
using UnityEngine;

namespace Scripts.TurnMotion
{
    public class TurnMotionAction : MonoBehaviour
    {
        [SerializeField] private TurnMotionDefinition definition;
        [SerializeField] private AnimationClip motionClip;
        [SerializeField, Min(0.01f)] private float turnDuration = 0.5f;
        [SerializeField] private GameObject animationRoot;
        [SerializeField] private List<TurnMotionPart> parts = new List<TurnMotionPart>();
        [Header("Runtime Preview")]
        [SerializeField] private bool playOnEnable;
        [SerializeField] private bool loop;
        [SerializeField] private bool playVfx = true;

        private float _time;
        private bool _playing;

        public float Time => _time;
        public float TurnDuration => turnDuration;
        public AnimationClip MotionClip => motionClip;
        public IReadOnlyList<TurnMotionPart> Parts => parts;

        public void Configure(
            TurnMotionDefinition sourceDefinition, AnimationClip clip, float duration,
            GameObject characterRoot, IEnumerable<TurnMotionPart> actionParts)
        {
            definition = sourceDefinition;
            motionClip = clip;
            turnDuration = Mathf.Max(0.01f, duration);
            animationRoot = characterRoot;
            parts = new List<TurnMotionPart>(actionParts);
        }

        private void OnEnable()
        {
            if (Application.isPlaying && playOnEnable)
                PlayFromStart();
        }

        private void OnDisable()
        {
            _playing = false;
            ResetParts();
        }

        private void Update()
        {
            if (!_playing) return;
            SetTime(_time + UnityEngine.Time.deltaTime, playVfx);
            if (_time < turnDuration) return;
            if (loop)
                SetTime(0f, playVfx);
            else
                _playing = false;
        }

        public void PlayFromStart()
        {
            ResetParts();
            _time = 0f;
            _playing = true;
            SetTime(0f, playVfx);
        }

        public void Pause()
        {
            _playing = false;
        }

        public void SetNormalizedTime(float normalizedTime, bool evaluateVfx = true)
        {
            SetTime(Mathf.Clamp01(normalizedTime) * turnDuration, evaluateVfx);
        }

        public void SetTime(float seconds, bool evaluateVfx = true)
        {
            _time = Mathf.Clamp(seconds, 0f, turnDuration);
            if (motionClip != null && animationRoot != null)
                motionClip.SampleAnimation(animationRoot, Mathf.Min(_time, motionClip.length));
            foreach (TurnMotionPart part in parts)
                if (part != null) part.Evaluate(_time, evaluateVfx);
        }

        private void ResetParts()
        {
            foreach (TurnMotionPart part in parts)
                if (part != null) part.ResetPart();
        }
    }
}
