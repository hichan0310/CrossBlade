using System.Collections.Generic;
using UnityEngine;

namespace Fighter
{
    public class Moveset:ScriptableObject
    {
        public List<Action> actions;
        [SerializeField] public int[] stanceUsage;
    }
}