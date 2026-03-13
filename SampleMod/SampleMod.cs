using CoreMod;
using GlobalNamespace;
using HMUI;

namespace SampleMod;

[Mod("com.example.testmod", "1.0.0")]
public static class TestMod
{
    [Config(Description = "Enable the mod")]
    public static bool Enabled { get; set; } = true;

    [Config(Description = "Button text to display")]
    public static string ButtonText { get; set; } = "Skill Issue";

    [Hook(typeof(StandardLevelDetailViewController), nameof(StandardLevelDetailViewController.DidActivate))]
    public static void OnLevelScreenActivate(StandardLevelDetailViewController self, bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        OnLevelScreenActivate(self, firstActivation, addedToHierarchy, screenSystemEnabling);

        if (Enabled)
        {
            var detailView = self._standardLevelDetailView;
            var actionButton = detailView._actionButton;
            var gameObject = actionButton.get_GameObject();
            var buttonText = gameObject.GetComponentInChildren<CurvedTextMeshPro>();
            buttonText.set_Text(ButtonText);
        }
    }
}
