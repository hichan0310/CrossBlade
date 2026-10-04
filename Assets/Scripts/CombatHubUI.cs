using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Scripts
{
    public class CombatHudUI : MonoBehaviour
    {
        [Header("Battle")]
        [SerializeField] private Actor playerActor;
        [SerializeField] private Actor enemyActor;

        [Header("Center UI")]
        [SerializeField] private TMP_Text winnerText;
        [SerializeField] private TMP_Text battleStateText;
        [SerializeField] private Button restartButton;

        private bool _battleEnded;
        private ActorManager _manager;
        private GUIStyle _debugStyle;

        private void Awake()
        {
            BindButton(restartButton, OnRestart);
        }

        private void Start()
        {
            _manager = FindFirstObjectByType<ActorManager>();
            RefreshHud();
        }

        private void Update()
        {
            UpdateBattleEnded();
            RefreshHud();
        }

        private void BindButton(Button button, UnityAction action)
        {
            if (button == null || action == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private void RefreshHud()
        {
            RefreshTexts();
        }

        private void RefreshTexts()
        {
            if (winnerText != null && winnerText.transform.parent != null)
                winnerText.transform.parent.gameObject.SetActive(_battleEnded);
            if (winnerText != null)
            {
                winnerText.text = GetWinnerText();
            }

            if (battleStateText != null)
            {
                battleStateText.text = GetBattleStateText();
            }

            if (restartButton != null)
            {
                restartButton.gameObject.SetActive(_battleEnded);
            }
        }
        private void UpdateBattleEnded()
        {
            if (_battleEnded || playerActor == null || enemyActor == null)
            {
                return;
            }

            if (playerActor.Hp <= 0 || enemyActor.Hp <= 0)
            {
                _battleEnded = true;
            }
        }

        private string GetWinnerText()
        {
            if (!_battleEnded || playerActor == null || enemyActor == null)
            {
                return string.Empty;
            }

            if (playerActor.Hp <= 0 && enemyActor.Hp <= 0)
            {
                return "Draw";
            }

            if (enemyActor.Hp <= 0)
            {
                return "Player Wins";
            }

            if (playerActor.Hp <= 0)
            {
                return "Enemy Wins";
            }

            return string.Empty;
        }

        private string GetBattleStateText()
        {
            if (_battleEnded)
            {
                return "Battle Ended";
            }

            if (playerActor == null || enemyActor == null)
            {
                return "-";
            }

            if (playerActor.IsMoveRunning || enemyActor.IsMoveRunning)
            {
                return "Fighting";
            }

            if (playerActor._recoilVelocity.sqrMagnitude > 0f || enemyActor._recoilVelocity.sqrMagnitude > 0f)
            {
                return "Fighting";
            }

            if (playerActor.GettingPlan || enemyActor.GettingPlan) return "Planning";

            return "Ready";
        }

        private void OnGUI()
        {
            if (playerActor == null || enemyActor == null) return;
            if (_debugStyle == null)
            {
                _debugStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.UpperLeft };
                _debugStyle.normal.textColor = Color.white;
            }

            float width = Mathf.Min(650f, Screen.width - 24f);
            const float lineHeight = 27f;
            GUI.Box(new Rect(12f, 12f, width, 204f), GUIContent.none);
            string phase = _battleEnded ? "Battle Ended" : GetBattleStateText();
            string exchange = _manager != null ? _manager.LastExchange.ToString() : "None";
            int turn = _manager != null ? _manager.TurnIndex : 0;
            GUI.Label(new Rect(24f, 18f, width - 24f, lineHeight),
                $"Turn {turn}  |  {phase}  |  Last contact: {exchange}", _debugStyle);
            GUI.Label(new Rect(24f, 49f, width - 24f, lineHeight), DescribeStats("Player", playerActor), _debugStyle);
            GUI.Label(new Rect(24f, 76f, width - 24f, lineHeight), DescribeAction(playerActor), _debugStyle);
            GUI.Label(new Rect(24f, 108f, width - 24f, lineHeight), DescribeStats("Enemy", enemyActor), _debugStyle);
            GUI.Label(new Rect(24f, 135f, width - 24f, lineHeight), DescribeAction(enemyActor), _debugStyle);
            GUI.Label(new Rect(24f, 172f, width - 24f, lineHeight),
                "Left/Right: move or adjust speed   Space: charge", _debugStyle);
        }

        private static string DescribeStats(string name, Actor actor)
        {
            string facing = actor.FacingSign > 0 ? ">" : "<";
            return $"{name}: HP {actor.Hp}/{actor.MaxHp}  ST {actor.Stance}/{actor.MaxStance}  " +
                   $"X {actor.Position.x:0.00} Y {actor.Position.y:0.00} {facing}  Input {actor.DirectionalInput:+0;-0;0}";
        }

        private static string DescribeAction(Actor actor)
        {
            string state = actor.IsInStartup ? "startup" : actor.IsMoveRunning ? "active" :
                actor.GettingPlan ? "planning" : "waiting";
            string progress = actor.IsMoveRunning ? $"{actor.MoveProgress * 100f:0}%" : "-";
            return $"    {actor.CurrentMoveId} ({state}, {progress})  " +
                   $"Q {actor.QueueCount}/{actor.MaxQueuedMoves}  Force {actor.Current.force}";
        }

        public void OnRestart()
        {
            Scene currentScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(currentScene.name);
        }
    }
}
