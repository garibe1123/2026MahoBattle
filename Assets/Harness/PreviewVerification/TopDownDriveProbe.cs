using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class TopDownDriveProbe : MonoBehaviour
{
    public float speed = 5f;
    public float acceleration = 12f;
    public float brake = 6f;
    public float drag = 1f;
    public float steer = 2.4f;
    public float grip = 6f;
    public float brakeGrip = 1.2f;

    private Vector2 velocity;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private float throttleInput;
    private float steeringInput;
    private bool braking;
    private bool resetRequested;

    private void Awake()
    {
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    private void OnEnable()
    {
        ClearMotion();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        throttleInput = 0f;
        steeringInput = 0f;
        braking = false;
        if (keyboard == null)
            return;

        throttleInput = ((keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) ? 1f : 0f)
            - ((keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) ? 1f : 0f);
        steeringInput = ((keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) ? 1f : 0f)
            - ((keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) ? 1f : 0f);
        braking = keyboard.spaceKey.isPressed;
        resetRequested |= keyboard.rKey.wasPressedThisFrame;
    }

    private void FixedUpdate()
    {
        if (resetRequested)
        {
            transform.SetPositionAndRotation(startPosition, startRotation);
            ClearMotion();
            return;
        }

        float step = Time.fixedDeltaTime;
        Vector2 forward = transform.up;
        float forwardSpeed = Vector2.Dot(velocity, forward);
        float speedLimit = Mathf.Max(0f, speed);
        float steeringWeight = speedLimit > 0f ? Mathf.Clamp(forwardSpeed / speedLimit, -1f, 1f) : 0f;
        transform.rotation = Quaternion.AngleAxis(
            -steeringInput * steer * steeringWeight * Mathf.Rad2Deg * step, Vector3.forward) * transform.rotation;

        forward = transform.up;
        Vector2 right = transform.right;
        forwardSpeed = Vector2.Dot(velocity, forward);
        float lateralSpeed = Vector2.Dot(velocity, right);
        if (braking)
        {
            forwardSpeed = Mathf.MoveTowards(forwardSpeed, 0f, Mathf.Max(0f, brake) * step);
        }
        else
        {
            forwardSpeed += throttleInput * Mathf.Max(0f, acceleration) * step;
        }

        forwardSpeed = Mathf.MoveTowards(forwardSpeed, 0f, Mathf.Max(0f, drag) * step);
        forwardSpeed = Mathf.Clamp(forwardSpeed, -speedLimit, speedLimit);
        float lateralGrip = Mathf.Max(0f, braking ? brakeGrip : grip);
        lateralSpeed *= Mathf.Exp(-lateralGrip * step);
        velocity = forward * forwardSpeed + right * lateralSpeed;
        Vector3 position = transform.position;
        position.x += velocity.x * step;
        position.y += velocity.y * step;
        transform.position = position;
    }

    private void ClearMotion()
    {
        velocity = Vector2.zero;
        throttleInput = 0f;
        steeringInput = 0f;
        braking = false;
        resetRequested = false;
    }
}
