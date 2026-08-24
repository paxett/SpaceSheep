using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drives a Rigidbody2D using the existing Input System "Move" action.
/// Gameplay stays on the XY plane; Z is never changed by this component.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float moveSmoothing = 0.08f;

    [Header("Level bounds")]
    [SerializeField] private float minX = -8.0f;
    [SerializeField] private float maxX = 8.0f;
    [SerializeField] private float minY = -4.5f;
    [SerializeField] private float maxY = 4.5f;

    private Rigidbody2D _rigidbody;
    private Vector2 _moveInput;
    private Vector2 _velocityRef;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody2D>();
    }

    /// <summary>
    /// Called automatically by the PlayerInput component (behavior "Send Messages")
    /// when the "Move" action (Player map) sends a value.
    /// </summary>
    public void OnMove(InputValue value)
    {
        _moveInput = value.Get<Vector2>();
    }

    private void FixedUpdate()
    {
        Vector2 targetVelocity = _moveInput * moveSpeed;
        _rigidbody.linearVelocity = Vector2.SmoothDamp(_rigidbody.linearVelocity, targetVelocity, ref _velocityRef, moveSmoothing);
    }
}