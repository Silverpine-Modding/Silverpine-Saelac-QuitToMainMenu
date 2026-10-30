using System;
using System.Collections;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Silverpine.QuitToMainMenu;

[BepInPlugin(Guid, Name, Version)]
[BepInDependency(Silverpine.ModdingTools.Plugin.PluginGuid, "1.12.1")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "Saelac.Silverpine.QuitToMainMenu";
    public const string Name = "Quit to Main Menu";
    public const string Version = "1.0.2";
    internal static ManualLogSource Log = null!;

    private void Awake()
    {
        Log = Logger;
#if RUNTIME_TESTS
        RuntimeProbe.Install();
#endif
        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("Quit to Main Menu installed; native save-load lifecycle and ModdingTools 1.12.1 integration enabled.");
    }
    // Silverpine destroys the bootstrap plugin host. Process-wide patches remain installed.
}

[HarmonyPatch(typeof(InventoryUI), "Start")]
internal static class InventoryReadyPatch
{
    private static void Postfix(InventoryUI __instance)
    {
        if (__instance.GetComponent<ReturnController>() == null)
            __instance.gameObject.AddComponent<ReturnController>().Initialize(__instance);
    }
}

// These execute when the enumerator is created, before the first fade, season change,
// loadout patch, or load. Never capture a played world as the startup baseline.
[HarmonyPatch(typeof(MainMenuUI), "FinishCharacterCreationCoroutine")]
internal static class CaptureNewGamePatch
{
    [HarmonyPriority(Priority.First)]
    [HarmonyBefore("Saelac.Silverpine.StartingInventory")]
    private static void Prefix() => SessionReturn.CaptureStartup();
}

[HarmonyPatch(typeof(SaveUI), "Load")]
internal static class CaptureBeforeLoadPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix() => SessionReturn.CaptureStartup();
}

[HarmonyPatch(typeof(MainMenuUI), "Finish")]
internal static class MenuFinishPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix() => !SessionReturn.RestoringStartup;
    private static void Postfix()
    {
        if (!SessionReturn.RestoringStartup) MenuPresentation.ReleasePortraits();
    }
}

[HarmonyPatch(typeof(SaveUI), nameof(SaveUI.Deserialize))]
internal static class FreshIdentityPatch
{
    // Runs before native load-completion events and mod identity refreshes.
    private static void Postfix(SaveUI __instance)
    {
        if (SessionReturn.RestoringStartup)
            Native.RunId.SetValue(__instance, SessionReturn.NextRunId);
    }
}

[HarmonyPatch(typeof(CustomNPCManager), "SpawnInNPCs")]
internal static class DeferMenuNpcSpawningPatch
{
    // Restoring the pristine world fires the normal load-completion event. The
    // original startup menu has no spawned custom NPCs yet; let the next New Game
    // or actual save load use the player's current enabled/disabled definitions.
    // Otherwise New Game registers those same NPCs twice in NeuralNPC.neuralNPCs.
    [HarmonyPriority(Priority.First)]
    private static bool Prefix() => !SessionReturn.RestoringStartup;
}

[HarmonyPatch(typeof(Player), nameof(Player.IsInputBlocked))]
internal static class MenuInputPatch
{
    private static void Postfix(ref bool __result)
    {
        // Player.Deserialize resets its counter ten frames later. A visible returned
        // menu must remain modal even if that delayed callback is still pending.
        if (SessionReturn.Busy || (SessionReturn.HasReturned && MainMenuUI.Instance != null && MainMenuUI.Instance.IsOpen()))
            __result = true;
    }
}

[HarmonyPatch(typeof(ActionQueue), nameof(ActionQueue.ProcessTurn))]
internal static class MenuTurnPatch
{
    private static bool Prefix() => !SessionReturn.Busy &&
        !(SessionReturn.HasReturned && MainMenuUI.Instance != null && MainMenuUI.Instance.IsOpen());
}

internal static class Native
{
    internal static readonly System.Reflection.FieldInfo RunId = AccessTools.Field(typeof(SaveUI), "runID");
    internal static T Field<T>(object instance, string name) => (T)AccessTools.Field(instance.GetType(), name).GetValue(instance);
    internal static void Set(object instance, string name, object? value) => AccessTools.Field(instance.GetType(), name).SetValue(instance, value);
    internal static object? Call(object instance, string name, params object[] args) => AccessTools.Method(instance.GetType(), name).Invoke(instance, args);
}
