using UnityEngine;

namespace Scripts.Story
{
    public class SetPortraitChain : StoryViewChain
    {
        private readonly Sprite sprite;
        private readonly bool left;
        private bool finished;

        public SetPortraitChain(Sprite sprite, bool left)
        {
            this.sprite = sprite;
            this.left = left;
        }

        public override void Execute(StoryObject sto)
        {
            var target = left ? sto.PortraitLeft : sto.PortraitRight;
            if (target != null)
            {
                target.sprite = sprite;
                target.gameObject.SetActive(sprite != null);
            }

            finished = true;
        }

        public override bool Finished()
        {
            return finished;
        }
    }
}