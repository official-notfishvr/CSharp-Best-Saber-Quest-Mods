using CoreMod;
using GlobalNamespace;

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

    [Hook(typeof(StandardLevelDetailViewController), nameof(StandardLevelDetailViewController.DidActivate), Phase = HookPhase.Prefix)]
    public static void OnLevelScreenActivatePrefix(StandardLevelDetailViewController self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        if (!Enabled)
            return;

        var detailView = self._standardLevelDetailView;
        if (detailView == null)
            return;

        if (detailView._buttonsWrapper != null)
            detailView._buttonsWrapper.SetActive(true);

        if (detailView._actionButtonText != null)
            detailView._actionButtonText.set_Text(ButtonText + PrefixSuffix);

        if (detailView._beatmapLevelVersionText != null)
            detailView._beatmapLevelVersionText.set_Text(VersionStatusText);

        if (detailView._actionButton != null)
            detailView._actionButton.m_Interactable = true;
    }

    [Hook(typeof(StandardLevelDetailViewController), nameof(StandardLevelDetailViewController.DidActivate), Phase = HookPhase.Postfix)]
    public static void OnLevelScreenActivatePostfix(StandardLevelDetailViewController self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        if (!Enabled)
            return;

        var detailView = self._standardLevelDetailView;
        if (detailView == null)
            return;

        if (detailView._actionButtonText != null)
            detailView._actionButtonText.set_Text(ButtonText + PostfixSuffix);

        if (detailView._practiceButton != null)
            detailView._practiceButton.m_Interactable = true;
    }
}
