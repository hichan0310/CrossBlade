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
        private TMP_Text _statusLabel;
        private Move _shownSource;
        private int _shownQueueCount = -1;

        public override void Bind(Actor actor, PlanMaker owner)
        {
            _actor = actor;
            foreach (var label in GetComponentsInChildren<TMP_Text>(true))
                if (label.name == "TitleText") { _statusLabel = label; break; }
            Rebuild();
        }

        private void Update()
        {
            if (_actor != null && (_shownSource != _actor.PlanningMove || _shownQueueCount != _actor.QueueCount))
                Rebuild();
        }

        private void Rebuild()
        {
            if (_actor == null || buttonPrefab == null || buttonRoot == null)
            {
                return;
            }

            _shownSource = _actor.PlanningMove;
            _shownQueueCount = _actor.QueueCount;
            if (_statusLabel != null)
                _statusLabel.text = $"Queue {_actor.QueueCount}/{_actor.MaxQueuedMoves}  Posture +{_actor.NextQueueRecoveryBonus}";

            for (int i = buttonRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(buttonRoot.GetChild(i).gameObject);
            }

            if (_shownSource == null) return;
            var after = _shownSource.After;
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

                button.interactable = _actor.HasQueueSpace;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SelectMove(move));
            }
        }

        private void SelectMove(Move move)
        {
            if (_actor == null || move == null || !_actor.GettingPlan || !_actor.HasQueueSpace)
            {
                return;
            }

            if (_actor.Enqueue(move)) Rebuild();
        }
    }
}
