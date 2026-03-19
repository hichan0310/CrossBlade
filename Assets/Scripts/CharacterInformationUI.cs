// 추가한거
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;

namespace Scripts
{
    public class CharacterInformationUI : MonoBehaviour
    {
        [Serializable]
        private class ActorPanel
        {
            public TMP_Text nameText;
            public TMP_Text moveText;
            public Slider hpSlider;
            public Slider stanceSlider;
            public Slider specialSlider;
        }

        [Header("Battle")]
        [SerializeField] private ActorManager manager;

        [Header("Panels")]
        [SerializeField] private ActorPanel panelA;
        [SerializeField] private ActorPanel panelB;

        private void Reset()
        {
            if (manager == null)
            {
                manager = GetComponent<ActorManager>();
            }
        }

        private void Update()
        {
            if (manager == null)
            {
                return;
            }

            RefreshPanel(panelA, manager.actorA);
            RefreshPanel(panelB, manager.actorB);
        }

        private void RefreshPanel(ActorPanel panel, Actor actor)
        {
            if (panel == null)
            {
                return;
            }

            if (actor == null)
            {
                SetText(panel.nameText, "-");
                SetText(panel.moveText, "Move  -");
                SetSlider(panel.hpSlider, 0, 1);
                SetSlider(panel.stanceSlider, 0, 1);
                SetSlider(panel.specialSlider, 0, 1);
                return;
            }

            SetText(panel.nameText, actor.ActorId);
            SetText(panel.moveText, $"Move  {actor.CurrentMoveId}");

            SetSlider(panel.hpSlider, actor.Hp, actor.MaxHp);

            SetSlider(panel.stanceSlider, actor.Stance, actor.MaxStance);

            SetSlider(panel.specialSlider, actor.SpecialForce, actor.MaxSpecialForce);
        }

        private static void SetText(TMP_Text textComponent, string value)
        {
            if (textComponent == null)
            {
                return;
            }

            textComponent.text = value;
        }

        private static void SetSlider(Slider slider, int value, int maxValue)
        {
            if (slider == null)
            {
                return;
            }

            slider.maxValue = Mathf.Max(1, maxValue);
            slider.value = Mathf.Clamp(value, 0, Mathf.Max(1, maxValue));
        }
    }
}