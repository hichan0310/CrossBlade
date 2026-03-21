using System;
using UnityEngine;

namespace Scripts
{
    public class MoveEffects:MonoBehaviour
    {
        [SerializeField] public bool noParents;
        [SerializeField] public float destroyTimer;

        private void OnEnable()
        {
            Debug.Log("OnEnable");
            if (noParents)
                this.transform.SetParent(null);
            Destroy(gameObject, destroyTimer);
        }
    }
}
