using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Silverpine.QuitToMainMenu;

internal static class MenuPresentation
{
    private static GameObject? portraitOwner;
    private static readonly List<CustomContentDefinition_PlayerCharacter> claimed = new();

    internal static void Open()
    {
        MainMenuUI menu = MainMenuUI.Instance;
        menu.StopAllCoroutines();
        menu.transform.parent.gameObject.SetActive(true);
        menu.otherUI.gameObject.SetActive(false);
        SetGroup(menu.GetComponent<CanvasGroup>(), true);
        SetGroup(menu.transform.Find("StartMenu").GetComponent<CanvasGroup>(), true);
        SetGroup(menu.transform.Find("CharacterCreation").GetComponent<CanvasGroup>(), false);
        RestorePortraits(menu);
        menu.characterSelectionUI.SetIndex(0);
        Native.Field<Toggle>(menu, "disableNeedsToggle").isOn = false;
        menu.disableNeeds = false;
        RefreshContinue(menu);
        SaveUI.Instance.StartLoadOnlyMode();
        // Close() will release exactly this block when starting/loading a run.
        Native.Set(Player.Instance, "inputBlockCount", 1);
        ActionQueue.Instance.processTurnRequested = false;
        menu.StartCoroutine((IEnumerator)Native.Call(menu, "CameraPanCoroutine", Camera.main)!);
        Compatibility.RefreshCharacterList();
    }

    private static void SetGroup(CanvasGroup group, bool visible)
    {
        group.alpha = visible ? 1 : 0;
        group.blocksRaycasts = visible;
        // Native New Game/Back only toggle alpha and raycasts, not interactable.
        group.interactable = true;
    }

    private static void RestorePortraits(MainMenuUI menu)
    {
        ReleasePortraits();
        if (portraitOwner == null)
        {
            portraitOwner = new GameObject("QuitToMainMenu Portrait Owner");
            portraitOwner.transform.SetParent(menu.transform, false);
        }
        var unload = (List<CharacterField>)AccessTools.Field(typeof(CharacterField), "toUnload").GetValue(null);
        foreach (CharacterField field in menu.characterSelectionUI.GetComponentsInChildren<CharacterField>(true))
        {
            Sprite? portrait;
            if (field.customCharacter)
            {
                if (!CustomContentDefinition_PlayerCharacter.loaded.TryGetValue(field.defaultCharacterName, out var definition)) continue;
                definition.ClaimLargeAssets(portraitOwner);
                claimed.Add(definition);
                portrait = definition.assets.dialogSprite;
                // Same image chosen by the game's CustomContentDefinition_PlayerCharacter.Load.
                field.GetComponentsInChildren<Image>(true)[1].sprite = portrait;
            }
            else
            {
                portrait = Resources.Load<Sprite>("Sprites/Player/sprite_player_" + field.defaultCharacterName.ToLowerInvariant() + "_big");
                field.transform.Find("CharacterSprite").GetComponent<Image>().sprite = portrait;
                Native.Set(field, "loadedSprite", portrait);
                if (!unload.Contains(field)) unload.Add(field);
            }
        }
    }

    internal static void ReleasePortraits()
    {
        if (portraitOwner != null)
            foreach (var definition in claimed) definition.ReleaseLargeAssets(portraitOwner);
        claimed.Clear();
    }

    private static void RefreshContinue(MainMenuUI menu)
    {
        Button button = Native.Field<Button>(menu, "continueButton");
        bool any = SaveUI.Instance.AnySavesExist();
        button.interactable = any;
        var preview = button.GetComponent<ContinuePreviewImageHandler>();
        if (preview == null) return;
        var target = Native.Field<CanvasGroupFader>(preview, "target");
        target.FadeOut();
        var image = target.transform.GetChild(0).GetComponent<Image>();
        if (image.sprite != null)
        {
            Object.Destroy(image.sprite.texture);
            Object.Destroy(image.sprite);
        }
        image.sprite = any ? SaveUI.Instance.GetContinueSaveDescriptionImageSprite() : null;
        target.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = any ? SaveUI.Instance.GetContinueSaveDescription() : "";
        preview.enabled = any;
    }
}
