using UnityEngine;
using UnityEngine.EventSystems;

namespace TrustIssues
{
    /// <summary>Shared touch/mouse input so the game is playable on phones.</summary>
    public static class TouchInput
    {
        public static float X;       // -1 left, +1 right, 0 none
        static bool _jump;
        public static void QueueJump() => _jump = true;
        public static bool ConsumeJump() { if (_jump) { _jump = false; return true; } return false; }
        public static void Clear() { X = 0f; _jump = false; }
    }

    /// <summary>An on-screen button: hold to move, tap to jump.</summary>
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public int dir; // -1 = left, +1 = right, 0 = jump

        public void OnPointerDown(PointerEventData e)
        {
            if (dir == 0) TouchInput.QueueJump();
            else TouchInput.X = dir;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (dir != 0 && Mathf.Approximately(TouchInput.X, dir)) TouchInput.X = 0f;
        }
    }
}
