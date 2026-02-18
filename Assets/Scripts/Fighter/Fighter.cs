using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;

namespace Fighter
{
    public class Fighter : MonoBehaviour
    {
        [SerializeField] private int maxHp;
        [SerializeField] private int hp;
        [SerializeField] private int maxStance;
        [SerializeField] private int stance;
        [SerializeField] private int maxStamina;
        [SerializeField] private int stamina;
        
        private List<Action> actions;

        public List<Action> getActions => this.actions;

        public void addMoveset(Moveset moveset)
        {
            foreach (var movesetAction in moveset.actions) actions.Add(movesetAction);
        }

        [CanBeNull]
        public Action popAction()
        {
            if(actions.Count == 0) return null;
            var result = actions[0];
            this.actions.RemoveAt(0);
            return result;
        }
    }
}