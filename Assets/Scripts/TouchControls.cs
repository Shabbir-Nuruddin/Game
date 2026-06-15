using UnityEngine;
using UnityEngine.EventSystems;

namespace TrustIssues
{
    /// <summary>Shared touch/mouse input so the game is playable on phones.</summary>
    public static class TouchInput
    {
        public static float X;       // -1 left, +1 right, 0 none
        public static bool FlyHeld;  // held while the FLY button is pressed
        static bool _jump, _fire;
        public static void QueueJump() => _jump = true;
        public static bool ConsumeJump() { if (_jump) { _jump = false; return true; } return false; }
        public static void QueueFire() => _fire = true;
        public static bool ConsumeFire() { if (_fire) { _fire = false; return true; } return false; }
        public static void Clear() { X = 0f; _jump = false; _fire = false; FlyHeld = false; }
    }

    /// <summary>An on-screen button: hold to move/fly, tap to jump/fire.</summary>
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public int dir; // -1 = left, +1 = right, 0 = jump, 2 = fire, 3 = fly (hold)

        public void OnPointerDown(PointerEventData e)
        {
            if (dir == 0) TouchInput.QueueJump();
            else if (dir == 2) TouchInput.QueueFire();
            else if (dir == 3) TouchInput.FlyHeld = true;
            else TouchInput.X = dir;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (dir == 3) TouchInput.FlyHeld = false;
            else if (dir != 0 && Mathf.Approximately(TouchInput.X, dir)) TouchInput.X = 0f;
        }
    }
}
