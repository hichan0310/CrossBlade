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
        [SerializeField] private ActorManager manager;
        [SerializeField] private Actor playerActor;
        [SerializeField] private Actor enemyActor;

        [Header("Player Moves")]
        [SerializeField] private Move playerAttack;
        [SerializeField] private Move player1;
        [SerializeField] private Move player2;
        [SerializeField] private Move player3;

        [Header("Enemy Moves")]
        [SerializeField] private Move enemy1;
        [SerializeField] private Move enemy2;
        [SerializeField] private Move enemy3;
        [SerializeField] private Move enemy4;

        [Header("Player Buttons")]
        [SerializeField] private Button playerAttackButton;
        [SerializeField] private Button playerButton1;
        [SerializeField] private Button playerButton2;
        [SerializeField] private Button playerButton3;

        [Header("Enemy Buttons")]
        [SerializeField] private Button enemyButton1;
        [SerializeField] private Button enemyButton2;
        [SerializeField] private Button enemyButton3;
        [SerializeField] private Button enemyButton4;

        [Header("Center UI")]
        [SerializeField] private TMP_Text winnerText;
        [SerializeField] private TMP_Text battleStateText;
        [SerializeField] private Button restartButton;

        private bool _battleEnded;

        private void Reset()
        {
            if (manager == null)
            {
                manager = FindAnyObjectByType<ActorManager>();
            }

            if (manager != null)
            {
                if (playerActor == null)
                {
                    playerActor = manager.actorA;
                }

                if (enemyActor == null)
                {
                    enemyActor = manager.actorB;
                }
            }
        }

        private void Awake()
        {
            if (manager == null)
            {
                manager = FindAnyObjectByType<ActorManager>();
            }

            if (manager != null)
            {
                if (playerActor == null)
                {
                    playerActor = manager.actorA;
                }

                if (enemyActor == null)
                {
                    enemyActor = manager.actorB;
                }
            }

            BindButton(playerAttackButton, OnPlayerAttack);
            BindButton(restartButton, OnRestart);
        }

        private void Start()
        {
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
            RefreshButtons();
        }

        private void RefreshTexts()
        {
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

        private void RefreshButtons()
        {
            bool playerAlive = playerActor != null && playerActor.Hp > 0;
            bool canPlayerInput = !_battleEnded && playerAlive;

            SetButtonState(playerAttackButton, canPlayerInput && playerAttack != null);
        }

        private void SetButtonState(Button button, bool value)
        {
            if (button == null)
            {
                return;
            }

            button.interactable = value;
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

            return "Ready";
        }

        public void OnPlayerAttack()
        {
            EnqueueMove(playerActor, playerAttack);
        }

        private void EnqueueMove(Actor actor, Move move)
        {
            if (_battleEnded || actor == null || move == null)
            {
                return;
            }

            actor.Enqueue(move);
        }

        public void OnRestart()
        {
            Scene currentScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(currentScene.name);
        }
    }
}
