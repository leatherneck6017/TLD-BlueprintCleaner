using Il2Cpp;
using MelonLoader;
using System;
using Il2CppSystem.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Il2CppTLD.Gear;
using Il2CppNodeCanvas.Tasks.Conditions;
using UnityEngine;
using Il2CppTLD.UI.Scroll;
using Il2CppTLD.Cooking;
using Il2CppNodeCanvas.Tasks.Actions;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using Il2CppRewired.Utils;
using Il2CppRewired.ComponentControls.Data;
using System.Security;

namespace BlueprintCleaner
{
    internal class Patches
    {
        public static float? scrollValue;
        public static string? lastBlueprintSelected;
        public static string? lastRecipeSelected;
        public static int? lastCookableItemSelected;
        //public static var name;

        // Dictionnary to store item names and corresponding BlueprintData
        public static Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<string>> blueprintDuplicates = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<string>>();

        // Dictionnary for active blueprint
        public static Il2CppSystem.Collections.Generic.Dictionary<string, string> activeBlueprint = new Il2CppSystem.Collections.Generic.Dictionary<string, string>();

        // Dictionnary to store item names and corresponding Recipe
        public static Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<string>> recipeDuplicates = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<string>>();

        // Dictionnary for active recipe
        public static Il2CppSystem.Collections.Generic.Dictionary<string, string> activeRecipe = new Il2CppSystem.Collections.Generic.Dictionary<string, string>();

        // Dictionnary to store item names and corresponding cookableItem
        public static Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<int>> cookableItemDuplicates = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<int>>();

        // Dictionnary for active cookable (without recipe)
        public static Il2CppSystem.Collections.Generic.Dictionary<string, int> activeCookableItem = new Il2CppSystem.Collections.Generic.Dictionary<string, int>();

        /////////////////////////////////////////////////////////////////////////////////
        // --------------------------  CRAFTING PATCHES  ----------------------------- //
        /////////////////////////////////////////////////////////////////////////////////

        // Blueprints are stacked by crafted result only (no crafting location in the key).
        // Vanilla builds m_FilteredBlueprints for the current category/location filter first
        // (ApplyFilter), then the ApplyFilter postfix collapses each stack to ONE member taken
        // from the members vanilla actually listed. The chosen member therefore always belongs
        // to the current view, and the row keeps the position of the stack's first member.

        // Controlled by the "Debug logging" option in Mod Settings
        private static bool DebugLog => Settings.settings != null && Settings.settings.debugLog;

        // Blueprint name -> BlueprintData
        public static System.Collections.Generic.Dictionary<string, BlueprintData> blueprintsByName = new System.Collections.Generic.Dictionary<string, BlueprintData>();

        // Stack key -> members vanilla listed in the last ApplyFilter pass (used by the [n/m] label and the arrow keys)
        public static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> lastVisible = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();

        // Separator used in the label, e.g. "Gunpowder [2/4 · Forge]"
        private const string LocationSeparator = " · ";

        // Stack key for a blueprint: crafted result name only
        public static string? GetResultKey(BlueprintData bpi)
        {
            if (bpi == null) return null;
            if (bpi.m_CraftedResultDecoration != null) return bpi.m_CraftedResultDecoration.name;
            if (bpi.m_CraftedResultGear != null) return bpi.m_CraftedResultGear.name;
            return null;
        }

        // Active pick if listed, then a member craftable at the current location, then the first listed
        public static string PickChosen(Panel_Crafting panel, string key, System.Collections.Generic.List<string> visible)
        {
            if (visible.Count == 0) return "";

            string active = activeBlueprint.ContainsKey(key) ? activeBlueprint[key] : "";
            if (visible.Contains(active)) return active;

            CraftingLocation here = panel.GetCurrentCraftingLocation();
            foreach (string name in visible)
            {
                if (blueprintsByName.TryGetValue(name, out BlueprintData bp) && bp != null && bp.CanCraftAtLocation(here)) return name;
            }

            return visible[0];
        }

        public static string GetLocationLabel(CraftingLocation location)
        {
            switch (location)
            {
                case CraftingLocation.Anywhere: return "Anywhere";
                case CraftingLocation.Workbench: return "Workbench";
                case CraftingLocation.Forge: return "Forge";
                case CraftingLocation.AmmoWorkbench: return "Ammo Workbench";
                case CraftingLocation.Fire: return "Fire";
                case CraftingLocation.FurnitureWorkbench: return "Furniture Workbench";
                default: return location.ToString();
            }
        }

        // Cycle the selected stack through the members visible in the current view (dir = +1 / -1)
        public static void CycleStack(Panel_Crafting panel, int dir)
        {
            BlueprintData bpi = panel.SelectedBPI;
            string? key = GetResultKey(bpi);
            if (key == null || !activeBlueprint.ContainsKey(key)) return;

            if (!lastVisible.TryGetValue(key, out var visible) || visible.Count < 2) return;

            int index = visible.IndexOf(bpi.name);
            if (index < 0) index = 0;
            int next = (index + dir + visible.Count) % visible.Count;

            activeBlueprint[key] = visible[next];
            lastBlueprintSelected = visible[next];

            // Keep the list where it is (same mechanism as hide/show); restored in the OnCategoryChanged postfix
            scrollValue = panel.m_ScrollBehaviour.m_ScrollBar.scrollValue;
            if (DebugLog) MelonLogger.Msg($"[BlueprintCleaner] Cycle {key} -> {visible[next]}, scroll before = {scrollValue}, index = {panel.m_ScrollBehaviour.SelectedIndex}");

            panel.OnCategoryChanged(panel.m_CategoryNavigation.m_CurrentIndex);
        }

        [HarmonyPatch(typeof(BlueprintManager), nameof(BlueprintManager.LoadAddressableBlueprints))]
        internal static class BlueprintManager_LoadAddressableBlueprints
        {
            private static void Postfix(BlueprintManager __instance)
            {
                // SKIP 
                if (Main.vanillaDisplay) return;

                blueprintDuplicates.Clear();
                activeBlueprint.Clear();
                blueprintsByName.Clear();
                lastVisible.Clear();

                foreach (BlueprintData bpi in __instance.m_AllBlueprints)
                {
                    if (bpi == null) continue;
                    if (bpi.name == "BLUEPRINT_GEAR_NoiseMaker_A") continue;

                    blueprintsByName[bpi.name] = bpi;

                    string? itemName = GetResultKey(bpi);

                    if (itemName != null)
                    {
                        if (blueprintDuplicates.ContainsKey(itemName))
                        {
                            blueprintDuplicates[itemName].Add(bpi.name);
                        }
                        else
                        {
                            blueprintDuplicates[itemName] = new Il2CppSystem.Collections.Generic.List<string>();
                            blueprintDuplicates[itemName].Add(bpi.name);
                        }
                    }
                }

                var keysToRemove = new Il2CppSystem.Collections.Generic.List<string>(); // List to keep track of keys to remove

                foreach (var pair in blueprintDuplicates)
                {
                    if (pair.Value.Count == 1)
                    {
                        keysToRemove.Add(pair.Key); // Mark the key for removal if it has only one blueprint name
                    }
                }

                // Now remove the marked keys from the dictionary
                foreach (var key in keysToRemove)
                {
                    blueprintDuplicates.Remove(key);
                }

                var keysToUpdate = new Il2CppSystem.Collections.Generic.List<string>();

                foreach (var blueprint in Main.blueprintsRemoved)
                {
                    foreach (var result in blueprintDuplicates)
                    {
                        if (result.Value != null && result.Value.Contains(blueprint))
                        {
                            result.Value.Remove(blueprint);
                            if (result.Value == null || result.Value.Count == 0)
                            {
                                keysToUpdate.Add(result.Key);
                            }
                        }
                    }
                }

                foreach (var key in keysToUpdate)
                {
                    blueprintDuplicates[key] = new Il2CppSystem.Collections.Generic.List<string>();
                }

                foreach (var pair in blueprintDuplicates)
                {
                    //populate activeBlueprint dictionary with the first blueprints for each item
                    activeBlueprint[pair.Key] = pair.Value != null && pair.Value.Count > 0 ? pair.Value[0] : "";
                }
            }
        }

        // Collapses each stack in the list vanilla just built.
        // Priority.First so this runs before other mods' ApplyFilter postfixes (e.g. CraftingSearch).
        [HarmonyPatch(typeof(Panel_Crafting), nameof(Panel_Crafting.ApplyFilter))]
        internal static class Panel_Crafting_ApplyFilter
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(Panel_Crafting __instance)
            {
                lastVisible.Clear();

                // SKIP 
                if (Main.vanillaDisplay) return;

                Il2CppSystem.Collections.Generic.List<BlueprintData> filtered = __instance.m_FilteredBlueprints;
                if (filtered == null || filtered.Count == 0) return;

                // 1. Members of each stack that vanilla listed, in list order
                var visible = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
                for (int i = 0; i < filtered.Count; i++)
                {
                    BlueprintData bpi = filtered[i];
                    if (bpi == null) continue;

                    string? key = GetResultKey(bpi);
                    if (key == null || !blueprintDuplicates.ContainsKey(key)) continue;

                    if (!visible.TryGetValue(key, out var members))
                    {
                        members = new System.Collections.Generic.List<string>();
                        visible[key] = members;
                    }
                    members.Add(bpi.name);
                }

                if (visible.Count == 0) return;

                // 2. One member per stack
                var chosen = new System.Collections.Generic.Dictionary<string, string>();
                foreach (var pair in visible)
                {
                    chosen[pair.Key] = PickChosen(__instance, pair.Key, pair.Value);
                }

                // 3. Rebuild the list: the chosen member takes the slot of the stack's first listed member
                var result = new System.Collections.Generic.List<BlueprintData>(filtered.Count);
                var emitted = new System.Collections.Generic.HashSet<string>();
                bool changed = false;

                for (int i = 0; i < filtered.Count; i++)
                {
                    BlueprintData bpi = filtered[i];
                    string? key = bpi != null ? GetResultKey(bpi) : null;

                    if (key == null || !chosen.ContainsKey(key))
                    {
                        result.Add(bpi!);
                        continue;
                    }

                    if (emitted.Contains(key))
                    {
                        changed = true; // other members of the stack are dropped
                        continue;
                    }
                    emitted.Add(key);

                    BlueprintData pick = bpi!;
                    if (blueprintsByName.TryGetValue(chosen[key], out BlueprintData chosenData) && chosenData != null)
                    {
                        pick = chosenData;
                    }
                    if (pick.name != bpi!.name) changed = true;
                    result.Add(pick);
                }

                foreach (var pair in visible) lastVisible[pair.Key] = pair.Value;

                if (DebugLog)
                {
                    MelonLogger.Msg($"[BlueprintCleaner] ApplyFilter: filter = {__instance.m_CurrentFilter}, location = {__instance.GetCurrentCraftingLocation()}, rows {filtered.Count} -> {result.Count}");
                    foreach (var pair in visible)
                    {
                        var parts = new System.Collections.Generic.List<string>();
                        foreach (string name in pair.Value)
                        {
                            string loc = blueprintsByName.TryGetValue(name, out BlueprintData d) && d != null ? d.m_RequiredCraftingLocation.ToString() : "?";
                            parts.Add($"{name} ({loc})");
                        }
                        MelonLogger.Msg($"[BlueprintCleaner]   {pair.Key}: shown = {chosen[pair.Key]}, listed = {string.Join(", ", parts)}");
                    }
                }

                if (!changed)
                {
                    // Same rows, but the [n/m] labels may have been built before lastVisible was updated
                    if (__instance.m_ScrollBehaviour != null) __instance.m_ScrollBehaviour.RefreshItems();
                    return;
                }

                filtered.Clear();
                foreach (BlueprintData bpi in result) filtered.Add(bpi);

                // Vanilla may already have built the rows for the uncollapsed list
                ScrollBehaviour sb = __instance.m_ScrollBehaviour;
                if (sb != null)
                {
                    sb.RebuildItems(filtered.Count);
                    if (filtered.Count > 0)
                    {
                        int index = Math.Clamp(sb.SelectedIndex, 0, filtered.Count - 1);
                        sb.SetSelectedIndex(index, true, false);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Panel_Crafting), nameof(Panel_Crafting.ItemPassesFilter))]
        internal static class Panel_Crafting_ItemPassesFilter
        {
            private static void Postfix(Panel_Crafting __instance, BlueprintData bpi, ref bool __result)
            {
                // SKIP 
                if (Main.vanillaDisplay) return;

                // Hidden blueprints are removed here; stacks are collapsed in the ApplyFilter postfix
                if (__result && bpi != null && Main.blueprintsRemoved.Contains(bpi.name))
                {
                    __result = false;
                }
            }
        }

        [HarmonyPatch(typeof(Panel_Crafting), nameof(Panel_Crafting.HandleInput))]
        internal static class Panel_Crafting_HandleInput
        {
            private static void Postfix(Panel_Crafting __instance)
            {
                // ENABLE/DISABLE vanilla view (and the whole mod)
                if (InputManager.GetKeyDown(InputManager.m_CurrentContext, Settings.settings.viewKey))
                {
                    Main.vanillaDisplay = !Main.vanillaDisplay;
                    __instance.OnCategoryChanged(__instance.m_CategoryNavigation.m_CurrentIndex);
                }

                // SKIP 
                if (Main.vanillaDisplay) return;
                if (__instance.SelectedBPI == null) return;

                if (InputManager.GetKeyDown(InputManager.m_CurrentContext, Settings.settings.rightKey))
                {
                    CycleStack(__instance, 1);
                }
                else if (InputManager.GetKeyDown(InputManager.m_CurrentContext, Settings.settings.leftKey))
                {
                    CycleStack(__instance, -1);
                }
            }
        }

        // This patch will allow to keep the blueprint selected
        [HarmonyPatch(typeof(Panel_Crafting), nameof(Panel_Crafting.OnCategoryChanged))]
        internal static class Panel_Crafting_OnCategoryChanged
        {
            private static void Postfix(Panel_Crafting __instance)
            {
                float? restoreScroll = scrollValue;
                scrollValue = null;

                if (restoreScroll != null)
                {
                    __instance.m_ScrollBehaviour.m_ScrollBar.scrollValue = (float)restoreScroll;
                }

                if (lastBlueprintSelected != null)
                {
                    int i = 0;
                    foreach (BlueprintData bpi in __instance.m_FilteredBlueprints)
                    {
                        if (bpi.name == lastBlueprintSelected)
                        {
                            __instance.m_ScrollBehaviour.SetSelectedIndex(i, true, true);
                            break;
                        }
                        i = i + 1;
                    }
                    lastBlueprintSelected = null;
                }

                // SetSelectedIndex can scroll the row to the edge of the window; put the list back where it was
                if (restoreScroll != null)
                {
                    if (DebugLog) MelonLogger.Msg($"[BlueprintCleaner] OnCategoryChanged: scroll after select = {__instance.m_ScrollBehaviour.m_ScrollBar.scrollValue}, restoring {restoreScroll}");
                    __instance.m_ScrollBehaviour.m_ScrollBar.scrollValue = (float)restoreScroll;
                }
            }
        }

        // Remove 'FILTER' Label in Panel_Crafting
        [HarmonyPatch(typeof(Panel_Crafting), nameof(Panel_Crafting.Enable), new Type[] { typeof(bool), typeof(bool) })]
        internal static class Panel_Crafting_Enable
        {
            private static void Postfix(Panel_Crafting __instance, bool enable)
            {
                __instance.m_FilterLabel.color = new Color(0f, 0f, 0f, 0f);
            }
        }

        // This allow to update the name displayed
        [HarmonyPatch(typeof(BlueprintData), nameof(BlueprintData.GetDisplayedNameWithCount))]
        internal static class BlueprintData_GetDisplayedNameWithCount
        {
            private static void Postfix(BlueprintData __instance, ref string __result)
            {
                if (__instance == null || string.IsNullOrEmpty(__result)) return;

                int indexParenthese = __result.IndexOf('(');
                if (indexParenthese > 0)
                {
                    __result = __result.Substring(0, indexParenthese - 1);
                }

                // SKIP 
                if (Main.vanillaDisplay) return;

                string? resultName = GetResultKey(__instance);
                if (resultName == null || !blueprintDuplicates.ContainsKey(resultName)) return;

                // Count only the members visible in the current view; fall back to the whole stack
                System.Collections.Generic.List<string> members;
                if (!lastVisible.TryGetValue(resultName, out members))
                {
                    members = new System.Collections.Generic.List<string>();
                    if (blueprintDuplicates[resultName] != null)
                    {
                        foreach (string blueprint in blueprintDuplicates[resultName]) members.Add(blueprint);
                    }
                }

                if (members.Count < 2) return;

                int rank = members.IndexOf(__instance.name) + 1;

                // Show the location only when the stack mixes crafting locations
                bool mixedLocations = false;
                foreach (string blueprint in members)
                {
                    if (blueprintsByName.TryGetValue(blueprint, out BlueprintData other) && other != null
                        && other.m_RequiredCraftingLocation != __instance.m_RequiredCraftingLocation)
                    {
                        mixedLocations = true;
                        break;
                    }
                }

                if (mixedLocations)
                {
                    __result += $" [{rank}/{members.Count}{LocationSeparator}{GetLocationLabel(__instance.m_RequiredCraftingLocation)}]";
                }
                else
                {
                    __result += $" [{rank}/{members.Count}]";
                }
            }
        }

        [HarmonyPatch(typeof(BlueprintDisplayItem), nameof(BlueprintDisplayItem.OnItemClick))]
        internal static class BlueprintDisplayItem_OnItemClick
        {
            private static void Postfix(BlueprintDisplayItem __instance)
            {
                if (Input.GetKey(Settings.settings.HoldKey))
                {
                    if (__instance.m_BlueprintData != null)
                    {
                        string blueprintName = __instance.m_BlueprintData.name;
                        string? resultName = GetResultKey(__instance.m_BlueprintData);
                        Panel_Crafting panel = InterfaceManager.GetPanel<Panel_Crafting>();
                        scrollValue = panel.m_ScrollBehaviour.m_ScrollBar.scrollValue;

                        if (Main.blueprintsRemoved.Contains(blueprintName))
                        {
                            Main.blueprintsRemoved.Remove(blueprintName);
                            if (resultName != null && blueprintDuplicates.ContainsKey(resultName))
                            {
                                if (blueprintDuplicates[resultName] == null || blueprintDuplicates[resultName].Count == 0)
                                {
                                    blueprintDuplicates[resultName] = new Il2CppSystem.Collections.Generic.List<string>();
                                }

                                if (!blueprintDuplicates[resultName].Contains(blueprintName))
                                {
                                    blueprintDuplicates[resultName].Add(blueprintName);
                                }
                                activeBlueprint[resultName] = blueprintDuplicates[resultName][0];
                            }
                        }
                        else
                        {
                            Main.blueprintsRemoved.Add(blueprintName);
                            if (resultName != null && blueprintDuplicates.ContainsKey(resultName))
                            {
                                blueprintDuplicates[resultName].Remove(blueprintName);
                                if (blueprintDuplicates[resultName] == null || blueprintDuplicates[resultName].Count == 0)
                                {
                                    activeBlueprint[resultName] = "";
                                }
                                else
                                {
                                    activeBlueprint[resultName] = blueprintDuplicates[resultName][0];
                                }
                            }
                        }
                        panel.OnCategoryChanged(panel.m_CategoryNavigation.m_CurrentIndex);

                        Main.SaveListToJson(Main.blueprintsRemoved);
                    }
                }
            }
        }
        /////////////////////////////////////////////////////////////////////////////////
        // --------------------------  COOKING PATCHES  ----------------------------- ///
        /////////////////////////////////////////////////////////////////////////////////

        [HarmonyPatch(typeof(RecipeBook), nameof(RecipeBook.OnRecipesLoaded))]
        internal static class RecipeBook_OnRecipesLoaded
        {
            private static void Postfix(RecipeBook __instance)
            {
                // SKIP 
                if (Main.vanillaDisplay) return;

                recipeDuplicates.Clear();
                activeRecipe.Clear();

                foreach (RecipeData recipe in __instance.AllRecipes)
                {
                    string itemName = recipe.m_DishBlueprint.m_CraftedResultGear.name;

                    if (itemName != null)
                    {
                        if (recipeDuplicates.ContainsKey(itemName))
                        {
                            // If the name is already in the dictionary, it's a duplicate, add the blueprint name
                            recipeDuplicates[itemName].Add(recipe.name);
                        }
                        else
                        {
                            // Otherwise, create a new dictionary entry with an empty list and add the blueprint name
                            recipeDuplicates[itemName] = new Il2CppSystem.Collections.Generic.List<string>();
                            recipeDuplicates[itemName].Add(recipe.name);
                        }
                    }
                }

                // Remove entries from the dictionary that have only one recipe name
                var keysToRemove = new Il2CppSystem.Collections.Generic.List<string>(); // List to keep track of keys to remove

                foreach (var pair in recipeDuplicates)
                {
                    if (pair.Value.Count == 1)
                    {
                        keysToRemove.Add(pair.Key); // Mark the key for removal if it has only one recipe name
                    }
                }

                // Now remove the marked keys from the dictionary
                foreach (var key in keysToRemove)
                {
                    recipeDuplicates.Remove(key);
                }

                var keysToUpdate = new Il2CppSystem.Collections.Generic.List<string>();

                foreach (var recipe in Main.blueprintsRemoved)
                {

                    foreach (var result in recipeDuplicates)
                    {
                        if (result.Value != null && result.Value.Contains(recipe))
                        {
                            result.Value.Remove(recipe);
                            if (result.Value == null || result.Value.Count == 0)
                            {
                                keysToUpdate.Add(result.Key);
                            }
                        }
                    }
                }

                foreach (var key in keysToUpdate)
                {
                    recipeDuplicates[key] = new Il2CppSystem.Collections.Generic.List<string>();
                }

                // Log the final dictionary content for debugging
                foreach (var pair in recipeDuplicates)
                {
                    //populate activeRecipe dictionary with the first recipes for each item
                    activeRecipe[pair.Key] = pair.Value != null && pair.Value.Count > 0 ? pair.Value[0] : "";

                    /// LOG PART ///
                    /*Il2CppSystem.Collections.Generic.List<string> recipeNamesList = pair.Value;
                    string recipeNames = string.Empty;

                    for (int i = 0; i < recipeNamesList.Count; i++)
                    {

                        recipeNames += recipeNamesList[i];

                        if (i < recipeNamesList.Count - 1)
                        {
                            recipeNames += ", ";
                        }
                    }
                    MelonLogger.Msg($"Item: {pair.Key}, recipes: {recipeNames}");*/
                }
            }
        }

        [HarmonyPatch(typeof(CookingToolPanelFilterButton), nameof(CookingToolPanelFilterButton.IsGearItemInFilter))]
        internal static class CookingToolPanelFilterButton_IsGearItemInFilter
        {
            private static void Postfix(CookingToolPanelFilterButton __instance, ref bool __result)
            {
                if (__result)
                {
                    // SKIP 
                    if (Main.vanillaDisplay) return;

                    cookableItemDuplicates.Clear();
                    activeCookableItem.Clear();

                    Il2CppSystem.Collections.Generic.List<GearItemObject> GearItems = GameManager.GetInventoryComponent().m_Items;

                    foreach (GearItemObject gio in GearItems)
                    {
                        if (gio.m_GearItem != null)
                        {
                            if (gio.m_GearItem.m_Cookable != null)
                            {
                                string itemName = gio.m_GearItem.name;
                                if (cookableItemDuplicates.ContainsKey(itemName))
                                {
                                    cookableItemDuplicates[itemName].Add(gio.m_GearItem.GetInstanceID());
                                }
                                else
                                {
                                    cookableItemDuplicates[itemName] = new Il2CppSystem.Collections.Generic.List<int>();
                                    cookableItemDuplicates[itemName].Add(gio.m_GearItem.GetInstanceID());
                                }
                                //MelonLogger.Msg($"{itemName}----{gio.m_GearItem.GetInstanceID()}");
                            }
                        }
                    }

                    var keysToRemove = new Il2CppSystem.Collections.Generic.List<string>(); // List to keep track of keys to remove

                    foreach (var pair in cookableItemDuplicates)
                    {
                        if (pair.Value.Count == 1)
                        {
                            keysToRemove.Add(pair.Key); // Mark the key for removal if it has only one recipe name
                        }
                    }

                    // Now remove the marked keys from the dictionary
                    foreach (var key in keysToRemove)
                    {
                        cookableItemDuplicates.Remove(key);
                    }

                    // Log the final dictionary content for debugging
                    foreach (var pair in cookableItemDuplicates)
                    {
                        //populate activeRecipe dictionary with the first recipes for each item
                        activeCookableItem[pair.Key] = pair.Value[0];
                    }
                }
            }
        }

        [HarmonyPatch(typeof(CookingToolPanelFilterButton), nameof(CookingToolPanelFilterButton.PassesFilter))]
        internal static class CookingToolPanelFilterButton_PassesFilter
        {
            private static void Postfix(CookingToolPanelFilterButton __instance, CookableItem cookableItem, ref bool __result)
            {
                // SKIP 
                if (Main.vanillaDisplay) return;

                if (__result)
                {
                    string resultName = cookableItem.m_GearItem.name;
                    //MelonLogger.Msg($"{resultName}----{cookableItem.m_GearItem.GetInstanceID()}");

                    if (cookableItem.m_Recipe != null)
                    {
                        string recipeName = cookableItem.m_Recipe.name;
                        if (recipeDuplicates.ContainsKey(resultName))
                        {
                            if (activeRecipe[resultName] == recipeName)
                            {
                                __result = true;
                            }
                            else
                            {
                                __result = false;
                            }
                        }
                        else if (Main.blueprintsRemoved.Contains(recipeName))
                        {
                            __result = false;
                        }
                    }
                    else
                    {
                        if (Main.blueprintsRemoved.Contains(resultName))
                        {
                            __result = false;
                        }
                        else if (cookableItemDuplicates.ContainsKey(resultName))
                        {
                            if (activeCookableItem[resultName] == cookableItem.m_GearItem.GetInstanceID())
                            {
                                __result = true;
                            }
                            else
                            {
                                __result = false;
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Panel_Cooking), nameof(Panel_Cooking.Update))]
        internal static class Panel_Cooking_Update
        {
            private static void Postfix(Panel_Cooking __instance)
            {
                // ENABLE/DISABLE vanilla view (and the whole mod)
                if (InputManager.GetKeyDown(InputManager.m_CurrentContext, Settings.settings.viewKey))
                {
                    Main.vanillaDisplay = !Main.vanillaDisplay;
                    __instance.RefreshFoodList();
                }

                // SKIP 
                if (Main.vanillaDisplay) return;

                if (InputManager.GetKeyDown(InputManager.m_CurrentContext, Settings.settings.rightKey))
                {
                    if (__instance.GetSelectedCookableItem().m_Recipe != null)
                    {
                        string resultName = __instance.GetSelectedCookableItem().m_GearItem.name;
                        string recipeName = __instance.GetSelectedCookableItem().m_Recipe.name;
                        int recipeIndex = 0;

                        if (activeRecipe.ContainsKey(resultName))
                        {
                            int recipeCount = recipeDuplicates[resultName].Count;

                            for (int i = 0; i < recipeCount; i++)
                            {
                                if (recipeDuplicates[resultName][i] == recipeName) recipeIndex = i;
                            }

                            if (recipeIndex == recipeCount - 1)
                            {
                                activeRecipe[resultName] = recipeDuplicates[resultName][0];
                            }
                            else
                            {
                                activeRecipe[resultName] = recipeDuplicates[resultName][recipeIndex + 1];
                            }

                            lastRecipeSelected = activeRecipe[resultName];
                            __instance.RefreshFoodList();
                        }
                    }
                    else if (__instance.GetSelectedCookableItem().m_GearItem != null)
                    {
                        string resultName = __instance.GetSelectedCookableItem().m_GearItem.name;
                        int gearItemID = __instance.GetSelectedCookableItem().m_GearItem.GetInstanceID();
                        int cookableItemIndex = 0;

                        if (activeCookableItem.ContainsKey(resultName))
                        {
                            int cookableItemCount = cookableItemDuplicates[resultName].Count;

                            for (int i = 0; i < cookableItemCount; i++)
                            {
                                if (cookableItemDuplicates[resultName][i] == gearItemID) cookableItemIndex = i;
                            }

                            if (cookableItemIndex == cookableItemCount - 1)
                            {
                                activeCookableItem[resultName] = cookableItemDuplicates[resultName][0];
                            }
                            else
                            {
                                activeCookableItem[resultName] = cookableItemDuplicates[resultName][cookableItemIndex + 1];
                            }

                            lastCookableItemSelected = activeCookableItem[resultName];
                            __instance.RefreshFoodList();
                        }
                    }
                }
                else if (InputManager.GetKeyDown(InputManager.m_CurrentContext, Settings.settings.leftKey))
                {
                    if (__instance.GetSelectedCookableItem().m_Recipe != null)
                    {
                        string resultName = __instance.GetSelectedCookableItem().m_GearItem.name;
                        string recipeName = __instance.GetSelectedCookableItem().m_Recipe.name;
                        int recipeIndex = 0;

                        if (activeRecipe.ContainsKey(resultName))
                        {
                            int recipeCount = recipeDuplicates[resultName].Count;

                            for (int i = 0; i < recipeCount; i++)
                            {
                                if (recipeDuplicates[resultName][i] == recipeName) recipeIndex = i;
                            }

                            if (recipeIndex == 0)
                            {
                                activeRecipe[resultName] = recipeDuplicates[resultName][recipeCount - 1];
                            }
                            else
                            {
                                activeRecipe[resultName] = recipeDuplicates[resultName][recipeIndex - 1];
                            }

                            lastRecipeSelected = activeRecipe[resultName];
                            __instance.RefreshFoodList();
                        }
                    }
                    else if (__instance.GetSelectedCookableItem().m_GearItem != null)
                    {
                        string resultName = __instance.GetSelectedCookableItem().m_GearItem.name;
                        int gearItemID = __instance.GetSelectedCookableItem().m_GearItem.GetInstanceID();
                        int cookableItemIndex = 0;

                        if (activeCookableItem.ContainsKey(resultName))
                        {
                            int cookableItemCount = cookableItemDuplicates[resultName].Count;

                            for (int i = 0; i < cookableItemCount; i++)
                            {
                                if (cookableItemDuplicates[resultName][i] == gearItemID) cookableItemIndex = i;
                            }

                            if (cookableItemIndex == 0)
                            {
                                activeCookableItem[resultName] = cookableItemDuplicates[resultName][cookableItemCount - 1];
                            }
                            else
                            {
                                activeCookableItem[resultName] = cookableItemDuplicates[resultName][cookableItemIndex - 1];
                            }

                            lastCookableItemSelected = activeCookableItem[resultName];
                            __instance.RefreshFoodList();
                        }
                    }
                }
            }
        }

        // This patch will allow to keep the blueprint selected wether or not they are craftable
        [HarmonyPatch(typeof(Panel_Cooking), nameof(Panel_Cooking.RefreshFoodList))]
        internal static class Panel_Cooking_RefreshFoodList
        {
            private static void Postfix(Panel_Cooking __instance)
            {

                if (scrollValue != null)
                {
                    __instance.m_ScrollBehaviour.m_ScrollBar.scrollValue = (float)scrollValue;
                    scrollValue = null;
                }

                if (lastRecipeSelected != null)
                {
                    int i = 0;
                    foreach (CookableItem cookableItem in __instance.m_FoodList)
                    {
                        if (cookableItem.m_Recipe != null)
                        {
                            if (cookableItem.m_Recipe.name == lastRecipeSelected)
                            {
                                __instance.m_ScrollBehaviour.SetSelectedIndex(i, true, true);
                                lastRecipeSelected = null;
                                return;
                            }
                        }
                        i = i + 1;
                    }
                    lastRecipeSelected = null;
                }
                else if (lastCookableItemSelected != null)
                {
                    int i = 0;
                    foreach (CookableItem cookableItem in __instance.m_FoodList)
                    {
                        if (cookableItem.m_GearItem.GetInstanceID() == lastCookableItemSelected)
                        {
                            __instance.m_ScrollBehaviour.SetSelectedIndex(i, true, true);
                            lastCookableItemSelected = null;
                            return;
                        }
                        i = i + 1;
                    }
                    lastCookableItemSelected = null;
                }
            }
        }

        // This allow to update the name displayed
        [HarmonyPatch(typeof(CookableItem), nameof(CookableItem.GetDisplayName))]
        internal static class CookableItem_GetDisplayName
        {
            private static void Postfix(CookableItem __instance, ref string __result)
            {

                // SKIP 
                if (Main.vanillaDisplay) return;

                string itemName = __instance.m_GearItem.name;
                int rank = 0;
                int i = 0;

                if (__instance.m_Recipe != null)
                {
                    if (recipeDuplicates.ContainsKey(itemName))
                    {
                        foreach (string recipe in recipeDuplicates[itemName])
                        {
                            i += 1;
                            if (__instance.m_Recipe.name == recipe)
                            {
                                rank = i;
                            }
                        }
                        __result += $" [{rank}/{i}]";
                    }
                }
                else
                {
                    if (cookableItemDuplicates.ContainsKey(itemName))
                    {
                        foreach (int cookableItemID in cookableItemDuplicates[itemName])
                        {
                            i += 1;
                            if (__instance.m_GearItem.GetInstanceID() == cookableItemID)
                            {
                                rank = i;
                            }
                        }
                        __result += $" [{rank}/{i}]";
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ScrollBehaviourItem), nameof(ScrollBehaviourItem.OnClick))]
        internal static class ScrollBehaviourItem_OnItemClick
        {
            private static void Postfix(ScrollBehaviourItem __instance)
            {
                string scene = GameManager.m_ActiveScene;
                if (string.IsNullOrEmpty(scene)
                    || scene.Equals("Boot", System.StringComparison.OrdinalIgnoreCase)
                    || scene.Equals("Empty", System.StringComparison.OrdinalIgnoreCase)
                    || scene.StartsWith("MainMenu", System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (__instance == null || __instance.gameObject == null) return;

                // If LeftAlt key is pressed and Panel_Cooking is active
                if (Input.GetKey(Settings.settings.HoldKey))
                {
                    if (InterfaceManager.TryGetPanel<Panel_Cooking>(out Panel_Cooking panel))
                    {
                        // Try to get CookableListItem component
                        if (__instance.gameObject.TryGetComponent<CookableListItem>(out CookableListItem cookableListItem))
                        {
                            if (cookableListItem.m_Cookable != null)
                            {
                                scrollValue = panel.m_ScrollBehaviour.m_ScrollBar.scrollValue;

                                string cookable = cookableListItem.m_Cookable.m_Recipe != null ? cookableListItem.m_Cookable.m_Recipe.name : cookableListItem.m_Cookable.m_GearItem.name;

                                if (Main.blueprintsRemoved.Contains(cookable))
                                {
                                    Main.blueprintsRemoved.Remove(cookable);
                                    if (cookableListItem.m_Cookable.m_Recipe != null)
                                    {
                                        string resultName = cookableListItem.m_Cookable.m_GearItem.name;
                                        if (recipeDuplicates.ContainsKey(resultName))
                                        {
                                            if (recipeDuplicates[resultName] == null || recipeDuplicates[resultName].Count == 0)
                                            {
                                                recipeDuplicates[resultName] = new Il2CppSystem.Collections.Generic.List<string>();
                                                recipeDuplicates[resultName].Add(cookable);
                                            }
                                            else
                                            {
                                                recipeDuplicates[resultName].Add(cookable);
                                            }
                                        }
                                    }
                                    else if (cookableListItem.m_Cookable.m_GearItem != null)
                                    {
                                        string resultName = cookableListItem.m_Cookable.m_GearItem.name;
                                    }
                                }
                                else
                                {
                                    Main.blueprintsRemoved.Add(cookable);
                                    if (cookableListItem.m_Cookable.m_Recipe != null)
                                    {
                                        string resultName = cookableListItem.m_Cookable.m_GearItem.name;
                                        if (recipeDuplicates.ContainsKey(resultName))
                                        {
                                            recipeDuplicates[resultName].Remove(cookable);
                                            if (recipeDuplicates[resultName] == null || recipeDuplicates[resultName].Count == 0)
                                            {
                                                activeRecipe[resultName] = "";
                                            }
                                            else
                                            {
                                                activeRecipe[resultName] = recipeDuplicates[resultName][0];
                                            }
                                        }
                                    }
                                    else if (cookableListItem.m_Cookable.m_GearItem != null)
                                    {
                                        string resultName = cookableListItem.m_Cookable.m_GearItem.name;
                                    }
                                }
                                panel.RefreshFoodList();
                                Main.SaveListToJson(Main.blueprintsRemoved);
                            }
                        }
                    }
                }
            }
        }
        /////////////////////////////////////////////////////////////////////////////////
        // ---------------------  CRAFTING & COOKING PATCHES  ------------------------ //
        /////////////////////////////////////////////////////////////////////////////////

        [HarmonyPatch(typeof(ScrollBehaviour), nameof(ScrollBehaviour.RefreshItems))]
        internal static class ScrollBehaviour_RefreshItems
        {
            private static void Postfix(ScrollBehaviour __instance)
            {
                if (__instance.m_VisibleItems != null)
                {
                    for (int i = 0; i < __instance.m_VisibleItems.Count; i++)
                    {

                        if (__instance.m_VisibleItems[i].TryGetComponent<BlueprintDisplayItem>(out BlueprintDisplayItem blueprint))
                        {
                            var item = blueprint;
                            item.m_Button.SetState(UIButtonColor.State.Normal, true);

                            if (item.m_DisplayName.mText == "")
                            {
                                item.m_Button.mDefaultColor = new Color(0.235f, 0.235f, 0.235f, 0.274f);
                                item.m_Button.defaultColor = new Color(0.235f, 0.235f, 0.235f, 0.274f);
                                item.m_Button.mStartingColor = new Color(0.235f, 0.235f, 0.235f, 0.274f);
                                item.m_Button.hover = new Color(0.431f, 0.513f, 0.486f, 0.078f);
                            }
                            else if (Main.blueprintsRemoved.Contains(item.m_BlueprintData.name))
                            {
                                item.m_Button.mDefaultColor = new Color(1f, 0.5f, 0.5f, 0.2f);
                                item.m_Button.defaultColor = new Color(1f, 0.5f, 0.5f, 0.2f);
                                item.m_Button.mStartingColor = new Color(1f, 0.5f, 0.5f, 0.2f);
                                item.m_Button.hover = new Color(1f, 0.5f, 0.5f, 0.2f);
                            }
                            else
                            {
                                item.m_Button.mDefaultColor = new Color(0.235f, 0.235f, 0.235f, 0.274f);
                                item.m_Button.defaultColor = new Color(0.235f, 0.235f, 0.235f, 0.274f);
                                item.m_Button.mStartingColor = new Color(0.235f, 0.235f, 0.235f, 0.274f);
                                item.m_Button.hover = new Color(0.431f, 0.513f, 0.486f, 0.078f);
                            }
                        }
                        else if (__instance.m_VisibleItems[i].TryGetComponent<CookableListItem>(out CookableListItem cookable))
                        {
                            var item = cookable;
                            item.m_Button.SetState(UIButtonColor.State.Normal, true);

                            if (item.m_Cookable.m_Recipe == null)
                            {
                                if (item.m_Cookable.m_GearItem == null) return;

                                if (Main.blueprintsRemoved.Contains(item.m_Cookable.m_GearItem.name))
                                {
                                    item.m_Button.mDefaultColor = new Color(1f, 0.5f, 0.5f, 0.2f);
                                    item.m_Button.defaultColor = new Color(1f, 0.5f, 0.5f, 0.2f);
                                    item.m_Button.mStartingColor = new Color(1f, 0.5f, 0.5f, 0.2f);
                                    item.m_Button.hover = new Color(1f, 0.5f, 0.5f, 0.2f);
                                }
                                else
                                {
                                    item.m_Button.mDefaultColor = new Color(0.235f, 0.235f, 0.235f, 0.274f);
                                    item.m_Button.defaultColor = new Color(0.235f, 0.235f, 0.235f, 0.274f);
                                    item.m_Button.mStartingColor = new Color(0.235f, 0.235f, 0.235f, 0.274f);
                                    item.m_Button.hover = new Color(0.431f, 0.513f, 0.486f, 0.078f);
                                }
                            }
                            else if (Main.blueprintsRemoved.Contains(item.m_Cookable.m_Recipe.name))
                            {
                                item.m_Button.mDefaultColor = new Color(1f, 0.5f, 0.5f, 0.2f);
                                item.m_Button.defaultColor = new Color(1f, 0.5f, 0.5f, 0.2f);
                                item.m_Button.mStartingColor = new Color(1f, 0.5f, 0.5f, 0.2f);
                                item.m_Button.hover = new Color(1f, 0.5f, 0.5f, 0.2f);
                            }
                            else
                            {
                                item.m_Button.mDefaultColor = new Color(0.235f, 0.235f, 0.235f, 0.274f);
                                item.m_Button.defaultColor = new Color(0.235f, 0.235f, 0.235f, 0.274f);
                                item.m_Button.mStartingColor = new Color(0.235f, 0.235f, 0.235f, 0.274f);
                                item.m_Button.hover = new Color(0.431f, 0.513f, 0.486f, 0.078f);
                            }
                        }
                        else
                        {
                            return;
                        }
                    }
                }
            }
        }
    }
}