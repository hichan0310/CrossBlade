using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Scripts
{
    public class PlayerPlanUI : PlanInputUIBinder
    {
        [SerializeField] private Button buttonPrefab;
        [SerializeField] private Transform buttonRoot;

        private Actor _actor;

        public override void Bind(Actor actor, PlanMaker owner)
        {
            _actor = actor;
            Rebuild();
        }

        private void Rebuild()
        {
            if (_actor == null || buttonPrefab == null || buttonRoot == null)
            {
                return;
            }

            Move baseMove = _actor.PlanningBaseMove;
            if (baseMove == null)
            {
                return;
            }

            var after = baseMove.After;
            if (after == null || after.Count == 0)
            {
                return;
            }

            for (int i = buttonRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(buttonRoot.GetChild(i).gameObject);
            }

            for (int i = 0; i < after.Count; i++)
            {
                Move move = after[i];
                if (move == null)
                {
                    continue;
                }

                Button button = Instantiate(buttonPrefab, buttonRoot);
                TMP_Text label = button.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = move.MoveId;
                }

                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SelectMove(move));
            }
        }

        private void SelectMove(Move move)
        {
            if (_actor == null || move == null)
            {
                return;
            }

            _actor.SubmitPlannedMove(move);
        }
    }
}