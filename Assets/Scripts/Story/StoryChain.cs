namespace Scripts.Story
{
    public abstract class StoryViewChain
    {
        public abstract void Exxecute(StoryObject sto);
    }

    public class AutoAfter : StoryViewChain
    {
        private StoryViewChain chain1;
        private StoryViewChain chain2;
        
        public AutoAfter(StoryViewChain chain1, StoryViewChain chain2)
        {
            this.chain1 = chain1;
            this.chain2 = chain2;
        }

        public override void Exxecute(StoryObject sto)
        {
            chain1.Exxecute(sto);
        }
    }
}
