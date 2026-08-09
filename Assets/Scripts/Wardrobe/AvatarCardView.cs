using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TrustIssues
{
    public sealed class AvatarCardView : MonoBehaviour
    {
        public Image cardBackground, selectedGlow, avatarImage, requirementIcon;
        public TMP_Text nameText, requirementText, equippedText;
        public Button button;

        public void Bind(string displayName, string requirement, Sprite avatar, Sprite icon,
            Color accent, Color avatarTint, bool unlocked, bool equipped, Action onPressed)
        {
            name = "AvatarCard_" + displayName.Replace(" ", string.Empty);
            nameText.text = displayName.ToUpperInvariant();
            nameText.color = accent;
            avatarImage.sprite = avatar; avatarImage.preserveAspect = true;
            avatarImage.color = avatarTint;
            requirementIcon.sprite = icon; requirementIcon.enabled = icon != null;
            requirementIcon.color = accent;
            requirementText.text = requirement;
            requirementText.color = unlocked ? new Color(.90f, .84f, .75f, 1f) : accent;
            equippedText.text = equipped ? "EQUIPPED" : unlocked ? "TAP TO EQUIP" : "";
            equippedText.color = equipped ? new Color(1f, .72f, .18f, 1f) : unlocked
                ? new Color(.72f, .66f, .61f, 1f) : new Color(.85f, .22f, .28f, 1f);
            selectedGlow.enabled = equipped;
            cardBackground.color = Color.white;
            button.onClick.RemoveAllListeners();
            if (onPressed != null) button.onClick.AddListener(() => onPressed());
        }
    }
}
