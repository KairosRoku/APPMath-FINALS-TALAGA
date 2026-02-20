using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Reads keyboard/gamepad input via Unity's Input System and forwards it
/// to GPUInstanceRenderer each frame. Zero physics code here.
/// </summary>
public class PlayerInputReader : MonoBehaviour
{
    GPUInstanceRenderer _renderer;

    InputAction _moveAction;
    InputAction _jumpAction;

    void Awake()
    {
        _renderer = GetComponent<GPUInstanceRenderer>();

        // 1D Axis composite: A/← = -1, D/→ = +1
        _moveAction = new InputAction("MoveAxis", type: InputActionType.Value);
        _moveAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/a")
            .With("Negative", "<Keyboard>/leftArrow")
            .With("Positive", "<Keyboard>/d")
            .With("Positive", "<Keyboard>/rightArrow");

        _jumpAction = new InputAction("Jump", type: InputActionType.Button);
        _jumpAction.AddBinding("<Keyboard>/space");
        _jumpAction.AddBinding("<Keyboard>/w");
        _jumpAction.AddBinding("<Keyboard>/upArrow");
        _jumpAction.AddBinding("<Gamepad>/buttonSouth");

        _moveAction.Enable();
        _jumpAction.Enable();
    }

    void OnDestroy()
    {
        _moveAction?.Dispose();
        _jumpAction?.Dispose();
    }

    void Update()
    {
        if (_renderer == null) return;

        float moveX    = _moveAction.ReadValue<float>();
        bool  jumpDown = _jumpAction.WasPressedThisFrame();

        _renderer.SetInput(new GPUInstanceRenderer.float2Input
        {
            moveX       = moveX,
            jumpPressed = jumpDown ? 1f : 0f
        });
    }
}
