using UnityEngine;

namespace Scripts.Story
{
    public class StoryTestStarter : MonoBehaviour
    {
        [SerializeField] private StoryManager storyManager;

        [Header("Sprites")]
        [SerializeField] private Sprite backgroundA;
        [SerializeField] private Sprite backgroundB;
        [SerializeField] private Sprite heroPortrait;
        [SerializeField] private Sprite enemyPortrait;

        private void Start()
        {
            if (storyManager == null)
            {
                return;
            }

            StoryViewChain story =
                new ClickAfter(
                    new ActionChain(sto =>
                    {
                        if (sto.DialogueRoot != null)
                        {
                            sto.DialogueRoot.SetActive(true);
                        }

                        if (sto.BackgroundImage != null)
                        {
                            sto.BackgroundImage.sprite = backgroundA;
                        }
                    }),

                    new StartSameTime(
                        new SetPortraitChain(heroPortrait, true),
                        new ActionChain(sto =>
                        {
                            if (sto.PortraitLeft != null)
                            {
                                sto.PortraitLeft.gameObject.SetActive(true);
                            }
                        })
                    ),

                    new DialogueChain("Hero", "여기가 첫 테스트 씬이군."),

                    new AutoAfter(
                        new ActionChain(sto =>
                        {
                            if (sto.PortraitRight != null)
                            {
                                sto.PortraitRight.gameObject.SetActive(true);
                                sto.PortraitRight.sprite = enemyPortrait;
                            }
                        })
                    ),

                    new DialogueChain("Enemy", "잘 왔다."),

                    new DialogueChain("Hero", "스토리 체인 테스트 시작.")
                );

            storyManager.Play(story);
        }
    }
}