using HarmonyLib;
using Il2Cpp;
using Il2CppTLD.Gear;
using UnityEngine;
using System;

namespace InterpoLoot
{
    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.InteractiveObjectsProcessInteraction))]
    public class PlayerManager_InteractiveObjectsProcessInteraction
    {
        public static bool Prefix(PlayerManager __instance, ref bool __result)
        {
            if (Settings.options.VanillaLooseItemInteractions) return true;

            GameObject crosshairObj = __instance.GetInteractiveObjectUnderCrosshairs(InterpoLootMain.vanillaInteractRange);
            if (crosshairObj == null) return true;

            GearItem gearItem = crosshairObj.GetComponent<GearItem>();
            if (gearItem == null) gearItem = crosshairObj.GetComponentInParent<GearItem>();

            if (gearItem != null)
            {
                var pot = gearItem.GetComponent<Il2Cpp.CookingPotItem>();
                if (pot == null) pot = gearItem.GetComponentInParent<Il2Cpp.CookingPotItem>();

                if (pot != null && (pot.m_GearPlacePointAttachedTo != null || pot.m_FireBeingUsed != null))
                {
                    return true; // Let vanilla handle cooking interactions
                }

                if (InterpoLootMain.ShouldLetVanillaHandleInteraction(gearItem))
                {
                    return true; // Let vanilla equip lit light sources immediately
                }

                // We handle all interaction logic (Quick Loot, Inspect, Drag) in InterpoLootMain.OnUpdate
                // Block the vanilla interaction so it doesn't conflict
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetFirePressed))]
    public class InputManager_GetFirePressed_Patch
    {
        public static bool Prefix(ref bool __result)
        {
            if (Settings.options.VanillaLooseItemInteractions || GameManager.m_IsPaused || GameManager.IsMainMenuActive()) return true;

            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            if (pm != null && pm.GetControlMode() == PlayerControlMode.Normal && !pm.IsInspectModeActive() && !InterfaceManager.IsOverlayActiveImmediate())
            {
                GameObject crosshairObj = pm.GetInteractiveObjectUnderCrosshairs(InterpoLootMain.vanillaInteractRange);
                if (crosshairObj != null)
                {
                    GearItem gearItem = crosshairObj.GetComponent<GearItem>();
                    if (gearItem == null) gearItem = crosshairObj.GetComponentInParent<GearItem>();

                    if (gearItem != null)
                    {
                        var pot = gearItem.GetComponent<Il2Cpp.CookingPotItem>();
                        if (pot == null) pot = gearItem.GetComponentInParent<Il2Cpp.CookingPotItem>();

                        if (pot != null && (pot.m_GearPlacePointAttachedTo != null || pot.m_FireBeingUsed != null))
                        {
                            return true; // Let vanilla handle cooking interactions
                        }

                        if (InterpoLootMain.ShouldLetVanillaHandleInteraction(gearItem))
                        {
                            return true; // Let vanilla equip lit light sources immediately
                        }

                        // Block Fire action if we are hovering over an item our mod handles!
                        __result = false;
                        return false;
                    }
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.GetInteractiveObjectUnderCrosshairs))]
    internal class PlayerManager_GetInteractiveObjectUnderCrosshairs_Patch
    {
        private static void Prefix(float __0)
        {
            if (__0 != InterpoLootMain.vanillaInteractRange && __0 < 10f)
            {
                InterpoLootMain.vanillaInteractRange = __0;
            }
        }

        private static void Postfix(PlayerManager __instance, ref GameObject __result)
        {
            if (__instance.IsInspectModeActive() && !InterpoLootMain.isBuggedVanillaInspect)
            {
                __result = null;
                return;
            }

            bool isOverlay = InterfaceManager.IsOverlayActiveImmediate();
            bool isNormal = (__instance.GetControlMode() == PlayerControlMode.Normal || __instance.GetControlMode() == PlayerControlMode.InVehicle);

            if (!isNormal || isOverlay)
            {
                // FREEZE tracking if in a UI or not normal.
                return;
            }

            if (__result == null)
            {
                // See if there's actually an object we're looking at but it's not being returned
                // Maybe log a raycast here? No, too spammy.
            }

            if (__result != null)
            {
                GearItem inspectedGear = __instance.GearItemBeingInspected();
                if (inspectedGear != null && (__result == inspectedGear.gameObject || __result.transform.IsChildOf(inspectedGear.transform)))
                {
                    __result = null;
                    return;
                }

                // ALSO ignore any items that are currently interpolating in a Quick Loot animation!
                GearItem hitGear = __result.GetComponent<GearItem>();
                if (hitGear != null)
                {
                    if (InterpoLootMain.interpolatingItems.Contains(hitGear.gameObject))
                    {
                        __result = null;
                        return;
                    }
                    else
                    {
                        if (Vector3.Distance(hitGear.transform.position, InterpoLootMain.lastInspectedItemOriginalPosition) > 0.01f)
                        {

                        }
                        // Continually track the TRUE world position of the item we are looking at!
                        // This guarantees we have the exact position if they click it, before the engine moves it!
                        InterpoLootMain.lastInspectedItemOriginalPosition = hitGear.transform.position;
                        InterpoLootMain.lastInspectedItemOriginalRotation = hitGear.transform.rotation;
                    }
                }

                if (Vector3.Distance(__instance.m_LocationOfLastInteractHit, InterpoLootMain.lastCrosshairHitPosition) > 0.01f)
                {

                }
                InterpoLootMain.lastCrosshairHitPosition = __instance.m_LocationOfLastInteractHit;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerManager), "OnPickupFromStandardGearItemInspection")]
    internal class PlayerManager_OnPickupFromStandardGearItemInspection_Patch
    {
        private static bool Prefix(PlayerManager __instance)
        {
            if (InterpoLootMain.isInspectingCookingPot) return true;

            GearItem gear = __instance.GearItemBeingInspected();

            if (gear != null)
            {
                Vector3 startPos = InterpoLootMain.lastInspectedItemOriginalPosition;

                UnityEngine.Quaternion startRot = InterpoLootMain.lastInspectedItemOriginalRotation;

                InterpoLootMain.StartQuickLootAnimation(gear.gameObject, gear, () => { }, startPos, InterpoLootMain.lastInspectedItemOriginalScale, null, startRot);

                InterpoLootMain.isTakingItem = true;
            }
            return true; // Let vanilla handle inventory insertion and harvest queue
        }

        private static void Postfix()
        {
            InterpoLootMain.isTakingItem = false;
        }
    }

    [HarmonyPatch(typeof(PlayerManager), "OnPickupFromContainerInspection")]
    internal class PlayerManager_OnPickupFromContainerInspection_Patch
    {
        private static bool Prefix(PlayerManager __instance)
        {
            GearItem gear = __instance.GearItemBeingInspected();
            if (gear != null)
            {
                UnityEngine.Vector3 camFwd = GameManager.GetMainCamera().transform.forward;
                Vector3 startPos = InterpoLootMain.lastInspectedItemOriginalPosition + (camFwd * 0.5f);

                InterpoLootMain.StartQuickLootAnimation(gear.gameObject, gear, () => { }, startPos, InterpoLootMain.lastInspectedItemOriginalScale, null, gear.transform.rotation);
            }
            return true; // Let vanilla handle container removal
        }
    }

    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.EnterInspectGearModeFromHarvestable))]
    internal class PlayerManager_EnterInspectGearModeFromHarvestable_Patch
    {
        public static Vector3 harvestPos = Vector3.zero;

        private static void Prefix(PlayerManager __instance, GearItem __0)
        {
            if (InterpoLootMain.lastCrosshairHitPosition != Vector3.zero)
            {
                harvestPos = InterpoLootMain.lastCrosshairHitPosition;
            }
            else
            {
                Transform cam = GameManager.GetMainCamera().transform;
                RaycastHit hit;
                if (Physics.Raycast(cam.position, cam.forward, out hit, 5f))
                {
                    harvestPos = hit.point;
                }
                else
                {
                    harvestPos = cam.position + cam.forward * 1.5f;
                }
            }

            // We place the item at the player's feet, which simulates the vanilla behavior 
            // for dropping an item from the inventory.
            if (GameManager.GetPlayerObject() != null)
            {
                InterpoLootMain.lastInspectedItemOriginalPosition = GameManager.GetPlayerObject().transform.position + Vector3.up * 0.1f;
            }
            else
            {
                InterpoLootMain.lastInspectedItemOriginalPosition = harvestPos; // Fallback
            }
        }

        private static void Postfix(PlayerManager __instance, GearItem __0)
        {
            if (__0 != null)
            {
                __0.transform.position = harvestPos; // Start interpolation from here
                InterpoLootMain.lastHarvestedItem = __0;
                InterpoLootMain.StartInspectLerpCoroutine(__0, harvestPos);
            }
        }
    }


    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.UseFoodInventoryItem), new Type[] { typeof(GearItem) })]
    internal class PlayerManager_UseFoodInventoryItem_Patch
    {
        private static bool Prefix(PlayerManager __instance, GearItem __0)
        {
            if (InterpoLootMain.isSimulatingRadialConsumption || InterpoLootMain.isEatingFromInspect || __0 == null) return true;
            if (InterpoLootMain.inspectingContainerItem) return true;
            if (InterpoLootMain.ShouldSkipRadialInterpolationDueToCollision()) return true;

            bool isDrink = __0.m_FoodItem != null && __0.m_FoodItem.m_IsDrink;
            if (!isDrink && GameManager.GetHungerComponent().GetCalorieReserves() >= GameManager.GetHungerComponent().GetAdjustedMaxReserveCalories())
                return true;
            if (isDrink && GameManager.GetThirstComponent().m_CurrentThirst <= 0.001f)
                return true;

            if (Settings.options.VanillaInventoryConsumption) return true;

            bool reopenInv = false;
            if (InterfaceManager.TryGetPanel<Panel_Inventory>(out var inv) && inv.IsEnabled())
            {
                inv.Enable(false);
                reopenInv = true;
            }

            if (InterpoLootMain.isEatingFromCookingSlot)
            {
                Vector3 startPos = InterpoLootMain.lastInspectedItemOriginalPosition;
                UnityEngine.Quaternion startRot = InterpoLootMain.lastInspectedItemOriginalRotation;

                InterpoLootMain.StartQuickLootAnimation(__0.gameObject, __0, () => {
                    Vector3 origScale = __0.transform.localScale;
                    __0.transform.localScale = Vector3.zero;

                    // Fix: If it's a cooking pot item (like an open can of beans) that is physically attached to a stove, 
                    // UseInventoryItem will get confused if called 0.5s later because the Cooking UI was closed.
                    // Instead, we manually pop it into the player's inventory right before eating it!
                    if (__0.m_CookingPotItem != null && __0.m_CookingPotItem.m_GearPlacePointAttachedTo != null)
                    {
                        InterpoLootMain.isTakingItem = true;
                        __instance.ProcessPickupItemInteraction(__0, false, false, false);
                        __instance.ResetPickup();
                        InterpoLootMain.isTakingItem = false;
                    }

                    InterpoLootMain.isEatingFromInspect = true;
                    __instance.UseInventoryItem(__0, false);
                    InterpoLootMain.isEatingFromInspect = false;

                    // Restore the scale just in case it survives as an inventory item (partial consumption)!
                    if (__0 != null) __0.transform.localScale = origScale;
                }, startPos, InterpoLootMain.lastInspectedItemOriginalScale, null, startRot);
                return false;
            }

            InterpoLootMain.StartRadialConsumptionAnimation(__0, reopenInv);
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.DrinkFromWaterSupply))]
    internal class PlayerManager_DrinkFromWaterSupply_Patch
    {
        private static bool Prefix(PlayerManager __instance, WaterSupply __0)
        {
            if (InterpoLootMain.isSimulatingRadialConsumption || InterpoLootMain.isEatingFromInspect || __0 == null) return true;
            if (InterpoLootMain.inspectingContainerItem) return true;
            if (InterpoLootMain.ShouldSkipRadialInterpolationDueToCollision()) return true;

            if (GameManager.GetThirstComponent().m_CurrentThirst <= 0.001f)
                return true;

            if (Settings.options.VanillaInventoryConsumption) return true;

            GearItem gearItem = __0.GetComponent<GearItem>();
            if (gearItem != null)
            {
                bool reopenInv = false;
                if (InterfaceManager.TryGetPanel<Panel_Inventory>(out var inv) && inv.IsEnabled())
                {
                    inv.Enable(false);
                    reopenInv = true;
                }

                if (InterpoLootMain.isEatingFromCookingSlot)
                {
                    Vector3 startPos = InterpoLootMain.lastInspectedItemOriginalPosition;
                    UnityEngine.Quaternion startRot = InterpoLootMain.lastInspectedItemOriginalRotation;

                    InterpoLootMain.StartQuickLootAnimation(gearItem.gameObject, gearItem, () => {
                        Vector3 origScale = gearItem.transform.localScale;
                        gearItem.transform.localScale = Vector3.zero;

                        if (gearItem.m_CookingPotItem != null && gearItem.m_CookingPotItem.m_GearPlacePointAttachedTo != null)
                        {
                            InterpoLootMain.isTakingItem = true;
                            __instance.ProcessPickupItemInteraction(gearItem, false, false, false);
                            __instance.ResetPickup();
                            InterpoLootMain.isTakingItem = false;
                        }

                        InterpoLootMain.isEatingFromInspect = true;
                        __instance.UseInventoryItem(gearItem, false);
                        InterpoLootMain.isEatingFromInspect = false;

                        if (gearItem != null) gearItem.transform.localScale = origScale;
                    }, startPos, InterpoLootMain.lastInspectedItemOriginalScale, null, startRot);
                    return false;
                }

                float volumeToDrink = __instance.CalculateWaterVolumeToDrink(__0.m_VolumeInLiters).ToQuantity(1f);
                string prefabName = volumeToDrink <= 0.5f ? "GEAR_Water500ml" : "GEAR_Water1000ml";

                InterpoLootMain.StartRadialConsumptionAnimation(gearItem, reopenInv, null, prefabName);
            }
            return false;
        }
    }


    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.EnterInspectGearMode), new Type[] { typeof(GearItem), typeof(Container), typeof(IceFishingHole), typeof(Harvestable), typeof(CookingPotItem) })]
    internal class PlayerManager_EnterInspectGearMode_Patch
    {
        private static void Prefix(GearItem gear, Container c, IceFishingHole hole, Harvestable h, CookingPotItem pot)
        {
            InterpoLootMain.isBuggedVanillaInspect = false;
            InterpoLootMain.isInspectingCookingPot = (pot != null);

            if (pot != null) return;

            if (gear != null)
            {
                InterpoLootMain.lastInspectedItemOriginalScale = gear.transform.localScale;


                // Lock in the absolute world position of a loose item before the engine moves it to the camera!
                if (c == null && h == null)
                {
                    InterpoLootMain.lastInspectedItemOriginalPosition = gear.transform.position;
                    InterpoLootMain.lastInspectedItemOriginalRotation = gear.transform.rotation;

                }
            }
            InterpoLootMain.inspectingContainerItem = (c != null);
            if (c != null)
            {
                if (InterpoLootMain.lastCrosshairHitPosition != UnityEngine.Vector3.zero)
                {
                    InterpoLootMain.lastInspectedItemOriginalPosition = InterpoLootMain.lastCrosshairHitPosition;

                }
                else
                {
                    InterpoLootMain.lastInspectedItemOriginalPosition = c.transform.position;

                }
                InterpoLootMain.lastInspectedItemOriginalRotation = c.transform.rotation;
            }
            else if (h != null)
            {
                if (InterpoLootMain.lastCrosshairHitPosition != UnityEngine.Vector3.zero)
                {
                    InterpoLootMain.lastInspectedItemOriginalPosition = InterpoLootMain.lastCrosshairHitPosition;
                }
                else
                {
                    UnityEngine.Transform cam = GameManager.GetMainCamera().transform;
                    UnityEngine.RaycastHit hit;
                    if (UnityEngine.Physics.Raycast(cam.position, cam.forward, out hit, 10f))
                    {
                        InterpoLootMain.lastInspectedItemOriginalPosition = hit.point;
                    }
                    else
                    {
                        InterpoLootMain.lastInspectedItemOriginalPosition = h.transform.position;
                    }
                }
                InterpoLootMain.lastInspectedItemOriginalRotation = UnityEngine.Quaternion.identity;
                InterpoLootMain.isInspectingHarvestable = true;
            }
            else
            {
                InterpoLootMain.isInspectingHarvestable = false;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.ExitInspectGearMode))]
    internal class PlayerManager_ExitInspectGearMode_Patch
    {
        private static void Prefix(PlayerManager __instance, out GearItem __state)
        {
            try { __state = __instance.GearItemBeingInspected(); } catch { __state = null; }
        }

        private static Exception Finalizer(PlayerManager __instance, GearItem __state, Exception __exception)
        {
            GearItem gear = __state;



            // If vanilla failed to clean up and restore the item (which happens if it returns early or gets stuck)
            // Note: For harvestables, vanilla intentionally leaves them active (dropped), but we still need to flush the stuck fields.
            if (gear != null && gear.gameObject.activeInHierarchy)
            {
                if (!InterpoLootMain.isInspectingHarvestable)
                {
                    InterpoLootMain.isBuggedVanillaInspect = true;
                }

                // FORCE CLEAR the stuck fields using Reflection!
                try
                {
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;

                    // Stop the coroutine (if exposed as property)
                    var routineProp = typeof(PlayerManager).GetProperty("m_InspectModeActiveCoroutine", flags);
                    if (routineProp != null)
                    {
                        var coroutine = routineProp.GetValue(__instance);
                        if (coroutine != null)
                        {
                            __instance.StopCoroutine((UnityEngine.Coroutine)coroutine);
                            routineProp.SetValue(__instance, null);
                        }
                    }

                    // Clear the inspected item fields
                    var fields = typeof(PlayerManager).GetFields(flags);
                    foreach (var prop in fields)
                    {
                        if (prop.FieldType == typeof(GearItem))
                        {
                            var val = prop.GetValue(__instance) as GearItem;
                            if (val != null && val == gear)
                            {
                                prop.SetValue(__instance, null);
                            }
                        }
                        else if (prop.FieldType == typeof(UnityEngine.GameObject))
                        {
                            var val = prop.GetValue(__instance) as UnityEngine.GameObject;
                            if (val != null && val == gear.gameObject)
                            {
                                prop.SetValue(__instance, null);
                            }
                        }
                    }

                    // Clear the inspected item properties
                    var properties = typeof(PlayerManager).GetProperties(flags);
                    foreach (var prop in properties)
                    {
                        if (!prop.CanRead || !prop.CanWrite) continue;

                        if (prop.PropertyType == typeof(GearItem))
                        {
                            var val = prop.GetValue(__instance) as GearItem;
                            if (val != null && val == gear)
                            {
                                prop.SetValue(__instance, null);
                            }
                        }
                        else if (prop.PropertyType == typeof(UnityEngine.GameObject))
                        {
                            var val = prop.GetValue(__instance) as UnityEngine.GameObject;
                            if (val != null && val == gear.gameObject)
                            {
                                prop.SetValue(__instance, null);
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {

                }

                // Force it back to its original world position if it's a loose item
                if (!InterpoLootMain.isInspectingHarvestable && !InterpoLootMain.isInspectingCookingPot)
                {
                    if (InterpoLootMain.lastInspectedItemOriginalPosition != Vector3.zero)
                    {
                        gear.transform.position = InterpoLootMain.lastInspectedItemOriginalPosition;
                        gear.transform.rotation = InterpoLootMain.lastInspectedItemOriginalRotation;
                        gear.transform.localScale = InterpoLootMain.lastInspectedItemOriginalScale;
                    }
                }

                // Force colliders back on
                if (!InterpoLootMain.isInspectingCookingPot)
                {
                    foreach (Collider col in gear.GetComponentsInChildren<Collider>())
                    {
                        col.enabled = true;
                    }
                }
                else
                {
                    // For cooking pots, indiscriminately enabling child colliders breaks the liquid/snow meshes!
                    // We only want to ensure the main collider is enabled.
                    Collider mainCol = gear.GetComponent<Collider>();
                    if (mainCol != null) mainCol.enabled = true;
                }

                gear.gameObject.layer = 17;
            }

            InterpoLootMain.inspectingContainerItem = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.OnEquipFromFoodItemInspection))]
    internal class PlayerManager_OnEquipFromFoodItemInspection_Patch
    {
        private static bool Prefix(PlayerManager __instance)
        {
            GearItem gear = __instance.GearItemBeingInspected();
            if (gear != null)
            {
                var pot = gear.GetComponent<Il2Cpp.CookingPotItem>();
                if (pot == null) pot = gear.GetComponentInParent<Il2Cpp.CookingPotItem>();

                Vector3 startPos = InterpoLootMain.lastInspectedItemOriginalPosition;
                UnityEngine.Quaternion startRot = InterpoLootMain.lastInspectedItemOriginalRotation;

                InterpoLootMain.StartQuickLootAnimation(gear.gameObject, gear, () => {
                    Vector3 origScale = gear.transform.localScale;
                    gear.transform.localScale = Vector3.zero;

                    InterpoLootMain.isTakingItem = true;
                    __instance.ProcessPickupItemInteraction(gear, false, false, false);
                    __instance.ResetPickup();
                    InterpoLootMain.isTakingItem = false;

                    InterpoLootMain.isEatingFromInspect = true;
                    __instance.UseInventoryItem(gear, false);
                    InterpoLootMain.isEatingFromInspect = false;

                    if (gear != null) gear.transform.localScale = origScale;
                }, startPos, null, null, startRot);

                InterpoLootMain.isTakingItem = true;
                __instance.ExitInspectGearMode(true);
                InterpoLootMain.isTakingItem = false;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.OnEquipFromDrinkableLiquidItemInspection))]
    internal class PlayerManager_OnEquipFromDrinkableLiquidItemInspection_Patch
    {
        private static bool Prefix(PlayerManager __instance)
        {
            GearItem gear = __instance.GearItemBeingInspected();
            if (gear != null)
            {
                var pot = gear.GetComponent<Il2Cpp.CookingPotItem>();
                if (pot == null) pot = gear.GetComponentInParent<Il2Cpp.CookingPotItem>();
                if (pot != null && (pot.m_GearPlacePointAttachedTo != null || pot.m_FireBeingUsed != null))
                {
                    return true; // Let vanilla handle 'Pass Time' for snow/water on stoves
                }
                Vector3 startPos = InterpoLootMain.lastInspectedItemOriginalPosition;
                UnityEngine.Quaternion startRot = InterpoLootMain.lastInspectedItemOriginalRotation;

                InterpoLootMain.StartQuickLootAnimation(gear.gameObject, gear, () => {
                    Vector3 origScale = gear.transform.localScale;
                    gear.transform.localScale = Vector3.zero;

                    InterpoLootMain.isTakingItem = true;
                    __instance.ProcessPickupItemInteraction(gear, false, false, false);
                    __instance.ResetPickup();
                    InterpoLootMain.isTakingItem = false;

                    InterpoLootMain.isEatingFromInspect = true;
                    if (gear.m_WaterSupply != null)
                    {
                        var method = typeof(PlayerManager).GetMethod("DrinkFromWaterSupply", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (method != null)
                        {
                            var parameters = method.GetParameters();
                            if (parameters.Length == 1)
                                method.Invoke(__instance, new object[] { gear.m_WaterSupply });
                            else if (parameters.Length == 2)
                            {
                                // Pass null or default for the second parameter (volumeAvailable)
                                var secondParamType = parameters[1].ParameterType;
                                object defaultVal = secondParamType.IsValueType ? System.Activator.CreateInstance(secondParamType) : null;
                                method.Invoke(__instance, new object[] { gear.m_WaterSupply, defaultVal });
                            }
                        }
                    }
                    else
                    {
                        __instance.UseInventoryItem(gear, false);
                    }
                    InterpoLootMain.isEatingFromInspect = false;

                    if (gear != null) gear.transform.localScale = origScale;
                }, startPos, null, null, startRot);

                InterpoLootMain.isTakingItem = true;
                __instance.ExitInspectGearMode(true);
                InterpoLootMain.isTakingItem = false;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.RestoreTransformFromLastInspection))]
    internal class PlayerManager_RestoreTransformFromLastInspection_Patch
    {
        private static bool Prefix(GearItem __0)
        {
            if (__0 != null && InterpoLootMain.interpolatingItems.Contains(__0.gameObject))
            {
                return false; // Prevent vanilla from moving our animating item!
            }
            return true;
        }

        private static void Postfix(GearItem __0)
        {
            if (__0 != null)
            {

                foreach (Collider col in __0.GetComponentsInChildren<Collider>())
                {

                }
            }
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.CookingPotItem), nameof(Il2Cpp.CookingPotItem.PickUpCookedItem))]
    internal class CookingPotItem_PickUpCookedItem_Patch
    {
        private static Vector3 GetStoveOrRaycastPos(Il2Cpp.CookingPotItem pot)
        {
            if (pot != null && pot.m_GearPlacePointAttachedTo != null)
            {
                return pot.m_GearPlacePointAttachedTo.transform.position;
            }

            Transform camTransform = GameManager.GetMainCamera().transform;

            if (pot != null)
            {
                // Temporarily disable the pot's colliders so the raycast hits the stove behind the 3D inspect UI
                Collider[] colliders = pot.GetComponentsInChildren<Collider>();
                bool[] states = new bool[colliders.Length];
                for (int i = 0; i < colliders.Length; i++) { states[i] = colliders[i].enabled; colliders[i].enabled = false; }

                Vector3 hitPos = camTransform.position + (camTransform.forward * 1.5f);
                if (Physics.Raycast(camTransform.position + (camTransform.forward * 0.3f), camTransform.forward, out RaycastHit hitInfo, 3f))
                {
                    hitPos = hitInfo.point + (camTransform.forward * 0.15f);
                }

                for (int i = 0; i < colliders.Length; i++) { colliders[i].enabled = states[i]; }
                return hitPos;
            }

            return camTransform.position + (camTransform.forward * 1.5f);
        }

        private static bool Prefix(Il2Cpp.CookingPotItem __instance)
        {
            GearItem gear = __instance.m_GearItemBeingCooked;

            if (gear != null)
            {
                // Cloning solid food (meat, potatoes)
                Vector3 startPos = GetStoveOrRaycastPos(__instance);
                UnityEngine.Quaternion startRot = gear.transform.rotation;

                string prefabName = gear.name.Replace("(Clone)", "").Trim();
                Il2Cpp.GearItem prefab = Il2Cpp.GearItem.LoadGearItemPrefab(prefabName);
                Vector3 targetScale = prefab != null ? prefab.transform.localScale : gear.transform.localScale;

                InterpoLootMain.StartQuickLootAnimation(gear.gameObject, gear, () => { }, startPos, gear.transform.localScale, targetScale, startRot);

                InterpoLootMain.isTakingItem = true;
            }
            else
            {
                // Taking liquid (water)
                GearItem waterPrefab = Il2Cpp.GearItem.LoadGearItemPrefab("GEAR_Water500ml");
                if (waterPrefab != null)
                {
                    Vector3 startPos = GetStoveOrRaycastPos(__instance);
                    UnityEngine.Quaternion startRot = __instance.transform.rotation;

                    InterpoLootMain.StartQuickLootAnimation(waterPrefab.gameObject, null, null, startPos, waterPrefab.transform.localScale, null, startRot);

                    InterpoLootMain.isTakingItem = true;
                }
            }
            return true;
        }

        private static void Postfix()
        {
            InterpoLootMain.isTakingItem = false;
        }
    }

    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.AddItemToPlayerInventory))]
    internal class PlayerManager_AddItemToPlayerInventory_Patch
    {
        private static Vector3 GetStoveOrRaycastPos(Il2Cpp.CookingPotItem pot)
        {
            if (pot != null && pot.m_GearPlacePointAttachedTo != null)
            {
                return pot.m_GearPlacePointAttachedTo.transform.position;
            }

            Transform camTransform = GameManager.GetMainCamera().transform;

            if (pot != null)
            {
                // Temporarily disable the pot's colliders so the raycast hits the stove behind the 3D inspect UI
                Collider[] colliders = pot.GetComponentsInChildren<Collider>();
                bool[] states = new bool[colliders.Length];
                for (int i = 0; i < colliders.Length; i++) { states[i] = colliders[i].enabled; colliders[i].enabled = false; }

                Vector3 hitPos = camTransform.position + (camTransform.forward * 1.5f);
                if (Physics.Raycast(camTransform.position + (camTransform.forward * 0.3f), camTransform.forward, out RaycastHit hitInfo, 3f))
                {
                    hitPos = hitInfo.point + (camTransform.forward * 0.15f);
                }

                for (int i = 0; i < colliders.Length; i++) { colliders[i].enabled = states[i]; }
                return hitPos;
            }

            return camTransform.position + (camTransform.forward * 1.5f);
        }

        private static void Prefix(PlayerManager __instance, GearItem gi)
        {
            if (gi == null || InterpoLootMain.isTakingItem || InterpoLootMain.isEatingFromCookingSlot) return;

            if (gi.m_CookingPotItem != null && gi.m_CookingPotItem.m_GearPlacePointAttachedTo != null)
            {
                // We are picking up a cooking pot (or pan/can) directly from a stove!
                Vector3 startPos = GetStoveOrRaycastPos(gi.m_CookingPotItem);
                UnityEngine.Quaternion startRot = gi.transform.rotation;

                InterpoLootMain.StartQuickLootAnimation(gi.gameObject, gi, () => { }, startPos, gi.transform.localScale, null, startRot);
            }
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.Panel_PickWater), "OnTakeWaterComplete")]
    internal class Panel_PickWater_OnTakeWaterComplete_Patch
    {
        private static void Postfix(bool success, bool playerCancel, float progress)
        {
            if (!success || playerCancel) return;

            Transform camTransform = GameManager.GetMainCamera().transform;
            Vector3 startPos = camTransform.position + (camTransform.forward * 1.5f);

            // Raycast on default layer to hit the toilet or sink
            if (Physics.Raycast(camTransform.position + (camTransform.forward * 0.3f), camTransform.forward, out RaycastHit hitInfo, 3f))
            {
                startPos = hitInfo.point + (camTransform.forward * 0.15f);
            }

            GearItem waterPrefab = Il2Cpp.GearItem.LoadGearItemPrefab("GEAR_Water500ml");
            if (waterPrefab != null)
            {
                InterpoLootMain.StartQuickLootAnimation(waterPrefab.gameObject, null, null, startPos, waterPrefab.transform.localScale, null, UnityEngine.Quaternion.identity);
            }
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.CookingPotItem), nameof(Il2Cpp.CookingPotItem.DoSpecialActionFromInspectMode))]
    internal class CookingPotItem_DoSpecialActionFromInspectMode_Patch
    {
        private static void Prefix(Il2Cpp.CookingPotItem __instance)
        {
            InterpoLootMain.isEatingFromCookingSlot = true;
            InterpoLootMain.lastInspectedItemOriginalPosition = __instance.transform.position;
            InterpoLootMain.lastInspectedItemOriginalRotation = __instance.transform.rotation;
            InterpoLootMain.lastInspectedItemOriginalScale = __instance.transform.localScale;
        }

        private static void Postfix()
        {
            InterpoLootMain.isEatingFromCookingSlot = false;
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.Fire), nameof(Il2Cpp.Fire.AddFuel))]
    internal class Fire_AddFuel_Patch
    {
        private static void Prefix(Il2Cpp.Fire __instance, GearItem fuel, bool inForge)
        {
            if (fuel != null)
            {
                string prefabName = fuel.name.Replace("(Clone)", "").Trim();
                GearItem prefab = GearItem.LoadGearItemPrefab(prefabName);

                if (prefab != null)
                {
                    prefab.PlayPickUpClip();

                    Transform cam = GameManager.GetMainCamera().transform;
                    Vector3 targetPos = cam.position + cam.forward * 1.5f;

                    InterpoLootMain.AnimateItemToFire(prefab, targetPos);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.BreakDown), nameof(Il2Cpp.BreakDown.DoBreakDown))]
    internal class BreakDown_DoBreakDown_Patch
    {
        private static Vector3 breakDownPos;
        private static GearItem yieldPrefabToClone;
        public static bool isBreakingDown = false;

        private static void Prefix(Il2Cpp.BreakDown __instance)
        {
            isBreakingDown = true;

            if (InterpoLootMain.lastCrosshairHitPosition != Vector3.zero)
            {
                breakDownPos = InterpoLootMain.lastCrosshairHitPosition;
            }
            else
            {
                breakDownPos = __instance.transform.position;
            }

            yieldPrefabToClone = null;
            if (__instance.m_YieldObject != null && __instance.m_YieldObject.Count > 0)
            {
                GameObject yieldObj = __instance.m_YieldObject[0];
                if (yieldObj != null)
                {
                    yieldPrefabToClone = yieldObj.GetComponent<GearItem>();
                }
            }
        }

        private static void Postfix(Il2Cpp.BreakDown __instance)
        {
            isBreakingDown = false;
            if (yieldPrefabToClone != null)
            {
                InterpoLootMain.SpawnSimulatedYieldClone(yieldPrefabToClone, breakDownPos);
                yieldPrefabToClone = null;
            }
        }
    }

    [HarmonyPatch(typeof(GearItem), nameof(GearItem.PlayPickUpClip))]
    internal class GearItem_PlayPickUpClip_Patch
    {
        private static bool Prefix()
        {
            if (BreakDown_DoBreakDown_Patch.isBreakingDown) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Panel_Rest), nameof(Panel_Rest.OnPickUp))]
    public class Panel_Rest_OnPickUp_Patch
    {
        public static void Prefix(Panel_Rest __instance)
        {
            var bed = __instance.m_Bed;
            if (bed != null)
            {
                var gearItem = bed.GetComponent<GearItem>();
                if (gearItem != null)
                {
                    string prefabName = gearItem.name.Replace("(Clone)", "").Trim();
                    var prefab = GearItem.LoadGearItemPrefab(prefabName);

                    if (prefab != null)
                    {
                        InterpoLootMain.StartQuickLootAnimation(prefab.gameObject, null, null, gearItem.transform.position, null, null, gearItem.transform.rotation);
                    }
                }
            }
        }
    }
}