using UnityEngine;

namespace Scripts.Story
{
    public class DialogueChain : StoryViewChain
    {
        private readonly string speaker;
        private readonly string dialogue;
        private bool shown;
        private bool finished;

        public DialogueChain(string speaker, string dialogue)
        {
            this.speaker = speaker;
            this.dialogue = dialogue;
        }

        public override void Execute(StoryObject sto)
        {
            if (!shown)
            {
                if (sto.DialogueRoot != null)
                {
                    sto.DialogueRoot.SetActive(true);
                }

                if (sto.SpeakerText != null)
                {
                    sto.SpeakerText.text = speaker;
                }

                if (sto.DialogueText != null)
                {
                    sto.DialogueText.text = dialogue;
                }

                shown = true;
                return;
            }

            if (sto.ConsumeClick())
            {
                finished = true;
            }
        }

        public override bool Finished()
        {
            return finished;
        }
    }
}