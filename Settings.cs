using ModSettings;
using UnityEngine;

namespace InterpoLoot
{
    internal class InterpoLootSettings : JsonModSettings
    {
        [Section("Old Habits Die Hard")]
        [Name("Always Use Vanilla Inspection Popup UI")]
        [Description("Restore vanilla item interaction behavior for loose items PLUS the new interpolated animation, in case you prefer the click-popup-click looting cycle. Remember that the inspection UI for loose items can ALWAYS be activated using the custom binding set here in Mod Settings (default is MMB)!")]
        public bool VanillaLooseItemInteractions = false;

        [Name("Disable Interpolated Consumption Animations")]
        [Description("Disable the interpolated animation when consuming items from the inventory or radial menu, in case you don't like the addition. With this mod, it takes ever so slightly more time to eat/drink, and will punt you from the inventory while you do, which might be jarring for you.")]
        public bool VanillaInventoryConsumption = false;

        [Section("Bindings")]
        [Name("Inspect Keybind")]
        [Description("Pressing this key while hovering over a loose item will enter the vanilla inspect mode (where you can rotate it, take it, or harvest it).")]
        public KeyCode InspectKey = KeyCode.Mouse1;
    }

    internal static class Settings
    {
        internal static InterpoLootSettings options = new InterpoLootSettings();

        public static void OnLoad()
        {
            options.AddToModSettings("InterpoLoot");
        }
    }
}
