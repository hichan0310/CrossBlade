using UnityEngine;

namespace Fighter.Actions
{
    public class AttackAction:Action
    {
        [Space(10)]
        [Tooltip("AttackAction")] public Collider2D attack;
    }
}