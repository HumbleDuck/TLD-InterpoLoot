using HarmonyLib;
using Il2Cpp;
using Il2CppTLD.Gear;
using MelonLoader;
using System;
using UnityEngine;

namespace InterpoLoot
{
    public static class PatchHelper
    {
        // Identifies any object belonging to a cooking station, no matter how weird the hierarchy is
        public static bool IsCookingStation(GameObject obj)
        {
            if (obj == null) return false;

            if (obj.GetComponent<Il2Cpp.GearPlacePoint>() != null) return true;
            if (obj.GetComponentInParent<Il2Cpp.GearPlacePoint>() != null) return true;

            Transform curr = obj.transform;
            int depth = 0;
            while (curr != null && depth < 4)
            {
                if (curr.GetComponentInChildren<Il2Cpp.Fire>() != null ||
                    curr.GetComponentInChildren<Il2Cpp.WoodStove>() != null ||
                    curr.GetComponentInChildren<Il2Cpp.Campfire>() != null)
                {
                    return true;
                }
                curr = curr.parent;
                depth++;
            }
            return false;
        }

        // Mathematically maps a physical mesh hit to the closest hidden slot for Inspect Mode snapping
        public static GameObject GetExactSlotUnderCrosshair(GameObject originalResult, Vector3 hitPoint)
        {
            Transform curr = originalResult.transform;
            Transform stationRoot = curr;
            int depth = 0;

            // Climb to the root interactive body of the stove
            while (curr != null && depth < 5)
            {
                if (curr.GetComponentInChildren<Il2Cpp.Fire>() != null ||
                    curr.GetComponentsInChildren<Il2Cpp.GearPlacePoint>(true).Length > 0)
                {
                    stationRoot = curr;
                    break;
                }
                curr = curr.parent;
                depth++;
            }

            var allSlots = stationRoot.GetComponentsInChildren<Il2Cpp.GearPlacePoint>(true);
            if (allSlots.Length == 0) return originalResult;

            Il2Cpp.GearPlacePoint closestSlot = null;
            float minDistance = 0.45f; // Tight radius to ensure we only substitute if actually aiming near the burner

            foreach (var slot in allSlots)
            {
                float dist = Vector3.Distance(hitPoint, slot.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestSlot = slot;
                }
            }

            return closestSlot != null ? closestSlot.gameObject : originalResult;
        }
    }

    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.InteractiveObjectsProcessInteraction))]
    public class PlayerManager_InteractiveObjectsProcessInteraction
    {
        public static bool Prefix(PlayerManager __instance, ref bool __result)
        {
            if (InterpoLootMain.isAnimatingPlacement) return true;
            if (Settings.options.VanillaLooseItemInteractions) return true;

            GameObject crosshairObj = __instance.GetInteractiveObjectUnderCrosshairs(InterpoLootMain.vanillaInteractRange);
            if (crosshairObj == null) return true;

            // Never block clicks on ANY part of a stove/fire. This allows vanilla to 
            // natively pinpoint the exact burner you clicked to open the Action Picker on!
            if (PatchHelper.IsCookingStation(crosshairObj))
            {
                return true;
            }

            GearItem gearItem = crosshairObj.GetComponent<GearItem>();
            if (gearItem == null) gearItem = crosshairObj.GetComponentInParent<GearItem>();

            if (gearItem != null)
            {
                var pot = gearItem.GetComponent<Il2Cpp.CookingPotItem>();
                if (pot == null) pot = gearItem.GetComponentInParent<Il2Cpp.CookingPotItem>();

                if (pot != null && (pot.m_GearPlacePointAttachedTo != null || pot.m_FireBeingUsed != null))
                {
                    return true;
                }

                if (InterpoLootMain.ShouldLetVanillaHandleInteraction(gearItem))
                {
                    return true;
                }

                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.GearPlacePoint), nameof(Il2Cpp.GearPlacePoint.DropAndPlaceItem))]
    internal class GearPlacePoint_DropAndPlaceItem_Patch
    {
        public static bool isRedirecting = false;

        // Snapshot: was the item sealed BEFORE vanilla had a chance to open it?
        private static bool s_NewItemWasSealed = false;

        private static bool Prefix(Il2Cpp.GearPlacePoint __instance, GearItem newPlacedItem)
        {
            // --- Sealed-can snapshot ---
            s_NewItemWasSealed = false;
            if (newPlacedItem != null)
            {
                foreach (Transform t in newPlacedItem.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "OBJ_CannedFood")
                    {
                        s_NewItemWasSealed = t.gameObject.activeSelf;
                        break;
                    }
                }
            }

            if (isRedirecting) return true;

            var hoveredSlot = PlayerManager_GetInteractiveObjectUnderCrosshairs_Patch.LastHoveredSlot;

            // If vanilla is trying to place on a slot, but we were aiming at a DIFFERENT slot...
            if (hoveredSlot != null && hoveredSlot != __instance)
            {
                // Make sure both slots belong to the same stove to prevent teleporting items across the room!
                if (__instance.transform.root == hoveredSlot.transform.root)
                {
                    // Verify the slot we actually wanted is empty
                    if (hoveredSlot.m_PlacedGear == null)
                    {
                        // REDIRECT!
                        isRedirecting = true;
                        hoveredSlot.DropAndPlaceItem(newPlacedItem);
                        isRedirecting = false;

                        // Abort the wrong sequential placement
                        return false;
                    }
                }
            }
            return true;
        }

        private static void Postfix(Il2Cpp.GearPlacePoint __instance, GearItem newPlacedItem)
        {
            GearItem placedGear = __instance.m_PlacedGear;
            if (placedGear == null || newPlacedItem == null || InterpoLootMain.isAnimatingPlacement) return;

            // Did we place exactly what we thought? (e.g. a normal Cooking Pot)
            bool isDirectHit = (placedGear == newPlacedItem);

            // Or did vanilla spawn a dummy pot and wrap our item inside it? (e.g. Potatoes, Meat)
            bool isDummyHit = (placedGear.name.Contains("Dummy") && newPlacedItem.transform.IsChildOf(placedGear.transform));

            if (isDirectHit)
            {
                InterpoLootMain.lastPlacedGearItem = placedGear; // Cache the actual root object on the stove
                GameAudioManager.PlaySound("Play_ClothRustle", GameManager.GetPlayerObject());
                // Direct-hit is always a pot or non-food — no sealed-can logic needed.
                InterpoLootMain.AnimateItemToSlot(placedGear.gameObject, __instance.transform.position, __instance.transform.rotation, false);
            }
            else if (isDummyHit)
            {
                InterpoLootMain.lastPlacedGearItem = placedGear; // Cache the dummy as the core object
                GameAudioManager.PlaySound("Play_ClothRustle", GameManager.GetPlayerObject());
                newPlacedItem.PlayPickUpClip(); // FIX: raw food "pat" sound

                // The magic trick: animate the FOOD (newPlacedItem), not the DUMMY!
                // Pass the pre-vanilla sealed state so the clone shows the right mesh.
                InterpoLootMain.AnimateItemToSlot(newPlacedItem.gameObject, newPlacedItem.transform.position, newPlacedItem.transform.rotation, s_NewItemWasSealed);
            }
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
                    if (PatchHelper.IsCookingStation(crosshairObj))
                    {
                        return true;
                    }

                    GearItem gearItem = crosshairObj.GetComponent<GearItem>();
                    if (gearItem == null) gearItem = crosshairObj.GetComponentInParent<GearItem>();

                    if (gearItem != null)
                    {
                        var pot = gearItem.GetComponent<Il2Cpp.CookingPotItem>();
                        if (pot == null) pot = gearItem.GetComponentInParent<Il2Cpp.CookingPotItem>();

                        if (pot != null && (pot.m_GearPlacePointAttachedTo != null || pot.m_FireBeingUsed != null))
                        {
                            return true;
                        }

                        if (InterpoLootMain.ShouldLetVanillaHandleInteraction(gearItem))
                        {
                            return true;
                        }

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
        public static Il2Cpp.GearPlacePoint LastHoveredSlot = null;

        private static void Prefix(float __0)
        {
            if (__0 != InterpoLootMain.vanillaInteractRange && __0 < 10f) InterpoLootMain.vanillaInteractRange = __0;
        }

        private static void Postfix(PlayerManager __instance, ref GameObject __result)
        {
            if (InterpoLootMain.isAnimatingPlacement) return;

            // --- 1. INSPECT MODE (Placing items directly from hands) ---
            if (__instance.IsInspectModeActive() && !InterpoLootMain.isBuggedVanillaInspect)
            {
                if (__result != null)
                {
                    if (__result.GetComponent<Il2Cpp.GearPlacePoint>() != null ||
                        __result.GetComponentInParent<Il2Cpp.GearPlacePoint>() != null)
                    {
                        return;
                    }

                    if (PatchHelper.IsCookingStation(__result))
                    {
                        Transform cam = GameManager.GetMainCamera().transform;
                        if (UnityEngine.Physics.Raycast(cam.position, cam.forward, out UnityEngine.RaycastHit hit, InterpoLootMain.vanillaInteractRange + 1f))
                        {
                            GameObject exactSlot = PatchHelper.GetExactSlotUnderCrosshair(__result, hit.point);
                            if (exactSlot.GetComponent<Il2Cpp.GearPlacePoint>() != null)
                            {
                                __result = exactSlot;
                                return;
                            }
                        }
                    }

                    __result = null;
                }
                return;
            }

            // --- 2. NORMAL MODE (Hovering, opening Action Picker) ---
            bool isOverlay = InterfaceManager.IsOverlayActiveImmediate();
            bool isNormal = (__instance.GetControlMode() == PlayerControlMode.Normal || __instance.GetControlMode() == PlayerControlMode.InVehicle);

            if (!isNormal || isOverlay) return;

            LastHoveredSlot = null; // Clear it every frame when freely walking around
            if (__result != null && PatchHelper.IsCookingStation(__result))
            {
                Transform cam = GameManager.GetMainCamera().transform;
                if (UnityEngine.Physics.Raycast(cam.position, cam.forward, out UnityEngine.RaycastHit hit, InterpoLootMain.vanillaInteractRange + 1f))
                {
                    GameObject exactSlotObj = PatchHelper.GetExactSlotUnderCrosshair(__result, hit.point);
                    LastHoveredSlot = exactSlotObj.GetComponent<Il2Cpp.GearPlacePoint>();
                }
            }

            if (__result != null)
            {
                GearItem inspectedGear = __instance.GearItemBeingInspected();
                if (inspectedGear != null && (__result == inspectedGear.gameObject || __result.transform.IsChildOf(inspectedGear.transform)))
                {
                    var stalePot = inspectedGear.GetComponent<Il2Cpp.CookingPotItem>();
                    if (stalePot == null) stalePot = inspectedGear.GetComponentInParent<Il2Cpp.CookingPotItem>();

                    bool potIsOnStove = stalePot != null &&
                                        (stalePot.m_GearPlacePointAttachedTo != null || stalePot.m_FireBeingUsed != null);
                    if (!potIsOnStove)
                    {
                        __result = null;
                        return;
                    }
                }

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
                        InterpoLootMain.lastInspectedItemOriginalPosition = hitGear.transform.position;
                        InterpoLootMain.lastInspectedItemOriginalRotation = hitGear.transform.rotation;
                    }
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
            return true;
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
            return true;
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

            if (InterfaceManager.TryGetPanel<Panel_Inventory>(out var inv) && inv.IsEnabled())
            {
                inv.Enable(false);
            }

            if (InterpoLootMain.isEatingFromCookingSlot)
            {
                Vector3 startPos = InterpoLootMain.lastInspectedItemOriginalPosition;
                UnityEngine.Quaternion startRot = InterpoLootMain.lastInspectedItemOriginalRotation;

                InterpoLootMain.StartQuickLootAnimation(__0.gameObject, __0, () => {
                    Vector3 origScale = __0.transform.localScale;
                    __0.transform.localScale = Vector3.zero;

                    var pot = __0.GetComponent<Il2Cpp.CookingPotItem>();
                    if (pot == null) pot = __0.GetComponentInParent<Il2Cpp.CookingPotItem>();

                    if (pot != null && pot.m_GearPlacePointAttachedTo != null)
                    {
                        InterpoLootMain.isTakingItem = true;
                        __instance.ProcessPickupItemInteraction(__0, false, false, false);
                        __instance.ResetPickup();
                        InterpoLootMain.isTakingItem = false;
                    }

                    InterpoLootMain.isEatingFromInspect = true;
                    __instance.UseInventoryItem(__0, false);
                    InterpoLootMain.isEatingFromInspect = false;

                    if (__0 != null) __0.transform.localScale = origScale;
                }, startPos, InterpoLootMain.lastInspectedItemOriginalScale, null, startRot);
                return false;
            }

            InterpoLootMain.StartRadialConsumptionAnimation(__0, false);
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
                if (InterfaceManager.TryGetPanel<Panel_Inventory>(out var inv) && inv.IsEnabled())
                {
                    inv.Enable(false);
                }

                if (InterpoLootMain.isEatingFromCookingSlot)
                {
                    Vector3 startPos = InterpoLootMain.lastInspectedItemOriginalPosition;
                    UnityEngine.Quaternion startRot = InterpoLootMain.lastInspectedItemOriginalRotation;

                    InterpoLootMain.StartQuickLootAnimation(gearItem.gameObject, gearItem, () => {
                        Vector3 origScale = gearItem.transform.localScale;
                        gearItem.transform.localScale = Vector3.zero;

                        var pot = gearItem.GetComponent<Il2Cpp.CookingPotItem>();
                        if (pot == null) pot = gearItem.GetComponentInParent<Il2Cpp.CookingPotItem>();

                        if (pot != null && pot.m_GearPlacePointAttachedTo != null)
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

                InterpoLootMain.StartRadialConsumptionAnimation(gearItem, false, null, prefabName);
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

            if (gear != null)
            {
                // ALWAYS flush the stuck reflection variables, regardless of whether the 
                // item was deactivated by being pulled into the inventory or not!
                try
                {
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
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

                    var fields = typeof(PlayerManager).GetFields(flags);
                    foreach (var field in fields)
                    {
                        if (field.FieldType == typeof(GearItem))
                        {
                            var val = field.GetValue(__instance) as GearItem;
                            if (val != null && val == gear) field.SetValue(__instance, null);
                        }
                        else if (field.FieldType == typeof(UnityEngine.GameObject))
                        {
                            var val = field.GetValue(__instance) as UnityEngine.GameObject;
                            if (val != null && val == gear.gameObject) field.SetValue(__instance, null);
                        }
                    }

                    var properties = typeof(PlayerManager).GetProperties(flags);
                    foreach (var prop in properties)
                    {
                        if (!prop.CanRead || !prop.CanWrite) continue;
                        if (prop.PropertyType == typeof(GearItem))
                        {
                            var val = prop.GetValue(__instance) as GearItem;
                            if (val != null && val == gear) prop.SetValue(__instance, null);
                        }
                        else if (prop.PropertyType == typeof(UnityEngine.GameObject))
                        {
                            var val = prop.GetValue(__instance) as UnityEngine.GameObject;
                            if (val != null && val == gear.gameObject) prop.SetValue(__instance, null);
                        }
                    }
                }
                catch (System.Exception) { }

                // Only attempt to restore physics colliders and layers if the item is still alive in the world
                if (gear.gameObject.activeInHierarchy)
                {
                    var pot = gear.GetComponent<Il2Cpp.CookingPotItem>();
                    if (pot == null) pot = gear.GetComponentInParent<Il2Cpp.CookingPotItem>();

                    bool isAttachedToStove = (pot != null && pot.m_GearPlacePointAttachedTo != null);

                    if (!isAttachedToStove)
                    {
                        if (!InterpoLootMain.isInspectingHarvestable)
                        {
                            InterpoLootMain.isBuggedVanillaInspect = true;
                        }

                        if (!InterpoLootMain.isInspectingHarvestable && !InterpoLootMain.isInspectingCookingPot)
                        {
                            if (InterpoLootMain.lastInspectedItemOriginalPosition != UnityEngine.Vector3.zero)
                            {
                                gear.transform.position = InterpoLootMain.lastInspectedItemOriginalPosition;
                                gear.transform.rotation = InterpoLootMain.lastInspectedItemOriginalRotation;
                                gear.transform.localScale = InterpoLootMain.lastInspectedItemOriginalScale;
                            }
                        }

                        if (!InterpoLootMain.isInspectingCookingPot)
                        {
                            UnityEngine.Collider rootCol = gear.GetComponent<UnityEngine.Collider>();
                            if (rootCol != null) rootCol.enabled = true;

                            foreach (UnityEngine.Collider col in gear.GetComponentsInChildren<UnityEngine.Collider>())
                            {
                                if (!col.isTrigger)
                                    col.enabled = true;
                            }
                            gear.gameObject.layer = 17;
                        }
                        else
                        {
                            UnityEngine.Collider mainCol = gear.GetComponent<UnityEngine.Collider>();
                            if (mainCol != null) mainCol.enabled = true;
                        }
                    }
                }
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
                    return true;
                }

                Vector3 startPos = InterpoLootMain.lastInspectedItemOriginalPosition;
                UnityEngine.Quaternion startRot = InterpoLootMain.lastInspectedItemOriginalRotation;

                InterpoLootMain.StartQuickLootAnimation(gear.gameObject, null, null, startPos, null, null, startRot, null, true);

                InterpoLootMain.isEatingFromInspect = true;
            }

            return true;
        }

        private static void Postfix()
        {
            InterpoLootMain.isEatingFromInspect = false;
        }
    }

    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.RestoreTransformFromLastInspection))]
    internal class PlayerManager_RestoreTransformFromLastInspection_Patch
    {
        private static bool Prefix(GearItem __0)
        {
            if (__0 != null && InterpoLootMain.interpolatingItems.Contains(__0.gameObject))
            {
                return false;
            }
            return true;
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
                Vector3 startPos = GetStoveOrRaycastPos(__instance);
                UnityEngine.Quaternion startRot = gear.transform.rotation;

                string prefabName = gear.name.Replace("(Clone)", "").Trim();
                Il2Cpp.GearItem prefab = Il2Cpp.GearItem.LoadGearItemPrefab(prefabName);
                Vector3 targetScale = prefab != null ? prefab.transform.localScale : gear.transform.localScale;

                // Pass TRUE for bypassHashSet so it never gets permanently stuck as a ghost item!
                InterpoLootMain.StartQuickLootAnimation(gear.gameObject, null, null, startPos, gear.transform.localScale, targetScale, startRot, null, true);

                InterpoLootMain.isTakingItem = true;
            }
            else
            {
                GearItem waterPrefab = Il2Cpp.GearItem.LoadGearItemPrefab("GEAR_Water500ml");
                if (waterPrefab != null)
                {
                    Vector3 startPos = GetStoveOrRaycastPos(__instance);
                    UnityEngine.Quaternion startRot = __instance.transform.rotation;

                    // Pass TRUE for bypassHashSet
                    InterpoLootMain.StartQuickLootAnimation(waterPrefab.gameObject, null, null, startPos, waterPrefab.transform.localScale, null, startRot, null, true);

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
                Vector3 startPos = GetStoveOrRaycastPos(gi.m_CookingPotItem);
                UnityEngine.Quaternion startRot = gi.transform.rotation;

                InterpoLootMain.StartQuickLootAnimation(gi.gameObject, gi, () => { }, startPos, gi.transform.localScale, null, startRot);
            }
            else if (gi.name.ToLower().Contains("travois"))
            {
                Vector3 startPos = GetStoveOrRaycastPos(null);

                InterpoLootMain.StartQuickLootAnimation(gi.gameObject, gi, () => { }, startPos, gi.transform.localScale, null, UnityEngine.Quaternion.identity);
            }
            else if (InterfaceManager.TryGetPanel<Panel_Container>(out var p) && p.isActiveAndEnabled)
            {
                UnityEngine.Vector3 camFwd = GameManager.GetMainCamera().transform.forward;

                Vector3 containerPos = InterpoLootMain.lastCrosshairHitPosition != Vector3.zero
                    ? InterpoLootMain.lastCrosshairHitPosition
                    : GameManager.GetMainCamera().transform.position + (camFwd * 1.5f);

                Vector3 startPos = containerPos + (camFwd * 0.5f);

                InterpoLootMain.StartQuickLootAnimation(gi.gameObject, null, null, startPos, gi.transform.localScale, null, gi.transform.rotation, null, true);
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

            // Wrap in try/finally so isBreakingDown can never get stuck true on exception.
            try
            {
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
                        yieldPrefabToClone = yieldObj.GetComponent<GearItem>();
                }
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[InterpoLoot] BreakDown Prefix error: {ex.Message}");
                isBreakingDown = false; // ensure we don't silence pickup sounds permanently
                yieldPrefabToClone = null;
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

    public static class TransferManager
    {
        public static GearItem pendingItem = null;
        public static bool isStashing = false;
    }

    [HarmonyPatch(typeof(Il2Cpp.Panel_Container), nameof(Il2Cpp.Panel_Container.OnContainerToInventory))]
    internal class Panel_Container_OnContainerToInventory_Patch
    {
        private static void Prefix(Il2Cpp.Panel_Container __instance)
        {
            var selectedItem = __instance.GetCurrentlySelectedItem();
            TransferManager.pendingItem = selectedItem?.m_GearItem;
            TransferManager.isStashing = false;
        }

        private static void Postfix()
        {
            if (TransferManager.pendingItem == null) return;

            if (InterfaceManager.TryGetPanel<Panel_PickUnits>(out var pu) && pu.isActiveAndEnabled) return;

            UnityEngine.Vector3 camFwd = GameManager.GetMainCamera().transform.forward;
            Vector3 containerPos = InterpoLootMain.lastCrosshairHitPosition != Vector3.zero
                ? InterpoLootMain.lastCrosshairHitPosition
                : GameManager.GetMainCamera().transform.position + (camFwd * 1.5f);
            Vector3 startPos = containerPos + (camFwd * 0.5f);

            InterpoLootMain.StartQuickLootAnimation(TransferManager.pendingItem.gameObject, null, null, startPos, TransferManager.pendingItem.transform.localScale, null, TransferManager.pendingItem.transform.rotation, null, true);
            TransferManager.pendingItem = null;
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.Panel_Container), nameof(Il2Cpp.Panel_Container.OnInventoryToContainer))]
    internal class Panel_Container_OnInventoryToContainer_Patch
    {
        private static void Prefix(Il2Cpp.Panel_Container __instance)
        {
            var selectedItem = __instance.GetCurrentlySelectedItem();
            TransferManager.pendingItem = selectedItem?.m_GearItem;
            TransferManager.isStashing = true;
        }

        private static void Postfix()
        {
            if (TransferManager.pendingItem == null) return;

            if (InterfaceManager.TryGetPanel<Panel_PickUnits>(out var pu) && pu.isActiveAndEnabled) return;

            UnityEngine.Vector3 camFwd = GameManager.GetMainCamera().transform.forward;
            Vector3 startPos = InterpoLootMain.GetPocketPosition();
            Vector3 containerPos = InterpoLootMain.lastCrosshairHitPosition != Vector3.zero
                ? InterpoLootMain.lastCrosshairHitPosition
                : GameManager.GetMainCamera().transform.position + (camFwd * 1.5f);
            Vector3 targetPos = containerPos + (camFwd * 0.5f);

            InterpoLootMain.StartQuickLootAnimation(TransferManager.pendingItem.gameObject, null, null, startPos, TransferManager.pendingItem.transform.localScale, null, TransferManager.pendingItem.transform.rotation, targetPos, true);
            TransferManager.pendingItem = null;
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.Panel_PickUnits), nameof(Il2Cpp.Panel_PickUnits.OnExecute))]
    internal class Panel_PickUnits_OnExecute_Patch
    {
        private static void Prefix(Il2Cpp.Panel_PickUnits __instance)
        {
            if (__instance.m_numUnits > 0 && TransferManager.pendingItem != null)
            {
                GearItem itemToTransfer = TransferManager.pendingItem;
                bool wasStashing = TransferManager.isStashing;

                UnityEngine.Vector3 camFwd = GameManager.GetMainCamera().transform.forward;
                Vector3 pocketPos = InterpoLootMain.GetPocketPosition();
                Vector3 containerPos = InterpoLootMain.lastCrosshairHitPosition != Vector3.zero
                    ? InterpoLootMain.lastCrosshairHitPosition
                    : GameManager.GetMainCamera().transform.position + (camFwd * 1.5f);
                Vector3 frontOfContainer = containerPos + (camFwd * 0.5f);

                if (wasStashing)
                {
                    InterpoLootMain.StartQuickLootAnimation(itemToTransfer.gameObject, null, null, pocketPos, itemToTransfer.transform.localScale, null, itemToTransfer.transform.rotation, frontOfContainer, true);
                }
                else
                {
                    InterpoLootMain.StartQuickLootAnimation(itemToTransfer.gameObject, null, null, frontOfContainer, itemToTransfer.transform.localScale, null, itemToTransfer.transform.rotation, null, true);
                }

                TransferManager.pendingItem = null;
            }
        }
    }

    // --- REPLACES CookingPotItem_StartCooking_Patch ---
    // Safely intercepts player-initiated cooking UI actions to animate food items dropped into existing pots.
    [HarmonyPatch(typeof(Il2Cpp.Panel_Cooking), nameof(Il2Cpp.Panel_Cooking.OnDoAction))]
    internal class Panel_Cooking_OnDoAction_Patch
    {
        private static GearItem s_PreviousFood = null;

        private static void Prefix(Il2Cpp.Panel_Cooking __instance)
        {
            s_PreviousFood = null;
            if (__instance.m_CookingPotInteractedWith != null)
            {
                // Snapshot the state of the pot BEFORE the UI action executes
                s_PreviousFood = __instance.m_CookingPotInteractedWith.m_GearItemBeingCooked;
            }
        }

        private static void Postfix(Il2Cpp.Panel_Cooking __instance)
        {
            if (__instance.m_CookingPotInteractedWith == null) return;
            if (InterpoLootMain.isAnimatingPlacement || InterpoLootMain.isAnimatingCookingPotPlacement) return;

            GearItem newFood = __instance.m_CookingPotInteractedWith.m_GearItemBeingCooked;

            // If a NEW food item was just successfully placed into the pot by clicking the UI button!
            if (newFood != null && newFood != s_PreviousFood)
            {
                // User requested to completely skip canned foods when added to an existing pot/skillet.
                bool isCan = false;
                foreach (Transform t in newFood.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "OBJ_CannedFood") { isCan = true; break; }
                }
                if (isCan) return;

                // Tactile sound feedback
                newFood.PlayPickUpClip();
                GameAudioManager.PlaySound("Play_ClothRustle", GameManager.GetPlayerObject());

                // Fly a clone of the FOOD ITEM from the pocket into the pot!
                Vector3 startPos = InterpoLootMain.GetPocketPosition();
                Vector3 finalPos = newFood.transform.position;
                UnityEngine.Quaternion finalRot = newFood.transform.rotation;

                // forceSealed = false (raw food doesn't need canned state trickery)
                InterpoLootMain.AnimateCookingPotPlacement(newFood.gameObject, startPos, finalPos, finalRot, false);
            }
        }
    }
}