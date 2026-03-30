using JetBrains.Annotations;
using UnityEngine;

namespace Scripts.Story
{
    public class StoryObject:ScriptableObject
    {
        public void WaitClick()
        {
            
        }

        public void WaitSecond()
        {
            
        }
        
        [CanBeNull] public string WaitAnswer(string[]  answers)
        {
            return null;
        }
        
        public bool ActionFinished()
        {
            return true;
        }
    }
}
