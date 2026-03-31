using UnityEngine;

namespace Scripts.Story
{
    public class WaitClickChain : StoryViewChain
    {
        private bool started;
        private bool finished;

        public override void Execute(StoryObject sto)
        {
            if (started)
            {
                if (sto.ConsumeClick())
                {
                    finished = true;
                }

                return;
            }

            started = true;
        }

        public override bool Finished()
        {
            return finished;
        }
    }
}