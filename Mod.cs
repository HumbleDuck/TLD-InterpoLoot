using MelonLoader;
using UnityEngine;
using Il2Cpp;
using System.Collections;
using Il2CppTLD.Gear;
using System.Linq;
using System.Reflection;

[assembly: MelonInfo(typeof(InterpoLoot.InterpoLootMain), "InterpoLoot", "1.0.0", "Author")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace InterpoLoot
{
    public class InterpoLootMain : MelonMod
    {
        public static bool isTakingItem = false;
        public static bool isEatingFromCookingSlot = false;
        public static bool isAnimatingPlacement = false;
        public static float vanillaInteractRange = 2f;
        public static HashSet<GameObject> interpolatingItems = new HashSet<GameObject>();
        public static UnityEngine.Vector3 lastInspectedItemOriginalPosition = UnityEngine.Vector3.zero;
        public static UnityEngine.Quaternion lastInspectedItemOriginalRotation = UnityEngine.Quaternion.identity;
        public static UnityEngine.Vector3 lastInspectedItemOriginalScale = UnityEngine.Vector3.one;
        public static UnityEngine.Vector3 lastCrosshairHitPosition = UnityEngine.Vector3.zero;
        public static bool isPlacementModeActive = false;
        public static bool inspectingContainerItem = false;

        public static Vector3 PocketOffset = new Vector3(0f, -0.2f, -0.4f);

        public static Vector3 GetPocketPosition()
        {
            Transform cam = GameManager.GetMainCamera().transform;
            return cam.position + (cam.up * PocketOffset.y) + (cam.forward * PocketOffset.z);
        }

        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("Initializing InterpoLoot...");

            Settings.OnLoad();
            Time.fixedDeltaTime = 1f / 60f;
            
            // Nix item-on-item physics collisions! Holdover from InterpoLoot's origin as a physics mod.
            Physics.IgnoreLayerCollision((int)vp_Layer.Gear, (int)vp_Layer.Gear, true);
            
            MelonLogger.Msg("InterpoLoot has loaded!");
        }


        public override void OnUpdate()
        {
            if (GameManager.m_IsPaused || GameManager.IsMainMenuActive()) return;

            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            if (pm == null || (pm.GetControlMode() != PlayerControlMode.Normal && pm.GetControlMode() != PlayerControlMode.Locked && pm.GetControlMode() != PlayerControlMode.InVehicle)) return;


            
            // DO NOT process manual quick loot or inspect hotkeys if we are already in Inspect mode!
            // This prevents left-clicking UI buttons (like "Take") from double-triggering Quick Loot!
            if (pm.IsInspectModeActive() && !isBuggedVanillaInspect) return;
            if (InterfaceManager.IsOverlayActiveImmediate()) return;

            GameObject crosshairObj = pm.GetInteractiveObjectUnderCrosshairs(vanillaInteractRange);
            GearItem hoverItem = crosshairObj != null ? crosshairObj.GetComponent<GearItem>() : null;
            
            // Check if item is currently on a cooking slot
            if (hoverItem != null)
            {
                var pot = hoverItem.GetComponent<Il2Cpp.CookingPotItem>();
                if (pot == null) pot = hoverItem.GetComponentInParent<Il2Cpp.CookingPotItem>();

                if (pot != null && (pot.m_GearPlacePointAttachedTo != null || pot.m_FireBeingUsed != null))
                {
                    // Let vanilla handle interaction (opens cooking interface)
                    return;
                }
            }
            
            // Check Inspect Keybind
            if (hoverItem != null)
            {
                if (Input.GetKeyDown(Settings.options.InspectKey))
                {
                    // Treat inspect key as the vanilla inspection
                    if (!pm.IsInspectModeActive() || isBuggedVanillaInspect)
                    {
                        pm.EnterInspectGearMode(hoverItem); 
                    }
                    return;
                }
            }

            // Check Left-Click (Interact)
            if (InputManager.GetInteractPressed(pm) || Input.GetMouseButtonDown(0))
            {
                if (Settings.options.VanillaLooseItemInteractions) return; // Let Vanilla handle the click!

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
                        // Instant Quick Loot!
                        StartQuickLootAnimation(hoverItem.gameObject, hoverItem);
                    }
                }
            }
        }



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

        public static GameObject CreateVisualClone(GearItem gearItem, string cloneName = "VisualClone")
        {
            GameObject clone = new GameObject(cloneName);
            clone.layer = 0; // Default layer
            
            clone.transform.position = gearItem.transform.position;
            clone.transform.rotation = gearItem.transform.rotation;
            clone.transform.localScale = gearItem.transform.localScale;

            ClothingItem clothing = gearItem.GetComponent<ClothingItem>();
            
            foreach (MeshRenderer originalRenderer in gearItem.GetComponentsInChildren<MeshRenderer>(true))
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
                    
                    if (clothing != null && (originalRenderer.name == "Mesh" || (originalRenderer.transform.parent != null && originalRenderer.transform.parent.name == "Mesh")))
                    {
                        child.SetActive(true);
                    }
                    else
                    {
                        child.SetActive(originalRenderer.gameObject.activeSelf);
                    }
                    
                    child.layer = 0;
                }
            }
            return clone;
        }

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
            
            // Play rustling sound
            GameAudioManager.PlaySound("Play_ClothRustle", GameManager.GetPlayerObject()); 

            // Clone the item for the animation so it doesn't break the inventory logic
            GameObject cloneObj;
            if (!string.IsNullOrEmpty(customPrefab)) {
                var prefab = GearItem.LoadGearItemPrefab(customPrefab);
                cloneObj = CreateVisualClone(prefab);
                cloneObj.transform.localScale = Vector3.one; // Ensure correct scale for loaded prefab
            } else {
                cloneObj = CreateVisualClone(gearItem);
            }
            
            cloneObj.transform.parent = null;
            cloneObj.SetActive(true);
            Utils.SetObjectAndChildrenLayer(cloneObj, 2, 0); 
            
            Renderer[] renderers = cloneObj.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                r.enabled = true;
                foreach (UnityEngine.Material m in r.materials)
                {
                    if (m.HasProperty("_Alpha")) m.SetFloat("_Alpha", 1f);
                    if (m.HasProperty("_Color")) 
                    {
                        UnityEngine.Color c = m.color;
                        c.a = 1f;
                        m.color = c;
                    }
                }
            }
            
            // (Colliders will be destroyed after we calculate bounds)
            Transform cameraTransform = GameManager.GetMainCamera().transform;


            // Step 1: pocket retrieval
            Vector3 startPos = cameraTransform.position + (-cameraTransform.up * 1.5f) + (cameraTransform.forward * 0.2f);
            
            // Boost further forward (~1m away) under crosshairs
            Vector3 midPos = cameraTransform.position + (cameraTransform.forward * 1.0f);
            
            float duration = 0.35f;
            float time = 0f;
            bool playedAudio = false;
            cloneObj.transform.rotation = UnityEngine.Quaternion.LookRotation(cameraTransform.position - midPos); 

            // Calculate centerOffset BEFORE destroying colliders
            Vector3 localCenterOffset = GetCenterOffset(cloneObj);

            // Disable physics/colliders on clone now that we have bounds
            if (cloneObj.GetComponent<Rigidbody>() != null) UnityEngine.Object.Destroy(cloneObj.GetComponent<Rigidbody>());
            foreach (Collider c in cloneObj.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);

            while (time < duration)
            {
                if (gearItem == null || gearItem.gameObject == null) yield break;
                time += UnityEngine.Time.deltaTime;
                
                if (!playedAudio && time >= (duration - 0.1f))
                {
                    gearItem.PlayPickUpClip();
                    playedAudio = true;
                }
                
                float t = time / duration;
                t = 1f - UnityEngine.Mathf.Pow(1f - t, 3f); // Ease-out cubic
                
                Vector3 worldOffset = cloneObj.transform.TransformVector(localCenterOffset);
                Vector3 adjustedStartPos = startPos - worldOffset;
                Vector3 adjustedMidPos = midPos - worldOffset;
                
                Vector3 currentPos = UnityEngine.Vector3.Lerp(adjustedStartPos, adjustedMidPos, t);
                float arc = Mathf.Sin(t * Mathf.PI) * 0.05f;
                currentPos += -cameraTransform.up * arc;
                
                cloneObj.transform.position = currentPos;
                yield return null;
            }
            if (!playedAudio) gearItem.PlayPickUpClip();

            // Short pause
            yield return new UnityEngine.WaitForSeconds(0.15f);

            // Step 2: Interpolate to face (consumption)
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

            // Destroy clone
            UnityEngine.Object.Destroy(cloneObj);

            // Finally, trigger actual consumption
            if (gearItem.m_FoodItem != null)
                pm.UseInventoryItem(gearItem);
            else if (gearItem.m_WaterSupply != null)
                pm.UseInventoryItem(gearItem);
            
            isSimulatingRadialConsumption = false;
            
            onComplete?.Invoke();

            // Unfreeze player
            GameManager.GetPlayerManagerComponent().SetControlMode(PlayerControlMode.Normal);

            if (reopenInventory)
            {
                // Wait for a few frames so the progress bar can appear
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

        public static void StartQuickLootAnimation(GameObject objToAnimate, GearItem originalGear = null, Action onComplete = null, Vector3? overrideStartPos = null, Vector3? startScale = null, Vector3? targetScale = null)
        {
            MelonCoroutines.Start(QuickLootCoroutine(objToAnimate, originalGear, onComplete, overrideStartPos, startScale, targetScale));
        }

        private static IEnumerator QuickLootCoroutine(GameObject objToAnimate, GearItem originalGear, Action onComplete, Vector3? overrideStartPos, Vector3? startScale, Vector3? targetScale)
        {
            if (objToAnimate == null || interpolatingItems.Contains(objToAnimate)) yield break;
            
            interpolatingItems.Add(objToAnimate);

            if (originalGear != null)
                originalGear.PlayPickUpClip();

            // Unparent from any inspect UI or container so world-space lerp works correctly
            objToAnimate.transform.parent = null;

            // Force it to be active and visible in the world
            objToAnimate.SetActive(true);
            
            if (originalGear != null && objToAnimate == originalGear.gameObject)
            {
                ClothingItem clothing = originalGear.GetComponent<ClothingItem>();
                if (clothing != null)
                {
                    UnityEngine.Transform mesh = null;
                    UnityEngine.Transform meshInspect = null;
                    
                    foreach (UnityEngine.Transform child in originalGear.gameObject.GetComponentsInChildren<UnityEngine.Transform>(true))
                    {
                        if (child.name == "Mesh") mesh = child;
                        if (child.name == "MeshInspectMode") meshInspect = child;
                    }
                    
                    if (meshInspect != null) meshInspect.gameObject.SetActive(false);
                    if (mesh != null) mesh.gameObject.SetActive(true);
                }
            }

            Vector3 localCenterOffset = GetCenterOffset(objToAnimate);

            Utils.SetObjectAndChildrenLayer(objToAnimate, 2, 0); // Layer 2 is Ignore Raycast, 0 preserves nothing
            
            Renderer[] renderers = objToAnimate.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                r.enabled = true;
                foreach (UnityEngine.Material m in r.materials)
                {
                    if (m.HasProperty("_Alpha")) m.SetFloat("_Alpha", 1f);
                    if (m.HasProperty("_Color")) 
                    {
                        UnityEngine.Color c = m.color;
                        c.a = 1f;
                        m.color = c;
                    }
                }
            }

            // Disable its collider and physics so it doesn't interact while flying
            if (objToAnimate.GetComponent<Rigidbody>() != null)
                objToAnimate.GetComponent<Rigidbody>().isKinematic = true;
            if (objToAnimate.GetComponent<Collider>() != null)
                objToAnimate.GetComponent<Collider>().enabled = false;

            float duration = 0.5f;
            float time = 0f;
            
            Vector3 initialWorldOffset = objToAnimate.transform.TransformVector(localCenterOffset);
            Vector3 startPos = overrideStartPos.HasValue ? overrideStartPos.Value : objToAnimate.transform.position;
            startPos -= initialWorldOffset; // Offset the root so the visual mesh spawns exactly at the original position

            Transform cameraTransform = GameManager.GetMainCamera().transform;
            
            // Calculate dynamic pocket offset based on max mesh bounds to prevent large items from clipping the camera
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;
            foreach (MeshRenderer m in objToAnimate.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!hasBounds) { bounds = m.bounds; hasBounds = true; }
                else { bounds.Encapsulate(m.bounds); }
            }
            float dynamicOffset = hasBounds ? Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) : 0f;
            dynamicOffset = Mathf.Clamp(dynamicOffset, 0f, 1.0f);

            while (time < duration)
            {
                if (objToAnimate == null) yield break;
                time += Time.deltaTime;
                float t = time / duration;
                
                // Ease out cubic
                t = 1f - Mathf.Pow(1f - t, 3f);

                // Move to pocket position, offset further down by the item's max dimension
                Vector3 targetPos = GetPocketPosition();
                targetPos += cameraTransform.up * -dynamicOffset;
                
                Vector3 worldOffset = objToAnimate.transform.TransformVector(localCenterOffset);
                Vector3 adjustedTargetPos = targetPos - worldOffset;
                
                Vector3 currentPos = Vector3.Lerp(startPos, adjustedTargetPos, t);
                
                // Add a very subtle downward arc to prevent clipping without looking too exaggerated
                float arc = Mathf.Sin(t * Mathf.PI) * 0.05f;
                currentPos += -cameraTransform.up * arc;
                
                objToAnimate.transform.position = currentPos;
                
                if (startScale.HasValue && targetScale.HasValue)
                {
                    objToAnimate.transform.localScale = Vector3.Lerp(startScale.Value, targetScale.Value, t);
                }

                yield return null;
            }
            
            if (objToAnimate != null && targetScale.HasValue)
            {
                objToAnimate.transform.localScale = targetScale.Value;
            }

            // Re-enable all colliders before adding to inventory so they are interactable when dropped
            Collider[] allColliders = objToAnimate.GetComponentsInChildren<Collider>(true);
            foreach (Collider c in allColliders) c.enabled = true;

            if (onComplete != null)
            {
                onComplete();
            }
            else
            {
                // Finally, add to inventory silently
                if (originalGear != null)
                {
                    GameManager.GetPlayerManagerComponent().AddItemToPlayerInventory(originalGear);
                    GameManager.GetPlayerManagerComponent().ResetPickup();
                    originalGear.gameObject.SetActive(false);
                }
            }
            
            interpolatingItems.Remove(objToAnimate);
        }
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

            // Target is vanilla inspect position (1.5f in front of camera)
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



        public static void AnimateItemToSlot(GameObject originalObj, Vector3 finalPos, UnityEngine.Quaternion finalRot, Il2CppAK.Wwise.Event delayedAudio = null)
        {
            if (originalObj == null || GameManager.GetMainCamera() == null) return;
            isAnimatingPlacement = true;
            
            // The item is already on the stove physically. Hide it by scaling to zero so we don't break its internal state by disabling it.
            Vector3 originalScale = originalObj.transform.localScale;
            originalObj.transform.localScale = Vector3.zero;

            Transform camTransform = GameManager.GetMainCamera().transform;
            Vector3 startPos = camTransform.position + (camTransform.forward * -0.4f) + (camTransform.up * -0.4f);
            UnityEngine.Quaternion startRot = originalObj.transform.rotation;

            GearItem gear = originalObj.GetComponent<GearItem>();
            GameObject clone = CreateVisualClone(gear, "PlacementVisualClone");
            clone.transform.position = startPos;
            clone.transform.rotation = finalRot;
            clone.SetActive(true);
            
            // The clone copied the zero scale, so we must set it back to the original scale
            clone.transform.localScale = originalScale;

            GameManager.GetPlayerManagerComponent().SetControlMode(PlayerControlMode.Locked);

            MelonCoroutines.Start(PlacementCoroutineObj(clone, null, startPos, startRot, finalPos, finalRot, () => {
                UnityEngine.Object.Destroy(clone);
                
                // Restore original item scale
                if (originalObj != null)
                {
                    originalObj.transform.localScale = originalScale;
                }
                isAnimatingPlacement = false;
                GameManager.GetPlayerManagerComponent().SetControlMode(PlayerControlMode.Normal);
            }, null, null, null, null, delayedAudio));
        }
        private static System.Collections.IEnumerator PlacementCoroutineObj(GameObject clone, Il2Cpp.GearItem gearItemToCook, Vector3 startPos, UnityEngine.Quaternion startRot, Vector3 finalPos, UnityEngine.Quaternion finalRot, Action onComplete, Vector3? startScale = null, Vector3? finalScale = null, GameObject dummyPotObject = null, GameObject emptyPotClone = null, Il2CppAK.Wwise.Event delayedAudio = null)
        {
            float duration = 0.25f;
            float elapsed = 0f;

            bool hasPlayedAudio = false;

            while (elapsed < duration)
            {
                if (!hasPlayedAudio && delayedAudio != null && (elapsed / duration) >= 0.8f)
                {
                    hasPlayedAudio = true;
                    delayedAudio.Post(clone);
                }
                Vector3 targetPos = gearItemToCook != null ? gearItemToCook.transform.position : finalPos;
                UnityEngine.Quaternion targetRot = gearItemToCook != null ? gearItemToCook.transform.rotation : finalRot;
                
                if (clone != null)
                {
                    float t = elapsed / duration;
                    float easeT = t * t * (3f - 2f * t); // Smoothstep
                    clone.transform.position = Vector3.Lerp(startPos, targetPos, easeT);
                    clone.transform.rotation = UnityEngine.Quaternion.Slerp(startRot, targetRot, easeT);
                    if (startScale.HasValue && finalScale.HasValue)
                        clone.transform.localScale = Vector3.Lerp(startScale.Value, finalScale.Value, easeT);
                }
                elapsed += UnityEngine.Time.deltaTime;
                yield return null;
            }

            if (clone != null)
            {
                Vector3 targetPos = gearItemToCook != null ? gearItemToCook.transform.position : finalPos;
                UnityEngine.Quaternion targetRot = gearItemToCook != null ? gearItemToCook.transform.rotation : finalRot;
                clone.transform.position = targetPos;
                clone.transform.rotation = targetRot;
                if (startScale.HasValue && finalScale.HasValue)
                    clone.transform.localScale = finalScale.Value;
            }

            if (gearItemToCook != null)
            {
                if (emptyPotClone == null)
                {
                    if (finalScale.HasValue) gearItemToCook.transform.localScale = finalScale.Value;
                }
                
                foreach (ParticleSystem ps in gearItemToCook.GetComponentsInChildren<ParticleSystem>(true)) 
                {
                    ps.Play();
                }
            }
            if (dummyPotObject != null && emptyPotClone != null)
            {
                dummyPotObject.transform.localScale = Vector3.one;
            }

            onComplete?.Invoke();
        }

        public static void SpawnSimulatedYieldClone(GearItem yieldPrefab, Vector3 spawnPos)
        {
            if (yieldPrefab == null) return;
            
            GameObject clone = CreateVisualClone(yieldPrefab, "YieldVisualClone");
            clone.transform.position = spawnPos;
            clone.transform.rotation = UnityEngine.Quaternion.identity;
            clone.SetActive(true);
            
            // Pass null for originalGear because the real item was already silently added to the inventory
            // But we need to play the audio manually!
            yieldPrefab.PlayPickUpClip();

            MelonCoroutines.Start(QuickLootCoroutine(clone, null, () => {
                UnityEngine.Object.Destroy(clone);
            }, spawnPos, clone.transform.localScale, clone.transform.localScale));
        }

        public static void AnimateItemToFire(GearItem gearItem, Vector3 targetPos)
        {
            if (gearItem == null || GameManager.GetMainCamera() == null) return;
            
            Transform camTransform = GameManager.GetMainCamera().transform;
            Vector3 startPos = GetPocketPosition();
            UnityEngine.Quaternion startRot = UnityEngine.Quaternion.identity;

            string prefabName = gearItem.name.Replace("(Clone)", "").Trim();
            GearItem prefab = GearItem.LoadGearItemPrefab(prefabName);
            GameObject clone = CreateVisualClone(prefab != null ? prefab : gearItem, "FuelVisualClone");
            
            clone.transform.localScale = Vector3.one;
            clone.transform.position = startPos;
            clone.transform.rotation = startRot;
            
            clone.SetActive(true);
            
            // Calculate dynamic pocket offset based on max mesh bounds to prevent large items from clipping the camera
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
            
            startPos -= initialWorldOffset; // Visually start at hand
            targetPos += camTransform.forward * 0.5f; // Push target further into the fire
            targetPos -= initialWorldOffset; // Visually end perfectly on the fire

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
                    float easeT = t * t * (3f - 2f * t); // Smoothstep
                    
                    Vector3 currentPos = Vector3.Lerp(startPos, targetPos, easeT);
                    float arc = Mathf.Sin(easeT * Mathf.PI) * 0.1f;
                    currentPos += Vector3.up * arc;

                    clone.transform.position = currentPos;
                    // Tumble slightly while falling into the fire
                    clone.transform.Rotate(new Vector3(180f, 90f, 45f) * Time.deltaTime);
                }
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (clone != null)
            {
                UnityEngine.Object.Destroy(clone);
            }
        }
        public static bool isBuggedVanillaInspect = false;
    }
}
