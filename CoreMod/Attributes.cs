namespace CoreMod;

public enum HookPhase
{
    Full = 0,
    Prefix = 1,
    Postfix = 2,
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class HookAttribute : Attribute
{
    public string? MethodName { get; set; }
    public Type? TargetType { get; set; }
    public string? ClassName { get; set; }
    public bool IsConstructor { get; set; }
    public HookPhase Phase { get; set; } = HookPhase.Full;

    public HookAttribute() { }

    public HookAttribute(string methodName)
    {
        MethodName = methodName;
    }

    public HookAttribute(Type targetType, string methodName)
    {
        TargetType = targetType;
        MethodName = methodName;
    }

    public HookAttribute(Type targetType)
    {
        TargetType = targetType;
    }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class ModAttribute : Attribute
{
    public string Id { get; }
    public string Version { get; }

    public ModAttribute(string id, string version)
    {
        Id = id;
        Version = version;
    }
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ConfigAttribute : Attribute
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public object? DefaultValue { get; set; }
}

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
