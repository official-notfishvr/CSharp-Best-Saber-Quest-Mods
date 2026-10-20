namespace CoreMod;

public enum BsmlMenuType
{
    None = 0,
    Solo = 1,
    Online = 2,
    Campaign = 4,
    Custom = 8,
    All = Solo | Online | Campaign | Custom,
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class MenuButtonAttribute : Attribute
{
    public string Text { get; }
    public string HoverHint { get; }

    public MenuButtonAttribute(string text, string hoverHint = "")
    {
        Text = text;
        HoverHint = hoverHint;
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class GameplaySetupTabAttribute : Attribute
{
    public string Name { get; }
    public BsmlMenuType MenuType { get; set; } = BsmlMenuType.All;

    public GameplaySetupTabAttribute(string name)
    {
        Name = name;
    }
}
