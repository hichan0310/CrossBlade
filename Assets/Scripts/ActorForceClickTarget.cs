using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Scripts
{
    public class ActorForceClickTarget : MonoBehaviour
    {
        [SerializeField] private Actor actor;
        [SerializeField] private Camera targetCamera;

        private Collider2D _collider;

        private void Reset()
        {
            if (actor == null)
            {
                actor = GetComponentInParent<Actor>();
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void Awake()
        {
            _collider = GetComponent<Collider2D>();

            if (actor == null)
            {
                actor = GetComponentInParent<Actor>();
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void Update()
        {
            if (actor == null || _collider == null || targetCamera == null)
            {
                return;
            }

            if (Mouse.current == null)
            {
                return;
            }

            if (!Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            Vector2 screenPos = Mouse.current.position.ReadValue();
            Vector3 world = targetCamera.ScreenToWorldPoint(screenPos);
            Vector2 point = new Vector2(world.x, world.y);

            if (!_collider.OverlapPoint(point))
            {
                return;
            }

            actor.TryIncreasePendingForce();
        }
    }
}