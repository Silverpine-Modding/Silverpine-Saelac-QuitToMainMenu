using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using Silverpine.ModdingTools;
using UnityEngine;

namespace Silverpine.QuitToMainMenu;

/// <summary>Session notifications for mods with state outside native or ModdingTools saves.</summary>
public static class MenuReturnEvents
{
    public static bool IsReturning => SessionReturn.Busy;
    public static event Action? Returning;
    public static event Action? Returned;
    internal static void Notify(bool finished)
    {
        Delegate[] callbacks = (finished ? Returned : Returning)?.GetInvocationList() ?? Array.Empty<Delegate>();
        foreach (Action callback in callbacks)
        {
            // A pre-transition failure aborts before tearing down the old world.
            // A notification after restoration must not invalidate a good world.
            if (!finished) callback();
            else try { callback(); } catch (Exception e) { Plugin.Log.LogError(e); }
        }
    }
}

internal static class SessionReturn
{
    internal static bool Busy { get; private set; }
    internal static bool RestoringStartup { get; private set; }
    internal static bool HasReturned { get; private set; }
    internal static string NextRunId { get; private set; } = "";
    internal static string? StartupPath { get; private set; }
    private static bool captureAttempted;

    internal static void CaptureStartup()
    {
        if (captureAttempted || Busy || MainMenuUI.Instance == null || !MainMenuUI.Instance.IsOpen()) return;
        captureAttempted = true;
        string? path = null;
        try
        {
            path = Snapshots.CreatePath("startup");
            Snapshots.CaptureStartup(path);
            StartupPath = path;
            Plugin.Log.LogInfo("Captured clean startup world before the first run.");
        }
        catch (Exception e)
        {
            Snapshots.Delete(path);
            Plugin.Log.LogError("Quit to Main Menu disabled for this session: startup capture failed. " + e);
        }
    }

    internal static bool CanReturn => !Busy && StartupPath != null && File.Exists(StartupPath) &&
        Player.Instance != null && SaveUI.Instance != null && MainMenuUI.Instance != null &&
        !MainMenuUI.Instance.IsOpen() && !SerializationManager.loadingSave &&
        ActionQueue.Instance != null && !ActionQueue.Instance.turnBeingProcessed &&
        !ActionQueue.Instance.processTurnRequested && !NeuralNPC.npcFunctionsBeingInvoked &&
        (DialogBox.Instance == null || !DialogBox.Instance.isOpen) &&
        !SaveUI.Instance.IsSavingBlocked() && InventoryModTools.ActiveToolId == null &&
        !PauseMenuManager.Instance.exclusiveMode;

    internal static IEnumerator Return()
    {
        if (!CanReturn) yield break;
        Busy = true;
        string? recovery = null;
        Exception? failure = null;
        bool worldTouched = false;
        try
        {
            recovery = Snapshots.CreatePath("recovery");
            Snapshots.SaveRecovery(recovery);
            MenuReturnEvents.Notify(false);
        }
        catch (Exception e) { failure = e; }

        if (failure == null)
        {
            PauseMenuManager.Instance.Close();
            yield return BlackScreen.Instance.FadeInCoroutine();
            try
            {
                ActionQueue.Instance.StopAllCoroutines();
                ActionQueue.Instance.processTurnRequested = false;
                ActionQueue.Instance.ensureNoZeroFrameTurnRequested = false;
                SaveUI.Instance.StopAllCoroutines();
                Compatibility.RemoveRetiredPlayerCallbacks();
                MainMenuUI.Instance.StopAllCoroutines();
                MainMenuUI.Instance.transform.parent.gameObject.SetActive(true);
                MainMenuUI.Instance.otherUI.gameObject.SetActive(false);
                NextRunId = Guid.NewGuid().ToString();
                RestoringStartup = true;
                worldTouched = true;
                // Uses normal load prefixes/postfixes: ModdingTools.ResetForNewGame,
                // MonsterDialogue visit reset, structures/layers, and native completion events.
                Snapshots.Load(StartupPath!);
                if ((string)Native.RunId.GetValue(SaveUI.Instance) != NextRunId)
                    throw new InvalidOperationException("Startup run identity was not replaced.");
                HasReturned = true;
            }
            catch (Exception e) { failure = e; }

            // Let native object Starts, UI subscriptions and the delayed input reset settle.
            if (failure == null)
            {
                for (int frame = 0; frame < 15; frame++) yield return null;
                try
                {
                    ModSaveData.ResetForNewGame(); // Detach the temporary snapshot path as well.
                    Compatibility.PrepareAnotherNewGame();
                    MenuPresentation.Open();
                    MenuReturnEvents.Notify(true);
                    Plugin.Log.LogInfo("Returned to main menu with a fresh startup identity: " + NextRunId);
                }
                catch (Exception e) { failure = e; }
            }
        }

        RestoringStartup = false;
        if (failure != null)
        {
            Plugin.Log.LogError("Return to menu failed: " + failure);
            if (worldTouched)
            {
                try
                {
                    SerializationManager.loadingSave = false;
                    Compatibility.RemoveRetiredPlayerCallbacks();
                    MainMenuUI.Instance.Close();
                    Snapshots.Load(recovery!);
                    Plugin.Log.LogWarning("Restored the pre-return recovery snapshot.");
                }
                catch (Exception recoveryFailure)
                {
                    Plugin.Log.LogError("Recovery snapshot retained at " + recovery + ": " + recoveryFailure);
                    Busy = false;
                    SerializationManager.loadingSave = false;
                    BlackScreen.Instance.FadeOut();
                    DialogBox.Instance.DisplayExceptionDialog("Could not return to the main menu. Recovery snapshot: " + recovery,
                        null, recoveryFailure);
                    yield break;
                }
            }
        }
        Busy = false;
        SerializationManager.loadingSave = false;
        Snapshots.Delete(recovery);
        if (failure != null)
        {
            yield return BlackScreen.Instance.FadeOutCoroutine();
            DialogBox.Instance.DisplayExceptionDialog("Could not return to the main menu; your current run was kept.", null, failure);
        }
        // On success CameraPanCoroutine handles the fade out.
    }

    internal static void Shutdown() => Snapshots.Delete(StartupPath);
}

internal static class Compatibility
{
    internal static void RemoveRetiredPlayerCallbacks()
    {
        // Player.Start attaches an anonymous handler but OnDestroy never detaches
        // it. Replaying character creation after a load otherwise calls old players.
        var field = AccessTools.Field(typeof(MainMenuUI), "OnCharacterCreationFinished");
        var handlers = field.GetValue(MainMenuUI.Instance) as Action;
        if (handlers == null) return;
        foreach (Action handler in handlers.GetInvocationList())
            if (handler.Target is Player) handlers -= handler;
        field.SetValue(MainMenuUI.Instance, handlers);
    }

    internal static void PrepareAnotherNewGame()
    {
        // Starting Inventory 1.0.1 keys its one-shot guard to the retained menu.
        // Remove only this menu's entry, preserving that mod's configuration.
        Type? type = FindOptionalType("SilverpineMods.StartingInventory.NewCharacterLoadoutPatch");
        object? table = type == null ? null : AccessTools.Field(type, "handled")?.GetValue(null);
        table?.GetType().GetMethod("Remove")?.Invoke(table, new object[] { MainMenuUI.Instance });

    }

    internal static void RefreshCharacterList()
    {
        // The character-list mod caches native portraits which Finish unloaded.
        Type? selectorType = FindOptionalType("SilverpineMods.QOLCharSelector.CharacterSelectionListController");
        if (selectorType == null) return;
        foreach (var component in MainMenuUI.Instance.GetComponentsInChildren(selectorType, true))
        {
            object? cache = AccessTools.Field(selectorType, "builtInSprites")?.GetValue(component);
            cache?.GetType().GetMethod("Clear")?.Invoke(cache, null);
            AccessTools.Method(selectorType, "RebuildRows")?.Invoke(component, null);
        }
    }

    private static Type? FindOptionalType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(name, false);
            if (type != null) return type;
        }
        return null;
    }
}
