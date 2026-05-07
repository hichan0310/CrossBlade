using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Scripts
{
    public class QueueSlotUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;

        public void SetMove(Move move)
        {
            bool visible = move != null;
            gameObject.SetActive(visible);

            if (!visible)
            {
                return;
            }

            if (label != null)
            {
                label.text = move.MoveId;
            }
        }
    }
}