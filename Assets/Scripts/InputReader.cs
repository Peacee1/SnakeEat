using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// InputReader reads from Unity's New Input System.
/// It buffers one directional input between movement ticks so that a key
/// pressed just slightly before the next tick is never lost.
/// It also prevents the snake from reversing direction (180-degree turns).
///
/// Attach to: GameManager GameObject (same object as GameManager).
/// Requires: SnakeInputActions asset generated from your .inputactions file.
/// </summary>
public class InputReader : MonoBehaviour
{
    // The buffered "next" direction. Consumed each movement tick by SnakeController.
    private Vector2Int _bufferedDirection = Vector2Int.right; // default direction
    private Vector2Int _lastConfirmedDirection = Vector2Int.right;

    // Auto-generated C# class from the .inputactions file.
    private SnakeInputActions _inputActions;

    private void Awake()
    {
        _inputActions = new SnakeInputActions();
    }

    private void OnEnable()
    {
        _inputActions.Player.Enable();
        _inputActions.Player.Move.performed += OnMovePerformed;
    }

    private void OnDisable()
    {
        _inputActions.Player.Move.performed -= OnMovePerformed;
        _inputActions.Player.Disable();
    }

    private void OnMovePerformed(InputAction.CallbackContext ctx)
    {
        Vector2 raw = ctx.ReadValue<Vector2>();

        // Convert the raw float vector to a cardinal direction.
        Vector2Int dir = Vector2Int.zero;
        if (Mathf.Abs(raw.x) > Mathf.Abs(raw.y))
            dir = raw.x > 0 ? Vector2Int.right : Vector2Int.left;
        else
            dir = raw.y > 0 ? Vector2Int.up : Vector2Int.down;

        // Prevent 180-degree reversal against the LAST CONFIRMED direction.
        if (dir == -_lastConfirmedDirection) return;

        // Buffer the valid direction. Only one direction is buffered at a time;
        // the most recent valid press wins within the same tick window.
        _bufferedDirection = dir;
    }

    /// <summary>
    /// Called by SnakeController each movement tick to consume the buffered direction.
    /// Pass in the snake's current direction so we can validate against it here too.
    /// </summary>
    public Vector2Int ConsumeDirection(Vector2Int currentDirection)
    {
        // Double-check reversal (edge case where _lastConfirmedDirection is stale).
        if (_bufferedDirection == -currentDirection)
            _bufferedDirection = currentDirection;

        _lastConfirmedDirection = _bufferedDirection;
        return _bufferedDirection;
    }

    /// <summary>Resets direction state when the game restarts.</summary>
    public void ResetDirection(Vector2Int startDirection)
    {
        _bufferedDirection = startDirection;
        _lastConfirmedDirection = startDirection;
    }

    // ── Restart input ─────────────────────────────────────────────────────────
    public bool IsRestartPressed()
    {
        return _inputActions.Player.Restart.WasPressedThisFrame();
    }

    // ── AI injection ──────────────────────────────────────────────────────────
    /// <summary>
    /// Called by AutoPlayer to override the buffered direction with the AI's choice.
    /// This runs before ConsumeDirection so the GameManager sees the AI direction.
    /// </summary>
    public void InjectDirection(Vector2Int dir)
    {
        if (dir != -_lastConfirmedDirection)
            _bufferedDirection = dir;
    }
}
