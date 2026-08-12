using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 12f;
    public float rotationSpeed = 0.18f;
    public float zoomSpeed = 2f;
    private float yaw;
    private float pitch = 12f;

    private void LateUpdate()
    {
        if (target == null) return;
        Mouse mouse = Mouse.current;
        bool pointerOverUI = EventSystem.current != null &&
                     EventSystem.current.IsPointerOverGameObject();

        if (mouse != null && mouse.leftButton.isPressed && !pointerOverUI)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * rotationSpeed;
            pitch = Mathf.Clamp(pitch - delta.y * rotationSpeed, -80f, 80f);
        }
        if (mouse != null) distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * 0.01f * zoomSpeed, 4f, 35f);
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
        transform.position = target.position + rotation * new Vector3(0, 0, -distance);
        transform.LookAt(target.position);
    }
}
