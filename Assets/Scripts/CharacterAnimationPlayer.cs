using UnityEngine;

namespace Scripts
{
    /// <summary>Visual-only clip sampling. Combat supplies time; no independent animation clock.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAnimationPlayer : MonoBehaviour
    {
        [SerializeField] private GameObject animationRoot;
        [Tooltip("Frame zero is the fallback for idle and Moves without a character clip.")]
        [SerializeField] private AnimationClip defaultPose;
        [Tooltip("Optional locomotion bone: hold its horizontal translation so gameplay movement stays authoritative.")]
        [SerializeField] private Transform motionRoot;
        [SerializeField] private float rightFacingYaw = 90f;

        private Transform[] _transforms;
        private Vector3[] _positions;
        private Quaternion[] _rotations;
        private Vector3[] _scales;
        private Vector3 _motionOrigin;
        private Move _move;
        private bool _initialized;

        public float SampleTime { get; private set; }
        public AnimationClip SampledClip { get; private set; }
        public GameObject AnimationRoot => animationRoot;

        private void Awake() => Initialize();

        internal void Configure(GameObject root, string rootPath, float yaw, AnimationClip pose)
        {
            animationRoot = root;
            motionRoot = string.IsNullOrEmpty(rootPath) ? null : root.transform.Find(rootPath);
            rightFacingYaw = yaw;
            defaultPose = pose;
            _initialized = false;
            _move = null;
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized || animationRoot == null) return;
            // Sampling is the sole writer. Imported Animator/controller time must not advance.
            foreach (Animator animator in animationRoot.GetComponentsInChildren<Animator>(true))
            {
                animator.applyRootMotion = false;
                animator.enabled = false;
            }
            _transforms = animationRoot.GetComponentsInChildren<Transform>(true);
            _positions = new Vector3[_transforms.Length];
            _rotations = new Quaternion[_transforms.Length];
            _scales = new Vector3[_transforms.Length];
            if (defaultPose != null) defaultPose.SampleAnimation(animationRoot, 0f);
            for (int i = 0; i < _transforms.Length; i++)
            {
                _positions[i] = _transforms[i].localPosition;
                _rotations[i] = _transforms[i].localRotation;
                _scales[i] = _transforms[i].localScale;
            }
            if (motionRoot != null) _motionOrigin = animationRoot.transform.InverseTransformPoint(motionRoot.position);
            _initialized = true;
        }

        internal void Evaluate(Move move, float progress, int facingSign)
        {
            Initialize();
            if (!_initialized) return;
            UpdateFacing(facingSign);
            AnimationClip clip = move != null ? move.CharacterAnimation : null;
            if (_move != move || SampledClip != (clip != null ? clip : defaultPose))
                RestorePose(); // Also clears channels absent from a subsequent clip.
            _move = move;
            SampledClip = clip != null ? clip : defaultPose;
            SampleTime = clip != null ? Mathf.Clamp01(progress) * clip.length : 0f;
            if (SampledClip != null) SampledClip.SampleAnimation(animationRoot, SampleTime);
            if (motionRoot != null)
            {
                Vector3 p = animationRoot.transform.InverseTransformPoint(motionRoot.position);
                p.x = _motionOrigin.x;
                p.z = _motionOrigin.z;
                motionRoot.position = animationRoot.transform.TransformPoint(p);
            }
        }

        internal void UpdateFacing(int sign)
        {
            // This root is a sibling of the 2D FacingRoot, never below its negative scale.
            transform.localRotation = Quaternion.Euler(0f, rightFacingYaw + (sign < 0 ? 180f : 0f), 0f);
        }

        private void RestorePose()
        {
            for (int i = 0; i < _transforms.Length; i++)
            {
                if (_transforms[i] == null) continue;
                _transforms[i].localPosition = _positions[i];
                _transforms[i].localRotation = _rotations[i];
                _transforms[i].localScale = _scales[i];
            }
        }

        private void OnDisable()
        {
            if (_initialized) RestorePose();
            _move = null;
            SampledClip = null;
            SampleTime = 0f;
        }
    }
}
