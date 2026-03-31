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

        private void Awake()
        {
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

            return "Ready";
        }

        public void OnRestart()
        {
            Scene currentScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(currentScene.name);
        }
    }
}
