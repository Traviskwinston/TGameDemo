using UnityEngine;

namespace GameDemo
{
    /// <summary>
    /// Single place every system reads input from, so rebinding and gamepad support
    /// only ever touch one file.
    /// </summary>
    public static class GameInput
    {
        public static KeyCode sprint = KeyCode.LeftShift;
        public static KeyCode slowWalk = KeyCode.LeftAlt;
        public static KeyCode crouch = KeyCode.C;
        public static KeyCode prone = KeyCode.Z;
        public static KeyCode light = KeyCode.F;
        public static KeyCode interact = KeyCode.E;

        public static Vector2 Move
        {
            get
            {
                Vector2 v = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                return v.sqrMagnitude > 1f ? v.normalized : v;
            }
        }

        public static Vector2 Look =>
            new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));

        public static bool SprintHeld => Input.GetKey(sprint);
        public static bool SlowWalkHeld => Input.GetKey(slowWalk);
        public static bool AimHeld => Input.GetMouseButton(1);
        public static bool FirePressed => Input.GetMouseButtonDown(0);
        public static bool JumpPressed => Input.GetButtonDown("Jump");
        public static bool CrouchPressed => Input.GetKeyDown(crouch);
        public static bool CrouchHeld => Input.GetKey(crouch);
        public static bool PronePressed => Input.GetKeyDown(prone);
        public static bool LightPressed => Input.GetKeyDown(light);
        public static bool InteractPressed => Input.GetKeyDown(interact);
    }
}
