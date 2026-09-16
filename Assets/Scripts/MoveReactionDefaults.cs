using UnityEngine;

namespace Scripts
{
    [CreateAssetMenu(menuName = "CrossBlade/Move Reaction Defaults", fileName = "MoveReactionDefaults")]
    public sealed class MoveReactionDefaults : ScriptableObject
    {
        public Move guardMove;
        public Move hitMove;
    }
}
