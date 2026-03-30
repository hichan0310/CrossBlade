using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Scripts.Story
{
    public abstract class StoryViewChain
    {
        // 클릭할 때마다 이전 StoryViewChain이 끝났는지 확인해서 Execute 호출함
        public abstract void Execute(StoryObject sto);
        public abstract bool Finished();
    }

    public class AutoAfter : StoryViewChain
    {
        private List<StoryViewChain> chains;
        private int step = 0;
        private bool started = false;
        private bool finished = false;

        public AutoAfter(params StoryViewChain[] chains)
        {
            this.chains = new List<StoryViewChain>(chains);
        }

        public override void Execute(StoryObject sto)
        {
            if (started) return;
            started = true;

            if (chains.Count == 0)
            {
                finished = true;
                return;
            }

            sto.StartCoroutine(Run(sto));
        }

        private IEnumerator Run(StoryObject sto)
        {
            while (step < chains.Count)
            {
                StoryViewChain current = chains[step];
                current.Execute(sto);

                yield return new WaitUntil(() => current.Finished());

                step++;
            }

            finished = true;
        }

        public override bool Finished()
        {
            return finished;
        }
    }

    public class StartSameTime : StoryViewChain
    {
        private List<StoryViewChain> chains;

        public StartSameTime(params StoryViewChain[] chains)
        {
            this.chains = new List<StoryViewChain>(chains);
        }

        public override void Execute(StoryObject sto)
        {
            foreach (StoryViewChain chain in chains)
                chain.Execute(sto);
        }

        public override bool Finished()
        {
            foreach (StoryViewChain chain in chains)
            {
                if (!chain.Finished())
                    return false;
            }

            return true;
        }
    }

    public class ClickAfter : StoryViewChain
    {
        private List<StoryViewChain> chains;
        private int step = 0;

        public ClickAfter(params StoryViewChain[] chains)
        {
            this.chains = new List<StoryViewChain>(chains);
        }

        public override void Execute(StoryObject sto)
        {
            if (step >= chains.Count) return;

            if (step == 0 || chains[step - 1].Finished())
            {
                chains[step].Execute(sto);
                step++;
            }
        }

        public override bool Finished()
        {
            if (chains.Count == 0) return true;
            return step >= chains.Count && chains[chains.Count - 1].Finished();
        }
    }

    public class Delay : StoryViewChain
    {
        private StoryViewChain chain;
        private float delay;
        private bool finished = false;
        private bool started = false;

        public Delay(StoryViewChain chain, float delay)
        {
            this.chain = chain;
            this.delay = delay;
        }

        public override void Execute(StoryObject sto)
        {
            if (started) return;
            started = true;
            
            sto.StartCoroutine(Run(sto));
        }

        public IEnumerator Run(StoryObject sto)
        {
            yield return new WaitForSeconds(delay);

            chain.Execute(sto);
            yield return new WaitUntil(() => chain.Finished());

            finished = true;
        }

        public override bool Finished()
        {
            return finished;
        }
    }
}
