using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Reads the default keyboard controls using Unity's Input System.</summary>
[DefaultExecutionOrder(-100)]
public sealed class VehicleInputHandler : MonoBehaviour
{
    public struct VehicleInput
    {
        public float Steering;
        public bool Accelerate;
        public bool BrakeOrReverse;
        public bool Handbrake;
        public bool ResetPressed;
    }

    public VehicleInput Current { get; private set; }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            Current = default;
            return;
        }

        float steering = 0f;
        if (keyboard.aKey.isPressed) steering -= 1f;
        if (keyboard.dKey.isPressed) steering += 1f;

        Current = new VehicleInput
        {
            Steering = steering,
            Accelerate = keyboard.wKey.isPressed,
            BrakeOrReverse = keyboard.sKey.isPressed,
            Handbrake = keyboard.spaceKey.isPressed,
            ResetPressed = keyboard.rKey.wasPressedThisFrame
        };
    }
}
