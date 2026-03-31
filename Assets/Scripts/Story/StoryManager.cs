using UnityEngine;
using UnityEngine.InputSystem;

namespace Scripts.Story
{
    public class StoryManager : MonoBehaviour
    {
        [SerializeField] private StoryObject storyObject;

        private StoryViewChain currentChain;
        private bool running;

        public bool IsRunning => running;

        public void Play(StoryViewChain chain)
        {
            if (storyObject == null || chain == null)
            {
                return;
            }

            currentChain = chain;
            running = true;

            currentChain.Execute(storyObject);

            if (currentChain.Finished())
            {
                running = false;
                currentChain = null;
            }
        }

        private void Update()
        {
            if (!running || currentChain == null || storyObject == null)
            {
                return;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                storyObject.NotifyClick();
                currentChain.Execute(storyObject);

                if (currentChain.Finished())
                {
                    running = false;
                    currentChain = null;
                }
            }
        }
    }
}