using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using System.Reflection;
using System.Collections.Concurrent;

namespace InterpoLoot
{
    public static class MethodSniffer
    {
        private static ConcurrentDictionary<string, int> methodCallCounts = new ConcurrentDictionary<string, int>();
        private const int SpamThreshold = 60;

        public static void PatchAll(HarmonyLib.Harmony harmony)
        {
            // Instead of patching 600+ methods, we target the specific interaction entry points
            // most likely used by the Inspect UI Spacebar consumption action.
            System.Type[] targetTypes = { typeof(Il2Cpp.PlayerManager), typeof(Il2Cpp.GearItem), typeof(Il2Cpp.WaterSupply) };

            string[] suspiciousMethods = {
                "ProcessInteraction",
                "UseInventoryItem",
                "DrinkFromWaterSupply",
                "OnEquipFromDrinkableLiquidItemInspection",
                "DoSpecialActionFromInspectMode",
                "OnTakeWaterComplete"
            };

            var prefix = typeof(MethodSniffer).GetMethod(nameof(SniffPrefix), BindingFlags.Static | BindingFlags.Public);
            var harmonyMethod = new HarmonyMethod(prefix);

            int patchedCount = 0;

            foreach (var type in targetTypes)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (System.Array.IndexOf(suspiciousMethods, method.Name) >= 0)
                    {
                        try
                        {
                            harmony.Patch(method, prefix: harmonyMethod);
                            patchedCount++;
                        }
                        catch { }
                    }
                }
            }
            MelonLogger.Msg($"[Method Sniffer] Targeted {patchedCount} specific interaction methods. THREAD-SAFE logging active!");
        }

        public static void SniffPrefix(MethodBase __originalMethod)
        {
            try
            {
                string methodName = __originalMethod.Name;
                int count = methodCallCounts.AddOrUpdate(methodName, 1, (key, oldValue) => oldValue + 1);

                if (count <= SpamThreshold)
                {
                    string fullMethodName = $"{__originalMethod.DeclaringType.Name}::{methodName}";
                    MelonLogger.Msg($"[UI CLICK TRACER] Fired: {fullMethodName}");
                }

                if (count == SpamThreshold)
                {
                    MelonLogger.Msg($"[UI CLICK TRACER] [MUTED] {__originalMethod.Name} reached spam threshold.");
                }
            }
            catch { }
        }
    }
}