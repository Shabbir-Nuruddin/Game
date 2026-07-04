using UnityEditor;
using UnityEngine;

namespace TrustIssues.EditorTools
{
    /// <summary>
    /// Testing helper: wipes every PlayerPrefs key so the next play-mode run
    /// behaves like a brand-new device — the first-session PLAY path, the
    /// in-world key prompts, fresh castle progress, no analytics device id.
    /// This game keeps almost all state in prefs, so this IS the reset button.
    /// </summary>
    public static class ResetDevice
    {
        [MenuItem("Trust Issues/Reset Device (wipe PlayerPrefs)")]
        public static void Wipe()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            Debug.Log("Trust Issues: all PlayerPrefs wiped — the next run is a first session.");
        }
    }
}
