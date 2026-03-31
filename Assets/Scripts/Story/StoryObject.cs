using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Scripts.Story
{
    public class StoryObject : MonoBehaviour
    {
        [Header("Dialogue UI")]
        [SerializeField] private GameObject dialogueRoot;
        [SerializeField] private TMP_Text speakerText;
        [SerializeField] private TMP_Text dialogueText;

        [Header("Images")]
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image portraitLeft;
        [SerializeField] private Image portraitRight;

        private bool clickedThisFrame;

        public GameObject DialogueRoot => dialogueRoot;
        public TMP_Text SpeakerText => speakerText;
        public TMP_Text DialogueText => dialogueText;
        public Image BackgroundImage => backgroundImage;
        public Image PortraitLeft => portraitLeft;
        public Image PortraitRight => portraitRight;

        public void NotifyClick()
        {
            clickedThisFrame = true;
        }

        public bool ConsumeClick()
        {
            if (!clickedThisFrame)
            {
                return false;
            }

            clickedThisFrame = false;
            return true;
        }

        public class StoryRunner : MonoBehaviour
        {
            public void RunStory(StoryViewChain chain, StoryObject sto)
            {
                StartCoroutine(Run(chain, sto));
            }

            private System.Collections.IEnumerator Run(StoryViewChain chain, StoryObject sto)
            {
                chain.Execute(sto);
                yield return new WaitUntil(() => chain.Finished());
            }
        }
    }
}