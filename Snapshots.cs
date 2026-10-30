using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace Silverpine.QuitToMainMenu;

internal static class Snapshots
{
    internal static string CreatePath(string purpose)
    {
        string folder = Path.Combine(BepInEx.Paths.CachePath, "QuitToMainMenu");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, purpose + "-" + Guid.NewGuid().ToString("N") + ".sav");
    }

    internal static void CaptureStartup(string path)
    {
        // Use the native serializers (including prefab/filter patches), but do not
        // publish a save event for the throwaway main-menu ID or create save metadata.
        using var errors = new SerializationErrors();
        Player player = Player.Instance;
        string? originalName = player.playerName;
        try
        {
            // Startup has no named player yet; VersionSafeStorage cannot encode null
            // strings. Supply an empty name solely while serializing the menu world.
            player.playerName ??= "";
            var objects = (HashSet<GameObject>)AccessTools.Method(typeof(SerializationManager), "GetGameObjectsForSerialization").Invoke(null, null);
            byte[] world = SerializationManager.SerializeSerializable(objects);
            byte[] persistent = (byte[])AccessTools.Method(typeof(SerializationManager), "SerializePersistent").Invoke(null, null);
            errors.ThrowIfAny();
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.WriteCountedByteArray(world);
            writer.WriteCountedByteArray(persistent);
        }
        finally { player.playerName = originalName; }
    }

    internal static void SaveRecovery(string path)
    {
        using var errors = new SerializationErrors();
        // Includes ModdingTools companion data and other mods' normal save hooks.
        SerializationManager.Save(path);
        errors.ThrowIfAny();
    }

    internal static void Load(string path)
    {
        using var errors = new SerializationErrors();
        SerializationManager.Load(path);
        errors.ThrowIfAny();
    }

    internal static void Delete(string? path)
    {
        if (path == null) return;
        foreach (string suffix in new[] { "", ".moddingtools", ".moddingtools.bak" })
        {
            try { if (File.Exists(path + suffix)) File.Delete(path + suffix); }
            catch (Exception e) { Plugin.Log.LogWarning("Could not remove temporary snapshot: " + e.Message); }
        }
    }
}

// Native serialization catches component errors and continues. Do not mistake a
// partial snapshot or partial restore for a successful session transition.
internal sealed class SerializationErrors : IDisposable
{
    private readonly List<string> errors = new();
    internal SerializationErrors() => Application.logMessageReceived += Receive;
    private void Receive(string message, string stack, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Exception) &&
            (message.Contains("serializing") || message.Contains("deserialized") || message.Contains("Failed AddToStorage Serialize") ||
             message.Contains("Missing prefab:") || message.Contains("Missing persistent:") ||
             message.Contains("Missing serializable monobehavior:") || message.Contains("Component count mismatch") ||
             message.Contains("CustomNPCManager: Failed to spawn")))
            errors.Add(message);
    }
    internal void ThrowIfAny()
    {
        if (errors.Count != 0) throw new InvalidDataException(string.Join("\n", errors));
    }
    public void Dispose() => Application.logMessageReceived -= Receive;
}
