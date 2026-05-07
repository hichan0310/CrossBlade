using System.Collections.Generic;
using UnityEngine;

namespace Scripts
{
    public class ActorQueueViewUI : MonoBehaviour
    {
        [SerializeField] private Actor actor;
        [SerializeField] private QueueSlotUI[] slots;

        private void LateUpdate()
        {
            if (actor == null || slots == null)
            {
                return;
            }

            List<Move> moves = actor.GetQueuedMoveSnapshot();

            for (int i = 0; i < slots.Length; i++)
            {
                Move move = i < moves.Count ? moves[i] : null;
                slots[i].SetMove(move);
            }
        }
    }
}