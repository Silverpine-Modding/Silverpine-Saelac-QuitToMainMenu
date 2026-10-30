#if RUNTIME_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Silverpine.ModdingTools;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Silverpine.QuitToMainMenu;

internal static class RuntimeProbe
{
    private static readonly List<string> report = new();
    private static string root = "";
    private static int payload;
    private static bool failNextLoad;
    private static float deadline;
    private static bool started;
    private static readonly List<string> npcErrors = new();
    private static readonly List<CustomContentDefinition_NPC> npcFixtures = new();

    internal static void Install()
    {
        root = BepInEx.Paths.GameRootPath;
        if (!File.Exists(Path.Combine(root, "quit-to-menu-test.instance")) ||
            Path.GetFullPath(Application.persistentDataPath) != Path.Combine(root, "TestData"))
        {
            Plugin.Log.LogError("Runtime tests require an isolated, marked game copy. Exiting before scene startup.");
            Application.Quit(2);
            throw new InvalidOperationException("Test isolation unavailable.");
        }
        var harmony = new Harmony(Plugin.Guid + ".tests");
        harmony.Patch(AccessTools.Method(typeof(InferenceServerSetupHandler), "SetUp"), prefix: new HarmonyMethod(typeof(RuntimeProbe), nameof(SkipInference)));
        harmony.Patch(AccessTools.Method(typeof(SerializationManager), "Load"), prefix: new HarmonyMethod(typeof(RuntimeProbe), nameof(FailLoad)));
        Application.logMessageReceived += (message, stack, type) =>
        {
            if ((type == LogType.Error || type == LogType.Exception) &&
                message.Contains("CustomNPCManager") && message.Contains("Failed to spawn"))
                npcErrors.Add(message);
        };
    }

    private static bool SkipInference() => false;
    private static void FailLoad(string path)
    {
        if (failNextLoad && path == SessionReturn.StartupPath)
        {
            failNextLoad = false;
            throw new InvalidDataException("Expected test failure: startup load interrupted.");
        }
    }
    internal static void StartOn(ReturnController host)
    {
        if (started) return;
        started = true;
        deadline = Time.realtimeSinceStartup + 240;
        host.StartCoroutine(Guard(Run(host)));
    }

    private static IEnumerator Guard(IEnumerator run)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(run);
        bool failed = false;
        while (stack.Count != 0)
        {
            object? next = null;
            try
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Runtime checks timed out.");
                var current = stack.Peek();
                if (!current.MoveNext()) { stack.Pop(); continue; }
                next = current.Current;
            }
            catch (Exception e)
            {
                report.Add("FAIL: " + e);
                Plugin.Log.LogError(e);
                failed = true;
            }
            if (failed) break;
            if (next is IEnumerator nested) stack.Push(nested);
            else yield return next;
        }
        report.Add(failed ? "FAILED" : "ALL RUNTIME CHECKS PASSED");
        File.WriteAllLines(Path.Combine(root, "runtime-report.txt"), report);
        Application.Quit(failed ? 1 : 0);
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++) yield return null;
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        report.Add("PASS: " + message);
        Plugin.Log.LogInfo("TEST PASS: " + message);
    }
    private static string RunId => (string)Native.RunId.GetValue(SaveUI.Instance);
    private static void CreateNpcFixtures()
    {
        foreach (var definition in CustomContentDefinition_NPC.loaded.Values) definition.enabled = false;
        string characterName = CustomContentDefinition_PlayerCharacter.loaded.Keys.First();
        int activeCount = Enum.IsDefined(typeof(NPCName), "CustomNPCSlot9") ? 10 : 2;
        for (int i = 0; i <= activeCount; i++)
        {
            var definition = new CustomContentDefinition_NPC
            {
                customContentName = "Menu Test NPC " + i,
                customPlayerCharacterDefinitionName = characterName,
                enabled = i < activeCount,
                spawnPosition = new Vector2Int(10 + i, 0),
                species = "Fixture species " + i,
                occupation = "Fixture occupation",
                walkUpChance = CustomContentDefinition_NPC.WalkUpChance.None
            };
            definition.Load();
            npcFixtures.Add(definition);
        }
        Check(CustomNPCHandler.customNPCHandlers.Count == 0, "Initial menu has no spawned custom NPCs");
        Check(npcFixtures.Count(n => n.enabled) == activeCount, $"Enabled {activeCount} custom NPC fixtures and one disabled alternative");
    }
    private static void CheckNpcRoster(string phase)
    {
        Check(npcErrors.Count == 0, phase + ": no custom NPC spawn failures" +
            (npcErrors.Count == 0 ? "" : ": " + string.Join(" | ", npcErrors)));
        var handlers = CustomNPCHandler.customNPCHandlers.ToArray();
        var expected = npcFixtures.Where(n => n.enabled).ToArray();
        Check(handlers.Length == expected.Length && handlers.Select(n => n.slot).Distinct().Count() == handlers.Length &&
            expected.All(d => handlers.Count(n => n.customContentName == d.customContentName) == 1),
            phase + ": exactly one NPC per enabled definition, none for disabled definitions");
        Check(handlers.All(h => NeuralNPC.neuralNPCs.TryGetValue(h.slot, out var npc) && npc == h.GetComponent<NeuralNPC>() &&
            npc.species == CustomContentDefinition_NPC.loaded[h.customContentName].species),
            phase + ": registry identities and configured properties match every custom NPC");
    }
    private static void CheckMenuNpcState()
    {
        Check(CustomNPCHandler.customNPCHandlers.Count == 0 && !NeuralNPC.neuralNPCs.Values.Any(n => n.IsCustomNPC()),
            "Returned menu defers custom NPC spawning until New Game or Load");
    }
    private static void SwitchNpcSelection(bool alternate)
    {
        npcFixtures[0].enabled = !alternate;
        npcFixtures[npcFixtures.Count - 1].enabled = alternate;
    }
    private static NeuralNPC PersistentFixtureNpc => CustomNPCHandler.customNPCHandlers
        .Single(n => n.customContentName == npcFixtures[1].customContentName).GetComponent<NeuralNPC>();
    private static IEnumerator StartGame(bool custom = false)
    {
        Native.Field<Button>(MainMenuUI.Instance, "newGameButton").onClick.Invoke();
        if (custom)
        {
            var field = MainMenuUI.Instance.characterSelectionUI.GetComponentsInChildren<CharacterField>(true).First(f => f.customCharacter);
            MainMenuUI.Instance.characterSelectionUI.SetIndex(field.transform.GetSiblingIndex());
        }
        Check(Native.Field<TMP_InputField>(MainMenuUI.Instance, "nameInputField").IsInteractable(), "Character creation controls are interactive");
        Native.Field<TMP_InputField>(MainMenuUI.Instance, "nameInputField").text = "MenuTest";
        Native.Field<Button>(MainMenuUI.Instance, "finishButton").onClick.Invoke();
        while (MainMenuUI.Instance.IsOpen()) yield return null;
        while (BlackScreen.Instance.IsActive()) yield return null;
        yield return Frames(20);
        if (DialogBox.Instance.isOpen) DialogBox.Instance.CloseBox();
        if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("Saelac.Silverpine.StartingInventory"))
            Check(Player.Instance.worldInventory.inventory.gold == 9999, "Starting Inventory applies on every New Game");
        CheckNpcRoster("New Game");
    }
    private static void OpenInventory()
    {
        if (!PauseMenuManager.Instance.open) PauseMenuManager.Instance.Toggle();
        InventoryUI.Instance.OnRootEnabled();
    }
    private static IEnumerator ReturnToMenu()
    {
        OpenInventory();
        Check(SessionReturn.CanReturn, "Return is available from the personal inventory");
        yield return SessionReturn.Return();
        yield return Frames(30);
        while (BlackScreen.Instance.IsActive()) yield return null;
        Check(MainMenuUI.Instance.IsOpen() && !SessionReturn.Busy, "Return completes at the main menu");
        Check(Player.Instance.IsInputBlocked(), "Returned menu blocks player input after delayed load callbacks");
    }
    private static Rect ScreenRect(RectTransform rect)
    {
        var points = new Vector3[4];
        rect.GetWorldCorners(points);
        Canvas canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
        Camera? camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3 point in points)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, point);
            min = Vector2.Min(min, screen);
            max = Vector2.Max(max, screen);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static void CheckButtonLayout(ReturnController host)
    {
        Canvas.ForceUpdateCanvases();
        var status = UnityEngine.Object.FindObjectOfType<MiniResourcesUI>(true);
        var bars = new[] { "hpBar", "sanityBar", "hungerBar", "thirstBar", "restBar", "energyBar" }
            .Select(name => ScreenRect((RectTransform)Native.Field<Slider>(status, name).transform)).ToArray();
        Rect button = ScreenRect((RectTransform)host.ReturnButton.transform);
        float scale = status.GetComponentInParent<Canvas>().rootCanvas.scaleFactor;
        Check(Mathf.Abs(button.xMax - bars.Max(b => b.xMax)) < 1 &&
            Mathf.Abs(bars.Min(b => b.yMin) - button.yMax - 10 * scale) < 1,
            $"Button aligns just below status bars at {Screen.width}x{Screen.height}");
        Check(button.xMin >= 0 && button.xMax <= Screen.width && button.yMin >= 0 && button.yMax <= Screen.height,
            "Button stays entirely within screen bounds");
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = button.center }, hits);
        Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == host.ReturnButton,
            "Screen-position raycast reaches the quit button above the inventory");
    }
    private static IEnumerator CheckResolutions(ReturnController host)
    {
        foreach (Vector2Int size in new[] { new Vector2Int(1024, 768), new Vector2Int(1600, 900), new Vector2Int(1280, 800), new Vector2Int(1280, 720) })
        {
            Screen.SetResolution(size.x, size.y, false);
            yield return Frames(12);
            Check(Screen.width == size.x && Screen.height == size.y, $"Live resolution change to {size.x}x{size.y}");
            CheckButtonLayout(host);
            CaptureUi($"inventory-{size.x}x{size.y}");
        }
    }
    private static Button ConfirmationButton(string label) => GameObject.Find("Quit to Main Menu Confirmation Canvas")
        .GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<TextMeshProUGUI>().text == label);

    private static IEnumerator CheckMenuTabsAndLocks(ReturnController host)
    {
        var pause = PauseMenuManager.Instance;
        Button[] tabs = Native.Field<Button[]>(pause, "buttons");
        foreach (Button tab in tabs)
        {
            tab.onClick.Invoke();
            yield return Frames(3);
            string label = tab.GetComponentInChildren<TextMeshProUGUI>().text;
            Check(host.ReturnButton.IsActive() && host.ReturnButton.IsInteractable(), "Quit button is available on " + label);
            host.ReturnButton.onClick.Invoke();
            yield return Frames(3);
            Check(GameObject.Find("Quit to Main Menu Confirmation Canvas") != null, "Confirmation stays open on " + label);
            ConfirmationButton("Cancel").onClick.Invoke();
            yield return Frames(2);
        }

        bool[] states = tabs.Select(tab => tab.interactable).ToArray();
        foreach (Button tab in tabs) tab.interactable = false;
        yield return Frames(3);
        Check(host.ReturnButton.IsActive() && !host.ReturnButton.IsInteractable(), "Quit button stays visible but disabled with locked menu tabs");
        host.ReturnButton.onClick.Invoke();
        yield return Frames(2);
        Check(GameObject.Find("Quit to Main Menu Confirmation Canvas") == null, "Disabled menu rejects a direct quit click as well");
        for (int i = 0; i < tabs.Length; i++) tabs[i].interactable = states[i];
        yield return Frames(3);
        Check(host.ReturnButton.IsInteractable(), "Quit button unlocks when menu tabs unlock");

        CanvasGroup group = pause.GetComponent<CanvasGroup>();
        bool addedGroup = group == null;
        if (group == null) group = pause.gameObject.AddComponent<CanvasGroup>();
        bool oldInteractable = group.interactable;
        bool oldRaycasts = group.blocksRaycasts;
        group.interactable = false;
        yield return Frames(3);
        Check(!host.ReturnButton.IsInteractable(), "Quit button follows the menu CanvasGroup interaction lock");
        group.interactable = oldInteractable;
        group.blocksRaycasts = false;
        yield return Frames(3);
        Check(!host.ReturnButton.IsInteractable(), "Quit button follows the menu CanvasGroup raycast lock");
        group.blocksRaycasts = oldRaycasts;
        yield return Frames(3);

        host.ReturnButton.onClick.Invoke();
        yield return Frames(3);
        Button confirm = ConfirmationButton("Quit to Main Menu");
        group.interactable = false;
        yield return Frames(3);
        Check(!confirm.IsInteractable(), "Open confirmation also disables Quit when the menu becomes locked");
        ExecuteEvents.Execute(confirm.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
        yield return Frames(2);
        Check(!SessionReturn.Busy && !MainMenuUI.Instance.IsOpen() && GameObject.Find("Quit to Main Menu Confirmation Canvas") != null,
            "Clicking a locked confirmation cannot quit the active run");
        group.interactable = oldInteractable;
        if (addedGroup) UnityEngine.Object.Destroy(group);
        yield return Frames(3);
        Check(confirm.IsInteractable(), "Confirmation unlocks when the menu becomes available again");
        ConfirmationButton("Cancel").onClick.Invoke();
        yield return Frames(2);

        SaveUI.Instance.SetSaveBlock(true);
        yield return Frames(3);
        Check(host.ReturnButton.IsActive() && !host.ReturnButton.IsInteractable(), "Quit remains disabled during the native save block");
        SaveUI.Instance.SetSaveBlock(false);
        yield return Frames(3);

        Action? closeTool = null;
        InventoryModTools.Register("test.return-menu.lock", "Menu Lock Test", (_, close) => closeTool = close);
        Check(InventoryModTools.TryOpen("test.return-menu.lock"), "Opened a real ModdingTools editor session");
        yield return Frames(3);
        Check(host.ReturnButton.IsActive() && !host.ReturnButton.IsInteractable(), "Quit stays disabled with the other buttons during a ModdingTools session");
        closeTool!();
        InventoryModTools.Unregister("test.return-menu.lock");
        yield return Frames(3);
        Check(host.ReturnButton.IsInteractable(), "Quit unlocks after the ModdingTools session closes");

        // Perform the suite's actual confirmed return from a non-inventory tab.
        tabs.Single(tab => tab.GetComponentInChildren<TextMeshProUGUI>().text == "Settings").onClick.Invoke();
        yield return Frames(3);
        Check(!InventoryUI.Instance.Root.activeInHierarchy && host.ReturnButton.IsInteractable(), "Settings supports returning while the inventory root is hidden");
    }
    private static IEnumerator Run(ReturnController host)
    {
        while (MainMenuUI.Instance == null || !MainMenuUI.Instance.GetComponent<CanvasGroup>().interactable ||
            Native.Field<List<string>?>(MainMenuUI.Instance, "customContentErrors") != null) yield return null;
        yield return Frames(12);
        Check(Application.persistentDataPath == Path.Combine(root, "TestData"), "Persistent data is isolated from the user's profile");
        CreateNpcFixtures();
        if (File.Exists(Path.Combine(root, "quit-to-menu-test.npc-regression")))
        {
            yield return StartGame();
            yield return ReturnToMenu();
            yield return StartGame();
            yield return ReturnToMenu();
            CheckMenuNpcState();
            yield break;
        }
        ModSaveData.Register("test.return-menu", new ModSaveDataDefinition
        {
            Id = "test.return-menu.payload", CurrentVersion = 1,
            Capture = () => payload.ToString(), Restore = text => payload = int.Parse(text), Reset = () => payload = 0
        });
        int startingGold = Player.Instance.worldInventory.inventory.gold;
        yield return StartGame();
        Check(SessionReturn.StartupPath != null && File.Exists(SessionReturn.StartupPath), "Pristine snapshot captured before character creation");
        string firstRun = RunId;
        Player.Instance.worldInventory.inventory.gold = 1234;
        PersistentFixtureNpc.relationshipLevel = 37;
        payload = 41;
        Native.Field<TMP_InputField>(SaveUI.Instance, "saveNameInputField").text = "Menu Return Fixture";
        Type saveType = typeof(SaveUI).GetNestedType("SaveType", BindingFlags.NonPublic);
        Native.Call(SaveUI.Instance, "Save", Enum.ToObject(saveType, 0));
        yield return Frames(15);
        Check(Directory.GetFiles(Path.Combine(Application.persistentDataPath, "Saves"), "*.savmeta").Length == 1, "Native save creates exactly one visible fixture");
        Player.Instance.worldInventory.inventory.gold = 9876;
        PersistentFixtureNpc.relationshipLevel = 77;
        payload = 99;
        OpenInventory();
        yield return Frames(4);
        Check(host.ReturnButton.IsActive() && host.ReturnButton.interactable, "Inventory button is visible and usable");
        var rect = (RectTransform)host.ReturnButton.transform;
        Check(rect.anchorMax == Vector2.one && rect.pivot == Vector2.one, "Button uses the HUD canvas's top-right anchors");
        CheckButtonLayout(host);
        yield return CheckResolutions(host);
        PauseMenuManager.Instance.Close();
        yield return Frames(3);
        Check(!host.ReturnButton.gameObject.activeSelf, "Quit button hides when the in-game menus close");
        OpenInventory();
        yield return Frames(3);
        yield return CheckMenuTabsAndLocks(host);
        for (Transform node = rect; node != null; node = node.parent)
        {
            if (node is RectTransform layout)
                report.Add("UI: " + node.name + " rect=" + layout.rect + " anchors=" + layout.anchorMin + "/" + layout.anchorMax + " position=" + layout.anchoredPosition);
        }
        report.Add("Button components: " + string.Join(", ", host.ReturnButton.GetComponents<Component>().Select(c => c.GetType().Name)));
        CaptureUi("menus-settings");
        yield return Frames(4);
        host.ReturnButton.onClick.Invoke();
        yield return Frames(4);
        GameObject modal = GameObject.Find("Quit to Main Menu Confirmation Canvas");
        Check(modal != null, "Quit opens an unsaved-progress confirmation");
        CaptureUi("confirmation");
        modal!.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<TextMeshProUGUI>().text == "Cancel").onClick.Invoke();
        yield return Frames(3);
        Check(!MainMenuUI.Instance.IsOpen() && RunId == firstRun && Player.Instance.worldInventory.inventory.gold == 9876, "Cancel preserves the active run and unsaved progress");
        host.ReturnButton.onClick.Invoke();
        yield return Frames(3);
        modal = GameObject.Find("Quit to Main Menu Confirmation Canvas");
        modal.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<TextMeshProUGUI>().text == "Quit to Main Menu").onClick.Invoke();
        yield return Frames(3);
        while (SessionReturn.Busy) yield return null;
        yield return Frames(30);
        while (BlackScreen.Instance.IsActive()) yield return null;
        Check(MainMenuUI.Instance.IsOpen(), "Confirmed button returns to the existing main menu");
        Check(RunId != firstRun && !string.IsNullOrEmpty(RunId), "Return assigns a fresh startup run ID");
        string secondRun = RunId;
        Check(payload == 0, "ModdingTools reset clears unsaved run payload");
        Check(Player.Instance.worldInventory.inventory.gold == startingGold, "Startup world replaces modified inventory");
        Check(Player.Instance.IsInputBlocked(), "Startup menu remains modal after deserialization");
        Check(!host.ReturnButton.gameObject.activeSelf, "Quit button is hidden on the main menu");
        CheckMenuNpcState();
        foreach (CharacterField field in MainMenuUI.Instance.characterSelectionUI.GetComponentsInChildren<CharacterField>(true))
            Check((field.customCharacter ? field.GetComponentsInChildren<Image>(true)[1] : field.transform.Find("CharacterSprite").GetComponent<Image>()).sprite != null,
                "Portrait restored: " + field.defaultCharacterName);
        CaptureUi("returned-menu");
        yield return Frames(4);
        SwitchNpcSelection(alternate: true);
        SaveUI.Instance.Continue();
        yield return new WaitForSecondsRealtime(1.5f);
        while (BlackScreen.Instance.IsActive() || SerializationManager.loadingSave) yield return null;
        yield return Frames(15);
        Check(!MainMenuUI.Instance.IsOpen() && RunId == firstRun, "Continue restores the original run ID");
        Check(Player.Instance.worldInventory.inventory.gold == 1234 && payload == 41, "Continue restores native save and ModdingTools companion payload");
        CheckNpcRoster("Continue after changing enabled NPCs");
        Check(PersistentFixtureNpc.relationshipLevel == 37, "Continue retains saved state of still-enabled custom NPCs");
        yield return ReturnToMenu();
        CheckMenuNpcState();
        SwitchNpcSelection(alternate: false);
        string thirdRun = RunId;
        Check(thirdRun != firstRun && thirdRun != secondRun, "Repeated returns never reuse startup identities");
        yield return StartGame(custom: true);
        Check(PersistentFixtureNpc.relationshipLevel == 0, "New Game creates fresh custom NPC state after changing menu selections");
        Check(RunId == thirdRun && Player.Instance.worldInventory.inventory.gold != 1234 && payload == 0, "Second New Game does not inherit the loaded run");
        var handlers = (Action)AccessTools.Field(typeof(MainMenuUI), "OnCharacterCreationFinished").GetValue(MainMenuUI.Instance);
        Check(handlers.GetInvocationList().Count(h => h.Target is Player) == 1, "Character creation has only the current player's callback");
        yield return ReturnToMenu();
        CheckMenuNpcState();
        SwitchNpcSelection(alternate: true);
        yield return StartGame();
        Check(RunId != thirdRun, "Third New Game works in the same process");
        payload = 77;
        Player.Instance.worldInventory.inventory.gold = 4321;
        PersistentFixtureNpc.relationshipLevel = 63;
        string recoveryId = RunId;
        failNextLoad = true;
        OpenInventory();
        yield return SessionReturn.Return();
        yield return Frames(25);
        Check(!SessionReturn.Busy && !MainMenuUI.Instance.IsOpen() && RunId == recoveryId, "Failed return recovers the original run identity");
        Check(payload == 77 && Player.Instance.worldInventory.inventory.gold == 4321, "Failed return recovers unsaved native and mod state");
        CheckNpcRoster("Recovery after failed return");
        Check(PersistentFixtureNpc.relationshipLevel == 63, "Failed return restores unsaved custom NPC state");
        Check(Directory.GetFiles(Path.Combine(Application.persistentDataPath, "Saves"), "*.savmeta").Length == 1, "Temporary startup and recovery snapshots never enter the save list");
    }

    private static void CaptureUi(string name)
    {
        // A hidden Windows swapchain produces black ScreenCapture images. Render the
        // actual cameras/canvases into a texture without displaying a desktop window.
        Camera camera = Camera.main;
        var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>().Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        var previous = canvases.Select(c => (c.worldCamera, c.planeDistance)).ToArray();
        var target = new RenderTexture(Screen.width, Screen.height, 24);
        var uiObject = new GameObject("Test UI Capture Camera");
        var uiCamera = uiObject.AddComponent<Camera>();
        uiCamera.enabled = false;
        uiCamera.orthographic = true;
        uiCamera.orthographicSize = 5;
        uiCamera.nearClipPlane = 0.01f;
        uiCamera.farClipPlane = 100;
        uiCamera.clearFlags = CameraClearFlags.Depth;
        uiCamera.cullingMask = 1 << 31;
        uiCamera.targetTexture = target;
        var layers = new Dictionary<GameObject, int>();
        foreach (Canvas canvas in canvases)
            foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true))
                if (!layers.ContainsKey(child.gameObject)) layers.Add(child.gameObject, child.gameObject.layer);
        var pixels = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        RenderTexture oldActive = RenderTexture.active;
        RenderTexture oldTarget = camera.targetTexture;
        int oldMask = camera.cullingMask;
        try
        {
            camera.targetTexture = target;
            camera.cullingMask &= ~(1 << 31);
            foreach (GameObject item in layers.Keys) item.layer = 31;
            for (int i = 0; i < canvases.Length; i++)
            {
                canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                canvases[i].worldCamera = uiCamera;
                canvases[i].planeDistance = 1;
            }
            Canvas.ForceUpdateCanvases();
            camera.Render();
            uiCamera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(Path.Combine(root, name + ".png"), pixels.EncodeToPNG());
        }
        finally
        {
            for (int i = 0; i < canvases.Length; i++)
            {
                canvases[i].renderMode = RenderMode.ScreenSpaceOverlay;
                canvases[i].worldCamera = previous[i].worldCamera;
                canvases[i].planeDistance = previous[i].planeDistance;
            }
            camera.targetTexture = oldTarget;
            camera.cullingMask = oldMask;
            foreach (var pair in layers) pair.Key.layer = pair.Value;
            RenderTexture.active = oldActive;
            UnityEngine.Object.Destroy(uiObject);
            UnityEngine.Object.Destroy(pixels);
            target.Release();
            UnityEngine.Object.Destroy(target);
            Canvas.ForceUpdateCanvases();
        }
    }
}
#endif
