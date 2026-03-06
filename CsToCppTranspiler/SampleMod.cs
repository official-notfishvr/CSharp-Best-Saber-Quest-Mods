using CsToCppTranspiler;

namespace SampleMod;

[Mod("com.example.testmod", "1.0.0")]
public static class TestMod
{
    [Config(Description = "Enable the mod")]
    public static bool Enabled { get; set; } = true;

    [Config(Description = "Button text to display")]
    public static string ButtonText { get; set; } = "Skill Issue";

    [Hook("DidActivate", ClassName = "StandardLevelDetailViewController")]
    public static void OnLevelScreenActivate(
        StandardLevelDetailViewController self,
        bool firstActivation,
        bool addedToHierarchy,
        bool screenSystemEnabling)
    {
        OnLevelScreenActivate(self, firstActivation, addedToHierarchy, screenSystemEnabling);

        if (Enabled)
        {
            var detailView = self._standardLevelDetailView;
            var actionButton = detailView.actionButton;
            var gameObject = actionButton.get_GameObject();
            var buttonText = gameObject.GetComponentInChildren<CurvedTextMeshPro>();
            buttonText.set_Text(ButtonText);
        }
    }
}

public class StandardLevelDetailViewController
{
    public StandardLevelDetailView _standardLevelDetailView;
}

public class StandardLevelDetailView
{
    public Button actionButton;
}

public class Button
{
    public GameObject get_GameObject() => default;
}

public class GameObject
{
    public T GetComponentInChildren<T>() => default;
}

public class CurvedTextMeshPro
{
    public void set_Text(string text) { }
    public string get_Text() => default;
}
