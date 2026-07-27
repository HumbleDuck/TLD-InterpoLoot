using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using System.Reflection;

namespace InterpoLoot
{
    public static class MethodSniffer
    {
        public static void PatchAll(HarmonyLib.Harmony harmony)
        {
            System.Type[] targetTypes = new System.Type[]
            {
                typeof(Il2Cpp.Panel_ActionPicker),
                typeof(Il2Cpp.Panel_Cooking)
            };

            var prefix = typeof(MethodSniffer).GetMethod(nameof(SniffPrefix), BindingFlags.Static | BindingFlags.Public);
            var harmonyMethod = new HarmonyMethod(prefix);
            int patchedCount = 0;

            foreach (var targetType in targetTypes)
            {
                foreach (var method in targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    string name = method.Name;
                    string lowerName = name.ToLower();

                    // 1. EXPLICITLY IGNORE FRAME-BY-FRAME UI CHECKS & GETTERS
                    if (name.Contains("Update") || name.StartsWith("get_") || name.StartsWith("set_") ||
                        name == "Awake" || name == "Start" || name == "LateUpdate" || name == "Initialize" ||
                        name.Contains("Fade") || name.Contains("Prep") || name.Contains("Hover") ||
                        name.Contains("Refresh") || name.Contains("Progress") || name.StartsWith("Get") || name.StartsWith("Is"))
                        continue;

                    // 2. EXPLICITLY REQUIRE INTERACTION VERBS
                    if (!lowerName.Contains("click") && !lowerName.Contains("place") &&
                        !lowerName.Contains("select") && !lowerName.Contains("action") &&
                        !lowerName.Contains("execute") && !lowerName.Contains("use"))
                        continue;

                    try
                    {
                        harmony.Patch(method, prefix: harmonyMethod);
                        patchedCount++;
                    }
                    catch { }
                }
            }
            MelonLogger.Msg($"[Method Sniffer] Tracking {patchedCount} hyper-filtered UI methods!");
        }

        public static void SniffPrefix(MethodBase __originalMethod)
        {
            try
            {
                MelonLogger.Msg($"[ROOT TRACER] UI Fired: {__originalMethod.DeclaringType.Name}::{__originalMethod.Name}");
            }
            catch { }
        }
    }
}