using UnityEngine;

namespace Scripts
{
    /// <summary>
    /// Scene-owned editing copy of a Move prefab. The real combat Move is still
    /// instantiated by ActorVisualController; this copy never enters combat.
    /// </summary>
    public sealed class SceneMoveAuthoring : MonoBehaviour
    {
        [SerializeField] private Move sourceMove;
        [SerializeField] private Move sceneMove;

        public Move SourceMove => sourceMove;
        public Move SceneMove => sceneMove;

        private void Awake()
        {
            // Keep the editable colliders visible in the Scene before Play,
            // but remove them from physics before the first combat simulation.
            if (Application.isPlaying) gameObject.SetActive(false);
        }
    }
}
