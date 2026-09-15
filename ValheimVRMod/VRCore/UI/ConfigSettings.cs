using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Valheim.SettingsGui;
using ValheimVRMod.Scripts;
using ValheimVRMod.Utilities;
using Object = UnityEngine.Object;
using TMPro;
using ValheimVRMod.Patches;

namespace ValheimVRMod.VRCore.UI {
    public class ConfigSettings {

        // TODO: Refactor and fix VHVR settings dialog layout.
        private const bool ENABLE_VHVR_SETTINGS_DIALOG = true;
        
        private const float MENU_ENTRY_HEIGHT = 40;
        private const float SETTINGS_ENTRY_HEIGHT = 52;
        private const string MenuName = "VHVR";
        private const int TabButtonWidth = 100;
        private const string SettingsHelp = "Point at a control and pull the trigger. Changes preview immediately. Save keeps them; Back restores the previous values. Each setting has its explanation directly underneath.";
        
        private static GameObject tabButtonPrefab;
        private static GameObject controlSettingsPrefab;
        private static GameObject togglePrefab;
        private static GameObject sliderPrefab;
        private static GameObject chooserPrefab;
        private static GameObject settingsPrefab;
        private static GameObject keyBindingPrefab;
        private static GameObject transformButtonPrefab;
        private static GameObject settings;
        private static Transform menuList;
        private static Transform menuParent;
        private static ConfigComponent tmpComfigComponent;
        private static bool enableTransformButtons;
        private static int tabCounter;
        public static bool doSave;
        public static GameObject toolTip;
        private static TMP_Text toolTipText;
        private static Image toolTipBackground;
        private static bool previewSaveOnConfigSet;
        private static Dictionary<ConfigEntryBase, string> previewOriginalValues;

        private class SettingsPage
        {
            public string Name;
            public string[] Keys;

            public SettingsPage(string name, params string[] keys)
            {
                Name = name;
                Keys = keys;
            }
        }

        // These labels describe an outcome rather than exposing internal config keys.
        // Keys not listed here stay available in Advanced with a readable split-name label.
        private static readonly Dictionary<string, string> FriendlyNames = new Dictionary<string, string>
        {
            { "PlayerHeightAdjust", "Body height offset" }, { "WorldScale", "World scale" },
            { "RecenterOnStart", "Recenter when the game starts" }, { "DisableRecenterPose", "Disable hands-in-front recenter" },
            { "DominantHand", "Dominant hand" }, { "SnapTurnEnabled", "Snap turning" },
            { "SnapTurnAngle", "Snap turn angle" }, { "SmoothTurnSpeed", "Smooth turn speed" },
            { "JoyStickForwardDirection", "Movement direction" }, { "CharaterMovesWithHeadset", "Room-scale movement" },
            { "SneakInput", "How to crouch" }, { "GesturedLocomotion", "Gestured movement" },
            { "UseLegacyHud", "Use classic HUD" }, { "QuickMenuType", "Quick-menu orientation" },
            { "QuickMenuVerticalAngle", "Quick-menu angle" }, { "UIPanelSize", "Menu size" },
            { "UIPanelDistance", "Menu distance" }, { "UIPanelVerticalOffset", "Menu height" },
            { "OneHandedBow", "One-handed bow and crossbow" }, { "SwingSpeedRequirement", "Melee swing strength" },
            { "MomentumScalesAttackDamage", "Scale melee damage by swing" }, { "UseAmplifyOcclusion", "Ambient occlusion" },
            { "EnemyRenderDistance", "Enemy render distance" }, { "BuildingPieceDetailReductionFactor", "Building detail" },
            { "ShowDamageText", "Show damage numbers" }, { "NearClipPlane", "Near clipping distance" },
            { "HipTrackerIndex", "Hip tracker device" }, { "LeftFootTrackerIndex", "Left-foot tracker device" },
            { "RightFootTrackerIndex", "Right-foot tracker device" }
        };

        private static readonly Dictionary<string, string> FriendlyHelp = new Dictionary<string, string>
        {
            { "PlayerHeightAdjust", "Move the character body up or down relative to your real head. Changes are visible immediately." },
            { "WorldScale", "Change how large the world feels. 1.00 is natural size; changing it re-calibrates your eye position." },
            { "JoyStickForwardDirection", "Choose what ‘forward’ means when you push the stick: your view, a hand, your body, or the character." },
            { "CharaterMovesWithHeadset", "Move the character when you physically walk in your play area. Turn it off if you prefer leaning in place." },
            { "SnapTurnEnabled", "Turn in fixed comfort steps instead of continuously." },
            { "SnapTurnAngle", "How far each snap turn rotates you." }, { "SmoothTurnSpeed", "How quickly continuous turning rotates you." },
            { "QuickMenuType", "Choose whether the quick menu follows your hand, body, or view." },
            { "UIPanelDistance", "How far the main inventory and menu panel sits in front of you. Changes are visible immediately." },
            { "UIPanelSize", "Size of the main VR menu panel. Changes are visible immediately." },
            { "UseAmplifyOcclusion", "Adds contact shadows. Performance impact: Medium." },
            { "EnemyRenderDistance", "How far away enemies are fully rendered. Performance impact: High." },
            { "BuildingPieceDetailReductionFactor", "Reduce distant building detail for better performance. Performance impact: Medium." },
            { "NearClipPlane", "Advanced: how close objects can get before they disappear from view." }
        };

        public static KeyboardMouseSettings keyboardMouseSettings;

        public static void instantiate(Transform mList, Transform mParent, GameObject sPrefab, bool enableTransformButtons) {
            menuList = mList.transform.Find("MenuEntries").transform;
            menuParent = mParent;
            settingsPrefab = sPrefab;
            createMenuEntry();
            generatePrefabs();
            ConfigSettings.enableTransformButtons = enableTransformButtons;
        }

        public static bool isVHVRClone(Component component)
        {
            return component.GetComponentInParent<SettingsCloneMarker>(includeInactive: true) != null;
        }
 
        /// <summary>
        /// Create an Entry in the Menu 
        /// </summary>
        private static void createMenuEntry() {
            int addedMenuEntryCount = 0;
            for (int i = 0; i < menuList.childCount; i++) {
                Transform menuEntry = menuList.GetChild(i);
                if (menuEntry.name == "Settings") {
                    if (ENABLE_VHVR_SETTINGS_DIALOG)
                    {
                        AddMenuEntry(MenuName, menuEntry, Vector2.zero, createModSettings);
                        addedMenuEntryCount++;
                    }

                    AddMenuEntry("Screenshot", menuEntry, Vector2.up * MENU_ENTRY_HEIGHT * addedMenuEntryCount, CaptureScreenshot);
                    addedMenuEntryCount++;

                    AddMenuEntry("Toggle auto-pickup", menuEntry, Vector2.up * MENU_ENTRY_HEIGHT * addedMenuEntryCount, ToggleAutoPickup);
                    addedMenuEntryCount++;

                }
                else if (addedMenuEntryCount > 0) {
                    var rectTransform = menuList.GetChild(i).GetComponent<RectTransform>();
                    rectTransform.anchoredPosition = rectTransform.anchoredPosition + Vector2.up * MENU_ENTRY_HEIGHT * addedMenuEntryCount;
                }
            }
        }

        private static void AddMenuEntry(string text, Transform original, Vector2 offsetFromOriginal, UnityAction onClick)
        {
            Transform menuEntry = GameObject.Instantiate(original, parent: menuList);
            menuEntry.GetComponentInChildren<TextMeshProUGUI>().text = text;
            menuEntry.GetComponent<Button>().onClick.SetPersistentListenerState(0, UnityEventCallState.Off);
            menuEntry.GetComponent<Button>().onClick.RemoveAllListeners();
            menuEntry.GetComponent<Button>().onClick.AddListener(onClick);
            RectTransform rectTransform = menuEntry.GetComponent<RectTransform>();
            rectTransform.anchoredPosition = rectTransform.anchoredPosition + offsetFromOriginal;
        }

        /// <summary>
        /// Create temporary prefabs out of existing elements
        /// </summary>
        private static void generatePrefabs() {

            var tabButtons = settingsPrefab.transform.Find("Panel").Find("TabButtons");
            if (tabButtonPrefab == null)
            {
                tabButtonPrefab = createTabButtonPrefab(tabButtons.GetChild(0).gameObject);
            }
            var tabs = settingsPrefab.transform.Find("Panel").Find("TabContent");
            controlSettingsPrefab = tabs.Find("KeyboardMouse").gameObject;
            togglePrefab = controlSettingsPrefab.GetComponentInChildren<Toggle>().gameObject;
            if (sliderPrefab == null)
            {
                sliderPrefab = createSliderPrefab(controlSettingsPrefab.GetComponentInChildren<Slider>().gameObject);
            }
            if (keyBindingPrefab == null)
            {
                keyBindingPrefab = createKeyBindingPrefab(
                    controlSettingsPrefab.transform.Find("List").Find("Bindings").Find("Viewport").Find("Grid").Find("Use").gameObject);
            }
            if (chooserPrefab == null)
            {
                chooserPrefab = createChooserPrefab(
                    settingsPrefab.transform.Find("Panel").Find("TabContent").Find("Gamepad").Find("Root").Find("CommonSettings").Find("InputLayout").gameObject);
            }
            if (transformButtonPrefab == null)
            {
                transformButtonPrefab = createTransformButtonPrefab(
                    sliderPrefab.transform.Find("Label").gameObject,
                    settingsPrefab.transform.Find("Panel").Find("Back").gameObject);
            }

            // These prefabs are cloned from vanilla settings rows, which carry a Localize component.
            // Its Start() runs a frame after instantiation and re-localizes the row, reverting the
            // config label we write back to the vanilla token's text - the chooser rows are cloned
            // from the gamepad InputLayout setting, so they all reverted to "Controller layout".
            StripLocalization(tabButtonPrefab);
            StripLocalization(sliderPrefab);
            StripLocalization(keyBindingPrefab);
            StripLocalization(chooserPrefab);
            StripLocalization(transformButtonPrefab);
        }

        private static void StripLocalization(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }
            foreach (var localize in prefab.GetComponentsInChildren<Localize>(includeInactive: true))
            {
                Object.Destroy(localize);
            }
            foreach (var localize in prefab.GetComponentsInChildren<PlatformSpecificLocalization>(includeInactive: true))
            {
                Object.Destroy(localize);
            }
        }

        private static void createToolTip(Transform settings) {
            toolTip = new GameObject();
            toolTip.transform.SetParent(settings, false);

            toolTipBackground = toolTip.AddComponent<Image>();
            toolTipBackground.rectTransform.pivot = new Vector2(0.5f, 0);
            toolTipBackground.rectTransform.anchoredPosition = new Vector2(0, -400);
            toolTipBackground.color = new Color(0,0,0,0.5f);
            toolTipBackground.raycastTarget = false;

            var textObj = Object.Instantiate(togglePrefab.GetComponentInChildren<TMP_Text>().gameObject, toolTip.transform);
            toolTipText = textObj.GetComponent<TMP_Text>();
            toolTipText.rectTransform.anchorMin = toolTipText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            toolTipText.rectTransform.sizeDelta = new Vector2(900, 125);
            toolTipText.rectTransform.anchoredPosition = new Vector2(454, 0);
            // text.resizeTextForBestFit = false;
            toolTipText.fontSize = 20;
            toolTipText.alignment = TextAlignmentOptions.MidlineLeft;
            toolTipText.raycastTarget = false;

            toolTip.SetActive(false);
        }

        public static void ShowToolTip(string text)
        {
            if (toolTip == null || toolTipText == null || toolTipBackground == null)
            {
                return;
            }

            toolTipText.text = text;
            toolTipBackground.rectTransform.sizeDelta = new Vector2(908, toolTipText.preferredHeight + 8);
            toolTip.SetActive(true);
        }

        public static void HideToolTip()
        {
            if (toolTip != null)
            {
                toolTip.SetActive(false);
            }
        }

        /// <summary>
        /// Make Copy of ingame Settings, clean up all existing tabs, then iterate bepinex config
        /// </summary>
        private static void createModSettings() {
            settings = Object.Instantiate(settingsPrefab, menuParent);
            doSave = false;
            previewSaveOnConfigSet = VHVRConfig.config.SaveOnConfigSet;
            VHVRConfig.config.SaveOnConfigSet = false;
            previewOriginalValues = new Dictionary<ConfigEntryBase, string>();
            foreach (var entry in VHVRConfig.config)
            {
                previewOriginalValues[entry.Value] = entry.Value.GetSerializedValue();
            }
            settings.AddComponent<SettingsCloneMarker>();
            settings.transform.Find("Panel").Find("Title").GetComponent<TMP_Text>().text = MenuName;
            createSettingsHelp(settings.transform.Find("Panel"));
            var tabButtons = settings.transform.Find("Panel").Find("TabButtons");

            // destroy old tab buttons
            foreach (Transform t in tabButtons) {
                Object.Destroy(t.gameObject);
            }
            // clear tab array
            tabButtons.GetComponent<TabHandler>().m_tabs.Clear();

            // deactivate old tab contents
            foreach (Transform t in settings.transform.Find("Panel").Find("TabContent")) {
                t.gameObject.SetActive(false);
            }

            tabCounter = 0;
            var allConfig = GetRuntimeConfigEntries();
            var usedKeys = new HashSet<string>();
            var pages = GetSettingsPages();
            var totalTabs = pages.Count + 2; // Advanced + Controls
            foreach (var page in pages)
            {
                createTabForPage(page, allConfig, usedKeys, totalTabs);
            }
            createAdvancedTab(allConfig, usedKeys, totalTabs);
            createControlsTab(totalTabs);

            setupOkAndBack(settings.transform.Find("Panel"));

            // The vanilla KeyboardMouse page clone runs its own enable logic one frame later.
            // Re-activate our first generated page after that logic has finished; otherwise it
            // is intermittently left empty until the user switches away and back.
            settings.AddComponent<InitialTabActivator>().Initialize(tabButtons.GetComponent<TabHandler>(), settings.transform.Find("Panel").Find("TabContent").Find(pages[0].Name).gameObject);
            keyboardMouseSettings.UpdateBindings();
        }

        private static void createSettingsHelp(Transform panel)
        {
            var help = Object.Instantiate(togglePrefab.GetComponentInChildren<TMP_Text>().gameObject, panel);
            help.name = "VHVRControlsHelp";
            var text = help.GetComponent<TMP_Text>();
            text.text = SettingsHelp;
            text.fontSize = 14;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0, -55);
            rect.sizeDelta = new Vector2(850, 38);
        }

        // Adds listeners for ok and back buttons
        private static void setupOkAndBack(Transform panel) {
            Button okButton = panel.Find("Ok")?.GetComponent<Button>();
            if (okButton == null)
            {
                LogUtils.LogWarning("Failed to find Ok button for VHVR");
            }
            else
            {
                okButton.onClick.RemoveAllListeners();
                okButton.onClick.m_PersistentCalls.Clear();
                okButton.onClick.AddListener(() => {
                    finishSettings(save: true);
                });
                Object.Destroy(okButton.GetComponent<UIGamePad>());
                var hint = okButton.transform.Find("KeyHint");
                if (hint) Object.Destroy(hint.gameObject);
            }

            Button backButton = panel.Find("Back")?.GetComponent<Button>();
            if (backButton == null)
            {
                LogUtils.LogWarning("Failed to find Back button for VHVR");
            }
            else
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.m_PersistentCalls.Clear();
                backButton.onClick.AddListener(() => {
                    finishSettings(save: false);
                });
                Object.Destroy(backButton.GetComponent<UIGamePad>());
                var hint = backButton.transform.Find("KeyHint");
                if (hint) Object.Destroy(hint.gameObject);
            }
        }

        /// <summary>
        /// Create a new Tab out of a config section
        /// </summary>
        private static void createTabForSection(KeyValuePair<string, Dictionary<string, ConfigEntryBase>> section, int sectionCount) {
            var tabButtons = settings.transform.Find("Panel").Find("TabButtons");

            // Create new tab button
            var newTabButton = Object.Instantiate(tabButtonPrefab, tabButtons);

            newTabButton.name = section.Key;
            var rectTransform = newTabButton.GetComponent<RectTransform>();
            var tabButtonXPosition = TabButtonWidth * (tabCounter - (sectionCount - 1) * 0.5f);
            rectTransform.anchoredPosition = new Vector2(tabButtonXPosition, rectTransform.anchoredPosition.y);

            var labels = newTabButton.GetComponentsInChildren<TMP_Text>(includeInactive: true);
            foreach (var label in labels)
            {
                label.text = section.Key;
            }

            // Create new tab content
            var tabs = settings.transform.Find("Panel").Find("TabContent");
            var newTab = Object.Instantiate(tabs.GetChild(0), tabs);
            newTab.name = section.Key;

            foreach (Transform child in newTab.transform)
            {
                Object.Destroy(child.gameObject);
            }

            // Register the new Tab in Tab array
            var tab = new TabHandler.Tab();
            tab.m_button = newTabButton.GetComponent<Button>();
            var activeTabIndex = tabCounter;
            tab.m_button.onClick.AddListener(() => {
                tabButtons.GetComponent<TabHandler>().SetActiveTab(activeTabIndex);
            });
            tab.m_default = tabCounter == 0;
            tab.m_page = newTab.GetComponent<RectTransform>();
            tab.m_onClick = new UnityEvent();

            tabButtons.GetComponent<TabHandler>().m_tabs.Add(tab);

            Transform content = CreateScrollableContent(newTab.gameObject, section.Value.Count);
            float contentHeight = content.GetComponent<RectTransform>().rect.height;
            int row = 0;

            // One scrollable column keeps descriptions readable and avoids the overlap
            // caused by wrapping long pages into two narrow columns.
            foreach (KeyValuePair<string, ConfigEntryBase> configValue in section.Value) {
                var position = new Vector2(0, contentHeight * 0.5f - 42f - row * 68f);
                if (! createElement(configValue, content, position, section.Key)) {
                    continue;
                }
                row++;
            }

            tabCounter++;
        }

        private static Transform CreateScrollableContent(GameObject page, int entryCount)
        {
            var viewport = new GameObject("SettingsViewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(page.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(18, 18);
            viewportRect.offsetMax = new Vector2(-18, -48);

            var content = new GameObject("SettingsContent", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, Mathf.Max(520f, entryCount * 68f + 42f));
            contentRect.anchoredPosition = Vector2.zero;

            var scrollRect = page.GetComponent<ScrollRect>() ?? page.AddComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.verticalNormalizedPosition = 1f;
            return content.transform;
        }

        private static void createControlsTab(int tabCount)
        {
            var tabButtons = settings.transform.Find("Panel").Find("TabButtons");
            var buttonObject = Object.Instantiate(tabButtonPrefab, tabButtons);
            buttonObject.name = "Controls";
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchoredPosition = new Vector2(TabButtonWidth * (tabCounter - (tabCount - 1) * 0.5f), buttonRect.anchoredPosition.y);
            foreach (var label in buttonObject.GetComponentsInChildren<TMP_Text>(includeInactive: true)) label.text = "Controls";

            var tabs = settings.transform.Find("Panel").Find("TabContent");
            var page = Object.Instantiate(tabs.GetChild(0), tabs);
            page.name = "Controls";
            foreach (Transform child in page.transform) Object.Destroy(child.gameObject);

            var tab = new TabHandler.Tab
            {
                m_button = buttonObject.GetComponent<Button>(),
                m_default = false,
                m_page = page.GetComponent<RectTransform>(),
                m_onClick = new UnityEvent()
            };
            var activeTabIndex = tabCounter;
            tab.m_button.onClick.AddListener(() => tabButtons.GetComponent<TabHandler>().SetActiveTab(activeTabIndex));
            tabButtons.GetComponent<TabHandler>().m_tabs.Add(tab);

            var guideObject = new GameObject("QuestControlsGuide", typeof(RectTransform), typeof(TextMeshProUGUI));
            guideObject.transform.SetParent(page, false);
            var guide = guideObject.GetComponent<TextMeshProUGUI>();
            var template = togglePrefab.GetComponentInChildren<TMP_Text>();
            guide.font = template.font;
            guide.fontSharedMaterial = template.fontSharedMaterial;
            guide.fontSize = 15;
            guide.alignment = TextAlignmentOptions.TopLeft;
            guide.enableWordWrapping = true;
            guide.text =
                "<b>Quest 3 — standard VHVR binding</b>\n\n" +
                "<b>Left stick</b>  Move   |   <b>Left-stick click</b>  Map\n" +
                "<b>Right stick</b>  Turn/look   |   <b>Right-stick click</b>  Menu\n" +
                "<b>A</b>  Jump / confirm in context   |   <b>B</b>  Right-hand quick slots\n" +
                "<b>X</b>  Inventory   |   <b>Y</b>  Left-hand quick actions\n" +
                "<b>Either trigger</b>  Use / interact / activate selected item\n" +
                "<b>Either Grip</b>  Hold that hand's weapon or object\n" +
                "<b>Right stick: push up</b>  Toggle run   |   <b>push down</b>  Toggle crouch (when Crouch = Controller only)\n\n" +
                "<b>While a VR laser is active</b>\n" +
                "<b>Right trigger</b>  Select / left click   |   <b>Right B</b>  Back / right click\n" +
                "<b>Grip + trigger</b>  Context action (map pin, discard, split stack — depends on the screen)\n\n" +
                "<b>Spear</b>\n" +
                "<b>Forward thrust into a target</b>  Melee stab   |   <b>Grip + trigger + throw</b>  Throw\n\n" +
                "Bindings are the profile defaults. SteamVR Input can override them.";
            guide.raycastTarget = false;
            var rect = guide.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0, -5);
            rect.sizeDelta = new Vector2(880, 510);
            tabCounter++;
        }

        /// <summary>
        /// Create a Settings Element out of a Config Entry
        /// </summary>
        private static bool createElement(KeyValuePair<string, ConfigEntryBase> configEntry, Transform parent, Vector2 pos, string sectionName) {
            
            var helpText = GetHelpText(configEntry);
            if (configEntry.Value.SettingType == typeof(bool)) {
                createToggle(configEntry, parent, pos);
                createInlineDescription(parent, pos, helpText);
                return true;
            }

            if (configEntry.Value.SettingType == typeof(KeyCode)) {
                createKeyBinding(configEntry, parent, pos);
                createInlineDescription(parent, pos, helpText);
                return true;
            }
            
            if (configEntry.Value.SettingType == typeof(Vector3)) {
                createTransformButton(configEntry, parent, pos, sectionName);
                createInlineDescription(parent, pos, helpText);
                return true;
            }
            
            if (configEntry.Value.SettingType == typeof(Quaternion)) {
                // ignore
                return false;
            }
            
            var acceptableValues = configEntry.Value.Description.AcceptableValues;
            if (acceptableValues == null) {
                return false;
            }

            var type = acceptableValues.GetType();
            if (type.GetGenericTypeDefinition() == typeof(AcceptableValueList<>)) {
                createValueList(configEntry, parent, pos, type, acceptableValues);
                createInlineDescription(parent, pos, helpText);
                return true;
            }
            
            if (type.GetGenericTypeDefinition() == typeof(AcceptableValueRange<>)) {
                createValueRange(configEntry, parent, pos, type, acceptableValues);
                createInlineDescription(parent, pos, helpText);
                return true;
            }

            return false;
        }

        private static string GetDisplayName(string key)
        {
            if (FriendlyNames.TryGetValue(key, out var name)) return name;
            return Regex.Replace(key, "(?<!^)([A-Z])", " $1");
        }

        private static string GetHelpText(KeyValuePair<string, ConfigEntryBase> entry)
        {
            var help = FriendlyHelp.TryGetValue(entry.Key, out var friendly) ? friendly : entry.Value.Description.Description;
            help = Regex.Replace(help ?? string.Empty, "\\s+", " ").Trim();
            // Long BepInEx descriptions were written for a text config file. In VR, keep the
            // always-visible version scannable; the first sentence normally contains the action.
            var sentenceEnd = help.IndexOfAny(new[] { '.', '!', '?' });
            if (sentenceEnd >= 0 && sentenceEnd < 170)
            {
                help = help.Substring(0, sentenceEnd + 1);
            }
            return help.Length <= 175 ? help : help.Substring(0, 172).TrimEnd() + "...";
        }

        private static void createInlineDescription(Transform parent, Vector2 pos, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var description = new GameObject("Description", typeof(RectTransform), typeof(TextMeshProUGUI));
            description.transform.SetParent(parent, false);
            var label = description.GetComponent<TextMeshProUGUI>();
            var template = togglePrefab.GetComponentInChildren<TMP_Text>();
            label.font = template.font;
            label.fontSharedMaterial = template.fontSharedMaterial;
            label.text = text;
            label.fontSize = 10;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.color = new Color(0.78f, 0.82f, 0.86f, 1f);
            label.raycastTarget = false;

            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = pos + new Vector2(-105, -17);
            rect.sizeDelta = new Vector2(315, 29);
        }

        private static void ConfigureRow(ConfigComponent component, KeyValuePair<string, ConfigEntryBase> entry)
        {
            component.configValue = entry;
            component.helpText = GetHelpText(entry);
        }

        private static GameObject createTabButtonPrefab(GameObject vanillaObject)
        {
            var prefab = GameObject.Instantiate(vanillaObject);
            var hint = prefab.transform.Find("KeyHint");
            if (hint) GameObject.Destroy(hint.gameObject);
            var button = prefab.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.m_PersistentCalls.Clear();
            return prefab;

        }

        private static GameObject createSliderPrefab(GameObject vanillaPrefab)
        {

            var sliderObj = Object.Instantiate(vanillaPrefab);
            var rectTransform = sliderObj.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(150, 30);
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            sliderObj.transform.Find("Label").GetComponent<RectTransform>().sizeDelta = new Vector3(200, 0);
            return sliderObj;
        }

        private static void createValueRange(KeyValuePair<string, ConfigEntryBase> configValue, Transform parent, Vector2 pos, Type type,
            AcceptableValueBase acceptableValues) {

            var sliderObj = Object.Instantiate(sliderPrefab, parent);
            var configComponent = sliderObj.AddComponent<ConfigComponent>();
            ConfigureRow(configComponent, configValue);
            sliderObj.transform.Find("Label").GetComponent<TMP_Text>().text = GetDisplayName(configValue.Key);
            sliderObj.GetComponent<RectTransform>().anchoredPosition = pos + Vector2.right * 60;
            var slider = sliderObj.GetComponentInChildren<Slider>();
            slider.minValue = float.Parse(type.GetProperty("MinValue").GetValue(acceptableValues).ToString());
            slider.maxValue =  float.Parse(type.GetProperty("MaxValue").GetValue(acceptableValues).ToString());
            var isFloat = acceptableValues.ValueType == typeof(float);
            var increment = isFloat ? GetSliderIncrement(configValue.Key, slider.minValue, slider.maxValue) : 1f;
            // Opening the menu must not invoke a listener and rewrite an existing value.
            slider.SetValueWithoutNotify(SnapToIncrement(float.Parse(configValue.Value.GetSerializedValue(), CultureInfo.InvariantCulture), slider.minValue, increment));
            var text = slider.transform.Find("Value").GetComponent<TMP_Text>();
            text.text = FormatSliderValue(slider.value, increment);

            slider.onValueChanged.AddListener(
                (value) =>
                {
                    var snappedValue = SnapToIncrement(value, slider.minValue, increment);
                    if (!Mathf.Approximately(slider.value, snappedValue))
                    {
                        slider.SetValueWithoutNotify(snappedValue);
                    }
                    text.text = FormatSliderValue(snappedValue, increment);
                    configComponent.Preview(snappedValue.ToString(CultureInfo.InvariantCulture));
                });

            if (acceptableValues.ValueType == typeof(int)) {
                slider.wholeNumbers = true;
            }
            
            configComponent.saveAction = param => {
                var snappedValue = SnapToIncrement(slider.value, slider.minValue, increment);
                configValue.Value.SetSerializedValue(snappedValue.ToString(CultureInfo.InvariantCulture));
            };


        }

        private static void finishSettings(bool save)
        {
            if (settings == null) return;
            doSave = save;
            foreach (var component in settings.GetComponentsInChildren<ConfigComponent>(includeInactive: true))
            {
                component.FinishPreview(save);
            }
            if (!save && previewOriginalValues != null)
            {
                foreach (var originalValue in previewOriginalValues)
                {
                    originalValue.Key.SetSerializedValue(originalValue.Value);
                }
            }
            VHVRConfig.config.SaveOnConfigSet = previewSaveOnConfigSet;
            if (save)
            {
                VHVRConfig.config.Save();
            }
            GameObject.Destroy(settings);
            settings = null;
            previewOriginalValues = null;
        }

        private static Dictionary<string, KeyValuePair<string, ConfigEntryBase>> GetRuntimeConfigEntries()
        {
            var entries = new Dictionary<string, KeyValuePair<string, ConfigEntryBase>>();
            foreach (var entry in VHVRConfig.config)
            {
                if (entry.Key.Section != "Immutable" && entry.Key.Key != "GroqApiKey")
                {
                    entries[entry.Key.Key] = new KeyValuePair<string, ConfigEntryBase>(entry.Key.Key, entry.Value);
                }
            }
            return entries;
        }

        private static List<SettingsPage> GetSettingsPages()
        {
            return new List<SettingsPage>
            {
                new SettingsPage("Quick Setup", "PlayerHeightAdjust", "WorldScale", "RecenterOnStart", "DominantHand", "SnapTurnEnabled", "SnapTurnAngle", "SmoothTurnSpeed", "JoyStickForwardDirection", "CharaterMovesWithHeadset", "SneakInput", "UseLegacyHud", "QuickMenuType"),
                new SettingsPage("Body & Camera", "PlayerHeightAdjust", "WorldScale", "DisableRecenterPose", "RoomscaleFadeToBlack", "ImmersiveShipCameraSitting", "ImmersiveShipCameraStanding", "ImmersiveDodgeRoll", "HipTrackerIndex", "LeftFootTrackerIndex", "RightFootTrackerIndex"),
                new SettingsPage("Movement", "SnapTurnEnabled", "SnapTurnAngle", "SmoothTurnSpeed", "SmoothSnapTurn", "SmoothSnapSpeed", "JoyStickForwardDirection", "CharaterMovesWithHeadset", "SneakInput", "RoomScaleSneakHeight", "GesturedLocomotion", "GesturedJumpPreparationHeight", "GesturedJumpMinSpeed", "RunIsToggled", "AutoRunThreshold", "InvertTurnDirection"),
                new SettingsPage("Weapons", "DominantHand", "OneHandedBow", "SwingSpeedRequirement", "MomentumScalesAttackDamage", "TwoHandedWield", "TwoHandedWithShield", "BlockingType", "SpearThrowingType", "CrossbowManualReload", "BowDrawRestrictType", "BowFullDrawLength"),
                new SettingsPage("UI & HUD", "UseLegacyHud", "UIPanelSize", "UIPanelDistance", "UIPanelVerticalOffset", "QuickMenuType", "QuickMenuVerticalAngle", "QuickMenuRadialItemDistribution", "HealthPanelPlacement", "StaminaPanelPlacement", "EitrPanelPlacement", "MinimapPanelPlacement", "CameraLocked", "CameraLocked2", "LeftWrist", "RightWrist", "AttachInventoryToHand", "AttachBuildMenuToHand"),
                new SettingsPage("Graphics", "UseAmplifyOcclusion", "ShowDamageText", "ShowAttackOutline", "RangedWeaponGlow", "MeleeWeaponGlow", "EnemyRenderDistance", "BuildingPieceDetailReductionFactor", "ShowEnemyHuds", "EnemyHudScale")
            };
        }

        private static void createTabForPage(SettingsPage page, Dictionary<string, KeyValuePair<string, ConfigEntryBase>> allConfig, HashSet<string> usedKeys, int sectionCount)
        {
            var entries = new Dictionary<string, ConfigEntryBase>();
            foreach (var key in page.Keys)
            {
                if (allConfig.TryGetValue(key, out var entry))
                {
                    entries[key] = entry.Value;
                    usedKeys.Add(key);
                }
            }
            createTabForSection(new KeyValuePair<string, Dictionary<string, ConfigEntryBase>>(page.Name, entries), sectionCount);
        }

        private static void createAdvancedTab(Dictionary<string, KeyValuePair<string, ConfigEntryBase>> allConfig, HashSet<string> usedKeys, int sectionCount)
        {
            var entries = new Dictionary<string, ConfigEntryBase>();
            foreach (var entry in allConfig)
            {
                if (!usedKeys.Contains(entry.Key)) entries[entry.Key] = entry.Value.Value;
            }
            createTabForSection(new KeyValuePair<string, Dictionary<string, ConfigEntryBase>>("Advanced", entries), sectionCount);
        }

        private static float GetSliderIncrement(string key, float min, float max)
        {
            if (key == "WorldScale" || key == "PlayerHeightAdjust")
            {
                return 0.01f;
            }
            return 0.1f;
        }

        private static float SnapToIncrement(float value, float min, float increment)
        {
            return Mathf.Round((value - min) / increment) * increment + min;
        }

        private static string FormatSliderValue(float value, float increment)
        {
            if (increment >= 1f) return value.ToString("0", CultureInfo.InvariantCulture);
            if (increment >= 0.1f) return value.ToString("0.0", CultureInfo.InvariantCulture);
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static GameObject createChooserPrefab(GameObject vanillaPrefab)
        {
            var chooserPrefab = Object.Instantiate(vanillaPrefab);
            chooserPrefab.transform.Find("LabelLeft").gameObject.SetActive(true);
            var rectTransform = chooserPrefab.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(150, 30);
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            Transform stepper = chooserPrefab.transform.Find("GUIStepper");
            for (int i = 0; i < stepper.childCount; i++)
            {
                var child = stepper.transform.GetChild(i);
                switch (child.name)
                {
                    case "Value":
                        child.gameObject.SetActive(true);
                        child.GetComponent<RectTransform>().sizeDelta = new Vector2(-60, -5);
                        child.GetComponentInChildren<TMP_Text>().fontSize = 5;
                        break;
                    case "Left":
                        child.gameObject.SetActive(true);
                        child.GetComponent<RectTransform>().sizeDelta = new Vector2(30, 30);
                        child.GetComponent<Button>().onClick.m_PersistentCalls.Clear();
                        child.GetComponent<Button>().onClick.RemoveAllListeners();
                        break;
                    case "Right":
                        child.gameObject.SetActive(true);
                        child.GetComponent<RectTransform>().sizeDelta = new Vector2(30, 30);
                        child.GetComponent<Button>().onClick.m_PersistentCalls.Clear();
                        child.GetComponent<Button>().onClick.RemoveAllListeners();
                        break;
                    default:
                        child.gameObject.SetActive(false);
                        break;
                }
            }
            return chooserPrefab;
        }

        private static void createValueList(
            KeyValuePair<string, ConfigEntryBase> configValue, Transform parent, Vector2 pos, Type type, AcceptableValueBase acceptableValues) {

            var chooserObj = Object.Instantiate(chooserPrefab, parent);
            chooserObj.GetComponent<RectTransform>().anchoredPosition = pos + Vector2.left * 10;
            chooserObj.SetActive(true);

            var configComponent = chooserObj.AddComponent<ConfigComponent>();
            ConfigureRow(configComponent, configValue);
            var configKeyText = chooserObj.transform.Find("LabelLeft").GetComponent<TMP_Text>();
            configKeyText.text = GetDisplayName(configValue.Key);
            if (configValue.Key == VHVRConfig.GesturedLocomotionLabel())
            {
                var distance = GesturedLocomotionManager.distanceTraveled;
                if (distance > 1000)
                {
                    configKeyText.text += "(s=" + (distance / 1000).ToString("F1") + "k)";
                } else if (distance > 10)
                {
                    configKeyText.text += "(s=" + distance.ToString("F0") + ")";
                }
            }
            Transform stepper = chooserObj.transform.Find("GUIStepper");
            var valueList = (string[])type.GetProperty("AcceptableValues").GetValue(acceptableValues);
            var currentIndex = Array.IndexOf(valueList, configValue.Value.GetSerializedValue());
            var valueText = stepper.Find("Value").GetComponentInChildren<TMP_Text>();
            valueText.text = configValue.Value.GetSerializedValue();
            stepper.Find("Left").GetComponent<Button>().onClick.AddListener(() =>{
                var text = valueList[mod(--currentIndex, valueList.Length)];
                valueText.text = text;
                configComponent.Preview(text);
            });
            stepper.Find("Right").GetComponent<Button>().onClick.AddListener(() => {
                var text = valueList[mod(++currentIndex, valueList.Length)];
                valueText.text = text;
                configComponent.Preview(text);
            });

            configComponent.saveAction = param => {
                configValue.Value.SetSerializedValue(stepper.Find("Value").GetComponentInChildren<TMP_Text>().text);
            };
        }
        
        private static void createToggle(KeyValuePair<string, ConfigEntryBase> configValue, Transform parent, Vector2 pos) {
            
            var toggle = Object.Instantiate(togglePrefab, parent);
            var configComponent = toggle.AddComponent<ConfigComponent>();
            ConfigureRow(configComponent, configValue);
            configComponent.saveAction = param => {
                configValue.Value.SetSerializedValue(toggle.GetComponent<Toggle>().isOn ? "true" : "false");
            };
            toggle.GetComponentInChildren<TMP_Text>().text = GetDisplayName(configValue.Key);
            toggle.GetComponent<Toggle>().isOn = configValue.Value.GetSerializedValue() == "true";
            toggle.GetComponent<Toggle>().onValueChanged.AddListener(value => configComponent.Preview(value ? "true" : "false"));
            toggle.GetComponent<RectTransform>().anchoredPosition = pos + Vector2.right * 100;
        }

        private static GameObject createTransformButtonPrefab(GameObject vanillaLabel, GameObject vanillaButton)
        {
            var transformButtonPrefab = new GameObject();
            transformButtonPrefab.AddComponent<RectTransform>().sizeDelta = new Vector2(180, 30);

            // Label
            var label = Object.Instantiate(vanillaLabel, transformButtonPrefab.transform);
            label.name = "Label";
            foreach (Transform child in label.transform)
            {
                GameObject.Destroy(child.gameObject);
            }
            label.GetComponent<RectTransform>().anchorMin = Vector2.one * 0.5f;
            label.GetComponent<RectTransform>().anchorMax = Vector2.one * 0.5f;

            // Button
            var setButton = Object.Instantiate(vanillaButton, transformButtonPrefab.transform);
            setButton.name = "SetButton";
            setButton.GetComponent<RectTransform>().anchoredPosition = Vector2.right * 50;
            setButton.GetComponent<RectTransform>().anchorMin = Vector2.one * 0.5f;
            setButton.GetComponent<RectTransform>().anchorMax = Vector2.one * 0.5f;
            setButton.GetComponent<RectTransform>().sizeDelta = new Vector2(100, 30);
            // Position-like settings are adjusted in the world by moving a controller;
            // calling this “Adjust” communicates that it is not an opaque numeric field.
            setButton.GetComponentInChildren<TMP_Text>().text = "Adjust";

            setButton.GetComponent<Button>().onClick.m_PersistentCalls.Clear();
            setButton.GetComponent<Button>().onClick.RemoveAllListeners();

            // This button will just reset the position and rotation back to the default values.
            var resetButton = Object.Instantiate(vanillaButton, transformButtonPrefab.transform);
            resetButton.name = "ResetButton";
            resetButton.GetComponent<RectTransform>().anchoredPosition = Vector2.right * 150;
            resetButton.GetComponent<RectTransform>().anchorMin = Vector2.one * 0.5f;
            resetButton.GetComponent<RectTransform>().anchorMax = Vector2.one * 0.5f;
            resetButton.GetComponent<RectTransform>().sizeDelta = new Vector2(100, 30);
            resetButton.GetComponentInChildren<TMP_Text>().text = "Reset";

            resetButton.GetComponent<Button>().onClick.m_PersistentCalls.Clear();
            resetButton.GetComponent<Button>().onClick.RemoveAllListeners();

            return transformButtonPrefab;
        }

        private static void createTransformButton(KeyValuePair<string, ConfigEntryBase> configValue, Transform parent, Vector2 pos, string sectionName) {

            var is3Axis = false;
            ConfigEntry<Quaternion> confRot;
            var configSection = configValue.Value.Definition.Section;
            if (!VHVRConfig.config.TryGetEntry(configSection, configValue.Key + "Rot", out confRot)) {
                Debug.LogError(configValue.Key + "Rot not found (in " + configSection + " section), will only read Vector3 Axis");
                is3Axis = true;
            }
            var transformButton = GameObject.Instantiate(transformButtonPrefab, parent);
            transformButton.GetComponent<RectTransform>().anchoredPosition = pos + Vector2.left * 10;

            var configComponent = transformButton.AddComponent<ConfigComponent>();
            ConfigureRow(configComponent, configValue);
            configComponent.saveAction = param => {};

            var label = transformButton.transform.Find("Label").GetComponent<TMP_Text>();
            label.text = GetDisplayName(configValue.Key);

            var setButton = transformButton.transform.Find("SetButton").GetComponent<Button>();
            if (!enableTransformButtons) {
                setButton.enabled = false;
            }
            setButton.onClick.AddListener(() => {
                // TODO: use something else (e. g. a dictionary from key to method) instead of Reflection to get the methods.
                // This can be broken easily without noticing: if the target method's name is changed,
                // This call does not show up in call hierarchy in IDE and does not throw any error at build time.
                var method = typeof(SettingCallback).GetMethod(configValue.Key);
                if (method == null)
                {
                    LogUtils.LogError("Cannot find method SettingCallback." + configValue.Key);
                    return;
                }

                if (is3Axis)
                {
                    if (!(bool)method.Invoke(
                    null,
                    new UnityAction<Vector3>[] {
                        (mPos) => {
                            configValue.Value.SetSerializedValue(String.Format(CultureInfo.InvariantCulture,
                                "{{\"x\":{0}, \"y\":{1}, \"z\":{2}}}", mPos.x, mPos.y, mPos.z));
                        }
                    }))
                    {
                        return;
                    }
                }
                else
                {
                    if (!(bool)method.Invoke(
                    null,
                    new UnityAction<Vector3, Quaternion>[] {
                        (mPos, mRot) => {
                            configValue.Value.SetSerializedValue(String.Format(CultureInfo.InvariantCulture,
                                "{{\"x\":{0}, \"y\":{1}, \"z\":{2}}}", mPos.x, mPos.y, mPos.z));
                            confRot.SetSerializedValue(String.Format(CultureInfo.InvariantCulture,
                                "{{\"x\":{0}, \"y\":{1}, \"z\":{2}, \"w\":{3}}}", mRot.x, mRot.y, mRot.z, mRot.w));
                        }
                    }))
                    {
                        return;
                    }
                }
                doSave = false;
                GameObject.Destroy(settings);
                Menu.instance.OnClose();
            });

            // This button will just reset the position and rotation back to the default values.
            var resetButton = transformButton.transform.Find("ResetButton").GetComponent<Button>();
       
            if (!enableTransformButtons)
            {
                resetButton.enabled = false;
                return;
            }

            resetButton.onClick.AddListener(() => {
                // TODO: use something else (e. g. a dictionary from key to method) instead of Reflection to get the methods.
                // This can be broken easily without noticing: if the target method's name is changed,
                // This call does not show up in call hierarchy in IDE and does not throw any error at build time.                
                var method = typeof(SettingCallback).GetMethod(configValue.Key + "Default");
                if (method == null)
                {
                    LogUtils.LogError("Cannot find method SettingCallback." + configValue.Key + "Default");
                    return;
                }
                if (is3Axis)
                {
                    if (!(bool)method.Invoke(null,
                    new UnityAction<Vector3>[] {
                        (mPos) => {
                            configValue.Value.SetSerializedValue(String.Format(CultureInfo.InvariantCulture,
                                "{{\"x\":{0}, \"y\":{1}, \"z\":{2}}}", mPos.x, mPos.y, mPos.z));
                        }
                    }))
                    {
                        return;
                    }
                }
                else
                {
                    if (!(bool)method.Invoke(null,
                    new UnityAction<Vector3, Quaternion>[] {
                        (mPos, mRot) => {
                            configValue.Value.SetSerializedValue(String.Format(CultureInfo.InvariantCulture,
                                "{{\"x\":{0}, \"y\":{1}, \"z\":{2}}}", mPos.x, mPos.y, mPos.z));
                            confRot.SetSerializedValue(String.Format(CultureInfo.InvariantCulture,
                                "{{\"x\":{0}, \"y\":{1}, \"z\":{2}, \"w\":{3}}}", mRot.x, mRot.y, mRot.z, mRot.w));
                        }
                    }))
                    {
                        return;
                    }
                }
            });
        }

        private static GameObject createKeyBindingPrefab(GameObject vanillaPrefab)
        {            
            var keyBindingPrefab = Object.Instantiate(vanillaPrefab);
            keyBindingPrefab.GetComponentInChildren<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
            keyBindingPrefab.GetComponentInChildren<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
            keyBindingPrefab.GetComponentInChildren<RectTransform>().sizeDelta = new Vector2(100, 25);
            keyBindingPrefab.GetComponentInChildren<Button>().gameObject.SetActive(true);
            return keyBindingPrefab;
        }

        private static void createKeyBinding(KeyValuePair<string, ConfigEntryBase> configValue, Transform parent, Vector2 pos) {
            
            var keyBinding = Object.Instantiate(keyBindingPrefab, parent);


            keyBinding.GetComponent<RectTransform>().anchoredPosition = pos + Vector2.right * 125;

            var configComponent = keyBinding.AddComponent<ConfigComponent>();
            ConfigureRow(configComponent, configValue);
            configComponent.saveAction = param => {
                configValue.Value.SetSerializedValue(param);
            };
            configComponent.usesDeferredSaveAction = true;

            keyBinding.transform.Find("Label").GetComponent<TMP_Text>().text = GetDisplayName(configValue.Key);
            keyboardMouseSettings.m_keys.Add(new KeySetting {m_keyName = configValue.Key, m_keyTransform = keyBinding.GetComponent<RectTransform>()});
            keyBinding.GetComponentInChildren<Button>().onClick.AddListener(() => {
                keyboardMouseSettings.OnOkAsync(null);
                tmpComfigComponent = configComponent;
            });
            // TODO: create a proper key binding UI prefab instead of adjusting the position here.
            if (ZInput.instance.m_buttons.ContainsKey(configValue.Key))
            {
                ZInput.instance.m_buttons.Remove(configValue.Key);
            }
            ZInput.instance.AddButton(configValue.Key, ZInput.KeyCodeToPath((KeyCode)Enum.Parse(typeof(KeyCode), configValue.Value.GetSerializedValue())));
        }

        private static void CaptureScreenshot()
        {
            string dir = new Regex("[\\/]valheim_Data$", RegexOptions.IgnoreCase).Replace(Application.dataPath, "") + "/VHVRScreenshots";
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string path = dir + "/vhvr_screenshot_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
            LogUtils.LogDebug("Saving screenshot to " + path);
            ScreenCapture.CaptureScreenshot(path);
        }

        private static void ToggleAutoPickup()
        {
            ZInput_GetButtonDown_Patch.EmulateButtonDown("JoyMenu");
            ZInput_GetButtonDown_Patch.EmulateButtonDown("AutoPickup");
        }

        private static int mod(int x, int m) {
            return (x%m + m)%m;
        }

        private class InitialTabActivator : MonoBehaviour
        {
            private TabHandler tabHandler;
            private GameObject firstPage;

            public void Initialize(TabHandler handler, GameObject page)
            {
                tabHandler = handler;
                firstPage = page;
            }

            private System.Collections.IEnumerator Start()
            {
                yield return null;
                if (tabHandler != null)
                {
                    tabHandler.SetActiveTab(0);
                }
                if (firstPage != null)
                {
                    firstPage.SetActive(true);
                }
                Object.Destroy(this);
            }
        }

        public static void updateBindings() {
            
            if (tmpComfigComponent == null) {
                return;
            }
            
            foreach (KeySetting key in keyboardMouseSettings.m_keys) {
                if (key.m_keyName == tmpComfigComponent.configValue.Key) {
                    var buttons = AccessTools.FieldRefAccess<ZInput, Dictionary<string, ZInput.ButtonDef>>(ZInput.instance, "m_buttons");
                    ZInput.ButtonDef buttonDef;
                    buttons.TryGetValue(key.m_keyName, out buttonDef);
                    tmpComfigComponent.value = buttonDef.ButtonAction.bindings[0].path;
                }
            }
            
            tmpComfigComponent = null;
        }

        private class SettingsCloneMarker : MonoBehaviour { }
    }
}
