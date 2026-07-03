using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// The 2.5D camera rig. GameRoot describes WHAT to frame (centre + half-height)
    /// via SetFrame; the rig decides HOW: flat mode reproduces the classic
    /// orthographic camera exactly, depth mode (opt_25d, default ON) switches to a
    /// 40° perspective camera placed at the distance that keeps the Z=0 gameplay
    /// plane the same on-screen size — so the switch never changes the platforming,
    /// only adds real depth (parallax planes, platform extrusions, camera sway).
    ///
    /// Structure: this component lives on a parent object; the camera is its child.
    /// The rig moves the PARENT, Juice.Shake keeps perturbing the camera's LOCAL
    /// position — which also fixes shake being stomped by the old code writing
    /// camera.position every LateUpdate (boss-fight shakes were invisible).
    ///
    /// Cinemtic punch-in (boss summon/defeat, player death) composes on top via
    /// SetPunch; a velocity-based yaw sway adds the depth cue while running.
    /// </summary>
    [DefaultExecutionOrder(100)]   // after GameRoot.LateUpdate has set the frame
    public class CameraRig : MonoBehaviour
    {
        public const float Fov = 40f;
        public static float HalfTan => Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
        /// <summary>Camera distance that shows `half` world-units of half-height at Z=0.</summary>
        public static float DistanceFor(float half) => half / HalfTan;

        Camera _cam;
        float _x = -1.5f, _y = -1.2f, _half = 5.6f;   // the logical frame
        Vector2 _focus; float _punch;                  // cinematic punch toward a point
        float _swayY, _prevX;

        public float FrameX => _x;

        /// <summary>Wrap an existing camera: rig parent adopts its position, camera local zeroes.</summary>
        public static CameraRig Attach(Camera cam)
        {
            var root = new GameObject("CamRig");
            root.transform.position = cam.transform.position;
            cam.transform.SetParent(root.transform, true);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
            var rig = root.AddComponent<CameraRig>();
            rig._cam = cam;
            rig._prevX = root.transform.position.x;
            return rig;
        }

        public void SetFrame(float x, float y, float halfHeight)
        {
            _x = x; _y = y; _half = Mathf.Max(0.5f, halfHeight);
        }

        /// <summary>Cinematic punch: pull the frame toward `focus`, tighten by up to ~45%.</summary>
        public void SetPunch(Vector2 focus, float amount)
        {
            _focus = focus;
            _punch = Mathf.Clamp01(amount);
        }

        void LateUpdate()
        {
            bool depth = GameRoot.Depth25;   // read live so the settings toggle applies instantly

            float half = _half * (1f - _punch * 0.45f);
            Vector2 c = Vector2.Lerp(new Vector2(_x, _y), _focus, _punch * 0.6f);

            // Yaw sway from horizontal camera motion — a subtle, free depth cue.
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            float vx = (_x - _prevX) / dt; _prevX = _x;
            float targetSway = depth ? Mathf.Clamp(vx * 0.35f, -1.5f, 1.5f) : 0f;
            _swayY = Mathf.Lerp(_swayY, targetSway, 1f - Mathf.Exp(-4f * dt));

            if (depth)
            {
                _cam.orthographic = false;
                _cam.fieldOfView = Fov;
                _cam.nearClipPlane = 0.3f;
                _cam.farClipPlane = 260f;                                   // backdrop planes sit out to z≈120
                _cam.transparencySortMode = TransparencySortMode.Orthographic; // equal-order sprites tie-break by Z
                transform.position = new Vector3(c.x, c.y, -DistanceFor(half));
                transform.rotation = Quaternion.Euler(0f, _swayY, 0f);
            }
            else
            {
                // Byte-identical to the pre-rig camera: ortho size + Z −10, no tilt.
                _cam.orthographic = true;
                _cam.orthographicSize = half;
                _cam.transparencySortMode = TransparencySortMode.Default;
                transform.position = new Vector3(c.x, c.y, -10f);
                transform.rotation = Quaternion.identity;
            }
        }
    }
}
