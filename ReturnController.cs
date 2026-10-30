using System;
using Silverpine.ModdingTools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Silverpine.QuitToMainMenu;

internal sealed class ReturnController : MonoBehaviour
{
    private InventoryUI inventory = null!;
    internal Button ReturnButton { get; private set; } = null!;
    private ModOverlay? confirmation;
    private Button? confirmReturnButton;
    private RectTransform statusCanvas = null!;
    private RectTransform[] statusBars = null!;
    private readonly Vector3[] corners = new Vector3[4];

    internal void Initialize(InventoryUI owner)
    {
        inventory = owner;
        var status = FindObjectOfType<MiniResourcesUI>(true);
        statusCanvas = (RectTransform)status.GetComponentInParent<Canvas>().rootCanvas.transform;
        string[] barFields = { "hpBar", "sanityBar", "hungerBar", "thirstBar", "restBar", "energyBar" };
        statusBars = Array.ConvertAll(barFields, name => (RectTransform)Native.Field<Slider>(status, name).transform);
        ReturnButton = ModUi.CloneButton(ModUi.GetInventoryButtonTemplate(owner), statusCanvas,
            "Quit to Main Menu", Confirm, 42);
        ReturnButton.name = "Quit to Main Menu";
        ReturnButton.GetComponent<LayoutElement>().ignoreLayout = true;
        var rect = (RectTransform)ReturnButton.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.sizeDelta = new Vector2(240, 42);
        // Inherit the HUD's CanvasScaler, while drawing above the in-game menus.
        var canvas = ReturnButton.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 400;
        ReturnButton.gameObject.AddComponent<GraphicRaycaster>();
        var label = ReturnButton.GetComponentInChildren<TextMeshProUGUI>(true);
        label.enableAutoSizing = true;
        label.fontSizeMin = 14;
        label.fontSizeMax = 22;
        ReturnButton.transform.SetAsLastSibling();
        ReturnButton.gameObject.SetActive(false);
        Canvas.willRenderCanvases += PlaceBelowStatusBars;
#if RUNTIME_TESTS
        RuntimeProbe.StartOn(this);
#endif
    }

    private void Update()
    {
        bool menuOpen = IsMenuOpen;
        bool available = CanUseMenuButton;
        if (ReturnButton != null)
        {
            ReturnButton.gameObject.SetActive(menuOpen);
            ReturnButton.interactable = confirmation == null && available;
        }
        if (confirmReturnButton != null) confirmReturnButton.interactable = available;
        if (confirmation != null && (!menuOpen || Input.GetKeyDown(KeyCode.Escape)))
            Dismiss();
    }

    private static bool IsMenuOpen => MainMenuUI.Instance != null && !MainMenuUI.Instance.IsOpen() &&
        PauseMenuManager.Instance != null && PauseMenuManager.Instance.isActiveAndEnabled &&
        PauseMenuManager.Instance.open && !SessionReturn.Busy;

    private static bool CanUseMenuButton => IsMenuOpen && SessionReturn.CanReturn && MenuNavigationEnabled();

    private static bool MenuNavigationEnabled()
    {
        // The HUD button is outside the menu hierarchy, so inherit its interaction
        // locks explicitly. IsInteractable includes ancestor CanvasGroup locks;
        // Graphic.Raycast also respects blocksRaycasts and ignoreParentGroups.
        // Read the current array so tabs added by ModdingTools/mods are included.
        foreach (Button tab in Native.Field<Button[]>(PauseMenuManager.Instance, "buttons"))
        {
            if (tab == null || !tab.IsActive() || !tab.IsInteractable()) continue;
            Graphic graphic = tab.targetGraphic;
            if (graphic == null || !graphic.raycastTarget) continue;
            Canvas canvas = graphic.canvas;
            Camera? camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 center = RectTransformUtility.WorldToScreenPoint(camera,
                graphic.rectTransform.TransformPoint(graphic.rectTransform.rect.center));
            if (graphic.Raycast(center, camera)) return true;
        }
        return false;
    }

    private void PlaceBelowStatusBars()
    {
        if (ReturnButton == null || !ReturnButton.gameObject.activeInHierarchy) return;
        float right = float.NegativeInfinity;
        float bottom = float.PositiveInfinity;
        foreach (RectTransform bar in statusBars)
        {
            bar.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector3 point = statusCanvas.InverseTransformPoint(corner);
                right = Mathf.Max(right, point.x);
                bottom = Mathf.Min(bottom, point.y);
            }
        }
        var rect = (RectTransform)ReturnButton.transform;
        Rect bounds = statusCanvas.rect;
        // These are HUD canvas units, not fixed screen pixels. Keep the right edge
        // aligned with the bars and leave a small, consistently scaled gap below.
        right = Mathf.Clamp(right, bounds.xMin + rect.rect.width + 8, bounds.xMax - 8);
        float top = Mathf.Clamp(bottom - 10, bounds.yMin + rect.rect.height + 8, bounds.yMax - 8);
        rect.anchoredPosition = new Vector2(right - bounds.xMax, top - bounds.yMax);
    }

    private void Confirm()
    {
        if (!CanUseMenuButton || confirmation != null) return;
        confirmation = ModUi.CreateOverlay(inventory, "Quit to Main Menu Confirmation", new Vector2(620, 230), 700);
        var shade = confirmation.Root.AddComponent<Image>();
        shade.color = new Color(0, 0, 0, 0.65f);
        shade.raycastTarget = true;
        Button template = ModUi.GetInventoryButtonTemplate(inventory);
        var title = ModUi.CloneTitle(template, confirmation.Panel.transform,
            "Quit to the main menu?\nUnsaved progress will be lost.");
        title.alignment = TextAlignmentOptions.Center;
        title.enableWordWrapping = true;
        title.fontSize = 25;
        Place(title.rectTransform, new Vector2(0, 35), new Vector2(570, 100));
        Button cancel = ModUi.CloneButton(template, confirmation.Panel.transform, "Cancel", Dismiss);
        Place((RectTransform)cancel.transform, new Vector2(-145, -65), new Vector2(250, 48));
        Button quit = ModUi.CloneButton(template, confirmation.Panel.transform, "Quit to Main Menu", () =>
        {
            Dismiss();
            if (CanUseMenuButton) StartCoroutine(SessionReturn.Return());
        });
        confirmReturnButton = quit;
        Place((RectTransform)quit.transform, new Vector2(145, -65), new Vector2(250, 48));
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var layout = rect.GetComponent<LayoutElement>();
        if (layout != null) layout.ignoreLayout = true;
    }

    private void Dismiss()
    {
        confirmation?.Destroy();
        confirmation = null;
        confirmReturnButton = null;
    }
    private void OnDisable()
    {
        if (ReturnButton != null) ReturnButton.gameObject.SetActive(false);
        Dismiss();
    }
    private void OnDestroy()
    {
        Canvas.willRenderCanvases -= PlaceBelowStatusBars;
        Dismiss();
        if (ReturnButton != null) Destroy(ReturnButton.gameObject);
    }
    private void OnApplicationQuit() => SessionReturn.Shutdown();
}
