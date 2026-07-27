using MelonLoader;
using UnityEngine;
using Il2Cpp;
using System.Collections;
using Il2CppTLD.Gear;
using System.Linq;
using System.Reflection;
using System;
using System.Collections.Generic;

[assembly: MelonInfo(typeof(InterpoLoot.InterpoLootMain), "InterpoLoot", "1.0.0", "Author")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace InterpoLoot
{
    public class InterpoLootMain : MelonMod
    {
        public static bool isTakingItem = false;
        public static bool isEatingFromCookingSlot = false;
        public static bool isAnimatingPlacement = false;
        public static bool isInspectingHarvestable = false;
        public static bool isInspectingCookingPot = false;
        public static float vanillaInteractRange = 2f;
        public static GearItem lastPlacedGearItem = null;
        public static HashSet<GameObject> interpolatingItems = new HashSet<GameObject>();
        public static UnityEngine.Vector3 lastInspectedItemOriginalPosition = UnityEngine.Vector3.zero;
        public static UnityEngine.Quaternion lastInspectedItemOriginalRotation = UnityEngine.Quaternion.identity;
        public static UnityEngine.Vector3 lastInspectedItemOriginalScale = UnityEngine.Vector3.one;
        public static UnityEngine.Vector3 lastCrosshairHitPosition = UnityEngine.Vector3.zero;
        public static bool isPlacementModeActive = false;
        public static bool inspectingContainerItem = false;
        public static bool isBuggedVanillaInspect = false;

        public static Vector3 PocketOffset = new Vector3(0f, -0.2f, -0.4f);

        // UI Transparency Tracking
        public static bool hasScannedContainerUI = false;
        public static List<GameObject> containerBackgroundsToHide = new List<GameObject>();

        public static Vector3 GetPocketPosition()
        {
            Transform cam = GameManager.GetMainCamera().transform;
            return cam.position + (cam.up * PocketOffset.y) + (cam.forward * PocketOffset.z);
        }

        public override void OnInitializeMelon()
        {
            /* STREAMING_CHUNK:Initializing the main mod components... */
            MelonLogger.Msg("Initializing InterpoLoot...");

            MethodSniffer.PatchAll(this.HarmonyInstance);

            Settings.OnLoad();
            Time.fixedDeltaTime = 1f / 60f;

            Physics.IgnoreLayerCollision((int)vp_Layer.Gear, (int)vp_Layer.Gear, true);

            MelonLogger.Msg("InterpoLoot has loaded!");
        }

        public override void OnUpdate()
        {
            /* STREAMING_CHUNK:Running the Hierarchy Dumper debugging tool... */
            // --- DEBUG: HIERARCHY DUMPER ---
            if (Input.GetKeyDown(KeyCode.F10))
            {
                DumpCrosshairHierarchy();
            }

            if (GameManager.m_IsPaused || GameManager.IsMainMenuActive()) return;

            /* STREAMING_CHUNK:Scanning for UI transparencies... */
            // --- UI TRANSPARENCY SCANNER ---
            if (InterfaceManager.TryGetPanel<Panel_Container>(out var containerPanel))
            {
                if (!hasScannedContainerUI)
                {
                    hasScannedContainerUI = true;
                    containerBackgroundsToHide.Clear();

                    foreach (Transform child in containerPanel.gameObject.GetComponentsInChildren<Transform>(true))
                    {
                        string name = child.name.ToLower();
                        if (name.Contains("darken") || name.Contains("bgsolid") || name.Contains("vignette") || name.Contains("backgroundsolid") ||
                            ((name == "bg" || name == "background") && child.parent == containerPanel.transform))
                        {
                            containerBackgroundsToHide.Add(child.gameObject);
                        }
                    }
                }

                foreach (var bg in containerBackgroundsToHide)
                {
                    if (bg != null && bg.activeSelf)
                    {
                        bg.SetActive(false);
                    }
                }
            }
            // ------------------------------------

            /* STREAMING_CHUNK:Handling input interaction logic... */
            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            if (pm == null || (pm.GetControlMode() != PlayerControlMode.Normal && pm.GetControlMode() != PlayerControlMode.Locked && pm.GetControlMode() != PlayerControlMode.InVehicle)) return;

            if (pm.IsInspectModeActive() && !isBuggedVanillaInspect) return;
            if (InterfaceManager.IsOverlayActiveImmediate()) return;

            GameObject crosshairObj = pm.GetInteractiveObjectUnderCrosshairs(vanillaInteractRange);
            GearItem hoverItem = crosshairObj != null ? crosshairObj.GetComponent<GearItem>() : null;

            if (hoverItem != null)
            {
                var pot = hoverItem.GetComponent<Il2Cpp.CookingPotItem>();
                if (pot == null) pot = hoverItem.GetComponentInParent<Il2Cpp.CookingPotItem>();

                if (pot != null && (pot.m_GearPlacePointAttachedTo != null || pot.m_FireBeingUsed != null))
                {
                    return;
                }
            }

            if (hoverItem != null)
            {
                if (Input.GetKeyDown(Settings.options.InspectKey))
                {
                    if (!pm.IsInspectModeActive() || isBuggedVanillaInspect)
                    {
                        pm.EnterInspectGearMode(hoverItem);
                    }
                    return;
                }
            }

            if (InputManager.GetInteractPressed(pm) || Input.GetMouseButtonDown(0))
            {
                if (Settings.options.VanillaLooseItemInteractions) return;

                if (hoverItem != null && !interpolatingItems.Contains(hoverItem.gameObject))
                {
                    bool isCooking = false;
                    var pot = hoverItem.GetComponent<Il2Cpp.CookingPotItem>();
                    if (pot != null && (pot.m_GearPlacePointAttachedTo != null || pot.m_FireBeingUsed != null))
                    {
                        isCooking = true;
                    }

                    if (!isCooking && !ShouldLetVanillaHandleInteraction(hoverItem))
                    {
                        StartQuickLootAnimation(hoverItem.gameObject, hoverItem);
                    }
                }
            }
        }

        /* STREAMING_CHUNK:Defining the Hierarchy Dumper functionality... */
        private void DumpCrosshairHierarchy()
        {
            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            if (pm == null) return;

            GameObject crosshairObj = pm.GetInteractiveObjectUnderCrosshairs(vanillaInteractRange);
            if (crosshairObj == null)
            {
                MelonLogger.Msg("[Dumper] No interactive object under crosshair. Attempting raw physics raycast...");

                Transform cam = GameManager.GetMainCamera().transform;
                if (UnityEngine.Physics.Raycast(cam.position, cam.forward, out UnityEngine.RaycastHit hit, 5f))
                {
                    MelonLogger.Msg($"[Dumper] Physics Raycast hit: {hit.collider.gameObject.name}");
                    crosshairObj = hit.collider.gameObject;
                }
                else
                {
                    MelonLogger.Msg("[Dumper] Nothing hit by Raycast either.");
                    return;
                }
            }

            // Try to find the root of the stove/fire/gear item to dump the whole tree
            Transform top = crosshairObj.transform;
            Il2Cpp.Fire fire = crosshairObj.GetComponentInParent<Il2Cpp.Fire>();

            if (fire != null)
            {
                top = fire.transform;
            }
            else
            {
                GearItem gi = crosshairObj.GetComponentInParent<GearItem>();
                if (gi != null) top = gi.transform;
            }

            // Fallback: Just go up a few levels if we didn't find a component root
            if (fire == null && crosshairObj.GetComponentInParent<GearItem>() == null)
            {
                int maxClimb = 3;
                while (top.parent != null && !top.parent.name.Contains("Scene") && top.parent.name != "Root" && maxClimb > 0)
                {
                    top = top.parent;
                    maxClimb--;
                }
            }

            MelonLogger.Msg($"\n=== HIERARCHY DUMP FOR [{top.name}] ===");
            MelonLogger.Msg($"Crosshair specifically hit: {crosshairObj.name}");
            DumpTransformNode(top, 0, crosshairObj);
            MelonLogger.Msg("========================================\n");
        }

        private void DumpTransformNode(Transform node, int indentLevel, GameObject targetObj)
        {
            string indent = new string(' ', indentLevel * 2);
            string hitMarker = (node.gameObject == targetObj) ? " <----- [CROSSHAIR HIT]" : "";

            bool hasGPP = node.GetComponent<Il2Cpp.GearPlacePoint>() != null;
            bool hasFire = node.GetComponent<Il2Cpp.Fire>() != null;
            bool hasCampfire = node.GetComponent<Il2Cpp.Campfire>() != null;
            bool hasWoodStove = node.GetComponent<Il2Cpp.WoodStove>() != null;
            Collider col = node.GetComponent<Collider>();

            string tags = "";
            if (hasGPP) tags += "[GearPlacePoint] ";
            if (hasFire) tags += "[Fire] ";
            if (hasCampfire) tags += "[Campfire] ";
            if (hasWoodStove) tags += "[WoodStove] ";
            if (col != null) tags += $"[Col:{col.GetIl2CppType().Name}] ";

            string act = node.gameObject.activeInHierarchy ? "ON" : "OFF";
            string lay = $"L:{node.gameObject.layer}";

            MelonLogger.Msg($"{indent}- {node.name} ({act} | {lay}) {tags}{hitMarker}");

            for (int i = 0; i < node.childCount; i++)
            {
                DumpTransformNode(node.GetChild(i), indentLevel + 1, targetObj);
            }
        }

        /* STREAMING_CHUNK:Defining standard vanilla interactions... */
        public static bool ShouldLetVanillaHandleInteraction(GearItem gearItem)
        {
            if (gearItem == null) return false;

            var flare = gearItem.GetComponent<FlareItem>();
            if (flare != null && flare.IsBurning()) return true;

            var torch = gearItem.GetComponent<TorchItem>();
            if (torch != null && torch.IsBurning()) return true;

            var lamp = gearItem.GetComponent<KeroseneLampItem>();
            if (lamp != null && lamp.IsOn()) return true;

            var bed = gearItem.GetComponent<Bed>();
            if (bed != null) return true;

            var snare = gearItem.GetComponent<SnareItem>();
            if (snare != null && snare.m_State == SnareState.Set) return true;

            return false;
        }

        public static bool isSimulatingRadialConsumption = false;
        public static bool isEatingFromInspect = false;
        public static GearItem lastHarvestedItem = null;

        public static bool ShouldSkipRadialInterpolationDueToCollision()
        {
            UnityEngine.Transform cam = GameManager.GetMainCamera().transform;
            GameObject playerObj = GameManager.GetPlayerObject();

            UnityEngine.RaycastHit[] hits = UnityEngine.Physics.RaycastAll(cam.position, cam.forward, 1.2f, -1, UnityEngine.QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider != null && hit.collider.gameObject != null)
                {
                    if (hit.collider.gameObject != playerObj && !hit.collider.transform.IsChildOf(playerObj.transform))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /* STREAMING_CHUNK:Cloning the object mesh for interpolation... */
        public static GameObject CreateVisualClone(GameObject sourceObject, string cloneName = "VisualClone")
        {
            if (sourceObject == null) return null;
            GameObject clone = new GameObject(cloneName);
            clone.layer = 0;

            clone.transform.position = sourceObject.transform.position;
            clone.transform.rotation = sourceObject.transform.rotation;
            clone.transform.localScale = sourceObject.transform.localScale;

            ClothingItem clothing = sourceObject.GetComponent<ClothingItem>();

            foreach (MeshRenderer originalRenderer in sourceObject.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (clothing != null)
                {
                    if (originalRenderer.transform.parent != null && originalRenderer.transform.parent.name == "MeshInspectMode") continue;
                    if (originalRenderer.name == "MeshInspectMode") continue;
                }

                MeshFilter originalFilter = originalRenderer.GetComponent<MeshFilter>();
                if (originalFilter != null && originalFilter.sharedMesh != null)
                {
                    GameObject child = new GameObject(originalRenderer.gameObject.name);
                    child.transform.position = originalRenderer.transform.position;
                    child.transform.rotation = originalRenderer.transform.rotation;
                    child.transform.localScale = originalRenderer.transform.lossyScale;
                    child.transform.SetParent(clone.transform, true);

                    MeshFilter mf = child.AddComponent<MeshFilter>();
                    mf.sharedMesh = originalFilter.sharedMesh;

                    MeshRenderer mr = child.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = originalRenderer.sharedMaterials;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.enabled = originalRenderer.enabled;

                    if (clothing != null && (originalRenderer.name == "Mesh" || (originalRenderer.transform.parent != null && originalRenderer.transform.parent.name == "Mesh")))
                    {
                        child.SetActive(true);
                    }
                    else
                    {
                        bool isActuallyActive = true;
                        Transform curr = originalRenderer.transform;

                        while (curr != null && curr != sourceObject.transform)
                        {
                            if (!curr.gameObject.activeSelf)
                            {
                                isActuallyActive = false;
                                break;
                            }
                            curr = curr.parent;
                        }
                        child.SetActive(isActuallyActive);
                    }

                    child.layer = 0;
                }
            }
            return clone;
        }

        /* STREAMING_CHUNK:Managing radial consumption logic... */
        public static void StartRadialConsumptionAnimation(GearItem gearItem, bool reopenInventory = false, System.Action onComplete = null, string customPrefab = null)
        {
            MelonCoroutines.Start(RadialConsumptionCoroutine(gearItem, reopenInventory, onComplete, customPrefab));
        }

        private static Vector3 GetCenterOffset(GameObject obj)
        {
            bool hasBounds = false;
            Bounds bounds = new Bounds(obj.transform.position, Vector3.zero);

            Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);
            foreach (Collider c in colliders)
            {
                if (!c.isTrigger)
                {
                    if (!hasBounds)
                    {
                        bounds = c.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(c.bounds);
                    }
                }
            }
            if (!hasBounds)
            {
                MeshRenderer[] meshes = obj.GetComponentsInChildren<MeshRenderer>(true);
                foreach (MeshRenderer m in meshes)
                {
                    if (!hasBounds)
                    {
                        bounds = m.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(m.bounds);
                    }
                }
            }
            if (hasBounds)
            {
                return obj.transform.InverseTransformPoint(bounds.center);
            }
            return Vector3.zero;
        }

        private static IEnumerator RadialConsumptionCoroutine(GearItem gearItem, bool reopenInventory, System.Action onComplete = null, string customPrefab = null)
        {
            if (gearItem == null) yield break;

            isSimulatingRadialConsumption = true;
            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            pm.SetControlMode(PlayerControlMode.Locked);

            PlayerManager.MaybeDisableInspectModeMesh(gearItem);

            GameAudioManager.PlaySound("Play_ClothRustle", GameManager.GetPlayerObject());
            gearItem.PlayPickUpClip();

            GameObject cloneObj;
            if (!string.IsNullOrEmpty(customPrefab))
            {
                var prefab = GearItem.LoadGearItemPrefab(customPrefab);
                cloneObj = CreateVisualClone(prefab?.gameObject);
                cloneObj.transform.localScale = Vector3.one;
            }
            else
            {
                cloneObj = CreateVisualClone(gearItem.gameObject);
            }

            if (cloneObj.transform.localScale.sqrMagnitude < 0.01f)
            {
                cloneObj.transform.localScale = Vector3.one;
            }

            cloneObj.transform.parent = null;
            cloneObj.SetActive(true);
            Utils.SetObjectAndChildrenLayer(cloneObj, 2, 0);

            Transform cameraTransform = GameManager.GetMainCamera().transform;

            Vector3 startPos = cameraTransform.position + (-cameraTransform.up * 1.5f) + (cameraTransform.forward * 0.2f);
            Vector3 midPos = cameraTransform.position + (cameraTransform.forward * 1.0f);

            float duration = 0.35f;
            float time = 0f;

            bool isDrink = (gearItem.m_WaterSupply != null) || (gearItem.m_FoodItem != null && gearItem.m_FoodItem.m_IsDrink);

            if (isDrink)
            {
                cloneObj.transform.rotation = UnityEngine.Quaternion.LookRotation(cameraTransform.position - midPos) * UnityEngine.Quaternion.Euler(0, -90, 0);
            }
            else
            {
                cloneObj.transform.rotation = UnityEngine.Quaternion.LookRotation(cameraTransform.position - midPos);
                cloneObj.transform.Rotate(0, 90, 0, Space.Self);
                cloneObj.transform.RotateAround(cloneObj.transform.position, cameraTransform.right, -45f);
            }

            Vector3 localCenterOffset = GetCenterOffset(cloneObj);

            if (cloneObj.GetComponent<Rigidbody>() != null) UnityEngine.Object.Destroy(cloneObj.GetComponent<Rigidbody>());
            foreach (Collider c in cloneObj.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);

            /* STREAMING_CHUNK:Animating radial interaction phase 1... */
            while (time < duration)
            {
                if (gearItem == null || gearItem.gameObject == null) yield break;
                time += UnityEngine.Time.deltaTime;

                float t = time / duration;
                t = 1f - UnityEngine.Mathf.Pow(1f - t, 3f);

                Vector3 worldOffset = cloneObj.transform.TransformVector(localCenterOffset);
                Vector3 adjustedStartPos = startPos - worldOffset;
                Vector3 adjustedMidPos = midPos - worldOffset;

                Vector3 currentPos = UnityEngine.Vector3.Lerp(adjustedStartPos, adjustedMidPos, t);
                float arc = Mathf.Sin(t * Mathf.PI) * 0.05f;
                currentPos += -cameraTransform.up * arc;

                cloneObj.transform.position = currentPos;
                yield return null;
            }

            yield return new UnityEngine.WaitForSeconds(0.15f);

            /* STREAMING_CHUNK:Animating radial interaction phase 2... */
            Vector3 endPos = cameraTransform.position + (-cameraTransform.up * 0.50f);
            time = 0f;
            duration = 0.25f;

            while (time < duration)
            {
                if (gearItem == null || gearItem.gameObject == null) yield break;
                time += Time.deltaTime;
                float t = time / duration;

                Vector3 worldOffset = cloneObj.transform.TransformVector(localCenterOffset);
                Vector3 adjustedMidPos = midPos - worldOffset;
                Vector3 adjustedEndPos = endPos - worldOffset;

                Vector3 currentPos = Vector3.Lerp(adjustedMidPos, adjustedEndPos, t);
                float arc = Mathf.Sin(t * Mathf.PI) * 0.05f;
                currentPos += -cameraTransform.up * arc;

                cloneObj.transform.position = currentPos;
                yield return null;
            }

            UnityEngine.Object.Destroy(cloneObj);

            if (gearItem.m_FoodItem != null)
                pm.UseInventoryItem(gearItem);
            else if (gearItem.m_WaterSupply != null)
                pm.UseInventoryItem(gearItem);

            isSimulatingRadialConsumption = false;

            onComplete?.Invoke();

            GameManager.GetPlayerManagerComponent().SetControlMode(PlayerControlMode.Normal);

            if (reopenInventory)
            {
                yield return null;
                yield return null;
                yield return null;

                if (InterfaceManager.TryGetPanel<Panel_GenericProgressBar>(out var pb))
                {
                    while (pb.isActiveAndEnabled && pb.GetSliderValue() < 1f && !pb.m_MarkForCancel)
                    {
                        yield return null;
                    }
                }

                if (InterfaceManager.TryGetPanel<Panel_Inventory>(out var inv))
                {
                    inv.Enable(true);
                }
            }
        }

        /* STREAMING_CHUNK:Managing quick loot animations... */
        public static void StartQuickLootAnimation(GameObject sourceObject, GearItem originalGear = null, Action onComplete = null, Vector3? overrideStartPos = null, Vector3? startScale = null, Vector3? targetScale = null, UnityEngine.Quaternion? overrideStartRot = null, Vector3? overrideTargetPos = null, bool bypassHashSet = false)
        {
            if (sourceObject == null) return;
            MelonCoroutines.Start(QuickLootCoroutine(sourceObject, originalGear, onComplete, overrideStartPos, startScale, targetScale, overrideStartRot, overrideTargetPos, bypassHashSet));
        }

        private static IEnumerator QuickLootCoroutine(GameObject sourceObject, GearItem originalGear, Action onComplete, Vector3? overrideStartPos, Vector3? startScale, Vector3? targetScale, UnityEngine.Quaternion? overrideStartRot, Vector3? overrideTargetPos = null, bool bypassHashSet = false)
        {
            if (sourceObject == null) yield break;

            if (!bypassHashSet)
            {
                if (interpolatingItems.Contains(sourceObject)) yield break;
                interpolatingItems.Add(sourceObject);
            }

            Vector3 startPos = overrideStartPos.HasValue ? overrideStartPos.Value : sourceObject.transform.position;
            UnityEngine.Quaternion startRot = overrideStartRot.HasValue ? overrideStartRot.Value : sourceObject.transform.rotation;

            GameObject cloneObj = CreateVisualClone(sourceObject);
            if (cloneObj == null)
            {
                if (!bypassHashSet) interpolatingItems.Remove(sourceObject);
                yield break;
            }

            if (onComplete != null)
            {
                onComplete();
            }
            else if (originalGear != null)
            {
                GameManager.GetPlayerManagerComponent().ProcessPickupItemInteraction(originalGear, false, false, false);
                GameManager.GetPlayerManagerComponent().ResetPickup();
            }

            if (originalGear != null)
                originalGear.PlayPickUpClip();

            cloneObj.transform.parent = null;
            cloneObj.SetActive(true);

            Vector3 localCenterOffset = GetCenterOffset(cloneObj);

            Utils.SetObjectAndChildrenLayer(cloneObj, 2, 0);

            float duration = 0.5f;
            float time = 0f;

            cloneObj.transform.position = startPos;
            cloneObj.transform.rotation = startRot;

            Transform cameraTransform = GameManager.GetMainCamera().transform;

            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;
            foreach (MeshRenderer m in cloneObj.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!hasBounds) { bounds = m.bounds; hasBounds = true; }
                else { bounds.Encapsulate(m.bounds); }
            }
            float dynamicOffset = hasBounds ? Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) : 0f;
            dynamicOffset = Mathf.Clamp(dynamicOffset, 0f, 1.0f);

            while (time < duration)
            {
                if (cloneObj == null) yield break;
                time += Time.deltaTime;
                float t = time / duration;

                t = 1f - Mathf.Pow(1f - t, 3f);

                Vector3 targetPos;
                if (overrideTargetPos.HasValue)
                {
                    targetPos = overrideTargetPos.Value;
                }
                else
                {
                    targetPos = GetPocketPosition();
                    targetPos += cameraTransform.up * -dynamicOffset;
                }

                Vector3 worldOffset = cloneObj.transform.TransformVector(localCenterOffset);
                Vector3 adjustedTargetPos = targetPos - worldOffset;

                Vector3 initialWorldOffset = cloneObj.transform.TransformVector(localCenterOffset);
                Vector3 adjustedStartPos = startPos - initialWorldOffset;

                Vector3 currentPos = Vector3.Lerp(adjustedStartPos, adjustedTargetPos, t);

                float arc = Mathf.Sin(t * Mathf.PI) * 0.05f;
                currentPos += -cameraTransform.up * arc;

                cloneObj.transform.position = currentPos;

                if (startScale.HasValue && targetScale.HasValue)
                {
                    cloneObj.transform.localScale = Vector3.Lerp(startScale.Value, targetScale.Value, t);
                }
                else if (startScale.HasValue && !targetScale.HasValue)
                {
                    cloneObj.transform.localScale = startScale.Value;
                }

                yield return null;
            }

            UnityEngine.Object.Destroy(cloneObj);

            if (!bypassHashSet)
            {
                interpolatingItems.Remove(sourceObject);
            }
        }

        /* STREAMING_CHUNK:Managing Inspect Lerp interactions... */
        public static void StartInspectLerpCoroutine(GearItem gearItem, Vector3 startPos)
        {
            MelonCoroutines.Start(InspectLerpCoroutine(gearItem, startPos));
        }

        private static IEnumerator InspectLerpCoroutine(GearItem gearItem, Vector3 startPos)
        {
            if (gearItem == null || interpolatingItems.Contains(gearItem.gameObject)) yield break;

            interpolatingItems.Add(gearItem.gameObject);

            Vector3 localCenterOffset = GetCenterOffset(gearItem.gameObject);

            float duration = 0.3f;
            float time = 0f;
            Transform cameraTransform = GameManager.GetMainCamera().transform;

            Vector3 targetPos = cameraTransform.position + cameraTransform.forward * 1.5f;

            while (time < duration)
            {
                time += Time.deltaTime;
                float t = time / duration;
                t = 1f - Mathf.Pow(1f - t, 3f);

                Vector3 worldOffset = gearItem.transform.TransformVector(localCenterOffset);
                Vector3 adjustedTargetPos = targetPos - worldOffset;

                Vector3 currentPos = Vector3.Lerp(startPos, adjustedTargetPos, t);
                float arc = Mathf.Sin(t * Mathf.PI) * 0.05f;
                currentPos += -cameraTransform.up * arc;

                gearItem.transform.position = currentPos;
                yield return null;
            }

            interpolatingItems.Remove(gearItem.gameObject);
        }

        /* STREAMING_CHUNK:Managing slot placement animations... */
        public static void AnimateItemToSlot(GameObject originalObj, Vector3 finalPos, UnityEngine.Quaternion finalRot)
        {
            if (originalObj == null || GameManager.GetMainCamera() == null) return;
            InterpoLootMain.isAnimatingPlacement = true;

            Transform camTransform = GameManager.GetMainCamera().transform;
            Vector3 startPos = InterpoLootMain.GetPocketPosition();
            UnityEngine.Quaternion startRot = camTransform.rotation;

            GearItem gear = originalObj.GetComponent<GearItem>();
            GameObject clone = CreateVisualClone(gear != null ? gear.gameObject : originalObj, "PlacementVisualClone");

            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;
            foreach (MeshRenderer m in clone.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!hasBounds) { bounds = m.bounds; hasBounds = true; }
                else { bounds.Encapsulate(m.bounds); }
            }
            float dynamicOffset = hasBounds ? Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) : 0f;
            dynamicOffset = Mathf.Clamp(dynamicOffset, 0f, 1.0f);

            startPos += camTransform.up * -dynamicOffset;

            clone.transform.position = startPos;
            clone.transform.rotation = startRot;
            clone.SetActive(true);

            MelonCoroutines.Start(PlacementCoroutineObj(clone, originalObj, startPos, startRot, finalPos, finalRot));
        }

        private static System.Collections.IEnumerator PlacementCoroutineObj(GameObject clone, GameObject originalObj, Vector3 startPos, UnityEngine.Quaternion startRot, Vector3 finalPos, UnityEngine.Quaternion finalRot)
        {
            if (originalObj != null)
            {
                originalObj.transform.position = finalPos;
                originalObj.transform.rotation = finalRot;
            }

            Material invisMat = new Material(Shader.Find("UI/Default"));
            invisMat.color = new Color(0, 0, 0, 0);

            MeshRenderer[] realRenderers = originalObj != null ? originalObj.GetComponentsInChildren<MeshRenderer>(true) : new MeshRenderer[0];
            System.Collections.Generic.Dictionary<MeshRenderer, Material[]> originalMats = new System.Collections.Generic.Dictionary<MeshRenderer, Material[]>();

            foreach (MeshRenderer r in realRenderers)
            {
                originalMats[r] = r.sharedMaterials;

                Material[] blankMats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < blankMats.Length; i++) blankMats[i] = invisMat;

                r.sharedMaterials = blankMats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            float duration = 0.5f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (clone != null)
                {
                    float t = elapsed / duration;
                    float easeT = 1f - UnityEngine.Mathf.Pow(1f - t, 3f);

                    Vector3 currentPos = Vector3.Lerp(startPos, finalPos, easeT);

                    if (GameManager.GetMainCamera() != null)
                    {
                        float arc = UnityEngine.Mathf.Sin(easeT * UnityEngine.Mathf.PI) * 0.05f;
                        currentPos += -GameManager.GetMainCamera().transform.up * arc;
                    }

                    clone.transform.position = currentPos;
                    clone.transform.rotation = UnityEngine.Quaternion.Slerp(startRot, finalRot, easeT);
                }
                elapsed += UnityEngine.Time.deltaTime;
                yield return null;
            }

            if (clone != null)
            {
                UnityEngine.Object.Destroy(clone);
            }

            foreach (MeshRenderer r in realRenderers)
            {
                if (r != null && originalMats.ContainsKey(r))
                {
                    r.sharedMaterials = originalMats[r];
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }

            InterpoLootMain.isAnimatingPlacement = false;
        }

        /* STREAMING_CHUNK:Managing yielding mechanics... */
        public static void SpawnSimulatedYieldClone(GearItem yieldPrefab, Vector3 spawnPos)
        {
            if (yieldPrefab == null) return;

            yieldPrefab.PlayPickUpClip();

            StartQuickLootAnimation(yieldPrefab.gameObject, null, () => { }, spawnPos, yieldPrefab.transform.localScale, yieldPrefab.transform.localScale, UnityEngine.Quaternion.identity, null, true);
        }

        public static void AnimateItemToFire(GearItem gearItem, Vector3 targetPos)
        {
            if (gearItem == null || GameManager.GetMainCamera() == null) return;

            Transform camTransform = GameManager.GetMainCamera().transform;
            Vector3 startPos = GetPocketPosition();
            UnityEngine.Quaternion startRot = UnityEngine.Quaternion.identity;

            string prefabName = gearItem.name.Replace("(Clone)", "").Trim();
            GearItem prefab = GearItem.LoadGearItemPrefab(prefabName);
            GameObject clone = CreateVisualClone(prefab != null ? prefab.gameObject : gearItem.gameObject, "FuelVisualClone");

            clone.transform.localScale = Vector3.one;
            clone.transform.position = startPos;
            clone.transform.rotation = startRot;

            clone.SetActive(true);

            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;
            foreach (MeshRenderer m in clone.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!hasBounds) { bounds = m.bounds; hasBounds = true; }
                else { bounds.Encapsulate(m.bounds); }
            }
            float dynamicOffset = hasBounds ? Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) : 0f;
            dynamicOffset = Mathf.Clamp(dynamicOffset, 0f, 1.0f);

            startPos += camTransform.up * -dynamicOffset;

            Vector3 localCenterOffset = GetCenterOffset(clone);
            Vector3 initialWorldOffset = clone.transform.TransformVector(localCenterOffset);

            startPos -= initialWorldOffset;
            targetPos += camTransform.forward * 0.5f;
            targetPos -= initialWorldOffset;

            MelonCoroutines.Start(FirePlacementCoroutine(clone, startPos, startRot, targetPos));
        }

        private static IEnumerator FirePlacementCoroutine(GameObject clone, Vector3 startPos, UnityEngine.Quaternion startRot, Vector3 targetPos)
        {
            float duration = 0.25f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (clone != null)
                {
                    float t = elapsed / duration;
                    float easeT = t * t * (3f - 2f * t);

                    Vector3 currentPos = Vector3.Lerp(startPos, targetPos, easeT);
                    float arc = Mathf.Sin(easeT * Mathf.PI) * 0.1f;
                    currentPos += Vector3.up * arc;

                    clone.transform.position = currentPos;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (clone != null)
            {
                UnityEngine.Object.Destroy(clone);
            }
        }
    }
}