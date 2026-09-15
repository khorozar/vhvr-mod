using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValheimVRMod.VRCore.UI;
using ValheimVRMod.Utilities;

namespace ValheimVRMod.Scripts {
    public class ConfigComponent : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
        private static ConfigComponent currentHoveredComponent;

        public KeyValuePair<string, ConfigEntryBase> configValue;
        public UnityAction<string> saveAction;
        public string value;
        public string helpText;
        // Key bindings are committed by Valheim's separate binding dialog. Other controls
        // preview directly into their ConfigEntry and must not be overwritten by a stale
        // duplicate control when the settings dialog closes.
        public bool usesDeferredSaveAction;

        // Apply immediately so VR settings can be judged while the player is looking at
        // the body, HUD, or menu. ConfigSettings owns the session and restores this value
        // on Back, so previewing never commits a half-finished adjustment to disk.
        public void Preview(string serializedValue)
        {
            value = serializedValue;
            configValue.Value.SetSerializedValue(serializedValue);
        }

        public void FinishPreview(bool save)
        {
            if (save)
            {
                // Key bindings still use their dedicated save action; live controls
                // have already written their current value to the same ConfigEntry.
                if (usesDeferredSaveAction && !string.IsNullOrEmpty(value) && saveAction != null)
                {
                    saveAction(value);
                }
            }
        }

        public void OnPointerEnter(PointerEventData eventData) {
            // Help is now permanently visible below every setting. Keeping a hover-only
            // tooltip made it difficult to operate with a VR laser pointer.
        }

        public void OnPointerExit(PointerEventData eventData) {
            // See OnPointerEnter.
        }

        // Saving/restoring is deliberately handled by ConfigSettings as one transaction,
        // rather than independently as each cloned row is destroyed.
    }
}
