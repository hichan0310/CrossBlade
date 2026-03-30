using UnityEngine;

namespace Scripts.Story
{
    public class StoryObject:MonoBehaviour
    {
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
