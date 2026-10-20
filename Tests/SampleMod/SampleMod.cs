using CoreMod;
using GlobalNamespace;
using UnityEngine;

namespace SampleMod;

[Mod("com.example.testmod", "1.0.0")]
public static class TestMod
{
    [Config(Description = "Enable the mod")]
    public static bool Enabled { get; set; } = true;

    [Config(Description = "Button text to display")]
    public static string ButtonText { get; set; } = "Skill Issue";

    [Config(Description = "Suffix to add in prefix")]
    public static string PrefixSuffix { get; set; } = " [pre]";

    [Config(Description = "Suffix to add in postfix")]
    public static string PostfixSuffix { get; set; } = " [post]";

    [Config(Description = "Optional status text for level version label")]
    public static string VersionStatusText { get; set; } = "Mod Active";

    [MenuButton("SampleMod", "Open the SampleMod menu")]
    public static void OpenSampleMenu() { }

    [GameplaySetupTab("SampleMod", MenuType = BsmlMenuType.All)]
    public static void OnGameplaySetupTabActivate(GameObject root, bool firstActivation)
    {
        if (!firstActivation)
            return;

        BSMLLite.CreateText(root.transform, "SampleMod", 4.0f, BSMLLite.Vector2(0.0f, 0.0f), BSMLLite.Vector2(60.0f, 8.0f));
    }

    [Patch(typeof(StandardLevelDetailViewController), nameof(StandardLevelDetailViewController.DidActivate), PatchKind.Before)]
    public static bool OnLevelScreenActivatePrefix(StandardLevelDetailViewController self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        if (Enabled)
        {
            var detailView = self._standardLevelDetailView;
            if (detailView != null)
            {
                if (detailView._buttonsWrapper != null)
                    detailView._buttonsWrapper.SetActive(true);

                if (detailView._actionButtonText != null)
                    detailView._actionButtonText.text = (ButtonText + PrefixSuffix);

                if (detailView._beatmapLevelVersionText != null)
                    detailView._beatmapLevelVersionText.text = VersionStatusText;

                if (detailView._actionButton != null)
                    detailView._actionButton.interactable = true;
            }
        }

        return true;
    }

    [Patch(typeof(StandardLevelDetailViewController), nameof(StandardLevelDetailViewController.DidActivate), PatchKind.After)]
    public static void OnLevelScreenActivatePostfix(StandardLevelDetailViewController self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        if (!Enabled)
            return;

        var detailView = self._standardLevelDetailView;
        if (detailView == null)
            return;

        if (detailView._actionButtonText != null)
            detailView._actionButtonText.text = (ButtonText + PostfixSuffix);

        if (detailView._practiceButton != null)
            detailView._practiceButton.interactable = true;
    }
}
