using System.Text;
using UnityEditor;
using UnityEngine;
using TrustIssues;

// Editor-only ground-truth for jump reach. The room-rule floors live or die on
// "this gap is impossible to jump" being TRUE, and an earlier pass shipped ghost
// gaps at 3.4 units on the false belief that a jump clears ~3.2 — it clears far
// more. Rather than guess again, this simulates PlayerController's ACTUAL
// FixedUpdate integration (same constants, same gravity switch, same air
// steering) and prints the real max horizontal reach for the base character and
// every skin's mobility traits, with and without bat glide.
//
// Never compiled into a build (lives under Assets/Editor). Run via the Trust
// Issues > Probe Jump Arcs menu, or -executeMethod JumpArcProbe.Run.
public static class JumpArcProbe
{
    // Mirrors PlayerController.cs:16-21, 28-29, 41 and FixedUpdate (338-403).
    const float MoveSpeed = 7.5f, JumpSpeed = 14f, FallG = 5.5f, RiseG = 3.4f;
    const float AirAccel = 70f, GlideFall = 2.2f, FlyDrain = 0.95f;
    const float G = 9.81f;                 // Physics2DSettings.asset m_Gravity.y
    const float Dt = 0.02f;                // 50 Hz fixed step (Unity default)
    const float ColliderHalf = 0.4f;       // player box half-width (approx) — gap edge bonus

    [MenuItem("Trust Issues/Probe Jump Arcs")]
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("JUMPARC — max horizontal reach (centre-to-centre), floor-to-floor landing");
        sb.AppendLine("skin                 | moveMul jumpMul air | plainJump | +double | +glide | notes");
        sb.AppendLine(new string('-', 92));

        Row(sb, "base / Castle (no fly)", 1f, 1f, 0, glide: false);
        foreach (var s in Skins.All)
        {
            // Skins that are pure cosmetics (all muls 1, no traits) don't move the number.
            bool trait = s.moveMul != 1f || s.jumpMul != 1f || s.airJumps > 0 || s.dash;
            if (!trait && s.id != "heir") continue;
            Row(sb, s.name, s.moveMul, s.jumpMul, s.airJumps, glide: true, dash: s.dash);
        }

        sb.AppendLine();
        sb.AppendLine("GAP TUNING RULES:");
        sb.AppendLine("  NightFloor (blind but jumpable): gap <= plainJump(base) so a blind hop clears it.");
        sb.AppendLine("  GhostFloor (must trust the dark): gap > BEST plainJump+glide across ALL skins,");
        sb.AppendLine("  or a premium-skin player jumps it lit and never learns the rule.");
        sb.AppendLine($"  Add ~{ColliderHalf * 2f:0.0} (both collider halves) to any 'edge-to-edge' gap to get centre-travel.");

        var text = sb.ToString();
        Debug.Log("JUMPARC_BEGIN\n" + text + "JUMPARC_END");
    }

    static void Row(StringBuilder sb, string label, float moveMul, float jumpMul, int airJumps,
                    bool glide, bool dash = false)
    {
        float plain  = SimReach(moveMul, jumpMul, airJumps: 0, glide: false);
        float dbl    = airJumps > 0 ? SimReach(moveMul, jumpMul, airJumps, glide: false) : plain;
        float glided = glide ? SimReach(moveMul, jumpMul, airJumps, glide: true) : plain;
        string note = dash ? "dash adds a flat burst on top" : "";
        sb.AppendLine($"{label,-20} | {moveMul,6:0.00} {jumpMul,6:0.00} {airJumps,3} | " +
                      $"{plain,9:0.00} | {dbl,7:0.00} | {glided,6:0.00} | {note}");
    }

    // Integrate a jump from a running start (v.x already at run speed) until the
    // character returns to launch height. Returns horizontal distance travelled.
    static float SimReach(float moveMul, float jumpMul, int airJumps, bool glide)
    {
        float wantX = MoveSpeed * moveMul;
        float vx = wantX;                  // running takeoff
        float vy = JumpSpeed * jumpMul;
        float x = 0f, y = 0f, flyMeter = 1f;
        int air = airJumps;
        bool jumpHeld = true;              // hold for the full floaty arc = max range

        for (int i = 0; i < 2000; i++)     // 40 s cap; jumps end far sooner
        {
            // Air steering: ease vx toward wantX at airAccel (MoveTowards).
            vx = Mathf.MoveTowards(vx, wantX, AirAccel * Dt);

            // Spend a double-jump at the top of the rise for max distance.
            if (air > 0 && vy <= 0.1f) { vy = JumpSpeed * jumpMul; air--; }

            // Glide: once falling, cap the fall while the meter lasts.
            bool flying = glide && vy < 0f && flyMeter > 0f;
            if (flying) flyMeter -= FlyDrain * Dt;

            float gScale = (vy > 0.1f && jumpHeld) ? RiseG : FallG;
            vy -= G * gScale * Dt;
            if (flying && vy < -GlideFall) vy = -GlideFall;

            x += vx * Dt;
            y += vy * Dt;
            if (y <= 0f && vy < 0f) break; // landed back at launch height
        }
        return x;
    }
}
