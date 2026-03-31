using System;
using UnityEngine;

namespace Scripts.Story
{
    public class ActionChain : StoryViewChain
    {
        private readonly Action<StoryObject> action;
        private bool finished;

        public ActionChain(Action<StoryObject> action)
        {
            this.action = action;
        }

        public override void Execute(StoryObject sto)
        {
            if (finished)
            {
                return;
            }

            action?.Invoke(sto);
            finished = true;
        }

        public override bool Finished()
        {
            return finished;
        }
    }
}