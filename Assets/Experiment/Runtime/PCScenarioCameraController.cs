using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Free-look camera used only by PCScenarioTestScene.</summary>
public sealed class PCScenarioCameraController : MonoBehaviour
{
    [Min(0.01f)] public float lookSensitivity = 0.12f;
    [Min(0.01f)] public float moveSpeed = 0.8f;
    [Min(1f)] public float fastMultiplier = 3f;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private float yaw;
    private float pitch;

    private void Awake()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        ReadEuler();
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null || Mouse.current == null) return;

        if (Mouse.current.rightButton.isPressed)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            yaw += delta.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -85f, 85f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        Vector3 move = Vector3.zero;
        if (Keyboard.current.wKey.isPressed) move += Vector3.forward;
        if (Keyboard.current.sKey.isPressed) move += Vector3.back;
        if (Keyboard.current.aKey.isPressed) move += Vector3.left;
        if (Keyboard.current.dKey.isPressed) move += Vector3.right;
        if (Keyboard.current.eKey.isPressed) move += Vector3.up;
        if (Keyboard.current.qKey.isPressed) move += Vector3.down;

        float speed = moveSpeed * (Keyboard.current.leftShiftKey.isPressed ? fastMultiplier : 1f);
        transform.position += transform.TransformDirection(move.normalized) * speed * Time.unscaledDeltaTime;

        if (Keyboard.current.rKey.wasPressedThisFrame) ResetPose();
        if (Keyboard.current.digit1Key.wasPressedThisFrame) SetLook(0f, 0f);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) SetLook(0f, -55f);
        if (Keyboard.current.digit3Key.wasPressedThisFrame) SetLook(0f, 55f);
        if (Keyboard.current.digit4Key.wasPressedThisFrame) SetLook(-30f, 0f);
        if (Keyboard.current.digit5Key.wasPressedThisFrame) SetLook(30f, 0f);
        if (Keyboard.current.digit6Key.wasPressedThisFrame) SetLook(0f, 180f);
#endif
    }

    public void ResetPose()
    {
        transform.SetPositionAndRotation(initialPosition, initialRotation);
        ReadEuler();
    }

    private void SetLook(float nextPitch, float nextYaw)
    {
        pitch = nextPitch;
        yaw = nextYaw;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void ReadEuler()
    {
        Vector3 euler = transform.eulerAngles;
        pitch = Mathf.DeltaAngle(0f, euler.x);
        yaw = Mathf.DeltaAngle(0f, euler.y);
    }
}
